using System.Globalization;

namespace OptimizingSoftware.Core.Cleanup;

public static class Sizes
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>1536 → « 1,5 Ko », 2 147 483 648 → « 2 Go ».</summary>
    public static string Format(long bytes)
    {
        string[] units = ["octets", "Ko", "Mo", "Go", "To"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0
            ? $"{bytes} {units[0]}"
            : value.ToString(value >= 100 ? "0" : "0.#", Fr) + " " + units[unit];
    }
}
