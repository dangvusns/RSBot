using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Python.Runtime;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Network;
using RSBot.Core.Objects.Party;

namespace RSBot.Python.Components;

/// <summary>
///     Starts the embedded Python, loads/unloads plugin files and forwards bot events to them.
/// </summary>
internal static class PythonPluginManager
{
    private const string EnabledPluginsKey = "RSBot.Python.EnabledPlugins";

    private static readonly ConcurrentDictionary<string, PythonPluginInfo> _loaded = new();
    private static readonly Dictionary<string, List<IPacketHook>> _hooks = new();
    private static readonly object _hookLock = new();

    private static PyObject _api;

    public static string PythonDirectory => Path.Combine(Kernel.BasePath, "Data", "Python");
    public static string PluginsDirectory => Path.Combine(PythonDirectory, "Plugins");
    public static string RuntimeDirectory => Path.Combine(PythonDirectory, "PyRuntime");

    public static bool IsInitialized { get; private set; }
    public static bool HasLoadedPlugins => !_loaded.IsEmpty;

    #region Runtime

    /// <summary>
    ///     Starts Python and creates the RSBot module. Runs on the Python worker thread.
    /// </summary>
    public static void Initialize()
    {
        if (IsInitialized)
            return;

        try
        {
            Directory.CreateDirectory(PluginsDirectory);
            RemoveOldApiCopy();

            var pythonDll = Directory.Exists(RuntimeDirectory)
                ? Directory
                    .GetFiles(RuntimeDirectory, "python3*.dll")
                    .FirstOrDefault(f => !Path.GetFileName(f).Equals("python3.dll", StringComparison.OrdinalIgnoreCase))
                : null;
            if (pythonDll == null)
            {
                PythonBridge.Log(
                    null,
                    $"Python was not found in {RuntimeDirectory}. Build the bot again (it downloads Python) to use Python plugins.",
                    1
                );
                return;
            }

            var zip = Directory.GetFiles(RuntimeDirectory, "python3*.zip").FirstOrDefault();

            Runtime.PythonDLL = pythonDll;
            PythonEngine.PythonHome = RuntimeDirectory;
            PythonEngine.PythonPath = string.Join(
                ";",
                new[] { RuntimeDirectory, zip, Path.Combine(RuntimeDirectory, "Lib"), PluginsDirectory }.Where(p =>
                    !string.IsNullOrEmpty(p)
                )
            );
            PythonEngine.Initialize();
            PythonEngine.BeginAllowThreads();

            using (Py.GIL())
            {
                // One step, no imports by name: the bridge is handed to the API module directly, and
                // "import RSBot" is answered by a finder (pythonnet also creates a namespace called RSBot)
                using var scope = Py.CreateScope();
                using (var source = new PyString(ReadApiSource()))
                    scope.Set("source", source);

                scope.Exec(
                    $@"
import clr, sys, types, importlib.abc, importlib.util
clr.AddReference('{typeof(PythonBridge).Assembly.GetName().Name}')
from RSBot.Python.Components import PythonBridge

module = types.ModuleType('RSBot')
module.__file__ = 'RSBot.py'
module._b = PythonBridge
exec(compile(source, 'RSBot.py', 'exec'), module.__dict__)

class _RSBotLoader(importlib.abc.Loader):
    def create_module(self, spec):
        return module
    def exec_module(self, mod):
        pass

class _RSBotFinder(importlib.abc.MetaPathFinder):
    def find_spec(self, fullname, path=None, target=None):
        if fullname == 'RSBot':
            return importlib.util.spec_from_loader('RSBot', _RSBotLoader())
        return None

sys.meta_path.insert(0, _RSBotFinder())
sys.modules['RSBot'] = module
"
                );

                _api = scope.Get("module");
            }

            WriteApiCopy();
            SubscribeEvents();
            ScriptManager.UnknownCommandHandler = HandleScriptCommand;

            IsInitialized = true;
            PythonBridge.Log(null, $"Python {PythonEngine.Version.Split(' ')[0]} is ready.", 0);
        }
        catch (Exception e)
        {
            var details = e is PythonException pythonException ? pythonException.Format() : e.ToString();
            PythonBridge.Log(null, $"Python could not be started: {e.Message}\n{details}", 2);
        }
    }

