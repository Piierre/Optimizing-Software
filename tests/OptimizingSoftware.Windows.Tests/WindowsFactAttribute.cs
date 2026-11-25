namespace OptimizingSoftware.Windows.Tests;

/// <summary>Test lancé seulement sous Windows (ignoré sur les autres systèmes).</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Nécessite Windows.";
    }
}
