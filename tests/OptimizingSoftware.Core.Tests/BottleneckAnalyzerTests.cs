using OptimizingSoftware.Core.Recording;

namespace OptimizingSoftware.Core.Tests;

public class BottleneckAnalyzerTests
{
    /// <summary>n mesures identiques, une par seconde.</summary>
    private static List<PerformanceSample> Samples(int n, double cpu, double core, double? gpu, double ram = 60, double disk = 10,
        double? gpuTemp = 70) =>
        Enumerable.Range(0, n)
            .Select(i => new PerformanceSample(TimeSpan.FromSeconds(i), cpu, core, gpu, ram, disk, gpuTemp))
            .ToList();

    [Fact]
    public void Gpu_at_99_percent_is_the_limiter()
    {
        var r = BottleneckAnalyzer.Analyze(Samples(120, cpu: 55, core: 80, gpu: 99));
        Assert.Equal(Limiter.Gpu, r.Limiter);
    }

    [Fact]
    public void Cpu_saturated_while_gpu_waits_is_the_limiter()
    {
        var r = BottleneckAnalyzer.Analyze(Samples(120, cpu: 96, core: 100, gpu: 65));
        Assert.Equal(Limiter.Cpu, r.Limiter);
    }

    [Fact]
    public void One_maxed_core_with_gpu_waiting_points_to_the_cpu()
    {
        var r = BottleneckAnalyzer.Analyze(Samples(120, cpu: 60, core: 100, gpu: 60));
        Assert.Equal(Limiter.Cpu, r.Limiter);
    }

    [Fact]
    public void Cpu_and_gpu_both_high_means_gpu_limits()
    {
        // Le processeur ne « limite » que si la carte graphique attend.
        var r = BottleneckAnalyzer.Analyze(Samples(120, cpu: 95, core: 100, gpu: 98));
        Assert.Equal(Limiter.Gpu, r.Limiter);
    }

    [Fact]
    public void Ram_above_90_percent_is_the_limiter()
    {
        var r = BottleneckAnalyzer.Analyze(Samples(120, cpu: 60, core: 80, gpu: 70, ram: 94));
        Assert.Equal(Limiter.Ram, r.Limiter);
    }

    [Fact]
    public void Disk_at_100_percent_is_the_limiter()
    {
        var r = BottleneckAnalyzer.Analyze(Samples(120, cpu: 60, core: 80, gpu: 70, disk: 100));
        Assert.Equal(Limiter.Disk, r.Limiter);
    }

    [Fact]
    public void Nothing_saturated_means_none()
    {
        var r = BottleneckAnalyzer.Analyze(Samples(120, cpu: 60, core: 80, gpu: 70));
        Assert.Equal(Limiter.None, r.Limiter);
    }

    [Fact]
    public void Short_saturation_spikes_are_ignored()
    {
        var samples = Samples(100, cpu: 60, core: 80, gpu: 70);
        samples.AddRange(Samples(20, cpu: 60, core: 80, gpu: 99)); // 17 % du temps seulement
        Assert.Equal(Limiter.None, BottleneckAnalyzer.Analyze(samples).Limiter);
    }

    [Fact]
    public void Desktop_time_is_not_enough_data()
    {
        var r = BottleneckAnalyzer.Analyze(Samples(300, cpu: 5, core: 20, gpu: 3));
        Assert.Equal(Limiter.NotEnoughData, r.Limiter);
    }

    [Fact]
    public void Desktop_time_is_excluded_from_the_analysis()
    {
        var samples = Samples(200, cpu: 5, core: 10, gpu: 2);   // menus, bureau
        samples.AddRange(Samples(90, cpu: 50, core: 70, gpu: 99)); // partie
        Assert.Equal(Limiter.Gpu, BottleneckAnalyzer.Analyze(samples).Limiter);
    }

    [Fact]
    public void Without_gpu_usage_cpu_can_still_be_found()
    {
        var r = BottleneckAnalyzer.Analyze(Samples(120, cpu: 97, core: 100, gpu: null));
        Assert.Equal(Limiter.Cpu, r.Limiter);
    }

