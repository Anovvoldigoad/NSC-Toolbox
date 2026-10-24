using System.Buffers.Binary;
using System.Text;

namespace NSCSwitch.Core;

/// <summary>
/// Inspect XFBIN chunk mappings and offsets without decoding game-specific payloads.
/// Supports nonseekable Android document streams and bounded metadata allocations.
/// </summary>
public sealed record XfbinChunkInfo(
    int Index, int Page, string ChunkType, string ResourcePath, string ResourceName,
    long HeaderOffset, long PayloadOffset, uint PayloadLength,
    uint PageLocalMapIndex, ushort Version, ushort VersionAttribute);

public sealed record XfbinChunkCatalog(
    int PageCount, long FileLength, long FirstChunkOffset,
    IReadOnlyList<XfbinChunkInfo> Chunks);

public static class XfbinChunkReader
{
    private const int HeaderSize = 0x44;
    private const ulong MaxMetadataBytes = 16UL * 1024UL * 1024UL;
    private const uint MaxChunkBytes = 256U * 1024U * 1024U;
    private const int MaxChunks = 50_000;

    public static async Task<XfbinChunkCatalog> InspectAsync(
        Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (stream.CanSeek) stream.Position = 0;
        byte[] header = new byte[HeaderSize];
        await ReadExactAsync(stream, header, ct);
        if (!header.AsSpan(0, 4).SequenceEqual("NUCC"u8))
            throw new InvalidDataException("File is not a NUCC/XFBIN container.");

        static uint U(ReadOnlySpan<byte> bytes, int at) =>
            BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(at, 4));

        uint typeCount = U(header, 0x1C), typeSize = U(header, 0x20);
        uint pathCount = U(header, 0x24), pathSize = U(header, 0x28);
        uint nameCount = U(header, 0x2C), nameSize = U(header, 0x30);
        uint mapCount = U(header, 0x34), mapSize = U(header, 0x38);
        uint indexCount = U(header, 0x3C), extraCount = U(header, 0x40);

        if (mapSize != (ulong)mapCount * 12UL)
            throw new InvalidDataException("Chunk-map table size and count disagree.");
        if (typeCount > 100_000 || pathCount > 100_001 ||
            nameCount > 100_001 || mapCount > 500_000 ||
            indexCount > 1_000_000 || extraCount > 500_000)
            throw new InvalidDataException("Chunk table count exceeds inspection limit.");

        ulong stringsEnd = HeaderSize + (ulong)typeSize + pathSize + nameSize;
        ulong alignedStrings = (stringsEnd + 3UL) & ~3UL;
        ulong mapsEnd = alignedStrings + mapSize + (ulong)extraCount * 8UL +
            (ulong)indexCount * 4UL;
        if (mapsEnd > MaxMetadataBytes)
            throw new InvalidDataException("XFBIN metadata exceeds 16 MiB inspection limit.");

        byte[] typesBytes = new byte[checked((int)typeSize)];
        byte[] pathBytes = new byte[checked((int)pathSize)];
        byte[] nameBytes = new byte[checked((int)nameSize)];
        await ReadExactAsync(stream, typesBytes, ct);
        await ReadExactAsync(stream, pathBytes, ct);
        await ReadExactAsync(stream, nameBytes, ct);
        var types = Decode(typesBytes, typeCount, 0, "NUCC");
        var paths = Decode(pathBytes, pathCount > 0 ? pathCount - 1 : 0, 1, "Paths");
        var names = Decode(nameBytes, nameCount > 0 ? nameCount - 1 : 0, 1, "Names");
        int pad = checked((int)(alignedStrings - stringsEnd));
        await SkipAsync(stream, pad, ct);

        byte[] mapBytes = new byte[checked((int)mapSize)];
        await ReadExactAsync(stream, mapBytes, ct);
        var mappings = new (uint Type, uint Path, uint Name)[checked((int)mapCount)];
        for (int i = 0; i < mappings.Length; i++)
        {
            int p = checked(i * 12);
            mappings[i] = (U(mapBytes, p), U(mapBytes, p + 4), U(mapBytes, p + 8));
            if (mappings[i].Type >= types.Length ||
                mappings[i].Path > paths.Length || mappings[i].Name > names.Length)
                throw new InvalidDataException("Chunk map refers to an invalid resource index.");
        }

