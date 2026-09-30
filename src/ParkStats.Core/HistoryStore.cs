using System.Text.Json;
using System.Text.Json.Serialization;

namespace ParkStats.Core;

public sealed record DayRecord
{
    public int Day { get; init; }
    public int? Visitors { get; init; }
    public double Income { get; init; }
    public double Expenses { get; init; }
    public double? Satisfaction { get; init; }
    public Dictionary<string, double> Needs { get; init; } = new();

    [JsonIgnore]
    public double Net => Income + Expenses;

    public static DayRecord From(ParkSnapshot park)
    {
        var money = MoneyBreakdown.From(park.MoneyToday);
        return new DayRecord
        {
            Day = park.Day ?? 0,
            Visitors = park.VisitorsToday,
            Income = money.TotalIncome,
            Expenses = money.TotalExpenses,
            Satisfaction = park.Satisfaction,
            Needs = park.Needs
                .Where(n => n.Average is not null)
                .ToDictionary(n => n.Name, n => n.Average!.Value),
        };
    }
}

/// <summary>Per-day park results kept in a JSON file, oldest first.</summary>
public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly int _maxDays;
    private List<DayRecord> _records;

    public HistoryStore(string path, int maxDays = 60)
    {
        _path = path;
        _maxDays = maxDays;
        _records = Load(path);
    }

    public IReadOnlyList<DayRecord> Records => _records;

    public void Add(DayRecord record)
    {
        // A day at or after the new one belongs to a timeline the player has reloaded away from.
        _records.RemoveAll(r => r.Day >= record.Day);
        _records.Add(record);
        if (_records.Count > _maxDays)
        {
            _records.RemoveRange(0, _records.Count - _maxDays);
        }
        Save();
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(_records, JsonOptions));
    }

    private static List<DayRecord> Load(string path)
    {
        if (!File.Exists(path)) return new List<DayRecord>();
        try
        {
            var records = JsonSerializer.Deserialize<List<DayRecord>>(File.ReadAllText(path), JsonOptions);
            return records?.OrderBy(r => r.Day).ToList() ?? new List<DayRecord>();
        }
        catch (JsonException)
        {
            // An unreadable history is not worth failing over; it is rewritten on the next day.
            return new List<DayRecord>();
        }
    }
}
