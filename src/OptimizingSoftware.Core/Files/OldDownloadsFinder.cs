namespace OptimizingSoftware.Core.Files;

/// <summary>Fichiers du dossier Téléchargements qui n'ont pas bougé depuis longtemps.</summary>
public static class OldDownloadsFinder
{
    public static readonly TimeSpan DefaultAge = TimeSpan.FromDays(90);

    private static readonly HashSet<string> Installers = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".msi", ".msix", ".appx", ".appinstaller" };
    private static readonly HashSet<string> Archives = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z", ".tar", ".gz" };
    private static readonly HashSet<string> DiskImages = new(StringComparer.OrdinalIgnoreCase) { ".iso", ".img" };

    public static IReadOnlyList<FileCandidate> Find(string downloads, DateTime nowUtc, TimeSpan? olderThan = null, CancellationToken ct = default)
    {
        var cutoff = nowUtc - (olderThan ?? DefaultAge);
        return FileWalker.Walk([downloads], ct)
            .Where(f => f.LastWriteTimeUtc <= cutoff)
            .Select(f =>
            {
                var months = (int)((nowUtc - f.LastWriteTimeUtc).TotalDays / 30);
                var age = months >= 24 ? $"{months / 12} ans" : $"{months} mois";
                var ext = f.Extension;
                var (reason, suggested) =
                    Installers.Contains(ext) ? ($"Installateur, téléchargé il y a {age}. Il se retélécharge si besoin.", true)
                    : DiskImages.Contains(ext) ? ($"Image disque, il y a {age}.", true)
                    : Archives.Contains(ext) ? ($"Archive, il y a {age}. Vérifie que tu l'as décompressée.", false)
                    : ($"Pas modifié depuis {age}.", false);
                return new FileCandidate(f.FullName, f.Length, f.LastWriteTimeUtc, reason, suggested);
            })
            .OrderByDescending(c => c.SizeBytes)
            .ToList();
    }
}
