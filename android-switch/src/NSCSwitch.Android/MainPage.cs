using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using System.Text.Json;
using NSCSwitch.Core;

namespace NSCSwitch.AndroidApp;

public sealed class MainPage : ContentPage
{
    private const int MaxVisibleEntries = 80;
    private readonly Label _result;
    private readonly Label _entries;
    private readonly Button _open;
    private readonly Button _export;
    private readonly Entry _search;
    private XfbinInventory? _inventory;
    private string? _selectedFile;
    private string _section = "NUCC";

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
            Text = "Android / Switch 1.70 — Android Tools 0.3.2",
            TextColor = Colors.LightGray,
            FontSize = 14
        };
        _open = new Button
        {
            Text = "Open XFBIN",
            BackgroundColor = Color.FromArgb("#236A9D"),
            TextColor = Colors.White
        };
        _open.Clicked += async (_, _) => await OpenFileAsync();

        _export = new Button { Text = "Share JSON report", IsEnabled = false };
        _export.Clicked += async (_, _) => await ShareReportAsync();

        _result = new Label
        {
            Text = "Choose an extracted Switch .xfbin to list container names, paths and NUCC packs.",
            TextColor = Colors.White,
            FontSize = 14
        };
        _entries = new Label
        {
            Text = "No file loaded.",
            TextColor = Colors.White,
            FontSize = 13,
            LineBreakMode = LineBreakMode.CharacterWrap
        };
        _search = new Entry
        {
            Placeholder = "Filter names / paths / packs",
            TextColor = Colors.White,
            PlaceholderColor = Colors.LightGray,
            BackgroundColor = Color.FromArgb("#263445"),
            IsEnabled = false
        };
        _search.TextChanged += (_, _) => RenderEntries();

        var tabs = new HorizontalStackLayout { Spacing = 8 };
        foreach (string section in new[] { "NUCC", "Paths", "Names" })
        {
            var button = new Button
            {
                Text = section,
                FontSize = 12,
                BackgroundColor = Color.FromArgb("#284762"),
                TextColor = Colors.White
            };
            button.Clicked += (_, _) => { _section = section; RenderEntries(); };
            tabs.Children.Add(button);
        }

        var swapButton = new Button { Text = "Experimental XFBIN file swap" };
        swapButton.Clicked += async (_, _) => await Navigation.PushAsync(new SwapPage());
        var patchButton = new Button { Text = "Expert XFBIN hex patch" };
        patchButton.Clicked += async (_, _) => await Navigation.PushAsync(new HexPatchPage());
        var viewerButton = new Button { Text = "3D model & animation viewer (GLB)" };
        viewerButton.Clicked += async (_, _) => await Navigation.PushAsync(new ViewerPage());

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(18, 28),
                Spacing = 15,
                Children =
                {
                    heading, subtitle, swapButton, patchButton, viewerButton, _open, _export, _result,
                    tabs, _search, _entries,
                    new Label
                    {
                        Text = "NUCC Explorer remains read-only. Experimental swap and hex patch create separate outputs; gameplay compatibility is unverified.",
                        TextColor = Colors.LightGray,
                        FontSize = 12
                    }
                }
            }
        };
    }

    private async Task OpenFileAsync()
    {
        _open.IsEnabled = false;
        try
        {
            var selected = await FilePicker.Default.PickAsync(
                new PickOptions { PickerTitle = "Select extracted Switch XFBIN" });
            if (selected is null) return;
            if (!selected.FileName.EndsWith(".xfbin", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Select an extracted .xfbin file, not a .cpk.");

            _result.Text = "Reading NUCC metadata...";
            await using var stream = await selected.OpenReadAsync();
            var inventory = await XfbinInventoryReader.InspectAsync(stream);
            _inventory = inventory;
            _selectedFile = selected.FileName;
            _section = "NUCC";
            _export.IsEnabled = true;
            _search.IsEnabled = true;
            _search.Text = "";

            var h = inventory.Header;
            var types = inventory.NuccPacks;
            string modelTypes = string.Join(", ", new[]
            {
                "nuccChunkModel", "nuccChunkClump", "nuccChunkAnm", "nuccChunkCoord"
            }.Where(t => types.Contains(t, StringComparer.Ordinal)));
            _result.Text =
                $"File: {_selectedFile}\n" +
                $"Size: {h.FileLength:N0} bytes\n" +
                $"NUCC packs: {inventory.NuccPacks.Count:N0}\n" +
                $"Paths: {inventory.Paths.Count:N0}\n" +
                $"Names: {inventory.Names.Count:N0}\n" +
                $"First chunk offset: 0x{h.FirstChunkOffset:X}\n" +
                (inventory.Warnings.Count == 0
                    ? "Header and string tables parsed. Switch format not yet verified."
                    : "Warnings: " + string.Join("; ", inventory.Warnings));
            RenderEntries();
        }
        catch (Exception ex)
        {
            _inventory = null;
            _selectedFile = null;
            _export.IsEnabled = false;
            _search.IsEnabled = false;
            _result.Text = "Inspection failed: " + ex.Message;
            _entries.Text = "No file loaded.";
        }
        finally
        {
            _open.IsEnabled = true;
        }
    }

    private void RenderEntries()
    {
        if (_inventory is null) return;
        IReadOnlyList<string> items = _section switch
        {
            "Paths" => _inventory.Paths,
            "Names" => _inventory.Names,
            _ => _inventory.NuccPacks
        };
        string term = _search.Text?.Trim() ?? "";
        var filtered = items.Select((value, index) => (value, index))
            .Where(x => x.value.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var preview = filtered.Take(MaxVisibleEntries)
            .Select(x => $"[{x.index}] " +
                (x.value.Length > 220 ? x.value[..220] + "..." : x.value));
        _entries.Text = _section + " — " + filtered.Count + "/" + items.Count +
            " entries\n\n" + string.Join("\n", preview) +
            (filtered.Count > MaxVisibleEntries
                ? "\n...Showing first " + MaxVisibleEntries + ". Refine the filter or export JSON for all."
                : filtered.Count == 0 ? "No matching entries." : "");
    }

    private async Task ShareReportAsync()
    {
        if (_inventory is null || _selectedFile is null) return;
        try
        {
            var report = new
            {
                FileName = _selectedFile,
                Container = "XFBIN/NUCC",
                Mode = "read-only-inventory",
                Notes = "Not a verification of Switch parameter layout or game runtime.",
                _inventory.Header,
                _inventory.NuccPacks,
                _inventory.Paths,
                _inventory.Names,
                _inventory.Warnings
            };
            string path = Path.Combine(FileSystem.CacheDirectory, "nsc-xfbin-inventory.json");
            await File.WriteAllTextAsync(path,
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "XFBIN inventory report",
                File = new ShareFile(path)
            });
        }
        catch (Exception ex)
        {
            _result.Text += "\nReport export failed: " + ex.Message;
        }
    }
}
