namespace ParkStats.Tests;

public class TmpMarkupTests
{
    private static string Render(params Row[] rows) => TmpMarkup.Render(rows);

    [Fact]
    public void A_normal_single_cell_row_is_plain_text()
    {
        Assert.Equal("Hello", Render(Row.Of(RowKind.Normal, "Hello")));
    }

    [Fact]
    public void Rows_are_separated_by_line_breaks()
    {
        Assert.Equal("One\nTwo", Render(Row.Of(RowKind.Normal, "One"), Row.Of(RowKind.Normal, "Two")));
    }

    [Fact]
    public void The_second_of_two_cells_starts_at_a_fixed_column()
    {
        Assert.Equal("Balance<pos=62%>12,400", Render(Row.Of(RowKind.Normal, "Balance", "12,400")));
    }

    [Fact]
    public void Seven_cells_use_the_table_columns_in_smaller_text()
    {
        var text = Render(Row.Of(RowKind.Normal, "A", "B", "C", "D", "E", "F", "G"));

        Assert.Equal("<size=80%>A<pos=34%>B<pos=48%>C<pos=57%>D<pos=68%>E<pos=79%>F<pos=91%>G</size>", text);
    }

    [Theory]
    [InlineData(RowKind.Good, "#5CFF8F")]
    [InlineData(RowKind.Bad, "#FF6B6B")]
    [InlineData(RowKind.Muted, "#9AD7FF")]
    public void Coloured_kinds_wrap_the_line_in_a_colour(RowKind kind, string colour)
    {
        Assert.Equal($"<color={colour}>Net<pos=62%>+5</color>", Render(Row.Of(kind, "Net", "+5")));
    }

    [Fact]
    public void A_header_is_bold_and_coloured()
    {
        Assert.Equal("<color=#FFD84A><b>Today</b></color>", Render(Row.Of(RowKind.Header, "Today")));
    }

    [Fact]
    public void A_header_after_other_rows_gets_a_blank_line_above_it()
    {
        var text = Render(Row.Of(RowKind.Normal, "One"), Row.Of(RowKind.Header, "Loans"));

        Assert.Equal("One\n\n<color=#FFD84A><b>Loans</b></color>", text);
    }

    [Fact]
    public void Angle_brackets_in_cell_text_cannot_inject_tags()
    {
        Assert.Equal("size=200Big", Render(Row.Of(RowKind.Normal, "<size=200>Big")));
    }

    [Fact]
    public void An_empty_row_is_a_blank_line()
    {
        Assert.Equal("One\n\nTwo", Render(Row.Of(RowKind.Normal, "One"), Row.Of(RowKind.Normal, ""), Row.Of(RowKind.Normal, "Two")));
    }

    [Fact]
    public void No_rows_render_as_empty_text()
    {
        Assert.Equal("", Render());
    }
}
