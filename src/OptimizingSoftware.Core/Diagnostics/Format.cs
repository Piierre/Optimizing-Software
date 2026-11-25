using System.Globalization;

namespace OptimizingSoftware.Core.Diagnostics;

internal static class Format
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static string Gigabytes(long bytes) =>
        (bytes / 1024d / 1024 / 1024).ToString(bytes >= 10L * 1024 * 1024 * 1024 ? "0" : "0.#", Fr) + " Go";

    public static string Percent(double ratio) => (ratio * 100).ToString("0", Fr) + " %";

    public static string Date(DateOnly date) => date.ToString("d MMMM yyyy", Fr);

    public static string Date(DateTimeOffset date) => date.ToString("d MMMM yyyy", Fr);
}
