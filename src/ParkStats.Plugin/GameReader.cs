using System.Globalization;
using BepInEx.Logging;
using CayplayAI;
using ParkStats.Core;

namespace ParkStats.Plugin;

/// <summary>
/// Reads the running game into a <see cref="ParkSnapshot"/>. Every read is guarded: a value
/// that cannot be read is left empty and reported once, so one renamed member after a game
/// update does not take the rest of the panel down.
/// </summary>
internal sealed class GameReader
{
    private readonly ManualLogSource _log;
    private readonly HashSet<string> _reported = new();
    private Dictionary<string, int> _complaintBaseline;

    public GameReader(ManualLogSource log) => _log = log;

    /// <summary>Starts counting complaints from now; called when a day ends.</summary>
    public void ResetComplaintBaseline() => _complaintBaseline = null;

    /// <param name="raw">When given, receives unprocessed values for the diagnostics file.</param>
    public ParkSnapshot Read(List<string> raw = null)
    {
        var game = Ref("GameManager.Instance", () => GameManager.Instance);
        if (game == null) return null;

        // Several values only exist on the host; clients get "n/a" for them.
        var isHost = Get("IsServer", () => game.IsServer) ?? false;
        Note(raw, "IsServer", isHost);

        var finance = Ref("FinanceSystem", () => game.FinanceSystem);
        var attractions = Ref("AttractionManager", () => game.AttractionManager);
        var staff = Ref("StaffManager", () => game.StaffManager);

        var moneyToday = isHost && finance != null ? ReadTrackers(finance, raw) : Array.Empty<MoneyLine>();
        var guests = isHost ? ReadGuests(game, raw) : new GuestStats();

        var snapshot = new ParkSnapshot
        {
            Day = Get("CurrentDay", () => game.CurrentDay.Value),

            Money = finance == null ? null : Get("CurrentMoney", () => (double)finance.CurrentMoney.Value),
            TotalEarned = finance == null ? null : Get("TotalMoneyEarned", () => (double)finance.TotalMoneyEarned.Value),
            TotalSpent = finance == null ? null : Get("TotalMoneySpent", () => (double)finance.TotalMoneySpent.Value),
            TicketPrice = finance == null ? null : Get("TicketPrice", () => (double)finance.TicketPrice),
            MoneyToday = Normalize.Signs(moneyToday),
            Loans = finance == null ? Array.Empty<LoanLine>() : ReadLoans(finance),

            Visitors = Get("CurrentVisitorCount", () => game.CurrentVisitorCount),
            ExpectedVisitors = Get("ExpectedMaxVisitors", () => game.ExpectedMaxVisitors.Value),
            MaxVisitors = Get("MaxNumberOfVisitors", () => game.MaxNumberOfVisitors.Value),
            VisitorsToday = Get("TotalAmountOfVisitors", () => game.TotalAmountOfVisitors),
            RefundedVisitors = Get("TotalAmountOfRefundedVisitors", () => game.TotalAmountOfRefundedVisitors),
            InjuredVisitors = Get("TotalAmountOfInjuredVisitors", () => game.TotalAmountOfInjuredVisitors),
            Satisfaction = Normalize.Fraction(Get("CurrentSatisfaction", () => (double)game.CurrentSatisfaction.Value)),
            AverageGuestCash = guests.AverageCash,
            Needs = ReadNeeds(game, guests),
            Complaints = ReadComplaints(game, raw),
            LeavingReasons = guests.LeavingReasons,

            Rides = attractions == null ? Array.Empty<RideRow>() : ReadRides(attractions, raw),
            Capacity = attractions == null ? Array.Empty<CapacityLine>() : ReadCapacity(attractions),
            CleanlinessThreshold = Normalize.Fraction(Get("CleanlinessThreshold", () => (double)GameSettings.Attractions.CleanlinessThreshold)),
            DurabilityThreshold = Normalize.Fraction(Get("DurabilityThreshold", () => (double)GameSettings.Attractions.DurabilityThreshold)),

            StaffCount = staff == null ? null : Get("CurrentStaffCount", () => staff.CurrentStaffCount.Value),
            StaffCapacity = staff == null ? null : Get("CurrentMaxStaffCapacity", () => staff.CurrentMaxStaffCapacity.Value),
            StaffSalary = staff == null || !isHost ? null : Get("CalculateStaffSalary", () => (double)staff.CalculateStaffSalary()),
            StaffTax = staff == null || !isHost ? null : Get("CalculateStaffTax", () => (double)staff.CalculateStaffTax()),
        };

        if (raw != null) NoteUnverified(game, finance, attractions, raw);
        return snapshot;
    }

