using System.Buffers.Binary;
using System.Text;

namespace NSCSwitch.Core;

/// <summary>
/// Read-only NUCC name-table inventory. These entries are not verified Switch 1.70 game
/// parameter schemas; do not infer gameplay support from a successful parse.
/// </summary>
public sealed record XfbinInventory(
    XfbinInspection Header,
    IReadOnlyList<string> NuccPacks,
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> Names,
    IReadOnlyList<string> Warnings);

public static class XfbinInventoryReader
{
    // Prevent corrupt headers from asking the app to allocate excessive memory.
    public const int MaxStringMetadataBytes = 8 * 1024 * 1024;
    private const uint MaxEntriesPerTable = 100_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async Task<XfbinInventory> InspectAsync(Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("The selected stream cannot be read.", nameof(source));
        if (source.CanSeek) source.Position = 0;

        var header = new byte[XfbinHeaderReader.HeaderLength];
        await ReadRequiredAsync(source, header, cancellationToken);

        if (!header.AsSpan(0, 4).SequenceEqual("NUCC"u8))
            throw new InvalidDataException("Not an XFBIN container (missing NUCC signature).");

        static uint Get(byte[] bytes, int offset)
            => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));

        uint nuccCount = Get(header, 0x1C);
        uint nuccBytes = Get(header, 0x20);
        uint pathCountRaw = Get(header, 0x24);
        uint pathBytes = Get(header, 0x28);
        uint nameCount = Get(header, 0x2C);
        uint nameBytes = Get(header, 0x30);

        if (nuccCount > MaxEntriesPerTable || pathCountRaw > MaxEntriesPerTable + 1
            || nameCount > MaxEntriesPerTable)
            throw new InvalidDataException("NUCC string-table entry count exceeds the safety limit.");

        ulong stringEnd = (ulong)header.Length + nuccBytes + pathBytes + nameBytes;
        if (stringEnd > MaxStringMetadataBytes)
            throw new InvalidDataException("XFBIN string metadata exceeds the 8 MiB inspection limit.");

        int metaBytes = checked((int)stringEnd);
        byte[] metadata = new byte[metaBytes];
        Buffer.BlockCopy(header, 0, metadata, 0, header.Length);
        if (metaBytes > header.Length)
            await ReadRequiredAsync(source, metadata.AsMemory(header.Length), cancellationToken);

        long totalLength;
        if (source.CanSeek)
        {
            totalLength = source.Length;
        }
        else
        {
            totalLength = metaBytes;
            byte[] buffer = new byte[64 * 1024];
            int n;
            while ((n = await source.ReadAsync(buffer, cancellationToken)) != 0)
                totalLength = checked(totalLength + n);
        }

        if (!XfbinHeaderReader.TryRead(header, totalLength, out var inspected, out var error))
            throw new InvalidDataException(error);

        int nuccStart = header.Length;
        int pathStart = checked(nuccStart + (int)nuccBytes);
        int nameStart = checked(pathStart + (int)pathBytes);
        var warnings = new List<string>();

        var packs = ReadStrings(metadata.AsSpan(nuccStart, (int)nuccBytes),
            nuccCount, 0, "NUCC", warnings);
        var paths = ReadStrings(metadata.AsSpan(pathStart, (int)pathBytes),
            pathCountRaw > 0 ? pathCountRaw - 1 : 0, 1, "Paths", warnings);
        var names = ReadStrings(metadata.AsSpan(nameStart, (int)nameBytes),
            nameCount, 1, "Names", warnings);
        if (pathCountRaw == 0)
            warnings.Add("Path count is zero; the expected sentinel entry is absent.");

        return new XfbinInventory(inspected!, packs, paths, names, warnings);
    }

    private static async Task ReadRequiredAsync(Stream stream, Memory<byte> target,
        CancellationToken cancellationToken)
    {
        int filled = 0;
        while (filled < target.Length)
        {
            int n = await stream.ReadAsync(target[filled..], cancellationToken);
            if (n == 0)
                throw new InvalidDataException("XFBIN string metadata or header is truncated.");
            filled += n;
        }
    }

    private static List<string> ReadStrings(ReadOnlySpan<byte> section,
        uint count, int leadingMarker, string label, List<string> warnings)
    {
        var output = new List<string>((int)count);
        if (count == 0) return output;

        if (section.Length < leadingMarker)
            throw new InvalidDataException(label + " section lacks its leading marker.");

        if (leadingMarker == 1 && section[0] != 0)
            warnings.Add(label + " section does not start with the expected NUL marker.");

        int offset = leadingMarker;
        for (uint i = 0; i < count; i++)
        {
            int end = section[offset..].IndexOf((byte)0);
            if (end < 0)
                throw new InvalidDataException(label + " entry " + i + " is not NUL-terminated.");
            var bytes = section.Slice(offset, end);
            try
            {
                output.Add(StrictUtf8.GetString(bytes));
            }
            catch (DecoderFallbackException)
            {
                output.Add(Encoding.Latin1.GetString(bytes));
                if (!warnings.Contains(label + " contains non-UTF-8 strings; displayed using Latin-1."))
                    warnings.Add(label + " contains non-UTF-8 strings; displayed using Latin-1.");
            }
            offset += end + 1;
        }
        return output;
    }
}
