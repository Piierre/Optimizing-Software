namespace OptimizingSoftware.Core.Files;

/// <summary>Un fichier personnel proposé au nettoyage. L'utilisateur décide toujours au cas par cas.</summary>
/// <param name="Reason">Pourquoi il est proposé (« Installateur de plus de 3 mois », « Copie de … »).</param>
/// <param name="Suggested">Coché par défaut (seulement quand c'est presque sûrement inutile).</param>
/// <param name="GroupKey">Pour les doublons : identifiant du groupe de fichiers identiques.</param>
public sealed record FileCandidate(
    string Path,
    long SizeBytes,
    DateTime LastWriteUtc,
    string Reason,
    bool Suggested,
    string? GroupKey = null);

public sealed record DuplicateGroup(string Key, long SizeBytes, IReadOnlyList<FileCandidate> Files)
{
    /// <summary>Place récupérable en ne gardant qu'un exemplaire.</summary>
    public long WastedBytes => SizeBytes * (Files.Count - 1);
}
