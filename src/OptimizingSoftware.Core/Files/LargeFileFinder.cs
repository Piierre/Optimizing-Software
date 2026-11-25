namespace OptimizingSoftware.Core.Files;

/// <summary>Les plus gros fichiers personnels. Rien n'est coché : c'est une liste pour faire le tri.</summary>
public static class LargeFileFinder
{
    public const long DefaultMinBytes = 500L * 1024 * 1024;
    public const int MaxResults = 300;

    public static IReadOnlyList<FileCandidate> Find(IEnumerable<string> roots, long minBytes = DefaultMinBytes, CancellationToken ct = default) =>
        FileWalker.Walk(roots, ct)
            .Where(f => f.Length >= minBytes)
            .OrderByDescending(f => f.Length)
            .Take(MaxResults)
            .Select(f => new FileCandidate(f.FullName, f.Length, f.LastWriteTimeUtc,
                $"Modifié le {f.LastWriteTime:dd/MM/yyyy}.", Suggested: false))
            .ToList();
}
