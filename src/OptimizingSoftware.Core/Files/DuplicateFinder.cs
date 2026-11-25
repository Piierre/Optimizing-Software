using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace OptimizingSoftware.Core.Files;

/// <summary>
/// Fichiers au contenu strictement identique (même taille, même empreinte SHA-256).
/// Dans chaque groupe, un exemplaire est gardé et les copies sont cochées.
/// </summary>
public static partial class DuplicateFinder
{
    public const long DefaultMinBytes = 1024 * 1024;
    private const int QuickHashBytes = 64 * 1024;

    /// <param name="progress">Nombre de fichiers dont l'empreinte a été calculée.</param>
    public static IReadOnlyList<DuplicateGroup> Find(IEnumerable<string> roots, long minBytes = DefaultMinBytes,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        // 1. Même taille. 2. Même début de fichier (rapide). 3. Même empreinte complète.
        var sameSize = FileWalker.Walk(roots, ct)
            .Where(f => f.Length >= minBytes)
            .GroupBy(f => f.Length)
            .Where(g => g.Count() > 1);

        var hashed = 0;
        var groups = new List<DuplicateGroup>();
        foreach (var bySize in sameSize)
        {
            var sameStart = bySize
                .Select(f => (File: f, Hash: TryHash(f.FullName, QuickHashBytes)))
                .Where(x => x.Hash is not null)
                .GroupBy(x => x.Hash!)
                .Where(g => g.Count() > 1);

            foreach (var candidates in sameStart)
            {
                var full = candidates
                    .Select(x => (x.File, Hash: x.File.Length <= QuickHashBytes ? x.Hash : TryHash(x.File.FullName, null)))
                    .Where(x => x.Hash is not null)
                    .GroupBy(x => x.Hash!)
                    .Where(g => g.Count() > 1);

                foreach (var identical in full)
                {
                    ct.ThrowIfCancellationRequested();
                    var files = identical.Select(x => x.File).ToList();
                    groups.Add(BuildGroup(identical.Key, files));
                    progress?.Report(hashed += files.Count);
                }
            }
        }
        return groups.OrderByDescending(g => g.WastedBytes).ToList();
    }

    /// <summary>Choisit l'exemplaire à garder, puis coche les autres.</summary>
    internal static DuplicateGroup BuildGroup(string hash, IReadOnlyList<FileInfo> files)
    {
        var keep = files
            .OrderBy(f => LooksLikeACopy(f.Name) ? 1 : 0)
            .ThenBy(f => IsInDownloads(f.FullName) ? 1 : 0)
            .ThenBy(f => f.CreationTimeUtc)
            .ThenBy(f => f.FullName.Length)
            .First();

        var key = hash[..12];
        var candidates = files
            .OrderBy(f => f == keep ? 0 : 1)
            .ThenBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(f => f == keep
                ? new FileCandidate(f.FullName, f.Length, f.LastWriteTimeUtc, "Exemplaire gardé.", false, key)
                : new FileCandidate(f.FullName, f.Length, f.LastWriteTimeUtc, $"Copie identique de {keep.Name}.", true, key))
            .ToList();
        return new DuplicateGroup(key, files[0].Length, candidates);
    }

    /// <summary>« photo (1).jpg », « photo - Copie.jpg », « Copy of photo.jpg »…</summary>
    public static bool LooksLikeACopy(string name) => CopyPattern().IsMatch(name);

    private static bool IsInDownloads(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}Downloads{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}Téléchargements{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static string? TryHash(string path, int? maxBytes)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan);
            if (maxBytes is { } max)
            {
                var buffer = new byte[max];
                var read = stream.ReadAtLeast(buffer, max, throwOnEndOfStream: false);
                return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, read)));
            }
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"(\(\d+\)|\s-\s(Copie|Copy)(\s\(\d+\))?|^(Copie de|Copy of)\s)", RegexOptions.IgnoreCase)]
    private static partial Regex CopyPattern();
}
