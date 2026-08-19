using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace MoniBrightness;

public sealed class MonitorService : IDisposable
{
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;

    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const uint MONITORINFOF_PRIMARY = 0x00000001;

    private readonly List<IntPtr> _handles = new();

    private sealed record DisplayTarget(
        string SourceGdiName,
        string MonitorDevicePath,
        string FriendlyName);

    private sealed record DisplayIdentity(
        string Id,
        string FriendlyName);

    public List<MonitorDevice> Enumerate()
    {
        var result = new List<MonitorDevice>();

        List<DisplayTarget> displayTargets;

        try
        {
            displayTargets =
                QueryDisplayTargets();
        }
        catch
        {
            // Stable ID取得に失敗しても、
            // DDC操作そのものは使えるようにする。
            displayTargets = new();
        }

        MonitorEnumProc callback =
            (hMonitor, _, _, _) =>
            {
                MONITORINFOEX? monitorInfo =
                    GetMonitorInfo(hMonitor);

                if (monitorInfo is null)
                    return true;

                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(
                                hMonitor,
                                out uint count))
                {
                    return true;
                }

                if (count == 0)
                    return true;

                var physicalMonitors =
            new PhysicalMonitor[count];

                if (!GetPhysicalMonitorsFromHMONITOR(
                hMonitor,
                count,
                physicalMonitors))
                {
                    return true;
                }

                string gdiName =
                    monitorInfo.Value.DeviceName
                        .TrimEnd('\0');

                DisplayIdentity?[] identities =
                    ResolveDisplayIdentities(
                        gdiName,
                        physicalMonitors,
                        displayTargets);

                for (int i = 0;
             i < physicalMonitors.Length;
             i++)
                {
                    PhysicalMonitor physical =
                physicalMonitors[i];

                    if (!GetMonitorBrightness(
                    physical.Handle,
                    out uint brightnessMin,
                    out uint brightnessCurrent,
                    out uint brightnessMax))
                    {
                        DestroyPhysicalMonitor(
                    physical.Handle);

                        continue;
                    }

                    if (!GetMonitorContrast(
                    physical.Handle,
                    out uint contrastMin,
                    out uint contrastCurrent,
                    out uint contrastMax))
                    {
                        DestroyPhysicalMonitor(
                    physical.Handle);

                        continue;
                    }

                    _handles.Add(
                physical.Handle);

                    DisplayIdentity? identity =
                identities[i];

                    string physicalDescription =
                string.IsNullOrWhiteSpace(
                    physical.Description)
                    ? $"Monitor {result.Count + 1}"
                    : physical.Description.Trim();

                    // QueryDisplayConfigのfriendly nameを優先。
                    // Generic PnP Monitorよりまともな名前が
                    // 取れる可能性がある。
                    string systemName =
                identity is not null &&
                !string.IsNullOrWhiteSpace(
                    identity.FriendlyName)
                    ? identity.FriendlyName
                    : physicalDescription;

                    RECT bounds =
                        monitorInfo.Value.Monitor;

                    result.Add(
                        new MonitorDevice(
                            physical.Handle,
                            identity?.Id,
                            systemName,

                            bounds.Left,
                            bounds.Top,
                            bounds.Right - bounds.Left,
                            bounds.Bottom - bounds.Top,

                            (monitorInfo.Value.Flags
                                & MONITORINFOF_PRIMARY) != 0,

                            brightnessMin,
                            brightnessMax,
                            RawToPercent(
                                brightnessMin,
                                brightnessCurrent,
                                brightnessMax),
                            contrastMin,
                            contrastMax,
                            RawToPercent(
                                contrastMin,
                                contrastCurrent,
                                contrastMax)));
                }

                return true;
            };

