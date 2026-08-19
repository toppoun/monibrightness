using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.IO;

namespace MoniBrightness;

public sealed partial class MainWindow : Window
{
    private TrayIconService? _trayIcon;
    private FlyoutWindow? _flyoutWindow;
    private MainPage? _mainPage;

    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon(
            Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "AppIcon.ico"));

        RootFrame.Navigate(
            typeof(MainPage));

        _mainPage =
            RootFrame.Content
                as MainPage
            ?? throw new InvalidOperationException(
                "MainPage could not be created.");

        _flyoutWindow =
            new FlyoutWindow(
                _mainPage);

        _flyoutWindow
            .OpenSettingsRequested +=
            (_, _) => ShowFromTray();

        _trayIcon =
            new TrayIconService(
                this,
                "MoniBrightness");

        _trayIcon.FlyoutRequested +=
            async (_, _) =>
            {
                if (_flyoutWindow is not null)
                {
                    await _flyoutWindow
                        .ToggleAtCursorAsync();
                }
            };

        _trayIcon.OpenRequested +=
            (_, _) => ShowFromTray();

        _trayIcon.ExitRequested +=
            (_, _) => ExitFromTray();

        AppWindow.Closing +=
            AppWindow_Closing;
    }

    private void AppWindow_Closing(
        AppWindow sender,
        AppWindowClosingEventArgs args)
    {
        if (_isExiting)
            return;

        args.Cancel = true;

        AppWindow.Hide();
    }

    private void ShowFromTray()
    {
        _flyoutWindow?
            .AppWindow.Hide();

        AppWindow.Show();
        Activate();
    }

    private void ExitFromTray()
    {
        _isExiting = true;

        _flyoutWindow?.Close();
        _flyoutWindow = null;

        _trayIcon?.Dispose();
        _trayIcon = null;

        Close();
    }
}