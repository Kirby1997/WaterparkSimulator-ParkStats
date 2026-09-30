namespace ParkStats.Core;

public enum RowKind
{
    Header,
    Normal,
    Good,
    Bad,
    Muted,
}

/// <summary>One line of a tab: a single cell is a full-width line, several cells are columns.</summary>
public sealed record Row(RowKind Kind, IReadOnlyList<string> Cells)
{
    public static Row Of(RowKind kind, params string[] cells) => new(kind, cells);

    public string Text => string.Join(" | ", Cells);
}

/// <summary>Builds the text content of each tab from a snapshot, independent of how it is drawn.</summary>
public static class Pages
{
    private const string NothingToFix = "Nothing needs fixing right now";
    private const string Blank = "-";
    private const double NeedGoodFrom = 0.7;
    private const double NeedBadBelow = 0.4;
    private const int ComplaintsShown = 5;
    private const int OverviewAdviceShown = 3;
    private const int LongestRideName = 20;
    private const int EarnersShown = 8;
    private const int BuildOptionsShown = 6;
    private const int EconomicsShown = 12;
    // Some game modes give guests a practically endless wallet.
    private const double UnlimitedCashFrom = 500_000;

    public static IReadOnlyList<Row> Overview(ParkSnapshot park, IReadOnlyList<Advice> advice)
    {
        var rows = new List<Row> { Row.Of(RowKind.Header, "Today") };

        var net = MoneyBreakdown.From(park.MoneyToday).Net;
        rows.Add(Row.Of(SignKind(net), "Net today", Format.Signed(net)));
        AddIfKnown(rows, "Balance", park.Money);

        if (park.Visitors is not null)
        {
            var visitors = park.MaxVisitors is null
                ? Format.Count(park.Visitors)
                : Format.Ratio(park.Visitors, park.MaxVisitors);
            if (park.ExpectedVisitors is { } expected && expected != park.MaxVisitors) visitors += $" (expected {expected})";
            rows.Add(Row.Of(RowKind.Normal, "Visitors", visitors));
        }

        if (park.Satisfaction is not null)
        {
            rows.Add(Row.Of(RowKind.Normal, "Satisfaction", Format.Percent(park.Satisfaction)));
        }

        if (park.Prestige is { } prestige)
        {
            var level = Format.Count(prestige.Level);
            if (prestige.MaxVisitors is { } allowed) level += $" (allows {allowed} visitors)";
            rows.Add(Row.Of(RowKind.Normal, "Prestige", level));
            if (prestige.DecorationLevel is not null)
            {
                rows.Add(Row.Of(RowKind.Normal, "Decoration", Format.Percent(prestige.DecorationLevel)));
            }
        }

        rows.Add(Row.Of(RowKind.Header, "Do next"));
        rows.AddRange(Advisor(advice.Take(OverviewAdviceShown).ToList()));

        return rows;
    }

