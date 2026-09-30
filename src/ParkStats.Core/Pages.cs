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
            if (park.ExpectedVisitors is { } expected) visitors += $" (expected {expected})";
            rows.Add(Row.Of(RowKind.Normal, "Visitors", visitors));
        }

        if (park.Satisfaction is not null)
        {
            rows.Add(Row.Of(RowKind.Normal, "Satisfaction", Format.Percent(park.Satisfaction)));
        }

        if (park.Prestige is { } prestige)
        {
            var progress = prestige.NextLevelPoints is { } needed
                ? $"decor {Format.Money(prestige.DecorationPoints)}/{Format.Money(needed)}"
                : "max";
            rows.Add(Row.Of(RowKind.Normal, "Prestige", $"{prestige.Level} ({progress})"));
        }

        rows.Add(Row.Of(RowKind.Header, "Do next"));
        if (advice.Count == 0)
        {
            rows.Add(Row.Of(RowKind.Good, NothingToFix));
        }
        foreach (var item in advice.Take(OverviewAdviceShown))
        {
            rows.Add(Row.Of(TitleKind(item), item.Title));
        }

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
        AddIfKnown(rows, "Staff salaries per day", park.StaffSalary);
        AddIfKnown(rows, "Staff tax", park.StaffTax);

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
            rows.Add(Row.Of(RowKind.Header, "Attraction", "State", "Uses", "Price", "Clean", "Durab.", "Queue"));
            foreach (var ride in park.Rides.OrderByDescending(r => r.UsesToday).ThenBy(r => r.Name, StringComparer.Ordinal))
            {
                rows.Add(RideLine(ride));
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

    public static IReadOnlyList<Row> Guests(ParkSnapshot park)
    {
        var rows = new List<Row>();

        if (park.Needs.Count > 0)
        {
            rows.Add(Row.Of(RowKind.Header, "Need", "Average", "Guests low"));
            foreach (var need in park.Needs)
            {
                var low = need.GuestsCounted > 0 ? $"{need.GuestsBelowThreshold}/{need.GuestsCounted}" : Blank;
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
        AddIfKnown(counts, "Expected", park.ExpectedVisitors);
        AddIfKnown(counts, "Average cash", park.AverageGuestCash);
        AddIfKnown(counts, "Refunded", park.RefundedVisitors);
        AddIfKnown(counts, "Injured", park.InjuredVisitors);
        if (counts.Count > 0)
        {
            rows.Add(Row.Of(RowKind.Header, "Guests"));
            rows.AddRange(counts);
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
            rows.Add(Row.Of(SignKind(day.Net),
                Format.Count(day.Day), Format.Count(day.Visitors), Format.Signed(day.Net), Format.Percent(day.Satisfaction)));
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
            rows.Add(Row.Of(TitleKind(item), item.Title));
            rows.Add(Row.Of(RowKind.Muted, item.Detail));
        }
        return rows;
    }

    private static Row RideLine(RideRow ride)
    {
        var (kind, state) = ride.IsBroken ? (RowKind.Bad, "Broken")
            : !ride.IsOpen ? (RowKind.Muted, "Closed")
            : (RowKind.Normal, "Open");

        var price = ride.Price is null ? Blank
            : ride.IdealPrice is null ? Format.Money(ride.Price)
            : $"{Format.Money(ride.Price)}/{Format.Money(ride.IdealPrice)}";

        return Row.Of(kind,
            ride.Name,
            state,
            Format.Count(ride.UsesToday),
            price,
            Format.Percent(ride.Cleanliness),
            Format.Percent(ride.Durability),
            ride.QueueLength is null ? Blank : Format.Count(ride.QueueLength));
    }

    private static void AddCounts(List<Row> rows, string header, IEnumerable<CountLine> lines, int limit)
    {
        var top = lines.Where(l => l.Count > 0).OrderByDescending(l => l.Count).Take(limit).ToList();
        if (top.Count == 0) return;

        rows.Add(Row.Of(RowKind.Header, header));
        foreach (var line in top)
        {
            rows.Add(Row.Of(RowKind.Normal, line.Label, Format.Count(line.Count)));
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

    private static RowKind TitleKind(Advice advice) =>
        advice.Severity == Severity.High ? RowKind.Bad : RowKind.Normal;
}
