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
        Loaded += async (_, _) =>
        {
            diagnostic.RunDiagnosticCommand.Execute(null);
            await cleanup.InitializeAsync();
            apps.ScanCommand.Execute(null);
            startup.RefreshCommand.Execute(null);
        };
    }

    private bool Confirm(string question) =>
        MessageBox.Show(this, question, "Optimizing Software", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;
}
