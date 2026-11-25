using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using OptimizingSoftware.Core.Diagnostics;

namespace OptimizingSoftware.Windows;

/// <summary>
/// Cherche les plantages de la carte graphique dans les journaux Windows :
/// - « LiveKernelEvent » (141, 117…) rapportés par Windows Error Reporting (journal Application, ID 1001) ;
/// - « Le pilote d'affichage a cessé de répondre et a récupéré » (journal Système, source Display, ID 4101).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class GpuCrashReader
{
    /// <summary>Codes LiveKernelEvent liés à la carte graphique (141, 117 : TDR ; 1a8 : pilote bloqué).</summary>
    private static readonly HashSet<string> GpuLiveKernelCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "141", "117", "1a8", "1b0",
    };

    public static IReadOnlyList<GpuCrashEvent> ReadSince(DateTime since)
    {
        var ms = (long)Math.Max(0, (DateTime.Now - since).TotalMilliseconds);
        var timeFilter = $"TimeCreated[timediff(@SystemTime) <= {ms}]";

        var events = new List<GpuCrashEvent>();

        foreach (var record in Read("Application",
                     $"*[System[Provider[@Name='Windows Error Reporting'] and EventID=1001 and {timeFilter}]]"))
        {
            // Les données WER sont : Bucket, BucketType, EventName, Response, CabId, P1, P2…
            // On repère « LiveKernelEvent » puis on prend P1, trois champs plus loin.
            var values = record.Properties.Select(p => p.Value?.ToString() ?? "").ToList();
            var i = values.FindIndex(v => v.Equals("LiveKernelEvent", StringComparison.OrdinalIgnoreCase));
            if (i < 0 || i + 3 >= values.Count) continue;
            var code = values[i + 3].Trim();
            if (GpuLiveKernelCodes.Contains(code) && record.TimeCreated is { } t)
                events.Add(new GpuCrashEvent(t, $"LiveKernelEvent {code}"));
        }

        foreach (var record in Read("System",
                     $"*[System[Provider[@Name='Display'] and EventID=4101 and {timeFilter}]]"))
        {
            if (record.TimeCreated is { } t)
                events.Add(new GpuCrashEvent(t, "pilote d'affichage relancé (4101)"));
        }

        return events.OrderByDescending(e => e.OccurredAt).ToList();
    }

    private static IEnumerable<EventRecord> Read(string log, string xpath)
    {
        using var reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, xpath));
        while (reader.ReadEvent() is { } record)
        {
            using (record)
                yield return record;
        }
    }
}
