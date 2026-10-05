using System.Collections.Generic;
using System.Linq;
using RSBot.Core;
using RSBot.Core.Network;

namespace RSBot.PacketAnalyzer.Components;

/// <summary>
///     Include / exclude opcode filter with direction toggles. Read from the network thread, changed from the UI thread,
///     so the opcode set is replaced instead of mutated.
/// </summary>
internal sealed class PacketFilter
{
    /// <summary>
    ///     Opcodes that flood the log while training: ping, movement, position, hp/mp, state and speed updates.
    /// </summary>
    public static readonly ushort[] NoisyOpcodes = { 0x2002, 0xB021, 0xB023, 0x3057, 0x30BF, 0x30D0 };

    private readonly string _key;
    private volatile HashSet<ushort> _opcodes = new();

    public PacketFilter(string scope)
    {
        _key = $"RSBot.PacketAnalyzer.{scope}";
    }

    public volatile bool IncludeOnly;
    public volatile bool ShowClient = true;
    public volatile bool ShowServer = true;
    public volatile bool ShowBot = true;

    public IEnumerable<ushort> Opcodes => _opcodes.OrderBy(o => o);

    public bool Matches(PacketCapture capture)
    {
        if (capture.Origin == PacketOrigin.BotInjected)
        {
            if (!ShowBot)
                return false;
        }
        else if (capture.Destination == PacketDestination.Server ? !ShowClient : !ShowServer)
        {
            return false;
        }

        var contains = _opcodes.Contains(capture.Opcode);

        return IncludeOnly ? contains : !contains;
    }

    public void Add(IEnumerable<ushort> opcodes)
    {
        _opcodes = new HashSet<ushort>(_opcodes.Concat(opcodes));
        Save();
    }

    public void Remove(ushort opcode)
    {
        var opcodes = new HashSet<ushort>(_opcodes);
        opcodes.Remove(opcode);

        _opcodes = opcodes;
        Save();
    }

    public void Clear()
    {
        _opcodes = new HashSet<ushort>();
        Save();
    }

    public void Load(bool excludeNoisyByDefault)
    {
        var opcodes = new HashSet<ushort>();

        if (GlobalConfig.Exists($"{_key}.Opcodes"))
        {
            foreach (var text in GlobalConfig.GetArray<string>($"{_key}.Opcodes"))
                if (OpcodeNames.TryParseOpcode(text, out var opcode))
                    opcodes.Add(opcode);
        }
        else if (excludeNoisyByDefault)
        {
            opcodes.UnionWith(NoisyOpcodes);
        }

        _opcodes = opcodes;
        IncludeOnly = GlobalConfig.Get($"{_key}.IncludeOnly", false);
        ShowClient = GlobalConfig.Get($"{_key}.ShowClient", true);
        ShowServer = GlobalConfig.Get($"{_key}.ShowServer", true);
        ShowBot = GlobalConfig.Get($"{_key}.ShowBot", true);
    }

    public void Save()
    {
        GlobalConfig.SetArray($"{_key}.Opcodes", _opcodes.OrderBy(o => o).Select(o => o.ToString("X4")));
        GlobalConfig.Set($"{_key}.IncludeOnly", IncludeOnly);
        GlobalConfig.Set($"{_key}.ShowClient", ShowClient);
        GlobalConfig.Set($"{_key}.ShowServer", ShowServer);
        GlobalConfig.Set($"{_key}.ShowBot", ShowBot);
    }
}
