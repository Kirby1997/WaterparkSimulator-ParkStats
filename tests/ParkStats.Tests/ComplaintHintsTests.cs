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
    [InlineData("PoorService", "control panel")]
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
            "An attraction they used was dirty. It needs cleaning. Mostly at: Kids Pool (4), Wave Slide (1).",
            ComplaintHints.Note("UsedDirtyAttraction", new[] { new CountLine("Kids Pool", 4), new CountLine("Wave Slide", 1) }));
    }

    [Fact]
    public void Places_are_left_out_for_complaints_that_are_not_about_an_attraction()
    {
        // A guest who slips was merely on the way to something; naming it would mislead.
        Assert.Equal("They slipped on a wet floor.", ComplaintHints.Note("Slipped", new[] { new CountLine("Park Bin", 1) }));
    }

    [Fact]
    public void Only_the_three_commonest_places_are_named()
    {
        var places = Enumerable.Range(1, 5).Select(i => new CountLine("Place" + i, i)).ToArray();

        Assert.EndsWith("Mostly at: Place5 (5), Place4 (4), Place3 (3).", ComplaintHints.Note("UsedWornAttraction", places));
    }

    [Fact]
    public void A_note_without_known_places_is_just_the_hint()
    {
        Assert.Equal("They could not find a trash bin in time.", ComplaintHints.Note("LowTrash", Array.Empty<CountLine>()));
    }

    [Fact]
    public void A_note_with_nothing_to_say_is_empty()
    {
        Assert.Null(ComplaintHints.Note("SomethingNew", Array.Empty<CountLine>()));
    }
}