    public static IReadOnlyList<Row> Money(ParkSnapshot park)
    {
        var breakdown = MoneyBreakdown.From(park.MoneyToday);
        var rows = new List<Row> { Row.Of(RowKind.Header, "Today", Format.Signed(breakdown.Net)) };

        if (breakdown.Income.Count == 0 && breakdown.Expenses.Count == 0)
        {
            rows.Add(Row.Of(RowKind.Muted, "No money movements today yet"));
        }
        else
        {
            foreach (var line in breakdown.Income)
            {
                rows.Add(Row.Of(RowKind.Good, Format.Reason(line.Reason), Format.Signed(line.Amount)));
            }
            foreach (var line in breakdown.Expenses)
            {
                rows.Add(Row.Of(RowKind.Bad, Format.Reason(line.Reason), Format.Signed(line.Amount)));
            }
            rows.Add(Row.Of(RowKind.Normal, "Income", Format.Signed(breakdown.TotalIncome)));
            rows.Add(Row.Of(RowKind.Normal, "Expenses", Format.Signed(breakdown.TotalExpenses)));
        }

        if (park.VisitorsToday > 0)
        {
            rows.Add(Row.Of(RowKind.Normal, "Visitors today", Format.Count(park.VisitorsToday)));
            rows.Add(Row.Of(RowKind.Normal, "Income per visitor", Format.Money(breakdown.TotalIncome / park.VisitorsToday.Value)));
        }

        var earners = park.Rides
            .GroupBy(r => r.Name)
            .Select(g => (Name: g.Key, Count: g.Count(), Earned: g.Sum(r => r.Earned)))
            .Where(k => k.Earned > 0)
            .OrderByDescending(k => k.Earned)
            .Take(EarnersShown)
            .ToList();
        if (earners.Count > 0)
        {
            rows.Add(Row.Of(RowKind.Header, "Earned by attraction (uses x price)"));
            foreach (var earner in earners)
            {
                rows.Add(Row.Of(RowKind.Normal, KindLabel(earner.Name, earner.Count), Format.Money(earner.Earned)));
            }
        }

        rows.Add(Row.Of(RowKind.Header, "Park"));
        AddIfKnown(rows, "Balance", park.Money);
        if (park.TicketPrice is not null)
        {
            var price = Format.Money(park.TicketPrice);
            if (park.TargetTicketPrice is not null) price += $" (target {Format.Money(park.TargetTicketPrice)})";
            rows.Add(Row.Of(RowKind.Normal, "Ticket price", price));
        }
        AddIfKnown(rows, "Lifetime earned", park.TotalEarned);
        AddIfKnown(rows, "Lifetime spent", park.TotalSpent);
        if (park.StaffSalary > 0) AddIfKnown(rows, "Staff salaries per day", park.StaffSalary);
        if (park.StaffTax > 0) AddIfKnown(rows, "Staff tax", park.StaffTax);

        if (park.Loans.Count > 0)
        {
            rows.Add(Row.Of(RowKind.Header, "Loans"));
            foreach (var loan in park.Loans)
            {
                rows.Add(Row.Of(RowKind.Normal, loan.Label, Format.Money(loan.Remaining)));
            }
        }

        return rows;
    }

    public static IReadOnlyList<Row> Rides(ParkSnapshot park)
    {
        var rows = new List<Row>();

        if (park.Rides.Count == 0)
        {
            rows.Add(Row.Of(RowKind.Muted, "No attractions built yet"));
        }
        else
        {
            rows.Add(Row.Of(RowKind.Header, "Attraction", "State", "Uses", "Price", "Clean", "Durab.", "Wait"));

            // A park can hold dozens of the same lounger; one row per kind keeps the table readable.
            var kinds = park.Rides
                .GroupBy(r => r.Name)
                .Select(g => g.ToList())
                .OrderByDescending(g => g.Sum(r => r.UsesToday))
                .ThenBy(g => g[0].Name, StringComparer.Ordinal);
            foreach (var kind in kinds)
            {
                rows.Add(RideLine(kind));
            }

            var maintenance = park.Rides.Where(r => r.MaintenancePerDay is not null).ToList();
            if (maintenance.Count > 0)
            {
                rows.Add(Row.Of(RowKind.Normal, "Maintenance per day",
                    Format.Money(maintenance.Sum(r => r.MaintenancePerDay!.Value))));
            }
        }

        if (park.Capacity.Count > 0)
        {
            rows.Add(Row.Of(RowKind.Header, "Capacity in use"));
            foreach (var line in park.Capacity)
            {
                rows.Add(Row.Of(RowKind.Normal, line.Name, Format.Ratio(line.Current, line.Max)));
            }
        }

        return rows;
    }

