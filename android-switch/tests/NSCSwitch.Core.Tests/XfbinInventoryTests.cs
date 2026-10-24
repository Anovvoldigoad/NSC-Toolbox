using System.Buffers.Binary;
using System.Text;
using NSCSwitch.Core;
using Xunit;

namespace NSCSwitch.Core.Tests;

public class XfbinInventoryTests
{
    private static byte[] Example()
    {
        byte[] nucc = Encoding.ASCII.GetBytes("nuccChunk\0nuccPage\0");
        byte[] paths = new byte[] { 0 }.Concat(Encoding.ASCII.GetBytes("data/patch/170/sample\0")).ToArray();
        byte[] names = new byte[] { 0 }.Concat(Encoding.ASCII.GetBytes("characode\0entry_001\0")).ToArray();
        byte[] data = new byte[512];
        "NUCC"u8.CopyTo(data);
        static void Put(byte[] bytes, int offset, uint value) =>
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), value);

        Put(data, 0x1C, 2);
        Put(data, 0x20, (uint)nucc.Length);
        Put(data, 0x24, 2); // includes sentinel
        Put(data, 0x28, (uint)paths.Length);
        Put(data, 0x2C, 2);
        Put(data, 0x30, (uint)names.Length);
        Put(data, 0x38, 8);
        Put(data, 0x3C, 3);

        nucc.CopyTo(data.AsSpan(0x44));
        paths.CopyTo(data.AsSpan(0x44 + nucc.Length));
        names.CopyTo(data.AsSpan(0x44 + nucc.Length + paths.Length));
        return data;
    }

    [Fact]
    public async Task ReadsAllThreeStringTables()
    {
        using var stream = new MemoryStream(Example());
        var inventory = await XfbinInventoryReader.InspectAsync(stream);
        Assert.Equal(new[] { "nuccChunk", "nuccPage" }, inventory.NuccPacks);
        Assert.Equal(new[] { "data/patch/170/sample" }, inventory.Paths);
        Assert.Equal(new[] { "characode", "entry_001" }, inventory.Names);
        Assert.Empty(inventory.Warnings);
    }

    [Fact]
    public async Task ReadsFromAndroidLikeNonSeekableStream()
    {
        using var stream = new NonSeekable(new MemoryStream(Example()));
        var inventory = await XfbinInventoryReader.InspectAsync(stream);
        Assert.Equal(512L, inventory.Header.FileLength);
        Assert.Equal(2, inventory.Names.Count);
    }

    [Fact]
    public async Task RejectsMissingStringTermination()
    {
        var bytes = Example();
        uint nuccLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0x20, 4));
        bytes.AsSpan(0x44, (int)nuccLength).Fill((byte)'A');
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => XfbinInventoryReader.InspectAsync(stream));
    }

    [Fact]
    public async Task RejectsOversizeMetadataWithoutAllocating()
    {
        var bytes = Example();
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x20, 4), uint.MaxValue);
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => XfbinInventoryReader.InspectAsync(stream));
    }

    [Fact]
    public async Task RejectsTruncatedMetadata()
    {
        var bytes = Example();
        using var stream = new MemoryStream(bytes[..75]);
        await Assert.ThrowsAsync<InvalidDataException>(() => XfbinInventoryReader.InspectAsync(stream));
    }

    private sealed class NonSeekable(Stream source) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            source.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
    }
}
