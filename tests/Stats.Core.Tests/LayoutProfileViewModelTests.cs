using Stats.Core.Metrics;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public class LayoutProfileViewModelTests
{
    private static (DashboardViewModel Vm, AppSettings Settings, Func<int> Saves) Make()
    {
        var settings = new AppSettings { DashboardMetrics = { "cpu", "gpu" }, ShowCoreMatrix = false };
        var store = new MetricStore(new[]
        {
            new MetricDefinition("cpu", "CPU", MetricGroup.Cpu, "CPU", "%"),
            new MetricDefinition("gpu", "GPU", MetricGroup.Gpu, "GPU", "%"),
        });
        int saves = 0;
        return (new DashboardViewModel(store, settings, () => saves++), settings, () => saves);
    }

    [Fact]
    public void SaveApplyRevertRenameAndDelete_KeepProfileStateAndSaveOncePerOperation()
    {
        var (vm, settings, saves) = Make();
        vm.SaveLayoutProfileCommand.Execute("Desk");
        Assert.Equal("Desk", vm.ActiveLayoutProfileName);
        Assert.Equal(1, saves());

        vm.SetTileSize("cpu", TileSize.S);
        Assert.True(vm.IsLayoutModified);
        vm.RevertLayoutProfileCommand.Execute(null);
        Assert.False(vm.IsLayoutModified);
        vm.RenameLayoutProfile("Desk", "Gaming");
        Assert.Equal("Gaming", vm.ActiveLayoutProfileName);
        vm.DeleteLayoutProfileCommand.Execute("Gaming");
        Assert.Null(vm.ActiveLayoutProfileName);
        Assert.Empty(settings.LayoutProfiles);
    }

    [Fact]
    public void ApplyGameModeLayout_UsesNamedPick_AndUnknownProfileDoesNothing()
    {
        var (vm, settings, saves) = Make();
        vm.SaveLayoutProfileCommand.Execute("Desk");
        vm.SetTileSize("cpu", TileSize.S);
        vm.SaveLayoutProfileCommand.Execute("Gaming");
        vm.ApplyLayoutProfile("Desk");
        settings.GameModeGamingLayoutProfile = "Gaming";
        var before = saves();

        vm.ApplyGameModeLayout(true);
        Assert.Equal("Gaming", vm.ActiveLayoutProfileName);
        Assert.True(vm.ApplyLayoutProfile("Missing") is false);
        Assert.Equal(before + 1, saves());
    }
}
