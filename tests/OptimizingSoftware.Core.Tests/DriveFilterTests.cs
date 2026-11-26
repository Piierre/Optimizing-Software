using OptimizingSoftware.Core.Files;

namespace OptimizingSoftware.Core.Tests;

public class DriveFilterTests
{
    [Fact]
    public void Keeps_only_folders_on_selected_drives()
    {
        // Les chemins Windows ne sont reconnus comme tels que sous Windows : on teste avec la racine du système courant.
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var folders = new[] { Path.Combine(root, "Users", "Pierre", "Documents"), Path.Combine(root, "Jeux") };

        Assert.Equal(folders, DriveFilter.Keep(folders, [root]));
        Assert.Empty(DriveFilter.Keep(folders, []));
        Assert.Empty(DriveFilter.Keep(folders, ["Q:\\"]));
    }
}
