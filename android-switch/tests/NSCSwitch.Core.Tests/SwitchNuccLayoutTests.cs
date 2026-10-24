using System.Buffers.Binary;
using System.Text;
using NSCSwitch.Core;
using Xunit;

namespace NSCSwitch.Core.Tests;

/// <summary>
/// Synthetic header/string tables modelling the count conventions verified
/// on 15 real Switch XFBIN assets. Contains no original game asset bytes.
/// </summary>
public class SwitchNuccLayoutTests
{
    [Fact]
    public async Task CountsExcludePathAndNameSentinels()
    {
        byte[] nucc = Encoding.ASCII.GetBytes(
            "nuccChunkNull\0nuccChunkModel\0nuccChunkClump\0nuccChunkAnm\0");
        byte[] paths = new byte[] { 0 }.Concat(
            Encoding.ASCII.GetBytes("c/test/model/first.max\0c/test/anm/walk.max\0")).ToArray();
        byte[] names = new byte[] { 0 }.Concat(
            Encoding.ASCII.GetBytes("clump\0walk\0Page0\0")).ToArray();
        byte[] data = new byte[1024];
        "NUCC"u8.CopyTo(data);
        static void Put(byte[] bytes, int offset, uint count)
            => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), count);
        Put(data, 0x1C, 4);
        Put(data, 0x20, (uint)nucc.Length);
        Put(data, 0x24, 3); // one sentinel and two paths
        Put(data, 0x28, (uint)paths.Length);
        Put(data, 0x2C, 4); // one sentinel and three names
        Put(data, 0x30, (uint)names.Length);
        int off = 0x44;
        nucc.CopyTo(data.AsSpan(off)); off += nucc.Length;
        paths.CopyTo(data.AsSpan(off)); off += paths.Length;
        names.CopyTo(data.AsSpan(off));
        using var input = new MemoryStream(data);
        var parsed = await XfbinInventoryReader.InspectAsync(input);

        Assert.Equal(4, parsed.NuccPacks.Count);
        Assert.Equal(2, parsed.Paths.Count);
        Assert.Equal(3, parsed.Names.Count);
        Assert.Equal(2u, parsed.Header.PathEntryCount);
        Assert.Equal(3u, parsed.Header.NameEntryCount);
        Assert.Equal("walk", parsed.Names[1]);
        Assert.DoesNotContain(parsed.Names, string.IsNullOrEmpty);
        Assert.Empty(parsed.Warnings);
    }
}