        await SkipAsync(stream, checked((long)extraCount * 8L), ct);
        byte[] indexBytes = new byte[checked((int)((ulong)indexCount * 4UL))];
        await ReadExactAsync(stream, indexBytes, ct);
        uint[] indices = new uint[checked((int)indexCount)];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = U(indexBytes, i * 4);
            if (indices[i] >= mappings.Length)
                throw new InvalidDataException("Chunk index references an invalid chunk map.");
        }

        long position = checked((long)mapsEnd);
        int page = 0;
        ulong pageIndexBase = 0;
        var chunks = new List<XfbinChunkInfo>();
        var rawHeader = new byte[12];

        while (true)
        {
            int n = await stream.ReadAsync(rawHeader.AsMemory(0, 1), ct);
            if (n == 0) break;
            await ReadExactAsync(stream, rawHeader.AsMemory(1), ct);
            long headerOffset = position;
            position = checked(position + 12);
            uint payloadSize = U(rawHeader, 0);
            uint localMapIndex = U(rawHeader, 4);
            ushort version = BinaryPrimitives.ReadUInt16BigEndian(rawHeader.AsSpan(8, 2));
            ushort attribute = BinaryPrimitives.ReadUInt16BigEndian(rawHeader.AsSpan(10, 2));
            if (payloadSize > MaxChunkBytes)
                throw new InvalidDataException("One chunk exceeds 256 MiB inspection limit.");
            ulong absoluteIndex = pageIndexBase + localMapIndex;
            if (absoluteIndex >= (ulong)indices.Length)
                throw new InvalidDataException("Page-local chunk index is outside the map table.");
            var m = mappings[checked((int)indices[(int)absoluteIndex])];
            string type = types[(int)m.Type];
            string path = m.Path == 0 ? "" : paths[(int)m.Path - 1];
            string name = m.Name == 0 ? "" : names[(int)m.Name - 1];

            chunks.Add(new XfbinChunkInfo(chunks.Count, page, type, path, name,
                headerOffset, position, payloadSize, localMapIndex, version, attribute));
            if (chunks.Count > MaxChunks)
                throw new InvalidDataException("XFBIN has too many chunks to inspect.");

            if (type == "nuccChunkPage")
            {
                if (payloadSize < 8)
                    throw new InvalidDataException("NUCC page footer is truncated.");
                byte[] footer = new byte[8];
                await ReadExactAsync(stream, footer, ct);
                uint pageTableCount = U(footer, 0);
                pageIndexBase += pageTableCount;
                if (pageIndexBase > (ulong)indices.Length)
                    throw new InvalidDataException("NUCC page index counts exceed mapping table.");
                await SkipAsync(stream, payloadSize - 8, ct);
                page++;
            }
            else
            {
                await SkipAsync(stream, payloadSize, ct);
            }
            position = checked(position + payloadSize);
        }
        return new XfbinChunkCatalog(page, position, checked((long)mapsEnd), chunks);
    }

    public static async Task CopyPayloadAsync(Stream source, XfbinChunkInfo chunk,
        Stream destination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("Destination not writable.");
        if (chunk.HeaderOffset < HeaderSize || chunk.PayloadLength > MaxChunkBytes)
            throw new ArgumentException("Invalid chunk selection.");

        if (source.CanSeek) source.Position = 0;
        await SkipAsync(source, chunk.HeaderOffset, ct);
        byte[] header = new byte[12];
        await ReadExactAsync(source, header, ct);
        uint n = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
        uint localIndex = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4, 4));
        if (n != chunk.PayloadLength || localIndex != chunk.PageLocalMapIndex)
            throw new InvalidDataException("The source chunk differs from the indexed selection.");

        byte[] buffer = new byte[64 * 1024];
        long remaining = chunk.PayloadLength;
        while (remaining > 0)
        {
            int read = await source.ReadAsync(buffer.AsMemory(0,
                (int)Math.Min(remaining, buffer.Length)), ct);
            if (read == 0)
                throw new InvalidDataException("Selected chunk is truncated.");
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            remaining -= read;
        }
    }

    private static string[] Decode(byte[] data, uint count, int skip, string label)
    {
        if (count > 100_000) throw new InvalidDataException(label + " count exceeds limit.");
        if (skip > data.Length) throw new InvalidDataException(label + " is truncated.");
        string[] strings = new string[checked((int)count)];
        int start = skip;
        for (int i = 0; i < strings.Length; i++)
        {
            int end = Array.IndexOf(data, (byte)0, start);
            if (end < 0) throw new InvalidDataException(label + " lacks a terminating NUL.");
            strings[i] = Encoding.UTF8.GetString(data, start, end - start);
            start = end + 1;
        }
        return strings;
    }

    private static async Task ReadExactAsync(Stream source, Memory<byte> bytes, CancellationToken ct)
    {
        int offset = 0;
        while (offset < bytes.Length)
        {
            int n = await source.ReadAsync(bytes[offset..], ct);
            if (n == 0) throw new InvalidDataException("XFBIN stream ended unexpectedly.");
            offset += n;
        }
    }

    private static async Task SkipAsync(Stream source, long remaining, CancellationToken ct)
    {
        byte[] buffer = new byte[64 * 1024];
        while (remaining > 0)
        {
            int n = await source.ReadAsync(buffer.AsMemory(0,
                (int)Math.Min(remaining, buffer.Length)), ct);
            if (n == 0) throw new InvalidDataException("XFBIN stream ended while skipping data.");
            remaining -= n;
        }
    }
}
