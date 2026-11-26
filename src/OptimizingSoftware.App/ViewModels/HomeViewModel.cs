using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptimizingSoftware.Core.Cleanup;
using OptimizingSoftware.Core.Files;

namespace OptimizingSoftware.App.ViewModels;

/// <summary>
/// Écran d'accueil : une seule analyse, un résumé en quelques lignes de ce qu'on peut libérer, et un bouton
/// « Nettoyer » qui fait tout après une seule confirmation. Il s'appuie sur les autres écrans, qui gardent le détail.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private readonly CleanupViewModel _cleanup;
    private readonly AppsViewModel _apps;
    private readonly StartupViewModel _startup;
    private readonly Quarantine _quarantine;
    private readonly Func<string, bool> _confirm;
    private readonly Func<string> _downloads;
    private readonly Action<string> _navigate;

    /// <summary>Vieux téléchargements presque sûrement inutiles (installateurs, images disque) : l'accueil s'en occupe lui-même.</summary>
    private IReadOnlyList<FileCandidate> _oldDownloads = [];

    public HomeViewModel(CleanupViewModel cleanup, AppsViewModel apps, StartupViewModel startup,
        Quarantine quarantine, Func<string, bool> confirm, Func<string> downloads, Action<string> navigate)
    {
        _cleanup = cleanup;
        _apps = apps;
        _startup = startup;
        _quarantine = quarantine;
        _confirm = confirm;
        _downloads = downloads;
        _navigate = navigate;

        Junk = new HomeLine("Fichiers inutiles", "cleanup");
        Downloads = new HomeLine("Vieux installateurs dans Téléchargements", "files");
        Leftovers = new HomeLine("Restes d'applications désinstallées", "apps");
        Startup = new HomeLine("Programmes lancés au démarrage", "startup");
        Lines = [Junk, Downloads, Leftovers, Startup];
        foreach (var line in Lines) line.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(HomeLine.IsSelected)) UpdateHeadline(); };

        // Si l'utilisateur coche ou décoche dans le détail, l'accueil suit.
        cleanup.PropertyChanged += OnDetailChanged;
        apps.PropertyChanged += OnDetailChanged;
        startup.PropertyChanged += OnDetailChanged;
    }

    public HomeLine Junk { get; }
    public HomeLine Downloads { get; }
    public HomeLine Leftovers { get; }
    public HomeLine Startup { get; }
    public ObservableCollection<HomeLine> Lines { get; }

    [ObservableProperty]
    private string _headline = "Analyse de ton PC…";

    [ObservableProperty]
    private string _subline = "Ça prend quelques secondes. Rien n'est modifié pendant l'analyse.";

    [ObservableProperty]
    private string? _result;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private bool _hasSomething;

    public bool IsNotAdministrator => _cleanup.IsNotAdministrator;

    /// <summary>À l'ouverture : purge la corbeille de plus de 7 jours puis analyse tout.</summary>
    public Task InitializeAsync() => AnalyzeAsync(first: true);

    [RelayCommand]
    private Task Analyze() => AnalyzeAsync(first: false);

    private async Task AnalyzeAsync(bool first)
    {
        IsBusy = true;
        Result = null;
        Headline = "Analyse de ton PC…";
        Subline = "Ça prend quelques secondes. Rien n'est modifié pendant l'analyse.";
        try
        {
            var downloads = Task.Run(() =>
            {
                try
                {
                    return OldDownloadsFinder.Find(_downloads(), DateTime.UtcNow).Where(c => c.Suggested).ToList();
                }
                catch (Exception)
                {
                    return new List<FileCandidate>();
                }
            });
            await Task.WhenAll(
                first ? _cleanup.InitializeAsync() : _cleanup.ScanCommand.ExecuteAsync(null),
                _apps.ScanCommand.ExecuteAsync(null),
                _startup.RefreshCommand.ExecuteAsync(null),
                downloads);
            _oldDownloads = await downloads;
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    [RelayCommand]
    private void Show(string target) => _navigate(target);

    private bool CanClean() => IsIdle && HasSomething;

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        var chosen = Lines.Where(l => l.IsSelected && l.HasItems).ToList();
        if (chosen.Count == 0)
        {
            Result = "Coche au moins une ligne.";
            return;
        }
        var question = "Nettoyer ton PC ?\n\n" + string.Join("\n", chosen.Select(l => $"•  {l.Title} : {l.Value}")) +
                       "\n\nLes fichiers vont dans la corbeille d'Optimizing Software : tu peux tout remettre en place pendant 7 jours. " +
                       "Les programmes retirés du démarrage restent installés.";
        if (!_confirm(question)) return;

        IsBusy = true;
        long freed = 0;
        var disabled = 0;
        try
        {
            if (Junk.IsSelected) freed += await _cleanup.CleanSelectedAsync(ask: false);
            if (Downloads.IsSelected && _oldDownloads.Count > 0)
            {
                var root = _downloads();
                var files = _oldDownloads.Select(f => f.Path).ToList();
                var outcome = await Task.Run(() => _quarantine.MoveIn("Vieux installateurs", files, [root], DateTimeOffset.Now));
                freed += outcome.MovedBytes;
                _oldDownloads = _oldDownloads.Where(f => System.IO.File.Exists(f.Path)).ToList();
                _cleanup.RefreshSessions();
            }
            if (Leftovers.IsSelected) freed += await _apps.CleanSelectedLeftoversAsync(ask: false);
            if (Startup.IsSelected) disabled = _startup.DisableSuggestedPrograms(ask: false);
        }
        catch (Exception ex)
        {
            Result = $"Le nettoyage s'est arrêté en route : {ex.Message}";
            IsBusy = false;
            Refresh();
            return;
        }
        IsBusy = false;
        Refresh();

        var parts = new List<string>();
        if (freed > 0) parts.Add($"{Sizes.Format(freed)} libérés");
        if (disabled > 0) parts.Add($"{disabled} programme(s) retiré(s) du démarrage");
        Result = parts.Count == 0
            ? "Rien n'a pu être nettoyé (fichiers en cours d'utilisation ?)."
            : "C'est fait : " + string.Join(", ", parts) + ". Tu peux tout remettre pendant 7 jours depuis la corbeille (bouton ci-dessous).";
    }

    private void OnDetailChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsBusy) return;
        if (e.PropertyName is nameof(CleanupViewModel.SelectedTotal) or nameof(AppsViewModel.LeftoversTotal)
            or nameof(StartupViewModel.StartupSummary))
            Refresh();
    }

    /// <summary>Recalcule les lignes à partir de l'état des écrans de détail.</summary>
    private void Refresh()
    {
        var junk = _cleanup.Categories.Where(c => c.IsSelected && c.Scan.TotalBytes > 0).OrderByDescending(c => c.Scan.TotalBytes).ToList();
        Junk.Set(junk.Sum(c => c.Scan.TotalBytes),
            junk.Count == 0 ? "Rien à nettoyer" : string.Join(", ", junk.Take(3).Select(c => c.Name)) + (junk.Count > 3 ? "…" : ""));

        var dl = _oldDownloads;
        Downloads.Set(dl.Sum(f => f.SizeBytes),
            dl.Count == 0 ? "Aucun vieil installateur" : $"{dl.Count.ToString("N0", Fr)} installateurs de plus de 3 mois, ils se retéléchargent");

        var left = _apps.Leftovers.Where(l => l.IsSelected).ToList();
        Leftovers.Set(left.Sum(l => l.Leftover.SizeBytes),
            left.Count == 0 ? "Aucun dossier oublié" : $"{left.Count} dossier(s) laissé(s) par des applications qui ne sont plus là");

        var suggested = _startup.Suggested;
        var enabled = _startup.Startup.Count(s => s.IsEnabled);
        Startup.SetCount(suggested.Count,
            suggested.Count == 0 ? "Rien d'inutile au démarrage"
                : $"{suggested.Count} sur {enabled} ne servent pas au démarrage : {string.Join(", ", suggested.Take(3).Select(s => s.Name))}{(suggested.Count > 3 ? "…" : "")}");

        UpdateHeadline();
    }

    private void UpdateHeadline()
    {
        if (IsBusy) return;
        var bytes = Lines.Where(l => l.IsSelected && l.Bytes > 0).Sum(l => l.Bytes);
        var programs = Startup.IsSelected ? Startup.Count : 0;
        HasSomething = Lines.Any(l => l.HasItems);
        if (!HasSomething)
        {
            Headline = "Ton PC est propre";
            Subline = "Rien à nettoyer pour l'instant. Reviens dans quelques semaines.";
        }
        else if (bytes > 0)
        {
            Headline = $"Tu peux libérer {Sizes.Format(bytes)}";
            Subline = (programs > 0 ? $"et alléger le démarrage de {programs} programme(s). " : "") +
                      "Rien n'est supprimé pour de bon pendant 7 jours.";
        }
        else
        {
            Headline = programs > 0 ? $"Tu peux alléger le démarrage de {programs} programme(s)" : "Coche ce que tu veux nettoyer";
            Subline = "Les programmes retirés du démarrage restent installés.";
        }
    }
}

/// <summary>Une ligne du résumé de l'accueil.</summary>
public sealed partial class HomeLine(string title, string target) : ObservableObject
{
    public string Title { get; } = title;

    /// <summary>Écran de détail à ouvrir avec « Voir ».</summary>
    public string Target { get; } = target;

    [ObservableProperty]
    private string _detail = "";

    [ObservableProperty]
    private string _value = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems))]
    private long _bytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems))]
    private int _count;

    [ObservableProperty]
    private bool _isSelected = true;

    public bool HasItems => Bytes > 0 || Count > 0;

    public void Set(long bytes, string detail)
    {
        Count = 0;
        Bytes = bytes;
        Value = bytes > 0 ? Sizes.Format(bytes) : "—";
        Detail = detail;
    }

    public void SetCount(int count, string detail)
    {
        Bytes = 0;
        Count = count;
        Value = count > 0 ? $"{count} programme(s)" : "—";
        Detail = detail;
    }
}
