namespace ParkStats.Tests;

public class NormalizeTests
{
    [Theory]
    [InlineData(0.71, 0.71)]
    [InlineData(1.0, 1.0)]
    [InlineData(71.0, 0.71)]
    [InlineData(100.0, 1.0)]
    [InlineData(0.0, 0.0)]
    public void Fraction_accepts_both_zero_to_one_and_percent_scales(double raw, double expected)
    {
        Assert.Equal(expected, Normalize.Fraction(raw)!.Value, 6);
    }

    [Theory]
    [InlineData(-5.0, 0.0)]
    [InlineData(140.0, 1.0)]
    public void Fraction_is_clamped(double raw, double expected)
    {
        Assert.Equal(expected, Normalize.Fraction(raw));
    }

    [Fact]
    public void Fraction_of_nothing_is_nothing()
    {
        Assert.Null(Normalize.Fraction(null));
    }

    [Fact]
    public void Signs_are_left_alone_when_the_game_already_records_costs_as_negative()
    {
        var lines = new[] { new MoneyLine("Ticket", 100), new MoneyLine("StaffSalary", -40) };

        Assert.Equal(lines, Normalize.Signs(lines));
    }

    [Fact]
    public void Signs_are_applied_by_reason_when_the_game_records_only_magnitudes()
    {
        var lines = new[]
        {
            new MoneyLine("Ticket", 100),
            new MoneyLine("StaffSalary", 40),
            new MoneyLine("Maintance", 10),
            new MoneyLine("Tips", 5),
        };

        Assert.Equal(
            new[]
            {
                new MoneyLine("Ticket", 100),
                new MoneyLine("StaffSalary", -40),
                new MoneyLine("Maintance", -10),
                new MoneyLine("Tips", 5),
            },
            Normalize.Signs(lines));
    }

    [Theory]
    [InlineData("Building")]
    [InlineData("LoanPayment")]
    [InlineData("StaffSalary")]
    [InlineData("Maintance")]
    [InlineData("Fine")]
    [InlineData("Research")]
    [InlineData("StaffTax")]
    [InlineData("TicketRefund")]
    public void Cost_reasons_become_negative(string reason)
    {
        var result = Normalize.Signs(new[] { new MoneyLine(reason, 25) });

        Assert.Equal(-25, Assert.Single(result).Amount);
    }

    [Theory]
    [InlineData("Ticket")]
    [InlineData("Attraction")]
    [InlineData("Tips")]
    [InlineData("Loan")]
    [InlineData("Missions")]
    [InlineData("Misc")]
    public void Income_reasons_stay_positive(string reason)
    {
        var result = Normalize.Signs(new[] { new MoneyLine(reason, 25) });

        Assert.Equal(25, Assert.Single(result).Amount);
    }

    [Fact]
    public void Since_reports_only_what_was_added_after_the_baseline()
    {
        var baseline = new Dictionary<string, int> { ["Bored"] = 10, ["Thirsty"] = 4 };
        var current = new Dictionary<string, int> { ["Bored"] = 13, ["Thirsty"] = 4, ["Hungry"] = 2 };

        Assert.Equal(
            new[] { new CountLine("Bored", 3), new CountLine("Hungry", 2) },
            Normalize.Since(baseline, current).OrderBy(l => l.Label));
    }

    [Fact]
    public void Since_uses_the_current_count_when_it_dropped_below_the_baseline()
    {
        var baseline = new Dictionary<string, int> { ["Bored"] = 10 };
        var current = new Dictionary<string, int> { ["Bored"] = 2 };

        Assert.Equal(new[] { new CountLine("Bored", 2) }, Normalize.Since(baseline, current));
    }
}
