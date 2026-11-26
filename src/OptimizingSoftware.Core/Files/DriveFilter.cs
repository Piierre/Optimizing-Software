namespace OptimizingSoftware.Core.Files;

/// <summary>Un disque proposé au choix avant une analyse.</summary>
/// <param name="Root">Racine, par exemple « D:\ ».</param>
public sealed record DriveChoice(string Root, string? Label, long TotalBytes, long FreeBytes, bool IsSystem);

public static class DriveFilter
{
    /// <summary>Garde les dossiers qui se trouvent sur l'un des disques choisis.</summary>
    public static IReadOnlyList<string> Keep(IEnumerable<string> folders, IEnumerable<string> selectedRoots)
    {
        var selected = selectedRoots.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return folders.Where(f => Path.GetPathRoot(f) is { Length: > 0 } root && selected.Contains(Normalize(root))).ToList();
    }

    private static string Normalize(string root) => root.TrimEnd('\\', '/').ToUpperInvariant();
}
