using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using OptimizingSoftware.Core.Diagnostics;

namespace OptimizingSoftware.Windows;

[SupportedOSPlatform("windows")]
internal static partial class SettingsReader
{
    /// <summary>HwSchMode : 2 = activée, 1 = désactivée, absent = non pris en charge ou inconnu.</summary>
    public static bool? ReadHardwareGpuScheduling()
    {
        var value = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", null);
        return value is int mode ? mode == 2 : null;
    }

    public static PowerPlanInfo? ReadPowerPlan()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var guidPtr) != 0)
            return null;
        try
        {
            var id = Marshal.PtrToStructure<Guid>(guidPtr);
            return new PowerPlanInfo(id, ReadFriendlyName(id));
        }
        finally
        {
            LocalFree(guidPtr);
        }
    }

    private static string? ReadFriendlyName(Guid scheme)
    {
        uint size = 0;
        if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size) != 0 || size == 0)
            return null;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            return PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
                ? Marshal.PtrToStringUni(buffer)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid,
        IntPtr subGroupGuid, IntPtr powerSettingGuid, IntPtr buffer, ref uint bufferSize);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr mem);
}