    /// <summary>What to build: where demand is, what earns, the best of each type, what is locked.</summary>
    public static IReadOnlyList<Row> Build(ParkSnapshot park)
    {
        var rows = new List<Row>();

        // Whether a need is short of places is the first question before building for it.
        if (park.Rides.Any(r => r.Raises.Count > 0))
        {
            rows.Add(Row.Of(RowKind.Header, "Demand now", "In use", "Waiting", "Verdict"));
            foreach (var demand in Demand.ByNeed(park))
            {
                if (demand.Verdict == DemandVerdict.NoneBuilt)
                {
                    rows.Add(Row.Of(RowKind.Muted, demand.Need, Blank, Blank, Demand.Describe(demand.Verdict)));
                    continue;
                }
                var kind = demand.Verdict is DemandVerdict.Short or DemandVerdict.NearlyFull ? RowKind.Bad : RowKind.Normal;
                rows.Add(Row.Of(kind, demand.Need, $"{demand.UsersNow}/{demand.Capacity}", Format.Count(demand.Waiting), Demand.Describe(demand.Verdict)));
            }
        }

        var economics = AttractionEconomics.For(park);
        if (economics.Count > 0)
        {
            // A morning's takings cannot be set against a whole day's upkeep, so a net figure
            // appears only once a full day is on record.
            var period = economics.Any(k => k.FullDay) ? "yesterday" : "today so far";
            rows.Add(Row.Of(RowKind.Header, $"Each one, {period}", "Earned", "Upkeep", "Net", "Busy"));
            foreach (var kind in economics.Take(EconomicsShown))
            {
                var rowKind = kind.NetEach < 0 ? RowKind.Bad : kind.Busy && kind.NetEach > 0 ? RowKind.Good : RowKind.Normal;
                rows.Add(Row.Of(rowKind,
                    KindLabel(kind.Name, kind.Count),
                    Format.Money(kind.EarnedEach),
                    Format.Money(kind.UpkeepEach),
                    kind.NetEach is null ? Blank : Format.Signed(kind.NetEach),
                    BusyCell(kind)));
            }
        }

        var categories = park.BuildOptions.Select(o => o.Category).OfType<string>().Distinct().OrderBy(c => c, StringComparer.Ordinal);
        var best = new List<Row>();
        foreach (var category in categories)
        {
            var option = Demand.BestUnlocked(park.BuildOptions, o => o.Category == category);
            if (option is null) continue;
            var owned = park.Rides.Any(r => r.Category == category);
            best.Add(Row.Of(RowKind.Normal, $"{category}: {option.Name}{(owned ? "" : " (none built)")}"));
        }
        if (best.Count > 0)
        {
            rows.Add(Row.Of(RowKind.Header, "Best you can build"));
            rows.AddRange(best);
        }

        var upgrades = Demand.Upgrades(park);
        if (upgrades.Count > 0)
        {
            rows.Add(Row.Of(RowKind.Header, "Better versions unlocked"));
            foreach (var (owned, count, better) in upgrades)
            {
                // One line: both names in full, as a right-hand column would cut the new one short.
                rows.Add(Row.Of(RowKind.Normal, $"{(count > 1 ? $"{owned} x{count}" : owned)}: {better.Name}"));
            }
        }

        if (park.AttractionCapacity is not null || park.Prestige?.MaxVisitors is not null)
        {
            rows.Add(Row.Of(RowKind.Header, "Visitor limit"));
            AddIfKnown(rows, "Your attractions allow", park.AttractionCapacity);
            if (park.Prestige is { } prestige)
            {
                AddIfKnown(rows, $"Prestige {prestige.Level} caps it at", prestige.MaxVisitors);
                AddIfKnown(rows, $"Prestige {prestige.Level + 1} caps it at", prestige.NextLevelMaxVisitors);
            }
        }

        var locked = park.BuildOptions
            .Where(o => o.LockedBy is not null && o.Capacity > 0)
            .OrderBy(o => o.Tier).ThenBy(o => o.Price)
            .Take(BuildOptionsShown)
            .ToList();
        if (locked.Count > 0)
        {
            rows.Add(Row.Of(RowKind.Header, "Locked"));
            foreach (var option in locked)
            {
                rows.Add(Row.Of(RowKind.Normal, ShortName(option.Name), option.LockedBy!));
            }
        }

        if (rows.Count == 0) rows.Add(Row.Of(RowKind.Muted, "No building data available"));
        return rows;
    }