        if (!EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                callback,
                IntPtr.Zero))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error());
        }

        // IDが取れているのに重複していたら、
        // この後Preset/Renameに使うには危険。
        List<string> duplicateIds =
            result
                .Where(
                    monitor =>
                        monitor.Id is not null)
                .GroupBy(
                    monitor => monitor.Id!,
                    StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

        if (duplicateIds.Count > 0)
        {
            throw new InvalidOperationException(
                "Duplicate stable monitor IDs detected:\n" +
                string.Join(
                    "\n",
                    duplicateIds));
        }

        return result;
    }

    private static List<DisplayTarget>
        QueryDisplayTargets()
    {
        // GetDisplayConfigBufferSizesとQueryDisplayConfigの間に
        // ディスプレイ構成が変化すると122が返ることがある。
        // その場合はサイズ取得からやり直す。
        for (int attempt = 0;
             attempt < 3;
             attempt++)
        {
            int status =
                GetDisplayConfigBufferSizes(
                    QDC_ONLY_ACTIVE_PATHS,
                    out uint pathCount,
                    out uint modeCount);

            if (status != 0)
            {
                throw new Win32Exception(
                    status,
                    "GetDisplayConfigBufferSizes failed.");
            }

            var paths =
                new DISPLAYCONFIG_PATH_INFO[
                    pathCount];

            var modes =
                new DISPLAYCONFIG_MODE_INFO[
                    modeCount];

            status =
                QueryDisplayConfig(
                    QDC_ONLY_ACTIVE_PATHS,
                    ref pathCount,
                    paths,
                    ref modeCount,
                    modes,
                    IntPtr.Zero);

            if (status ==
                ERROR_INSUFFICIENT_BUFFER)
            {
                continue;
            }

            if (status != 0)
            {
                throw new Win32Exception(
                    status,
                    "QueryDisplayConfig failed.");
            }

            var targets =
                new List<DisplayTarget>();

            for (int i = 0;
                 i < pathCount;
                 i++)
            {
                DISPLAYCONFIG_PATH_INFO path =
                    paths[i];

                var source =
                    new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                    {
                        Header =
                            new DISPLAYCONFIG_DEVICE_INFO_HEADER
                            {
                                Type =
                                    DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,

                                Size =
                                    (uint)Marshal.SizeOf<
                                        DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),

                                AdapterId =
                                    path.SourceInfo.AdapterId,

                                Id =
                                    path.SourceInfo.Id
                            },

                        ViewGdiDeviceName =
                            string.Empty
                    };

                status =
                    DisplayConfigGetSourceDeviceInfo(
                        ref source);

                if (status != 0)
                    continue;

                var target =
                    new DISPLAYCONFIG_TARGET_DEVICE_NAME
                    {
                        Header =
                            new DISPLAYCONFIG_DEVICE_INFO_HEADER
                            {
                                Type =
                                    DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,

                                Size =
                                    (uint)Marshal.SizeOf<
                                        DISPLAYCONFIG_TARGET_DEVICE_NAME>(),

                                AdapterId =
                                    path.TargetInfo.AdapterId,

                                Id =
                                    path.TargetInfo.Id
                            },

                        MonitorFriendlyDeviceName =
                            string.Empty,

                        MonitorDevicePath =
                            string.Empty
                    };

                status =
                    DisplayConfigGetTargetDeviceInfo(
                        ref target);

                if (status != 0)
                    continue;

                string sourceName =
                    source.ViewGdiDeviceName.TrimEnd(
                        '\0');

                string devicePath =
                    target.MonitorDevicePath
                        .TrimEnd('\0');

                string friendlyName =
                    target.MonitorFriendlyDeviceName
                        .TrimEnd('\0');

                if (string.IsNullOrWhiteSpace(
                        sourceName) ||
                    string.IsNullOrWhiteSpace(
                        devicePath))
                {
                    continue;
                }

                // Windowsのdevice path比較は
                // 大文字小文字を区別しない前提で正規化。
                devicePath =
                    devicePath.ToLowerInvariant();

                targets.Add(
                    new DisplayTarget(
                        sourceName,
                        devicePath,
                        friendlyName));
            }

            return targets;
        }

        throw new InvalidOperationException(
            "Display configuration changed repeatedly while enumerating.");
    }

    private static DisplayIdentity?[]
        ResolveDisplayIdentities(
            string gdiName,
            PhysicalMonitor[] physicalMonitors,
            List<DisplayTarget> targets)
    {
        var result =
            new DisplayIdentity?[
                physicalMonitors.Length];

        List<DisplayTarget> candidates =
            targets
                .Where(
                    target =>
                        string.Equals(
                            target.SourceGdiName,
                            gdiName,
                            StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (candidates.Count == 0)
            return result;

        // 普通の拡張デスクトップ:
        // 1 HMONITOR = 1 physical monitor = 1 target
        if (physicalMonitors.Length == 1 &&
            candidates.Count == 1)
        {
            DisplayTarget target =
                candidates[0];

            result[0] =
                new DisplayIdentity(
                    target.MonitorDevicePath,
                    target.FriendlyName);

            return result;
        }

        // 1 HMONITORに複数physical monitorがいる場合は
        // physical description と friendly name をまず比較。
        bool[] used =
            new bool[candidates.Count];

        for (int physicalIndex = 0;
             physicalIndex < physicalMonitors.Length;
             physicalIndex++)
        {
            string description =
                physicalMonitors[
                    physicalIndex]
                    .Description
                    .Trim();

            if (string.IsNullOrWhiteSpace(
                    description))
            {
                continue;
            }

            List<int> matches =
                Enumerable
                    .Range(
                        0,
                        candidates.Count)
                    .Where(
                        candidateIndex =>
                            !used[candidateIndex] &&
                            !string.IsNullOrWhiteSpace(
                                candidates[
                                    candidateIndex]
                                    .FriendlyName) &&
                            string.Equals(
                                candidates[
                                    candidateIndex]
                                    .FriendlyName,
                                description,
                                StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (matches.Count != 1)
                continue;

            int index =
                matches[0];

            used[index] = true;

            result[physicalIndex] =
                new DisplayIdentity(
                    candidates[index]
                        .MonitorDevicePath,
                    candidates[index]
                        .FriendlyName);
        }

        // 名前で判別できなかった残りについて、
        // unresolved数とunused target数が一致する場合だけ
        // QueryDisplayConfigの順番で対応させる。
        List<int> unresolved =
            Enumerable
                .Range(
                    0,
                    result.Length)
                .Where(
                    index =>
                        result[index] is null)
                .ToList();

        List<int> unused =
            Enumerable
                .Range(
                    0,
                    candidates.Count)
                .Where(
                    index =>
                        !used[index])
                .ToList();

        if (unresolved.Count ==
            unused.Count)
        {
            for (int i = 0;
                 i < unresolved.Count;
                 i++)
            {
                int physicalIndex =
                    unresolved[i];

                int candidateIndex =
                    unused[i];

                DisplayTarget target =
                    candidates[
                        candidateIndex];

                result[physicalIndex] =
                    new DisplayIdentity(
                        target.MonitorDevicePath,
                        target.FriendlyName);
            }
        }

        return result;
    }

    private static MONITORINFOEX?
        GetMonitorInfo(
            IntPtr hMonitor)
    {
        var info =
            new MONITORINFOEX
            {
                CbSize =
                    (uint)Marshal.SizeOf<
                        MONITORINFOEX>(),

                DeviceName =
                    string.Empty
            };

        if (!GetMonitorInfoW(
                hMonitor,
                ref info))
        {
            return null;
        }

        return info;
    }

    public bool SetBrightness(
        MonitorDevice monitor,
        double percent)
    {
        uint value =
            PercentToRaw(
                percent,
                monitor.BrightnessMin,
                monitor.BrightnessMax);

        return SetMonitorBrightness(
            monitor.Handle,
            value);
    }

    public bool SetContrast(
        MonitorDevice monitor,
        double percent)
    {
        uint value =
            PercentToRaw(
                percent,
                monitor.ContrastMin,
                monitor.ContrastMax);

        return SetMonitorContrast(
            monitor.Handle,
            value);
    }

    private static double RawToPercent(
        uint min,
        uint current,
        uint max)
    {
        if (max == min)
            return 0;

        return (current - min)
            * 100.0
            / (max - min);
    }

    private static uint PercentToRaw(
        double percent,
        uint min,
        uint max)
    {
        percent =
            Math.Clamp(
                percent,
                0,
                100);

        return min
            + (uint)Math.Round(
                (max - min)
                * percent
                / 100.0);
    }

    public void Dispose()
    {
        foreach (IntPtr handle
                 in _handles)
        {
            DestroyPhysicalMonitor(
                handle);
        }

        _handles.Clear();
    }

    private delegate bool MonitorEnumProc(
        IntPtr hMonitor,
        IntPtr hdcMonitor,
        IntPtr monitorRect,
        IntPtr data);

    [StructLayout(
        LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public uint CbSize;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 128)]
        public string Description;
    }

    [StructLayout(
        LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(
        LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(
        LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID AdapterId;
        public uint Id;

        // unionの実体は32bitなので、
        // 今回使わない中身はuintとして確保。
        public uint ModeInfoIdx;

        public uint StatusFlags;
    }

    [StructLayout(
        LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DISPLAYCONFIG_RATIONAL RefreshRate;
        public uint ScanLineOrdering;

        // Win32 BOOL = 32bit
        public int TargetAvailable;

        public uint StatusFlags;
    }

    [StructLayout(
        LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO
            SourceInfo;

        public DISPLAYCONFIG_PATH_TARGET_INFO
            TargetInfo;

        public uint Flags;
    }

    // 今回mode情報の中身は読まない。
    // Native DISPLAYCONFIG_MODE_INFOの領域だけ確保する。
    [StructLayout(
        LayoutKind.Sequential,
        Size = 64)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint InfoType;
        public uint Id;
        public LUID AdapterId;
    }

    [StructLayout(
        LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint Type;
        public uint Size;
        public LUID AdapterId;
        public uint Id;
    }

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER
            Header;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER
            Header;

        // DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS
        public uint Flags;

        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 64)]
        public string MonitorFriendlyDeviceName;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 128)]
        public string MonitorDevicePath;
    }

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool
        EnumDisplayMonitors(
            IntPtr hdc,
            IntPtr clipRect,
            MonitorEnumProc callback,
            IntPtr data);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool
        GetMonitorInfoW(
            IntPtr monitor,
            ref MONITORINFOEX info);

    [DllImport(
        "user32.dll")]
    private static extern int
        GetDisplayConfigBufferSizes(
            uint flags,
            out uint pathCount,
            out uint modeCount);

    [DllImport(
        "user32.dll")]
    private static extern int
        QueryDisplayConfig(
            uint flags,
            ref uint pathCount,

            [Out]
            DISPLAYCONFIG_PATH_INFO[]
                pathArray,

            ref uint modeCount,

            [Out]
            DISPLAYCONFIG_MODE_INFO[]
                modeArray,

            IntPtr currentTopologyId);

    [DllImport(
        "user32.dll",
        EntryPoint =
            "DisplayConfigGetDeviceInfo",
        CharSet = CharSet.Unicode)]
    private static extern int
        DisplayConfigGetSourceDeviceInfo(
            ref DISPLAYCONFIG_SOURCE_DEVICE_NAME
                request);

    [DllImport(
        "user32.dll",
        EntryPoint =
            "DisplayConfigGetDeviceInfo",
        CharSet = CharSet.Unicode)]
    private static extern int
        DisplayConfigGetTargetDeviceInfo(
            ref DISPLAYCONFIG_TARGET_DEVICE_NAME
                request);

    [DllImport(
        "Dxva2.dll",
        SetLastError = true)]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool
        GetNumberOfPhysicalMonitorsFromHMONITOR(
            IntPtr hMonitor,
            out uint numberOfPhysicalMonitors);

    [DllImport(
        "Dxva2.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool
        GetPhysicalMonitorsFromHMONITOR(
            IntPtr hMonitor,
            uint physicalMonitorArraySize,
            [Out]
            PhysicalMonitor[]
                physicalMonitorArray);

    [DllImport(
        "Dxva2.dll",
        SetLastError = true)]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool
        GetMonitorBrightness(
            IntPtr hMonitor,
            out uint minimumBrightness,
            out uint currentBrightness,
            out uint maximumBrightness);

    [DllImport(
        "Dxva2.dll",
        SetLastError = true)]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool
        SetMonitorBrightness(
            IntPtr hMonitor,
            uint newBrightness);

    [DllImport(
        "Dxva2.dll",
        SetLastError = true)]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool
        GetMonitorContrast(
            IntPtr hMonitor,
            out uint minimumContrast,
            out uint currentContrast,
            out uint maximumContrast);

    [DllImport(
        "Dxva2.dll",
        SetLastError = true)]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool
        SetMonitorContrast(
            IntPtr hMonitor,
            uint newContrast);

    [DllImport(
        "Dxva2.dll",
        SetLastError = true)]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool
        DestroyPhysicalMonitor(
            IntPtr hMonitor);
}