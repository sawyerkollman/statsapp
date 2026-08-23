using Stats.Core.Fans;
namespace Stats.Core.Tests;
public class ConflictingFanSoftwareTests
{
    [Fact]
    public void Match_KnownStems_CaseInsensitive_DedupedFriendlyNames()
    {
        var found = ConflictingFanSoftware.Match(new[] { "MSI Center", "msi.centralserver", "FanControl", "explorer", "MSIAfterburner", "iCUE" });
        Assert.Equal(new[] { "MSI Center", "Fan Control", "MSI Afterburner", "Corsair iCUE" }, found);
    }
    [Fact]
    public void Match_IgnoresUnknown_AndExeSuffix()
    {
        Assert.Empty(ConflictingFanSoftware.Match(new[] { "chrome", "Stats.App", "gccx" }));
        Assert.Equal(new[] { "Gigabyte Control Center" }, ConflictingFanSoftware.Match(new[] { "GCC.exe" }));
        Assert.Equal(new[] { "ASUS Armoury Crate" }, ConflictingFanSoftware.Match(new[] { "ArmouryCrate.Service" }));
    }
}
