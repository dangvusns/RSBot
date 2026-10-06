using System;
using System.Drawing;
using RSBot.Core.Client;
using Xunit;

namespace RSBot.Core.Tests;

public class DDSImageTests
{
    [Fact]
    public void StrictDecode_ReadsSupportedTilePixels()
    {
        var bytes = CreateHeader("DXT1", 8);
        // One opaque green 4x4 DXT1 block, all pixels selecting endpoint zero.
        bytes[128] = 0xE0;
        bytes[129] = 0x07;

        using var bitmap = DDSImage.ToBitmap(bytes, throwOnUnsupported: true);

        Assert.Equal(4, bitmap.Width);
        Assert.Equal(4, bitmap.Height);
        Assert.Equal(Color.Lime.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.Lime.ToArgb(), bitmap.GetPixel(3, 3).ToArgb());
    }

    [Fact]
    public void StrictDecode_ReportsUnsupportedFormatInsteadOfBlankTile()
    {
        var bytes = CreateHeader("DX10", 0);

        Assert.Throws<NotSupportedException>(() => DDSImage.ToBitmap(bytes, throwOnUnsupported: true));
    }

    [Fact]
    public void DefaultDecode_PreservesUnsupportedFormatFallback()
    {
        using var bitmap = DDSImage.ToBitmap(CreateHeader("DX10", 0));

        Assert.Equal(256, bitmap.Width);
        Assert.Equal(256, bitmap.Height);
        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
    }

    private static byte[] CreateHeader(string fourCC, int payloadLength)
    {
        var bytes = new byte[128 + payloadLength];
        bytes[0] = (byte)'D';
        bytes[1] = (byte)'D';
        bytes[2] = (byte)'S';
        bytes[3] = (byte)' ';
        bytes[4] = 124;
        bytes[12] = 4;
        bytes[16] = 4;
        bytes[76] = 32;
        bytes[80] = 4;
        for (var i = 0; i < fourCC.Length; i++)
            bytes[84 + i] = (byte)fourCC[i];

        return bytes;
    }
}
