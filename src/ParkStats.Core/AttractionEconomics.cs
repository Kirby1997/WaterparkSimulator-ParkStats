namespace ParkStats.Core;

/// <summary>What one attraction of a kind earns against what it costs to keep, and how wanted it is.</summary>
public sealed record KindEconomics
{
    public string Name { get; init; } = "";
    public int Count { get; init; }
    public double EarnedEach { get; init; }
    public double UpkeepEach { get; init; }

    /// <summary>True when the earnings are a full day's, from history, rather than today so far.</summary>
    public bool FullDay { get; init; }

    /// <summary>Earnings less upkeep per day. Only known for a full day.</summary>
    public double? NetEach { get; init; }
    public int? UsersNow { get; init; }
    public int? Capacity { get; init; }
    public int Waiting { get; init; }
    public bool Busy { get; init; }
    public double? BuildPrice { get; init; }

    /// <summary>Days for another one to earn back its price, when it makes money at all.</summary>
    public double? PaybackDays { get; init; }
}

public static class AttractionEconomics
{
    // A kind counts as busy when nearly every place is taken, or a guest is waiting at each one.
    private const double NearlyFull = 0.8;

    /// <summary>One entry per kind of attraction that charges or costs upkeep, best earner first.</summary>
    public static IReadOnlyList<KindEconomics> For(ParkSnapshot park)
    {
        var kinds = new List<KindEconomics>();
        foreach (var group in park.Rides.GroupBy(r => r.Name))
        {
            var rides = group.ToList();
            var upkeepEach = rides.Average(r => r.MaintenancePerDay ?? 0);
            if (!rides.Any(r => r.Price > 0) && upkeepEach <= 0) continue;

            AttractionDay? yesterday = null;
            park.YesterdayAttractions?.TryGetValue(group.Key, out yesterday);
            var fullDay = yesterday is { Count: > 0 };

            var earnedEach = fullDay ? yesterday!.Earned / yesterday.Count : rides.Sum(r => r.Earned) / rides.Count;
            double? netEach = fullDay ? earnedEach - upkeepEach : null;
            var buildPrice = rides.Select(r => r.BuildPrice).FirstOrDefault(p => p is not null);

            int? usersNow = rides.Any(r => r.UsersNow is not null) ? rides.Sum(r => r.UsersNow ?? 0) : null;
            int? capacity = rides.Any(r => r.Capacity is not null) ? rides.Sum(r => r.Capacity ?? 0) : null;
            var waiting = rides.Sum(r => r.QueueLength ?? 0);

            kinds.Add(new KindEconomics
            {
                Name = group.Key,
                Count = rides.Count,
                EarnedEach = earnedEach,
                UpkeepEach = upkeepEach,
                FullDay = fullDay,
                NetEach = netEach,
                UsersNow = usersNow,
                Capacity = capacity,
                Waiting = waiting,
                Busy = waiting >= rides.Count || (capacity > 0 && usersNow >= capacity * NearlyFull),
                BuildPrice = buildPrice,
                PaybackDays = netEach > 0 && buildPrice > 0 ? buildPrice / netEach : null,
            });
        }

        return kinds
            .OrderByDescending(k => k.NetEach ?? double.MinValue)
            .ThenByDescending(k => k.EarnedEach)
            .ToList();
    }
}
