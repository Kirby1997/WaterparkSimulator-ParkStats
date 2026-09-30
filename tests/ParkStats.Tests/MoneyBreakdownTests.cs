namespace ParkStats.Tests;

public class MoneyBreakdownTests
{
    [Fact]
    public void Entries_with_the_same_reason_are_summed()
    {
        var breakdown = MoneyBreakdown.From(new[]
        {
            new MoneyLine("Ticket", 50),
            new MoneyLine("Ticket", 50),
            new MoneyLine("Tips", 20),
        });

        Assert.Equal(new[] { new MoneyLine("Ticket", 100), new MoneyLine("Tips", 20) }, breakdown.Income);
    }

    [Fact]
    public void Income_and_expenses_are_split_and_largest_comes_first()
    {
        var breakdown = MoneyBreakdown.From(new[]
        {
            new MoneyLine("Tips", 350),
            new MoneyLine("Maintance", -480),
            new MoneyLine("Ticket", 2950),
            new MoneyLine("StaffSalary", -1200),
        });

        Assert.Equal(new[] { "Ticket", "Tips" }, breakdown.Income.Select(l => l.Reason));
        Assert.Equal(new[] { "StaffSalary", "Maintance" }, breakdown.Expenses.Select(l => l.Reason));
    }

    [Fact]
    public void Totals_and_net_add_up()
    {
        var breakdown = MoneyBreakdown.From(new[]
        {
            new MoneyLine("Ticket", 2950),
            new MoneyLine("Tips", 350),
            new MoneyLine("StaffSalary", -1200),
        });

        Assert.Equal(3300, breakdown.TotalIncome);
        Assert.Equal(-1200, breakdown.TotalExpenses);
        Assert.Equal(2100, breakdown.Net);
    }

    [Fact]
    public void A_reason_that_nets_to_zero_is_left_out()
    {
        var breakdown = MoneyBreakdown.From(new[]
        {
            new MoneyLine("Loan", 500),
            new MoneyLine("Loan", -500),
        });

        Assert.Empty(breakdown.Income);
        Assert.Empty(breakdown.Expenses);
    }

    [Fact]
    public void No_entries_gives_an_empty_breakdown()
    {
        var breakdown = MoneyBreakdown.From(Array.Empty<MoneyLine>());

        Assert.Equal(0, breakdown.Net);
    }
}
