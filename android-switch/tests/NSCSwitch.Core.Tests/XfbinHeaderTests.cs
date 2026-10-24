using System.Buffers.Binary;
using NSCSwitch.Core;
using Xunit;

namespace NSCSwitch.Core.Tests;

public class XfbinHeaderTests
{
    private static byte[] Example()
    {
        byte[] data = new byte[256];
        "NUCC"u8.CopyTo(data);
        static void Put(byte[] bytes, int offset, uint value) =>
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
        Put(data, 0x1C, 2);
        Put(data, 0x20, 16);
        Put(data, 0x24, 2);
        Put(data, 0x28, 12);
        Put(data, 0x2C, 3);
        Put(data, 0x30, 16);
        Put(data, 0x38, 8);
        Put(data, 0x3C, 3);
        return data;
    }

    [Fact]
    public void PlausibleHeaderIsAccepted()
    {
        var data = Example();
        Assert.True(XfbinHeaderReader.TryRead(data, data.Length, out var info, out var error), error);
        Assert.NotNull(info);
        Assert.Equal(2U, info.NuccEntryCount);
        Assert.Equal(1U, info.PathEntryCount);
        Assert.Equal(132, info.FirstChunkOffset);
    }

    [Fact]
    public void RejectsUnknownFormat()
    {
        var data = Example();
        data[0] = 0;
        Assert.False(XfbinHeaderReader.TryRead(data, data.Length, out _, out _));
    }

    [Fact]
    public void RejectsTruncatedHeader()
    {
        Assert.False(XfbinHeaderReader.TryRead(new byte[8], 8, out _, out _));
    }

    [Fact]
    public void RejectsOversizedMetadata()
    {
        var data = Example();
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0x28), uint.MaxValue);
        Assert.False(XfbinHeaderReader.TryRead(data, data.Length, out _, out _));
    }

    [Fact]
    public async Task NonSeekableStreamCanBeInspected()
    {
        byte[] data = Example();
        await using var source = new NonSeekableStream(new MemoryStream(data));
        var info = await XfbinStreamInspector.InspectAsync(source);
        Assert.Equal(data.Length, info.FileLength);
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
