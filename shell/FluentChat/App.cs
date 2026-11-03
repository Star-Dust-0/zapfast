using System;
using System.IO;
using System.Threading;
using Microsoft.UI.Xaml;

namespace FluentChat;

public partial class App : Application
{
    private static Mutex? _mutex;

    private MainWindow? _window;

    public static string AppData =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluentChat");

    public App()
    {
        InitializeComponent();
        UnhandledException += delegate (object s, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(AppData);
                File.AppendAllText(
                    Path.Combine(AppData, "crash.log"),
                    DateTime.Now.ToString() + "\n" + e.Exception?.ToString() + "\n\n");
            }
            catch
            {
            }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        bool created;
        _mutex = new Mutex(true, "FluentChat.SingleInstance", out created);
        if (!created)
        {
            // Professional behaviour: one running instance; activating again
            // simply exits the newcomer. (Bringing the first window forward
            // lands with the tray/activation work.)
            Environment.Exit(0);
            return;
        }
        _window = new MainWindow();
        _window.Activate();
    }
}
