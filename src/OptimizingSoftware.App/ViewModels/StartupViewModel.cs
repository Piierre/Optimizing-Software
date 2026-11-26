using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptimizingSoftware.Core.Cleanup;
using OptimizingSoftware.Core.Processes;
using OptimizingSoftware.Core.Startup;

namespace OptimizingSoftware.App.ViewModels;

/// <summary>
/// Onglet « Démarrage et processus » : programmes lancés à l'ouverture de session (activables et désactivables
/// comme dans le Gestionnaire des tâches) et programmes en cours, avec ceux qui sont gourmands, inutiles ou suspects.
/// </summary>
public sealed partial class StartupViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<StartupEntry>> _readStartup;
    private readonly Func<StartupEntry, bool, bool> _setEnabled;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ProcessInfo>>> _readProcesses;
    private readonly Func<IEnumerable<int>, int> _close;
    private readonly Func<string, bool> _confirm;
    private readonly Action<string> _reveal;
    private readonly Action<string> _search;

    public StartupViewModel(
        Func<IReadOnlyList<StartupEntry>> readStartup, Func<StartupEntry, bool, bool> setEnabled,
        Func<CancellationToken, Task<IReadOnlyList<ProcessInfo>>> readProcesses, Func<IEnumerable<int>, int> close,
        Func<string, bool> confirm, bool isAdministrator, Action<string> reveal, Action<string> search)
    {
        _readStartup = readStartup;
        _setEnabled = setEnabled;
        _readProcesses = readProcesses;
        _close = close;
        _confirm = confirm;
        IsAdministrator = isAdministrator;
        _reveal = reveal;
        _search = search;
        ProcessesView = CollectionViewSource.GetDefaultView(Processes);
        ProcessesView.Filter = o => o is ProcessItem p && (!OnlyFlagged || p.Assessment.Verdict != ProcessVerdict.Normal);
        ProcessesView.SortDescriptions.Add(new SortDescription(nameof(ProcessItem.Rank), ListSortDirection.Ascending));
        ProcessesView.SortDescriptions.Add(new SortDescription(nameof(ProcessItem.MemoryBytes), ListSortDirection.Descending));
    }

    public bool IsAdministrator { get; }

    public ObservableCollection<StartupItem> Startup { get; } = [];
    public ObservableCollection<ProcessItem> Processes { get; } = [];
    public ICollectionView ProcessesView { get; }

    [ObservableProperty]
    private int _section;

    [ObservableProperty]
    private bool _onlyFlagged = true;

    partial void OnOnlyFlaggedChanged(bool value) => ProcessesView.Refresh();

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private string _startupSummary = "";

    [ObservableProperty]
    private string _processesSummary = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        Status = "Analyse du démarrage et des programmes en cours…";
        try
        {
            var startupTask = Task.Run(_readStartup);
            var processes = await _readProcesses(CancellationToken.None);
            var startup = await startupTask;

            Startup.Clear();
            foreach (var e in startup
                         .Select(e => new StartupItem(e))
                         .OrderBy(i => i.Rank).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
                Startup.Add(e);
            UpdateStartupSummary();

            Processes.Clear();
            foreach (var p in processes) Processes.Add(new ProcessItem(p));
            var suspicious = Processes.Count(p => p.Assessment.Verdict == ProcessVerdict.Suspicious);
            ProcessesSummary = $"{Processes.Count} programmes en cours · {Sizes.Format(Processes.Sum(p => p.MemoryBytes))} de mémoire"
                               + (suspicious > 0 ? $" · {suspicious} suspect(s)" : " · rien de suspect");
            Status = suspicious > 0
                ? $"{suspicious} programme(s) suspect(s) : regarde l'onglet Processus."
                : $"Rien de suspect. {Startup.Count(s => s.IsEnabled && s.Advice == StartupAdvice.Disable)} programme(s) au démarrage peuvent être désactivés.";
            if (suspicious > 0) Section = 1;
        }
        catch (Exception ex)
        {
            Status = $"L'analyse a échoué : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Toggle(StartupItem item)
    {
        var enable = !item.IsEnabled;
        if (_setEnabled(item.Entry, enable))
        {
            item.IsEnabled = enable;
            Status = $"{item.Name} {(enable ? "réactivé" : "désactivé")} au démarrage. Ça prend effet à la prochaine ouverture de session.";
            UpdateStartupSummary();
        }
        else
        {
            Status = $"Impossible de modifier {item.Name} : il est lancé pour tout le PC"
                     + (IsAdministrator ? "." : ", relance l'appli en administrateur (bouton dans l'onglet Nettoyage).");
        }
    }

    [RelayCommand]
    private void DisableSuggested()
    {
        var targets = Startup.Where(s => s.IsEnabled && s.Advice == StartupAdvice.Disable).ToList();
        if (targets.Count == 0)
        {
            Status = "Aucun programme conseillé à désactiver.";
            return;
        }
        if (!_confirm($"Désactiver au démarrage :\n\n{string.Join("\n", targets.Select(t => "•  " + t.Name))}\n\n" +
                      "Ils restent installés et tu pourras les réactiver ici (ou dans le Gestionnaire des tâches)."))
            return;
        var done = 0;
        foreach (var t in targets)
        {
            if (!_setEnabled(t.Entry, false)) continue;
            t.IsEnabled = false;
            done++;
        }
        UpdateStartupSummary();
        Status = $"{done} programme(s) désactivé(s) au démarrage."
                 + (done < targets.Count ? $" {targets.Count - done} lancé(s) pour tout le PC demandent les droits administrateur." : "");
    }

    [RelayCommand]
    private void CloseProcess(ProcessItem item)
    {
        if (!item.Assessment.CanClose)
        {
            Status = $"{item.Name} est un programme de Windows : le fermer ferait planter le PC.";
            return;
        }
        if (!_confirm($"Fermer {item.Name} ({item.Count} processus) ?\n\nUn travail non enregistré dans ce programme serait perdu. " +
                      "Il se relancera s'il est prévu au démarrage ou si un autre programme en a besoin."))
            return;
        var closed = _close(item.Info.Pids);
        if (closed > 0) Processes.Remove(item);
        Status = closed > 0 ? $"{item.Name} fermé." : $"{item.Name} n'a pas pu être fermé (droits administrateur nécessaires ?).";
    }

    [RelayCommand]
    private void Reveal(string? path)
    {
        if (!string.IsNullOrEmpty(path)) _reveal(path);
    }

    [RelayCommand]
    private void SearchOnline(string name) => _search(name);

    private void UpdateStartupSummary()
    {
        var enabled = Startup.Count(s => s.IsEnabled);
        var suggested = Startup.Count(s => s.IsEnabled && s.Advice == StartupAdvice.Disable);
        StartupSummary = $"{enabled} programme(s) lancés au démarrage sur {Startup.Count}" +
                         (suggested > 0 ? $" · {suggested} peuvent être désactivés" : "");
    }
}

public sealed partial class StartupItem : ObservableObject
{
    public StartupItem(StartupEntry entry)
    {
        Entry = entry;
        (Advice, Reason) = StartupAdvisor.Advise(entry);
        _isEnabled = entry.Enabled;
    }

    public StartupEntry Entry { get; }
    public StartupAdvice Advice { get; }
    public string Reason { get; }
    public string Name => Entry.Name;
    public string Publisher => Entry.Publisher ?? "";
    public string? Path => Entry.ExePath;
    public string Scope => Entry.ForAllUsers ? "Tout le PC" : "Ton compte";
    public string AdviceText => Advice switch
    {
        StartupAdvice.Keep => "À garder",
        StartupAdvice.Disable => "À désactiver",
        StartupAdvice.Broken => "Cassé",
        _ => "À voir",
    };
    public string AdviceBrush => Advice switch
    {
        StartupAdvice.Keep => "OkBrush",
        StartupAdvice.Disable or StartupAdvice.Broken => "WarningBrush",
        _ => "UnknownBrush",
    };
    public int Rank => Advice switch { StartupAdvice.Broken => 0, StartupAdvice.Disable => 1, StartupAdvice.Review => 2, _ => 3 };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(ToggleText))]
    private bool _isEnabled;

    public string StateText => IsEnabled ? "Activé" : "Désactivé";
    public string ToggleText => IsEnabled ? "Désactiver" : "Réactiver";
}