    /// <summary>File-safe name of the current park, used to keep one history per park.</summary>
    public string ParkKey()
    {
        var name = Ref("ParkName", () => GameManager.Instance?.ParkUpgradeSystem?.ParkNameNetVar?.Value.ToString());
        if (string.IsNullOrWhiteSpace(name)) return "default";

        var safe = new string(name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim();
        return safe.Length == 0 ? "default" : safe;
    }

    private IReadOnlyList<MoneyLine> ReadTrackers(FinanceSystem finance, List<string> raw)
    {
        var lines = new List<MoneyLine>();
        try
        {
            var trackers = finance.Trackers;
            if (trackers == null) return lines;
            for (var i = 0; i < trackers.Count; i++)
            {
                var tracker = trackers[i];
                lines.Add(new MoneyLine(tracker.Reason.ToString(), tracker.Amount));
                Note(raw, $"Trackers[{i}]", $"{tracker.Reason} {Number(tracker.Amount)}");
            }
        }
        catch (Exception e)
        {
            Report("FinanceSystem.Trackers", e);
        }
        return lines;
    }

    private IReadOnlyList<LoanLine> ReadLoans(FinanceSystem finance)
    {
        var loans = new List<LoanLine>();
        try
        {
            var active = finance.ActiveLoans;
            if (active == null) return loans;
            for (var i = 0; i < active.Count; i++)
            {
                var loan = active[i];
                loans.Add(new LoanLine($"Loan {loan.LoanID} ({loan.DaysLeftToRepay} days left)", loan.AmountOwed));
            }
        }
        catch (Exception e)
        {
            Report("FinanceSystem.ActiveLoans", e);
        }
        return loans;
    }

    private sealed class GuestStats
    {
        public int Counted;
        public double? AverageCash;
        public readonly Dictionary<string, int> Low = new();
        public IReadOnlyList<CountLine> LeavingReasons = Array.Empty<CountLine>();
    }

    private GuestStats ReadGuests(GameManager game, List<string> raw)
    {
        var stats = new GuestStats();
        try
        {
            var manager = game.AINetManager;
            var brains = manager?.AIBrains;
            if (manager == null || brains == null) return stats;

            var cash = 0.0;
            var leaving = new Dictionary<string, int>();
            for (var i = 0; i < brains.Count; i++)
            {
                var brain = brains[i];
                if (brain == null) continue;
                var data = brain.Data;
                if (data == null || !IsParkVisitor(brain, data)) continue;

                stats.Counted++;
                cash += data.Money;
                CountIfLow(stats, "Fun", data.FunNeed);
                CountIfLow(stats, "Hygiene", data.HygieneNeed);
                CountIfLow(stats, "Thirst", data.ThirstNeed);
                CountIfLow(stats, "Hunger", data.HungerNeed);
                CountIfLow(stats, "Toilet", data.ToiletNeed);
                CountIfLow(stats, "Trash", data.TrashNeed);
                var energy = data.EnergyNeed;
                if (energy != null && energy.Value < energy.SatisfactionThreshold) Add(stats.Low, "Energy");

                if (data.IsLeavingPark) Add(leaving, Format.Reason(data.LeavingReason.ToString()));

                if (raw != null && stats.Counted <= 3)
                {
                    var fun = data.FunNeed;
                    Note(raw, $"Guest[{stats.Counted}]",
                        $"money {Number(data.Money)} fun {Number(fun.Value)} (min {Number(fun.MinValue)} max {Number(fun.MaxValue)} " +
                        $"threshold {Number(fun.SatisfactionThreshold)}) toilet {Number(data.ToiletNeed.Value)} " +
                        $"(threshold {Number(data.ToiletNeed.SatisfactionThreshold)}) happiness {Number(data.Happiness.Value)}");
                }
            }

            if (stats.Counted > 0) stats.AverageCash = cash / stats.Counted;
            stats.LeavingReasons = leaving.Select(p => new CountLine(p.Key, p.Value)).ToList();
            Note(raw, "Guests counted", stats.Counted);
        }
        catch (Exception e)
        {
            Report("Guests", e);
        }
        return stats;
    }

    private static bool IsParkVisitor(AIBrain brain, AIDataStorage data)
    {
        // The game's own filter behind AINetManager.ParkVisitorList, when it can be reached.
        var gameFilter = AINetManager.__c.__9;
        if (gameFilter != null) return gameFilter._get_ParkVisitorList_b__10_0(brain);

        return !data.IsStaff && !data.IsNPC && !data.IsDormant && data.DoesHaveTicket;
    }

    private static void CountIfLow(GuestStats stats, string need, AINeed value)
    {
        if (value != null && value.Value < value.SatisfactionThreshold) Add(stats.Low, need);
    }

    private IReadOnlyList<NeedStat> ReadNeeds(GameManager game, GuestStats guests)
    {
        NeedStat Need(string name, Func<float> average) => new(
            name,
            Normalize.Fraction(Get(name + "Satisfaction", () => (double)average())),
            guests.Low.TryGetValue(name, out var low) ? low : 0,
            guests.Counted);

        return new[]
        {
            Need("Fun", () => game.FunSatisfaction.Value),
            Need("Energy", () => game.EnergySatisfaction.Value),
            Need("Hygiene", () => game.HygeineSatisfaction.Value),
            Need("Thirst", () => game.ThirstSatisfaction.Value),
            Need("Hunger", () => game.HungerSatisfaction.Value),
            Need("Toilet", () => game.ToiletSatisfaction.Value),
            Need("Trash", () => game.TrashSatisfaction.Value),
        };
    }

    private IReadOnlyList<CountLine> ReadComplaints(GameManager game, List<string> raw)
    {
        try
        {
            var thoughts = GameSettings.Common?.staticData?.Thoughts;
            var occurrences = game.TrackerSystem?.Data?.ThoughtsOccurance;
            if (thoughts == null || occurrences == null) return Array.Empty<CountLine>();

            // The game counts every thought; complaints are the ones that lower mood.
            var current = new Dictionary<string, int>();
            for (var i = 0; i < thoughts.Count; i++)
            {
                var thought = thoughts[i];
                if (thought == null || thought.MoodEffect >= 0) continue;
                if (occurrences.TryGetValue(thought.Type, out var count) && count > 0)
                {
                    current[Format.Reason(thought.Type.ToString())] = count;
                }
            }
            Note(raw, "Complaint totals", string.Join(", ", current.Select(p => $"{p.Key}={p.Value}")));

            _complaintBaseline ??= current;
            return Normalize.Since(_complaintBaseline, current);
        }
        catch (Exception e)
        {
            Report("Complaints", e);
            return Array.Empty<CountLine>();
        }
    }

    private IReadOnlyList<RideRow> ReadRides(AttractionManager manager, List<string> raw)
    {
        var rides = new List<RideRow>();
        try
        {
            var attractions = manager.ParkAttractions;
            if (attractions == null) return rides;
            for (var i = 0; i < attractions.Count; i++)
            {
                var attraction = attractions[i];
                if (attraction == null || Get("IsBuilt", () => attraction.IsBuilt) == false) continue;
                rides.Add(ReadRide(attraction, raw));
            }
        }
        catch (Exception e)
        {
            Report("AttractionManager.ParkAttractions", e);
        }
        return rides;
    }

    private RideRow ReadRide(AttractionInteraction attraction, List<string> raw)
    {
        var name = Ref("Building.GetName", () => attraction.Building?.GetName())
            ?? Ref("Attraction.name", () => attraction.name)
            ?? "?";
        name = name.Replace("(Clone)", "").Trim();

        var price = Get("CurrentPrice", () => (double)attraction.CurrentPrice);
        var ideal = Get("IdealPrice", () => (double)attraction.AttractionData.IdealPrice);
        var maintenance = Get("MaintenanceDailyPrice", () => (double)attraction.MaintenanceDailyPrice);
        var rawClean = Get("HasCleanness", () => attraction.HasCleanness) == true
            ? Get("CurrentCleannessLevel", () => (double)attraction.CurrentCleannessLevel.Value)
            : null;
        var rawDurability = Get("HasDurability", () => attraction.HasDurability) == true
            ? Get("CurrentDurabilityLevel", () => (double)attraction.CurrentDurabilityLevel.Value)
            : null;

        Note(raw, $"Ride {name}",
            $"price {Number(price)} ideal {Number(ideal)} clean {Number(rawClean)} durability {Number(rawDurability)} maintenance {Number(maintenance)}");

        return new RideRow
        {
            Name = name,
            IsOpen = Get("IsOpen", () => attraction.IsOpen) ?? true,
            IsBroken = Get("IsMalfunctioning", () => attraction.IsMalfunctioning.Value) ?? false,
            UsesToday = Get("AmountOfUsesToday", () => attraction.AmountOfUsesToday) ?? 0,
            // A price of zero means the attraction is free to use, not that it is underpriced.
            Price = price > 0 ? price : null,
            IdealPrice = ideal > 0 ? ideal : null,
            Cleanliness = Normalize.Fraction(rawClean),
            Durability = Normalize.Fraction(rawDurability),
            QueueLength = Get("HasQueue", () => attraction.HasQueue) == true
                ? Get("Queue.AIVisitorCount", () => attraction.Queue.AIVisitorCount)
                : null,
            MaintenancePerDay = maintenance > 0 ? maintenance : null,
        };
    }

    private IReadOnlyList<CapacityLine> ReadCapacity(AttractionManager manager)
    {
        var lines = new List<CapacityLine>();
        try
        {
            var info = manager.AttractionsInfo;
            if (info.MaxPoolsUsers > 0) lines.Add(new CapacityLine("Pools", info.CurrentPoolsUsers, info.MaxPoolsUsers));
            if (info.MaxSlidersUsers > 0) lines.Add(new CapacityLine("Slides", info.CurrentSlidersUsers, info.MaxSlidersUsers));
            if (info.MaxToiletsUsers > 0) lines.Add(new CapacityLine("Toilets", info.CurrentToiletsUsers, info.MaxToiletsUsers));
        }
        catch (Exception e)
        {
            Report("AttractionsInfo", e);
        }
        return lines;
    }

    // Values whose meaning has to be confirmed against the running game before the panel relies on them.
    private void NoteUnverified(GameManager game, FinanceSystem finance, AttractionManager attractions, List<string> raw)
    {
        if (finance != null)
        {
            Note(raw, "BaseTicketPrice", Get("BaseTicketPrice", () => finance.BaseTicketPrice));
            Note(raw, "TicketPriceModifier", Get("TicketPriceModifier", () => finance.TicketPriceModifier));
            Note(raw, "GetTargetTicketPrice()", Get("GetTargetTicketPrice", () => finance.GetTargetTicketPrice()));
            Note(raw, "DailyDecorTips", Get("DailyDecorTips", () => finance.DailyDecorTips));
            Note(raw, "DailyTipsTicketBase", Get("DailyTipsTicketBase", () => finance.DailyTipsTicketBase));
        }

        Note(raw, "ParkPrestige", Get("ParkPrestige", () => game.ParkPrestige.Value));
        Note(raw, "ParkPrestigeInt", Get("ParkPrestigeInt", () => game.ParkPrestigeInt));
        Note(raw, "CurrentBonusVisitorCount", Get("CurrentBonusVisitorCount", () => game.CurrentBonusVisitorCount));
        Note(raw, "CurrentBaseVisitorCount", Get("CurrentBaseVisitorCount", () => game.CurrentBaseVisitorCount));
        Note(raw, "RecentAverageSatisfaction", Get("RecentAverageSatisfaction", () => game.HistorySystem.RecentAverageSatisfaction.Value));
        if (attractions != null)
        {
            Note(raw, "ParkDecoration", Get("ParkDecoration", () => attractions.ParkDecoration.Value));
            Note(raw, "ParkDecorationLevel", Get("ParkDecorationLevel", () => attractions.ParkDecorationLevel.Value));
        }

        try
        {
            var levels = GameSettings.Park.PrestigeSettings;
            for (var i = 0; i < levels.Count; i++)
            {
                var level = levels[i];
                Note(raw, $"PrestigeSettings[{i}]",
                    $"level {Number(level.PrestigeLevel)} maxVisitors {level.MaxVisitors} decorationPointsNeeded {level.DecorationPointsNeeded}");
            }

            var range = GameSettings.Park.SatisfactionRange;
            Note(raw, "SatisfactionRange", $"{Number(range.x)}..{Number(range.y)}");
        }
        catch (Exception e)
        {
            Report("PrestigeSettings", e);
        }
    }

    private static void Add(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;

    private static void Note(List<string> raw, string name, object value) =>
        raw?.Add($"{name} = {(value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "n/a")}");

    private static string Number(double? value) =>
        value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "n/a";

    private T? Get<T>(string what, Func<T> read) where T : struct
    {
        try
        {
            return read();
        }
        catch (Exception e)
        {
            Report(what, e);
            return null;
        }
    }

    private T Ref<T>(string what, Func<T> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception e)
        {
            Report(what, e);
            return null;
        }
    }

    private void Report(string what, Exception e)
    {
        if (_reported.Add(what)) _log.LogWarning($"Could not read {what}: {e.GetType().Name}: {e.Message}");
    }
}
