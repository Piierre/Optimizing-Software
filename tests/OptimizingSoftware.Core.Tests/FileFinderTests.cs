using OptimizingSoftware.Core.Cleanup;
using OptimizingSoftware.Core.Files;

namespace OptimizingSoftware.Core.Tests;

public sealed class FileFinderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "os-files-" + Guid.NewGuid().ToString("N"));
    private readonly DateTime _now = DateTime.UtcNow;

    public FileFinderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string P(string relative) => Path.Combine(_dir, relative.Replace('/', Path.DirectorySeparatorChar));

    private string File(string relative, int size = 100, double ageDays = 200, byte fill = 0)
    {
        var path = P(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new byte[size];
        Array.Fill(bytes, fill);
        System.IO.File.WriteAllBytes(path, bytes);
        System.IO.File.SetLastWriteTimeUtc(path, _now.AddDays(-ageDays));
        return path;
    }

    [Fact]
    public void Old_installers_are_suggested_but_old_documents_are_not()
    {
        File("Downloads/setup.exe", ageDays: 200);
        File("Downloads/game.iso", ageDays: 200);
        File("Downloads/cv.pdf", ageDays: 200);
        File("Downloads/photos.zip", ageDays: 200);
        File("Downloads/recent.exe", ageDays: 10);

        var found = OldDownloadsFinder.Find(P("Downloads"), _now).ToDictionary(c => Path.GetFileName(c.Path));

        Assert.Equal(4, found.Count);
        Assert.True(found["setup.exe"].Suggested);
        Assert.True(found["game.iso"].Suggested);
        Assert.False(found["cv.pdf"].Suggested);
        Assert.False(found["photos.zip"].Suggested);
        Assert.Contains("6 mois", found["setup.exe"].Reason);
    }

    [Fact]
    public void Large_files_are_sorted_and_never_suggested()
    {
        File("Videos/small.mp4", size: 10);
        File("Videos/big.mp4", size: 5000);
        File("Videos/bigger.mkv", size: 9000);

        var found = LargeFileFinder.Find([P("Videos")], minBytes: 1000);

        Assert.Equal(["bigger.mkv", "big.mp4"], found.Select(f => Path.GetFileName(f.Path)));
        Assert.All(found, f => Assert.False(f.Suggested));
    }

    [Fact]
    public void Installed_games_and_app_data_are_never_scanned()
    {
        File("D/SteamLibrary/steamapps/common/Game/data.pak", size: 9000);
        File("D/Epic Games/Fortnite/pak.pak", size: 9000);
        File("User/AppData/Local/big.db", size: 9000);
        File("D/Films/film.mkv", size: 9000);

        var found = LargeFileFinder.Find([P("D"), P("User")], minBytes: 1000);

        Assert.Equal("film.mkv", Path.GetFileName(Assert.Single(found).Path));
    }

    [Fact]
    public void Duplicates_keep_the_original_and_suggest_the_copies()
    {
        File("Documents/photo.jpg", size: 3000, fill: 7);
        File("Documents/photo (1).jpg", size: 3000, fill: 7);
        File("Downloads/photo.jpg", size: 3000, fill: 7);
        File("Documents/other.jpg", size: 3000, fill: 8); // même taille, contenu différent

        var group = Assert.Single(DuplicateFinder.Find([P("Documents"), P("Downloads")], minBytes: 1000));

        Assert.Equal(3, group.Files.Count);
        Assert.Equal(6000, group.WastedBytes);
        var kept = Assert.Single(group.Files, f => !f.Suggested);
        Assert.Equal(P("Documents/photo.jpg"), kept.Path);
    }

    [Fact]
    public void Large_identical_files_are_compared_in_full()
    {
        // Même début (64 Ko de zéros) mais fin différente : ce ne sont pas des doublons.
        var a = File("Documents/a.bin", size: 200_000);
        var b = File("Documents/b.bin", size: 200_000);
        using (var s = new FileStream(b, FileMode.Open)) { s.Seek(-1, SeekOrigin.End); s.WriteByte(1); }
        File("Documents/c.bin", size: 200_000);

        var group = Assert.Single(DuplicateFinder.Find([P("Documents")], minBytes: 1000));

        Assert.Equal(2, group.Files.Count);
        Assert.DoesNotContain(group.Files, f => f.Path == b);
        Assert.Contains(group.Files, f => f.Path == a);
    }

    [Theory]
    [InlineData("photo (1).jpg", true)]
    [InlineData("photo - Copie.jpg", true)]
    [InlineData("photo - Copy (2).jpg", true)]
    [InlineData("Copie de photo.jpg", true)]
    [InlineData("photo.jpg", false)]
    [InlineData("vacances 2024.jpg", false)]
    public void Copy_names_are_recognised(string name, bool expected) =>
        Assert.Equal(expected, DuplicateFinder.LooksLikeACopy(name));

    [Fact]
    public void Files_on_another_drive_stay_on_that_drive_and_restore()
    {
        var onOtherDrive = File("D/Films/film.mkv", size: 500);
        var q = new Quarantine(P("C/Corbeille"),
            path => path.StartsWith(P("D")) ? P("D/" + Quarantine.VolumeFolderName) : null);

        var outcome = q.MoveIn("Gros fichiers", [onOtherDrive], [P("D")], DateTimeOffset.Now);

        var stored = Directory.GetFiles(P("D/" + Quarantine.VolumeFolderName), "*", SearchOption.AllDirectories);
        Assert.Single(stored);
        Assert.Equal(1, outcome.MovedFiles);

        q.Restore(outcome.SessionId!);

        Assert.True(System.IO.File.Exists(onOtherDrive));
        Assert.Empty(Directory.GetDirectories(P("D/" + Quarantine.VolumeFolderName)));
    }

    [Fact]
    public void Deleting_a_session_also_empties_the_other_drive()
    {
        var onOtherDrive = File("D/Films/film.mkv", size: 500);
        var q = new Quarantine(P("C/Corbeille"),
            path => path.StartsWith(P("D")) ? P("D/" + Quarantine.VolumeFolderName) : null);
        var id = q.MoveIn("Gros fichiers", [onOtherDrive], [P("D")], DateTimeOffset.Now).SessionId!;

        q.DeletePermanently(id);

        Assert.Empty(Directory.GetDirectories(P("D/" + Quarantine.VolumeFolderName)));
        Assert.Empty(q.List());
    }
}
