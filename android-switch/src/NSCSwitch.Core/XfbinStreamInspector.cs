namespace NSCSwitch.Core;

public static class XfbinStreamInspector
{
    /// <summary>
    /// Reads just the header into memory. Non-seekable Android document streams
    /// are scanned in fixed-size blocks to determine length without buffering all bytes.
    /// </summary>
    public static async Task<XfbinInspection> InspectAsync(Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        byte[] header = new byte[XfbinHeaderReader.HeaderLength];
        int read = 0;
        while (read < header.Length)
        {
            int n = await source.ReadAsync(header.AsMemory(read), cancellationToken);
            if (n == 0)
                throw new InvalidDataException("XFBIN header is truncated.");
            read += n;
        }

        long length;
        if (source.CanSeek)
        {
            length = source.Length;
        }
        else
        {
            length = read;
            byte[] buffer = new byte[64 * 1024];
            int n;
            while ((n = await source.ReadAsync(buffer, cancellationToken)) > 0)
                length = checked(length + n);
        }

        if (!XfbinHeaderReader.TryRead(header, length, out var result, out var error))
            throw new InvalidDataException(error);
        return result!;
    }
}
