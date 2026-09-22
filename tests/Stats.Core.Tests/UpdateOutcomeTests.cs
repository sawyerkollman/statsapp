using Stats.Core.Startup;

namespace Stats.Core.Tests;

public class UpdateOutcomeTests
{
    [Fact]
    public void SuccessRequiresExpectedVersionAndRebootIsNotSilentSuccess()
    {
        string[] args = ["--update-exit-code", "0", "--update-expected", "v1.11.0-beta.7"];
        Assert.Equal(-1, StartupArgs.InstallerOutcome(args, "1.11.0-beta.6+commit"));
        Assert.Equal(0, StartupArgs.InstallerOutcome(args, "1.11.0-beta.7+commit"));
        Assert.Contains("restart", StartupArgs.InstallerFailure(3010)!);
    }
    [Theory]
    [InlineData("0", false)]
    [InlineData("7", true)]
    [InlineData("abc", false)]
    public void InstallerOutcomeIsNumericAndFailuresRemainActionable(string code, bool failed)
    {
        var result = StartupArgs.InstallerFailure(["--update-exit-code", code]);
        Assert.Equal(failed, result is not null);
        if (failed) Assert.Contains("retry", result!);
        Assert.Null(StartupArgs.InstallerFailure(["--update-exit-code"]));
    }
}
