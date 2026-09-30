namespace ParkStats.Core;

// Plain data read from the game. Fractions (satisfaction, cleanliness, durability) are 0..1.
// Null means the value could not be read, e.g. host-only data on a multiplayer client.

public sealed record MoneyLine(string Reason, double Amount);

public sealed record LoanLine(string Label, double Remaining);

public sealed record CountLine(string Label, int Count);

public sealed record CapacityLine(string Name, int Current, int Max);

public sealed record NeedStat(string Name, double? Average, int GuestsBelowThreshold, int GuestsCounted);

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
}

public sealed record PrestigeInfo
{
    public int Level { get; init; }
    public double DecorationPoints { get; init; }
    public double? NextLevelPoints { get; init; }
    public int? NextLevelMaxVisitors { get; init; }
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
    public double? AverageGuestCash { get; init; }
    public IReadOnlyList<NeedStat> Needs { get; init; } = Array.Empty<NeedStat>();
    public IReadOnlyList<CountLine> Complaints { get; init; } = Array.Empty<CountLine>();
    public IReadOnlyList<CountLine> LeavingReasons { get; init; } = Array.Empty<CountLine>();

    public PrestigeInfo? Prestige { get; init; }

    public IReadOnlyList<RideRow> Rides { get; init; } = Array.Empty<RideRow>();
    public IReadOnlyList<CapacityLine> Capacity { get; init; } = Array.Empty<CapacityLine>();
    public double? CleanlinessThreshold { get; init; }
    public double? DurabilityThreshold { get; init; }

    public int? StaffCount { get; init; }
    public int? StaffCapacity { get; init; }
    public double? StaffSalary { get; init; }
    public double? StaffTax { get; init; }
}
