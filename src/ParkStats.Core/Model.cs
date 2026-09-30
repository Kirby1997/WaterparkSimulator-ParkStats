namespace ParkStats.Core;

// Plain data read from the game. Fractions (satisfaction, cleanliness, durability) are 0..1.
// Null means the value could not be read, e.g. host-only data on a multiplayer client.

public sealed record MoneyLine(string Reason, double Amount);

public sealed record LoanLine(string Label, double Remaining);

public sealed record CountLine(string Label, int Count)
{
    /// <summary>What the count means, in the game's own words when it has them.</summary>
    public string? Note { get; init; }
}

public sealed record CapacityLine(string Name, int Current, int Max);

public sealed record NeedStat(string Name, double? Average, int GuestsLow, int GuestsCounted)
{
    /// <summary>Attractions in the park that raise this need, e.g. "Far Shore Shower x4".</summary>
    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();
}

public sealed record RideRow
{
    public string Name { get; init; } = "";
    public bool IsOpen { get; init; } = true;
    public bool IsBroken { get; init; }
    public int UsesToday { get; init; }
    public double? Price { get; init; }
    public double? IdealPrice { get; init; }
    public double? Cleanliness { get; init; }
    public double? Durability { get; init; }
    public int? QueueLength { get; init; }
    public double? MaintenancePerDay { get; init; }

    /// <summary>Items left to sell and how many fit, for stands and vending machines.</summary>
    public int? StockLeft { get; init; }
    public int? StockCapacity { get; init; }

    /// <summary>Guests using it right now, and how many it takes at once.</summary>
    public int? UsersNow { get; init; }
    public int? Capacity { get; init; }

    /// <summary>What another one costs to build.</summary>
    public double? BuildPrice { get; init; }

    /// <summary>A guest inside has asked for different settings and not yet got them (sauna, hot tub).</summary>
    public bool HasOpenRequest { get; init; }

    /// <summary>Uses today times the current price: what it has taken, near enough.</summary>
    public double Earned => UsesToday * (Price ?? 0);
}

/// <summary>One kind of attraction over one day: how many there were, their uses and takings.</summary>
public sealed record AttractionDay
{
    public int Count { get; init; }
    public int Uses { get; init; }
    public double Earned { get; init; }
}

/// <summary>Something the player could build, from the game's building catalogue.</summary>
public sealed record BuildOption
{
    public string Name { get; init; } = "";
    public double Price { get; init; }

    /// <summary>Visitors it adds room for: how many guests can use it at once.</summary>
    public int Capacity { get; init; }
    public double? IdealPrice { get; init; }
    public double? MaintenancePerDay { get; init; }
    public IReadOnlyList<string> Raises { get; init; } = Array.Empty<string>();
    public int Owned { get; init; }

    /// <summary>Why it cannot be built yet, e.g. "prestige 5"; nothing when it can.</summary>
    public string? LockedBy { get; init; }
}

public sealed record PrestigeInfo
{
    public int Level { get; init; }

    /// <summary>The most visitors this prestige level allows, whatever else the park offers.</summary>
    public int? MaxVisitors { get; init; }
    public int? NextLevelMaxVisitors { get; init; }
    public double? DecorationLevel { get; init; }
}

public sealed record ParkSnapshot
{
    public int? Day { get; init; }

    public double? Money { get; init; }
    public double? TotalEarned { get; init; }
    public double? TotalSpent { get; init; }
    public double? TicketPrice { get; init; }
    public double? TargetTicketPrice { get; init; }
    public IReadOnlyList<MoneyLine> MoneyToday { get; init; } = Array.Empty<MoneyLine>();
    public IReadOnlyList<LoanLine> Loans { get; init; } = Array.Empty<LoanLine>();

    public int? Visitors { get; init; }
    public int? ExpectedVisitors { get; init; }
    public int? MaxVisitors { get; init; }
    public int? VisitorsToday { get; init; }
    public int? RefundedVisitors { get; init; }
    public int? InjuredVisitors { get; init; }
    public double? Satisfaction { get; init; }

    /// <summary>The game's satisfaction figure for the day as a whole, once it has one.</summary>
    public double? DaySatisfaction { get; init; }

    /// <summary>The game's average over recent days, which is what sets the visitor cap.</summary>
    public double? RecentSatisfaction { get; init; }
    public double? AverageGuestCash { get; init; }
    public IReadOnlyList<NeedStat> Needs { get; init; } = Array.Empty<NeedStat>();
    public IReadOnlyList<CountLine> Complaints { get; init; } = Array.Empty<CountLine>();
    public IReadOnlyList<CountLine> LeavingReasons { get; init; } = Array.Empty<CountLine>();

    public PrestigeInfo? Prestige { get; init; }

    /// <summary>
    /// Visitors the park's attractions have room for. The game's visitor cap is this number,
    /// limited to what the prestige level allows.
    /// </summary>
    public int? AttractionCapacity { get; init; }
    public IReadOnlyList<BuildOption> BuildOptions { get; init; } = Array.Empty<BuildOption>();

    /// <summary>
    /// What each kind of attraction did over the last full day, from history. Today's figures
    /// cover only part of a day, so they cannot be set against a full day's upkeep.
    /// </summary>
    public IReadOnlyDictionary<string, AttractionDay>? YesterdayAttractions { get; init; }

    /// <summary>The game's own description of what the next star needs.</summary>
    public string? NextStarTask { get; init; }

    /// <summary>The game's multiplier on visitor numbers at the current recent satisfaction, and at the best.</summary>
    public double? VisitorMultiplier { get; init; }
    public double? BestVisitorMultiplier { get; init; }

    /// <summary>Income of a full day, from recent history. Today's income is only a part of a day.</summary>
    public double? TypicalDayIncome { get; init; }

    public IReadOnlyList<RideRow> Rides { get; init; } = Array.Empty<RideRow>();
    public IReadOnlyList<CapacityLine> Capacity { get; init; } = Array.Empty<CapacityLine>();
    public double? CleanlinessThreshold { get; init; }
    public double? DurabilityThreshold { get; init; }

    public int? StaffCount { get; init; }
    public int? StaffCapacity { get; init; }
    public double? StaffSalary { get; init; }
    public double? StaffTax { get; init; }

    /// <summary>Staff tax added by each member of staff over capacity.</summary>
    public double? StaffTaxPerExtra { get; init; }
}
