using System.Windows;
using OptimizingSoftware.App.ViewModels;
using OptimizingSoftware.Core.Diagnostics;
using OptimizingSoftware.Windows;

namespace OptimizingSoftware.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var diagnostic = new MainViewModel(new WindowsSnapshotProvider(), new DiagnosticEngine());
        var recording = new RecordingViewModel(() => diagnostic.LastSnapshot);
        DiagnosticTab.DataContext = diagnostic;
        RecordingTab.DataContext = recording;
        Closed += (_, _) => recording.Dispose();
        // Le diagnostic est en lecture seule : on peut le lancer dès l'ouverture.
        Loaded += (_, _) => diagnostic.RunDiagnosticCommand.Execute(null);
    }
}
