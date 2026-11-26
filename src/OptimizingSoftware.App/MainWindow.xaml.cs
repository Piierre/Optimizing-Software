using System.Diagnostics;
using System.Windows;
using OptimizingSoftware.App.ViewModels;
using OptimizingSoftware.Core.Cleanup;
using OptimizingSoftware.Core.Diagnostics;
using OptimizingSoftware.Windows;

namespace OptimizingSoftware.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var isAdmin = Elevation.IsAdministrator();
        Title += $" {AppInfo.VersionText}";
        if (isAdmin) Title += " (administrateur)";
        var quarantine = new Quarantine(WindowsCleanupRules.QuarantineRoot, registry: new WindowsRegistryStore());

        var cleanup = new CleanupViewModel(
            WindowsCleanupRules.For(isAdmin), quarantine, Confirm,
            new WindowsRecycleBin(), isAdmin, Elevation.RestartAsAdministrator);
        var files = new FilesViewModel(
            quarantine, Confirm,
            () => KnownFolders.Downloads,
            KnownFolders.PersonalFolders,
            KnownFolders.PersonalFoldersAndDataDrives,
            onCleaned: cleanup.RefreshSessions,
            reveal: path => Process.Start("explorer.exe", $"/select,\"{path}\""),
            drives: KnownFolders.FixedDrives);
        var apps = new AppsViewModel(
            () => GameLibrariesReader.ReadAllApps(), AppUninstaller.LeftoverParents, app => AppUninstaller.RunAsync(app),
            quarantine, Confirm, isAdmin, Elevation.RestartAsAdministrator,
            onCleaned: cleanup.RefreshSessions,
            reveal: path => Process.Start("explorer.exe", $"\"{path}\""));
        var startup = new StartupViewModel(
            StartupPrograms.Read, StartupPrograms.SetEnabled, ct => ProcessReader.ReadAsync(ct), ProcessReader.Close,
            Confirm, isAdmin,
            reveal: path => Process.Start("explorer.exe", $"/select,\"{path}\""),
            search: name => Process.Start(new ProcessStartInfo(
                "https://www.google.com/search?q=" + Uri.EscapeDataString(name + ".exe processus")) { UseShellExecute = true }));
        var diagnostic = new MainViewModel(new WindowsSnapshotProvider(), new DiagnosticEngine());
        var recording = new RecordingViewModel(() => diagnostic.LastSnapshot);

        CleanupTab.DataContext = cleanup;
        FilesTab.DataContext = files;
        AppsTab.DataContext = apps;
        StartupTab.DataContext = startup;
        DiagnosticTab.DataContext = diagnostic;
        RecordingTab.DataContext = recording;
        Closed += (_, _) => recording.Dispose();

        // Analyse et diagnostic sont en lecture seule : on peut les lancer dès l'ouverture.
        Loaded += (_, _) =>
        {
            InitialScans = Task.WhenAll(
                diagnostic.RunDiagnosticCommand.ExecuteAsync(null),
                cleanup.InitializeAsync(),
                apps.ScanCommand.ExecuteAsync(null),
                startup.RefreshCommand.ExecuteAsync(null),
                CheckForUpdateAsync());
            _statuses = () => [cleanup.Status, apps.Status, startup.Status];
        };
    }

    private Func<IEnumerable<string?>> _statuses = () => [];

    /// <summary>Analyses lancées à l'ouverture (utilisé par le test de démarrage en CI).</summary>
    public Task InitialScans { get; private set; } = Task.CompletedTask;

    /// <summary>Messages d'erreur affichés après les analyses d'ouverture, s'il y en a.</summary>
    public IReadOnlyList<string> ScanErrors() =>
        _statuses().Where(s => s is not null && s.Contains("échoué", StringComparison.Ordinal)).Select(s => s!).ToList();

    private Core.Updates.UpdateInfo? _update;

    private async Task CheckForUpdateAsync()
    {
        _update = await AppInfo.FindUpdateAsync();
        if (_update is null) return;
        UpdateText.Text = $"La version {_update.Version} d'Optimizing Software est disponible (tu as la {AppInfo.Version.ToString(3)}).";
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_update is null) return;
        Process.Start(new ProcessStartInfo(_update.InstallerUrl ?? _update.PageUrl) { UseShellExecute = true });
    }

    private bool Confirm(string question) =>
        MessageBox.Show(this, question, "Optimizing Software", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;
}
