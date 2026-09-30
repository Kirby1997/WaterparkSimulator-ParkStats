namespace ParkStats.Core;

public enum DemandVerdict
{
    NoneBuilt,
    Plenty,
    Fine,
    NearlyFull,
    Short,
}

/// <summary>How much of what serves a need is in use right now.</summary>
public sealed record NeedDemand
{
    public string Need { get; init; } = "";
    public int Units { get; init; }
    public int UsersNow { get; init; }
    public int Capacity { get; init; }
    public int Waiting { get; init; }
    public DemandVerdict Verdict { get; init; }
}

public static class Demand
{
    public static readonly string[] Needs = { "Fun", "Energy", "Hygiene", "Thirst", "Hunger", "Toilet", "Trash" };

    /// <summary>
    /// Food and drink are not tiers of one another: an ice cream stand is not a better vending
    /// machine, and one needs staff where the other does not.
    /// </summary>
    public const string FoodAndDrink = "Food and drink";

    private const double NearlyFullFrom = 0.8;
    private const double PlentyBelow = 0.3;

    /// <summary>
    /// For each need, the open attractions that raise it and how many of their places are taken
    /// right now. A snapshot: worth reading at a busy time of day.
    /// </summary>
    public static IReadOnlyList<NeedDemand> ByNeed(ParkSnapshot park)
    {
        var result = new List<NeedDemand>();
        foreach (var need in Needs)
        {
            var serving = park.Rides.Where(r => r.Raises.Contains(need) && r.IsOpen && !r.IsBroken).ToList();
            if (serving.Count == 0)
            {
                result.Add(new NeedDemand { Need = need, Verdict = DemandVerdict.NoneBuilt });
                continue;
            }

            var users = serving.Sum(r => r.UsersNow ?? 0);
            var capacity = serving.Sum(r => r.Capacity ?? 0);
            var waiting = serving.Sum(r => r.QueueLength ?? 0);
            var verdict = waiting >= serving.Count ? DemandVerdict.Short
                : capacity > 0 && users >= capacity * NearlyFullFrom ? DemandVerdict.NearlyFull
                : capacity > 0 && users < capacity * PlentyBelow && waiting == 0 ? DemandVerdict.Plenty
                : DemandVerdict.Fine;

            result.Add(new NeedDemand
            {
                Need = need, Units = serving.Count, UsersNow = users, Capacity = capacity, Waiting = waiting, Verdict = verdict,
            });
        }
        return result;
    }

    /// <summary>" (staffed)" or " (automated)" for food and drink, nothing when unknown.</summary>
    public static string Service(bool? staffed) => staffed switch
    {
        true => " (staffed)",
        false => " (automated)",
        _ => "",
    };

    /// <summary>
    /// Whether a food or drink building is staffed, judged from its names when there is no
    /// owned one to ask. Nothing when the names do not say.
    /// </summary>
    public static bool? StaffedFromNames(string internalName, string displayName)
    {
        var names = $"{internalName} {displayName}";
        bool Says(string word) => names.Contains(word, StringComparison.OrdinalIgnoreCase);

        // Checked first: stalls are "Vending..." in the game's own class names, machines never "Stand".
        if (Says("Machine")) return false;
        if (Says("Stand") || Says("Stall") || Says("Cart") || Says("Kiosk")) return true;
        return null;
    }

    /// <summary>True when the park has none of this building, going by the catalogue or by name.</summary>
    public static bool NotOwned(ParkSnapshot park, BuildOption option) =>
        option.Owned == 0 && !park.Rides.Any(r => r.Name == option.Name);

    public static string Describe(DemandVerdict verdict) => verdict switch
    {
        DemandVerdict.NoneBuilt => "None built",
        DemandVerdict.Plenty => "Plenty",
        DemandVerdict.Fine => "Fine",
        DemandVerdict.NearlyFull => "Nearly full",
        _ => "Short",
    };

    /// <summary>The best building of a type that can be built now: highest tier, then most room.</summary>
    public static BuildOption? BestUnlocked(IEnumerable<BuildOption> options, Func<BuildOption, bool> match) =>
        options
            .Where(o => o.LockedBy is null && match(o))
            .OrderByDescending(o => o.Tier)
            .ThenByDescending(o => o.Capacity)
            .ThenByDescending(o => o.IdealPrice ?? 0)
            .FirstOrDefault();

    /// <summary>Owned kinds for which a higher tier of the same type can be built.</summary>
    public static IReadOnlyList<(string Owned, int Count, BuildOption Better)> Upgrades(ParkSnapshot park)
    {
        var upgrades = new List<(string, int, BuildOption)>();
        foreach (var kind in park.Rides.Where(r => r.Category is not null && r.Category != FoodAndDrink && r.Tier is not null).GroupBy(r => r.Name))
        {
            var sample = kind.First();
            var best = BestUnlocked(park.BuildOptions, o => o.Category == sample.Category);
            if (best is not null && best.Tier > sample.Tier && best.Name != kind.Key)
            {
                upgrades.Add((kind.Key, kind.Count(), best));
            }
        }
        return upgrades.OrderByDescending(u => u.Item2).ToList();
    }
}
