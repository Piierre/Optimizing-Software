using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OptimizingSoftware.Core.Processes;

namespace OptimizingSoftware.Windows;

/// <summary>Programmes en cours, regroupés par programme, avec leur mémoire et leur processeur sur ~1 seconde.</summary>
[SupportedOSPlatform("windows")]
public static partial class ProcessReader
{
    public static async Task<IReadOnlyList<ProcessInfo>> ReadAsync(CancellationToken ct = default)
    {
        var first = Snapshot();
        var sw = Stopwatch.StartNew();
        await Task.Delay(1000, ct);
        var second = Snapshot();
        var elapsed = sw.Elapsed.TotalMilliseconds * Environment.ProcessorCount;

        var rows = second.Select(p =>
        {
            var cpu = first.TryGetValue(p.Key, out var before) && p.Value.Cpu is { } now && before.Cpu is { } then
                ? Math.Max(0, (now - then).TotalMilliseconds / elapsed * 100) : 0;
            return (Pid: p.Key, p.Value.Name, p.Value.Path, p.Value.Memory, Cpu: cpu);
        });

        var self = Environment.ProcessId;
        return rows
            .Where(r => r.Pid != self && r.Pid != 0)
            .GroupBy(r => (r.Name.ToUpperInvariant(), r.Path?.ToUpperInvariant()))
            .Select(g =>
            {
                var path = g.First().Path;
                var (publisher, signed) = FileTrust.Check(path);
                string? description = null;
                try
                {
                    if (path is not null) description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
                }
                catch (Exception)
                {
                    // Sans description.
                }
                return new ProcessInfo(g.Select(r => r.Pid).ToList(), g.First().Name, path, g.Sum(r => r.Memory),
                    g.Sum(r => r.Cpu), publisher, signed, string.IsNullOrEmpty(description) ? null : description);
            })
            .OrderByDescending(p => p.MemoryBytes)
            .ToList();
    }

    /// <summary>Ferme tous les processus du programme. Renvoie le nombre fermés.</summary>
    public static int Close(IEnumerable<int> pids)
    {
        var closed = 0;
        foreach (var pid in pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill();
                p.WaitForExit(3000);
                closed++;
            }
            catch (Exception)
            {
                // Déjà fermé, ou protégé.
            }
        }
        return closed;
    }

    private static Dictionary<int, (string Name, string? Path, long Memory, TimeSpan? Cpu)> Snapshot()
    {
        var result = new Dictionary<int, (string, string?, long, TimeSpan?)>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    TimeSpan? cpu = null;
                    try
                    {
                        cpu = p.TotalProcessorTime;
                    }
                    catch (Exception)
                    {
                        // Processus protégé : pas de mesure.
                    }
                    result[p.Id] = (p.ProcessName, PathOf(p.Id), p.WorkingSet64, cpu);
                }
                catch (Exception)
                {
                    // Terminé entre-temps.
                }
            }
        }
        return result;
    }

    private static string? PathOf(int pid)
    {
        var handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new char[1024];
            var size = buffer.Length;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageNameW(IntPtr process, uint flags, [Out] char[] buffer, ref int size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
