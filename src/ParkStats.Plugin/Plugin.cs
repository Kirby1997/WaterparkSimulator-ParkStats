using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using ParkStats.Core;

namespace ParkStats.Plugin;

[BepInPlugin(Id, Name, Version)]
public sealed class Plugin : BasePlugin
{
    public const string Id = "com.github.kirby1997.parkstats";
    public const string Name = "ParkStats";
    public const string Version = "0.1.0";

    private readonly HashSet<string> _reported = new();
    private GameReader _reader;
    private HistoryStore _history;
    private string _historyKey;
    private int _historyDays;
    private bool _diagnosticsLogged;

    internal static Plugin Instance { get; private set; }

    internal TabletPanel Panel { get; private set; }

    private static string DataDirectory => Path.Combine(Paths.ConfigPath, Name);

    public override void Load()
    {
        Instance = this;

        var enabled = Config.Bind("General", "Enabled", true, "Add the stats tabs to the tablet's Park Management page.");
        var fontScale = Config.Bind("General", "FontScale", 1f, "Multiplier for the text size in the stats tabs.");
        var historyDays = Config.Bind("General", "HistoryDays", 60, "How many days the History tab keeps.");
        var diagnostics = Config.Bind("Debug", "WriteDiagnostics", false,
            "Write diagnostics.txt and method-addresses.txt to BepInEx/config/ParkStats when the Park Management " +
            "page is opened. They list what the mod reads from the game and the page layout, for fixing the mod " +
            "after game updates. Turn this on before reporting a problem.");

        if (!enabled.Value)
        {
            Log.LogInfo($"{Name} is disabled in its config.");
            return;
        }

        _historyDays = historyDays.Value;
        _reader = new GameReader(Log);
        Panel = new TabletPanel(Log, _reader, History, fontScale.Value, diagnostics.Value ? WriteDiagnostics : null);

        var harmony = new Harmony(Id);
        harmony.PatchAll(typeof(Hooks));
        Hooks.PatchPageToggle(harmony, Log);
        Log.LogInfo($"{Name} {Version} loaded");
    }

    /// <summary>Stores today's result; called just before the game clears the day's money trackers.</summary>
    internal void RecordDay()
    {
        var park = _reader.Read();
        // The trackers are also cleared when a save loads, with nothing in them worth recording.
        if (park == null || park.MoneyToday.Count == 0) return;

        var record = DayRecord.From(park);
        History()?.Add(record);
        _reader.ResetComplaintBaseline();
        Log.LogInfo($"Recorded day {record.Day}: income {Format.Money(record.Income)}, expenses {Format.Money(record.Expenses)}, " +
                    $"satisfaction {Format.Percent(record.Satisfaction)}.");
    }

    internal void RecordThought(VisitorThoughtsSystem system, VisitorThoughtType type) => _reader.Complaints.Record(system, type);

    internal void CaptureDaySatisfaction(string moment) => _reader.CaptureDaySatisfaction(moment);

    internal void ReportOnce(string what, Exception e)
    {
        if (_reported.Add(what)) Log.LogError($"{what} failed: {e}");
    }

    /// <summary>The history of the park currently loaded; one file per park name.</summary>
    private HistoryStore History()
    {
        try
        {
            var key = _reader.ParkKey();
            if (_history == null || key != _historyKey)
            {
                _history = new HistoryStore(Path.Combine(DataDirectory, $"history-{key}.json"), _historyDays);
                _historyKey = key;
            }
            return _history;
        }
        catch (Exception e)
        {
            ReportOnce("history file", e);
            return null;
        }
    }

    private void WriteDiagnostics(TabletUI tablet, IReadOnlyList<string> panelLines)
    {
        try
        {
            var path = Path.Combine(DataDirectory, "diagnostics.txt");
            Diagnostics.Write(path, tablet, _reader, Panel, panelLines);
            // The file is rewritten on every tab click; saying so once is enough.
            if (!_diagnosticsLogged)
            {
                Log.LogInfo($"Diagnostics written to {path}");
                MethodAddresses.Write(Path.Combine(DataDirectory, "method-addresses.txt"));
            }
            _diagnosticsLogged = true;
        }
        catch (Exception e)
        {
            ReportOnce("diagnostics", e);
        }
    }
}
