namespace ParkStats.Tests;

public class FormatTests
{
    [Theory]
    [InlineData(2330.0, "2,330")]
    [InlineData(-1880.0, "-1,880")]
    [InlineData(0.0, "0")]
    [InlineData(12.5, "13")]
    [InlineData(-0.4, "0")]
    public void Money_rounds_to_whole_units_with_thousands_separator(double amount, string expected)
    {
        Assert.Equal(expected, Format.Money(amount));
    }

    [Fact]
    public void Money_without_a_value_is_not_available()
    {
        Assert.Equal("n/a", Format.Money(null));
    }

    [Theory]
    [InlineData(2330.0, "+2,330")]
    [InlineData(-1880.0, "-1,880")]
    [InlineData(0.0, "0")]
    public void Signed_shows_a_plus_for_gains(double amount, string expected)
    {
        Assert.Equal(expected, Format.Signed(amount));
    }

    [Theory]
    [InlineData(0.71, "71%")]
    [InlineData(1.0, "100%")]
    [InlineData(0.005, "1%")]
    public void Percent_takes_a_fraction(double fraction, string expected)
    {
        Assert.Equal(expected, Format.Percent(fraction));
    }

    [Fact]
    public void Percent_without_a_value_is_not_available()
    {
        Assert.Equal("n/a", Format.Percent(null));
    }

    [Fact]
    public void Ratio_joins_current_and_max()
    {
        Assert.Equal("59/75", Format.Ratio(59, 75));
    }

    [Fact]
    public void Ratio_marks_a_missing_side()
    {
        Assert.Equal("59/n/a", Format.Ratio(59, null));
    }

    [Theory]
    [InlineData("StaffSalary", "Staff salary")]
    [InlineData("TicketRefund", "Ticket refund")]
    [InlineData("Ticket", "Ticket")]
    [InlineData("Maintance", "Maintenance")]
    public void Reason_turns_game_enum_names_into_labels(string reason, string expected)
    {
        Assert.Equal(expected, Format.Reason(reason));
    }
}
