using Stats.Core.Platform;

namespace Stats.Core.Tests;

public class ProgramFilesPathTests
{
    private static readonly string[] Roots = { @"C:\Program Files", @"C:\Program Files (x86)" };

    [Theory]
    [InlineData(@"C:\Program Files\Stats\Stats.App.exe", true)]
    [InlineData(@"c:\program files\stats\stats.app.exe", true)]
    [InlineData(@"C:\Program Files (x86)\Stats\Stats.App.exe", true)]
    [InlineData(@"C:\Program Files\Stats\..\Stats\Stats.App.exe", true)]
    [InlineData(@"C:\Program Files", false)]
    [InlineData(@"C:\Program Files Evil\Stats.App.exe", false)]
    [InlineData(@"C:\Program Files\Stats\..\..\Users\Ada\evil.exe", false)]
    [InlineData(@"C:\Users\Ada\src\Stats.App.exe", false)]
    [InlineData(@"Stats.App.exe", false)]
    [InlineData(@"\\server\share\Stats.App.exe", false)]
    [InlineData("", false)]
    public void IsUnder_AdminOnlyRoots(string path, bool expected)
    {
        Assert.Equal(expected, ProgramFilesPath.IsUnder(path, Roots));
    }
}
