namespace ParkStats.Tests;

public class PagesTests
{
    private static void Has(IReadOnlyList<Row> rows, RowKind kind, string text) =>
        Assert.Contains(rows, r => r.Kind == kind && r.Text == text);

    private static string[] Texts(IReadOnlyList<Row> rows) => rows.Select(r => r.Text).ToArray();

    // ---- Money ----

    private static readonly ParkSnapshot MoneyPark = new()
    {
        Money = 12400,
        TicketPrice = 50,
        TargetTicketPrice = 62,
        TotalEarned = 90000,
        TotalSpent = 77600,
        StaffSalary = 1200,
        MoneyToday = new[]
        {
            new MoneyLine("Ticket", 2950),
            new MoneyLine("Tips", 350),
            new MoneyLine("StaffSalary", -1200),
            new MoneyLine("Maintance", -480),
        },
        Loans = new[] { new LoanLine("Bank loan", 3000) },
    };

    [Fact]
    public void Money_starts_with_todays_net()
    {
        Assert.Equal("Today | +1,620", Pages.Money(MoneyPark)[0].Text);
    }

    [Fact]
    public void Money_lists_income_as_good_and_expenses_as_bad_with_readable_labels()
    {
        var rows = Pages.Money(MoneyPark);

        Has(rows, RowKind.Good, "Ticket | +2,950");
        Has(rows, RowKind.Good, "Tips | +350");
        Has(rows, RowKind.Bad, "Staff salary | -1,200");
        Has(rows, RowKind.Bad, "Maintenance | -480");
    }

    [Fact]
    public void Money_shows_income_and_expense_totals()
    {
        var rows = Pages.Money(MoneyPark);

        Has(rows, RowKind.Normal, "Income | +3,300");
        Has(rows, RowKind.Normal, "Expenses | -1,680");
    }

    [Fact]
    public void Money_shows_balance_lifetime_totals_ticket_price_and_loans()
    {
        var rows = Pages.Money(MoneyPark);

        Has(rows, RowKind.Normal, "Balance | 12,400");
        Has(rows, RowKind.Normal, "Ticket price | 50 (target 62)");
        Has(rows, RowKind.Normal, "Lifetime earned | 90,000");
        Has(rows, RowKind.Normal, "Lifetime spent | 77,600");
        Has(rows, RowKind.Normal, "Staff salaries per day | 1,200");
        Has(rows, RowKind.Header, "Loans");
        Has(rows, RowKind.Normal, "Bank loan | 3,000");
    }

    [Fact]
    public void Money_without_movements_says_so_and_has_no_loans_section()
    {
        var rows = Pages.Money(new ParkSnapshot { Money = 500, TicketPrice = 50 });

        Has(rows, RowKind.Muted, "No money movements today yet");
        Has(rows, RowKind.Normal, "Ticket price | 50");
        Assert.DoesNotContain("Loans", Texts(rows));
    }

    // ---- Rides ----

    [Fact]
    public void Rides_has_a_column_header_and_one_row_per_ride_busiest_first()
    {
        var park = new ParkSnapshot
        {
            Rides = new[]
            {
                new RideRow { Name = "Kids Pool", UsesToday = 4, Price = 10, IdealPrice = 10, Cleanliness = 0.9, Durability = 0.8, QueueLength = 0 },
                new RideRow { Name = "Big Slide", UsesToday = 31, Price = 5, IdealPrice = 8, Cleanliness = 0.55, Durability = 0.7, QueueLength = 3 },
            },
        };

        var texts = Texts(Pages.Rides(park));

        Assert.Equal("Attraction | State | Uses | Price | Clean | Durab. | Queue", texts[0]);
        Assert.Equal("Big Slide | Open | 31 | 5/8 | 55% | 70% | 3", texts[1]);
        Assert.Equal("Kids Pool | Open | 4 | 10/10 | 90% | 80% | 0", texts[2]);
    }

