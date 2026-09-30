namespace ParkStats.Core;

/// <summary>What a guest complaint means, keyed by the game's thought name.</summary>
public static class ComplaintHints
{
    private const int PlacesShown = 3;

    // The game's own text for a thought is what the guest says ("I had bad service!"), which
    // does not tell the player what to change. These say what lies behind it.
    private static readonly Dictionary<string, string> Hints = new()
    {
        ["PoorService"] = "A guest in a sauna or hot tub asked for different settings and did not get them. " +
                          "Set what they ask for on its control panel.",
        ["WaitedTooLong"] = "They gave up waiting to be served. The stand or desk needs more or faster staff.",
        ["Waiting"] = "They are waiting to be served.",
        ["Tired"] = "No free lounger when they needed rest.",
        ["SleptOnFloor"] = "They slept on the floor for lack of a free lounger.",
        ["LowTrash"] = "They could not find a trash bin in time.",
        ["LowToilet"] = "They could not reach a toilet in time.",
        ["LowHygeine"] = "They could not get to a shower.",
        ["Hungry"] = "They found nothing to eat.",
        ["Thirsty"] = "They found nothing to drink.",
        ["Bored"] = "Not enough fun attractions for them.",
        ["UsedDirtyAttraction"] = "An attraction they used was dirty. It needs cleaning.",
        ["UsedWornAttraction"] = "An attraction they used was worn. It needs a repair.",
        ["WaterWasDirty"] = "The pool water was dirty.",
        ["PoolWasDrained"] = "A pool was drained while they wanted to swim.",
        ["BadDecorationLevel"] = "The park has too little decoration.",
        ["Drowning"] = "A guest was drowning. Lifeguards prevent this.",
        ["Slipped"] = "They slipped on a wet floor.",
        ["Injured"] = "They were injured on an attraction.",
        ["Panic"] = "They panicked on an attraction.",
        ["PhotographedByPervert"] = "A visitor is taking unwanted photos. Catch them.",
        ["SawVomit"] = "Someone was sick nearby.",
        ["Disgust"] = "They saw something disgusting nearby.",
    };

    // Complaints caused by the attraction the guest was using. For the rest, the attraction a
    // guest happened to be heading for says nothing about the cause.
    private static readonly HashSet<string> AboutAnAttraction = new()
    {
        "PoorService", "WaitedTooLong", "Waiting", "UsedDirtyAttraction", "UsedWornAttraction",
        "WaterWasDirty", "PoolWasDrained", "Drowning", "Injured",
    };

    public static string? For(string thought) => Hints.TryGetValue(thought, out var hint) ? hint : null;

    /// <summary>The hint for a complaint followed by where it has mostly happened.</summary>
    public static string? Note(string thought, IReadOnlyList<CountLine> places)
    {
        var parts = new List<string>();
        if (For(thought) is { } hint) parts.Add(hint);

        var top = AboutAnAttraction.Contains(thought)
            ? places.Where(p => p.Count > 0).OrderByDescending(p => p.Count).Take(PlacesShown).ToList()
            : new List<CountLine>();
        if (top.Count > 0)
        {
            parts.Add($"Mostly at: {string.Join(", ", top.Select(p => $"{p.Label} ({p.Count})"))}.");
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }
}
