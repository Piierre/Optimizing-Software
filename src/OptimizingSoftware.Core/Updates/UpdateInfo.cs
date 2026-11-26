using System.Text.Json;

namespace OptimizingSoftware.Core.Updates;

/// <summary>Une version publiée (release GitHub) et le lien de son installeur.</summary>
public sealed record UpdateInfo(Version Version, string PageUrl, string? InstallerUrl, string? Notes);

public static class UpdateFeed
{
    /// <summary>Adresse de la dernière version publiée. Le dépôt est privé pour l'instant : la recherche échoue sans bruit.</summary>
    public const string LatestReleaseApi = "https://api.github.com/repos/Piierre/Optimizing-Software/releases/latest";

    /// <summary>Lit la réponse de l'API GitHub « releases/latest ». Null si elle est illisible.</summary>
    public static UpdateInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out var tag) || ParseVersion(tag.GetString()) is not { } version) return null;
            var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
            string? installer = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                        installer = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                }
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;
            return new UpdateInfo(version, page, installer, notes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>« v0.8.0 », « 0.8 » ou « 0.8.0-beta » → 0.8.0.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().TrimStart('v', 'V');
        var dash = t.IndexOfAny(['-', '+']);
        if (dash >= 0) t = t[..dash];
        if (!Version.TryParse(t.Contains('.') ? t : t + ".0", out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    /// <summary>Vrai si la version publiée est plus récente que celle installée.</summary>
    public static bool IsNewer(UpdateInfo latest, Version current) =>
        latest.Version > new Version(current.Major, current.Minor, Math.Max(0, current.Build));
}
