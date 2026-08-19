using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics;

namespace MoniBrightness;

public sealed partial class FlyoutWindow : Window
{
    private const int PopupWidth = 390;
    private const int PopupHeight = 520;
    private const int ScreenMargin = 8;

    private readonly MainPage _mainPage;

    public event EventHandler? OpenSettingsRequested;

    public FlyoutWindow(
        MainPage mainPage)
    {
        InitializeComponent();

        _mainPage = mainPage;

        MonitorList.ItemsSource =
            _mainPage.Monitors;

        PresetList.ItemsSource =
            _mainPage.Presets;

        var presenter =
            OverlappedPresenter.Create();

        presenter.SetBorderAndTitleBar(
            false,
            false);

        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;

        AppWindow.SetPresenter(
            presenter);

        AppWindow.IsShownInSwitchers =
            false;

        Activated += FlyoutWindow_Activated;
    }

    public async Task ToggleAtCursorAsync()
    {
        if (AppWindow.IsVisible)
        {
            AppWindow.Hide();
            return;
        }

        await _mainPage
            .EnsureInitializedAsync();

        MoveNearCursor();

        AppWindow.Show();
        Activate();
    }

    private void MoveNearCursor()
    {
        if (!GetCursorPos(
                out POINT cursor))
        {
            return;
        }

        var point =
            new PointInt32(
                cursor.X,
                cursor.Y);

        DisplayArea display =
            DisplayArea.GetFromPoint(
                point,
                DisplayAreaFallback.Nearest);

        RectInt32 outer =
            display.OuterBounds;

        RectInt32 work =
            display.WorkArea;

        // WorkAreaのXYはDisplayArea内での相対位置。
        int workLeft =
            outer.X + work.X;

        int workTop =
            outer.Y + work.Y;

        int workRight =
            workLeft + work.Width;

        int workBottom =
            workTop + work.Height;

        int minX =
            workLeft + ScreenMargin;

        int minY =
            workTop + ScreenMargin;

        int maxX =
            Math.Max(
                minX,
                workRight
                - PopupWidth
                - ScreenMargin);

        int maxY =
            Math.Max(
                minY,
                workBottom
                - PopupHeight
                - ScreenMargin);

        // Trayアイコンの少し左上を狙う。
        int x =
            Math.Clamp(
                cursor.X
                - PopupWidth
                + 20,
                minX,
                maxX);

        int y =
            Math.Clamp(
                cursor.Y
                - PopupHeight
                - 12,
                minY,
                maxY);

        AppWindow.MoveAndResize(
            new RectInt32(
                x,
                y,
                PopupWidth,
                PopupHeight));
    }

    private void FlyoutWindow_Activated(
        object sender,
        WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState ==
            WindowActivationState.Deactivated)
        {
            AppWindow.Hide();
        }
    }

    private void BrightnessSlider_ValueChanged(
        object sender,
        RangeBaseValueChangedEventArgs e)
    {
        if (sender is not Slider slider)
            return;

        if (slider.DataContext
            is not MonitorDevice monitor)
        {
            return;
        }

        _mainPage
            .QueueBrightnessFromUi(
                monitor);
    }

    private void ContrastSlider_ValueChanged(
        object sender,
        RangeBaseValueChangedEventArgs e)
    {
        if (sender is not Slider slider)
            return;

        if (slider.DataContext
            is not MonitorDevice monitor)
        {
            return;
        }

        _mainPage
            .QueueContrastFromUi(
                monitor);
    }

    private async void Preset_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        if (button.DataContext
            is not MonitorPreset preset)
        {
            return;
        }

        await _mainPage
            .ApplyPresetFromUiAsync(
                preset);
    }

    private void OpenSettings_Click(
        object sender,
        RoutedEventArgs e)
    {
        AppWindow.Hide();

        OpenSettingsRequested?.Invoke(
            this,
            EventArgs.Empty);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(
        out POINT point);
}