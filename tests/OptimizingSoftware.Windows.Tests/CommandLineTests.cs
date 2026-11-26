namespace OptimizingSoftware.Windows.Tests;

public class CommandLineTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\unins000.exe\" /SILENT", "C:\\Program Files\\App\\unins000.exe", "/SILENT")]
    [InlineData("\"C:\\Program Files\\App\\uninstall.exe\"", "C:\\Program Files\\App\\uninstall.exe", "")]
    [InlineData("MsiExec.exe /X{1234}", "MsiExec.exe", "/X{1234}")]
    public void Separe_programme_et_arguments(string command, string exe, string args)
    {
        var (e, a) = CommandLine.Split(command);
        Assert.Equal(exe, e);
        Assert.Equal(args, a);
    }
}
