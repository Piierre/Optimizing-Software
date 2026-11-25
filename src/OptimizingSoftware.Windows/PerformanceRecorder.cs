using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OptimizingSoftware.Core.Recording;

namespace OptimizingSoftware.Windows;

/// <summary>
/// Enregistre une mesure par seconde : processeur (total et cœur le plus chargé), carte graphique,
/// RAM, disque le plus occupé, et température de la carte graphique (NVIDIA). Lecture seule.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class PerformanceRecorder : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan GpuInstancesRefresh = TimeSpan.FromSeconds(10);

    private readonly List<PerformanceSample> _samples = [];
    private readonly Lock _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>Déclenché après chaque mesure (sur un thread d'arrière-plan).</summary>
    public event Action<PerformanceSample>? SampleTaken;

    public bool IsRecording => _loop is { IsCompleted: false };

    public void Start()
    {
        if (IsRecording) return;
        lock (_lock) _samples.Clear();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => RunAsync(token), token);
    }

    public async Task<IReadOnlyList<PerformanceSample>> StopAsync()
    {
        if (_cts is null || _loop is null) return [];
        await _cts.CancelAsync();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }
        lock (_lock) return _samples.ToList();
    }

    private async Task RunAsync(CancellationToken token)
    {
        using var sensors = new Sensors();
        sensors.Warmup();
        var clock = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(token))
        {
            var sample = sensors.Read(clock.Elapsed);
            lock (_lock) _samples.Add(sample);
            SampleTaken?.Invoke(sample);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    /// <summary>Les compteurs Windows et NVML. Chaque capteur indisponible est simplement ignoré.</summary>
    private sealed class Sensors : IDisposable
    {
        private readonly PerformanceCounter? _cpuTotal;
        private readonly List<PerformanceCounter> _cores = [];
        private readonly List<PerformanceCounter> _disks = [];
        private readonly Dictionary<string, PerformanceCounter> _gpuEngines = [];
        private readonly Nvml _nvml = new();
        private readonly Stopwatch _sinceGpuRefresh = new();

        public Sensors()
        {
            // « % Processor Utility » correspond à ce qu'affiche le Gestionnaire des tâches (peut dépasser 100 % en turbo).
            _cpuTotal = TryCounter("Processor Information", "% Processor Utility", "_Total");
            foreach (var instance in Instances("Processor Information").Where(i => !i.Contains("_Total")))
                if (TryCounter("Processor Information", "% Processor Utility", instance) is { } c) _cores.Add(c);
            foreach (var instance in Instances("PhysicalDisk").Where(i => i != "_Total"))
                if (TryCounter("PhysicalDisk", "% Idle Time", instance) is { } c) _disks.Add(c);
            if (!_nvml.IsAvailable) RefreshGpuEngines();
        }

        public void Warmup()
        {
            // Le premier NextValue() d'un compteur renvoie toujours 0.
            _cpuTotal?.NextValue();
            foreach (var c in _cores.Concat(_disks).Concat(_gpuEngines.Values)) Safe(c);
        }

        public PerformanceSample Read(TimeSpan at)
        {
            var cpu = Clamp(_cpuTotal is null ? 0 : Safe(_cpuTotal));
            var busiest = _cores.Count == 0 ? cpu : _cores.Max(c => Clamp(Safe(c)));
            var disk = _disks.Count == 0 ? 0 : _disks.Max(c => Clamp(100 - Safe(c)));
            return new PerformanceSample(at, cpu, busiest, ReadGpu(), MemoryLoadPercent(), disk, _nvml.TemperatureC());
        }

        private double? ReadGpu()
        {
            if (_nvml.UtilizationPercent() is { } nv) return nv;

            // Autres marques : somme des moteurs 3D par carte (comme le Gestionnaire des tâches), puis la carte la plus chargée.
            if (_sinceGpuRefresh.Elapsed > GpuInstancesRefresh) RefreshGpuEngines();
            if (_gpuEngines.Count == 0) return null;
            return Clamp(_gpuEngines
                .GroupBy(kv => Luid(kv.Key))
                .Max(g => g.Sum(kv => Safe(kv.Value))));
        }

        private void RefreshGpuEngines()
        {
            _sinceGpuRefresh.Restart();
            var current = Instances("GPU Engine").Where(i => i.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase)).ToHashSet();
            foreach (var gone in _gpuEngines.Keys.Where(k => !current.Contains(k)).ToList())
            {
                _gpuEngines[gone].Dispose();
                _gpuEngines.Remove(gone);
            }
            foreach (var instance in current.Where(i => !_gpuEngines.ContainsKey(i)))
            {
                if (TryCounter("GPU Engine", "Utilization Percentage", instance) is { } c)
                {
                    Safe(c);
                    _gpuEngines[instance] = c;
                }
            }
        }

        /// <summary>« pid_1234_luid_0x00000000_0x0000D1E2_phys_0_eng_0_engtype_3D » → « 0x00000000_0x0000D1E2 ».</summary>
        private static string Luid(string instance)
        {
            var start = instance.IndexOf("luid_", StringComparison.Ordinal);
            var end = instance.IndexOf("_phys", StringComparison.Ordinal);
            return start >= 0 && end > start ? instance[(start + 5)..end] : instance;
        }

        private static string[] Instances(string category)
        {
            try
            {
                return new PerformanceCounterCategory(category).GetInstanceNames();
            }
            catch (Exception)
            {
                return [];
            }
        }

        private static PerformanceCounter? TryCounter(string category, string counter, string instance)
        {
            try
            {
                return new PerformanceCounter(category, counter, instance, readOnly: true);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Un compteur peut disparaître (processus fermé) : on renvoie 0 au lieu de planter.</summary>
        private static double Safe(PerformanceCounter counter)
        {
            try
            {
                return counter.NextValue();
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static double Clamp(double percent) => Math.Clamp(percent, 0, 100);

        public void Dispose()
        {
            _cpuTotal?.Dispose();
            foreach (var c in _cores.Concat(_disks).Concat(_gpuEngines.Values)) c.Dispose();
            _nvml.Dispose();
        }
    }

    private static double MemoryLoadPercent()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status.MemoryLoad : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
