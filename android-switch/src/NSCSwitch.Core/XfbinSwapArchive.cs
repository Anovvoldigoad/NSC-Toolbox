using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace NSCSwitch.Core;

/// <summary>
/// Experimental whole-XFBIN swap; no internal moveset or character IDs are remapped.
/// Validates both input containers, writes copies and records exact SHA-256 payloads.
/// </summary>
public static class XfbinSwapArchive
{
    public static async Task CreateAsync(
        Func<Task<Stream>> openA, string fileNameA,
        Func<Task<Stream>> openB, string fileNameB,
        Stream destination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("Output is not writable.", nameof(destination));

        // Fail closed on malformed input before starting a ZIP.
        var analysis = await XfbinSwapAnalyzer.AnalyzeAsync(
            openA, fileNameA, openB, fileNameB, ct);

        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        string hashB = await AddAsync(zip, "swapped/" + analysis.FirstFile, openB, ct);
        string hashA = await AddAsync(zip, "swapped/" + analysis.SecondFile, openA, ct);

        var manifest = zip.CreateEntry("SWAP_MANIFEST.json", CompressionLevel.Optimal);
        await using var writer = manifest.Open();
        await JsonSerializer.SerializeAsync(writer, new
        {
            format = "nsc-switch-whole-xfbin-swap-v2",
            status = "EXPERIMENTAL_NOT_GAME_VERIFIED",
            sources = new[]
            {
                new { sourceFile = analysis.FirstFile, byteLength = analysis.FirstBytes },
                new { sourceFile = analysis.SecondFile, byteLength = analysis.SecondBytes }
            },
            replacements = new[]
            {
                new { output = "swapped/" + analysis.FirstFile, containsSource = analysis.SecondFile, sha256 = hashB },
                new { output = "swapped/" + analysis.SecondFile, containsSource = analysis.FirstFile, sha256 = hashA }
            },
            analysis,
            warnings = new[]
            {
                "This swaps entire XFBIN file bytes; it does not modify referenced character IDs or retarget animations.",
                "A successful analysis does not imply Switch 1.70 runtime compatibility.",
                "Output needs independent dependency review, CPK rebuilding and gameplay testing.",
                "Original files were not overwritten."
            }
        }, cancellationToken: ct);
    }

    private static async Task<string> AddAsync(
        ZipArchive zip, string path, Func<Task<Stream>> open, CancellationToken ct)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Fastest);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var output = entry.Open();
        await using var input = await open();
        byte[] buffer = new byte[64 * 1024];
        int n;
        while ((n = await input.ReadAsync(buffer, ct)) > 0)
        {
            hash.AppendData(buffer, 0, n);
            await output.WriteAsync(buffer.AsMemory(0, n), ct);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
