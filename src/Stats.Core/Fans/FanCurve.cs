using Stats.Core.Settings;

namespace Stats.Core.Fans;

/// <summary>Immutable, validated temperature→percent curve. Linear between points, flat beyond the ends.</summary>
public sealed class FanCurve
{
    public const int MinPoints = 2;
    public const int MaxPoints = 8;
    public const float MinTemp = 0f, MaxTemp = 120f;
    private const float DuplicateTempTolerance = 0.5f;

    public static IReadOnlyList<FanPoint> DefaultPoints { get; } =
        new[] { new FanPoint(30, 30), new FanPoint(50, 45), new FanPoint(70, 75), new FanPoint(85, 100) };
    public static FanCurve Default { get; } = new(DefaultPoints);

    private FanCurve(IReadOnlyList<FanPoint> sortedPoints) => Points = sortedPoints;

    public IReadOnlyList<FanPoint> Points { get; }

    public static bool TryCreate(IEnumerable<FanPoint>? points, out FanCurve? curve)
    {
        curve = null;
        if (points is null) return false;
        var list = points.ToList();
        if (list.Count < MinPoints || list.Count > MaxPoints) return false;
        foreach (var p in list)
        {
            if (float.IsNaN(p.TempC) || float.IsNaN(p.Percent)) return false;
            if (p.TempC < MinTemp || p.TempC > MaxTemp || p.Percent < 0f || p.Percent > 100f) return false;
        }
        list.Sort((a, b) => a.TempC.CompareTo(b.TempC));
        for (int i = 1; i < list.Count; i++)
            if (list[i].TempC - list[i - 1].TempC < DuplicateTempTolerance) return false;
        curve = new FanCurve(list);
        return true;
    }

    public float Evaluate(float tempC)
    {
        var pts = Points;
        if (tempC <= pts[0].TempC) return pts[0].Percent;
        if (tempC >= pts[^1].TempC) return pts[^1].Percent;
        for (int i = 1; i < pts.Count; i++)
        {
            if (tempC <= pts[i].TempC)
            {
                var a = pts[i - 1]; var b = pts[i];
                float f = (tempC - a.TempC) / (b.TempC - a.TempC);
                return a.Percent + f * (b.Percent - a.Percent);
            }
        }
        return pts[^1].Percent;
    }
}