public sealed class ProcessItem(ProcessInfo info)
{
    public ProcessInfo Info { get; } = info;
    public ProcessAssessment Assessment { get; } = ProcessAdvisor.Assess(info);
    public string Name => Info.Name;
    public string Description => Info.Description ?? "";
    public int Count => Info.Pids.Count;
    public string Memory => Sizes.Format(Info.MemoryBytes);
    public long MemoryBytes => Info.MemoryBytes;
    public string Cpu => $"{Info.CpuPercent:0} %";
    public double CpuPercent => Info.CpuPercent;
    public string Publisher => Info.Publisher ?? "";
    public string? Path => Info.Path;
    public string Signature => Info.Signed switch { true => "Signé", false => "Non signé", _ => "" };
    public string Reasons => string.Join(" ", Assessment.Reasons);
    public string VerdictText => Assessment.Verdict switch
    {
        ProcessVerdict.Suspicious => "Suspect",
        ProcessVerdict.Unnecessary => "Inutile",
        ProcessVerdict.Heavy => "Gourmand",
        _ => "",
    };
    public string VerdictBrush => Assessment.Verdict switch
    {
        ProcessVerdict.Suspicious => "ProblemBrush",
        ProcessVerdict.Unnecessary or ProcessVerdict.Heavy => "WarningBrush",
        _ => "",
    };
    public int Rank => Assessment.Verdict switch
    {
        ProcessVerdict.Suspicious => 0, ProcessVerdict.Unnecessary => 1, ProcessVerdict.Heavy => 2, _ => 3,
    };
    public bool CanClose => Assessment.CanClose;
}
