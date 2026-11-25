using OptimizingSoftware.Core.Cleanup;

namespace OptimizingSoftware.Core.Tests;

/// <summary>Tests sur de vrais dossiers temporaires, supprimés à la fin.</summary>
public sealed class CleanupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "os-tests-" + Guid.NewGuid().ToString("N"));
    private readonly DateTime _now = DateTime.UtcNow;

    public CleanupTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string File(string relative, int size = 100, double ageDays = 10)
    {
        var path = Path.Combine(_dir, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, new byte[size]);
        System.IO.File.SetLastWriteTimeUtc(path, _now.AddDays(-ageDays));
        return path;
    }

    private string P(string relative) => Path.Combine(_dir, relative.Replace('/', Path.DirectorySeparatorChar));

    private CleanupRule Rule(params string[] roots) =>
        new("test", "Test", "Test", roots.Select(P).ToList(), TimeSpan.FromDays(1));

    [Fact]
    public void Scan_finds_old_files_and_skips_recent_ones()
    {
        File("temp/old.tmp", size: 300, ageDays: 3);
        File("temp/sub/old2.tmp", size: 200, ageDays: 3);
        File("temp/new.tmp", ageDays: 0.1);

        var scan = CleanupScanner.ScanRule(Rule("temp"), _now);

        Assert.Equal(2, scan.Files.Count);
        Assert.Equal(500, scan.TotalBytes);
    }

    [Fact]
    public void Scan_never_looks_outside_its_roots()
    {
        File("temp/a.tmp");
        File("documents/precious.docx");

        var scan = CleanupScanner.ScanRule(Rule("temp"), _now);

        Assert.All(scan.Files, f => Assert.StartsWith(P("temp"), f.Path));
    }

    [Fact]
    public void Star_segment_expands_to_every_profile()
    {
        File("chrome/Default/Cache/f1");
        File("chrome/Profile 1/Cache/f2");
        File("chrome/Profile 1/History");

        var scan = CleanupScanner.ScanRule(Rule("chrome/*/Cache"), _now);

        Assert.Equal(2, scan.Roots.Count);
        Assert.Equal(2, scan.Files.Count);
        Assert.DoesNotContain(scan.Files, f => f.Path.EndsWith("History"));
    }

    [Fact]
    public void Missing_root_gives_an_empty_scan()
    {
        var scan = CleanupScanner.ScanRule(Rule("does-not-exist"), _now);
        Assert.Empty(scan.Files);
    }

    [Fact]
    public void Scan_skips_symbolic_links()
    {
        File("outside/secret.txt");
        Directory.CreateDirectory(P("temp"));
        Directory.CreateSymbolicLink(P("temp/link"), P("outside"));

        var scan = CleanupScanner.ScanRule(Rule("temp"), _now);

        Assert.Empty(scan.Files);
    }

    [Theory]
    [InlineData("temp/a.tmp", true)]
    [InlineData("temp/sub/a.tmp", true)]
    [InlineData("temp", false)]
    [InlineData("temp2/a.tmp", false)]
    [InlineData("temp/../documents/a.docx", false)]
    public void IsInside_is_strict(string relative, bool expected)
    {
        Assert.Equal(expected, PathSafety.IsInside(P(relative), [P("temp")]));
    }

    [Fact]
    public void Clean_then_restore_puts_everything_back()
    {
        var a = File("temp/a.tmp", size: 300);
        var b = File("temp/sub/b.tmp", size: 200);
        var q = new Quarantine(P("quarantine"));

        var outcome = q.MoveIn("Fichiers temporaires", [a, b], [P("temp")], DateTimeOffset.Now);

        Assert.Equal(2, outcome.MovedFiles);
        Assert.Equal(500, outcome.MovedBytes);
        Assert.False(System.IO.File.Exists(a));
        Assert.False(System.IO.File.Exists(b));
        var session = Assert.Single(q.List());
        Assert.Equal(500, session.TotalBytes);

        var restore = q.Restore(session.Id);

        Assert.Equal(2, restore.RestoredFiles);
        Assert.True(System.IO.File.Exists(a));
        Assert.True(System.IO.File.Exists(b));
        Assert.Empty(q.List());
    }

    [Fact]
    public void Clean_refuses_files_outside_allowed_roots()
    {
        var precious = File("documents/precious.docx");
        var q = new Quarantine(P("quarantine"));

        var outcome = q.MoveIn("Test", [precious], [P("temp")], DateTimeOffset.Now);

        Assert.Equal(0, outcome.MovedFiles);
        Assert.Equal(1, outcome.SkippedFiles);
        Assert.True(System.IO.File.Exists(precious));
        Assert.Empty(q.List());
    }

    [Fact]
    public void Restore_never_overwrites_a_new_file()
    {
        var a = File("temp/a.tmp", size: 300);
        var q = new Quarantine(P("quarantine"));
        var id = q.MoveIn("Test", [a], [P("temp")], DateTimeOffset.Now).SessionId!;
        File("temp/a.tmp", size: 42); // un nouveau fichier a pris la place

        var restore = q.Restore(id);

        Assert.Equal(0, restore.RestoredFiles);
        Assert.Equal(1, restore.SkippedFiles);
        Assert.Equal(42, new FileInfo(a).Length);
        Assert.Single(q.List()); // l'ancien reste restaurable dans la corbeille
    }

    [Fact]
    public void Purge_only_removes_sessions_older_than_7_days()
    {
        var q = new Quarantine(P("quarantine"));
        var now = DateTimeOffset.Now;
        q.MoveIn("Vieux", [File("temp/old.tmp", size: 100)], [P("temp")], now.AddDays(-8));
        q.MoveIn("Récent", [File("temp/new.tmp", size: 50)], [P("temp")], now.AddDays(-2));

        var freed = q.PurgeExpired(now);

        Assert.Equal(100, freed.FreedBytes);
        Assert.Equal("Récent", Assert.Single(q.List()).Label);
    }

    [Fact]
    public void Delete_permanently_refuses_unknown_sessions()
    {
        var q = new Quarantine(P("quarantine"));
        Directory.CreateDirectory(P("quarantine"));
        Assert.Throws<InvalidOperationException>(() => q.DeletePermanently(".."));
    }

    [Fact]
    public void Read_only_files_are_deleted_permanently()
    {
        var a = File("temp/readonly.dll", size: 300);
        System.IO.File.SetAttributes(a, FileAttributes.ReadOnly);
        var q = new Quarantine(P("quarantine"));
        var id = q.MoveIn("Test", [a], [P("temp")], DateTimeOffset.Now).SessionId!;

        var outcome = q.DeletePermanently(id);

        Assert.Equal(1, outcome.DeletedFiles);
        Assert.Equal(300, outcome.FreedBytes);
        Assert.Equal(0, outcome.LockedFiles);
        Assert.Empty(q.List());
        Assert.False(Directory.Exists(P("quarantine/" + id)));
    }

    [Fact]
    public void Undeletable_files_stay_in_the_quarantine_instead_of_crashing()
    {
        var a = File("temp/a.tmp", size: 300);
        var b = File("temp/loaded.dll", size: 200);
        var locked = true;
        var q = new Quarantine(P("quarantine")) { IsLockedForTests = p => locked && p.EndsWith(".dll") };
        var id = q.MoveIn("Test", [a, b], [P("temp")], DateTimeOffset.Now).SessionId!;

        var outcome = q.DeletePermanently(id);

        Assert.Equal(1, outcome.DeletedFiles);
        Assert.Equal(300, outcome.FreedBytes);
        Assert.Equal(1, outcome.LockedFiles);
        var left = Assert.Single(q.List());           // toujours listé, retentable plus tard
        Assert.Equal(b, Assert.Single(left.Entries).OriginalPath);

        locked = false;                                // l'application a été fermée
        var retry = q.DeletePermanently(id);

        Assert.Equal(1, retry.DeletedFiles);
        Assert.Empty(q.List());
    }

    [Fact]
    public void Purge_never_throws()
    {
        var q = new Quarantine(P("quarantine"));
        q.MoveIn("Vieux", [File("temp/old.tmp", size: 100)], [P("temp")], DateTimeOffset.Now.AddDays(-8));

        var outcome = q.PurgeExpired(DateTimeOffset.Now);

        Assert.Equal(100, outcome.FreedBytes);
    }
}
