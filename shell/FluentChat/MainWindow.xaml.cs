using System;
using FluentChat.Core;
using FluentChat.Pages;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FluentChat;

public sealed partial class MainWindow : Window
{
    private readonly CoreClient? _core;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Fluent Chat";
        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDrag);

        // Test hook for the VM matrix: force an RTL layout without an Arabic
        // display language (verifies mirroring; Arabic shaping is separate).
        if (Environment.GetEnvironmentVariable("FLUENTCHAT_RTL") != null)
        {
            RootFrame.FlowDirection = FlowDirection.RightToLeft;
        }

        _core = CoreClient.Start();
        RootFrame.Navigate(_core == null ? typeof(Pages.CoreMissingPage) : typeof(Pages.HostPage), _core);
    }
}
