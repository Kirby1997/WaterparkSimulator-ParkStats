using System.Globalization;
using BepInEx.Logging;
using CayplayAI;
using CayplayMissions;
using I2.Loc;
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
    private double? _capturedDaySatisfaction;
    private int? _capturedDay;

    public GameReader(ManualLogSource log)
    {
        _log = log;
        Complaints = new ComplaintLog(RideName);
    }

    /// <summary>Where complaints have come up today.</summary>
    public ComplaintLog Complaints { get; }

    /// <summary>Starts counting complaints from now; called when a day ends.</summary>
    public void ResetComplaintBaseline()
    {
        _complaintBaseline = null;
        Complaints.Reset();
    }

    /// <summary>
    /// Remembers the game's running satisfaction average for the day. Called just before the
    /// game finalises or clears it, since a day is recorded around the same moment.
    /// </summary>
    public void CaptureDaySatisfaction(string moment)
    {
        var game = Ref("GameManager.Instance", () => GameManager.Instance);
        if (game == null) return;

        var leavers = Get("dailySatisfactionCount", () => game.dailySatisfactionCount) ?? 0;
        var sum = Get("dailySatisfactionSum", () => game.dailySatisfactionSum) ?? 0;
        if (leavers <= 0) return;

        _capturedDaySatisfaction = Normalize.Fraction(sum / leavers);
        _capturedDay = Get("CurrentDay", () => game.CurrentDay.Value);
        _log.LogInfo($"Day {_capturedDay} satisfaction at {moment}: sum {Number(sum)} over {leavers} guests.");
    }

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
        var day = Get("CurrentDay", () => game.CurrentDay.Value);
        var visitors = Get("CurrentVisitorCount", () => game.CurrentVisitorCount);
        // With nobody in the park the game's satisfaction figures read zero, which says nothing.
        var hasGuests = visitors > 0;
        var needSources = attractions == null ? new Dictionary<string, IReadOnlyList<string>>() : ReadNeedSources(attractions, raw);
        var recent = Get("RecentAverageSatisfaction", () => game.HistorySystem.RecentAverageSatisfaction.Value);

        var snapshot = new ParkSnapshot
        {
            Day = day,

            Money = finance == null ? null : Get("CurrentMoney", () => (double)finance.CurrentMoney.Value),
            TotalEarned = finance == null ? null : Get("TotalMoneyEarned", () => (double)finance.TotalMoneyEarned.Value),
            TotalSpent = finance == null ? null : Get("TotalMoneySpent", () => (double)finance.TotalMoneySpent.Value),
            TicketPrice = finance == null ? null : Get("TicketPrice", () => (double)finance.TicketPrice),
            MoneyToday = Normalize.Signs(moneyToday),
            Loans = finance == null ? Array.Empty<LoanLine>() : ReadLoans(finance),

            Visitors = visitors,
            ExpectedVisitors = Get("ExpectedMaxVisitors", () => game.ExpectedMaxVisitors.Value),
            MaxVisitors = Get("MaxNumberOfVisitors", () => game.MaxNumberOfVisitors.Value),
            VisitorsToday = Get("TotalAmountOfVisitors", () => game.TotalAmountOfVisitors),
            RefundedVisitors = Get("TotalAmountOfRefundedVisitors", () => game.TotalAmountOfRefundedVisitors),
            InjuredVisitors = Get("TotalAmountOfInjuredVisitors", () => game.TotalAmountOfInjuredVisitors),
            Satisfaction = hasGuests
                ? Normalize.Fraction(Get("CurrentSatisfaction", () => (double)game.CurrentSatisfaction.Value))
                : null,
            DaySatisfaction = ReadDaySatisfaction(game, day),
            RecentSatisfaction = Normalize.Fraction(recent),
            // VisitorMultiplier is deliberately left empty. The game has a curve from satisfaction
            // to a multiplier (ParkSettings.GetSatisfactionPenaltyMultiplier), but where it applies
            // that multiplier is not established, so the panel makes no claim about it.
            AverageGuestCash = guests.AverageCash,
            Needs = ReadNeeds(game, guests, hasGuests, needSources),
            Prestige = ReadPrestige(game, attractions),
            NextStarTask = ReadNextStarTask(game, raw),
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
            StaffTaxPerExtra = staff == null || !isHost ? null : Get("CalculateStaffOverCap", () => (double)staff.CalculateStaffOverCap()),
        };

        if (raw != null) NoteUnverified(game, finance, attractions, raw);
        return snapshot;
    }

    /// <summary>The game's own end-of-day satisfaction figures, for the days it still remembers.</summary>
    public IReadOnlyList<(int Day, double Satisfaction)> DaySnapshots()
    {
        var days = new List<(int, double)>();
        try
        {
            var snapshots = GameManager.Instance?.HistorySystem?.Snapshots;
            if (snapshots == null) return days;
            for (var i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                if (Normalize.Fraction(snapshot.OverallSatisfaction) is { } satisfaction && satisfaction > 0)
                {
                    days.Add((snapshot.Day, satisfaction));
                }
            }
        }
        catch (Exception e)
        {
            Report("HistorySystem.Snapshots", e);
        }
        return days;
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
                if (energy != null && UnderHalf(energy.Value, energy.MinValue, energy.MaxValue)) Add(stats.Low, "Energy");

                if (data.IsLeavingPark) Add(leaving, Format.Reason(data.LeavingReason.ToString()));

                if (raw != null && stats.Counted <= 3)
                {
                    var fun = data.FunNeed;
                    Note(raw, $"Guest[{stats.Counted}]",
                        $"money {Number(data.Money)} fun {Number(fun.Value)} (min {Number(fun.MinValue)} max {Number(fun.MaxValue)} " +
                        $"threshold {Number(fun.SatisfactionThreshold)} low {Number(fun.LowReactionThreshold)}) toilet {Number(data.ToiletNeed.Value)} " +
                        $"(threshold {Number(data.ToiletNeed.SatisfactionThreshold)} low {Number(data.ToiletNeed.LowReactionThreshold)}) happiness {Number(data.Happiness.Value)}");
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

    // The game's own thresholds do not make a useful "low" mark: one sits near full (where a
    // guest stops seeking the need) and the other near empty. Half way is plain to read.
    private static void CountIfLow(GuestStats stats, string need, AINeed value)
    {
        if (value != null && UnderHalf(value.Value, value.MinValue, value.MaxValue)) Add(stats.Low, need);
    }

    private static bool UnderHalf(float value, float min, float max) =>
        max > min && (value - min) / (max - min) < 0.5f;

    private IReadOnlyList<NeedStat> ReadNeeds(GameManager game, GuestStats guests, bool hasGuests,
        Dictionary<string, IReadOnlyList<string>> sources)
    {
        NeedStat Need(string name, Func<float> average) => new(
            name,
            hasGuests ? Normalize.Fraction(Get(name + "Satisfaction", () => (double)average())) : null,
            guests.Low.TryGetValue(name, out var low) ? low : 0,
            guests.Counted)
        {
            Sources = sources.TryGetValue(name, out var raisedBy) ? raisedBy : Array.Empty<string>(),
        };

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

    /// <summary>
    /// Which built attractions raise which need, from the stat changes the game applies when a
    /// guest uses them. Only positive claims come out of this: an attraction that works some
    /// other way (handing over an item, say) is simply not listed.
    /// </summary>
    private Dictionary<string, IReadOnlyList<string>> ReadNeedSources(AttractionManager manager, List<string> raw)
    {
        const int shown = 4;
        var byNeed = new Dictionary<string, Dictionary<string, int>>();
        try
        {
            var attractions = manager.ParkAttractions;
            for (var i = 0; attractions != null && i < attractions.Count; i++)
            {
                var attraction = attractions[i];
                if (attraction == null || !attraction.IsBuilt) continue;
                var changes = attraction.AttractionData?.StatChanges;
                if (changes == null) continue;

                var name = RideName(attraction);
                for (var j = 0; j < changes.Length; j++)
                {
                    var change = changes[j];
                    if (change == null) continue;
                    Note(raw, $"StatChange {name}", $"{change.Target} {Number(change.Value)}");
                    var need = NeedName(change.Target);
                    if (need == null || change.Value <= 0) continue;

                    if (!byNeed.TryGetValue(need, out var names)) byNeed[need] = names = new Dictionary<string, int>();
                    Add(names, name);
                }
            }
        }
        catch (Exception e)
        {
            Report("StatChanges", e);
        }

        var result = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var (need, names) in byNeed)
        {
            var labels = names
                .OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => p.Value > 1 ? $"{p.Key} x{p.Value}" : p.Key)
                .ToList();
            if (labels.Count > shown)
            {
                var more = labels.Count - shown;
                labels = labels.Take(shown).Append($"{more} more").ToList();
            }
            result[need] = labels;
        }
        return result;
    }

    private static string NeedName(EStat stat) => stat switch
    {
        EStat.Fun => "Fun",
        EStat.Energy => "Energy",
        EStat.Hygiene => "Hygiene",
        EStat.Thirst => "Thirst",
        EStat.Hunger => "Hunger",
        EStat.Toilet => "Toilet",
        EStat.Trash => "Trash",
        _ => null,
    };

    /// <summary>
    /// The game's own wording of what the next star needs. Stars are handed out by a mission,
    /// so this is the current task of the active mission that goes on to raise prestige.
    /// </summary>
    private string ReadNextStarTask(GameManager game, List<string> raw)
    {
        try
        {
            var missions = game.MissionManager?.ActiveMissions;
            if (missions == null) return null;

            var prestige = game.ParkPrestige.Value;
            string task = null;
            for (var i = 0; i < missions.Count; i++)
            {
                var mission = missions[i];
                if (mission == null) continue;

                var raises = RaisesPrestige(mission, prestige);
                var text = Ref("Mission.GetTaskText", () => mission.GetTaskText());
                text = string.IsNullOrWhiteSpace(text) ? null : string.Join("; ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                Note(raw, $"Mission[{i}]", $"{mission.MissionSO?.MissionID} stage {mission.CurrentStage?.StageIndex} raises prestige {raises} task \"{text}\"");
                if (raw != null) NoteConditions(mission, raw);

                if (raises && task == null) task = RemainingConditions(mission) ?? text;
            }
            return task;
        }
        catch (Exception e)
        {
            Report("Missions", e);
            return null;
        }
    }

    private static bool RaisesPrestige(MissionData mission, float prestige)
    {
        var stages = mission.Stages;
        for (var i = 0; stages != null && i < stages.Count; i++)
        {
            var actions = stages[i]?.Actions;
            for (var j = 0; actions != null && j < actions.Count; j++)
            {
                var setPrestige = actions[j]?.TryCast<SetParkPrestige_MissionAction>();
                if (setPrestige != null && setPrestige.ParkPrestige > prestige) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// What is left of a mission's current stage, e.g. "1 of 4 tasks left: Earn money:
    /// $489787/500000". Nothing when the conditions cannot be read.
    /// </summary>
    private string RemainingConditions(MissionData mission)
    {
        try
        {
            var all = new List<string>();
            var left = new List<string>();
            var transitions = mission.CurrentStage?.Transitions;
            for (var i = 0; transitions != null && i < transitions.Count; i++)
            {
                var conditions = transitions[i]?.Conditions;
                for (var j = 0; conditions != null && j < conditions.Count; j++)
                {
                    var condition = conditions[j];
                    var text = condition?.GetTaskText()?.Trim();
                    if (string.IsNullOrEmpty(text) || all.Contains(text)) continue;

                    all.Add(text);
                    if (!condition._hasPassed) left.Add(text);
                }
            }

            if (all.Count == 0) return null;
            return left.Count == 0
                ? "every task is done."
                : $"{left.Count} of {all.Count} tasks left: {string.Join("; ", left)}";
        }
        catch (Exception e)
        {
            Report("Mission conditions", e);
            return null;
        }
    }

    private void NoteConditions(MissionData mission, List<string> raw)
    {
        try
        {
            var transitions = mission.CurrentStage?.Transitions;
            for (var i = 0; transitions != null && i < transitions.Count; i++)
            {
                var conditions = transitions[i]?.Conditions;
                for (var j = 0; conditions != null && j < conditions.Count; j++)
                {
                    var condition = conditions[j];
                    if (condition == null) continue;
                    Note(raw, "  condition", $"{condition.GetIl2CppType().Name} counter {Number(condition.Counter)} passed {condition._hasPassed} text \"{condition.GetTaskText()}\"");
                }
            }
        }
        catch (Exception e)
        {
            Note(raw, "  conditions", $"<{e.GetType().Name}: {e.Message}>");
        }
    }

    /// <summary>
    /// The game's satisfaction figure for a whole day: its end-of-day snapshot when that exists,
    /// otherwise its running average of the guests who have left so far, otherwise that average
    /// as it stood before the game cleared it.
    /// </summary>
    private double? ReadDaySatisfaction(GameManager game, int? day)
    {
        try
        {
            var snapshots = game.HistorySystem?.Snapshots;
            if (snapshots != null && day != null)
            {
                for (var i = 0; i < snapshots.Count; i++)
                {
                    var snapshot = snapshots[i];
                    if (snapshot.Day == day) return Normalize.Fraction(snapshot.OverallSatisfaction);
                }
            }

            var leavers = game.dailySatisfactionCount;
            if (leavers > 0) return Normalize.Fraction(game.dailySatisfactionSum / leavers);

            return _capturedDay == day ? _capturedDaySatisfaction : null;
        }
        catch (Exception e)
        {
            Report("DaySatisfaction", e);
            return null;
        }
    }

    private PrestigeInfo ReadPrestige(GameManager game, AttractionManager attractions)
    {
        try
        {
            var level = game.ParkPrestigeInt;
            int? allowed = null, nextAllowed = null;
            double? decorationNeeded = null;

            var levels = GameSettings.Park.PrestigeSettings;
            for (var i = 0; i < levels.Count; i++)
            {
                var setting = levels[i];
                var settingLevel = (int)setting.PrestigeLevel;
                if (settingLevel == level)
                {
                    allowed = setting.MaxVisitors;
                    decorationNeeded = setting.DecorationPointsNeeded;
                }
                else if (settingLevel == level + 1)
                {
                    nextAllowed = setting.MaxVisitors;
                }
            }

            double? decoration = null;
            if (attractions != null && decorationNeeded > 0)
            {
                decoration = Math.Clamp(attractions.ParkDecoration.Value / decorationNeeded.Value, 0, 1);
            }

            return new PrestigeInfo
            {
                Level = level,
                MaxVisitors = allowed,
                NextLevelMaxVisitors = nextAllowed,
                DecorationLevel = decoration,
            };
        }
        catch (Exception e)
        {
            Report("Prestige", e);
            return null;
        }
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
            var thoughtNames = new Dictionary<string, string>();
            for (var i = 0; i < thoughts.Count; i++)
            {
                var thought = thoughts[i];
                if (thought == null || thought.MoodEffect >= 0) continue;

                var label = Format.Reason(thought.Type.ToString());
                thoughtNames[label] = thought.Type.ToString();
                if (raw != null)
                {
                    Note(raw, $"Thought {thought.Type}",
                        $"mood {Number(thought.MoodEffect)} name \"{Translate(thought.LocalizedName)}\" description \"{Translate(thought.LocalizedDescription)}\"");
                }

                if (occurrences.TryGetValue(thought.Type, out var count) && count > 0) current[label] = count;
            }
            Note(raw, "Complaint totals", string.Join(", ", current.Select(p => $"{p.Key}={p.Value}")));

            _complaintBaseline ??= current;
            // The game's own text for a thought is only what the guest says. What lies behind
            // it, and where it has been happening, is what the player can act on.
            return Normalize.Since(_complaintBaseline, current)
                .Select(line => thoughtNames.TryGetValue(line.Label, out var thought)
                    ? line with { Note = ComplaintHints.Note(thought, Complaints.Places(thought)) }
                    : line)
                .ToList();
        }
        catch (Exception e)
        {
            Report("Complaints", e);
            return Array.Empty<CountLine>();
        }
    }

    private static string Translate(LocalizedString text)
    {
        try
        {
            var term = text?.mTerm;
            if (string.IsNullOrEmpty(term)) return null;
            var translation = LocalizationManager.GetTranslation(term);
            return string.IsNullOrWhiteSpace(translation) ? null : translation.Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string RideName(AttractionInteraction attraction)
    {
        var name = Ref("Building.GetName", () => attraction.Building?.GetName())
            ?? Ref("Attraction.name", () => attraction.name)
            ?? "?";
        return name.Replace("(Clone)", "").Trim();
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
        var name = RideName(attraction);

        var price = Get("CurrentPrice", () => (double)attraction.CurrentPrice);
        var ideal = Get("IdealPrice", () => (double)attraction.AttractionData.IdealPrice);
        var maintenance = Get("MaintenanceDailyPrice", () => (double)attraction.MaintenanceDailyPrice);
        var rawClean = Get("HasCleanness", () => attraction.HasCleanness) == true
            ? Get("CurrentCleannessLevel", () => (double)attraction.CurrentCleannessLevel.Value)
            : null;
        var rawDurability = Get("HasDurability", () => attraction.HasDurability) == true
            ? Get("CurrentDurabilityLevel", () => (double)attraction.CurrentDurabilityLevel.Value)
            : null;

        // Stands and vending machines sell from a stock that runs out.
        var shop = Ref("ShopInteraction", () => attraction.TryCast<ShopInteraction>());
        var stockLeft = shop == null ? null : Get("CurrentStockCount", () => shop.CurrentStockCount);
        var stockCapacity = shop == null ? null : Get("StockCapacity", () => shop.StockCapacity);

        if (raw != null)
        {
            Note(raw, $"Ride {name}",
                $"price {Number(price)} ideal {Number(ideal)} clean {Number(rawClean)} durability {Number(rawDurability)} maintenance {Number(maintenance)} " +
                $"request {HasOpenRequest(attraction)} stock {stockLeft}/{stockCapacity} visitorBonus {Get("VisitorBonus", () => attraction.AttractionData.VisitorBonus)} " +
                $"maxUsers {Get("MaxSimultaneousUsers", () => attraction.AttractionData.MaxSimultaneousUsers)} queueFull {Get("IsQueueFull", () => attraction.IsQueueFull)}");
        }

        return new RideRow
        {
            Name = name,
            IsOpen = Get("IsOpen", () => attraction.IsOpen) ?? true,
            IsBroken = Get("IsMalfunctioning", () => attraction.IsMalfunctioning.Value) ?? false,
            UsesToday = Get("AmountOfUsesToday", () => attraction.AmountOfUsesToday) ?? 0,
            // Free with nothing to charge is just free. Free where the game has an ideal price
            // is money left on the table, so that zero is kept.
            Price = price > 0 ? price : ideal > 0 ? 0 : null,
            IdealPrice = ideal > 0 ? ideal : null,
            Cleanliness = Normalize.Fraction(rawClean),
            Durability = Normalize.Fraction(rawDurability),
            QueueLength = Get("HasQueue", () => attraction.HasQueue) == true
                ? Get("Queue.AIVisitorCount", () => attraction.Queue.AIVisitorCount)
                : null,
            MaintenancePerDay = maintenance > 0 ? maintenance : null,
            StockLeft = stockLeft,
            StockCapacity = stockCapacity,
            HasOpenRequest = HasOpenRequest(attraction),
        };
    }

    /// <summary>
    /// Saunas and hot tubs take requests: a guest inside asks for a temperature and a humidity
    /// or pressure, and the player sets them on the control panel. A guest who leaves without
    /// them is the one who complains of bad service.
    /// </summary>
    private bool HasOpenRequest(AttractionInteraction attraction)
    {
        try
        {
            var sauna = attraction.TryCast<SaunaInteraction>();
            if (sauna != null) return sauna.HasActiveRequest.Value && !sauna.ActiveRequestFulfilled.Value;

            var hotTub = attraction.TryCast<HotTubInteraction>();
            if (hotTub != null) return hotTub.haveRequest.Value && !hotTub.AreSettingsCorrect();
        }
        catch (Exception e)
        {
            Report("Guest requests", e);
        }
        return false;
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

        Note(raw, "CurrentSatisfaction", Get("CurrentSatisfaction", () => game.CurrentSatisfaction.Value));
        Note(raw, "dailySatisfactionSum", Get("dailySatisfactionSum", () => game.dailySatisfactionSum));
        Note(raw, "dailySatisfactionCount", Get("dailySatisfactionCount", () => game.dailySatisfactionCount));
        Note(raw, "TotalAmountOfVisitors", Get("TotalAmountOfVisitors", () => game.TotalAmountOfVisitors));
        Note(raw, "CleanlinessThreshold", Get("CleanlinessThreshold", () => GameSettings.Attractions.CleanlinessThreshold));
        Note(raw, "CleanlinessStaffPreventionThreshold", Get("CleanlinessStaffPreventionThreshold", () => GameSettings.Attractions.CleanlinessStaffPreventionThreshold));
        Note(raw, "DurabilityThreshold", Get("DurabilityThreshold", () => GameSettings.Attractions.DurabilityThreshold));
        Note(raw, "DurabilityStaffPreventionThreshold", Get("DurabilityStaffPreventionThreshold", () => GameSettings.Attractions.DurabilityStaffPreventionThreshold));
        NoteIncomeLevers(game, finance, attractions, raw);
        Note(raw, "ParkPrestige", Get("ParkPrestige", () => game.ParkPrestige.Value));
        var staff = game.StaffManager;
        if (staff != null)
        {
            Note(raw, "CalculateStaffSalary()", Get("CalculateStaffSalary", () => staff.CalculateStaffSalary()));
            Note(raw, "CalculateStaffTax()", Get("CalculateStaffTax", () => staff.CalculateStaffTax()));
            Note(raw, "CalculateStaffOverCap()", Get("CalculateStaffOverCap", () => staff.CalculateStaffOverCap()));
            Note(raw, "CalculateStaffOvertimePay()", Get("CalculateStaffOvertimePay", () => staff.CalculateStaffOvertimePay()));
            Note(raw, "StaffTaxPercentPerOverhire", Get("StaffTaxPercentPerOverhire", () => GameSettings.Staff.StaffTaxPercentPerOverhire));
        }
        for (var satisfaction = 0.2f; satisfaction <= 1.001f; satisfaction += 0.1f)
        {
            var level = satisfaction;
            Note(raw, $"GetSatisfactionPenaltyMultiplier({Number(level)})", Get("GetSatisfactionPenaltyMultiplier sample", () => GameSettings.Park.GetSatisfactionPenaltyMultiplier(level)));
        }
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

            var snapshots = game.HistorySystem.Snapshots;
            for (var i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                Note(raw, $"HistorySnapshot[{i}]", $"day {snapshot.Day} overall {Number(snapshot.OverallSatisfaction)} fun {Number(snapshot.FunSatisfaction)}");
            }

            var range = GameSettings.Park.SatisfactionRange;
            Note(raw, "SatisfactionRange", $"{Number(range.x)}..{Number(range.y)}");
        }
        catch (Exception e)
        {
            Report("PrestigeSettings", e);
        }
    }

    // What tips, the ticket price and the visitor cap are made of. Logged so that advice on
    // them can be written from real numbers rather than from member names.
    private void NoteIncomeLevers(GameManager game, FinanceSystem finance, AttractionManager attractions, List<string> raw)
    {
        try
        {
            var ticket = GameSettings.Ticket;
            Note(raw, "Ticket.MaxTipsPercentage", ticket.MaxTipsPercentage);
            Note(raw, "Ticket.MaxDecorTipsPercentage", ticket.MaxDecorTipsPercentage);
            Note(raw, "Ticket.PoolTicketPriceBonus", ticket.PoolTicketPriceBonus);
            Note(raw, "Ticket.SlideTicketPriceBonus", ticket.SlideTicketPriceBonus);
            Note(raw, "Ticket.SlidePassBonus", ticket.SlidePassBonus);
            Note(raw, "Ticket.SeniorDiscount", ticket.SeniorDiscount);
            Note(raw, "Ticket.TeenDiscount", ticket.TeenDiscount);
            var cash = ticket.VisitorMoneyBonus;
            Note(raw, "Ticket.VisitorMoneyBonus", $"{Number(cash.x)}..{Number(cash.y)}");
            for (var happiness = 0f; happiness <= 1.001f; happiness += 0.25f)
            {
                var level = happiness;
                Note(raw, $"Ticket.GetTipsMultiplier({Number(level)})", Get("GetTipsMultiplier", () => ticket.GetTipsMultiplier(level)));
            }

            var prestige = ticket.GetPrestigeSettings(game.ParkPrestigeInt);
            Note(raw, "TicketPrestigeSettings.BaseTicketPrice", prestige?.BaseTicketPrice);
            Note(raw, "Finance.PoolPricePerTier", GameSettings.Finance.PoolPricePerTier);
            Note(raw, "Finance.SlidePricePerTier", GameSettings.Finance.SlidePricePerTier);
            Note(raw, "Finance.UpgradeATMPrice", GameSettings.Finance.UpgradeATMPrice);
            Note(raw, "TicketMachinesBought", Get("TicketMachinesBought", () => game.ParkUpgradeSystem.TicketMachinesBought));
            if (finance != null) Note(raw, "GetTipsTechMultiplier()", Get("GetTipsTechMultiplier", () => finance.GetTipsTechMultiplier()));

            var bonus = 0;
            var rides = attractions?.ParkAttractions;
            for (var i = 0; rides != null && i < rides.Count; i++)
            {
                var ride = rides[i];
                if (ride != null && ride.IsBuilt) bonus += ride.AttractionData.VisitorBonus;
            }
            Note(raw, "Sum of attraction VisitorBonus", bonus);
            Note(raw, "Park.minVisitors", GameSettings.Park.minVisitors);
            Note(raw, "Park.GetVisitorLimit(sum)", Get("GetVisitorLimit", () => GameSettings.Park.GetVisitorLimit(bonus)));
        }
        catch (Exception e)
        {
            Report("Income levers", e);
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
