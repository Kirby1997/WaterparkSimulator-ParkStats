namespace ParkStats.Tests;

public class ComplaintHintsTests
{
    [Theory]
    [InlineData("Tired", "lounger")]
    [InlineData("SleptOnFloor", "lounger")]
    [InlineData("LowTrash", "bin")]
    [InlineData("UsedDirtyAttraction", "clean")]
    [InlineData("UsedWornAttraction", "repair")]
    [InlineData("WaitedTooLong", "served")]
    [InlineData("PoorService", "staff")]
    public void Known_complaints_say_what_causes_them(string thought, string expectedWord)
    {
        Assert.Contains(expectedWord, ComplaintHints.For(thought), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unknown_complaint_has_no_hint()
    {
        Assert.Null(ComplaintHints.For("SomethingNew"));
    }

    [Fact]
    public void A_note_joins_the_hint_with_where_it_happened()
    {
        Assert.Equal(
            "A member of staff served them badly. Mostly at: Ice Cream Stand (4), Drinks Vending Machine (1).",
            ComplaintHints.Note("PoorService", new[] { new CountLine("Ice Cream Stand", 4), new CountLine("Drinks Vending Machine", 1) }));
    }

    [Fact]
    public void A_note_without_known_places_is_just_the_hint()
    {
        Assert.Equal("They could not find a trash bin in time.", ComplaintHints.Note("LowTrash", Array.Empty<CountLine>()));
    }

    [Fact]
    public void A_note_for_an_unknown_complaint_still_says_where()
    {
        Assert.Equal("Mostly at: Wave Slide (2).", ComplaintHints.Note("SomethingNew", new[] { new CountLine("Wave Slide", 2) }));
    }

    [Fact]
    public void A_note_with_nothing_to_say_is_empty()
    {
        Assert.Null(ComplaintHints.Note("SomethingNew", Array.Empty<CountLine>()));
    }

    [Fact]
    public void Only_the_three_commonest_places_are_named()
    {
        var places = Enumerable.Range(1, 5).Select(i => new CountLine("Place" + i, i)).ToArray();

        Assert.Equal("Mostly at: Place5 (5), Place4 (4), Place3 (3).", ComplaintHints.Note("SomethingNew", places));
    }
}
