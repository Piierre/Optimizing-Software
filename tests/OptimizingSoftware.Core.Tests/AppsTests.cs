using OptimizingSoftware.Core.Apps;
using OptimizingSoftware.Core.Cleanup;

namespace OptimizingSoftware.Core.Tests;

public sealed class AppsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "os-apps-" + Guid.NewGuid().ToString("N"));
    private readonly DateTime _now = DateTime.UtcNow;

    public AppsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string P(string relative) => Path.Combine(_dir, relative.Replace('/', Path.DirectorySeparatorChar));

    private void File(string relative, int size = 100, double ageDays = 200)
    {
        var path = P(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, new byte[size]);
        System.IO.File.SetLastWriteTimeUtc(path, _now.AddDays(-ageDays));
        // Comme sur un vrai PC : les dossiers parents n'ont pas bougé depuis.
        for (var d = Path.GetDirectoryName(path)!; d.Length > _dir.Length; d = Path.GetDirectoryName(d)!)
            Directory.SetLastWriteTimeUtc(d, _now.AddDays(-ageDays));
    }

    private static InstalledApp App(string name, string? publisher = null, string? location = null) =>
        new(name, publisher, "1.0", null, null, location, null, @"HKEY_CURRENT_USER\Software\" + name, IsBroken: false);

    private IReadOnlyList<LeftoverFolder> Find(params InstalledApp[] apps) =>
        LeftoverFinder.Find(apps, [P("ProgramFiles"), P("AppData")], new HashSet<string> { P("ProgramFiles") }, _now);

    [Theory]
    [InlineData("Discord", "Discord", true)]
    [InlineData("discord", "Discord Inc.", true)]
    [InlineData("VideoLAN", "VLC media player", false)]
    [InlineData("Ubisoft Connect", "Ubisoft Connect", true)]
    [InlineData("App", "Application", false)]   // trop court pour conclure
    public void Folder_names_match_app_names(string folder, string app, bool expected) =>
        Assert.Equal(expected, NameMatcher.Matches(folder, [NameMatcher.Normalize(app)]));

    [Fact]
    public void Folder_of_an_installed_app_is_not_a_leftover()
    {
        File("ProgramFiles/VideoLAN/VLC/vlc.exe");
        File("AppData/discord/settings.json");

        var found = Find(
            App("VLC media player", "VideoLAN", location: P("ProgramFiles/VideoLAN/VLC")),
            App("Discord", "Discord Inc."));

        Assert.Empty(found);
    }

    [Fact]
    public void Program_folder_without_exe_is_a_suggested_leftover()
    {
        File("ProgramFiles/OldGameLauncher/logs/log.txt", size: 5000);

        var leftover = Assert.Single(Find());

        Assert.Equal(P("ProgramFiles/OldGameLauncher"), leftover.Path);
        Assert.Equal(5000, leftover.SizeBytes);
        Assert.True(leftover.Suggested);
    }

    [Fact]
    public void App_data_with_content_is_listed_but_not_suggested()
    {
        File("AppData/SomePortableTool/config.ini", size: 300);

        var leftover = Assert.Single(Find());

        Assert.False(leftover.Suggested);
    }

    [Fact]
    public void Empty_unknown_folder_is_suggested()
    {
        Directory.CreateDirectory(P("AppData/EmptyOldThing"));
        Directory.SetLastWriteTimeUtc(P("AppData/EmptyOldThing"), _now.AddDays(-300));

        var leftover = Assert.Single(Find());

        Assert.True(leftover.Suggested);
        Assert.Equal(0, leftover.SizeBytes);
    }

    [Fact]
    public void Recently_used_folders_are_ignored()
    {
        File("AppData/PortableApp/data.db", ageDays: 5);
        Assert.Empty(Find());
    }

    [Fact]
    public void Protected_folders_are_never_proposed()
    {
        File("AppData/Microsoft/x.dat");
        File("AppData/My Games/save.dat");
        File("ProgramFiles/Common Files/y.dll");
        File("AppData/NVIDIA/cache.bin");
        Assert.Empty(Find());
    }

    [Fact]
    public void Registry_keys_are_backed_up_deleted_and_restored()
    {
        var store = new FakeRegistry(@"HKCU\Uninstall\Ghost", @"HKCU\Uninstall\Other");
        var q = new Quarantine(P("q"), registry: store);

        var outcome = q.MoveRegistryIn("Entrées cassées", [@"HKCU\Uninstall\Ghost"], DateTimeOffset.Now);

        Assert.Equal(1, outcome.MovedFiles);
        Assert.DoesNotContain(@"HKCU\Uninstall\Ghost", store.Keys);
        var session = Assert.Single(q.List());
        Assert.Single(session.RegistryKeys);

        var restore = q.Restore(session.Id);

        Assert.Equal(1, restore.RestoredFiles);
        Assert.Contains(@"HKCU\Uninstall\Ghost", store.Keys);
        Assert.Empty(q.List());
    }

    [Fact]
    public void A_key_that_cannot_be_backed_up_is_never_deleted()
    {
        var store = new FakeRegistry(@"HKLM\Protected") { ExportFails = true };
        var q = new Quarantine(P("q"), registry: store);

        var outcome = q.MoveRegistryIn("Test", [@"HKLM\Protected"], DateTimeOffset.Now);

        Assert.Equal(0, outcome.MovedFiles);
        Assert.Equal(1, outcome.SkippedFiles);
        Assert.Contains(@"HKLM\Protected", store.Keys);
        Assert.Empty(q.List());
    }

    [Fact]
    public void Old_manifests_without_registry_still_load()
    {
        var q = new Quarantine(P("q"));
        Directory.CreateDirectory(P("q/old/files"));
        System.IO.File.WriteAllText(P("q/old/manifest.json"),
            """{"Id":"old","Label":"Ancien","CreatedAt":"2026-11-25T20:00:00+01:00","Entries":[]}""");

        var session = Assert.Single(q.List());

        Assert.Empty(session.RegistryKeys);
    }

    /// <summary>Registre simulé : une clé exportée devient un fichier contenant son nom.</summary>
    private sealed class FakeRegistry(params string[] keys) : IRegistryStore
    {
        public HashSet<string> Keys { get; } = [.. keys];
        public bool ExportFails { get; init; }

        public bool Export(string key, string regFile)
        {
            if (ExportFails || !Keys.Contains(key)) return false;
            System.IO.File.WriteAllText(regFile, key);
            return true;
        }

        public bool Delete(string key) => Keys.Remove(key);

        public bool Import(string regFile) => Keys.Add(System.IO.File.ReadAllText(regFile));
    }

    [Fact]
    public void Just_uninstalled_app_folders_are_suggested_even_if_recent()
    {
        File("ProgramFiles/Foobar Player/data.bin", ageDays: 1);
        File("AppData/Foobar Player/settings.json", ageDays: 1);
        File("AppData/Other Recent/settings.json", ageDays: 1);
        var removed = App("Foobar Player", "Same Publisher", location: P("ProgramFiles/Foobar Player"));

        var found = LeftoverFinder.Find([], [P("ProgramFiles"), P("AppData")],
            new HashSet<string> { P("ProgramFiles") }, _now, justUninstalled: removed);

        Assert.Equal(2, found.Count);
        Assert.All(found, f => Assert.True(f.Suggested));
        Assert.DoesNotContain(found, f => f.Path.Contains("Other Recent"));
    }

    [Fact]
    public void Just_uninstalled_folder_shared_with_an_installed_app_is_kept()
    {
        File("AppData/Foobar/settings.json", ageDays: 1);

        var found = LeftoverFinder.Find([App("Foobar Studio")], [P("AppData")], new HashSet<string>(), _now,
            justUninstalled: App("Foobar Player"));

        Assert.Empty(found);
    }

    [Fact]
    public void Leftover_folder_files_are_listed_and_empty_folders_removed()
    {
        File("AppData/Gone/a/b/c.txt");
        File("AppData/Gone/d.txt");
        Directory.CreateDirectory(P("AppData/Gone/empty/deeper"));

        Assert.Equal(2, LeftoverFinder.FilesIn(P("AppData/Gone")).Count());

        System.IO.File.Delete(P("AppData/Gone/a/b/c.txt"));
        LeftoverFinder.RemoveEmptyDirectories(P("AppData/Gone"));
        Assert.True(Directory.Exists(P("AppData/Gone")));          // d.txt est encore là
        Assert.False(Directory.Exists(P("AppData/Gone/a")));
        Assert.False(Directory.Exists(P("AppData/Gone/empty")));

        System.IO.File.Delete(P("AppData/Gone/d.txt"));
        LeftoverFinder.RemoveEmptyDirectories(P("AppData/Gone"));
        Assert.False(Directory.Exists(P("AppData/Gone")));
    }
}
