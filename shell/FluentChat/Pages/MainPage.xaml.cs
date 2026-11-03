using FluentChat.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FluentChat.Pages;

/// <summary>The two rounded layered cards over Mica: chat list + conversation.</summary>
public sealed partial class MainPage : Page
{
    private CoreClient? _core;

    public MainPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _core = (CoreClient)e.Parameter;
        ChatsFrame.Navigate(typeof(ChatListPage), this);
    }

    public CoreClient Core => _core!;

    public void OpenChat(Chat chat)
    {
        ConvoFrame.Navigate(typeof(ChatPage), new ChatOpenArgs(_core!, chat));
    }
}

public sealed class ChatOpenArgs
{
    public ChatOpenArgs(CoreClient core, Chat chat)
    {
        Core = core;
        Chat = chat;
    }

    public CoreClient Core { get; }

    public Chat Chat { get; }
}
