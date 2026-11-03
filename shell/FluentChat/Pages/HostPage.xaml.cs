using FluentChat.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FluentChat.Pages;

/// <summary>Watches the core's link state and swaps login vs. main UI.</summary>
public sealed partial class HostPage : Page
{
    private CoreClient? _core;

    public HostPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _core = (CoreClient)e.Parameter;
        HostFrame.Navigate(typeof(LoginPage), _core);
        _core.CoreEvent += OnCoreEvent;
        _ = _core.RequestAsync("getState");
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_core != null)
        {
            _core.CoreEvent -= OnCoreEvent;
        }
        base.OnNavigatedFrom(e);
    }

    private void OnCoreEvent(string name, System.Text.Json.JsonElement data)
    {
        if (name != "link")
        {
            return;
        }
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            string? state = data.TryGetProperty("state", out System.Text.Json.JsonElement s)
                ? s.GetString()
                : null;
            System.Type target = state == "connected" ? typeof(MainPage) : typeof(LoginPage);
            if (HostFrame.CurrentSourcePageType != target)
            {
                HostFrame.Navigate(target, _core);
            }
        });
    }
}
