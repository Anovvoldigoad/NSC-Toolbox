using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using System.Text.Json;
using NSCSwitch.Core;

namespace NSCSwitch.AndroidApp;

public sealed class MainPage : ContentPage
{
    private readonly Label _result;
    private readonly Button _export;
    private XfbinInspection? _inspection;
    private string? _selectedFile;

    public MainPage()
    {
        Title = "NSC Toolbox Switch";
        BackgroundColor = Color.FromArgb("#101722");
        var heading = new Label
        {
            Text = "NSC Toolbox Switch",
            FontSize = 26,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White
        };
        var subtitle = new Label
        {
            Text = "Android / Nintendo Switch 1.70 — XFBIN inspector (read-only alpha)",
            TextColor = Colors.LightGray,
            FontSize = 14
        };
        var pick = new Button { Text = "Open XFBIN", BackgroundColor = Color.FromArgb("#236A9D"), TextColor = Colors.White };
        pick.Clicked += async (_, _) => await OpenFileAsync();
        _export = new Button { Text = "Share inspection report", IsEnabled = false };
        _export.Clicked += async (_, _) => await ShareReportAsync();
        _result = new Label { Text = "Select an extracted .xfbin file to inspect its NUCC header.", TextColor = Colors.White, FontSize = 14 };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20, 36),
                Spacing = 18,
                Children =
                {
                    heading, subtitle, pick, _export, _result,
                    new Label { Text = "This alpha does not modify XFBIN or CPK files.", TextColor = Colors.LightGray, FontSize = 12 }
                }
            }
        };
    }

    private async Task OpenFileAsync()
    {
        try
        {
            var selected = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Select extracted XFBIN" });
            if (selected is null) return;
            if (!selected.FileName.EndsWith(".xfbin", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Please choose a .xfbin file, not a CPK archive.");
            await using var input = await selected.OpenReadAsync();
            var parsed = await XfbinStreamInspector.InspectAsync(input);
            _inspection = parsed;
            _selectedFile = selected.FileName;
            _export.IsEnabled = true;
            _result.Text = $"File: {_selectedFile}\n" +
                $"Size: {parsed.FileLength:N0} bytes\n" +
                $"NUCC entries: {parsed.NuccEntryCount:N0}\n" +
                $"Paths: {parsed.PathEntryCount:N0}\n" +
                $"Names: {parsed.NameEntryCount:N0}\n" +
                $"First chunk offset: 0x{parsed.FirstChunkOffset:X}\n" +
                $"NUCC table: {parsed.NuccTableBytes:N0} bytes\n" +
                $"Path table: {parsed.PathTableBytes:N0} bytes\n" +
                $"Name table: {parsed.NameTableBytes:N0} bytes\n" +
                $"Binary tables: {parsed.FirstBinaryTableBytes + (long)parsed.SecondBinaryTableBytes:N0} bytes\n" +
                "Status: structurally plausible NUCC header (not proof of Switch compatibility).";
        }
        catch (Exception ex)
        {
            _inspection = null;
            _selectedFile = null;
            _export.IsEnabled = false;
            _result.Text = $"Could not inspect file: {ex.Message}";
        }
    }

    private async Task ShareReportAsync()
    {
        if (_inspection is null || _selectedFile is null) return;
        try
        {
            var report = new { FileName = _selectedFile, Format = "XFBIN/NUCC", Version = "read-only-alpha", Info = _inspection };
            var path = Path.Combine(FileSystem.CacheDirectory, "nsc-xfbin-report.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            await Share.Default.RequestAsync(new ShareFileRequest { Title = "XFBIN inspection report", File = new ShareFile(path) });
        }
        catch (Exception ex)
        {
            _result.Text += $"\nReport export failed: {ex.Message}";
        }
    }
}