    public static IReadOnlyList<Row> Guests(ParkSnapshot park)
    {
        var rows = new List<Row>();

        if (park.Needs.Count > 0)
        {
            rows.Add(Row.Of(RowKind.Header, "Need", "Average", "Under 50%"));
            foreach (var need in park.Needs)
            {
                var low = need.GuestsCounted > 0 ? $"{need.GuestsLow}/{need.GuestsCounted}" : Blank;
                rows.Add(Row.Of(NeedKind(need.Average), need.Name, Format.Percent(need.Average), low));
            }
        }

        var counts = new List<Row>();
        if (park.Visitors is not null)
        {
            counts.Add(Row.Of(RowKind.Normal, "In park", park.MaxVisitors is null
                ? Format.Count(park.Visitors)
                : Format.Ratio(park.Visitors, park.MaxVisitors)));
        }
        if (park.ExpectedVisitors != park.MaxVisitors) AddIfKnown(counts, "Expected", park.ExpectedVisitors);
        // Only worth a line when guests can actually run short.
        if (park.AverageGuestCash < UnlimitedCashFrom) AddIfKnown(counts, "Average cash", park.AverageGuestCash);
        AddIfKnown(counts, "Refunded", park.RefundedVisitors);
        AddIfKnown(counts, "Injured", park.InjuredVisitors);
        if (counts.Count > 0)
        {
            rows.Add(Row.Of(RowKind.Header, "Guests"));
            rows.AddRange(counts);
        }

        var raised = park.Needs.Where(n => n.Sources.Count > 0).ToList();
        if (raised.Count > 0)
        {
            rows.Add(Row.Of(RowKind.Header, "Raised by"));
            foreach (var need in raised)
            {
                rows.Add(Row.Of(RowKind.Normal, $"{need.Name}: {string.Join(", ", need.Sources)}"));
            }
        }

        if (park.VisitorMultiplier is not null)
        {
            rows.Add(Row.Of(RowKind.Header, "What satisfaction buys"));
            if (park.RecentSatisfaction is not null)
            {
                rows.Add(Row.Of(RowKind.Normal, "Recent satisfaction", Format.Percent(park.RecentSatisfaction)));
            }
            if (park.VisitorMultiplier is { } multiplier)
            {
                var text = Format.Multiplier(multiplier);
                if (park.BestVisitorMultiplier is { } best && best > multiplier)
                {
                    text += $" ({Format.Multiplier(best)} at best)";
                }
                rows.Add(Row.Of(RowKind.Normal, "Visitor multiplier", text));
            }
        }

        AddCounts(rows, "Top complaints", park.Complaints, ComplaintsShown);
        AddCounts(rows, "Leaving because", park.LeavingReasons, int.MaxValue);

        if (rows.Count == 0)
        {
            rows.Add(Row.Of(RowKind.Muted, "No guest data available"));
        }

        return rows;
    }

    public static IReadOnlyList<Row> History(IReadOnlyList<DayRecord> days)
    {
        if (days.Count == 0)
        {
            return new[] { Row.Of(RowKind.Muted, "History starts after the first full day") };
        }

        var rows = new List<Row> { Row.Of(RowKind.Header, "Day", "Visitors", "Net", "Satisfaction") };
        foreach (var day in days.OrderByDescending(d => d.Day))
        {
            // The game reports zero until it has a figure, so zero means none was recorded.
            var satisfaction = day.Satisfaction > 0 ? day.Satisfaction : null;
            rows.Add(Row.Of(SignKind(day.Net),
                Format.Count(day.Day), Format.Count(day.Visitors), Format.Signed(day.Net), Format.Percent(satisfaction)));
        }
        return rows;
    }

    public static IReadOnlyList<Row> Advisor(IReadOnlyList<Advice> advice)
    {
        if (advice.Count == 0)
        {
            return new[] { Row.Of(RowKind.Good, NothingToFix) };
        }

        var rows = new List<Row>();
        foreach (var item in advice)
        {
            if (rows.Count > 0) rows.Add(Row.Of(RowKind.Normal, ""));
            rows.Add(Row.Of(TitleKind(item), item.Title));
            rows.Add(Row.Of(RowKind.Normal, item.Detail));
        }
        return rows;
    }

