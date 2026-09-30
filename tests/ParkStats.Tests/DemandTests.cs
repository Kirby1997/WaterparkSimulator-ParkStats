namespace ParkStats.Tests;

public class DemandTests
{
    private static RideRow Lounger(int usersNow) => new()
    {
        Name = "Premium Double Lounger", Category = "Loungers", Tier = 3, Raises = new[] { "Energy" }, Capacity = 2, UsersNow = usersNow,
    };

    private static NeedDemand For(ParkSnapshot park, string need) => Demand.ByNeed(park).Single(d => d.Need == need);

    [Fact]
    public void Few_places_in_use_means_plenty()
    {
        var loungers = Enumerable.Range(0, 31).Select(i => Lounger(i < 6 ? 1 : 0)).ToArray();

        var energy = For(new ParkSnapshot { Rides = loungers }, "Energy");

        Assert.Equal(31, energy.Units);
        Assert.Equal(6, energy.UsersNow);
        Assert.Equal(62, energy.Capacity);
        Assert.Equal(DemandVerdict.Plenty, energy.Verdict);
    }

    [Fact]
    public void Guests_waiting_at_every_place_means_short()
    {
        var slide = new RideRow { Name = "Wave Slide", Raises = new[] { "Fun" }, Capacity = 1, UsersNow = 1, QueueLength = 3 };

        Assert.Equal(DemandVerdict.Short, For(new ParkSnapshot { Rides = new[] { slide } }, "Fun").Verdict);
    }

    [Fact]
    public void Most_places_in_use_means_nearly_full()
    {
        var pool = new RideRow { Name = "Pool", Raises = new[] { "Fun" }, Capacity = 10, UsersNow = 8, QueueLength = 0 };

        Assert.Equal(DemandVerdict.NearlyFull, For(new ParkSnapshot { Rides = new[] { pool } }, "Fun").Verdict);
    }

    [Fact]
    public void A_need_nothing_in_the_park_serves_says_so()
    {
        var thirst = For(new ParkSnapshot { Rides = new[] { Lounger(0) } }, "Thirst");

        Assert.Equal(0, thirst.Units);
        Assert.Equal(DemandVerdict.NoneBuilt, thirst.Verdict);
    }

    private static readonly BuildOption[] Slides =
    {
        new() { Name = "Wave Slide", Category = "Slides", Tier = 2, Capacity = 1, IdealPrice = 30, Price = 1500 },
        new() { Name = "Crew Challenge Slide", Category = "Slides", Tier = 4, Capacity = 1, IdealPrice = 50, Price = 3000 },
        new() { Name = "Mega Slide", Category = "Slides", Tier = 5, Capacity = 2, IdealPrice = 80, Price = 9000, LockedBy = "prestige 5" },
        new() { Name = "Rusty Slide", Category = "Slides", Tier = 0, Capacity = 1, IdealPrice = 5, Price = 100 },
    };

    [Fact]
    public void The_best_unlocked_building_is_the_highest_tier_that_is_not_locked()
    {
        Assert.Equal("Crew Challenge Slide", Demand.BestUnlocked(Slides, o => o.Category == "Slides")!.Name);
    }

    [Fact]
    public void An_owned_kind_below_the_best_unlocked_tier_has_an_upgrade()
    {
        var park = new ParkSnapshot
        {
            Rides = new[]
            {
                new RideRow { Name = "Wave Slide", Category = "Slides", Tier = 2 },
                new RideRow { Name = "Wave Slide", Category = "Slides", Tier = 2 },
            },
            BuildOptions = Slides,
        };

        var upgrade = Assert.Single(Demand.Upgrades(park));

        Assert.Equal("Wave Slide", upgrade.Owned);
        Assert.Equal(2, upgrade.Count);
        Assert.Equal("Crew Challenge Slide", upgrade.Better.Name);
    }

    [Fact]
    public void Food_and_drink_are_never_offered_as_upgrades_of_one_another()
    {
        var park = new ParkSnapshot
        {
            Rides = new[] { new RideRow { Name = "Snacks Vending Machine", Category = Demand.FoodAndDrink, Tier = 1, Staffed = false } },
            BuildOptions = new[]
            {
                new BuildOption { Name = "Snacks Vending Machine", Category = Demand.FoodAndDrink, Tier = 1, Staffed = false },
                new BuildOption { Name = "Ice Cream Stand", Category = Demand.FoodAndDrink, Tier = 3, Staffed = true },
            },
        };

        Assert.Empty(Demand.Upgrades(park));
    }

    [Fact]
    public void Owning_the_best_unlocked_tier_needs_no_upgrade()
    {
        var park = new ParkSnapshot
        {
            Rides = new[] { new RideRow { Name = "Crew Challenge Slide", Category = "Slides", Tier = 4 } },
            BuildOptions = Slides,
        };

        Assert.Empty(Demand.Upgrades(park));
    }
}
