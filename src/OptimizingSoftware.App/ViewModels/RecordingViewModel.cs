using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptimizingSoftware.Core.Diagnostics;
using OptimizingSoftware.Core.Recording;
using OptimizingSoftware.Windows;

namespace OptimizingSoftware.App.ViewModels;

/// <summary>Onglet « Enregistrement en jeu » : mesure pendant une partie, puis dit ce qui limite.</summary>
public sealed partial class RecordingViewModel : ObservableObject, IDisposable
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private readonly PerformanceRecorder _recorder = new();
    private readonly Func<SystemSnapshot?> _snapshot;
    private readonly Stopwatch _clock = new();

    public RecordingViewModel(Func<SystemSnapshot?> snapshot)
    {
        _snapshot = snapshot;
        _recorder.SampleTaken += sample =>
            Application.Current?.Dispatcher.BeginInvoke(() => ShowLive(sample));
    }

    public ObservableCollection<LiveGauge> Live { get; } = [];
    public ObservableCollection<string> Upgrades { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    public ObservableCollection<StatLine> Stats { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isRecording;

    [ObservableProperty]
    private string _elapsed = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport))]
    private string? _verdict;

    [ObservableProperty]
    private string? _explanation;

    [ObservableProperty]
    private string? _brushKey;

    public bool HasReport => Verdict is not null;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        Verdict = null;
        Explanation = null;
        Upgrades.Clear();
        Warnings.Clear();
        Stats.Clear();
        Live.Clear();
        _clock.Restart();
        Elapsed = "0:00";
        _recorder.Start();
        IsRecording = true;
    }

    private bool CanStart() => !IsRecording;

    [RelayCommand(CanExecute = nameof(IsRecording))]
    private async Task StopAsync()
    {
        var samples = await _recorder.StopAsync();
        IsRecording = false;
        _clock.Stop();
        ShowReport(BottleneckAnalyzer.Analyze(samples, _snapshot()));
    }

    private void ShowLive(PerformanceSample s)
    {
        if (!IsRecording) return;
        Elapsed = _clock.Elapsed.ToString(@"m\:ss", Fr);
        Live.Clear();
        Live.Add(new LiveGauge("Processeur", s.CpuPercent, $"{s.CpuPercent:0} % (cœur max {s.CpuBusiestCorePercent:0} %)"));
        if (s.GpuPercent is { } gpu)
            Live.Add(new LiveGauge("Carte graphique", gpu, s.GpuTemperatureC is { } t ? $"{gpu:0} % · {t:0} °C" : $"{gpu:0} %"));
        Live.Add(new LiveGauge("RAM", s.RamPercent, $"{s.RamPercent:0} %"));
        Live.Add(new LiveGauge("Disque", s.DiskPercent, $"{s.DiskPercent:0} %"));
    }

    private void ShowReport(BottleneckReport r)
    {
        Live.Clear();
        Explanation = r.Explanation;
        BrushKey = r.Limiter switch
        {
            Limiter.None => "OkBrush",
            Limiter.NotEnoughData => "UnknownBrush",
            _ => "WarningBrush",
        };
        foreach (var u in r.Upgrades) Upgrades.Add(u);
        foreach (var w in r.Warnings) Warnings.Add(w);
        foreach (var s in r.Stats)
            Stats.Add(new StatLine(s.Name, s.Average.ToString("0", Fr), s.P95.ToString("0", Fr), s.Max.ToString("0", Fr)));
        Verdict = r.Verdict;
    }

    public void Dispose() => _recorder.Dispose();
}

public sealed record LiveGauge(string Name, double Percent, string Text);

public sealed record StatLine(string Name, string Average, string P95, string Max);
