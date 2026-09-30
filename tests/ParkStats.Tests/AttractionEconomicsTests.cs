namespace ParkStats.Tests;

public class AttractionEconomicsTests
{
    private static RideRow Slide(int uses = 3, int? queue = null, int? usersNow = null) => new()
    {
        Name = "Wave Slide", UsesToday = uses, Price = 30, IdealPrice = 30, MaintenancePerDay = 70,
        BuildPrice = 2000, Capacity = 1, UsersNow = usersNow, QueueLength = queue,
    };

    private static KindEconomics Only(ParkSnapshot park) => Assert.Single(AttractionEconomics.For(park));

    [Fact]
    public void A_full_days_earnings_from_history_are_set_against_a_days_upkeep()
    {
        var park = new ParkSnapshot
        {
            Rides = new[] { Slide(), Slide() },
            YesterdayAttractions = new Dictionary<string, AttractionDay>
            {
                ["Wave Slide"] = new() { Count = 2, Uses = 40, Earned = 1200 },
            },
        };

        var slide = Only(park);

        Assert.True(slide.FullDay);
        Assert.Equal(2, slide.Count);
        Assert.Equal(600, slide.EarnedEach);
        Assert.Equal(70, slide.UpkeepEach);
        Assert.Equal(530, slide.NetEach);
    }

    [Fact]
    public void Payback_is_the_build_price_over_what_one_nets_in_a_day()
    {
        var park = new ParkSnapshot
        {
            Rides = new[] { Slide() },
            YesterdayAttractions = new Dictionary<string, AttractionDay> { ["Wave Slide"] = new() { Count = 1, Earned = 570 } },
        };

        Assert.Equal(4, Only(park).PaybackDays);
    }

    [Fact]
    public void Without_a_recorded_day_todays_takings_are_shown_but_no_net_is_claimed()
    {
        // Set against a whole day of upkeep, a morning's takings would make everything look like a loss.
        var slide = Only(new ParkSnapshot { Rides = new[] { Slide(uses: 3), Slide(uses: 5) } });

        Assert.False(slide.FullDay);
        Assert.Equal(120, slide.EarnedEach);
        Assert.Null(slide.NetEach);
        Assert.Null(slide.PaybackDays);
    }

    [Fact]
    public void A_kind_that_loses_money_has_no_payback()
    {
        var park = new ParkSnapshot
        {
            Rides = new[] { Slide() },
            YesterdayAttractions = new Dictionary<string, AttractionDay> { ["Wave Slide"] = new() { Count = 1, Earned = 40 } },
        };

        var slide = Only(park);

        Assert.Equal(-30, slide.NetEach);
        Assert.Null(slide.PaybackDays);
    }

    [Fact]
    public void A_kind_with_a_guest_waiting_per_attraction_is_busy()
    {
        Assert.True(Only(new ParkSnapshot { Rides = new[] { Slide(queue: 1) } }).Busy);
    }

    [Fact]
    public void A_kind_nearly_full_right_now_is_busy()
    {
        var pool = new RideRow { Name = "Pool", Price = 33, Capacity = 8, UsersNow = 7 };

        Assert.True(Only(new ParkSnapshot { Rides = new[] { pool } }).Busy);
    }

    [Fact]
    public void A_half_empty_kind_with_nobody_waiting_is_not_busy()
    {
        var pool = new RideRow { Name = "Pool", Price = 33, Capacity = 8, UsersNow = 3, QueueLength = 0 };

        Assert.False(Only(new ParkSnapshot { Rides = new[] { pool } }).Busy);
    }

    [Fact]
    public void Things_that_neither_charge_nor_cost_upkeep_are_left_out()
    {
        var bin = new RideRow { Name = "Park Bin", UsesToday = 20 };

        Assert.Empty(AttractionEconomics.For(new ParkSnapshot { Rides = new[] { bin } }));
    }

    [Fact]
    public void Kinds_are_ordered_by_what_each_nets_best_first()
    {
        var park = new ParkSnapshot
        {
            Rides = new[] { Slide(), new RideRow { Name = "Sauna", Price = 25, MaintenancePerDay = 50 } },
            YesterdayAttractions = new Dictionary<string, AttractionDay>
            {
                ["Wave Slide"] = new() { Count = 1, Earned = 300 },
                ["Sauna"] = new() { Count = 1, Earned = 900 },
            },
        };

        Assert.Equal(new[] { "Sauna", "Wave Slide" }, AttractionEconomics.For(park).Select(k => k.Name));
    }
}
