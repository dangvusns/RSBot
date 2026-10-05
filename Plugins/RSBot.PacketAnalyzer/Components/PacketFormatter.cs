using System.Text;
using RSBot.Core.Network;

namespace RSBot.PacketAnalyzer.Components;

/// <summary>
///     Formats captured packets the same way for the live view and the recorded log.
/// </summary>
internal static class PacketFormatter
{
    private const int BytesPerLine = 16;

    public static string Context(PacketCapture capture)
    {
        return capture.Context == PacketContext.Agent ? "A" : "G";
    }

    public static string Direction(PacketCapture capture)
    {
        return capture.Destination == PacketDestination.Server ? "C->S" : "S->C";
    }

    public static string Origin(PacketCapture capture)
    {
        return capture.Origin switch
        {
            PacketOrigin.BotInjected => "BOT",
            PacketOrigin.Replaced => "REPL",
            PacketOrigin.Dropped => "DROP",
            _ => "RECV",
        };
    }

    public static string Flags(PacketCapture capture)
    {
        return (capture.Encrypted ? "[E]" : string.Empty) + (capture.Massive ? "[M]" : string.Empty);
    }

    /// <summary>
    ///     e.g. <c>14:03:12.345 [A] S->C RECV 0x3013 CharacterDataResponse len=1234 [E]</c>
    /// </summary>
    public static string Header(PacketCapture capture)
    {
        var flags = Flags(capture);

        return $"{capture.Time:HH:mm:ss.fff} [{Context(capture)}] {Direction(capture)} {Origin(capture),-4} "
            + $"0x{capture.Opcode:X4} {OpcodeNames.Get(capture.Opcode, capture.Destination)} len={capture.Payload.Length}"
            + (flags.Length > 0 ? " " + flags : string.Empty);
    }

    /// <summary>
    ///     Appends a hex dump with a 4 digit hex offset, 16 bytes per line and an ascii column.
    /// </summary>
    public static void AppendHexDump(StringBuilder builder, byte[] data, string indent)
    {
        for (var offset = 0; offset < data.Length; offset += BytesPerLine)
        {
            builder.Append(indent).Append(offset.ToString("X4")).Append("  ");

            for (var i = 0; i < BytesPerLine; i++)
            {
                if (offset + i < data.Length)
                    builder.Append(data[offset + i].ToString("X2")).Append(' ');
                else
                    builder.Append("   ");

                if (i == 7)
                    builder.Append(' ');
            }

            builder.Append(" |");
            for (var i = 0; i < BytesPerLine && offset + i < data.Length; i++)
            {
                var b = data[offset + i];
                builder.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
            }

            builder.AppendLine("|");
        }
    }

    public static string HexString(byte[] data)
    {
        var builder = new StringBuilder(data.Length * 3);
        for (var i = 0; i < data.Length; i++)
        {
            if (i > 0)
                builder.Append(' ');

            builder.Append(data[i].ToString("X2"));
        }

        return builder.ToString();
    }

    public static string Format(PacketCapture capture, bool includeHex)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Header(capture));

        if (includeHex)
            AppendHexDump(builder, capture.Payload, "    ");

        return builder.ToString();
    }
}
