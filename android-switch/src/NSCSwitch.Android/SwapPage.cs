using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using NSCSwitch.Core;

namespace NSCSwitch.AndroidApp;

public sealed class SwapPage : ContentPage
{
    private FileResult? _first;
    private FileResult? _second;
    private XfbinSwapAnalysis? _analysis;
    private readonly Label _status = new() { Text = "Choose two extracted XFBIN files.", TextColor = Colors.White };
    private readonly Button _analyze = new() { Text = "Analyze resource differences", IsEnabled = false };
    private readonly Button _export = new() { Text = "Export experimental swap ZIP", IsEnabled = false };

    public SwapPage()
    {
        Title = "XFBIN Swap Analyzer";
        BackgroundColor = Color.FromArgb("#101722");
        var a = new Button { Text = "Choose XFBIN A" };
        var b = new Button { Text = "Choose XFBIN B" };
        a.Clicked += async (_, _) =>
        {
            var selected = await FilePicker.Default.PickAsync();
            if (selected is not null) { _first = selected; Refresh(); }
        };
        b.Clicked += async (_, _) =>
        {
            var selected = await FilePicker.Default.PickAsync();
            if (selected is not null) { _second = selected; Refresh(); }
        };
        _analyze.Clicked += async (_, _) => await AnalyzeAsync();
        _export.Clicked += async (_, _) => await ExportAsync();
        Content = new ScrollView { Content = new VerticalStackLayout
        {
            Padding = new Thickness(18, 24), Spacing = 14,
            Children =
            {
                new Label { Text = "XFBIN swap / dependency check", FontSize = 22,
                    FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                new Label
                {
                    Text = "EXPERIMENTAL: Compare NUCC packs, paths and named entries before exchanging complete XFBIN files. No character-ID remapping or game-compatible moveset migration. Never install results without separate verification.",
                    TextColor = Colors.Orange, FontSize = 13
                },
                a, b, _analyze, _export, _status
            }
        }};
    }

    private void Refresh()
    {
        _analysis = null;
        _status.Text = "A: " + (_first?.FileName ?? "none") +
            "\nB: " + (_second?.FileName ?? "none") +
            "\nAnalyze both files before creating an archive.";
        _analyze.IsEnabled = _first != null && _second != null;
        _export.IsEnabled = false;
    }

    private async Task AnalyzeAsync()
    {
        if (_first is null || _second is null) return;
        _analyze.IsEnabled = false;
        _export.IsEnabled = false;
        try
        {
            _status.Text = "Inspecting XFBIN resource tables...";
            var report = await XfbinSwapAnalyzer.AnalyzeAsync(
                () => _first.OpenReadAsync(), _first.FileName,
                () => _second.OpenReadAsync(), _second.FileName);
            _analysis = report;
            _status.Text =
                "A: " + report.FirstFile + " (" + report.FirstBytes.ToString("N0") + " bytes)" +
                "\nB: " + report.SecondFile + " (" + report.SecondBytes.ToString("N0") + " bytes)" +
                "\nNUCC types: A " + report.NuccPacks.FirstCount + ", B " + report.NuccPacks.SecondCount +
                ", shared distinct " + report.NuccPacks.SharedDistinctCount +
                "\nPaths: A " + report.Paths.FirstCount + ", B " + report.Paths.SecondCount +
                ", shared distinct " + report.Paths.SharedDistinctCount +
                "\nNames: A " + report.Names.FirstCount + ", B " + report.Names.SecondCount +
                ", shared distinct " + report.Names.SharedDistinctCount +
                "\n\nA-only paths (preview): " + string.Join(", ", report.Paths.FirstOnlyPreview) +
                "\nB-only paths (preview): " + string.Join(", ", report.Paths.SecondOnlyPreview) +
                "\n\nWarnings:\n• " + string.Join("\n• ", report.Warnings);
            _export.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _analysis = null;
            _status.Text = "Analysis rejected: " + ex.Message;
        }
        finally { _analyze.IsEnabled = true; }
    }

    private async Task ExportAsync()
    {
        if (_first is null || _second is null || _analysis is null) return;
        _export.IsEnabled = false;
        string path = Path.Combine(FileSystem.CacheDirectory, "nsc-unverified-file-swap.zip");
        try
        {
            _status.Text += "\n\nValidating and exporting ZIP...";
            await using (var output = File.Create(path))
                await XfbinSwapArchive.CreateAsync(
                    () => _first.OpenReadAsync(), _first.FileName,
                    () => _second.OpenReadAsync(), _second.FileName, output);
            _status.Text += "\nZIP produced with SHA-256 hashes and analysis. Sources unchanged.";
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Unverified XFBIN file swap with manifest", File = new ShareFile(path)
            });
        }
        catch (Exception ex)
        {
            _status.Text += "\nExport failed: " + ex.Message;
            try { File.Delete(path); } catch { }
        }
        finally { _export.IsEnabled = _analysis is not null; }
    }
}
