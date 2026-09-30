namespace ParkStats.Core;

public enum Severity
{
    Low,
    Medium,
    High,
}

public sealed record Advice(Severity Severity, string Title, string Detail);

/// <summary>
/// Turns a snapshot into a ranked list of things to fix. The rules compare values the game
/// exposes against the game's own thresholds where it has them; they do not reproduce the
/// game's internal formulas.
/// </summary>
public static class Advisor
{
    // A ride is called dirty or worn below this. The game's own threshold is used when it is
    // stricter; the game's can sit at "fully clean", which would flag every ride all day.
    private const double ConditionThreshold = 0.5;

    private const double NeedMediumBelow = 0.6;
    private const double NeedHighBelow = 0.4;
    private const double NearFull = 0.9;
    private const double TicketTolerance = 0.1;
    private const double RidePriceTolerance = 0.2;
    private const double DecorationLowBelow = 0.5;
    private const double StaffCostShare = 0.5;
    private const int NamesShown = 3;
    // Below this many uses across the park, an attraction with none yet says nothing.
    private const int BusyParkUses = 50;
    private const int ShortestQueueWorthBuildingFor = 2;

    private static readonly Dictionary<string, string> NeedHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Fun"] = "Add more attractions or reopen closed ones.",
        ["Energy"] = "Add places to rest.",
        ["Hygiene"] = "Add showers and keep pools clean.",
        ["Thirst"] = "Add or restock drink stands.",
        ["Hunger"] = "Add or restock food stands.",
        ["Toilet"] = "Build more toilets.",
        ["Trash"] = "Add trash bins or cleaning staff.",
    };

    public static IReadOnlyList<Advice> Evaluate(ParkSnapshot park)
    {
        var advice = new List<Advice>();

        AddRideCondition(park, advice);
        AddOpenRequests(park, advice);
        AddEmptyStands(park, advice);
        AddQueues(park, advice);
        AddWeakestNeed(park, advice);
        AddCapacity(park, advice);
        AddTicketPrice(park, advice);
        AddRidePricing(park, advice);
        AddUnused(park, advice);
        AddSatisfactionMultiplier(park, advice);
        AddNextStar(park, advice);
        AddDecoration(park, advice);
        AddStaff(park, advice);
        AddTopComplaint(park, advice);

        // OrderByDescending is stable, so rules keep their order within a severity.
        return advice.OrderByDescending(a => a.Severity).ToList();
    }

    private static void AddRideCondition(ParkSnapshot park, List<Advice> advice)
    {
        var broken = park.Rides.Where(r => r.IsBroken).ToList();
        if (broken.Count > 0)
        {
            advice.Add(new Advice(Severity.High, "Repair broken rides",
                $"{Names(broken)}: malfunctioning, so no guests can use them."));
        }

        var closed = park.Rides.Where(r => !r.IsOpen && !r.IsBroken).ToList();
        if (closed.Count > 0)
        {
            advice.Add(new Advice(Severity.Medium, "Reopen closed rides",
                $"{Names(closed)}: closed, earning nothing and serving no needs."));
        }

        var cleanLimit = Math.Min(park.CleanlinessThreshold ?? ConditionThreshold, ConditionThreshold);
        var dirty = park.Rides.Where(r => r.Cleanliness < cleanLimit).ToList();
        if (dirty.Count > 0)
        {
            advice.Add(new Advice(Severity.Medium, "Clean dirty rides",
                $"{Names(dirty)}: cleanliness below {Format.Percent(cleanLimit)}."));
        }

        var wearLimit = Math.Min(park.DurabilityThreshold ?? ConditionThreshold, ConditionThreshold);
        var worn = park.Rides.Where(r => r.Durability < wearLimit).ToList();
        if (worn.Count > 0)
        {
            advice.Add(new Advice(Severity.Medium, "Maintain worn rides",
                $"{Names(worn)}: durability below {Format.Percent(wearLimit)}, breakdowns are likely."));
        }
    }

    private static void AddOpenRequests(ParkSnapshot park, List<Advice> advice)
    {
        var waiting = Kinds(park.Rides.Where(r => r.HasOpenRequest));
        if (waiting.Count == 0) return;

        advice.Add(new Advice(Severity.High, "Guests have a request open",
            $"{KindNames(waiting)}: a guest inside has asked for different settings. Set them on the control panel; " +
            "a guest who leaves without them complains of bad service."));
    }

    private static void AddEmptyStands(ParkSnapshot park, List<Advice> advice)
    {
        var empty = Kinds(park.Rides.Where(r => r.IsOpen && !r.IsBroken && r.StockCapacity > 0 && r.StockLeft == 0));
        if (empty.Count == 0) return;

        advice.Add(new Advice(Severity.High, "Restock empty stands",
            $"{KindNames(empty)}: sold out. Every guest who walks up leaves without buying."));
    }

    private static void AddQueues(ParkSnapshot park, List<Advice> advice)
    {
        var queued = Kinds(park.Rides)
            .Select(kind => (Kind: kind, Waiting: kind.Sum(r => r.QueueLength ?? 0)))
            .Where(k => k.Waiting >= Math.Max(ShortestQueueWorthBuildingFor, k.Kind.Count))
            .OrderByDescending(k => k.Waiting)
            .Take(NamesShown)
            .ToList();
        if (queued.Count == 0) return;

        var lines = queued.Select(k =>
        {
            var line = $"{KindName(k.Kind)}: {k.Waiting} waiting";
            var earnedEach = k.Kind.Sum(r => r.Earned) / k.Kind.Count;
            return earnedEach > 0 ? $"{line}, about {Format.Money(earnedEach)} earned each today" : line;
        });
        advice.Add(new Advice(Severity.Medium, "Guests are queuing",
            $"{string.Join("; ", lines)}. Another of these would serve them sooner."));
    }

    private static void AddUnused(ParkSnapshot park, List<Advice> advice)
    {
        if (park.Rides.Sum(r => r.UsesToday) < BusyParkUses) return;

        var unused = Kinds(park.Rides.Where(r => r.Price > 0 && r.IsOpen && !r.IsBroken))
            .Where(kind => kind.All(r => r.UsesToday == 0))
            .ToList();
        if (unused.Count == 0) return;

        advice.Add(new Advice(Severity.Low, "Unused paid attractions",
            $"{KindNames(unused)}: no guest has used them today. Check that guests can reach them, or try a lower price."));
    }

    private static List<List<RideRow>> Kinds(IEnumerable<RideRow> rides) =>
        rides.GroupBy(r => r.Name).Select(g => g.ToList()).ToList();

    private static string KindName(IReadOnlyList<RideRow> kind) =>
        kind.Count > 1 ? $"{kind[0].Name} x{kind.Count}" : kind[0].Name;

    private static string KindNames(IReadOnlyList<List<RideRow>> kinds)
    {
        var shown = string.Join(", ", kinds.Take(NamesShown).Select(KindName));
        return kinds.Count > NamesShown ? $"{shown} and {kinds.Count - NamesShown} more" : shown;
    }

    private static void AddWeakestNeed(ParkSnapshot park, List<Advice> advice)
    {
        var weakest = park.Needs.Where(n => n.Average is not null).MinBy(n => n.Average);
        if (weakest?.Average is not { } average || average >= NeedMediumBelow) return;

        var severity = average < NeedHighBelow ? Severity.High : Severity.Medium;
        var detail = $"{weakest.Name} satisfaction is {Format.Percent(average)}";
        if (weakest.GuestsCounted > 0)
        {
            detail += $"; {weakest.GuestsLow} of {weakest.GuestsCounted} guests are under half";
        }
        detail += ".";
        if (weakest.Sources.Count > 0)
        {
            detail += $" In the park it is raised by: {string.Join(", ", weakest.Sources)}.";
        }
        if (NeedHints.TryGetValue(weakest.Name, out var hint))
        {
            detail += " " + hint;
        }

        advice.Add(new Advice(severity, $"Weakest need: {weakest.Name}", detail));
    }

    private static void AddCapacity(ParkSnapshot park, List<Advice> advice)
    {
        foreach (var line in park.Capacity)
        {
            if (line.Max <= 0 || line.Current < line.Max * NearFull) continue;
            advice.Add(new Advice(Severity.Medium, $"{line.Name} at capacity",
                $"{line.Current}/{line.Max} in use. Build more so guests are not turned away."));
        }
    }

    private static void AddTicketPrice(ParkSnapshot park, List<Advice> advice)
    {
        if (park.TicketPrice is not { } price || park.TargetTicketPrice is not { } target || target <= 0) return;

        if (price < target * (1 - TicketTolerance))
        {
            advice.Add(new Advice(Severity.Medium, "Raise ticket price",
                $"Ticket price is {Format.Money(price)}, the park supports {Format.Money(target)}."));
        }
        else if (price > target * (1 + TicketTolerance))
        {
            advice.Add(new Advice(Severity.Medium, "Lower ticket price",
                $"Ticket price is {Format.Money(price)}, above the {Format.Money(target)} the park supports. Guests may complain or leave."));
        }
    }

    private static void AddRidePricing(ParkSnapshot park, List<Advice> advice)
    {
        var priced = park.Rides.Where(r => r.Price is not null && r.IdealPrice > 0).ToList();

        var under = priced.Where(r => r.Price < r.IdealPrice * (1 - RidePriceTolerance)).ToList();
        if (under.Count > 0)
        {
            advice.Add(new Advice(Severity.Low, "Underpriced rides",
                $"{Names(under, PriceLabel)}: raising prices adds income."));
        }

        var over = priced.Where(r => r.Price > r.IdealPrice * (1 + RidePriceTolerance)).ToList();
        if (over.Count > 0)
        {
            advice.Add(new Advice(Severity.Low, "Overpriced rides",
                $"{Names(over, PriceLabel)}: guests may skip them."));
        }
    }

    /// <summary>
    /// The game multiplies the visitor cap by a factor read off recent satisfaction: below
    /// one when guests have been unhappy, above one when they have been very happy.
    /// </summary>
    private static void AddSatisfactionMultiplier(ParkSnapshot park, List<Advice> advice)
    {
        if (park.VisitorMultiplier is not { } multiplier || park.BestVisitorMultiplier is not { } best) return;
        if (multiplier >= best - 0.005) return;

        var satisfaction = park.RecentSatisfaction is { } recent ? $"Recent satisfaction of {Format.Percent(recent)}" : "Recent satisfaction";
        var detail = $"{satisfaction} puts the game's visitor multiplier at {Format.Multiplier(multiplier)}. " +
                     $"The best is {Format.Multiplier(best)}. Raising the weakest needs lifts it.";

        advice.Add(multiplier < 1
            ? new Advice(Severity.Medium, "Low satisfaction is costing visitors", detail)
            : new Advice(Severity.Low, "Higher satisfaction brings more visitors", detail));
    }

    private static void AddNextStar(ParkSnapshot park, List<Advice> advice)
    {
        if (string.IsNullOrWhiteSpace(park.NextStarTask)) return;

        var detail = "";
        if (park.Prestige is { MaxVisitors: { } allowed, NextLevelMaxVisitors: { } next } prestige)
        {
            detail = $"Prestige {prestige.Level + 1} raises the visitor base from {allowed} to {next}. ";
        }
        detail += $"Still to do: {park.NextStarTask}";

        // A park that fills up is held back by its star more than by anything else.
        var full = park.Visitors is { } visitors && park.MaxVisitors is { } cap && cap > 0 && visitors >= cap * NearFull;
        advice.Add(new Advice(full ? Severity.Medium : Severity.Low, "Next star", detail));
    }

    private static void AddDecoration(ParkSnapshot park, List<Advice> advice)
    {
        if (park.Prestige?.DecorationLevel is not { } level || level >= DecorationLowBelow) return;

        advice.Add(new Advice(Severity.Low, "Decorate the park",
            $"Decoration is at {Format.Percent(level)}. Guests notice decoration, and it raises tips."));
    }

    private static void AddStaff(ParkSnapshot park, List<Advice> advice)
    {
        if (park.StaffCount is { } count && park.StaffCapacity is { } capacity && count > capacity)
        {
            var over = count - capacity;
            var detail = $"{count} staff for a capacity of {capacity}.";
            if (park.StaffTax > 0)
            {
                detail += $" The {over} extra cost {Format.Money(park.StaffTax)} a day in staff tax";
                if (park.StaffTaxPerExtra > 0) detail += $" ({Format.Money(park.StaffTaxPerExtra)} each)";
                detail += ".";
            }
            detail += $" Expand staff capacity or let {over} go.";
            advice.Add(new Advice(Severity.Medium, "Staff over capacity", detail));
        }

        // Judged against a whole day. Income so far today starts at nothing every morning.
        if (park.StaffSalary is not { } salary || park.TypicalDayIncome is not { } income || income <= 0) return;

        var cost = salary + (park.StaffTax ?? 0);
        if (cost <= income * StaffCostShare) return;

        advice.Add(new Advice(Severity.Medium, "Staff cost is high",
            $"Staff cost {Format.Money(cost)} a day is {Format.Percent(cost / income)} of a typical day's income ({Format.Money(income)})."));
    }

    private static void AddTopComplaint(ParkSnapshot park, List<Advice> advice)
    {
        var top = park.Complaints.Where(c => c.Count > 0).MaxBy(c => c.Count);
        if (top is null) return;

        var detail = $"\"{top.Label}\" ({top.Count} times).";
        if (!string.IsNullOrWhiteSpace(top.Note)) detail += " " + top.Note;
        advice.Add(new Advice(Severity.Low, "Top complaint", detail));
    }

    private static string PriceLabel(RideRow ride) =>
        $"{ride.Name} ({Format.Money(ride.Price)}, ideal {Format.Money(ride.IdealPrice)})";

    private static string Names(IReadOnlyList<RideRow> rides, Func<RideRow, string>? label = null)
    {
        label ??= r => r.Name;
        var shown = string.Join(", ", rides.Take(NamesShown).Select(label));
        return rides.Count > NamesShown ? $"{shown} and {rides.Count - NamesShown} more" : shown;
    }
}
