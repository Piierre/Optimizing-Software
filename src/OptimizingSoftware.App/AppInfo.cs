using System.Net.Http;
using System.Reflection;
using OptimizingSoftware.Core.Updates;

namespace OptimizingSoftware.App;

public static class AppInfo
{
    public static Version Version { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static string VersionText => $"{Version.Major}.{Version.Minor}.{Math.Max(0, Version.Build)}";

    /// <summary>
    /// Cherche une version plus récente publiée sur GitHub. Null si aucune, si le PC est hors ligne ou si le dépôt
    /// n'est pas public : la recherche ne doit jamais gêner l'utilisation.
    /// </summary>
    public static async Task<UpdateInfo?> FindUpdateAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"OptimizingSoftware/{VersionText}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.GetAsync(UpdateFeed.LatestReleaseApi);
            if (!response.IsSuccessStatusCode) return null;
            var latest = UpdateFeed.Parse(await response.Content.ReadAsStringAsync());
            return latest is not null && UpdateFeed.IsNewer(latest, Version) ? latest : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
