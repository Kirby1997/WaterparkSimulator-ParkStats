namespace ParkStats.Tests;

public class AdvisorTests
{
    // A park with nothing wrong: every rule should stay quiet for it.
    private static ParkSnapshot Healthy() => new()
    {
        TicketPrice = 60,
        TargetTicketPrice = 60,
        Visitors = 40,
        ExpectedVisitors = 70,
        MaxVisitors = 75,
        Satisfaction = 0.8,
        CleanlinessThreshold = 0.3,
        DurabilityThreshold = 0.3,
        MoneyToday = new[] { new MoneyLine("Ticket", 3000), new MoneyLine("StaffSalary", -600) },
        StaffSalary = 600,
        StaffTax = 0,
        StaffCount = 4,
        StaffCapacity = 6,
        Needs = new[]
        {
            new NeedStat("Fun", 0.8, 2, 40),
            new NeedStat("Toilet", 0.75, 3, 40),
        },
        Rides = new[]
        {
            new RideRow { Name = "Kids Pool", Price = 10, IdealPrice = 10, Cleanliness = 0.9, Durability = 0.9 },
        },
        Capacity = new[] { new CapacityLine("Toilets", 3, 12) },
    };

    private static Advice Single(ParkSnapshot park, string titlePart) =>
        Assert.Single(Advisor.Evaluate(park), a => a.Title.Contains(titlePart, StringComparison.OrdinalIgnoreCase));