    [Fact]
    public void Rides_marks_broken_and_closed_rides()
    {
        var park = new ParkSnapshot
        {
            Rides = new[]
            {
                new RideRow { Name = "Big Slide", IsBroken = true },
                new RideRow { Name = "Kids Pool", IsOpen = false },
            },
        };

        var rows = Pages.Rides(park);

        Has(rows, RowKind.Bad, "Big Slide | Broken | 0 | - | n/a | n/a | -");
        Has(rows, RowKind.Muted, "Kids Pool | Closed | 0 | - | n/a | n/a | -");
    }

    [Fact]
    public void Rides_sums_daily_maintenance_and_lists_capacity_in_use()
    {
        var park = new ParkSnapshot
        {
            Rides = new[]
            {
                new RideRow { Name = "A", MaintenancePerDay = 120 },
                new RideRow { Name = "B", MaintenancePerDay = 80 },
            },
            Capacity = new[] { new CapacityLine("Toilets", 11, 12) },
        };

        var rows = Pages.Rides(park);

        Has(rows, RowKind.Normal, "Maintenance per day | 200");
        Has(rows, RowKind.Header, "Capacity in use");
        Has(rows, RowKind.Normal, "Toilets | 11/12");
    }

    [Fact]
    public void Rides_without_attractions_says_so()
    {
        Has(Pages.Rides(new ParkSnapshot()), RowKind.Muted, "No attractions built yet");
    }

    // ---- Guests ----

    [Fact]
    public void Guests_lists_each_need_with_average_and_guests_running_low()
    {
        var park = new ParkSnapshot
        {
            Needs = new[]
            {
                new NeedStat("Fun", 0.82, 2, 40),
                new NeedStat("Toilet", 0.35, 21, 40),
                new NeedStat("Thirst", 0.55, 9, 40),
            },
        };

        var rows = Pages.Guests(park);

        Assert.Equal("Need | Average | Guests low", rows[0].Text);
        Has(rows, RowKind.Good, "Fun | 82% | 2/40");
        Has(rows, RowKind.Bad, "Toilet | 35% | 21/40");
        Has(rows, RowKind.Normal, "Thirst | 55% | 9/40");
    }

    [Fact]
    public void Guests_shows_counts_and_average_cash()
    {
        var park = new ParkSnapshot
        {
            Visitors = 59,
            MaxVisitors = 75,
            ExpectedVisitors = 70,
            AverageGuestCash = 34.4,
            RefundedVisitors = 2,
            InjuredVisitors = 1,
        };

        var rows = Pages.Guests(park);

        Has(rows, RowKind.Normal, "In park | 59/75");
        Has(rows, RowKind.Normal, "Expected | 70");
        Has(rows, RowKind.Normal, "Average cash | 34");
        Has(rows, RowKind.Normal, "Refunded | 2");
        Has(rows, RowKind.Normal, "Injured | 1");
    }

    [Fact]
    public void Guests_shows_the_five_most_common_complaints_and_leaving_reasons()
    {
        var complaints = Enumerable.Range(1, 7).Select(i => new CountLine("Complaint " + i, i)).ToArray();
        var park = new ParkSnapshot
        {
            Complaints = complaints,
            LeavingReasons = new[] { new CountLine("Prices too high", 6) },
        };

        var texts = Texts(Pages.Guests(park));

        Assert.Contains("Top complaints", texts);
        Assert.Contains("Complaint 7 | 7", texts);
        Assert.Contains("Complaint 3 | 3", texts);
        Assert.DoesNotContain("Complaint 2 | 2", texts);
        Assert.Contains("Leaving because", texts);
        Assert.Contains("Prices too high | 6", texts);
    }

    [Fact]
    public void Guests_leaves_out_sections_with_no_data()
    {
        var texts = Texts(Pages.Guests(new ParkSnapshot()));

        Assert.DoesNotContain("Top complaints", texts);
        Assert.DoesNotContain("Leaving because", texts);
        Assert.DoesNotContain("Need | Average | Guests low", texts);
    }

    [Fact]
    public void Guests_with_no_data_says_so()
    {
        Has(Pages.Guests(new ParkSnapshot()), RowKind.Muted, "No guest data available");
    }

    // ---- History ----