    [Fact]
    public void Hot_gpu_is_reported()
    {
        var r = BottleneckAnalyzer.Analyze(Samples(120, cpu: 55, core: 80, gpu: 99, gpuTemp: 88));
        Assert.Contains(r.Warnings, w => w.Contains("88 °C"));
    }

    [Fact]
    public void Stats_include_average_and_peak()
    {
        var r = BottleneckAnalyzer.Analyze(Samples(120, cpu: 55, core: 80, gpu: 99));
        var gpu = Assert.Single(r.Stats, s => s.Name.StartsWith("Carte graphique"));
        Assert.Equal(99, gpu.Average);
        Assert.Equal(99, gpu.P95);
    }

    [Fact]
    public void Pierres_cpu_gets_socket_1151_advice()
    {
        var r = BottleneckAnalyzer.Analyze(Samples(120, cpu: 96, core: 100, gpu: 65), Snapshots.PierreBefore());
        Assert.Contains(r.Upgrades, u => u.Contains("i7-9700K"));
    }

    [Fact]
    public void Gpu_upgrade_warns_about_a_weak_cpu()
    {
        var r = BottleneckAnalyzer.Analyze(Samples(120, cpu: 55, core: 80, gpu: 99), Snapshots.PierreBefore());
        Assert.Contains(r.Upgrades, u => u.Contains("6 threads"));
    }

    [Fact]
    public void Ram_advice_suggests_32_gb_ddr4_for_pierre()
    {
        var r = BottleneckAnalyzer.Analyze(Samples(120, cpu: 60, core: 80, gpu: 70, ram: 94), Snapshots.PierreBefore());
        Assert.Contains(r.Upgrades, u => u.Contains("32 Go de RAM DDR4"));
    }

    [Fact]
    public void Pierres_recording_cpu_peaks_with_idle_gpu_points_to_the_cpu()
    {
        // Mesures réelles du 25/11 : processeur 72 % en moyenne avec des pointes à 100 %, carte graphique à 35 %.
        var samples = new List<PerformanceSample>();
        for (var i = 0; i < 200; i++)
        {
            var peak = i % 10 == 0;
            samples.Add(new PerformanceSample(TimeSpan.FromSeconds(i),
                CpuPercent: peak ? 95 : 70, CpuBusiestCorePercent: peak ? 100 : 76, GpuPercent: 35,
                RamPercent: 81, DiskPercent: 2, GpuTemperatureC: 41));
        }

        var r = BottleneckAnalyzer.Analyze(samples, Snapshots.PierreBefore());

        Assert.Equal(Limiter.Cpu, r.Limiter);
        Assert.Contains(r.Upgrades, u => u.Contains("i7-9700K"));
        Assert.Contains(r.Warnings, w => w.Contains("RAM"));
        Assert.Contains(r.Upgrades, u => u.Contains("32 Go"));
    }

    [Fact]
    public void Fps_cap_with_low_cpu_and_gpu_is_not_blamed_on_the_cpu()
    {
        var samples = Samples(100, cpu: 52, core: 70, gpu: 40);
        samples.AddRange(Samples(10, cpu: 92, core: 100, gpu: 40));
        Assert.Equal(Limiter.None, BottleneckAnalyzer.Analyze(samples).Limiter);
    }

    [Fact]
    public void What_to_do_is_never_empty()
    {
        Assert.NotEmpty(BottleneckAnalyzer.Analyze(Samples(120, cpu: 60, core: 80, gpu: 70)).Upgrades);
        Assert.NotEmpty(BottleneckAnalyzer.Analyze(Samples(10, cpu: 60, core: 80, gpu: 70)).Upgrades);
        Assert.NotEmpty(BottleneckAnalyzer.Analyze(Samples(120, cpu: 60, core: 80, gpu: 70, disk: 100)).Upgrades);
    }
}
