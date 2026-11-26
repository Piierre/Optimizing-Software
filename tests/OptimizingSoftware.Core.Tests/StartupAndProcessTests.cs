using OptimizingSoftware.Core.Processes;
using OptimizingSoftware.Core.Startup;

namespace OptimizingSoftware.Core.Tests;

public class StartupAndProcessTests
{
    private static StartupEntry Entry(string name, string? exe = null, bool exists = true, bool? signed = true) =>
        new(name, exe ?? $@"C:\Program Files\{name}\{name}.exe", exe ?? $@"C:\Program Files\{name}\{name}.exe",
            StartupLocation.RunUser, true, "Éditeur", signed, exists);

    [Fact]
    public void Startup_approved_values_round_trip()
    {
        Assert.True(StartupApproved.IsEnabled(null));
        Assert.True(StartupApproved.IsEnabled(StartupApproved.Enabled()));
        var disabled = StartupApproved.Disabled(new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc));
        Assert.False(StartupApproved.IsEnabled(disabled));
        Assert.Equal(12, disabled.Length);
        Assert.Equal(new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc(), BitConverter.ToInt64(disabled, 4));
    }

    [Theory]
    [InlineData("Steam", StartupAdvice.Disable)]
    [InlineData("Discord", StartupAdvice.Disable)]
    [InlineData("EpicGamesLauncher", StartupAdvice.Disable)]
    [InlineData("SecurityHealth", StartupAdvice.Keep)]
    [InlineData("RtkAudUService", StartupAdvice.Review)]
    [InlineData("RealtekHDAudio", StartupAdvice.Keep)]
    [InlineData("OneDrive", StartupAdvice.Keep)]
    [InlineData("Obsidian", StartupAdvice.Review)]   // « obs » ne doit pas viser Obsidian
    [InlineData("SomeTool", StartupAdvice.Review)]
    public void Startup_advice(string name, StartupAdvice expected) =>
        Assert.Equal(expected, StartupAdvisor.Advise(Entry(name)).Advice);

    [Fact]
    public void Startup_entry_whose_program_is_gone_is_broken() =>
        Assert.Equal(StartupAdvice.Broken, StartupAdvisor.Advise(Entry("Old", exists: false)).Advice);

    private static ProcessInfo Proc(string name, string? path, bool? signed = true, string? publisher = "Éditeur",
        long memory = 100_000_000, double cpu = 1) =>
        new([1234], name, path, memory, cpu, publisher, signed, null);

    [Fact]
    public void Real_svchost_is_normal_and_cannot_be_closed()
    {
        var a = ProcessAdvisor.Assess(Proc("svchost", @"C:\Windows\System32\svchost.exe", publisher: "Microsoft Corporation"));
        Assert.Equal(ProcessVerdict.Normal, a.Verdict);
        Assert.False(a.CanClose);
    }

    [Fact]
    public void Fake_svchost_outside_windows_is_suspicious_and_closable()
    {
        var a = ProcessAdvisor.Assess(Proc("svchost", @"C:\Users\Pierre\AppData\Roaming\svchost.exe"));
        Assert.Equal(ProcessVerdict.Suspicious, a.Verdict);
        Assert.True(a.CanClose);
    }

    [Fact]
    public void Unsigned_program_in_temp_without_publisher_is_suspicious() =>
        Assert.Equal(ProcessVerdict.Suspicious,
            ProcessAdvisor.Assess(Proc("abcxyz", @"C:\Users\Pierre\AppData\Local\Temp\abcxyz.exe", signed: false, publisher: null)).Verdict);

    [Fact]
    public void Signed_program_in_appdata_is_fine() =>
        Assert.Equal(ProcessVerdict.Normal,
            ProcessAdvisor.Assess(Proc("Discord", @"C:\Users\Pierre\AppData\Local\Discord\app-1.0\Discord.exe")).Verdict);

    [Fact]
    public void Miner_is_suspicious() =>
        Assert.Equal(ProcessVerdict.Suspicious, ProcessAdvisor.Assess(Proc("xmrig", @"C:\Games\xmrig.exe")).Verdict);

    [Fact]
    public void Launcher_in_background_is_unnecessary() =>
        Assert.Equal(ProcessVerdict.Unnecessary,
            ProcessAdvisor.Assess(Proc("EpicGamesLauncher", @"C:\Program Files (x86)\Epic Games\Launcher\EpicGamesLauncher.exe")).Verdict);

    [Fact]
    public void Heavy_process_is_flagged()
    {
        var a = ProcessAdvisor.Assess(Proc("chrome", @"C:\Program Files\Google\Chrome\chrome.exe", memory: 2L << 30, cpu: 40));
        Assert.Equal(ProcessVerdict.Heavy, a.Verdict);
        Assert.Equal(2, a.Reasons.Count);
    }

    [Fact]
    public void Protected_process_without_path_is_not_judged() =>
        Assert.Equal(ProcessVerdict.Normal, ProcessAdvisor.Assess(Proc("lsass", null, signed: null, publisher: null)).Verdict);
}
