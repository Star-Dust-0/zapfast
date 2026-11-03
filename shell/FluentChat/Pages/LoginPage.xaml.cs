using FluentChat.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using QRCoder;
using System.IO;

namespace FluentChat.Pages;

public sealed partial class LoginPage : Page
{
    private CoreClient? _core;
    private string? _shownQr;

    public LoginPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _core = (CoreClient)e.Parameter;
        _core.CoreEvent += OnCoreEvent;
        RefreshState();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_core != null)
        {
            _core.CoreEvent -= OnCoreEvent;
        }
        base.OnNavigatedFrom(e);
    }

    private async void RefreshState()
    {
        if (_core == null)
        {
            return;
        }
        try
        {
            System.Text.Json.JsonElement state = await _core.RequestAsync("getState");
            if (state.TryGetProperty("link", out System.Text.Json.JsonElement link))
            {
                ApplyLink(link);
            }
        }
        catch
        {
        }
    }

    private void OnCoreEvent(string name, System.Text.Json.JsonElement data)
    {
        if (name != "link")
        {
            return;
        }
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () => ApplyLink(data));
    }

    private void ApplyLink(System.Text.Json.JsonElement link)
    {
        string? state = link.TryGetProperty("state", out System.Text.Json.JsonElement s)
            ? s.GetString()
            : null;
        string? qr = link.TryGetProperty("qr", out System.Text.Json.JsonElement q)
            && q.ValueKind == System.Text.Json.JsonValueKind.String
                ? q.GetString()
                : null;
        switch (state)
        {
            case "unlinked" when qr != null:
                StatusText.Text = "Scan this code with WhatsApp on your phone.";
                Working.IsActive = false;
                Working.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                QrHost.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                QrSteps.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                if (qr != _shownQr)
                {
                    _shownQr = qr;
                    RenderQr(qr);
                }
                break;
            case "connecting":
                StatusText.Text = "Code accepted. Connecting...";
                break;
            case "disconnected":
                string reason = link.TryGetProperty("reason", out System.Text.Json.JsonElement r)
                    ? r.GetString() ?? ""
                    : "";
                StatusText.Text = "Connection dropped (" + reason + "). Reconnecting...";
                break;
            case "failed":
                string error = link.TryGetProperty("error", out System.Text.Json.JsonElement er)
                    ? er.GetString() ?? ""
                    : "";
                StatusText.Text = "Connection failed: " + error;
                break;
            default:
                StatusText.Text = "Connecting to WhatsApp...";
                break;
        }
    }

    private void RenderQr(string payload)
    {
        using (var generator = new QRCodeGenerator())
        using (QRCodeData data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q))
        using (var code = new PngByteQRCode(data))
        {
            byte[] png = code.GetGraphic(8, new byte[] { 17, 17, 17 }, new byte[] { 255, 255, 255 }, false);
            var bitmap = new BitmapImage();
            using (var stream = new System.IO.MemoryStream(png))
            {
                bitmap.SetSource(stream.AsRandomAccessStream());
            }
            QrImage.Source = bitmap;
        }
    }
}
