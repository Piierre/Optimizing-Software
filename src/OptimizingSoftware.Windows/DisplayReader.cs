using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OptimizingSoftware.Core.Diagnostics;

namespace OptimizingSoftware.Windows;

/// <summary>Écrans branchés, fréquence actuelle et fréquence maximale à la résolution actuelle.</summary>
[SupportedOSPlatform("windows")]
internal static class DisplayReader
{
    private const int EnumCurrentSettings = -1;
    private const int AttachedToDesktop = 0x1;
    private const int PrimaryDevice = 0x4;

    public static IReadOnlyList<DisplayInfo> Read()
    {
        var displays = new List<DisplayInfo>();
        var adapter = NewDisplayDevice();
        for (uint i = 0; EnumDisplayDevices(null, i, ref adapter, 0); i++, adapter = NewDisplayDevice())
        {
            if ((adapter.StateFlags & AttachedToDesktop) == 0) continue;

            var current = NewDevMode();
            if (!EnumDisplaySettings(adapter.DeviceName, EnumCurrentSettings, ref current)) continue;

            var max = (int)current.dmDisplayFrequency;
            var mode = NewDevMode();
            for (var m = 0; EnumDisplaySettings(adapter.DeviceName, m, ref mode); m++, mode = NewDevMode())
            {
                if (mode.dmPelsWidth == current.dmPelsWidth && mode.dmPelsHeight == current.dmPelsHeight)
                    max = Math.Max(max, (int)mode.dmDisplayFrequency);
            }

            var isPrimary = (adapter.StateFlags & PrimaryDevice) != 0;
            displays.Add(new DisplayInfo(
                MonitorName(adapter.DeviceName, displays.Count + 1, isPrimary),
                (int)current.dmPelsWidth,
                (int)current.dmPelsHeight,
                (int)current.dmDisplayFrequency,
                max,
                isPrimary));
        }
        return displays;
    }

    private static string MonitorName(string adapterName, int index, bool isPrimary)
    {
        var label = isPrimary ? $"Écran {index} (principal)" : $"Écran {index}";
        var monitor = NewDisplayDevice();
        if (EnumDisplayDevices(adapterName, 0, ref monitor, 0)
            && !string.IsNullOrWhiteSpace(monitor.DeviceString)
            && !monitor.DeviceString.Contains("PnP", StringComparison.OrdinalIgnoreCase))
        {
            return $"{label} {monitor.DeviceString}";
        }
        return label;
    }

    private static DISPLAY_DEVICE NewDisplayDevice() => new() { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };

    private static DEVMODE NewDevMode() => new() { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW")]
    private static extern bool EnumDisplayDevices(string? device, uint devNum, ref DISPLAY_DEVICE displayDevice, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }
}
