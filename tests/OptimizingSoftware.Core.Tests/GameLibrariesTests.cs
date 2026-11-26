using OptimizingSoftware.Core.Apps;

namespace OptimizingSoftware.Core.Tests;

public class GameLibrariesTests
{
    private const string LibraryFolders = """
        "libraryfolders"
        {
            "0"
            {
                "path"		"C:\\Program Files (x86)\\Steam"
                "label"		""
                "apps" { "228980" "0" }
            }
            "1"
            {
                "path"		"D:\\SteamLibrary"
            }
        }
        """;

    [Fact]
    public void Steam_library_folders_are_read()
    {
        var folders = GameLibraries.ParseSteamLibraryFolders(LibraryFolders);
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], folders);
    }

    [Fact]
    public void Steam_manifest_gives_name_size_and_folder()
    {
        const string acf = """
            "AppState"
            {
                "appid"		"730"
                "name"		"Counter-Strike 2"
                "installdir"		"Counter-Strike Global Offensive"
                "SizeOnDisk"		"35000000000"
            }
            """;
        var game = GameLibraries.ParseSteamManifest(acf, "LIB")!;
        Assert.Equal("Counter-Strike 2", game.Name);
        Assert.Equal(35_000_000_000, game.SizeBytes);
        Assert.Equal(Path.Combine("LIB", "steamapps", "common", "Counter-Strike Global Offensive"), game.InstallLocation);
        Assert.Equal("steam://uninstall/730", game.UninstallCommand);
        Assert.True(game.IsGame);
        Assert.Equal("Steam", game.Source);
    }

    [Fact]
    public void Steam_redistributables_are_not_games()
    {
        const string acf = "\"appid\" \"228980\" \"name\" \"Steamworks Common Redistributables\" \"installdir\" \"Steamworks Shared\"";
        Assert.Null(GameLibraries.ParseSteamManifest(acf, "LIB"));
    }

    [Fact]
    public void Epic_manifest_is_read_and_incomplete_installs_skipped()
    {
        var game = GameLibraries.ParseEpicManifest("""{"DisplayName":"Fortnite","InstallLocation":"D:/Epic Games/Fortnite","InstallSize":40000000000,"AppName":"Fortnite"}""")!;
        Assert.Equal("Fortnite", game.Name);
        Assert.Equal(40_000_000_000, game.SizeBytes);
        Assert.Equal("Epic Games", game.Source);
        Assert.Null(GameLibraries.ParseEpicManifest("""{"DisplayName":"X","InstallLocation":"D:/X","bIsIncompleteInstall":true}"""));
        Assert.Null(GameLibraries.ParseEpicManifest("pas du json"));
    }

    private static InstalledApp Reg(string name, string? publisher = null, string? location = null, long? size = null) =>
        new(name, publisher, null, null, size, location, "uninstall.exe", @"HKEY_LOCAL_MACHINE\X\" + name, false);

    [Theory]
    [InlineData("Blizzard Entertainment", null, true, "Battle.net")]
    [InlineData("Electronic Arts", null, true, "EA")]
    [InlineData("Ubisoft", null, true, "Ubisoft")]
    [InlineData("GOG.com", null, true, "GOG")]
    [InlineData("Riot Games, Inc", null, true, "Riot Games")]
    [InlineData("Unknown Studio", @"D:\XboxGames\Forza", true, "Xbox")]
    [InlineData("Microsoft Corporation", @"C:\Program Files\Office", false, null)]
    [InlineData("Mozilla", null, false, null)]
    public void Games_are_recognized_by_publisher_or_folder(string publisher, string? location, bool isGame, string? source)
    {
        var app = GameLibraries.Classify(Reg("App", publisher, location));
        Assert.Equal(isGame, app.IsGame);
        Assert.Equal(source, app.Source);
    }

    [Fact]
    public void Launcher_games_are_merged_with_windows_entries()
    {
        var registry = new[]
        {
            Reg("Counter-Strike 2", "Valve", @"C:\Steam\steamapps\common\Counter-Strike Global Offensive"),
            Reg("Discord", "Discord Inc."),
        };
        var steam = new InstalledApp("Counter-Strike 2", null, null, null, 35_000_000_000,
            @"C:\Steam\steamapps\common\Counter-Strike Global Offensive", "steam://uninstall/730", "steam:730", false, "Steam", true);
        var epic = new InstalledApp("Fortnite", null, null, null, 40_000_000_000, @"D:\Epic Games\Fortnite", null, "epic:Fortnite", false, "Epic Games", true);

        var merged = GameLibraries.Merge(registry, [steam, epic]);

        Assert.Equal(3, merged.Count);
        var cs = Assert.Single(merged, a => a.Name == "Counter-Strike 2");
        Assert.True(cs.FromRegistry);           // garde l'entrée de Windows, qui sait désinstaller
        Assert.Equal(35_000_000_000, cs.SizeBytes);
        Assert.True(cs.IsGame);
        Assert.False(Assert.Single(merged, a => a.Name == "Discord").IsGame);
        Assert.True(Assert.Single(merged, a => a.Name == "Fortnite").IsGame);
    }
}
