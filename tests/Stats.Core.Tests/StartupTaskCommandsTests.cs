using Stats.Core.Startup;

namespace Stats.Core.Tests;

public class StartupTaskCommandsTests
{
    private const string ExePath = @"C:\Program Files\Stats\Stats.App.exe";

    [Fact]
    public void Query_UsesTaskName()
    {
        Assert.Equal(new[] { "/Query", "/TN", "Stats" }, StartupTaskCommands.Query());
    }

    [Fact]
    public void Delete_ForcesAndUsesTaskName()
    {
        Assert.Equal(new[] { "/Delete", "/F", "/TN", "Stats" }, StartupTaskCommands.Delete());
    }

    [Fact]
    public void Create_MatchesInstallerShape_QuotedExeAndMinimizedFlag()
    {
        var args = StartupTaskCommands.Create(ExePath);

        Assert.Equal(new[]
        {
            "/Create", "/F", "/TN", "Stats",
            "/TR", "\"C:\\Program Files\\Stats\\Stats.App.exe\" --minimized",
            "/SC", "ONLOGON",
            "/RL", "HIGHEST",
            "/IT",
        }, args);
    }

    [Fact]
    public void Create_TrValue_QuotesOnlyTheExecutablePath()
    {
        var args = StartupTaskCommands.Create(ExePath);
        var trIndex = args.ToList().IndexOf("/TR");
        var trValue = args[trIndex + 1];

        Assert.StartsWith("\"" + ExePath + "\"", trValue);
        Assert.EndsWith(StartupArgs.MinimizedFlag, trValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankPath_Throws(string path)
    {
        Assert.Throws<ArgumentException>(() => StartupTaskCommands.Create(path));
    }

    [Fact]
    public void Create_ProgramFilesX86_IsAllowed()
    {
        var args = StartupTaskCommands.Create(@"C:\Program Files (x86)\Stats\Stats.App.exe");

        Assert.Contains("/RL", args);
        Assert.Contains("HIGHEST", args);
    }

    [Theory]
    [InlineData(@"C:\Users\Ada\src\Stats\Stats.App.exe")]
    [InlineData(@"C:\Program Files\Stats\..\..\Users\Ada\evil.exe")]
    [InlineData(@"C:\Program Files Evil\Stats.App.exe")]
    [InlineData(@"Stats.App.exe")]
    public void Create_PathOutsideProgramFiles_Throws(string path)
    {
        var ex = Assert.Throws<ArgumentException>(() => StartupTaskCommands.Create(path));
        Assert.Contains("Program Files", ex.Message);
    }

    [Fact]
    public void TaskName_IsStats()
    {
        Assert.Equal("Stats", StartupTaskCommands.TaskName);
    }
}