    [Fact]
    public void History_lists_days_newest_first_with_profit_and_loss_marked()
    {
        var days = new[]
        {
            new DayRecord { Day = 1, Visitors = 40, Income = 1000, Expenses = -1500, Satisfaction = 0.5 },
            new DayRecord { Day = 2, Visitors = 59, Income = 4210, Expenses = -1880, Satisfaction = 0.71 },
        };

        var rows = Pages.History(days);

        Assert.Equal("Day | Visitors | Net | Satisfaction", rows[0].Text);
        Assert.Equal(Row.Of(RowKind.Good, "2", "59", "+2,330", "71%").Text, rows[1].Text);
        Assert.Equal(RowKind.Good, rows[1].Kind);
        Assert.Equal("1 | 40 | -500 | 50%", rows[2].Text);
        Assert.Equal(RowKind.Bad, rows[2].Kind);
    }

    [Fact]
    public void History_with_no_days_explains_when_it_starts()
    {
        Has(Pages.History(Array.Empty<DayRecord>()), RowKind.Muted, "History starts after the first full day");
    }

    // ---- Advice ----

    [Fact]
    public void Advice_shows_each_title_followed_by_its_detail()
    {
        var advice = new[]
        {
            new Advice(Severity.High, "Repair broken rides", "Big Slide: malfunctioning."),
            new Advice(Severity.Low, "Top complaint", "\"Too pricey\" (3 times)."),
        };

        var rows = Pages.Advisor(advice);

        Assert.Equal(
            new[] { "Repair broken rides", "Big Slide: malfunctioning.", "Top complaint", "\"Too pricey\" (3 times)." },
            Texts(rows));
        Assert.Equal(new[] { RowKind.Bad, RowKind.Muted, RowKind.Normal, RowKind.Muted }, rows.Select(r => r.Kind));
    }

    [Fact]
    public void Advice_with_nothing_to_fix_says_so()
    {
        Has(Pages.Advisor(Array.Empty<Advice>()), RowKind.Good, "Nothing needs fixing right now");
    }

    // ---- Overview ----

    [Fact]
    public void Overview_summarises_the_day_and_the_park()
    {
        var park = MoneyPark with
        {
            Visitors = 59,
            MaxVisitors = 75,
            ExpectedVisitors = 70,
            Satisfaction = 0.71,
            Prestige = new PrestigeInfo { Level = 2, DecorationPoints = 340, NextLevelPoints = 500 },
        };

        var rows = Pages.Overview(park, Array.Empty<Advice>());

        Has(rows, RowKind.Good, "Net today | +1,620");
        Has(rows, RowKind.Normal, "Balance | 12,400");
        Has(rows, RowKind.Normal, "Visitors | 59/75 (expected 70)");
        Has(rows, RowKind.Normal, "Satisfaction | 71%");
        Has(rows, RowKind.Normal, "Prestige | 2 (decor 340/500)");
    }

    [Fact]
    public void Overview_marks_a_losing_day_as_bad()
    {
        var park = new ParkSnapshot { MoneyToday = new[] { new MoneyLine("StaffSalary", -300) } };

        Has(Pages.Overview(park, Array.Empty<Advice>()), RowKind.Bad, "Net today | -300");
    }

    [Fact]
    public void Overview_shows_top_prestige_without_a_next_target()
    {
        var park = new ParkSnapshot { Prestige = new PrestigeInfo { Level = 5, DecorationPoints = 900 } };

        Has(Pages.Overview(park, Array.Empty<Advice>()), RowKind.Normal, "Prestige | 5 (max)");
    }

    [Fact]
    public void Overview_lists_only_the_three_most_urgent_advice_titles()
    {
        var advice = new[]
        {
            new Advice(Severity.High, "First", "a"),
            new Advice(Severity.Medium, "Second", "b"),
            new Advice(Severity.Medium, "Third", "c"),
            new Advice(Severity.Low, "Fourth", "d"),
        };

        var rows = Pages.Overview(new ParkSnapshot(), advice);

        Has(rows, RowKind.Header, "Do next");
        Has(rows, RowKind.Bad, "First");
        Has(rows, RowKind.Normal, "Third");
        Assert.DoesNotContain("Fourth", Texts(rows));
    }
}