    /// <summary>One table row for all attractions of the same kind.</summary>
    private static Row RideLine(IReadOnlyList<RideRow> rides)
    {
        var count = rides.Count;
        var broken = rides.Count(r => r.IsBroken);
        var open = rides.Count(r => r.IsOpen);

        var requests = rides.Count(r => r.HasOpenRequest);

        var (kind, state) = broken > 0 ? (RowKind.Bad, count == 1 ? "Broken" : $"{broken} broken")
            : requests > 0 ? (RowKind.Bad, count == 1 ? "Request" : requests == 1 ? "1 request" : $"{requests} requests")
            : open == 0 ? (RowKind.Muted, "Closed")
            : open == count ? (RowKind.Normal, "Open")
            : (RowKind.Normal, $"{open}/{count} open");

        var name = KindLabel(rides[0].Name, count);

        var queues = rides.Where(r => r.QueueLength is not null).ToList();

        return Row.Of(kind,
            name,
            state,
            Format.Count(rides.Sum(r => r.UsesToday)),
            PriceCell(rides),
            // The worst of the group: that is the one needing attention.
            Format.Percent(rides.Min(r => r.Cleanliness)),
            Format.Percent(rides.Min(r => r.Durability)),
            queues.Count == 0 ? Blank : Format.Count(queues.Sum(r => r.QueueLength!.Value)));
    }

    private static string BusyCell(KindEconomics kind)
    {
        var waiting = kind.Waiting > 0 ? $"+{kind.Waiting}" : "";
        if (kind.UsersNow is null || kind.Capacity is null) return waiting.Length > 0 ? waiting : Blank;
        return waiting.Length > 0 ? $"{kind.UsersNow}/{kind.Capacity} {waiting}" : $"{kind.UsersNow}/{kind.Capacity}";
    }

    private static string KindLabel(string name, int count)
    {
        name = ShortName(name);
        return count > 1 ? $"{name} x{count}" : name;
    }

    private static string ShortName(string name) =>
        name.Length > LongestRideName ? name[..(LongestRideName - 2)] + ".." : name;

    private static string PriceCell(IReadOnlyList<RideRow> rides)
    {
        var priced = rides.Where(r => r.Price is not null).ToList();
        if (priced.Count == 0) return Blank;

        var lowest = priced.Min(r => r.Price);
        var highest = priced.Max(r => r.Price);
        if (lowest != highest) return $"{Format.Money(lowest)}-{Format.Money(highest)}";

        var ideal = priced[0].IdealPrice;
        return ideal is null ? Format.Money(lowest) : $"{Format.Money(lowest)}/{Format.Money(ideal)}";
    }

    private static void AddCounts(List<Row> rows, string header, IEnumerable<CountLine> lines, int limit)
    {
        var top = lines.Where(l => l.Count > 0).OrderByDescending(l => l.Count).Take(limit).ToList();
        if (top.Count == 0) return;

        rows.Add(Row.Of(RowKind.Header, header));
        foreach (var line in top)
        {
            rows.Add(Row.Of(RowKind.Normal, line.Label, Format.Count(line.Count)));
            if (!string.IsNullOrWhiteSpace(line.Note)) rows.Add(Row.Of(RowKind.Normal, line.Note));
        }
    }

    private static void AddIfKnown(List<Row> rows, string label, double? amount)
    {
        if (amount is not null) rows.Add(Row.Of(RowKind.Normal, label, Format.Money(amount)));
    }

    private static void AddIfKnown(List<Row> rows, string label, int? count)
    {
        if (count is not null) rows.Add(Row.Of(RowKind.Normal, label, Format.Count(count)));
    }

    private static RowKind SignKind(double amount) =>
        amount > 0 ? RowKind.Good : amount < 0 ? RowKind.Bad : RowKind.Normal;

    private static RowKind NeedKind(double? average) => average switch
    {
        null => RowKind.Muted,
        < NeedBadBelow => RowKind.Bad,
        >= NeedGoodFrom => RowKind.Good,
        _ => RowKind.Normal,
    };

    // The title carries the colour; the explanation stays in ordinary text, the easiest to read.
    private static RowKind TitleKind(Advice advice) =>
        advice.Severity == Severity.High ? RowKind.Bad : RowKind.Header;
}
