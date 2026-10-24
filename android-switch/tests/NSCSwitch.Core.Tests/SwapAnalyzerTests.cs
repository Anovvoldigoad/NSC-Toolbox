using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSCSwitch.Core;
using Xunit;

namespace NSCSwitch.Core.Tests;

public class SwapAnalyzerTests
{
    private static byte[] Fixture(string pack, string path, string name)
    {
        byte[] packBytes = Encoding.UTF8.GetBytes(pack + "\0");
        byte[] pathBytes = new byte[] { 0 }.Concat(Encoding.UTF8.GetBytes(path + "\0")).ToArray();
        byte[] nameBytes = new byte[] { 0 }.Concat(Encoding.UTF8.GetBytes(name + "\0")).ToArray();
        byte[] bytes = new byte[512];
        "NUCC"u8.CopyTo(bytes);
        static void Put(byte[] dst, int index, uint v) =>
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(dst.AsSpan(index, 4), v);
        Put(bytes, 0x1C, 1);
        Put(bytes, 0x20, (uint)packBytes.Length);
        Put(bytes, 0x24, 2);
        Put(bytes, 0x28, (uint)pathBytes.Length);
        Put(bytes, 0x2C, 1);
        Put(bytes, 0x30, (uint)nameBytes.Length);
        packBytes.CopyTo(bytes.AsSpan(0x44));
        pathBytes.CopyTo(bytes.AsSpan(0x44 + packBytes.Length));
        nameBytes.CopyTo(bytes.AsSpan(0x44 + packBytes.Length + pathBytes.Length));
        return bytes;
    }

    private static Func<Task<Stream>> Open(byte[] src) =>
        () => Task.FromResult<Stream>(new MemoryStream(src, writable: false));

    [Fact]
    public async Task ReportsDifferentResourceTables()
    {
        byte[] first = Fixture("nuccChunkModel", "char/abc", "abc");
        byte[] second = Fixture("nuccChunkAnm", "char/xyz", "xyz");
        var analysis = await XfbinSwapAnalyzer.AnalyzeAsync(
            Open(first), "abc.xfbin", Open(second), "xyz.xfbin");
        Assert.Equal(0, analysis.NuccPacks.SharedDistinctCount);
        Assert.Equal(new[] { "char/abc" }, analysis.Paths.FirstOnlyPreview);
        Assert.Equal(new[] { "char/xyz" }, analysis.Paths.SecondOnlyPreview);
        Assert.Contains(analysis.Warnings, x => x.Contains("path lists differ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IncludesProvenanceHashesInSwapManifest()
    {
        byte[] first = Fixture("nuccChunkModel", "char/abc", "abc");
        byte[] second = Fixture("nuccChunkAnm", "char/xyz", "xyz");
        using var output = new MemoryStream();
        await XfbinSwapArchive.CreateAsync(Open(first), "abc.xfbin",
            Open(second), "xyz.xfbin", output);
        output.Position = 0;
        using var zip = new ZipArchive(output, ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("swapped/abc.xfbin"));
        using var manifestReader = new StreamReader(zip.GetEntry("SWAP_MANIFEST.json")!.Open());
        using var document = JsonDocument.Parse(await manifestReader.ReadToEndAsync());
        var swap = document.RootElement.GetProperty("replacements")[0];
        Assert.Equal(Convert.ToHexString(SHA256.HashData(second)).ToLowerInvariant(),
            swap.GetProperty("sha256").GetString());
        Assert.Equal("swapped/abc.xfbin", swap.GetProperty("output").GetString());
        Assert.True(document.RootElement.TryGetProperty("analysis", out var analysis));
        Assert.Equal(1, analysis.GetProperty("Paths").GetProperty("FirstCount").GetInt32());
    }

    [Fact]
    public async Task RejectsInvalidNameBeforeWriting()
    {
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            XfbinSwapArchive.CreateAsync(
                Open(Fixture("a","path-a","a")), "../a.xfbin",
                Open(Fixture("b","path-b","b")), "b.xfbin", output));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task RejectsInvalidNUCCMetadataBeforeWriting()
    {
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            XfbinSwapArchive.CreateAsync(
                Open(new byte[512]), "a.xfbin",
                Open(Fixture("b","path-b","b")), "b.xfbin", output));
        Assert.Equal(0, output.Length);
    }
}
