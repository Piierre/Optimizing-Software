using System.Management;

namespace OptimizingSoftware.Windows;

internal static class Wmi
{
    public static List<ManagementBaseObject> Query(string wql, string scope = @"root\cimv2")
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        using var results = searcher.Get();
        return results.Cast<ManagementBaseObject>().ToList();
    }

    public static string? Str(this ManagementBaseObject o, string name) =>
        o[name]?.ToString()?.Trim() is { Length: > 0 } s ? s : null;

    public static long? Long(this ManagementBaseObject o, string name) =>
        o[name] is { } v ? Convert.ToInt64(v) : null;

    public static int? Int(this ManagementBaseObject o, string name) =>
        o[name] is { } v ? Convert.ToInt32(v) : null;
}
