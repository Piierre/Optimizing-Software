namespace OptimizingSoftware.Core.Cleanup;

/// <summary>La corbeille du système (celle du Bureau).</summary>
public interface IRecycleBin
{
    (long Bytes, long Items) Query();

    /// <summary>Vide la corbeille. Irréversible.</summary>
    void Empty();
}
