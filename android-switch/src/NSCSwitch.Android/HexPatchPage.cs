using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using NSCSwitch.Core;

namespace NSCSwitch.AndroidApp;

public sealed class HexPatchPage : ContentPage
{
    private FileResult? _selected;
    private readonly Entry _offset = new() { Placeholder = "Offset: 0x100 or 256", Keyboard = Keyboard.Text };
    private readonly Entry _expected = new() { Placeholder = "Original hex bytes: 01020304" };
    private readonly Entry _replacement = new() { Placeholder = "New hex bytes: same length" };
    private readonly Label _status = new() { Text = "No XFBIN chosen.", TextColor = Colors.White };
    private readonly Button _apply = new() { Text = "Create separate patched XFBIN", IsEnabled = false };

    public HexPatchPage()
    {
        Title = "Expert Moveset Hex Editor";
        BackgroundColor = Color.FromArgb("#101722");
        var picker = new Button { Text = "Choose moveset/parameter XFBIN" };
        picker.Clicked += async (_, _) =>
        {
            var file = await FilePicker.Default.PickAsync();
            if (file != null)
            {
                _selected = file;
                _status.Text = "Source: " + file.FileName;
                _apply.IsEnabled = true;
            }
        };
        _apply.Clicked += async (_, _) => await ApplyAsync();
        Content = new ScrollView { Content = new VerticalStackLayout
        {
            Padding = new Thickness(18, 24), Spacing = 12,
            Children =
            {
                new Label { Text = "Expert hex patch (experimental)", FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                new Label { Text = "This edits exact bytes, not named combo/jutsu fields. You must know the offset and expected values. The patch does not repair resource references; malformed edits can crash the game.", TextColor = Colors.Orange },
                picker, _status, _offset, _expected, _replacement, _apply
            }
        }};
    }

    private async Task ApplyAsync()
    {
        if (_selected == null) return;
        _apply.IsEnabled = false;
        string path = Path.Combine(FileSystem.CacheDirectory, "nsc-patched-output.xfbin");
        try
        {
            if (!_selected.FileName.EndsWith(".xfbin", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose .xfbin source file.");
            long position = XfbinHexPatch.ParseOffset(_offset.Text ?? "");
            byte[] expected = XfbinHexPatch.ParseHex(_expected.Text ?? "");
            byte[] replacement = XfbinHexPatch.ParseHex(_replacement.Text ?? "");

            await using (var original = await _selected.OpenReadAsync())
            await using (var output = File.Create(path))
                await XfbinHexPatch.ApplyAsync(original, output, position, expected, replacement);

            _status.Text = "Patched copy created. Source file unchanged.";
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Patched XFBIN (unverified)", File = new ShareFile(path)
            });
        }
        catch (Exception ex)
        {
            _status.Text = "Patch rejected: " + ex.Message;
            try { File.Delete(path); } catch { }
        }
        finally { _apply.IsEnabled = true; }
    }
}
