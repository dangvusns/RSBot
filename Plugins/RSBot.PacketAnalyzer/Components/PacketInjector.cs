using System;
using System.Globalization;
using RSBot.Core;
using RSBot.Core.Network;

namespace RSBot.PacketAnalyzer.Components;

internal static class PacketInjector
{
    public static bool IsConnected =>
        Kernel.Proxy != null && (Kernel.Proxy.IsConnectedToAgentserver || Kernel.Proxy.IsConnectedToGatewayserver);

    /// <summary>
    ///     Parses hex bytes such as <c>01 0A ff</c> or <c>010AFF</c>.
    /// </summary>
    public static bool TryParseHex(string text, out byte[] bytes, out string error)
    {
        bytes = Array.Empty<byte>();
        error = null;

        var hex = (text ?? string.Empty)
            .Replace(" ", string.Empty)
            .Replace("\t", string.Empty)
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty)
            .Replace("-", string.Empty);

        if (hex.Length % 2 != 0)
        {
            error = "The data must contain an even number of hex digits.";
            return false;
        }

        var result = new byte[hex.Length / 2];
        for (var i = 0; i < result.Length; i++)
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result[i]))
            {
                error = $"'{hex.Substring(i * 2, 2)}' is not a valid hex byte.";
                return false;
            }

        bytes = result;
        return true;
    }

    public static void Inject(ushort opcode, byte[] data, PacketDestination destination, bool encrypted, bool massive)
    {
        var packet = new Packet(opcode, encrypted, massive, data);

        PacketHub.Recorder?.AddMarker(
            $"Injecting 0x{opcode:X4} to {(destination == PacketDestination.Server ? "server" : "client")} ({data.Length} bytes)"
        );
        Log.Notify($"[PacketAnalyzer] Injected packet 0x{opcode:X4} to the {destination.ToString().ToLowerInvariant()}");

        PacketManager.SendPacket(packet, destination);
    }
}
