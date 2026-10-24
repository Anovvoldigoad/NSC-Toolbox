using System.IO.Compression;
using System.Text;
using NSCSwitch.Core;
using Xunit;

namespace NSCSwitch.Core.Tests;

public class SwapArchiveTests
{
    private static byte[] Make(string id)
    {
        byte[] bytes = new byte[256];
        "NUCC"u8.CopyTo(bytes);
        Encoding.UTF8.GetBytes(id).CopyTo(bytes.AsSpan(180));
        return bytes;
    }

    private static Func<Task<Stream>> Open(byte[] bytes) =>
        () => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));

    [Fact]
    public async Task CreatesSwappedPayloadWithManifest()
    {
        var a = Make("AAA");
        var b = Make("BBB");
        using var output = new MemoryStream();
        await XfbinSwapArchive.CreateAsync(Open(a), "a.xfbin", Open(b), "b.xfbin", output);
        output.Position = 0;
        using var zip = new ZipArchive(output, ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("SWAP_MANIFEST.json"));
        using var entry = zip.GetEntry("swapped/a.xfbin")!.Open();
        using var copied = new MemoryStream();
        await entry.CopyToAsync(copied);
        Assert.Equal(b, copied.ToArray());
    }

    [Fact]
    public async Task RejectsDuplicateFileNames()
    {
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            XfbinSwapArchive.CreateAsync(Open(Make("A")), "same.xfbin",
                Open(Make("B")), "SAME.xfbin", output));
    }

    [Fact]
    public async Task RejectsNonXfbinData()
    {
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            XfbinSwapArchive.CreateAsync(Open(new byte[256]), "a.xfbin",
                Open(Make("B")), "b.xfbin", output));
    }
}
