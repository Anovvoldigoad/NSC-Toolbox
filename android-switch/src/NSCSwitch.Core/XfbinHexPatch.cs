namespace NSCSwitch.Core;

/// <summary>
/// Expert exact-byte replacement with expected-value check. This is not a
/// semantic moveset editor; unknown references are not repaired automatically.
/// The source stream is read only; the caller must provide a new output stream.
/// </summary>
public static class XfbinHexPatch
{
    public static byte[] ParseHex(string input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        string raw = new(input.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != ':').ToArray());
        if (raw.Length == 0 || (raw.Length & 1) != 0 || raw.Length > 8192)
            throw new ArgumentException("Enter pairs of hex digits (maximum 4096 bytes).");
        return Convert.FromHexString(raw);
    }

    public static long ParseOffset(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new FormatException("Offset required.");
        string s = input.Trim();
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt64(s[2..], 16)
            : long.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static async Task ApplyAsync(Stream source, Stream output,
        long offset, byte[] expected, byte[] replacement,
        CancellationToken ct = default)
    {
        if (offset < 0x44) throw new ArgumentOutOfRangeException(nameof(offset), "Header patching disabled.");
        if (expected is null || replacement is null || expected.Length == 0 ||
            expected.Length != replacement.Length || expected.Length > 4096)
            throw new ArgumentException("Equal-length expected and new bytes are required (1-4096).");
        if (!source.CanRead || !output.CanWrite)
            throw new ArgumentException("Source must be readable, output writable.");
        if (source.CanSeek) source.Position = 0;

        byte[] header = new byte[0x44];
        await source.ReadExactlyAsync(header, ct);
        if (!header.AsSpan(0, 4).SequenceEqual("NUCC"u8))
            throw new InvalidDataException("Not a NUCC/XFBIN file.");
        await output.WriteAsync(header, ct);

        long remaining = offset - header.Length;
        byte[] buffer = new byte[64 * 1024];
        while (remaining > 0)
        {
            int n = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(remaining, buffer.Length)), ct);
            if (n == 0) throw new InvalidDataException("Offset exceeds file size.");
            await output.WriteAsync(buffer.AsMemory(0, n), ct);
            remaining -= n;
        }

        byte[] actual = new byte[expected.Length];
        await source.ReadExactlyAsync(actual, ct);
        if (!actual.AsSpan().SequenceEqual(expected))
            throw new InvalidDataException("Original bytes don't match at this offset. No valid patch produced.");
        await output.WriteAsync(replacement, ct);
        await source.CopyToAsync(output, 64 * 1024, ct);
    }
}
