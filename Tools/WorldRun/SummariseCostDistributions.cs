using System.Globalization;

/// <summary>
/// The arithmetic the three cost modes share: a nearest-rank percentile, a spread over repeats, and a
/// least-squares slope on a log-log plot.
///
/// Nearest-rank rather than interpolated, because every other cost row in this folder uses it
/// (<c>GradeThePlayMeasures.Percentile</c>) and two rows filed side by side must mean the same thing by
/// "p99". It is also why a p99 over fewer than a hundred samples is refused by the callers rather than
/// computed: under a hundred, nearest-rank p99 is the maximum.
/// </summary>
internal static class SummariseCostDistributions
{
    /// <summary>The nearest-rank percentile of a sample, sorting a copy; zero for an empty sample.</summary>
    public static double Percentile(IEnumerable<double> values, double fraction)
    {
        double[] sorted = values.Where(double.IsFinite).OrderBy(v => v).ToArray();
        return sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(fraction * sorted.Length) - 1, 0, sorted.Length - 1)];
    }

    /// <summary>The machine's load averages as the kernel reports them, for the sentence beside every timing:
    /// the other lanes build on this machine while these modes run, and a timing without its load is not
    /// comparable with anything. It is context rather than an input, so an unreadable load is said rather
    /// than failing a run that is otherwise whole.</summary>
    public static string MachineLoad()
    {
        try
        {
            using var probe = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("sysctl", "-n vm.loadavg")
                { RedirectStandardOutput = true, UseShellExecute = false });
            if (probe == null) return "unread";
            string text = probe.StandardOutput.ReadToEnd().Trim().Trim('{', '}').Trim();
            probe.WaitForExit();
            return text.Length == 0 ? "unread" : text;
        }
        catch (Exception failure)
        {
            return "unread (" + failure.GetType().Name + ")";
        }
    }

    public static double Mean(IReadOnlyCollection<double> values) => values.Count == 0 ? 0 : values.Average();

    /// <summary>"a, b, c (spread d)" for a set of repeats, so a row's message carries every draw it averaged.</summary>
    public static string Draws(IReadOnlyCollection<double> values, string format)
        => string.Join(", ", values.Select(v => v.ToString(format, CultureInfo.InvariantCulture)))
            + (values.Count > 1 ? $" (spread {(values.Max() - values.Min()).ToString(format, CultureInfo.InvariantCulture)})" : "");

    /// <summary>
    /// The slope of log(y) on log(x) by least squares, with its coefficient of determination, or null
    /// when fewer than two points are positive on both axes — a marginal cost at or below zero has no
    /// logarithm, and a slope through one point is not a fit.
    /// </summary>
    public static (double Exponent, double RSquared, int Points)? LogLogSlope(IReadOnlyList<(double X, double Y)> points)
    {
        var usable = points.Where(p => p.X > 0 && p.Y > 0).Select(p => (X: Math.Log(p.X), Y: Math.Log(p.Y))).ToList();
        if (usable.Count < 2) return null;
        double mx = usable.Average(p => p.X), my = usable.Average(p => p.Y);
        double sxx = usable.Sum(p => (p.X - mx) * (p.X - mx)), sxy = usable.Sum(p => (p.X - mx) * (p.Y - my));
        double syy = usable.Sum(p => (p.Y - my) * (p.Y - my));
        if (sxx == 0) return null;
        double slope = sxy / sxx;
        double r2 = syy == 0 ? 1 : sxy * sxy / (sxx * syy);
        return (slope, r2, usable.Count);
    }
}
