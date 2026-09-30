namespace ParkStats.Tests;

public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ParkStatsTests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "history.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static DayRecord Day(int day, double income = 0) => new() { Day = day, Income = income };

    [Fact]
    public void A_day_record_is_built_from_the_state_of_the_park_at_closing()
    {
        var park = new ParkSnapshot
        {
            Day = 7,
            VisitorsToday = 120,
            Satisfaction = 0.71,
            MoneyToday = new[] { new MoneyLine("Ticket", 4210), new MoneyLine("StaffSalary", -1880) },
            Needs = new[] { new NeedStat("Fun", 0.8, 0, 0), new NeedStat("Trash", null, 0, 0) },
        };

        var record = DayRecord.From(park);

        Assert.Equal(7, record.Day);
        Assert.Equal(120, record.Visitors);
        Assert.Equal(4210, record.Income);
        Assert.Equal(-1880, record.Expenses);
        Assert.Equal(0.71, record.Satisfaction);
        Assert.Equal(new Dictionary<string, double> { ["Fun"] = 0.8 }, record.Needs);
    }

    [Fact]
    public void A_day_record_prefers_the_games_figure_for_the_whole_day()
    {
        // Once the park has emptied the live satisfaction reads zero; the day's own figure does not.
        var park = new ParkSnapshot { Day = 7, Satisfaction = 0, DaySatisfaction = 0.64 };

        Assert.Equal(0.64, DayRecord.From(park).Satisfaction);
    }

    [Fact]
    public void A_store_with_no_file_starts_empty()
    {
        Assert.Empty(new HistoryStore(FilePath).Records);
    }

    [Fact]
    public void An_added_day_is_still_there_after_reopening_the_file()
    {
        new HistoryStore(FilePath).Add(new DayRecord
        {
            Day = 3,
            Visitors = 59,
            Income = 4210,
            Expenses = -1880,
            Satisfaction = 0.71,
            Needs = { ["Fun"] = 0.8 },
        });

        var record = Assert.Single(new HistoryStore(FilePath).Records);

        Assert.Equal(3, record.Day);
        Assert.Equal(59, record.Visitors);
        Assert.Equal(2330, record.Net);
        Assert.Equal(0.71, record.Satisfaction);
        Assert.Equal(0.8, record.Needs["Fun"]);
    }

    [Fact]
    public void Adding_the_same_day_again_replaces_it()
    {
        var store = new HistoryStore(FilePath);
        store.Add(Day(1, income: 100));
        store.Add(Day(1, income: 250));

        Assert.Equal(250, Assert.Single(store.Records).Income);
    }

    [Fact]
    public void Adding_an_earlier_day_drops_the_later_days_of_the_abandoned_timeline()
    {
        var store = new HistoryStore(FilePath);
        store.Add(Day(1));
        store.Add(Day(2));
        store.Add(Day(3));

        store.Add(Day(2, income: 50));

        Assert.Equal(new[] { 1, 2 }, store.Records.Select(r => r.Day));
        Assert.Equal(50, store.Records[^1].Income);
    }

    [Fact]
    public void Only_the_newest_days_are_kept()
    {
        var store = new HistoryStore(FilePath, maxDays: 3);
        for (var day = 1; day <= 5; day++) store.Add(Day(day));

        Assert.Equal(new[] { 3, 4, 5 }, store.Records.Select(r => r.Day));
    }

    [Fact]
    public void A_missing_satisfaction_figure_can_be_filled_in_later_and_is_saved()
    {
        var store = new HistoryStore(FilePath);
        store.Add(new DayRecord { Day = 2, Income = 149 });

        store.FillSatisfaction(2, 0.64);

        var record = Assert.Single(new HistoryStore(FilePath).Records);
        Assert.Equal(0.64, record.Satisfaction);
        Assert.Equal(149, record.Income);
    }

    [Fact]
    public void A_zero_satisfaction_figure_counts_as_missing()
    {
        var store = new HistoryStore(FilePath);
        store.Add(new DayRecord { Day = 2, Satisfaction = 0 });

        store.FillSatisfaction(2, 0.64);

        Assert.Equal(0.64, store.Records[0].Satisfaction);
    }

    [Fact]
    public void A_recorded_satisfaction_figure_is_not_overwritten()
    {
        var store = new HistoryStore(FilePath);
        store.Add(new DayRecord { Day = 2, Satisfaction = 0.5 });

        store.FillSatisfaction(2, 0.64);

        Assert.Equal(0.5, store.Records[0].Satisfaction);
    }

    [Fact]
    public void Filling_in_a_day_that_was_never_recorded_does_nothing()
    {
        var store = new HistoryStore(FilePath);

        store.FillSatisfaction(9, 0.64);

        Assert.Empty(store.Records);
    }

    [Fact]
    public void A_corrupt_file_is_treated_as_empty_and_can_be_written_again()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");

        var store = new HistoryStore(FilePath);
        Assert.Empty(store.Records);

        store.Add(Day(1));
        Assert.Single(new HistoryStore(FilePath).Records);
    }
}
