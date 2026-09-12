using Stats.Core.Frames;
using Stats.Core.Metrics;

namespace Stats.Core.Tests;

public class GameMetricRolesTests
{
    [Fact]
    public void RoleOf_FrameMetricsDefinitions_YieldsExactlyOneOfEachRole()
    {
        var roles = FrameMetrics.Definitions.Select(GameMetricRoles.RoleOf).ToList();

        Assert.Single(roles, r => r == GameMetricRole.Fps);
        Assert.Single(roles, r => r == GameMetricRole.LowFps);
        Assert.Single(roles, r => r == GameMetricRole.FrameTime);
    }

    [Fact]
    public void RoleOf_NonGameGroup_IsNone()
    {
        var def = new MetricDefinition("cpu.fake.fps", "Fake FPS", MetricGroup.Cpu, "Ryzen", "fps");
        Assert.Equal(GameMetricRole.None, GameMetricRoles.RoleOf(def));
    }

    [Fact]
    public void RoleOf_GameFpsWithoutOnePercent_IsFps()
    {
        var def = new MetricDefinition("gallery.inverted", "Simulated FPS", MetricGroup.Game, "Gallery", "fps");
        Assert.Equal(GameMetricRole.Fps, GameMetricRoles.RoleOf(def));
    }

    [Fact]
    public void RoleOf_GameOtherUnit_IsNone()
    {
        var def = new MetricDefinition("game.other", "Something Else", MetricGroup.Game, "Foreground app", "%");
        Assert.Equal(GameMetricRole.None, GameMetricRoles.RoleOf(def));
    }

    [Fact]
    public void Find_ReturnsFirstMatch_OrNull()
    {
        var defs = new List<MetricDefinition>
        {
            new("a", "A", MetricGroup.Cpu, "Ryzen", "%"),
            new("fps.avg", "FPS", MetricGroup.Game, "Foreground app", "fps"),
            new("fps.avg2", "FPS Duplicate", MetricGroup.Game, "Foreground app", "fps"),
        };

        var found = GameMetricRoles.Find(defs, GameMetricRole.Fps);
        Assert.Same(defs[1], found);

        Assert.Null(GameMetricRoles.Find(defs, GameMetricRole.FrameTime));
    }
}
