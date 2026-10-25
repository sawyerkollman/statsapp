using Stats.Core.Frames;
using Stats.Core.Platform;

namespace Stats.Core.Tests;

public class PresentMonLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stats-pm-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Find_ShippedBinary_IgnoresVendorEvenWhenWalkIsAllowed()
    {
        var app = Path.Combine(_root, "app");
        Directory.CreateDirectory(app);
        var shipped = Path.Combine(app, PresentMonLocator.ShippedFileName);
        File.WriteAllText(shipped, "shipped");
        var vendor = Path.Combine(_root, "installer", "vendor");
        Directory.CreateDirectory(vendor);
        File.WriteAllText(Path.Combine(vendor, "PresentMon-9.exe"), "vendor");

        Assert.Equal(shipped, PresentMonLocator.Find(app, allowVendorWalk: true));
    }

    [Fact]
    public void Find_WalkDisabled_DoesNotReturnParentVendor()
    {
        var app = Path.Combine(_root, "app");
        Directory.CreateDirectory(app);
        var vendor = Path.Combine(_root, "installer", "vendor");
        Directory.CreateDirectory(vendor);
        File.WriteAllText(Path.Combine(vendor, "PresentMon-9.exe"), "vendor");

        Assert.Null(PresentMonLocator.Find(app, allowVendorWalk: false));
    }

    [Fact]
    public void Find_WalkEnabled_ReturnsRepoVendorExe()
    {
        var app = Path.Combine(_root, "src", "Stats.App", "bin", "Debug", "net8.0-windows");
        Directory.CreateDirectory(app);
        var exe = Path.Combine(_root, "installer", "vendor", "PresentMon-1.10.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "v");

        Assert.Equal(exe, PresentMonLocator.Find(app, allowVendorWalk: true));
    }

    [Fact]
    public void AllowsVendorWalk_ReleaseBuild_IsFalse()
    {
        Assert.False(PresentMonLocator.AllowsVendorWalk(@"D:\src\stats\bin\Debug\net8.0-windows", debugBuild: false));
    }

    [Fact]
    public void AllowsVendorWalk_DebugUnderProgramFiles_IsFalse()
    {
        Assert.False(PresentMonLocator.AllowsVendorWalk(@"C:\Program Files\Stats", debugBuild: true));
        Assert.False(PresentMonLocator.AllowsVendorWalk(@"C:\Program Files (x86)\Stats", debugBuild: true));
    }

    [Fact]
    public void AllowsVendorWalk_DebugSourceTree_IsTrue()
    {
        Assert.True(PresentMonLocator.AllowsVendorWalk(@"D:\src\stats\bin\Debug\net8.0-windows", debugBuild: true));
    }

    [Fact]
    public void Find_ForcedWalk_StillRefusesInstalledLayout()
    {
        Assert.Null(PresentMonLocator.Find(@"C:\Program Files\Stats", allowVendorWalk: true));
    }

    [Fact]
    public void IsDriveRoot_MatchesDriveRootOnly()
    {
        Assert.True(ProgramFilesPath.IsDriveRoot(@"C:\"));
        Assert.True(ProgramFilesPath.IsDriveRoot(@"D:/"));
        Assert.False(ProgramFilesPath.IsDriveRoot(@"C:\installer"));
        Assert.False(ProgramFilesPath.IsDriveRoot(@"C:\Program Files\Stats"));
    }
}
