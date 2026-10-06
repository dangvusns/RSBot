using System.IO;
using System.Text.RegularExpressions;

namespace RSBot.Python.Components;

/// <summary>
///     Name, description etc. of a plugin file, read from its constants without running it:
///     <c>NAME = "..."</c>, <c>DESCRIPTION = "..."</c>, <c>AUTHOR = "..."</c>, <c>VERSION = "..."</c>.
/// </summary>
public class PythonPluginInfo
{
    public string FileName { get; private init; }
    public string FilePath { get; private init; }
    public string Name { get; private init; }
    public string Description { get; private init; }
    public string Author { get; private init; }
    public string Version { get; private init; }

    /// <summary>
    ///     The Python module name the plugin is loaded under.
    /// </summary>
    public string ModuleName => "rsbot_plugin_" + Regex.Replace(Path.GetFileNameWithoutExtension(FileName), @"\W", "_");

    public static PythonPluginInfo Read(string filePath)
    {
        var text = File.ReadAllText(filePath);

        string ReadConst(string key)
        {
            var match = Regex.Match(
                text,
                $@"^\s*{key}\s*=\s*[""'](?<v>[^""']*)[""']\s*(#.*)?$",
                RegexOptions.Multiline
            );

            return match.Success ? match.Groups["v"].Value.Trim() : null;
        }

        var fileName = Path.GetFileName(filePath);

        return new PythonPluginInfo
        {
            FileName = fileName,
            FilePath = filePath,
            Name = ReadConst("NAME") ?? Path.GetFileNameWithoutExtension(fileName),
            Description = ReadConst("DESCRIPTION") ?? string.Empty,
            Author = ReadConst("AUTHOR") ?? string.Empty,
            Version = ReadConst("VERSION") ?? string.Empty,
        };
    }
}
