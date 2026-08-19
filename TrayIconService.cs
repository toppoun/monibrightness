using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MoniBrightness;

public sealed class TrayIconService : IDisposable
{
    public event EventHandler? FlyoutRequested;
    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;

    private const int GWLP_WNDPROC = -4;

    private const uint WM_APP = 0x8000;
    private const uint WM_TRAYICON = WM_APP + 1;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_NULL = 0x0000;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const uint LR_DEFAULTSIZE = 0x00000040;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;

    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_NONOTIFY = 0x0080;
    private const uint TPM_RETURNCMD = 0x0100;

    private const uint MENU_OPEN = 1;
    private const uint MENU_EXIT = 2;

    private const uint TRAY_ID = 1;

    private readonly IntPtr _hwnd;
    private readonly WndProcDelegate _wndProc;

    private IntPtr _oldWndProc;
    private IntPtr _iconHandle;

    private NOTIFYICONDATA _notifyIconData;

    private bool _disposed;

    public TrayIconService(
        Window window,
        string tooltip)
    {
        _hwnd =
            WinRT.Interop.WindowNative.GetWindowHandle(
                window);

        _wndProc = WndProc;

        _oldWndProc =
            SetWindowLongPtrW(
                _hwnd,
                GWLP_WNDPROC,
                Marshal.GetFunctionPointerForDelegate(
                    _wndProc));

        string iconPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "AppIcon.ico");

        _iconHandle =
            LoadImageW(
                IntPtr.Zero,
                iconPath,
                IMAGE_ICON,
                0,
                0,
                LR_LOADFROMFILE |
                LR_DEFAULTSIZE);

        if (_iconHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Could not load tray icon: {iconPath}");
        }

        _notifyIconData =
            new NOTIFYICONDATA
            {
                cbSize =
                    (uint)Marshal.SizeOf<
                        NOTIFYICONDATA>(),

                hWnd = _hwnd,
                uID = TRAY_ID,

                uFlags =
                    NIF_MESSAGE |
                    NIF_ICON |
                    NIF_TIP,

                uCallbackMessage =
                    WM_TRAYICON,

                hIcon =
                    _iconHandle,

                szTip =
                    tooltip,

                szInfo = "",
                szInfoTitle = ""
            };

        if (!Shell_NotifyIconW(
                NIM_ADD,
                ref _notifyIconData))
        {
            throw new InvalidOperationException(
                "Could not create tray icon.");
        }
    }

    private IntPtr WndProc(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (message == WM_TRAYICON)
        {
            uint mouseMessage =
                unchecked(
                    (uint)lParam.ToInt64());

            if (mouseMessage ==
                WM_LBUTTONUP)
            {
                FlyoutRequested?.Invoke(
                    this,
                    EventArgs.Empty);

                return IntPtr.Zero;
            }

            if (mouseMessage ==
                WM_RBUTTONUP)
            {
                ShowContextMenu();

                return IntPtr.Zero;
            }
        }

        return CallWindowProcW(
            _oldWndProc,
            hwnd,
            message,
            wParam,
            lParam);
    }

    private void ShowContextMenu()
    {
        IntPtr menu =
            CreatePopupMenu();

        if (menu == IntPtr.Zero)
            return;

        AppendMenuW(
            menu,
            MF_STRING,
            MENU_OPEN,
            "Open");

        AppendMenuW(
            menu,
            MF_SEPARATOR,
            0,
            null);

        AppendMenuW(
            menu,
            MF_STRING,
            MENU_EXIT,
            "Exit");

        GetCursorPos(
            out POINT point);

        SetForegroundWindow(
            _hwnd);

        uint command =
            TrackPopupMenuEx(
                menu,
                TPM_RIGHTBUTTON |
                TPM_NONOTIFY |
                TPM_RETURNCMD,
                point.X,
                point.Y,
                _hwnd,
                IntPtr.Zero);

        DestroyMenu(menu);

        PostMessageW(
            _hwnd,
            WM_NULL,
            IntPtr.Zero,
            IntPtr.Zero);

        if (command == MENU_OPEN)
        {
            OpenRequested?.Invoke(
                this,
                EventArgs.Empty);
        }
        else if (command == MENU_EXIT)
        {
            ExitRequested?.Invoke(
                this,
                EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        Shell_NotifyIconW(
            NIM_DELETE,
            ref _notifyIconData);

        if (_oldWndProc != IntPtr.Zero)
        {
            SetWindowLongPtrW(
                _hwnd,
                GWLP_WNDPROC,
                _oldWndProc);

            _oldWndProc =
                IntPtr.Zero;
        }

        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(
                _iconHandle);

            _iconHandle =
                IntPtr.Zero;
        }
    }

    private delegate IntPtr WndProcDelegate(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;

        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport(
        "shell32.dll",
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(
        uint message,
        ref NOTIFYICONDATA data);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowLongPtrW",
        SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(
        IntPtr hwnd,
        int index,
        IntPtr newValue);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern IntPtr CallWindowProcW(
        IntPtr previousWndProc,
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(
        IntPtr instance,
        string name,
        uint type,
        int width,
        int height,
        uint load);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(
        IntPtr icon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(
        IntPtr menu,
        uint flags,
        uint id,
        string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(
        IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(
        out POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(
        IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        IntPtr hwnd,
        IntPtr parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);
}