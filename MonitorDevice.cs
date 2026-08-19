using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MoniBrightness;

public sealed class MonitorDevice : INotifyPropertyChanged
{
    // DDC/CI用。プロセスを跨いでは使わない。
    public IntPtr Handle { get; }

    // 再起動後も同じモニターを識別するためのID。
    // 取得できなかった場合だけnull。
    public string? Id { get; }

    // Windows / モニターから取得した名前。
    public string SystemName { get; }

    // Windowsの仮想デスクトップ上での位置とサイズ。
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    // Windowsのメインディスプレイか。
    public bool IsPrimary { get; }

    private string? _customName;

    // 後でユーザーが "Main" などを設定する。
    public string? CustomName
    {
        get => _customName;
        set
        {
            string? normalized =
                string.IsNullOrWhiteSpace(value)
                    ? null
                    : value.Trim();

            if (_customName == normalized)
                return;

            _customName = normalized;

            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    // UIは今後これだけ見る。
    public string DisplayName =>
        CustomName ?? SystemName;

    internal uint BrightnessMin { get; }
    internal uint BrightnessMax { get; }
    internal uint ContrastMin { get; }
    internal uint ContrastMax { get; }

    private double _brightness;
    private double _contrast;

    public double Brightness
    {
        get => _brightness;
        set
        {
            value = Math.Clamp(value, 0, 100);

            if (Math.Abs(_brightness - value) < 0.01)
                return;

            _brightness = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(BrightnessText));
        }
    }

    public double Contrast
    {
        get => _contrast;
        set
        {
            value = Math.Clamp(value, 0, 100);

            if (Math.Abs(_contrast - value) < 0.01)
                return;

            _contrast = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(ContrastText));
        }
    }

    public string BrightnessText => $"{Brightness:0}";
    public string ContrastText => $"{Contrast:0}";

    public MonitorDevice(
        IntPtr handle,
        string? id,
        string systemName,
        int x,
        int y,
        int width,
        int height,
        bool isPrimary,
        uint brightnessMin,
        uint brightnessMax,
        double brightness,
        uint contrastMin,
        uint contrastMax,
        double contrast)
    {
        Handle = handle;
        Id = id;
        SystemName = systemName;

        BrightnessMin = brightnessMin;
        BrightnessMax = brightnessMax;
        ContrastMin = contrastMin;
        ContrastMax = contrastMax;

        _brightness = brightness;
        _contrast = contrast;

        Handle = handle;
        Id = id;
        SystemName = systemName;

        X = x;
        Y = y;
        Width = width;
        Height = height;
        IsPrimary = isPrimary;

        BrightnessMin = brightnessMin;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}