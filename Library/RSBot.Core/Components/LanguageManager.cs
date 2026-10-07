using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace RSBot.Core.Components;

using LangDict = Dictionary<string, string>;

public class LanguageManager
{
    private static readonly string _path = Path.Combine(Kernel.BasePath, "Data", "Languages");

    /// <summary>
    ///     Parsed language values by assembly name. Read from any thread while views are translated.
    /// </summary>
    private static readonly ConcurrentDictionary<string, LangDict> _values = new();

    /// <summary>
    ///     Parsed language files by path, re-parsed only when the file changed.
    /// </summary>
    private static readonly ConcurrentDictionary<string, (DateTime LastWrite, LangDict Values)> _parsedFiles = new();

    /// <summary>
    ///     Assembly names, cached because <see cref="Assembly.GetName()" /> allocates.
    /// </summary>
    private static readonly ConcurrentDictionary<Assembly, string> _assemblyNames = new();

    /// <summary>
    ///     Get all menu items
    /// </summary>
    /// <param name="menuItem">The toolstrip menu item</param>
    /// <returns></returns>
    private static List<ToolStripMenuItem> GetAllMenuItems(ToolStripMenuItem menuItem)
    {
        var collection = new List<ToolStripMenuItem> { menuItem };
        foreach (ToolStripMenuItem item in menuItem.DropDownItems)
            collection.AddRange(GetAllMenuItems(item));

        return collection;
    }

