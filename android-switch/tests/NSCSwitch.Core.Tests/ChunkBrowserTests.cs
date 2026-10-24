using System.Buffers.Binary;
using System.Text;
using NSCSwitch.Core;
using Xunit;

namespace NSCSwitch.Core.Tests;

public class ChunkBrowserTests
{
    private static byte[] MakeFile(bool corrupt = false)
    {
        byte[] types = Encoding.UTF8.GetBytes("nuccChunkNull\0nuccChunkBinary\0nuccChunkPage\0");
        byte[] paths = new byte[] { 0 }.Concat(Encoding.UTF8.GetBytes("Z:/param/skill/attack.xml\0")).ToArray();
        byte[] names = new byte[] { 0 }.Concat(Encoding.UTF8.GetBytes("attack\0Page0\0")).ToArray();
        using var bytes = new MemoryStream();
        byte[] h = new byte[68];
        "NUCC"u8.CopyTo(h);
        static void Put(byte[] b, int offset, uint v) => BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(offset, 4), v);
        Put(h, 0x1C, 3); Put(h, 0x20, (uint)types.Length);
        Put(h, 0x24, 2); Put(h, 0x28, (uint)paths.Length);
        Put(h, 0x2C, 3); Put(h, 0x30, (uint)names.Length);
        Put(h, 0x34, 3); Put(h, 0x38, 36);
        Put(h, 0x3C, 3); Put(h, 0x40, 1);
        bytes.Write(h);bytes.Write(types);bytes.Write(paths);bytes.Write(names);
        while (bytes.Length % 4 != 0) bytes.WriteByte(0);
        byte[] maps = new byte[36];
        Put(maps, 0, 0);Put(maps, 4, 0);Put(maps, 8, 0);
        Put(maps, 12, 1);Put(maps, 16, 1);Put(maps, 20, 1);
        Put(maps, 24, 2);Put(maps, 28, 0);Put(maps, 32, 2);
        bytes.Write(maps);
        bytes.Write(new byte[8]); // one extra mapping (reserved)
        byte[] indices = new byte[12];
        Put(indices, 0, 0);Put(indices, 4, corrupt ? 99U : 1U);Put(indices, 8, 2);
        bytes.Write(indices);
        byte[] chunk = new byte[12];
        Put(chunk, 0, 4); Put(chunk, 4, 1);
        bytes.Write(chunk);bytes.Write(new byte[]{ 11, 22, 33, 44 });
        byte[] page = new byte[12];
        Put(page, 0, 8); Put(page, 4, 2);
        bytes.Write(page);
        byte[] footer = new byte[8];
        Put(footer, 0, 3);Put(footer, 4, 1);
        bytes.Write(footer);
        return bytes.ToArray();
    }

    [Fact]
    public async Task ResolvesChunkTypeNamePathAndExtraMapOffset()
    {
        byte[] input = MakeFile();
        using var file = new MemoryStream(input);
        var index = await XfbinChunkReader.InspectAsync(file);
        Assert.Equal(1, index.PageCount);
        Assert.Equal(2, index.Chunks.Count);
        var skill = index.Chunks[0];
        Assert.Equal("nuccChunkBinary", skill.ChunkType);
        Assert.Equal("attack", skill.ResourceName);
        Assert.Equal("Z:/param/skill/attack.xml", skill.ResourcePath);
        Assert.Equal(4u, skill.PayloadLength);
        Assert.Equal(input.Length, index.FileLength);
        Assert.Equal(0, (int)(index.FirstChunkOffset % 4));
        using var original = new MemoryStream(input);
        using var output = new MemoryStream();
        await XfbinChunkReader.CopyPayloadAsync(original, skill, output);
        Assert.Equal(new byte[]{ 11,22,33,44 }, output.ToArray());
    }

    [Fact]
    public async Task RejectsInvalidMappingIndices()
    {
        using var file = new MemoryStream(MakeFile(corrupt: true));
        await Assert.ThrowsAsync<InvalidDataException>(() => XfbinChunkReader.InspectAsync(file));
    }

    [Fact]
    public async Task DetectsTruncatedPayload()
    {
        byte[] file = MakeFile();
        using var input = new MemoryStream(file[..^2]);
        await Assert.ThrowsAsync<InvalidDataException>(() => XfbinChunkReader.InspectAsync(input));
    }

    [Fact]
    public async Task CopyRejectsChangedChunkHeader()
    {
        var bytes = MakeFile();
        using var indexed = new MemoryStream(bytes);
        var catalog = await XfbinChunkReader.InspectAsync(indexed);
        bytes[(int)catalog.Chunks[0].HeaderOffset + 3] = 7;
        using var source = new MemoryStream(bytes);
        using var dest = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            XfbinChunkReader.CopyPayloadAsync(source, catalog.Chunks[0], dest));
    }
}
