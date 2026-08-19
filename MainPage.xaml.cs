using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Windows.ApplicationModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MoniBrightness;

using TextBox = Microsoft.UI.Xaml.Controls.TextBox;
using Button = Microsoft.UI.Xaml.Controls.Button;


public sealed partial class MainPage
    : Page, INotifyPropertyChanged
{
    private readonly Task _initializeTask;
    public ObservableCollection<MonitorDevice> Monitors
    {
        get;
    } = new();

    private MonitorDevice? _selectedMonitor;

    public MonitorDevice? SelectedMonitor
    {
        get => _selectedMonitor;

        private set
        {
            if (ReferenceEquals(
                    _selectedMonitor,
                    value))
            {
                return;
            }

            _selectedMonitor = value;

            OnPropertyChanged();
        }
    }

    public ObservableCollection<MonitorPreset> Presets
    {
        get;
    } = new();

    private readonly PresetService _presetService =
        new();

    private readonly MonitorNameService _monitorNameService =
        new();

    private bool _applyingPreset;

    private readonly MonitorService _monitorService =
        new();

    private readonly Dictionary<
        IntPtr,
        CancellationTokenSource>
        _brightnessWrites = new();

    private readonly Dictionary<
        IntPtr,
        CancellationTokenSource>
        _contrastWrites = new();

    public MainPage()
    {
        InitializeComponent();

        foreach (MonitorPreset preset in _presetService.Load())
        {
            Presets.Add(preset);
        }

        _initializeTask = InitializeAsync();

        Unloaded += MainPage_Unloaded;
    }

    private async Task InitializeAsync()
    {
        var monitors = await Task.Run(
            () => _monitorService.Enumerate());

        foreach (var monitor in monitors)
        {
            if (Monitors.Count >= 4)
                break;

            if (monitor.Id is not null)
            {
                monitor.CustomName =
                    _monitorNameService.GetName(
                        monitor.Id);
            }

            Monitors.Add(monitor);
        }

        SelectedMonitor =
            Monitors.FirstOrDefault(
                monitor => monitor.IsPrimary)
            ?? Monitors.FirstOrDefault();

        RenderMonitorLayout();

        await RefreshStartupStateAsync();
    }

    private void MonitorLayoutCanvas_SizeChanged(
    object sender,
    SizeChangedEventArgs e)
    {
        RenderMonitorLayout();
    }

    private void RenderMonitorLayout()
    {
        if (Monitors.Count == 0)
            return;

        double canvasWidth =
            MonitorLayoutCanvas.ActualWidth;

        double canvasHeight =
            MonitorLayoutCanvas.ActualHeight;

        if (canvasWidth <= 0 ||
            canvasHeight <= 0)
        {
            return;
        }

        MonitorLayoutCanvas.Children.Clear();

        int minX =
            Monitors.Min(
                monitor => monitor.X);

        int minY =
            Monitors.Min(
                monitor => monitor.Y);

        int maxX =
            Monitors.Max(
                monitor =>
                    monitor.X +
                    monitor.Width);

        int maxY =
            Monitors.Max(
                monitor =>
                    monitor.Y +
                    monitor.Height);

        int desktopWidth =
            maxX - minX;

        int desktopHeight =
            maxY - minY;

        if (desktopWidth <= 0 ||
            desktopHeight <= 0)
        {
            return;
        }

        const double padding = 12;

        double availableWidth =
            Math.Max(
                1,
                canvasWidth -
                padding * 2);

        double availableHeight =
            Math.Max(
                1,
                canvasHeight -
                padding * 2);

        double scale =
            Math.Min(
                availableWidth /
                desktopWidth,

                availableHeight /
                desktopHeight);

        double renderedWidth =
            desktopWidth *
            scale;

        double renderedHeight =
            desktopHeight *
            scale;

        double offsetX =
            (canvasWidth -
             renderedWidth) / 2;

        double offsetY =
            (canvasHeight -
             renderedHeight) / 2;

        foreach (MonitorDevice monitor
                 in Monitors)
        {
            double x =
                offsetX +
                (monitor.X - minX) *
                scale;

            double y =
                offsetY +
                (monitor.Y - minY) *
                scale;

            double width =
                monitor.Width *
                scale;

            double height =
                monitor.Height *
                scale;

            // モニター同士が接していても
            // 別のディスプレイだと分かるように少し隙間を作る。
            const double gap = 4;

            x += gap / 2;
            y += gap / 2;

            width =
                Math.Max(
                    40,
                    width - gap);

            height =
                Math.Max(
                    32,
                    height - gap);

            bool selected =
                ReferenceEquals(
                    monitor,
                    SelectedMonitor);

            var label =
                new TextBlock
                {
                    Text =
                        monitor.DisplayName,

                    FontSize = 13,

                    FontWeight =
                        Microsoft.UI.Text
                            .FontWeights
                            .SemiBold,

                    TextTrimming =
                        TextTrimming
                            .CharacterEllipsis,

                    HorizontalAlignment =
                        HorizontalAlignment.Center,

                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            var button =
                new Button
                {
                    Tag = monitor,

                    Width = width,
                    Height = height,

                    Padding =
                        new Thickness(8),

                    CornerRadius =
                        new CornerRadius(6),

                    HorizontalContentAlignment =
                        HorizontalAlignment.Center,

                    VerticalContentAlignment =
                        VerticalAlignment.Center,

                    Content = label
                };

            if (selected)
            {
                var accentBrush =
                    Application.Current.Resources[
                        "AccentFillColorDefaultBrush"]
                    as Microsoft.UI.Xaml.Media.Brush;

                var accentForeground =
                    Application.Current.Resources[
                        "TextOnAccentFillColorPrimaryBrush"]
                    as Microsoft.UI.Xaml.Media.Brush;

                button.Background = accentBrush;
                button.Foreground = accentForeground;

                button.Resources[
                    "ButtonBackgroundPointerOver"] =
                    accentBrush;

                button.Resources[
                    "ButtonBackgroundPressed"] =
                    accentBrush;

                button.Resources[
                    "ButtonForegroundPointerOver"] =
                    accentForeground;

                button.Resources[
                    "ButtonForegroundPressed"] =
                    accentForeground;

            }
            else
            {
                button.Background =
                    Application.Current.Resources[
                        "CardBackgroundFillColorDefaultBrush"]
                    as Microsoft.UI.Xaml.Media.Brush;

                button.BorderBrush =
                    Application.Current.Resources[
                        "CardStrokeColorDefaultBrush"]
                    as Microsoft.UI.Xaml.Media.Brush;
            }

            button.Click +=
                MonitorLayoutButton_Click;

            Canvas.SetLeft(
                button,
                x);

            Canvas.SetTop(
                button,
                y);

            MonitorLayoutCanvas.Children.Add(
                button);
        }
    }

    private void MonitorLayoutButton_Click(
    object sender,
    RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        if (button.Tag
            is not MonitorDevice monitor)
        {
            return;
        }

        SelectedMonitor = monitor;

        RenderMonitorLayout();
    }

    public Task EnsureInitializedAsync()
    {
        return _initializeTask;
    }
    public void QueueBrightnessFromUi(
    MonitorDevice monitor)
    {
        if (_applyingPreset)
            return;

        QueueBrightnessWrite(monitor);
    }

    public void QueueContrastFromUi(
        MonitorDevice monitor)
    {
        if (_applyingPreset)
            return;

        QueueContrastWrite(monitor);
    }

    public Task ApplyPresetFromUiAsync(
        MonitorPreset preset)
    {
        return ApplyPresetAsync(preset);
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

        QueueBrightnessFromUi(monitor);
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

        QueueContrastFromUi(monitor);
    }

    private void QueueBrightnessWrite(
        MonitorDevice monitor)
    {
        if (_brightnessWrites.TryGetValue(
                monitor.Handle,
                out var old))
        {
            old.Cancel();
        }

        var cts =
            new CancellationTokenSource();

        _brightnessWrites[monitor.Handle] = cts;

        double value = monitor.Brightness;

        _ = WriteBrightnessAsync(
            monitor,
            value,
            cts);
    }

    private async Task WriteBrightnessAsync(
        MonitorDevice monitor,
        double value,
        CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(
                120,
                cts.Token);

            await Task.Run(
                () => _monitorService.SetBrightness(
                    monitor,
                    value),
                cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_brightnessWrites.TryGetValue(
                    monitor.Handle,
                    out var current)
                && ReferenceEquals(current, cts))
            {
                _brightnessWrites.Remove(
                    monitor.Handle);
            }

            cts.Dispose();
        }
    }

    private void QueueContrastWrite(
        MonitorDevice monitor)
    {
        if (_contrastWrites.TryGetValue(
                monitor.Handle,
                out var old))
        {
            old.Cancel();
        }

        var cts =
            new CancellationTokenSource();

        _contrastWrites[monitor.Handle] = cts;

        double value = monitor.Contrast;

        _ = WriteContrastAsync(
            monitor,
            value,
            cts);
    }

    private async Task WriteContrastAsync(
        MonitorDevice monitor,
        double value,
        CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(
                120,
                cts.Token);

            await Task.Run(
                () => _monitorService.SetContrast(
                    monitor,
                    value),
                cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_contrastWrites.TryGetValue(
                    monitor.Handle,
                    out var current)
                && ReferenceEquals(current, cts))
            {
                _contrastWrites.Remove(
                    monitor.Handle);
            }

            cts.Dispose();
        }
    }

    private void MainPage_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        foreach (var cts in _brightnessWrites.Values)
            cts.Cancel();

        foreach (var cts in _contrastWrites.Values)
            cts.Cancel();

        _monitorService.Dispose();
    }
    private async void NewPreset_Click(
    object sender,
    RoutedEventArgs e)
    {
        var input = new TextBox
        {
            PlaceholderText = "Night"
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "New preset",
            Content = input,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        ContentDialogResult result =
            await dialog.ShowAsync();

        if (result != ContentDialogResult.Primary)
            return;

        string name = input.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return;

        MonitorPreset preset =
            _presetService.Capture(
                name,
                Monitors);

        MonitorPreset? oldPreset =
            Presets.FirstOrDefault(
                x => string.Equals(
                    x.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase));

        if (oldPreset is not null)
        {
            int index = Presets.IndexOf(oldPreset);

            Presets[index] = preset;
        }
        else
        {
            Presets.Add(preset);
        }

        _presetService.Save(Presets);
    }
    private async void PresetButton_Click(
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

        await ApplyPresetAsync(preset);
    }
    private async Task ApplyPresetAsync(
        MonitorPreset preset)
    {
        CancelPendingWrites();

        _applyingPreset = true;

        try
        {
            foreach (MonitorPresetEntry entry
                     in preset.Monitors)
            {
                MonitorDevice? monitor =
                    FindMonitor(entry);

                if (monitor is null)
                    continue;

                double brightness =
                    Math.Clamp(
                        entry.Brightness,
                        0,
                        100);

                double contrast =
                    Math.Clamp(
                        entry.Contrast,
                        0,
                        100);

                bool brightnessOk =
                    await Task.Run(
                        () => _monitorService.SetBrightness(
                            monitor,
                            brightness));

                if (brightnessOk)
                {
                    monitor.Brightness =
                        brightness;
                }

                await Task.Delay(100);

                bool contrastOk =
                    await Task.Run(
                        () => _monitorService.SetContrast(
                            monitor,
                            contrast));

                if (contrastOk)
                {
                    monitor.Contrast =
                        contrast;
                }

                await Task.Delay(100);
            }
        }
        finally
        {
            _applyingPreset = false;
        }
    }
    private MonitorDevice? FindMonitor(
        MonitorPresetEntry entry)
    {
        return Monitors.FirstOrDefault(
            monitor =>
                monitor.Id is not null &&
                string.Equals(
                    monitor.Id,
                    entry.MonitorId,
                    StringComparison.OrdinalIgnoreCase));
    }
    private void CancelPendingWrites()
    {
        foreach (CancellationTokenSource cts
                 in _brightnessWrites.Values.ToList())
        {
            cts.Cancel();
        }

        foreach (CancellationTokenSource cts
                 in _contrastWrites.Values.ToList())
        {
            cts.Cancel();
        }
    }
    private void DeletePreset_Click(
    object sender,
    RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item)
            return;

        if (item.Tag
            is not MonitorPreset preset)
        {
            return;
        }

        Presets.Remove(preset);

        _presetService.Save(Presets);
    }

    private void UpdatePresetFromCurrent_Click(
    object sender,
    RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item)
            return;

        if (item.Tag is not MonitorPreset preset)
            return;

        int index =
            Presets.IndexOf(preset);

        if (index < 0)
            return;

        MonitorPreset updated =
            _presetService.Capture(
                preset.Name,
                Monitors);

        Presets[index] = updated;

        _presetService.Save(Presets);
    }

    private async void EditPreset_Click(
    object sender,
    RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item)
            return;

        if (item.Tag is not MonitorPreset preset)
            return;

        var nameInput =
            new TextBox
            {
                Text = preset.Name,
                Header = "Name"
            };

        var content =
            new StackPanel
            {
                Spacing = 14,
                Width = 420
            };

        content.Children.Add(nameInput);

        var editors =
            new List<(
                string MonitorId,
                Slider Brightness,
                Slider Contrast)>();

        foreach (MonitorPresetEntry entry
                 in preset.Monitors)
        {
            MonitorDevice? monitor =
                FindMonitor(entry);

            string displayName =
                monitor?.DisplayName
                ?? "Disconnected monitor";

            var brightness =
                new Slider
                {
                    Minimum = 0,
                    Maximum = 100,
                    StepFrequency = 1,
                    Value = entry.Brightness
                };

            var contrast =
                new Slider
                {
                    Minimum = 0,
                    Maximum = 100,
                    StepFrequency = 1,
                    Value = entry.Contrast
                };

            editors.Add(
                (
                    entry.MonitorId,
                    brightness,
                    contrast
                ));

            var monitorPanel =
                new StackPanel
                {
                    Spacing = 6
                };

            monitorPanel.Children.Add(
                new TextBlock
                {
                    Text = displayName,
                    FontWeight =
                        Microsoft.UI.Text.FontWeights.SemiBold
                });

            monitorPanel.Children.Add(
                CreatePresetSliderRow(
                    "Brightness",
                    brightness));

            monitorPanel.Children.Add(
                CreatePresetSliderRow(
                    "Contrast",
                    contrast));

            var border =
                new Border
                {
                    Padding = new Thickness(12),
                    CornerRadius = new CornerRadius(8),
                    Background =
                        (Microsoft.UI.Xaml.Media.Brush)
                        Application.Current.Resources[
                            "CardBackgroundFillColorDefaultBrush"]
                };

            border.Child = monitorPanel;

            content.Children.Add(border);
        }

        var errorText =
            new TextBlock
            {
                Visibility = Visibility.Collapsed
            };

        content.Children.Add(errorText);

        var dialog =
            new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Edit preset",
                Content = content,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton =
                    ContentDialogButton.Primary
            };

        dialog.PrimaryButtonClick +=
            (_, args) =>
            {
                string name =
                    nameInput.Text.Trim();

                if (string.IsNullOrWhiteSpace(name))
                {
                    errorText.Text =
                        "Preset name cannot be empty.";

                    errorText.Visibility =
                        Visibility.Visible;

                    args.Cancel = true;
                    return;
                }

                bool duplicate =
                    Presets.Any(
                        other =>
                            !ReferenceEquals(
                                other,
                                preset) &&
                            string.Equals(
                                other.Name,
                                name,
                                StringComparison.OrdinalIgnoreCase));

                if (duplicate)
                {
                    errorText.Text =
                        "A preset with this name already exists.";

                    errorText.Visibility =
                        Visibility.Visible;

                    args.Cancel = true;
                }
            };

        ContentDialogResult result =
            await dialog.ShowAsync();

        if (result !=
            ContentDialogResult.Primary)
        {
            return;
        }

        var edited =
            new MonitorPreset
            {
                Name =
                    nameInput.Text.Trim()
            };

        foreach (var editor
                 in editors)
        {
            edited.Monitors.Add(
                new MonitorPresetEntry
                {
                    MonitorId =
                        editor.MonitorId,

                    Brightness =
                        editor.Brightness.Value,

                    Contrast =
                        editor.Contrast.Value
                });
        }

        int index =
            Presets.IndexOf(preset);

        if (index < 0)
            return;

        Presets[index] = edited;

        _presetService.Save(Presets);
    }

    private static Grid CreatePresetSliderRow(
    string label,
    Slider slider)
    {
        var grid =
            new Grid
            {
                ColumnSpacing = 10
            };

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(80)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(32)
            });

        var labelText =
            new TextBlock
            {
                Text = label,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        var valueText =
            new TextBlock
            {
                Text =
                    $"{slider.Value:0}",

                HorizontalAlignment =
                    HorizontalAlignment.Right,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        slider.ValueChanged +=
            (_, args) =>
            {
                valueText.Text =
                    $"{args.NewValue:0}";
            };

        Grid.SetColumn(
            labelText,
            0);

        Grid.SetColumn(
            slider,
            1);

        Grid.SetColumn(
            valueText,
            2);

        grid.Children.Add(
            labelText);

        grid.Children.Add(
            slider);

        grid.Children.Add(
            valueText);

        return grid;
    }

    private const string StartupTaskId =
    "MoniBrightnessStartup";

    private bool _updatingStartupToggle;

    private async Task RefreshStartupStateAsync()
    {
        StartupTask task =
            await StartupTask.GetAsync(
                StartupTaskId);

        _updatingStartupToggle = true;

        StartupToggle.IsOn =
            task.State == StartupTaskState.Enabled;

        _updatingStartupToggle = false;
    }
    private async void StartupToggle_Toggled(
        object sender,
        RoutedEventArgs e)
    {
        if (_updatingStartupToggle)
            return;

        StartupTask task =
            await StartupTask.GetAsync(
                StartupTaskId);

        if (StartupToggle.IsOn)
        {
            if (task.State ==
                StartupTaskState.Disabled)
            {
                StartupTaskState result =
                    await task.RequestEnableAsync();

                _updatingStartupToggle = true;

                StartupToggle.IsOn =
                    result ==
                    StartupTaskState.Enabled;

                _updatingStartupToggle = false;
            }
            else if (task.State ==
                     StartupTaskState.DisabledByUser)
            {
                _updatingStartupToggle = true;
                StartupToggle.IsOn = false;
                _updatingStartupToggle = false;

                await ShowStartupDisabledDialog();
            }
            else if (task.State ==
                     StartupTaskState.DisabledByPolicy)
            {
                _updatingStartupToggle = true;
                StartupToggle.IsOn = false;
                _updatingStartupToggle = false;
            }
        }
        else
        {
            if (task.State ==
                StartupTaskState.Enabled)
            {
                task.Disable();
            }
        }
    }
    private async Task ShowStartupDisabledDialog()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Startup is disabled",
            Content =
                "MoniBrightness was disabled in Windows startup settings. " +
                "Enable it again from Settings > Apps > Startup.",
            CloseButtonText = "OK"
        };

        await dialog.ShowAsync();
    }
    private async void RenameMonitor_Click(
    object sender,
    RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        if (button.DataContext
            is not MonitorDevice monitor)
        {
            return;
        }

        if (monitor.Id is null)
            return;

        var input = new TextBox
        {
            Text = monitor.CustomName ?? "",
            PlaceholderText = monitor.SystemName
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Rename monitor",
            Content = input,
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            DefaultButton =
                ContentDialogButton.Primary
        };

        ContentDialogResult result =
            await dialog.ShowAsync();

        if (result ==
            ContentDialogResult.None)
        {
            return;
        }

        if (result ==
            ContentDialogResult.Secondary)
        {
            monitor.CustomName = null;

            _monitorNameService.SetName(
                monitor.Id,
                null);

            return;
        }

        string? newName =
            string.IsNullOrWhiteSpace(
                input.Text)
                ? null
                : input.Text.Trim();

        monitor.CustomName =
            newName;

        _monitorNameService.SetName(
            monitor.Id,
            newName);
    }
    public event PropertyChangedEventHandler?
    PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName]
    string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
    }
}