using OptimizingSoftware.Core.Diagnostics;
using OptimizingSoftware.Core.Diagnostics.Checks;

namespace OptimizingSoftware.Core.Tests;

public class CheckTests
{
    private static SystemSnapshot Pierre => Snapshots.PierreBefore();
    private const long GiB = Snapshots.GiB;

    [Theory]
    [InlineData(10, CheckStatus.Ok)]
    [InlineData(120, CheckStatus.Warning)]
    [InlineData(200, CheckStatus.Problem)]
    public void Driver_age_thresholds(int ageDays, CheckStatus expected)
    {
        var date = DateOnly.FromDateTime(Snapshots.Now.Date).AddDays(-ageDays);
        var s = Pierre with { Gpus = [new GpuInfo("GPU", "1.0", date)] };
        Assert.Equal(expected, new GpuDriverAgeCheck().Evaluate(s).Status);
    }

    [Fact]
    public void Driver_check_uses_the_oldest_driver()
    {
        var s = Pierre with
        {
            Gpus =
            [
                new GpuInfo("Récente", "2", new DateOnly(2026, 11, 20)),
                new GpuInfo("Ancienne", "1", new DateOnly(2025, 1, 1)),
            ],
        };
        var r = new GpuDriverAgeCheck().Evaluate(s);
        Assert.Equal(CheckStatus.Problem, r.Status);
        Assert.Contains("Ancienne", r.Summary);
    }

    [Fact]
    public void Old_gpu_crashes_are_ignored()
    {
        var s = Pierre with { GpuCrashes = [new GpuCrashEvent(Snapshots.Now.AddDays(-45), "LiveKernelEvent 141")] };
        Assert.Equal(CheckStatus.Ok, new GpuCrashCheck().Evaluate(s).Status);
    }

    [Fact]
    public void Three_recent_gpu_crashes_are_a_problem()
    {
        var crash = new GpuCrashEvent(Snapshots.Now.AddDays(-2), "4101");
        var s = Pierre with { GpuCrashes = [crash, crash, crash] };
        Assert.Equal(CheckStatus.Problem, new GpuCrashCheck().Evaluate(s).Status);
    }

    [Fact]
    public void Uncollected_crashes_are_unknown()
    {
        var s = Pierre with { GpuCrashesCollected = false };
        Assert.Equal(CheckStatus.Unknown, new GpuCrashCheck().Evaluate(s).Status);
    }

    [Fact]
    public void Screen_below_its_max_refresh_rate_is_a_problem()
    {
        var s = Pierre with { Displays = [new DisplayInfo("HKC", 1920, 1080, 60, 165, IsPrimary: true)] };
        var r = new DisplayRefreshRateCheck().Evaluate(s);
        Assert.Equal(CheckStatus.Problem, r.Status);
        Assert.Contains("165", r.Summary);
    }

    [Fact]
    public void Rounding_59_vs_60_hz_is_not_flagged()
    {
        var s = Pierre with
        {
            Displays =
            [
                new DisplayInfo("A", 1920, 1080, 59, 60, IsPrimary: true),
                new DisplayInfo("B", 1920, 1080, 60, 60, IsPrimary: false),
            ],
        };
        Assert.Equal(CheckStatus.Ok, new DisplayRefreshRateCheck().Evaluate(s).Status);
    }

    [Fact]
    public void Mixed_refresh_rates_mention_windows_10_only_on_windows_10()
    {
        var on10 = new DisplayRefreshRateCheck().Evaluate(Pierre);
        var on11 = new DisplayRefreshRateCheck().Evaluate(Pierre with { Os = new OsInfo("Windows 11", "24H2", 26100) });
        Assert.Contains("Windows 10", on10.Advice);
        Assert.DoesNotContain("Windows 10", on11.Advice);
    }

    [Fact]
    public void Single_memory_stick_is_single_channel()
    {
        var s = Pierre with
        {
            Memory = new MemoryInfo(8 * GiB, 4 * GiB, [new MemoryModule(8 * GiB, 3200, 3200, MemoryType.Ddr4, "A2")], 4),
        };
        Assert.Equal(CheckStatus.Problem, new MemoryChannelCheck().Evaluate(s).Status);
    }

    [Fact]
    public void Ram_below_its_rated_speed_is_flagged()
    {
        var stick = new MemoryModule(8 * GiB, 3200, 2133, MemoryType.Ddr4, "A2");
        var s = Pierre with { Memory = new MemoryInfo(16 * GiB, 8 * GiB, [stick, stick], 4) };
        Assert.Equal(CheckStatus.Warning, new MemorySpeedCheck().Evaluate(s).Status);
    }

    [Fact]
    public void Ddr4_at_jedec_speed_suggests_xmp()
    {
        var stick = new MemoryModule(8 * GiB, 2400, 2400, MemoryType.Ddr4, "A2");
        var s = Pierre with { Memory = new MemoryInfo(16 * GiB, 8 * GiB, [stick, stick], 4) };
        var r = new MemorySpeedCheck().Evaluate(s);
        Assert.Equal(CheckStatus.Warning, r.Status);
        Assert.Contains("XMP", r.Advice);
    }

    [Fact]
    public void Windows_on_a_hard_drive_is_a_problem()
    {
        var s = Pierre with { Disks = [new DiskInfo("C:", null, DiskMediaType.Hdd, 931 * GiB, 500 * GiB, true)] };
        Assert.Equal(CheckStatus.Problem, new SystemDiskCheck().Evaluate(s).Status);
    }

    [Fact]
    public void Nearly_full_system_disk_is_a_problem()
    {
        var s = Pierre with { Disks = [new DiskInfo("C:", null, DiskMediaType.Ssd, 931 * GiB, 15 * GiB, true)] };
        Assert.Equal(CheckStatus.Problem, new SystemDiskCheck().Evaluate(s).Status);
    }

    [Fact]
    public void Power_saver_is_a_problem()
    {
        var s = Pierre with { PowerPlan = new PowerPlanInfo(PowerPlanInfo.PowerSaver, "Économie d'énergie") };
        Assert.Equal(CheckStatus.Problem, new PowerPlanCheck().Evaluate(s).Status);
    }

    [Fact]
    public void Memory_usage_above_90_percent_is_a_problem()
    {
        var s = Pierre with { Memory = Pierre.Memory! with { AvailableBytes = 1 * GiB } };
        Assert.Equal(CheckStatus.Problem, new MemoryUsageCheck().Evaluate(s).Status);
    }
}
