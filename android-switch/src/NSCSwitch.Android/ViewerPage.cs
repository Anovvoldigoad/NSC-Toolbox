using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Graphics;
using System.Text.Json;

namespace NSCSwitch.AndroidApp;

/// <summary>Renders self-contained GLB, NOT native XFBIN. Babylon.js is fetched over HTTPS.</summary>
public sealed class ViewerPage : ContentPage
{
    private const int MaxPreviewBytes = 20 * 1024 * 1024;
    private readonly WebView _web;
    private readonly Label _status;
    private readonly Button _choose;
    private bool _loaded;

    public ViewerPage()
    {
        Title = "3D Model & Animation";
        BackgroundColor = Color.FromArgb("#101722");
        _status = new Label
        {
            Text = "3D preview: self-contained .glb only (mesh/skeleton/animation). Direct XFBIN decoding is not implemented. Internet required for the rendering library.",
            TextColor = Colors.White,
            FontSize = 12
        };
        _choose = new Button { Text = "Select animated .glb model" };
        _choose.Clicked += async (_, _) => await OpenAsync();
        _web = new WebView { Source = "viewer.html" };
        _web.Navigated += (_, e) =>
        {
            _loaded = e.Result == WebNavigationResult.Success;
            if (!_loaded) _status.Text = "Viewer failed to initialize.";
        };
        var layout = new Grid { Padding = 10, RowSpacing = 8 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        Grid.SetRow(_status, 0);
        Grid.SetRow(_choose, 1);
        Grid.SetRow(_web, 2);
        layout.Children.Add(_status);
        layout.Children.Add(_choose);
        layout.Children.Add(_web);
        Content = layout;
    }

    private async Task OpenAsync()
    {
        _choose.IsEnabled = false;
        try
        {
            if (!_loaded) throw new InvalidOperationException("Viewer page hasn't loaded.");
            var ready = await _web.EvaluateJavaScriptAsync("String(window.viewerReady === true)");
            if (ready?.Trim('"') != "true")
                throw new InvalidOperationException("3D engine unavailable. Connect to internet and reopen this screen.");

            var file = await FilePicker.Default.PickAsync(
                new PickOptions { PickerTitle = "Select a single-file glTF .glb" });
            if (file == null) return;
            if (!file.FileName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Only .glb is currently supported.");

            await using var input = await file.OpenReadAsync();
            if (input.CanSeek && input.Length > MaxPreviewBytes)
                throw new InvalidDataException("GLB exceeds the 20 MiB preview limit.");
            using var copied = new MemoryStream();
            byte[] part = new byte[64 * 1024];
            int n;
            while ((n = await input.ReadAsync(part)) > 0)
            {
                if (copied.Length + n > MaxPreviewBytes)
                    throw new InvalidDataException("GLB exceeds the 20 MiB preview limit.");
                await copied.WriteAsync(part.AsMemory(0, n));
            }
            _status.Text = "Loading " + file.FileName + "...";
            string encoded = Convert.ToBase64String(copied.ToArray());
            await _web.EvaluateJavaScriptAsync(
                "window.loadModelFromBase64(" + JsonSerializer.Serialize(encoded) + ")");
            _status.Text = "Preview requested: " + file.FileName +
                ". Animation controls are inside the viewer.";
        }
        catch (Exception ex) { _status.Text = "Viewer: " + ex.Message; }
        finally { _choose.IsEnabled = true; }
    }
}
