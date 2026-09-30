namespace ParkStats.Core;

/// <summary>Brings raw game values into the shapes the rest of the mod expects.</summary>
public static class Normalize
{
    // Above this a value cannot be a 0..1 fraction, so it is read as a percentage.
    private const double LargestFraction = 1.5;

    private static readonly HashSet<string> CostReasons = new()
    {
        "Building", "LoanPayment", "StaffSalary", "Maintance", "Fine", "Research", "StaffTax", "TicketRefund",
    };

    /// <summary>Accepts a value on either a 0..1 or a 0..100 scale and returns it as 0..1.</summary>
    public static double? Fraction(double? raw)
    {
        if (raw is not { } value) return null;
        if (value > LargestFraction) value /= 100;
        return Math.Clamp(value, 0, 1);
    }

    /// <summary>
    /// Makes costs negative. If any amount is already negative the game records signed amounts
    /// and the lines are returned unchanged; otherwise the sign is taken from the reason.
    /// </summary>
    public static IReadOnlyList<MoneyLine> Signs(IReadOnlyList<MoneyLine> lines)
    {
        if (lines.Any(l => l.Amount < 0)) return lines;

        return lines
            .Select(l => CostReasons.Contains(l.Reason) ? l with { Amount = -l.Amount } : l)
            .ToList();
    }

    /// <summary>Counts added since a baseline. A count below its baseline means the counter restarted.</summary>
    public static IReadOnlyList<CountLine> Since(
        IReadOnlyDictionary<string, int> baseline, IReadOnlyDictionary<string, int> current)
    {
        var added = new List<CountLine>();
        foreach (var (label, count) in current)
        {
            var before = baseline.TryGetValue(label, out var value) ? value : 0;
            var delta = count >= before ? count - before : count;
            if (delta > 0) added.Add(new CountLine(label, delta));
        }
        return added;
    }
}
