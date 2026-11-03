using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FluentChat.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace FluentChat.Pages;

public sealed partial class ChatPage : Page
{
    private readonly ObservableCollection<Message> _messages = new();
    private ChatOpenArgs? _args;
    private bool _composing;

    public string Title { get; private set; } = "";

    public ChatPage()
    {
        InitializeComponent();
        MessageList.ItemsSource = _messages;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _args = (ChatOpenArgs)e.Parameter;
        Title = _args.Chat.Name;
        HeaderPicture.DisplayName = Title;
        Subtitle.Text = _args.Chat.Kind == "group"
            ? (_args.Chat.Participants?.Length.ToString() ?? "0") + " members"
            : "";
        Bindings.Update();
        _args.Core.CoreEvent += OnCoreEvent;
        _ = _args.Core.RequestAsync("markRead", new { chat = _args.Chat.Id, receipts = true });
        try
        {
            System.Text.Json.JsonElement result = await _args.Core.RequestAsync(
                "loadChat", new { chat = _args.Chat.Id });
            MessagePage? page = System.Text.Json.JsonSerializer.Deserialize<MessagePage>(result.GetRawText());
            if (page != null)
            {
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                {
                    _messages.Clear();
                    foreach (Message message in page.Messages)
                    {
                        _messages.Add(message);
                    }
                    ScrollToEnd();
                });
            }
        }
        catch
        {
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_args != null)
        {
            _args.Core.CoreEvent -= OnCoreEvent;
            SetComposing(false);
        }
        base.OnNavigatedFrom(e);
    }

    private void ScrollToEnd()
    {
        if (_messages.Count > 0)
        {
            MessageList.ScrollIntoView(_messages[_messages.Count - 1]);
        }
    }

    private void OnCoreEvent(string name, System.Text.Json.JsonElement data)
    {
        if (_args == null)
        {
            return;
        }
        string chatId = _args.Chat.Id;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            switch (name)
            {
                case "messages":
                    if (data.TryGetProperty("chat", out System.Text.Json.JsonElement pc)
                        && pc.GetString() == chatId)
                    {
                        MessagePage? page = System.Text.Json.JsonSerializer.Deserialize<MessagePage>(data.GetRawText());
                        if (page != null && !page.Older)
                        {
                            _messages.Clear();
                            foreach (Message message in page.Messages)
                            {
                                _messages.Add(message);
                            }
                            ScrollToEnd();
                        }
                    }
                    break;
                case "messageUpdated":
                    Message? updated = System.Text.Json.JsonSerializer.Deserialize<Message>(data.GetRawText());
                    if (updated != null && updated.Chat == chatId)
                    {
                        for (int i = 0; i < _messages.Count; i++)
                        {
                            if (_messages[i].Id == updated.Id)
                            {
                                _messages[i] = updated;
                                return;
                            }
                        }
                        _messages.Add(updated);
                        ScrollToEnd();
                    }
                    break;
                case "messageDeleted":
                    if (data.TryGetProperty("chat", out System.Text.Json.JsonElement dc)
                        && dc.GetString() == chatId
                        && data.TryGetProperty("id", out System.Text.Json.JsonElement di))
                    {
                        string? id = di.GetString();
                        for (int i = 0; i < _messages.Count; i++)
                        {
                            if (_messages[i].Id == id)
                            {
                                _messages.RemoveAt(i);
                                break;
                            }
                        }
                    }
                    break;
                case "typing":
                    if (data.TryGetProperty("chat", out System.Text.Json.JsonElement tc)
                        && tc.GetString() == chatId)
                    {
                        bool active = data.TryGetProperty("activity", out System.Text.Json.JsonElement ta)
                            && ta.ValueKind == System.Text.Json.JsonValueKind.String;
                        Subtitle.Text = active ? "typing..." : "";
                    }
                    break;
            }
        });
    }

    private void SetComposing(bool composing)
    {
        if (_args != null && composing != _composing)
        {
            _composing = composing;
            _ = _args.Core.RequestAsync("setComposing", new { chat = _args.Chat.Id, composing });
        }
    }

    private void OnComposerChanged(object sender, TextChangedEventArgs e)
    {
        if (SendButton != null) SendButton.IsEnabled = !string.IsNullOrWhiteSpace(Composer.Text);
        SetComposing(Composer.Text.Length > 0);
    }

    private void OnComposerKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            Send();
        }
    }

    private void OnSend(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Send();

    private void Send()
    {
        if (_args == null)
        {
            return;
        }
        string text = Composer.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }
        Composer.Text = "";
        SetComposing(false);
        _ = _args.Core.RequestAsync("sendText", new { chat = _args.Chat.Id, text });
    }
}
