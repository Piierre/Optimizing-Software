using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OptimizingSoftware.Windows;

/// <summary>
/// NVIDIA Management Library : température et utilisation des cartes NVIDIA, sans droits administrateur.
/// nvml.dll est installé avec le pilote NVIDIA. Sur un PC sans carte NVIDIA, tout renvoie null.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class Nvml : IDisposable
{
    private readonly IntPtr _device;
    private readonly bool _ready;

    public Nvml()
    {
        try
        {
            _ready = nvmlInit_v2() == 0 && nvmlDeviceGetHandleByIndex_v2(0, out _device) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _ready = false;
        }
    }

    public bool IsAvailable => _ready;

    public double? TemperatureC() =>
        _ready && nvmlDeviceGetTemperature(_device, 0, out var t) == 0 ? t : null;

    public double? UtilizationPercent() =>
        _ready && nvmlDeviceGetUtilizationRates(_device, out var u) == 0 ? u.Gpu : null;

    public void Dispose()
    {
        if (_ready) nvmlShutdown();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [LibraryImport("nvml.dll")]
    private static partial int nvmlInit_v2();

    [LibraryImport("nvml.dll")]
    private static partial int nvmlShutdown();

    [LibraryImport("nvml.dll")]
    private static partial int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

    [LibraryImport("nvml.dll")]
    private static partial int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temperature);

    [LibraryImport("nvml.dll")]
    private static partial int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);
}
