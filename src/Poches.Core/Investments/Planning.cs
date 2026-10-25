namespace Poches.Core.Investments;

public sealed record ContributionLine(string Id, decimal Value, decimal Target);

public sealed record ContributionPart(string Id, decimal Amount);

/// <summary>Percentiles of the simulated value at the end of a month (0 = today).</summary>
public sealed record ProjectionBand(int Month, double P10, double P50, double P90);

/// <summary><see cref="Probability"/> is the share of paths at or above the goal at the end; null without a goal.</summary>
public sealed record Projection(IReadOnlyList<ProjectionBand> Bands, double? Probability, double Contributed);

/// <summary>Splitting a contribution towards target weights, and projecting savings towards a goal. Same results as the JavaScript page.</summary>
public static class Planning
{
    /// <summary>
    /// Splits a new contribution so the lines move towards their targets without selling: each underweight line gets
    /// what it lacks at the new total, scaled down when the lacks exceed the amount. Lines without target get nothing.
    /// </summary>
    public static IReadOnlyList<ContributionPart> NextContribution(IReadOnlyList<ContributionLine> lines, decimal amount)
    {
        var withTarget = lines.Where(l => l.Target > 0).ToList();
        if (amount <= 0 || withTarget.Count == 0) return [];
        var total = lines.Sum(l => l.Value) + amount;
        var targetSum = withTarget.Sum(l => l.Target);
        var lacks = withTarget.Select(l => (l.Id, Lack: Math.Max(0, l.Target / targetSum * total - l.Value))).ToList();
        var sum = lacks.Sum(l => l.Lack);
        if (sum <= 0) return withTarget.Select(l => new ContributionPart(l.Id, amount * l.Target / targetSum)).ToList();
        var scale = Math.Min(1, amount / sum);
        var parts = lacks.Select(l => new ContributionPart(l.Id, l.Lack * scale)).ToList();
        // What the lacks don't use (all lines already close to target) goes pro rata to targets.
        var rest = amount - parts.Sum(p => p.Amount);
        if (rest > 0.005m)
            parts = parts.Select(p => p with { Amount = p.Amount + rest * withTarget.First(l => l.Id == p.Id).Target / targetSum }).ToList();
        return parts.Where(p => p.Amount > 0.005m).ToList();
    }

    /// <summary>
    /// Monthly simulation of the value: today's value plus a contribution each month, returns drawn from a log-normal
    /// law with the given yearly mean and volatility. Seeded, so the same inputs always draw the same fan.
    /// </summary>
    public static Projection Project(double value, double monthly, int months, double expectedReturn, double volatility, double goal, int paths = 2000, int seed = 42)
    {
        var random = Mulberry32(seed);
        var sigma = volatility / Math.Sqrt(12);
        var mu = Math.Log(1 + expectedReturn) / 12 - sigma * sigma / 2;
        var series = new double[months + 1][];
        for (var m = 0; m <= months; m++) series[m] = new double[paths];
        for (var p = 0; p < paths; p++)
        {
            var v = value;
            series[0][p] = v;
            for (var m = 1; m <= months; m++)
            {
                v = v * Math.Exp(mu + sigma * Normal(random)) + monthly;
                series[m][p] = v;
            }
        }
        var bands = series.Select((row, m) =>
        {
            var sorted = row.ToArray();
            Array.Sort(sorted);
            return new ProjectionBand(m, Quantile(sorted, 0.1), Quantile(sorted, 0.5), Quantile(sorted, 0.9));
        }).ToList();
        double? reached = goal > 0 ? series[months].Count(v => v >= goal) / (double)paths : null;
        return new Projection(bands, reached, value + monthly * months);
    }

    /// <summary>Monthly amount for which the median path reaches the goal (closed form on the median growth).</summary>
    public static double? RequiredMonthly(double value, double goal, int months, double expectedReturn, double volatility)
    {
        if (months <= 0 || goal <= 0) return null;
        var sigma = volatility / Math.Sqrt(12);
        var g = Math.Exp(Math.Log(1 + expectedReturn) / 12 - sigma * sigma / 2) - 1;
        var growth = Math.Pow(1 + g, months);
        var fromValue = value * growth;
        if (fromValue >= goal) return 0;
        var annuity = Math.Abs(g) < 1e-12 ? months : (growth - 1) / g;
        return (goal - fromValue) / annuity;
    }

    private static double Quantile(double[] sorted, double q) =>
        sorted[Math.Min(sorted.Length - 1, Math.Max(0, (int)Math.Round(q * (sorted.Length - 1), MidpointRounding.AwayFromZero)))];

    // The page's small deterministic generator (mulberry32), bit for bit.
    private static Func<double> Mulberry32(int seed) => () =>
    {
        unchecked
        {
            seed += 0x6D2B79F5;
            var t = (seed ^ (int)((uint)seed >> 15)) * (1 | seed);
            t = (t + (t ^ (int)((uint)t >> 7)) * (61 | t)) ^ t;
            return (uint)(t ^ (int)((uint)t >> 14)) / 4294967296.0;
        }
    };

    private static double Normal(Func<double> random)
    {
        double u = 0, v = 0;
        while (u == 0) u = random();
        while (v == 0) v = random();
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
    }
}
