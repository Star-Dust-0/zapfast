using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FluentChat.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FluentChat.Pages;

public sealed partial class ChatListPage : Page
{
    private readonly ObservableCollection<Chat> _visible = new();
    private readonly List<Chat> _all = new();
    private MainPage? _host;
    private string _filter = "all";

    public ChatListPage()
    {
        InitializeComponent();
        ChatList.ItemsSource = _visible;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _host = (MainPage)e.Parameter;
        _host.Core.CoreEvent += OnCoreEvent;
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_host != null)
        {
            _host.Core.CoreEvent -= OnCoreEvent;
        }
        base.OnNavigatedFrom(e);
    }

    private async void Refresh()
    {
        if (_host == null)
        {
            return;
        }
        try
        {
            System.Text.Json.JsonElement result = await _host.Core.RequestAsync("listChats");
            if (result.TryGetProperty("chats", out System.Text.Json.JsonElement chats))
            {
                Chat[]? list = System.Text.Json.JsonSerializer.Deserialize<Chat[]>(chats.GetRawText());
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () => ReplaceAll(list ?? Array.Empty<Chat>()));
            }
        }
        catch
        {
        }
    }

    private void ReplaceAll(Chat[] chats)
    {
        _all.Clear();
        _all.AddRange(chats);
        ApplyFilter();
    }

    private void Upsert(Chat chat)
    {
        int index = _all.FindIndex(c => c.Id == chat.Id);
        if (index >= 0)
        {
            _all[index] = chat;
        }
        else
        {
            _all.Add(chat);
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string query = SearchBox.Text?.Trim() ?? "";
        IEnumerable<Chat> shown = _all.Where(c => !c.Archived);
        shown = _filter switch
        {
            "unread" => shown.Where(c => c.Unread > 0 || c.MarkedUnread),
            "groups" => shown.Where(c => c.Kind == "group"),
            "favorites" => shown.Where(c => c.Favorite),
            _ => shown,
        };
        if (query.Length > 0)
        {
            shown = shown.Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        var sorted = shown
            .OrderByDescending(c => c.Pinned)
            .ThenByDescending(c => c.Pinned ? c.PinnedAt : 0)
            .ThenByDescending(c => c.LastActivity)
            .ToList();
        _visible.Clear();
        foreach (Chat chat in sorted)
        {
            _visible.Add(chat);
        }
    }

    private void OnCoreEvent(string name, System.Text.Json.JsonElement data)
    {
        if (name != "chats" && name != "chatUpdated")
        {
            return;
        }
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            if (name == "chats" && data.TryGetProperty("chats", out System.Text.Json.JsonElement chats))
            {
                Chat[]? list = System.Text.Json.JsonSerializer.Deserialize<Chat[]>(chats.GetRawText());
                ReplaceAll(list ?? Array.Empty<Chat>());
            }
            else if (name == "chatUpdated" && data.TryGetProperty("chat", out System.Text.Json.JsonElement chatEl))
            {
                Chat? chat = System.Text.Json.JsonSerializer.Deserialize<Chat>(chatEl.GetRawText());
                if (chat != null)
                {
                    Upsert(chat);
                }
            }
        });
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnChip(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tag)
        {
            _filter = tag;
            ApplyFilter();
        }
    }

    private void OnChatClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Chat chat)
        {
            _host?.OpenChat(chat);
        }
    }
}
