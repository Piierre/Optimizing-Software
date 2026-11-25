namespace OptimizingSoftware.Core.Cleanup;

/// <summary>
/// Une catégorie de fichiers à nettoyer (« Fichiers temporaires », « Cache de Chrome »…).
/// Le nettoyage ne touche jamais rien en dehors de <see cref="Roots"/>.
/// </summary>
/// <param name="Id">Identifiant stable, par exemple « user-temp ».</param>
/// <param name="Roots">
/// Dossiers à vider. Un segment « * » désigne n'importe quel sous-dossier (par exemple un profil de navigateur) :
/// <c>C:\Users\Pierre\AppData\Local\Google\Chrome\User Data\*\Cache</c>.
/// </param>
/// <param name="MinAge">Seuls les fichiers non modifiés depuis cette durée sont proposés (évite ceux en cours d'utilisation).</param>
/// <param name="SelectedByDefault">Cochée par défaut dans l'interface.</param>
/// <param name="Note">Mise en garde affichée à l'utilisateur, si besoin.</param>
public sealed record CleanupRule(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> Roots,
    TimeSpan MinAge,
    bool SelectedByDefault = true,
    string? Note = null);

public sealed record FoundFile(string Path, long SizeBytes, DateTime LastWriteUtc);

/// <param name="Roots">Les dossiers réels trouvés après expansion des « * ».</param>
public sealed record CategoryScan(CleanupRule Rule, IReadOnlyList<string> Roots, IReadOnlyList<FoundFile> Files)
{
    public long TotalBytes => Files.Sum(f => f.SizeBytes);
}
