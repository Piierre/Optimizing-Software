namespace OptimizingSoftware.Core.Cleanup;

/// <summary>Liste ce que chaque règle nettoierait. Ne modifie rien.</summary>
public static class CleanupScanner
{
    public static IReadOnlyList<CategoryScan> Scan(IEnumerable<CleanupRule> rules, DateTime nowUtc, CancellationToken ct = default) =>
        rules.Select(rule => ScanRule(rule, nowUtc, ct)).ToList();

    public static CategoryScan ScanRule(CleanupRule rule, DateTime nowUtc, CancellationToken ct = default)
    {
        var roots = rule.Roots.SelectMany(PathSafety.Expand).Distinct().ToList();
        var files = new List<FoundFile>();
        var cutoff = nowUtc - rule.MinAge;
        foreach (var root in roots)
            Walk(root, roots, cutoff, files, ct);
        return new CategoryScan(rule, roots, files);
    }

    private static void Walk(string dir, IReadOnlyList<string> roots, DateTime cutoffUtc, List<FoundFile> found, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var info = new DirectoryInfo(dir);
        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = info.EnumerateFileSystemInfos().ToList();
        }
        catch (Exception)
        {
            return; // Accès refusé, dossier disparu : on passe.
        }

        foreach (var entry in entries)
        {
            // Jamais de lien symbolique ou de jonction : ils peuvent pointer n'importe où.
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;

            if (entry is DirectoryInfo sub)
                Walk(sub.FullName, roots, cutoffUtc, found, ct);
            else if (entry is FileInfo file
                     && file.LastWriteTimeUtc <= cutoffUtc
                     && PathSafety.IsInside(file.FullName, roots))
                found.Add(new FoundFile(file.FullName, file.Length, file.LastWriteTimeUtc));
        }
    }
}
