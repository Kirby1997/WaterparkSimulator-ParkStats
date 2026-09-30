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
    public void Money_leaves_out_staff_cost_when_there_is_none()
    {
        var texts = Texts(Pages.Money(new ParkSnapshot { Money = 500, StaffSalary = 0, StaffTax = 0 }));

        Assert.DoesNotContain(texts, t => t.StartsWith("Staff"));
    }

    [Fact]
    public void Money_lists_what_each_kind_of_attraction_has_earned_today_biggest_first()
    {
        var park = new ParkSnapshot
        {
            Rides = new[]
            {
                new RideRow { Name = "Wave Slide", UsesToday = 5, Price = 30 },
                new RideRow { Name = "Treasure Pool", UsesToday = 10, Price = 33 },
                new RideRow { Name = "Treasure Pool", UsesToday = 12, Price = 33 },
                new RideRow { Name = "Park Bin", UsesToday = 20 },
            },
        };

        var texts = Texts(Pages.Money(park));

        var header = Array.IndexOf(texts, "Earned by attraction (uses x price)");
        Assert.True(header >= 0);
        Assert.Equal("Treasure Pool x2 | 726", texts[header + 1]);
        Assert.Equal("Wave Slide | 150", texts[header + 2]);
        Assert.DoesNotContain(texts, t => t.StartsWith("Park Bin"));
    }

    [Fact]
    public void Money_shows_income_per_visitor()
    {
        var park = new ParkSnapshot { VisitorsToday = 120, MoneyToday = new[] { new MoneyLine("Ticket", 6000) } };

        var rows = Pages.Money(park);

        Has(rows, RowKind.Normal, "Visitors today | 120");
        Has(rows, RowKind.Normal, "Income per visitor | 50");
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

        Assert.Equal("Attraction | State | Uses | Price | Clean | Durab. | Wait", texts[0]);
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
    public void Rides_with_the_same_name_share_a_row_showing_totals_and_the_worst_condition()
    {
        var park = new ParkSnapshot
        {
            Rides = new[]
            {
                new RideRow { Name = "Rusty Lounger", UsesToday = 6, Price = 1, IdealPrice = 1, Cleanliness = 1.0, Durability = 0.74 },
                new RideRow { Name = "Rusty Lounger", UsesToday = 2, Price = 1, IdealPrice = 1, Cleanliness = 0.9, Durability = 0.87, QueueLength = 1 },
                new RideRow { Name = "Rusty Lounger", UsesToday = 1, Price = 1, IdealPrice = 1, Cleanliness = 1.0, Durability = 0.87, QueueLength = 2 },
            },
        };

        Has(Pages.Rides(park), RowKind.Normal, "Rusty Lounger x3 | Open | 9 | 1/1 | 90% | 74% | 3");
    }

    [Fact]
    public void A_group_with_some_rides_closed_says_how_many_are_open()
    {
        var park = new ParkSnapshot
        {
            Rides = new[]
            {
                new RideRow { Name = "Shower" },
                new RideRow { Name = "Shower" },
                new RideRow { Name = "Shower", IsOpen = false },
            },
        };

        Has(Pages.Rides(park), RowKind.Normal, "Shower x3 | 2/3 open | 0 | - | n/a | n/a | -");
    }

    [Fact]
    public void A_group_with_a_broken_ride_is_marked_bad_and_says_how_many()
    {
        var park = new ParkSnapshot
        {
            Rides = new[] { new RideRow { Name = "Shower" }, new RideRow { Name = "Shower", IsBroken = true } },
        };

        Has(Pages.Rides(park), RowKind.Bad, "Shower x2 | 1 broken | 0 | - | n/a | n/a | -");
    }

    [Fact]
    public void A_group_with_every_ride_closed_is_closed()
    {
        var park = new ParkSnapshot
        {
            Rides = new[] { new RideRow { Name = "Shower", IsOpen = false }, new RideRow { Name = "Shower", IsOpen = false } },
        };

        Has(Pages.Rides(park), RowKind.Muted, "Shower x2 | Closed | 0 | - | n/a | n/a | -");
    }

    [Fact]
    public void Long_attraction_names_are_cut_to_fit_their_column()
    {
        var park = new ParkSnapshot { Rides = new[] { new RideRow { Name = "Extremely Long Attraction Name" } } };

        Assert.StartsWith("Extremely Long Att.. | Open", Pages.Rides(park)[1].Text);
    }

    [Fact]
    public void A_ride_with_a_guest_request_waiting_is_flagged()
    {
        var park = new ParkSnapshot { Rides = new[] { new RideRow { Name = "Wooden Sauna", HasOpenRequest = true } } };

        Has(Pages.Rides(park), RowKind.Bad, "Wooden Sauna | Request | 0 | - | n/a | n/a | -");
    }

    [Fact]
    public void Rides_without_attractions_says_so()
    {
        Has(Pages.Rides(new ParkSnapshot()), RowKind.Muted, "No attractions built yet");
    }

    // ---- Build ----

    private static readonly ParkSnapshot BuildPark = new()
    {
        AttractionCapacity = 78,
        Prestige = new PrestigeInfo { Level = 4, MaxVisitors = 75, NextLevelMaxVisitors = 100 },
        BuildOptions = new[]
        {
            new BuildOption { Name = "Treasure Pool", Price = 4000, Capacity = 8, Owned = 2, Raises = new[] { "Fun" } },
            new BuildOption { Name = "Sunbed Lounger", Price = 300, Capacity = 2, Owned = 17, Raises = new[] { "Energy" } },
            new BuildOption { Name = "Hotdog Stand", Price = 1200, Capacity = 1, Raises = new[] { "Hunger" } },
            new BuildOption { Name = "Mega Slide", Price = 9000, Capacity = 4, LockedBy = "prestige 5" },
            new BuildOption { Name = "Palm Tree", Price = 50 },
        },
    };

    [Fact]
    public void Build_shows_how_much_room_attractions_give_against_what_prestige_allows()
    {
        var rows = Pages.Build(BuildPark);

        Has(rows, RowKind.Header, "Room for visitors");
        Has(rows, RowKind.Normal, "Attractions hold | 78");
        Has(rows, RowKind.Normal, "Prestige 4 allows | 75");
        Has(rows, RowKind.Normal, "Prestige 5 allows | 100");
    }

    [Fact]
    public void Build_lists_the_cheapest_room_first()
    {
        var texts = Texts(Pages.Build(BuildPark));

        var header = Array.IndexOf(texts, "Cheapest room to add");
        Assert.True(header >= 0);
        Assert.Equal("Sunbed Lounger | +2 for 300", texts[header + 1]);
        Assert.Equal("Treasure Pool | +8 for 4,000", texts[header + 2]);
        Assert.Equal("Hotdog Stand | +1 for 1,200", texts[header + 3]);
    }

    [Fact]
    public void Build_lists_attractions_not_built_yet_with_what_they_raise()
    {
        var rows = Pages.Build(BuildPark);

        Has(rows, RowKind.Header, "Not built yet");
        Has(rows, RowKind.Normal, "Hotdog Stand | 1,200, raises Hunger");
        Assert.DoesNotContain(Texts(rows), t => t.StartsWith("Palm Tree"));
    }

    [Fact]
    public void Build_lists_what_is_still_locked_and_why()
    {
        var rows = Pages.Build(BuildPark);

        Has(rows, RowKind.Header, "Locked");
        Has(rows, RowKind.Normal, "Mega Slide | prestige 5");
    }

    [Fact]
    public void Build_shows_what_each_attraction_earns_against_its_upkeep_and_how_busy_it_is()
    {
        var park = new ParkSnapshot
        {
            Rides = new[]
            {
                new RideRow { Name = "Wave Slide", Price = 30, MaintenancePerDay = 70, Capacity = 1, UsersNow = 1, QueueLength = 3 },
                new RideRow { Name = "Wave Slide", Price = 30, MaintenancePerDay = 70, Capacity = 1, UsersNow = 1, QueueLength = 0 },
            },
            YesterdayAttractions = new Dictionary<string, AttractionDay> { ["Wave Slide"] = new() { Count = 2, Earned = 1200 } },
        };

        var texts = Texts(Pages.Build(park));

        var header = Array.IndexOf(texts, "Each one, yesterday | Earned | Upkeep | Net | Busy");
        Assert.True(header >= 0);
        Assert.Equal("Wave Slide x2 | 600 | 70 | +530 | 2/2 +3", texts[header + 1]);
    }

    [Fact]
    public void Build_without_a_recorded_day_shows_todays_takings_and_no_net()
    {
        var park = new ParkSnapshot
        {
            Rides = new[] { new RideRow { Name = "Wave Slide", UsesToday = 4, Price = 30, MaintenancePerDay = 70 } },
        };

        var texts = Texts(Pages.Build(park));

        var header = Array.IndexOf(texts, "Each one, today so far | Earned | Upkeep | Net | Busy");
        Assert.True(header >= 0);
        Assert.Equal("Wave Slide | 120 | 70 | - | -", texts[header + 1]);
    }

    [Fact]
    public void Build_without_a_catalogue_says_so()
    {
        Has(Pages.Build(new ParkSnapshot()), RowKind.Muted, "No building data available");
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

        Assert.Equal("Need | Average | Under 50%", rows[0].Text);
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
    public void Guests_leaves_out_expected_visitors_when_it_equals_the_cap()
    {
        var texts = Texts(Pages.Guests(new ParkSnapshot { Visitors = 3, MaxVisitors = 8, ExpectedVisitors = 8 }));

        Assert.Contains("In park | 3/8", texts);
        Assert.DoesNotContain("Expected | 8", texts);
    }

    [Fact]
    public void Guests_with_effectively_endless_cash_get_no_cash_row()
    {
        // In game modes where guests cannot run out of money the figure tells the player nothing.
        var texts = Texts(Pages.Guests(new ParkSnapshot { Visitors = 40, AverageGuestCash = 999987 }));

        Assert.DoesNotContain(texts, t => t.StartsWith("Average cash"));
    }

    [Fact]
    public void Guests_with_limited_cash_show_the_average()
    {
        Has(Pages.Guests(new ParkSnapshot { AverageGuestCash = 34.4 }), RowKind.Normal, "Average cash | 34");
    }

    [Fact]
    public void Guests_shows_a_need_without_a_reading_as_not_available()
    {
        var park = new ParkSnapshot { Needs = new[] { new NeedStat("Fun", null, 0, 0) } };

        Has(Pages.Guests(park), RowKind.Muted, "Fun | n/a | -");
    }

    [Fact]
    public void Guests_lists_what_in_the_park_raises_each_need()
    {
        var park = new ParkSnapshot
        {
            Needs = new[]
            {
                new NeedStat("Fun", 0.78, 0, 57) { Sources = new[] { "Wave Slide", "Crew Challenge Slide x2" } },
                new NeedStat("Trash", 0.65, 0, 57),
            },
        };

        var rows = Pages.Guests(park);

        Has(rows, RowKind.Header, "Raised by");
        Has(rows, RowKind.Normal, "Fun: Wave Slide, Crew Challenge Slide x2");
        Assert.DoesNotContain(Texts(rows), t => t.StartsWith("Trash:"));
    }

    [Fact]
    public void Guests_shows_what_satisfaction_does_to_visitor_numbers()
    {
        var park = new ParkSnapshot { RecentSatisfaction = 0.7, VisitorMultiplier = 0.85, BestVisitorMultiplier = 1 };

        var rows = Pages.Guests(park);

        Has(rows, RowKind.Header, "What satisfaction buys");
        Has(rows, RowKind.Normal, "Recent satisfaction | 70%");
        Has(rows, RowKind.Normal, "Visitor multiplier | x0.85 (x1.00 at best)");
    }

    [Fact]
    public void Guests_says_nothing_about_what_satisfaction_buys_without_a_multiplier_to_show()
    {
        var texts = Texts(Pages.Guests(new ParkSnapshot { Visitors = 40, RecentSatisfaction = 0.7 }));

        Assert.DoesNotContain("What satisfaction buys", texts);
    }

    [Fact]
    public void Guests_shows_a_visitor_multiplier_already_at_its_best_without_the_comparison()
    {
        var park = new ParkSnapshot { VisitorMultiplier = 1, BestVisitorMultiplier = 1 };

        Has(Pages.Guests(park), RowKind.Normal, "Visitor multiplier | x1.00");
    }

    [Fact]
    public void Guests_explains_a_complaint_when_the_game_has_a_description_for_it()
    {
        var park = new ParkSnapshot
        {
            Complaints = new[] { new CountLine("Poor service", 6) { Note = "Nobody was there to serve me." } },
        };

        var texts = Texts(Pages.Guests(park));

        var rows = Pages.Guests(park);
        var complaint = Array.IndexOf(texts, "Poor service | 6");
        Assert.True(complaint >= 0);
        Assert.Equal("Nobody was there to serve me.", texts[complaint + 1]);
        Assert.Equal(RowKind.Normal, rows[complaint + 1].Kind);
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
        Assert.DoesNotContain("Need | Average | Under 50%", texts);
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
    public void History_shows_a_day_without_a_satisfaction_figure_as_not_available()
    {
        var rows = Pages.History(new[] { new DayRecord { Day = 3, Visitors = 7, Income = 149, Expenses = -209 } });

        Assert.Equal("3 | 7 | -60 | n/a", rows[1].Text);
    }

    [Fact]
    public void History_treats_a_zero_satisfaction_figure_as_not_recorded()
    {
        var rows = Pages.History(new[] { new DayRecord { Day = 2, Visitors = 7, Income = 149, Expenses = -209, Satisfaction = 0 } });

        Assert.Equal("2 | 7 | -60 | n/a", rows[1].Text);
    }

    [Fact]
    public void History_with_no_days_explains_when_it_starts()
    {
        Has(Pages.History(Array.Empty<DayRecord>()), RowKind.Muted, "History starts after the first full day");
    }

    // ---- Advice ----

    [Fact]
    public void Advice_shows_each_title_followed_by_its_detail_with_a_blank_line_between_items()
    {
        var advice = new[]
        {
            new Advice(Severity.High, "Repair broken rides", "Big Slide: malfunctioning."),
            new Advice(Severity.Low, "Top complaint", "\"Too pricey\" (3 times)."),
        };

        var rows = Pages.Advisor(advice);

        Assert.Equal(
            new[] { "Repair broken rides", "Big Slide: malfunctioning.", "", "Top complaint", "\"Too pricey\" (3 times)." },
            Texts(rows));
        // Titles stand out by colour; the explanation is ordinary text, the easiest to read.
        Assert.Equal(
            new[] { RowKind.Bad, RowKind.Normal, RowKind.Normal, RowKind.Header, RowKind.Normal },
            rows.Select(r => r.Kind));
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
            Prestige = new PrestigeInfo { Level = 4, MaxVisitors = 75, DecorationLevel = 0.34 },
        };

        var rows = Pages.Overview(park, Array.Empty<Advice>());

        Has(rows, RowKind.Good, "Net today | +1,620");
        Has(rows, RowKind.Normal, "Balance | 12,400");
        Has(rows, RowKind.Normal, "Visitors | 59/75 (expected 70)");
        Has(rows, RowKind.Normal, "Satisfaction | 71%");
        Has(rows, RowKind.Normal, "Prestige | 4 (allows 75 visitors)");
        Has(rows, RowKind.Normal, "Decoration | 34%");
    }

    [Fact]
    public void Overview_does_not_repeat_expected_visitors_when_it_equals_the_cap()
    {
        var park = new ParkSnapshot { Visitors = 3, MaxVisitors = 3, ExpectedVisitors = 3 };

        Has(Pages.Overview(park, Array.Empty<Advice>()), RowKind.Normal, "Visitors | 3/3");
    }

    [Fact]
    public void Overview_shows_prestige_alone_when_its_visitor_limit_is_unknown()
    {
        var park = new ParkSnapshot { Prestige = new PrestigeInfo { Level = 5 } };

        Has(Pages.Overview(park, Array.Empty<Advice>()), RowKind.Normal, "Prestige | 5");
    }

    [Fact]
    public void Overview_lists_the_three_most_urgent_things_to_do_with_their_details()
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
        Has(rows, RowKind.Normal, "a");
        Has(rows, RowKind.Header, "Third");
        Has(rows, RowKind.Normal, "c");
        Assert.DoesNotContain("Fourth", Texts(rows));
        Assert.DoesNotContain("d", Texts(rows));
    }

    [Fact]
    public void Overview_marks_a_losing_day_as_bad()
    {
        var park = new ParkSnapshot { MoneyToday = new[] { new MoneyLine("StaffSalary", -300) } };

        Has(Pages.Overview(park, Array.Empty<Advice>()), RowKind.Bad, "Net today | -300");
    }
}
