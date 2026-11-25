using System.Text.Json;

namespace OptimizingSoftware.Core.Cleanup;

/// <summary>Un nettoyage passé, restaurable tant qu'il est dans la corbeille interne.</summary>
public sealed record QuarantineSession(
    string Id,
    string Label,
    DateTimeOffset CreatedAt,
    IReadOnlyList<QuarantineEntry> Entries)
{
    public long TotalBytes => Entries.Sum(e => e.SizeBytes);
}

/// <param name="StoredName">Nom du fichier dans la corbeille interne.</param>
public sealed record QuarantineEntry(string OriginalPath, string StoredName, long SizeBytes);

public sealed record CleanOutcome(int MovedFiles, long MovedBytes, int SkippedFiles, string? SessionId);

public sealed record RestoreOutcome(int RestoredFiles, int SkippedFiles);

/// <summary>
/// Corbeille interne : les fichiers nettoyés y sont déplacés (pas supprimés) avec la liste de leurs
/// emplacements d'origine, et peuvent être remis en place. Ils ne sont supprimés définitivement
/// qu'après <see cref="RetentionPeriod"/> ou quand l'utilisateur vide la corbeille.
/// </summary>
public sealed class Quarantine(string root)
{
    public static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(7);
    private const string ManifestName = "manifest.json";
    private const string FilesDir = "files";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Root { get; } = root;

    /// <summary>
    /// Déplace les fichiers dans la corbeille interne. Chaque fichier est revérifié : il doit être sous l'un des
    /// <paramref name="allowedRoots"/> et ne pas être un lien. Un fichier verrouillé (en cours d'utilisation) est ignoré.
    /// </summary>
    public CleanOutcome MoveIn(string label, IEnumerable<string> paths, IReadOnlyList<string> allowedRoots,
        DateTimeOffset now, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var id = $"{now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
        var sessionDir = Path.Combine(Root, id);
        var filesDir = Path.Combine(sessionDir, FilesDir);
        Directory.CreateDirectory(filesDir);

        var entries = new List<QuarantineEntry>();
        var skipped = 0;
        var index = 0;
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(path) || PathSafety.IsLink(path) || !PathSafety.IsInside(path, allowedRoots))
                {
                    skipped++;
                    continue;
                }
                var size = new FileInfo(path).Length;
                var stored = $"{index++:D7}{Path.GetExtension(path)}";
                File.Move(path, Path.Combine(filesDir, stored));
                entries.Add(new QuarantineEntry(path, stored, size));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++; // En cours d'utilisation ou protégé : on n'insiste pas.
            }
            progress?.Report(entries.Count + skipped);
        }

        if (entries.Count == 0)
        {
            Directory.Delete(sessionDir, recursive: true);
            return new CleanOutcome(0, 0, skipped, null);
        }

        Save(new QuarantineSession(id, label, now, entries));
        return new CleanOutcome(entries.Count, entries.Sum(e => e.SizeBytes), skipped, id);
    }

    public IReadOnlyList<QuarantineSession> List()
    {
        if (!Directory.Exists(Root)) return [];
        var sessions = new List<QuarantineSession>();
        foreach (var dir in Directory.GetDirectories(Root))
        {
            var manifest = Path.Combine(dir, ManifestName);
            if (!File.Exists(manifest)) continue;
            try
            {
                if (JsonSerializer.Deserialize<QuarantineSession>(File.ReadAllText(manifest), Json) is { } s)
                    sessions.Add(s);
            }
            catch (JsonException)
            {
                // Manifeste abîmé : on le laisse, il ne sera ni listé ni purgé automatiquement.
            }
        }
        return sessions.OrderByDescending(s => s.CreatedAt).ToList();
    }

    /// <summary>Remet chaque fichier à son emplacement d'origine. Un fichier qui y existe déjà n'est pas écrasé.</summary>
    public RestoreOutcome Restore(string sessionId)
    {
        var session = List().FirstOrDefault(s => s.Id == sessionId)
                      ?? throw new InvalidOperationException($"Nettoyage {sessionId} introuvable.");
        var filesDir = Path.Combine(Root, sessionId, FilesDir);
        var restored = 0;
        var remaining = new List<QuarantineEntry>();
        foreach (var e in session.Entries)
        {
            var stored = Path.Combine(filesDir, e.StoredName);
            try
            {
                if (File.Exists(e.OriginalPath) || !File.Exists(stored))
                {
                    remaining.Add(e);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(e.OriginalPath)!);
                File.Move(stored, e.OriginalPath);
                restored++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                remaining.Add(e);
            }
        }

        if (remaining.Count == 0)
            Directory.Delete(Path.Combine(Root, sessionId), recursive: true);
        else
            Save(session with { Entries = remaining });
        return new RestoreOutcome(restored, remaining.Count);
    }

    /// <summary>Supprime définitivement un nettoyage de la corbeille interne. Irréversible.</summary>
    public void DeletePermanently(string sessionId)
    {
        var dir = Path.Combine(Root, sessionId);
        // Garde-fou : on ne supprime qu'un dossier de session de la corbeille interne.
        if (!PathSafety.IsInside(dir, [Root]) || !File.Exists(Path.Combine(dir, ManifestName)))
            throw new InvalidOperationException($"Nettoyage {sessionId} introuvable.");
        Directory.Delete(dir, recursive: true);
    }

    /// <summary>Supprime définitivement les nettoyages de plus de 7 jours. Renvoie l'espace libéré.</summary>
    public long PurgeExpired(DateTimeOffset now)
    {
        long freed = 0;
        foreach (var s in List().Where(s => now - s.CreatedAt > RetentionPeriod))
        {
            DeletePermanently(s.Id);
            freed += s.TotalBytes;
        }
        return freed;
    }

    private void Save(QuarantineSession session) =>
        File.WriteAllText(Path.Combine(Root, session.Id, ManifestName), JsonSerializer.Serialize(session, Json));
}
