using System.Buffers.Binary;

namespace NSCSwitch.Core;

/// <summary>
/// Conservative read-only NUCC/XFBIN container header interpretation.
/// No claim is made about Switch/PC parameter layout compatibility.
/// </summary>
public sealed record XfbinInspection(
    long FileLength,
    uint NuccEntryCount,
    uint NuccTableBytes,
    uint PathEntryCount,
    uint PathTableBytes,
    uint NameEntryCount,
    uint NameTableBytes,
    uint FirstBinaryTableBytes,
    uint SecondBinaryTableBytes,
    long FirstChunkOffset);

public static class XfbinHeaderReader
{
    public const int HeaderLength = 0x44;

    public static bool TryRead(ReadOnlySpan<byte> header, long fileLength,
        out XfbinInspection? inspection, out string? error)
    {
        inspection = null;
        error = null;
        if (header.Length < HeaderLength || fileLength < HeaderLength)
        {
            error = "XFBIN header truncated (expected at least 0x44 bytes).";
            return false;
        }
        if (!header.Slice(0, 4).SequenceEqual("NUCC"u8))
        {
            error = "Not an XFBIN container (missing NUCC signature).";
            return false;
        }

        static uint Read(ReadOnlySpan<byte> bytes, int position)
            => BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(position, 4));

        uint nuccCount = Read(header, 0x1C);
        uint nuccSize = Read(header, 0x20);
        uint pathCount = Read(header, 0x24);
        uint pathSize = Read(header, 0x28);
        uint nameCount = Read(header, 0x2C);
        uint nameSize = Read(header, 0x30);
        uint bin1Size = Read(header, 0x38);
        uint bin2Words = Read(header, 0x3C);

        if (bin2Words > uint.MaxValue / 4)
        {
            error = "XFBIN binary table size is invalid.";
            return false;
        }

        ulong tablesEnd = (ulong)HeaderLength + nuccSize + pathSize + nameSize;
        ulong paddedTablesEnd = (tablesEnd + 3UL) & ~3UL;
        ulong firstChunk = paddedTablesEnd + bin1Size + ((ulong)bin2Words * 4UL);
        if (tablesEnd > (ulong)fileLength || firstChunk > (ulong)fileLength)
        {
            error = "XFBIN metadata section sizes exceed file length.";
            return false;
        }

        inspection = new XfbinInspection(fileLength, nuccCount, nuccSize,
            pathCount > 0 ? pathCount - 1 : 0, pathSize,
            nameCount > 0 ? nameCount - 1 : 0, nameSize, bin1Size,
            checked(bin2Words * 4), (long)firstChunk);
        return true;
    }
}
