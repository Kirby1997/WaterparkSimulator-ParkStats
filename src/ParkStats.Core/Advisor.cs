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
    private const double VisitorCapShortfall = 0.8;
    private const double DecorationLowBelow = 0.5;
    private const double StaffCostShare = 0.5;
    private const int NamesShown = 3;

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
        AddWeakestNeed(park, advice);
        AddCapacity(park, advice);
        AddTicketPrice(park, advice);
        AddRidePricing(park, advice);
        AddVisitorCap(park, advice);
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

    private static void AddVisitorCap(ParkSnapshot park, List<Advice> advice)
    {
        if (park.MaxVisitors is not { } cap) return;
        if (park.Prestige is not { MaxVisitors: { } allowed } prestige || allowed <= 0) return;

        if (cap < allowed * VisitorCapShortfall)
        {
            var detail = $"The park admits up to {cap} visitors; prestige {prestige.Level} allows {allowed}. " +
                         "Attractions and recent guest satisfaction decide how much of that you get.";
            if (park.RecentSatisfaction is { } recent)
            {
                detail += $" Recent satisfaction is {Format.Percent(recent)}.";
            }
            advice.Add(new Advice(Severity.Medium, "Visitor cap is low", detail));
        }
        else if (cap >= allowed && prestige.NextLevelMaxVisitors is { } next)
        {
            var detail = $"{cap} visitors is the most prestige {prestige.Level} allows. Prestige {prestige.Level + 1} allows {next}.";
            if (!string.IsNullOrWhiteSpace(park.NextStarTask))
            {
                detail += $" For the next star: {park.NextStarTask}";
            }

            // Only a park that actually fills up is held back by this.
            var full = park.Visitors >= cap * NearFull;
            advice.Add(new Advice(full ? Severity.Medium : Severity.Low, "Prestige limit reached", detail));
        }
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
                detail += $" Staff over capacity are taxed: {Format.Money(park.StaffTax)} a day at the moment.";
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
