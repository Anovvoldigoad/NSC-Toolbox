namespace NSCSwitch.Core;

/// <summary>
/// Compares XFBIN string tables before an experimental whole-file swap.
/// Shared resource names are descriptive only; they never establish game
/// compatibility or correct character-ID remapping.
/// </summary>
public sealed record XfbinSetComparison(
    int FirstCount,
    int SecondCount,
    int SharedDistinctCount,
    IReadOnlyList<string> FirstOnlyPreview,
    IReadOnlyList<string> SecondOnlyPreview);

public sealed record XfbinSwapAnalysis(
    string FirstFile,
    string SecondFile,
    long FirstBytes,
    long SecondBytes,
    XfbinSetComparison NuccPacks,
    XfbinSetComparison Paths,
    XfbinSetComparison Names,
    IReadOnlyList<string> Warnings);

public static class XfbinSwapAnalyzer
{
    private const int PreviewLimit = 12;

    public static async Task<XfbinSwapAnalysis> AnalyzeAsync(
        Func<Task<Stream>> openFirst, string firstFile,
        Func<Task<Stream>> openSecond, string secondFile,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(openFirst);
        ArgumentNullException.ThrowIfNull(openSecond);
        if (!IsSafeName(firstFile) || !IsSafeName(secondFile))
            throw new ArgumentException("Both inputs must use safe .xfbin filenames.");
        if (string.Equals(firstFile, secondFile, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose different filenames for the swap.");

        XfbinInventory first;
        XfbinInventory second;
        await using (var s = await openFirst())
            first = await XfbinInventoryReader.InspectAsync(s, ct);
        await using (var s = await openSecond())
            second = await XfbinInventoryReader.InspectAsync(s, ct);

        var packs = Compare(first.NuccPacks, second.NuccPacks);
        var paths = Compare(first.Paths, second.Paths);
        var names = Compare(first.Names, second.Names);
        var warnings = first.Warnings.Select(w => "First: " + w)
            .Concat(second.Warnings.Select(w => "Second: " + w)).ToList();

        if (packs.FirstCount != packs.SecondCount || packs.FirstOnlyPreview.Count > 0
            || packs.SecondOnlyPreview.Count > 0)
            warnings.Add("NUCC type lists differ. This may indicate incompatible resource structures.");
        if (paths.FirstCount != paths.SecondCount || paths.FirstOnlyPreview.Count > 0
            || paths.SecondOnlyPreview.Count > 0)
            warnings.Add("Resource path lists differ. Dependencies and references require manual verification.");
        if (names.FirstCount != names.SecondCount || names.FirstOnlyPreview.Count > 0
            || names.SecondOnlyPreview.Count > 0)
            warnings.Add("Named resource entries differ. Character assets may have missing links.");

        warnings.Add("A similar table layout does NOT prove a safe moveset or model swap.");
        warnings.Add("This tool does not remap character IDs, animation references, or rebuild CPK.");

        return new XfbinSwapAnalysis(firstFile, secondFile,
            first.Header.FileLength, second.Header.FileLength,
            packs, paths, names, warnings);
    }

    private static bool IsSafeName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name == Path.GetFileName(name)
        && name.EndsWith(".xfbin", StringComparison.OrdinalIgnoreCase)
        && name.IndexOfAny(new[] { '/', '\\', ':', '\0' }) < 0;

    private static XfbinSetComparison Compare(
        IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        var a = new HashSet<string>(first, StringComparer.Ordinal);
        var b = new HashSet<string>(second, StringComparer.Ordinal);
        return new XfbinSetComparison(first.Count, second.Count,
            a.Count(value => b.Contains(value)),
            a.Except(b).OrderBy(x => x, StringComparer.Ordinal).Take(PreviewLimit).ToArray(),
            b.Except(a).OrderBy(x => x, StringComparer.Ordinal).Take(PreviewLimit).ToArray());
    }
}
