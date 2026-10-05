using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RSBot.Core;
using RSBot.Core.Network;

namespace RSBot.PacketAnalyzer.Components;

/// <summary>
///     Resolves opcode names from the optional user file <c>Data/PacketAnalyzer/opcodes.txt</c>
///     (one <c>0xB021=Name</c> per line), the registered packet handlers / hooks and the wiki's packet index.
/// </summary>
internal static class OpcodeNames
{
    private const string Unknown = "?";

    private static Dictionary<(ushort, PacketDestination), string> _byDestination;
    private static Dictionary<ushort, string> _byOpcode;
    private static Dictionary<ushort, string> _userNames;
    private static int _builtFromCount = -1;

    public static string UserFilePath => Path.Combine(Kernel.BasePath, "Data", "PacketAnalyzer", "opcodes.txt");

    public static string Get(ushort opcode, PacketDestination destination)
    {
        // Plugin handlers may be registered after the first lookup, rebuild when their number changes.
        if (_byOpcode == null || (PacketManager.GetHandlers()?.Count ?? 0) != _builtFromCount)
            Reload();

        if (_userNames.TryGetValue(opcode, out var name))
            return name;

        if (_byDestination.TryGetValue((opcode, destination), out name))
            return name;

        // Requests have no handler in the bot; the wiki names most of them
        var wikiNames = destination == PacketDestination.Server ? WikiOpcodeNames.ToServer : WikiOpcodeNames.ToClient;
        if (wikiNames.TryGetValue(opcode, out name))
            return name;

        return _byOpcode.TryGetValue(opcode, out name) ? name : Unknown;
    }

    /// <summary>
    ///     Rebuilds the name tables. Handlers of plugins are registered after this plugin is initialized,
    ///     so it is built lazily on first use.
    /// </summary>
    public static void Reload()
    {
        var byDestination = new Dictionary<(ushort, PacketDestination), string>();
        var byOpcode = new Dictionary<ushort, string>();

        void Add(ushort opcode, PacketDestination destination, Type type)
        {
            var name = type.Name;

            byDestination.TryAdd((opcode, destination), name);
            byOpcode.TryAdd(opcode, name);
        }

        try
        {
            var handlers = PacketManager.GetHandlers();
            _builtFromCount = handlers?.Count ?? 0;
            if (handlers != null)
                foreach (var handler in handlers.ToArray())
                    if (handler != null)
                        Add(handler.Opcode, handler.Destination, handler.GetType());

            var hooks = PacketManager.GetHooks();
            if (hooks != null)
                foreach (var hook in hooks.ToArray())
                    if (hook != null)
                        Add(hook.Opcode, hook.Destination, hook.GetType());
        }
        catch (Exception e)
        {
            Log.Debug($"[PacketAnalyzer] Could not read packet handlers: {e.Message}");
        }

        _userNames = LoadUserNames();
        _byDestination = byDestination;
        _byOpcode = byOpcode;
    }

    private static Dictionary<ushort, string> LoadUserNames()
    {
        var result = new Dictionary<ushort, string>();

        try
        {
            if (!File.Exists(UserFilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(UserFilePath));
                File.WriteAllText(
                    UserFilePath,
                    "# Custom opcode names for the packet analyzer, one per line: 0xB021=EntityMovement"
                        + Environment.NewLine
                        + "# These override the names taken from the bot's packet handlers."
                        + Environment.NewLine
                );

                return result;
            }

            foreach (var rawLine in File.ReadAllLines(UserFilePath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                var separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                if (TryParseOpcode(line[..separator], out var opcode))
                    result[opcode] = line[(separator + 1)..].Trim();
            }
        }
        catch (Exception e)
        {
            Log.Debug($"[PacketAnalyzer] Could not read {UserFilePath}: {e.Message}");
        }

        return result;
    }

    /// <summary>
    ///     Parses an opcode written in hex, with or without the <c>0x</c> prefix.
    /// </summary>
    public static bool TryParseOpcode(string text, out ushort opcode)
    {
        text = text?.Trim() ?? string.Empty;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];

        return ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out opcode);
    }
}
