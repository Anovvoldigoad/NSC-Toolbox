using System.IO.Compression;
using System.Text.Json;

namespace NSCSwitch.Core;

/// <summary>
/// Byte-for-byte whole-file XFBIN swapping, not an internal moveset remapper.
/// Source data is never overwritten and game compatibility is not implied.
/// </summary>
public static class XfbinSwapArchive
{
    public static async Task CreateAsync(
        Func<Task<Stream>> openA, string fileNameA,
        Func<Task<Stream>> openB, string fileNameB,
        Stream destination, CancellationToken ct = default)
    {
        string a = ValidateName(fileNameA), b = ValidateName(fileNameB);
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Input files must have different filenames.");
        if (!destination.CanWrite)
            throw new ArgumentException("Output is not writable.");

        await ValidateAsync(openA, ct);
        await ValidateAsync(openB, ct);
        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, true);
        await AddAsync(zip, "swapped/" + a, openB, ct);
        await AddAsync(zip, "swapped/" + b, openA, ct);
        var manifest = zip.CreateEntry("SWAP_MANIFEST.json", CompressionLevel.Optimal);
        await using var writer = manifest.Open();
        await JsonSerializer.SerializeAsync(writer, new
        {
            format = "nsc-switch-xfbin-raw-swap-v1",
            status = "EXPERIMENTAL_UNVERIFIED",
            replacements = new[]
            {
                new { output = "swapped/" + a, contains = b },
                new { output = "swapped/" + b, contains = a }
            },
            warnings = new[]
            {
                "Complete XFBIN file bytes are swapped without remapping internal character IDs.",
                "This is NOT a validated moveset/model swap; dependencies and game compatibility are unknown.",
                "Original input files are unchanged. Do not install without verifying dependencies.",
                "No CPK rebuild or ROMFS deployment is included."
            }
        }, cancellationToken: ct);
    }

    private static string ValidateName(string input)
    {
        if (string.IsNullOrWhiteSpace(input) ||
            input != Path.GetFileName(input) ||
            !input.EndsWith(".xfbin", StringComparison.OrdinalIgnoreCase) ||
            input.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0)
            throw new ArgumentException("Expected a safe .xfbin filename.");
        return input;
    }

    private static async Task ValidateAsync(Func<Task<Stream>> open, CancellationToken ct)
    {
        await using var stream = await open();
        _ = await XfbinStreamInspector.InspectAsync(stream, ct);
    }

    private static async Task AddAsync(ZipArchive archive, string name,
        Func<Task<Stream>> open, CancellationToken ct)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        await using var output = entry.Open();
        await using var input = await open();
        await input.CopyToAsync(output, 64 * 1024, ct);
    }
}
