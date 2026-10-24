using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using NSCSwitch.Core;

namespace NSCSwitch.AndroidApp;

public sealed class ChunkBrowserPage : ContentPage
{
    private FileResult? _file;
    private XfbinChunkCatalog? _catalog;
    private readonly Entry _filter = new() { Placeholder = "Filter: nuccChunkAnm / model / skill" };
    private readonly Picker _choice = new() { Title = "Select a resource chunk" };
    private readonly Label _status = new() { Text = "Pick an extracted Switch XFBIN.", TextColor = Colors.White };
    private readonly Button _export = new() { Text = "Export selected raw chunk", IsEnabled = false };
    private List<XfbinChunkInfo> _displayed = new();

    public ChunkBrowserPage()
    {
        Title = "XFBIN Chunk Browser";
        BackgroundColor = Color.FromArgb("#101722");
        var pick = new Button { Text = "Open extracted XFBIN" };
        pick.Clicked += async (_, _) => await PickAsync();
        _filter.TextChanged += (_, _) => Refresh();
        _choice.SelectedIndexChanged += (_, _) =>
        {
            var index = _choice.SelectedIndex;
            _export.IsEnabled = index >= 0 && index < _displayed.Count;
        };
        _export.Clicked += async (_, _) => await ExportAsync();
        Content = new ScrollView { Content = new VerticalStackLayout
        {
            Padding = new Thickness(18, 22), Spacing = 13,
            Children =
            {
                new Label { Text = "Resource Chunk Browser", FontSize = 23,
                    FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                new Label { Text = "Read-only type/path/name/offset listing. Exported chunks are raw bytes; model and animation decoding is NOT included.", TextColor = Colors.Orange },
                pick, _filter, _choice, _export, _status
            }
        }};
    }

    private async Task PickAsync()
    {
        try
        {
            var selected = await FilePicker.Default.PickAsync();
            if (selected is null) return;
            if (!selected.FileName.EndsWith(".xfbin", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Please select an .xfbin file.");
            _status.Text = "Indexing chunk references...";
            await using var source = await selected.OpenReadAsync();
            var indexed = await XfbinChunkReader.InspectAsync(source);
            _file = selected;
            _catalog = indexed;
            _filter.Text = "";
            Refresh();
        }
        catch (Exception ex)
        {
            _file = null;
            _catalog = null;
            _choice.ItemsSource = null;
            _export.IsEnabled = false;
            _status.Text = "Index rejected: " + ex.Message;
        }
    }

    private void Refresh()
    {
        if (_catalog is null) return;
        string needle = _filter.Text?.Trim() ?? "";
        _displayed = _catalog.Chunks
            .Where(c => c.ChunkType.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || c.ResourceName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || c.ResourcePath.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .Take(150).ToList();
        _choice.ItemsSource = _displayed.Select(c =>
            $"#{c.Index} P{c.Page}: {c.ChunkType} {c.ResourceName} ({c.PayloadLength:N0} bytes)")
            .ToList();
        _choice.SelectedIndex = -1;
        _export.IsEnabled = false;
        _status.Text =
            "File: " + _file?.FileName +
            "\nPages: " + _catalog.PageCount +
            "\nChunks: " + _catalog.Chunks.Count +
            "\nFirst chunk: 0x" + _catalog.FirstChunkOffset.ToString("X") +
            "\nShowing first " + _displayed.Count + " matching chunks (limit 150)." +
            "\nFilter by 'nuccChunkModel', 'nuccChunkClump', 'nuccChunkAnm', or 'nuccChunkBinary'.";
    }

    private async Task ExportAsync()
    {
        int selected = _choice.SelectedIndex;
        if (_file is null || selected < 0 || selected >= _displayed.Count) return;
        var chunk = _displayed[selected];
        _export.IsEnabled = false;
        var name = "nsc-chunk-" + chunk.Index.ToString("D5") + ".bin";
        var path = Path.Combine(FileSystem.CacheDirectory, name);
        try
        {
            await using (var input = await _file.OpenReadAsync())
            await using (var output = File.Create(path))
                await XfbinChunkReader.CopyPayloadAsync(input, chunk, output);

            _status.Text += "\nExtracted " + chunk.ChunkType + " / " + chunk.ResourceName +
                " to a raw .bin copy; original XFBIN unchanged.";
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Raw NUCC chunk " + chunk.Index,
                File = new ShareFile(path)
            });
        }
        catch (Exception ex)
        {
            _status.Text += "\nChunk export failed: " + ex.Message;
            try { File.Delete(path); } catch { }
        }
        finally { _export.IsEnabled = _choice.SelectedIndex >= 0; }
    }
}
