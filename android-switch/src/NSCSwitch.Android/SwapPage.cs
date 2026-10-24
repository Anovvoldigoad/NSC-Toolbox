using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using NSCSwitch.Core;

namespace NSCSwitch.AndroidApp;

public sealed class SwapPage : ContentPage
{
    private FileResult? _first;
    private FileResult? _second;
    private readonly Label _status = new() { Text = "Choose two extracted XFBIN files.", TextColor = Colors.White };
    private readonly Button _export = new() { Text = "Export experimental swap ZIP", IsEnabled = false };

    public SwapPage()
    {
        Title = "XFBIN File Swap";
        BackgroundColor = Color.FromArgb("#101722");
        var a = new Button { Text = "Choose XFBIN A" };
        var b = new Button { Text = "Choose XFBIN B" };
        a.Clicked += async (_, _) =>
        {
            var selected = await FilePicker.Default.PickAsync();
            if (selected != null) { _first = selected; Refresh(); }
        };
        b.Clicked += async (_, _) =>
        {
            var selected = await FilePicker.Default.PickAsync();
            if (selected != null) { _second = selected; Refresh(); }
        };
        _export.Clicked += async (_, _) => await ExportAsync();
        Content = new ScrollView { Content = new VerticalStackLayout
        {
            Padding = new Thickness(18, 24), Spacing = 14,
            Children =
            {
                new Label { Text = "Experimental full-file XFBIN swap", FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                new Label
                {
                    Text = "Swaps whole file payloads, NOT character references or complete movesets. CPK rebuilding and correct resource IDs are not handled. A bad swap can prevent the game from booting. Do not install without separate dependency verification.",
                    TextColor = Colors.Orange, FontSize = 13
                },
                a, b, _status, _export
            }
        }};
    }

    private void Refresh()
    {
        _status.Text = "A: " + (_first?.FileName ?? "none") + "\nB: " + (_second?.FileName ?? "none");
        _export.IsEnabled = _first != null && _second != null;
    }

    private async Task ExportAsync()
    {
        if (_first is null || _second is null) return;
        _export.IsEnabled = false;
        string path = Path.Combine(FileSystem.CacheDirectory, "nsc-unverified-file-swap.zip");
        try
        {
            await using (var output = File.Create(path))
                await XfbinSwapArchive.CreateAsync(
                    () => _first.OpenReadAsync(), _first.FileName,
                    () => _second.OpenReadAsync(), _second.FileName, output);
            _status.Text = "ZIP ready. Original files unchanged. Read SWAP_MANIFEST.json.";
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Unverified XFBIN file swap", File = new ShareFile(path)
            });
        }
        catch (Exception ex)
        {
            _status.Text = "Swap rejected: " + ex.Message;
            try { File.Delete(path); } catch { }
        }
        finally { _export.IsEnabled = true; }
    }
}
