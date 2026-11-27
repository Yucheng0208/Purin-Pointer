namespace PurinPointer.Core;

/// <summary>
/// Hotspot calibration: the user lines the tip they see up with a target dot and clicks. The click
/// lands on the current hotspot, so the gap from click to dot is how far the hotspot must move.
/// </summary>
public static class Calibration
{
    /// <summary>Targets as fractions of the calibration pad, visited in this order.</summary>
    public static readonly (double X, double Y)[] Targets = [(0.2, 0.28), (0.8, 0.28), (0.5, 0.5), (0.2, 0.74), (0.8, 0.74)];

    /// <summary>
    /// Averages the click offsets (dot minus click, in the same units as <paramref name="width"/>/<paramref name="height"/>,
    /// the cursor's displayed size), leaving out the one furthest from the rest when there are four or more.
    /// Returns the mean offset and the corrected hotspot as a fraction of the image.
    /// </summary>
    public static (double OffsetX, double OffsetY, double HotX, double HotY) Solve(
        IReadOnlyList<(double X, double Y)> offsets, (double X, double Y) hotspot, double width, double height)
    {
        if (offsets.Count == 0) return (0, 0, hotspot.X, hotspot.Y);
        var used = offsets.ToList();
        if (used.Count >= 4)
        {
            double mx = used.Average(o => o.X), my = used.Average(o => o.Y);
            used.Remove(used.MaxBy(o => (o.X - mx) * (o.X - mx) + (o.Y - my) * (o.Y - my)));
        }
        double dx = used.Average(o => o.X), dy = used.Average(o => o.Y);
        double hx = Math.Clamp(hotspot.X + dx / Math.Max(1, width), 0, 0.999999);
        double hy = Math.Clamp(hotspot.Y + dy / Math.Max(1, height), 0, 0.999999);
        return (dx, dy, hx, hy);
    }
}
