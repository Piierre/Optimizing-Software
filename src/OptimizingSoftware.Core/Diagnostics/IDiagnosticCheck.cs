namespace OptimizingSoftware.Core.Diagnostics;

/// <summary>Une vérification : lit la photo du PC et rend un verdict. Ne modifie jamais rien.</summary>
public interface IDiagnosticCheck
{
    string Id { get; }
    string Title { get; }
    CheckResult Evaluate(SystemSnapshot snapshot);
}

/// <summary>Source de la photo du PC (implémentée pour Windows dans OptimizingSoftware.Windows).</summary>
public interface ISystemSnapshotProvider
{
    Task<SystemSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}