    private static void None(ParkSnapshot park, string titlePart) =>
        Assert.DoesNotContain(Advisor.Evaluate(park), a => a.Title.Contains(titlePart, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void A_healthy_park_gets_no_advice()
    {
        Assert.Empty(Advisor.Evaluate(Healthy()));
    }

    [Fact]
    public void A_park_with_no_readable_data_gets_no_advice()
    {
        Assert.Empty(Advisor.Evaluate(new ParkSnapshot()));
    }

    [Fact]
    public void A_broken_ride_is_a_high_priority_and_is_named()
    {
        var park = Healthy() with { Rides = new[] { new RideRow { Name = "Big Slide", IsBroken = true } } };

        var advice = Single(park, "broken");

        Assert.Equal(Severity.High, advice.Severity);
        Assert.Contains("Big Slide", advice.Detail);
    }

    [Fact]
    public void A_closed_ride_is_reported()
    {
        var park = Healthy() with { Rides = new[] { new RideRow { Name = "Big Slide", IsOpen = false } } };

        var advice = Single(park, "closed");

        Assert.Equal(Severity.Medium, advice.Severity);
        Assert.Contains("Big Slide", advice.Detail);
    }

    [Fact]
    public void A_ride_below_the_games_cleanliness_threshold_is_reported()
    {
        var park = Healthy() with { Rides = new[] { new RideRow { Name = "Kids Pool", Cleanliness = 0.2 } } };

        var advice = Single(park, "dirty");

        Assert.Contains("Kids Pool", advice.Detail);
    }

    [Fact]
    public void A_ride_above_the_cleanliness_threshold_is_not_reported()
    {
        var park = Healthy() with { Rides = new[] { new RideRow { Name = "Kids Pool", Cleanliness = 0.35 } } };

        None(park, "dirty");
    }

    [Fact]
    public void Without_a_game_threshold_half_clean_counts_as_dirty()
    {
        var park = Healthy() with
        {
            CleanlinessThreshold = null,
            Rides = new[] { new RideRow { Name = "Kids Pool", Cleanliness = 0.45 } },
        };

        Single(park, "dirty");
    }

    [Fact]
    public void A_ride_below_the_durability_threshold_is_reported()
    {
        var park = Healthy() with { Rides = new[] { new RideRow { Name = "Big Slide", Durability = 0.1 } } };

        var advice = Single(park, "worn");

        Assert.Contains("Big Slide", advice.Detail);
    }

    [Fact]
    public void Long_ride_lists_are_shortened()
    {
        var rides = Enumerable.Range(1, 5).Select(i => new RideRow { Name = "Ride" + i, IsBroken = true }).ToArray();
        var park = Healthy() with { Rides = rides };

        var advice = Single(park, "broken");

        Assert.Contains("Ride1, Ride2, Ride3", advice.Detail);
        Assert.Contains("2 more", advice.Detail);
        Assert.DoesNotContain("Ride4", advice.Detail);
    }

    [Fact]
    public void The_weakest_need_is_reported_with_how_many_guests_it_affects()
    {
        var park = Healthy() with
        {
            Needs = new[]
            {
                new NeedStat("Fun", 0.55, 10, 40),
                new NeedStat("Toilet", 0.45, 21, 40),
            },
        };

        var advice = Single(park, "weakest need");

        Assert.Contains("Toilet", advice.Title);
        Assert.Equal(Severity.Medium, advice.Severity);
        Assert.Contains("45%", advice.Detail);
        Assert.Contains("21 of 40", advice.Detail);
        Assert.Contains("toilets", advice.Detail);
    }

    [Fact]
    public void A_very_low_need_is_a_high_priority()
    {
        var park = Healthy() with { Needs = new[] { new NeedStat("Thirst", 0.3, 30, 40) } };

        Assert.Equal(Severity.High, Single(park, "weakest need").Severity);
    }

    [Fact]
    public void Needs_without_an_average_are_ignored()
    {
        var park = Healthy() with { Needs = new[] { new NeedStat("Thirst", null, 0, 0) } };

        None(park, "weakest need");
    }

    [Fact]
    public void A_facility_type_near_its_limit_is_reported()
    {
        var park = Healthy() with { Capacity = new[] { new CapacityLine("Toilets", 11, 12) } };

        var advice = Single(park, "capacity");

        Assert.Contains("Toilets", advice.Title);
        Assert.Contains("11/12", advice.Detail);
    }

    [Fact]
    public void A_facility_type_that_is_not_built_is_not_a_capacity_problem()
    {
        var park = Healthy() with { Capacity = new[] { new CapacityLine("Slides", 0, 0) } };

        None(park, "capacity");
    }

    [Fact]
    public void A_ticket_price_well_below_target_suggests_raising_it()
    {
        var park = Healthy() with { TicketPrice = 50, TargetTicketPrice = 62 };

        var advice = Single(park, "ticket price");

        Assert.Contains("Raise", advice.Title);
        Assert.Contains("50", advice.Detail);
        Assert.Contains("62", advice.Detail);
    }

    [Fact]
    public void A_ticket_price_well_above_target_suggests_lowering_it()
    {
        var park = Healthy() with { TicketPrice = 80, TargetTicketPrice = 62 };

        Assert.Contains("Lower", Single(park, "ticket price").Title);
    }

    [Fact]
    public void A_ticket_price_close_to_target_is_left_alone()
    {
        var park = Healthy() with { TicketPrice = 60, TargetTicketPrice = 62 };

        None(park, "ticket price");
    }

    [Fact]
    public void Rides_priced_well_below_ideal_are_listed_with_both_prices()
    {
        var park = Healthy() with { Rides = new[] { new RideRow { Name = "Hotdogs", Price = 5, IdealPrice = 8 } } };

        var advice = Single(park, "underpriced");

        Assert.Equal(Severity.Low, advice.Severity);
        Assert.Contains("Hotdogs (5, ideal 8)", advice.Detail);
    }

    [Fact]
    public void Rides_priced_well_above_ideal_are_listed()
    {
        var park = Healthy() with { Rides = new[] { new RideRow { Name = "Hotdogs", Price = 12, IdealPrice = 8 } } };

        Assert.Contains("Hotdogs (12, ideal 8)", Single(park, "overpriced").Detail);
    }

    [Fact]
    public void A_game_threshold_of_fully_clean_does_not_flag_lightly_used_rides()
    {
        var park = Healthy() with
        {
            CleanlinessThreshold = 1.0,
            DurabilityThreshold = 1.0,
            Rides = new[] { new RideRow { Name = "Kids Pool", Cleanliness = 0.8, Durability = 0.8 } },
        };

        None(park, "dirty");
        None(park, "worn");
    }

    [Fact]
    public void Low_decoration_is_reported()
    {
        var park = Healthy() with { Prestige = new PrestigeInfo { DecorationLevel = 0.2 } };

        var advice = Single(park, "decorat");

        Assert.Equal(Severity.Low, advice.Severity);
        Assert.Contains("20%", advice.Detail);
    }

    [Fact]
    public void Good_decoration_is_not_reported()
    {
        var park = Healthy() with { Prestige = new PrestigeInfo { DecorationLevel = 0.7 } };

        None(park, "decorat");
    }

    [Fact]
    public void The_most_common_complaint_is_quoted_with_its_count()
    {
        var park = Healthy() with
        {
            Complaints = new[]
            {
                new CountLine("There are no showers in this park!", 4),
                new CountLine("The prices are too high!", 14),
            },
        };

        var advice = Single(park, "complaint");

        Assert.Contains("The prices are too high!", advice.Detail);
        Assert.Contains("14", advice.Detail);
    }

    [Fact]
    public void Staff_costing_more_than_half_of_a_typical_days_income_is_reported()
    {
        var park = Healthy() with { TypicalDayIncome = 1000, StaffSalary = 500, StaffTax = 100 };

        var advice = Single(park, "staff cost");

        Assert.Contains("600", advice.Detail);
        Assert.Contains("60%", advice.Detail);
    }

    [Fact]
    public void Staff_cost_is_not_judged_against_a_day_that_has_only_just_started()
    {
        // Early in the day income is near zero, so income-so-far would always look too small.
        var park = Healthy() with
        {
            TypicalDayIncome = null,
            MoneyToday = new[] { new MoneyLine("Ticket", 50) },
            StaffSalary = 2400,
        };

        None(park, "staff cost");
    }

    [Fact]
    public void The_top_complaint_comes_with_the_games_explanation_of_it()
    {
        var park = Healthy() with
        {
            Complaints = new[] { new CountLine("Poor service", 6) { Note = "Nobody was there to serve me." } },
        };

        Assert.Contains("Nobody was there to serve me.", Single(park, "complaint").Detail);
    }

    [Fact]
    public void The_weakest_need_names_what_in_the_park_raises_it()
    {
        var park = Healthy() with
        {
            Needs = new[] { new NeedStat("Hygiene", 0.45, 21, 40) { Sources = new[] { "Far Shore Shower x4" } } },
        };

        Assert.Contains("Far Shore Shower x4", Single(park, "weakest need").Detail);
    }

    [Fact]
    public void A_queue_at_a_paid_attraction_suggests_adding_another_and_says_what_each_earns()
    {
        var park = Healthy() with
        {
            Rides = new[]
            {
                new RideRow { Name = "Wave Slide", UsesToday = 10, Price = 30, IdealPrice = 30, QueueLength = 3 },
                new RideRow { Name = "Wave Slide", UsesToday = 10, Price = 30, IdealPrice = 30, QueueLength = 2 },
            },
        };

        var advice = Single(park, "queuing");

        Assert.Equal(Severity.Medium, advice.Severity);
        Assert.Contains("Wave Slide", advice.Detail);
        Assert.Contains("5 waiting", advice.Detail);
        Assert.Contains("300", advice.Detail);
    }

    [Fact]
    public void A_single_guest_waiting_is_not_a_queue_worth_building_for()
    {
        var park = Healthy() with
        {
            Rides = new[] { new RideRow { Name = "Wave Slide", UsesToday = 10, Price = 30, IdealPrice = 30, QueueLength = 1 } },
        };

        None(park, "queuing");
    }

    [Fact]
    public void A_stand_with_nothing_left_to_sell_is_a_high_priority()
    {
        var park = Healthy() with
        {
            Rides = new[] { new RideRow { Name = "Ice Cream Stand", Price = 60, IdealPrice = 60, StockLeft = 0, StockCapacity = 12 } },
        };

        var advice = Single(park, "restock");

        Assert.Equal(Severity.High, advice.Severity);
        Assert.Contains("Ice Cream Stand", advice.Detail);
    }

    [Fact]
    public void A_stand_that_still_has_stock_is_not_reported()
    {
        var park = Healthy() with
        {
            Rides = new[] { new RideRow { Name = "Ice Cream Stand", Price = 60, IdealPrice = 60, StockLeft = 5, StockCapacity = 12 } },
        };

        None(park, "restock");
    }

    [Fact]
    public void Paid_attractions_nobody_has_used_are_pointed_out_once_the_park_is_busy()
    {
        var park = Healthy() with
        {
            Rides = new[]
            {
                new RideRow { Name = "Kids Pool", UsesToday = 60, Price = 5, IdealPrice = 5 },
                new RideRow { Name = "Wooden Sauna", UsesToday = 0, Price = 25, IdealPrice = 25 },
                new RideRow { Name = "Park Bin", UsesToday = 0 },
            },
        };

        var advice = Single(park, "unused");

        Assert.Equal(Severity.Low, advice.Severity);
        Assert.Contains("Wooden Sauna", advice.Detail);
        Assert.DoesNotContain("Park Bin", advice.Detail);
    }

    [Fact]
    public void Unused_attractions_are_not_judged_before_the_park_has_been_busy()
    {
        var park = Healthy() with
        {
            Rides = new[]
            {
                new RideRow { Name = "Kids Pool", UsesToday = 5, Price = 5, IdealPrice = 5 },
                new RideRow { Name = "Wooden Sauna", UsesToday = 0, Price = 25, IdealPrice = 25 },
            },
        };

        None(park, "unused");
    }

    [Fact]
    public void Satisfaction_that_cuts_visitor_numbers_is_reported_with_the_games_multiplier()
    {
        var park = Healthy() with { RecentSatisfaction = 0.5, VisitorMultiplier = 0.72, BestVisitorMultiplier = 1.2 };

        var advice = Single(park, "satisfaction");

        Assert.Equal(Severity.Medium, advice.Severity);
        Assert.Contains("costing", advice.Title);
        Assert.Contains("50%", advice.Detail);
        Assert.Contains("x0.72", advice.Detail);
        Assert.Contains("x1.20", advice.Detail);
    }

    [Fact]
    public void Satisfaction_with_room_to_add_visitors_is_a_low_priority()
    {
        var park = Healthy() with { RecentSatisfaction = 0.7, VisitorMultiplier = 1.0, BestVisitorMultiplier = 1.2 };

        var advice = Single(park, "satisfaction");

        Assert.Equal(Severity.Low, advice.Severity);
        Assert.Contains("x1.00", advice.Detail);
        Assert.Contains("x1.20", advice.Detail);
    }

    [Fact]
    public void Satisfaction_already_at_the_best_multiplier_is_not_reported()
    {
        var park = Healthy() with { RecentSatisfaction = 1, VisitorMultiplier = 1.2, BestVisitorMultiplier = 1.2 };

        None(park, "satisfaction");
    }

    [Fact]
    public void The_next_star_says_what_is_still_to_do_and_what_it_brings()
    {
        var park = Healthy() with
        {
            Visitors = 30,
            MaxVisitors = 75,
            NextStarTask = "Earn money: $489787/500000",
            Prestige = new PrestigeInfo { Level = 4, MaxVisitors = 75, NextLevelMaxVisitors = 100 },
        };

        var advice = Single(park, "next star");

        Assert.Equal(Severity.Low, advice.Severity);
        Assert.Contains("Earn money: $489787/500000", advice.Detail);
        Assert.Contains("75", advice.Detail);
        Assert.Contains("100", advice.Detail);
    }

    [Fact]
    public void The_next_star_matters_more_when_the_park_is_full()
    {
        var park = Healthy() with
        {
            Visitors = 71,
            MaxVisitors = 75,
            NextStarTask = "Earn money: $489787/500000",
            Prestige = new PrestigeInfo { Level = 4, MaxVisitors = 75, NextLevelMaxVisitors = 100 },
        };

        Assert.Equal(Severity.Medium, Single(park, "next star").Severity);
    }

    [Fact]
    public void Without_a_star_task_there_is_no_next_star_advice()
    {
        var park = Healthy() with { Prestige = new PrestigeInfo { Level = 6, MaxVisitors = 200 } };

        None(park, "next star");
    }

    [Fact]
    public void More_staff_than_capacity_is_reported_with_what_each_extra_costs()
    {
        var park = Healthy() with { StaffCount = 6, StaffCapacity = 4, StaffTax = 907, StaffTaxPerExtra = 453 };

        var advice = Single(park, "staff over capacity");

        Assert.Contains("6 staff", advice.Detail);
        Assert.Contains("capacity of 4", advice.Detail);
        Assert.Contains("907", advice.Detail);
        Assert.Contains("453 each", advice.Detail);
        Assert.Contains("let 2 go", advice.Detail);
    }

    [Fact]
    public void A_guest_request_nobody_has_answered_is_a_high_priority()
    {
        var park = Healthy() with
        {
            Rides = new[]
            {
                new RideRow { Name = "Wooden Sauna", HasOpenRequest = true },
                new RideRow { Name = "Wooden Hot Tub" },
            },
        };

        var advice = Single(park, "request");

        Assert.Equal(Severity.High, advice.Severity);
        Assert.Contains("Wooden Sauna", advice.Detail);
        Assert.DoesNotContain("Wooden Hot Tub", advice.Detail);
        Assert.Contains("control panel", advice.Detail);
    }

    [Fact]
    public void Advice_is_ordered_from_most_to_least_urgent()
    {
        var park = Healthy() with
        {
            Rides = new[]
            {
                new RideRow { Name = "Hotdogs", Price = 5, IdealPrice = 8 },
                new RideRow { Name = "Kids Pool", IsOpen = false },
                new RideRow { Name = "Big Slide", IsBroken = true },
            },
        };

        var severities = Advisor.Evaluate(park).Select(a => a.Severity).ToArray();

        Assert.Equal(new[] { Severity.High, Severity.Medium, Severity.Low }, severities);
    }
}
