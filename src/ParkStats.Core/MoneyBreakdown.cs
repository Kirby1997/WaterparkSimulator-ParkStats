namespace ParkStats.Core;

/// <summary>Groups raw money changes by reason and splits them into income and expenses.</summary>
public sealed class MoneyBreakdown
{
    public IReadOnlyList<MoneyLine> Income { get; private init; } = Array.Empty<MoneyLine>();
    public IReadOnlyList<MoneyLine> Expenses { get; private init; } = Array.Empty<MoneyLine>();
    public double TotalIncome { get; private init; }
    public double TotalExpenses { get; private init; }
    public double Net => TotalIncome + TotalExpenses;

    public static MoneyBreakdown From(IEnumerable<MoneyLine> lines)
    {
        var byReason = lines
            .GroupBy(l => l.Reason)
            .Select(g => new MoneyLine(g.Key, g.Sum(l => l.Amount)))
            .ToList();

        var income = byReason.Where(l => l.Amount > 0).OrderByDescending(l => l.Amount).ToList();
        var expenses = byReason.Where(l => l.Amount < 0).OrderBy(l => l.Amount).ToList();

        return new MoneyBreakdown
        {
            Income = income,
            Expenses = expenses,
            TotalIncome = income.Sum(l => l.Amount),
            TotalExpenses = expenses.Sum(l => l.Amount),
        };
    }
}