    /// <summary>
    ///     Parse the language file
    /// </summary>
    /// <param name="file">The language file</param>
    /// <returns>Parsed language strings</returns>
    public static LangDict ParseLanguageFile(string file)
    {
        var languages = new LangDict();
        var lines = File.ReadAllLines(file);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine) || !trimmedLine.Contains("="))
                continue;

            var parts = trimmedLine.Split(new[] { '=' }, 2);
            if (parts.Length < 2)
                continue;

            var key = parts[0].Trim();
            var value = parts[1].Trim().Trim('"');

            value = value
                .Replace("\\r\\n", "\r\n") // CRLF
                .Replace("\\n", "\n") // LF
                .Replace("\\r", "\r"); // CR

            if (!languages.ContainsKey(key))
            {
                languages[key] = value;
            }
        }

        return languages;
    }

    /// <summary>
    ///     Compare between controls ang languages strings if there have any missing complete and write to language file
    /// </summary>
    /// <param name="file">The language file path</param>
    /// <param name="controls">The controls</param>
    /// <param name="languages">the parsed languages list</param>
    private static void CheckMissings(string file, string header, Control main, LangDict languages)
    {
        var contents = new List<string>();

        foreach (Control control in main.Controls)
        {
            var headerEx = $"{header}.{control.Parent.GetType().Name}";
            if (control is ToolStrip toolStrip)
            {
                var menuItems = new List<ToolStripItem>();
                foreach (var menuItem in toolStrip.Items.OfType<ToolStripMenuItem>())
                    menuItems.AddRange(GetAllMenuItems(menuItem));

                foreach (var item in menuItems)
                {
                    if (string.IsNullOrEmpty(item.Name) || string.IsNullOrEmpty(item.Text))
                        continue;

                    var menuItemCheckName = $"{headerEx}.{item.Name}";
                    if (!languages.ContainsKey(menuItemCheckName))
                    {
                        contents.Add($"{menuItemCheckName}=\"{item.Text}\"");

                        languages[item.Name] = item.Text;
                    }
                }
            }

            if (control is ToolStrip)
                continue;

            CheckMissings(file, headerEx, control, languages);

            if (
                !(control is Label)
                && !(control is GroupBox)
                && !(control is ButtonBase)
                && !(control is TabControl)
                && !(control is TabPage)
                && !(control is ToolStrip)
            )
                continue;

            if (string.IsNullOrEmpty(control.Name) || string.IsNullOrEmpty(control.Text))
                continue;

            var checkName = $"{headerEx}.{control.Name}";
            if (!languages.ContainsKey(checkName))
            {
                contents.Add($"{checkName}=\"{control.Text}\"");

                languages[control.Name] = control.Text;
            }
        }

        if (contents.Count > 0)
            File.AppendAllLines(file, contents);
    }

    /// <summary>
    ///     Get language value
    /// </summary>
    /// <param name="key">The key</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string GetLang(string key)
    {
        // The calling plugin decides which language file is used
        return GetLangForAssembly(Assembly.GetCallingAssembly(), key);
    }

    /// <summary>
    ///     Get language value
    /// </summary>
    /// <param name="key">The key</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string GetLang(string key, params object[] args)
    {
        return string.Format(GetLangForAssembly(Assembly.GetCallingAssembly(), key), args);
    }

    /// <summary>
    ///     Get the language value of the given assembly's language file.
    ///     Used by Core helpers that translate on behalf of their caller, e.g. <see cref="Log.NotifyLang" />.
    /// </summary>
    /// <param name="assembly">The assembly whose language file is used</param>
    /// <param name="key">The key</param>
    /// <param name="args">The format arguments</param>
    public static string GetLangForAssembly(Assembly assembly, string key, params object[] args)
    {
        var parent = _assemblyNames.GetOrAdd(assembly, a => a.GetName().Name);

        if (!_values.TryGetValue(parent, out var values) || !values.TryGetValue(key, out var value))
            return string.Empty;

        return args == null || args.Length == 0 ? value : string.Format(value, args);
    }

    /// <summary>
    ///     Get language value
    /// </summary>
    /// <param name="key">The key</param>
    /// <param name="default">The default value that will be returned if the translation could not be found</param>
    public static string GetLangBySpecificKey(string parent, string key, string @default = "")
    {
        if (_values.TryGetValue(parent, out var values) && values.TryGetValue(key, out var value))
            return value;

        return @default;
    }

    /// <summary>
    ///     Translate the control
    /// </summary>
    /// <param name="view">The control view</param>
    /// <param name="file">The language file path</param>
    public static void Translate(Control view, string language = "en_US")
    {
        // Install once, even when a plugin does not have a language file.
        RSBot.Core.Extensions.TabControlExtensions.AutoSizeHeaders(view);

        var type = view.GetType();

        var controlName = type.FullName;
        var assembly = type.Assembly.GetName().Name;

        var path = Path.Combine(_path, assembly, language + ".rsl");
        var dir = Path.GetDirectoryName(path);

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        if (!File.Exists(path))
            return;

        // Every view of a plugin shares one file; parse it once instead of per view
        var lastWrite = File.GetLastWriteTimeUtc(path);
        if (!_parsedFiles.TryGetValue(path, out var parsed) || parsed.LastWrite != lastWrite)
        {
            parsed = (lastWrite, ParseLanguageFile(path));
            _parsedFiles[path] = parsed;
        }

        var values = parsed.Values;
        //CheckMissings(path, assembly, view, values);

        _values[assembly] = values;

        TranslateControls(values, view, assembly);
    }

    private static void TranslateControls(LangDict values, Control view, string header)
    {
        foreach (Control control in view.Controls)
        {
            var headerEx = $"{header}.{control.Parent.GetType().Name}";

            string translatedText;

            if (control is ToolStrip strip)
            {
                foreach (var toolStripItem in strip.Items.OfType<ToolStripMenuItem>())
                {
                    var subItems = GetAllMenuItems(toolStripItem);
                    foreach (var subMenuItem in subItems)
                        if (values.TryGetValue($"{headerEx}.{subMenuItem.Name}", out translatedText))
                            if (!string.IsNullOrWhiteSpace(translatedText))
                                subMenuItem.Text = translatedText;
                }

                continue;
            }

            if (values.TryGetValue($"{headerEx}.{control.Name}", out translatedText))
                if (!string.IsNullOrWhiteSpace(translatedText))
                    control.Text = translatedText;

            TranslateControls(values, control, headerEx);
        }
    }

    public static Dictionary<string, string> GetLanguages()
    {
        var filePath = Path.Combine(_path, "langs.rsl");
        if (!File.Exists(filePath))
        {
            MessageBox.Show($"Language list file is missing! \n {filePath}");
            Environment.Exit(0);
        }

        return File.ReadAllLines(filePath).ToDictionary(p => p.Split(':')[0], p => p.Split(':')[1]);
    }
}
