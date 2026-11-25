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
        var viewModel = new MainViewModel(new WindowsSnapshotProvider(), new DiagnosticEngine());
        DataContext = viewModel;
        // Le diagnostic est en lecture seule : on peut le lancer dès l'ouverture.
        Loaded += (_, _) => viewModel.RunDiagnosticCommand.Execute(null);
    }
}
