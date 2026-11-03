using FluentChat.Core;
using System.Text.Json;
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
        _core.CoreEvent += OnCoreEvent;
        HostFrame.Navigate(typeof(LoginPage), _core);
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
        try
        {
            JsonElement state = await _core!.RequestAsync("getState");
            if (state.TryGetProperty("link", out JsonElement link))
            {
                ApplyLink(link);
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Initial core state failed: " + ex.Message);
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

    private void ApplyLink(JsonElement data)
    {
        string? state = data.TryGetProperty("state", out JsonElement s) ? s.GetString() : null;
        System.Type target = state == "connected" ? typeof(MainPage) : typeof(LoginPage);
        if (HostFrame.CurrentSourcePageType != target)
        {
            HostFrame.Navigate(target, _core);
        }
    }
}
