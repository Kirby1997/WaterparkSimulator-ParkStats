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
    public void Decoration_needed_for_the_next_prestige_level_is_reported()
    {
        var park = Healthy() with
        {
            Prestige = new PrestigeInfo { Level = 2, DecorationPoints = 340, NextLevelPoints = 500, NextLevelMaxVisitors = 100 },
        };

        var advice = Single(park, "prestige 3");

        Assert.Equal(Severity.Low, advice.Severity);
        Assert.Contains("160", advice.Detail);
        Assert.Contains("100", advice.Detail);
    }

    [Fact]
    public void Prestige_becomes_a_high_priority_when_the_park_is_nearly_full()
    {
        var park = Healthy() with
        {
            Visitors = 70,
            MaxVisitors = 75,
            Prestige = new PrestigeInfo { Level = 2, DecorationPoints = 340, NextLevelPoints = 500 },
        };

        Assert.Equal(Severity.High, Single(park, "prestige 3").Severity);
    }

    [Fact]
    public void At_the_top_prestige_level_there_is_no_prestige_advice()
    {
        var park = Healthy() with { Prestige = new PrestigeInfo { Level = 5, DecorationPoints = 900, NextLevelPoints = null } };

        None(park, "prestige");
    }

    [Fact]
    public void Expected_attendance_far_below_the_cap_is_reported()
    {
        var park = Healthy() with { ExpectedVisitors = 48, MaxVisitors = 75 };

        var advice = Single(park, "attendance");

        Assert.Contains("48", advice.Detail);
        Assert.Contains("75", advice.Detail);
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
    public void Staff_costing_more_than_half_of_todays_income_is_reported()
    {
        var park = Healthy() with
        {
            MoneyToday = new[] { new MoneyLine("Ticket", 1000) },
            StaffSalary = 500,
            StaffTax = 100,
        };

        var advice = Single(park, "staff cost");

        Assert.Contains("600", advice.Detail);
        Assert.Contains("60%", advice.Detail);
    }

    [Fact]
    public void A_loan_does_not_count_as_income_when_judging_staff_cost()
    {
        var park = Healthy() with
        {
            MoneyToday = new[] { new MoneyLine("Ticket", 1000), new MoneyLine("Loan", 5000) },
            StaffSalary = 600,
        };

        Assert.Contains("60%", Single(park, "staff cost").Detail);
    }

    [Fact]
    public void More_staff_than_capacity_is_reported()
    {
        var park = Healthy() with { StaffCount = 8, StaffCapacity = 6 };

        Assert.Contains("8/6", Single(park, "staff over capacity").Detail);
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
