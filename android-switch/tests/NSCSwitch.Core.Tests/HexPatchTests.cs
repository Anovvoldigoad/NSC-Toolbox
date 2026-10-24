using NSCSwitch.Core;
using Xunit;

namespace NSCSwitch.Core.Tests;

public class HexPatchTests
{
    private static byte[] Source()
    {
        byte[] x = new byte[256];
        "NUCC"u8.CopyTo(x);
        x[120] = 0x01; x[121] = 0x02;
        return x;
    }

    [Fact]
    public async Task ChangesOnlyRequestedBytes()
    {
        var src = Source();
        using var input = new MemoryStream(src);
        using var output = new MemoryStream();
        await XfbinHexPatch.ApplyAsync(input, output, 120,
            new byte[]{ 1, 2 }, new byte[]{ 0xAA, 0xBB });
        var modified = output.ToArray();
        Assert.Equal(src.Length, modified.Length);
        Assert.Equal((byte)0xAA, modified[120]);
        Assert.Equal((byte)0xBB, modified[121]);
        Assert.Equal(src[..120], modified[..120]);
        Assert.Equal(src[122..], modified[122..]);
    }

    [Fact]
    public async Task RejectsStaleExpectedBytes()
    {
        using var input = new MemoryStream(Source());
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            XfbinHexPatch.ApplyAsync(input, output, 120,
                new byte[]{ 9, 9 }, new byte[]{ 3, 3 }));
    }

    [Fact]
    public void ParsesOffsetsAndBytePairs()
    {
        Assert.Equal(256, XfbinHexPatch.ParseOffset("0x100"));
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
            XfbinHexPatch.ParseHex("DE AD:BE-EF"));
        Assert.Throws<ArgumentException>(() => XfbinHexPatch.ParseHex("0"));
    }
}
