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
    private CoreClient? _core;
    private bool _closing;
    private int _restartAttempts;

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

        StartCore();
        Closed += (_, _) =>
        {
            _closing = true;
            _core?.Dispose();
        };
    }
    private void StartCore()
    {
        _core = CoreClient.Start();
        if (_core != null) _core.CoreExited += OnCoreExited;
        RootFrame.Navigate(_core == null ? typeof(Pages.CoreMissingPage) : typeof(Pages.HostPage), _core);
    }

    private void OnCoreExited(int exitCode)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closing) return;
            var previous = _core;
            if (previous != null) previous.CoreExited -= OnCoreExited;
            previous?.Dispose();
            _core = null;
            // Bound retries for a corrupt binary instead of creating a tight
            // respawn loop. A normal window close never restarts the daemon.
            if (++_restartAttempts <= 3)
            {
                StartCore();
            }
            else
            {
                RootFrame.Navigate(typeof(Pages.CoreMissingPage));
            }
        });
    }

}