    private static string ReadApiSource()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames().First(n => n.EndsWith("RSBot.py"));

        using var stream = assembly.GetManifestResourceStream(name);
        using var reader = new StreamReader(stream!);

        return reader.ReadToEnd();
    }

    /// <summary>
    ///     Saves the API next to the plugins as RSBot.pyi so editors can autocomplete it (the bot uses its
    ///     built-in copy). It must not be a .py file: the plugins folder is on Python's import path, and a
    ///     file named RSBot.py would be imported instead of RSBot's .NET namespace.
    /// </summary>
    private static void WriteApiCopy()
    {
        try
        {
            File.WriteAllText(Path.Combine(PluginsDirectory, "RSBot.pyi"), ReadApiSource());
        }
        catch
        {
            // Another bot process may be writing it at the same time; it is only for editors
        }
    }

    /// <summary>
    ///     Removes the RSBot.py copy written by earlier versions, which shadows the .NET namespace.
    /// </summary>
    private static void RemoveOldApiCopy()
    {
        try
        {
            var oldCopy = Path.Combine(PluginsDirectory, "RSBot.py");
            if (File.Exists(oldCopy))
                File.Delete(oldCopy);
        }
        catch (Exception e)
        {
            PythonBridge.Log(null, $"Please delete {Path.Combine(PluginsDirectory, "RSBot.py")}: {e.Message}", 1);
        }
    }

    /// <summary>
    ///     Calls a function of the RSBot module. The GIL must be held.
    /// </summary>
    private static PyObject Invoke(string function, params object[] args)
    {
        var pyArgs = args.Select(ToPython).ToArray();
        try
        {
            return _api.InvokeMethod(function, pyArgs);
        }
        finally
        {
            foreach (var arg in pyArgs)
                arg.Dispose();
        }
    }

    private static PyObject ToPython(object value)
    {
        return value switch
        {
            string s => new PyString(s),
            int i => new PyInt(i),
            bool b => b.ToPython(),
            _ => throw new ArgumentException($"Unsupported argument type {value?.GetType().Name ?? "null"}"),
        };
    }

    /// <summary>
    ///     Runs a blocking bot call from Python without holding the GIL, so packet hooks and
    ///     script commands of other threads can run meanwhile (e.g. a select waiting for its reply).
    /// </summary>
    internal static T WithoutGil<T>(Func<T> action)
    {
        if (!IsInitialized)
            return action();

        var state = PythonEngine.BeginAllowThreads();
        try
        {
            return action();
        }
        finally
        {
            PythonEngine.EndAllowThreads(state);
        }
    }

    #endregion

    #region Plugins

    public static List<PythonPluginInfo> ScanPlugins()
    {
        Directory.CreateDirectory(PluginsDirectory);

        return Directory
            .GetFiles(PluginsDirectory, "*.py", SearchOption.TopDirectoryOnly)
            .Where(f =>
                f.EndsWith(".py", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(f).Equals("RSBot.py", StringComparison.OrdinalIgnoreCase)
            )
            .Select(PythonPluginInfo.Read)
            .OrderBy(p => p.Name)
            .ToList();
    }

    public static bool IsLoaded(string fileName)
    {
        return _loaded.Values.Any(p => p.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }

    public static string GetDisplayName(string pluginKey)
    {
        return !string.IsNullOrEmpty(pluginKey) && _loaded.TryGetValue(pluginKey, out var info) ? info.Name : null;
    }

    /// <summary>
    ///     Loads a plugin on the Python thread.
    /// </summary>
    public static void Load(PythonPluginInfo info, Action<bool> done = null)
    {
        PythonWorker.Post(() =>
        {
            var loaded = false;
            try
            {
                if (!IsInitialized || _loaded.ContainsKey(info.ModuleName))
                    return;

                // Registered first: the plugin's code calls back (GUI, register_packet) while it loads
                _loaded[info.ModuleName] = info;

                try
                {
                    using (Py.GIL())
                    using (var result = Invoke("_load", info.ModuleName, info.FilePath))
                        loaded = result.IsTrue();
                }
                catch (Exception e)
                {
                    PythonBridge.Log(info.ModuleName, $"Could not be loaded: {e.Message}", 2);
                }

                if (!loaded)
                {
                    Cleanup(info.ModuleName);
                    return;
                }

                PythonBridge.Log(info.ModuleName, "Loaded.", 0);
            }
            finally
            {
                done?.Invoke(loaded);
            }
        });
    }

    /// <summary>
    ///     Unloads a plugin on the Python thread.
    /// </summary>
    public static void Unload(string fileName, Action done = null)
    {
        PythonWorker.Post(() =>
        {
            try
            {
                var info = _loaded.Values.FirstOrDefault(p =>
                    p.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)
                );
                if (info == null)
                    return;

                try
                {
                    using (Py.GIL())
                        Invoke("_unload", info.ModuleName).Dispose();

                    PythonBridge.Log(info.ModuleName, "Unloaded.", 0);
                }
                catch (Exception e)
                {
                    PythonBridge.Log(info.ModuleName, $"Error while unloading: {e.Message}", 2);
                }
                finally
                {
                    Cleanup(info.ModuleName);
                }
            }
            finally
            {
                done?.Invoke();
            }
        });
    }

    private static void Cleanup(string pluginKey)
    {
        lock (_hookLock)
        {
            if (_hooks.Remove(pluginKey, out var hooks))
                foreach (var hook in hooks)
                    PacketManager.RemoveHook(hook);
        }

        PythonGui.RemovePage(pluginKey);
        _loaded.TryRemove(pluginKey, out _);
    }

    /// <summary>
    ///     Calls the function in every loaded plugin that has it. Must run on the Python thread.
    /// </summary>
    public static void CallAll(string function, params object[] args)
    {
        if (!IsInitialized || _loaded.IsEmpty)
            return;

        using (Py.GIL())
            Invoke("_call_all", function, JsonSerializer.Serialize(args)).Dispose();
    }

    /// <summary>
    ///     Queues a call of the function in every plugin, so the calling bot thread doesn't wait for Python.
    /// </summary>
    public static void PostAll(string function, params object[] args)
    {
        if (!IsInitialized || _loaded.IsEmpty)
            return;

        PythonWorker.Post(() => CallAll(function, args));
    }

    public static void PostGuiEvent(int controlId, object value)
    {
        if (!IsInitialized)
            return;

        PythonWorker.Post(() =>
        {
            using (Py.GIL())
                Invoke("_gui_event", controlId, JsonSerializer.Serialize(value)).Dispose();
        });
    }

    #endregion

    #region Enabled plugins per character

    /// <summary>
    ///     Runs the plugins this character has enabled: unloads the others (e.g. after switching character)
    ///     and loads the missing ones. Runs on the Python thread.
    /// </summary>
    public static void LoadEnabledPlugins()
    {
        if (!IsInitialized)
            return;

        var enabled = PlayerConfig.GetArray<string>(EnabledPluginsKey, '|');

        foreach (var info in _loaded.Values.Where(p => !enabled.Contains(p.FileName)).ToList())
            Unload(info.FileName);

        foreach (var info in ScanPlugins().Where(p => enabled.Contains(p.FileName) && !IsLoaded(p.FileName)))
            Load(info);

        PythonWorker.Post(Views.View.Instance.RefreshPlugins);
    }

    public static void SaveEnabledPlugins()
    {
        if (Game.Player == null)
            return;

        PlayerConfig.SetArray(EnabledPluginsKey, _loaded.Values.Select(p => p.FileName).ToArray(), "|");
        PlayerConfig.Save();
    }

    #endregion

    #region Packets

    public static void RegisterPacket(string pluginKey, ushort opcode, bool fromServer)
    {
        // Packets from the server are on their way to the client
        var destination = fromServer ? PacketDestination.Client : PacketDestination.Server;

        lock (_hookLock)
        {
            if (!_hooks.TryGetValue(pluginKey, out var hooks))
                _hooks[pluginKey] = hooks = new List<IPacketHook>();

            if (hooks.Any(h => h.Opcode == opcode && h.Destination == destination))
                return;

            var hook = new DelegatePacketHook(
                opcode,
                destination,
                packet => OnHookedPacket(pluginKey, fromServer, packet)
            );
            hooks.Add(hook);
            PacketManager.RegisterHook(hook);
        }
    }

    public static void UnregisterPacket(string pluginKey, ushort opcode, bool fromServer)
    {
        var destination = fromServer ? PacketDestination.Client : PacketDestination.Server;

        lock (_hookLock)
        {
            if (!_hooks.TryGetValue(pluginKey, out var hooks))
                return;

            foreach (var hook in hooks.Where(h => h.Opcode == opcode && h.Destination == destination).ToList())
            {
                hooks.Remove(hook);
                PacketManager.RemoveHook(hook);
            }
        }
    }

    /// <summary>
    ///     Runs on the network thread: the plugin decides right away whether the packet passes.
    /// </summary>
    private static Packet OnHookedPacket(string pluginKey, bool fromServer, Packet packet)
    {
        if (packet == null || !IsInitialized || !_loaded.ContainsKey(pluginKey))
            return packet;

        try
        {
            var data = Convert.ToHexString(packet.GetBytes());

            bool keep;
            using (Py.GIL())
            using (var result = Invoke("_packet", pluginKey, fromServer, (int)packet.Opcode, data))
                keep = result.IsTrue();

            return keep ? packet : null;
        }
        catch (Exception e)
        {
            PythonBridge.Log(pluginKey, $"Packet 0x{packet.Opcode:X4}: {e.Message}", 2);
            return packet;
        }
    }

    #endregion

    #region Script commands

    /// <summary>
    ///     A script line whose command is unknown to RSBot calls the plugin function of the same name.
    ///     Runs on the script thread and waits as long as the function asks.
    /// </summary>
    private static bool? HandleScriptCommand(string command, string[] arguments)
    {
        if (!IsInitialized || _loaded.IsEmpty || string.IsNullOrEmpty(command))
            return null;

        int result;
        using (Py.GIL())
        using (var value = Invoke("_script", command, JsonSerializer.Serialize(arguments ?? Array.Empty<string>())))
            result = value.As<int>();

        if (result == -1)
            return null;

        if (result == -2)
            return false;

        if (result > 0)
            Thread.Sleep(result);

        return true;
    }

    #endregion

    #region Events

    private static void SubscribeEvents()
    {
        EventManager.SubscribeEvent("OnEnterGame", () => PostAll("on_enter_game"));
        EventManager.SubscribeEvent("OnAgentServerDisconnected", () => PostAll("on_disconnect"));
        EventManager.SubscribeEvent("OnTeleportComplete", () => PostAll("on_teleported"));
        EventManager.SubscribeEvent("OnStartBot", () => PostAll("on_bot_started"));
        EventManager.SubscribeEvent("OnStopBot", () => PostAll("on_bot_stopped"));
        EventManager.SubscribeEvent("OnPlayerDied", () => PostAll("on_player_died"));
        EventManager.SubscribeEvent("OnKillEnemy", () => PostAll("on_kill"));
        EventManager.SubscribeEvent(
            "OnLevelUp",
            new Action<byte>(_ => PostAll("on_level_up", (int)(Game.Player?.Level ?? 0)))
        );
        EventManager.SubscribeEvent("OnPartyMemberJoin", new Action<PartyMember>(m => PostParty("join", m)));
        EventManager.SubscribeEvent("OnPartyMemberLeave", new Action<PartyMember>(m => PostParty("leave", m)));
        EventManager.SubscribeEvent("OnPartyMemberUpdate", new Action<PartyMember>(m => PostParty("update", m)));
        EventManager.SubscribeEvent("OnPartyDismiss", () => PostAll("on_party_changed", "dismiss", ""));
        EventManager.SubscribeEvent("OnLoadCharacter", () => PythonWorker.Post(LoadEnabledPlugins));
    }

    private static void PostParty(string change, PartyMember member)
    {
        PostAll("on_party_changed", change, member?.Name ?? "");
    }

    #endregion
}
