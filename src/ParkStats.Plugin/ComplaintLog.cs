using CayplayAI;
using ParkStats.Core;

namespace ParkStats.Plugin;

/// <summary>
/// Notes where guests are when a complaint comes up. The game only counts complaints; which
/// attraction the guest was at is what makes a count something the player can act on.
/// </summary>
internal sealed class ComplaintLog
{
    private const int RecentKept = 40;
    // The short overload of AddThought may call the long one; both are hooked.
    private const long SameEventMs = 100;

    private readonly Func<AttractionInteraction, string> _rideName;
    private readonly Dictionary<string, Dictionary<string, int>> _places = new();
    private readonly Queue<string> _recent = new();
    private HashSet<VisitorThoughtType> _complaints;

    private IntPtr _lastSystem;
    private VisitorThoughtType _lastType;
    private long _lastTick;

    public ComplaintLog(Func<AttractionInteraction, string> rideName) => _rideName = rideName;

    /// <summary>The latest complaints with the guest's state at the time, for the diagnostics file.</summary>
    public IEnumerable<string> Recent => _recent;

    public void Reset() => _places.Clear();

    /// <summary>Called just before the game adds a thought to a guest.</summary>
    public void Record(VisitorThoughtsSystem system, VisitorThoughtType type)
    {
        if (system == null || !IsComplaint(type)) return;

        var now = Environment.TickCount64;
        if (system.Pointer == _lastSystem && type == _lastType && now - _lastTick < SameEventMs) return;
        _lastSystem = system.Pointer;
        _lastType = type;
        _lastTick = now;

        // The game refuses repeats of a thought that is still cooling down.
        if (!system.CanAddThought(type)) return;
        var brain = system.AIBrain;
        if (brain == null) return;

        var attraction = brain.AttractionInteraction;
        var place = attraction == null ? null : _rideName(attraction);
        var thought = type.ToString();
        if (place != null)
        {
            if (!_places.TryGetValue(thought, out var counts)) _places[thought] = counts = new Dictionary<string, int>();
            counts[place] = counts.TryGetValue(place, out var count) ? count + 1 : 1;
        }

        _recent.Enqueue($"{thought} | state {brain.currentStateName} | before {brain.lastState} | at {place ?? "-"}");
        while (_recent.Count > RecentKept) _recent.Dequeue();
    }

    public IReadOnlyList<CountLine> Places(string thought) =>
        _places.TryGetValue(thought, out var counts)
            ? counts.Select(p => new CountLine(p.Key, p.Value)).ToList()
            : Array.Empty<CountLine>();

    private bool IsComplaint(VisitorThoughtType type)
    {
        if (_complaints == null)
        {
            var thoughts = GameSettings.Common?.staticData?.Thoughts;
            if (thoughts == null) return false;

            // Complaints are the thoughts that lower a guest's mood.
            var complaints = new HashSet<VisitorThoughtType>();
            for (var i = 0; i < thoughts.Count; i++)
            {
                var thought = thoughts[i];
                if (thought != null && thought.MoodEffect < 0) complaints.Add(thought.Type);
            }
            _complaints = complaints;
        }
        return _complaints.Contains(type);
    }
}
