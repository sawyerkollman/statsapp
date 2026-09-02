using Stats.Core.Fans;

namespace Stats.Core.Tests;

public class FileFanArmedMarkerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "StatsTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        if (File.Exists(_dir)) File.Delete(_dir);
    }

    [Fact]
    public void Exists_BeforeSet_ReturnsFalse()
    {
        var marker = new FileFanArmedMarker(_dir);
        Assert.False(marker.Exists());
    }

    [Fact]
    public void Set_ThenExists_ReturnsTrue()
    {
        var marker = new FileFanArmedMarker(_dir);

        Assert.True(marker.Set());
        Assert.True(marker.Exists());
    }

    [Fact]
    public void Clear_AfterSet_ExistsReturnsFalse()
    {
        var marker = new FileFanArmedMarker(_dir);
        marker.Set();

        marker.Clear();

        Assert.False(marker.Exists());
    }

    [Fact]
    public void Clear_WithoutSet_DoesNotThrow()
    {
        var marker = new FileFanArmedMarker(_dir);
        marker.Clear(); // no marker file yet — must be a silent no-op
        Assert.False(marker.Exists());
    }

    [Fact]
    public void Set_UnusablePath_ReturnsFalseAndDoesNotThrow()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dir)!);
        File.WriteAllText(_dir, "not a directory");
        var marker = new FileFanArmedMarker(_dir);

        Assert.False(marker.Set());
        Assert.False(marker.Exists());
    }
}
