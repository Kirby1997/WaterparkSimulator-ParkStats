# ParkStats

A Waterpark Simulator mod that adds a proper stats breakdown to the tablet.

The game's Park Management page shows the ticket price, the age split, a visitor count and eight average need bars. The game tracks much more than that. ParkStats adds a row of tabs to the same page:

| Tab | Shows |
|---|---|
| Park | The game's own page, unchanged |
| Today | Net result so far, visitors, satisfaction, prestige, and the three most urgent things to do, each with its reason |
| Money | Today's income and expenses by reason, income per visitor, what each kind of attraction has earned, balance, lifetime totals, loans, staff cost |
| Rides | Attractions by kind: open, closed, broken or with a guest request waiting, uses today, price against ideal price, worst cleanliness and durability, guests waiting |
| Guests | Each need with its average and how many guests are under half, which attractions raise it, top complaints with their cause and where they happen, reasons for leaving |
| Build | What one attraction of each kind earns against its upkeep and how busy it is, room for visitors against what prestige allows, the cheapest attractions to add room with, what is not built yet, what is locked |
| History | Visitors, net result and satisfaction for past days |
| Advice | A ranked list of what is holding the park back or losing sales: empty stands, queues, unused or underpriced attractions, the weakest need, and what the next star needs |

The tabs use the game's own card and text style. While one of them is open, the game's own stats on that card are hidden; the Park tab brings them back.

The mod only reads the game's state. It changes nothing in your park or your save.

## Status

Early. The data and advice logic is covered by tests. The panel has been run in the game on a small park; large parks, controllers and multiplayer clients are untested.

Things to know:

- Most of the detail exists only on the host. When you join someone else's park, those values show as `n/a`.
- History starts from the first full day played with the mod installed, and is kept per park name.
- Complaints, and the places they happen, are counted from the moment the park is loaded and start again each day.
- The Money tab counts every change to your balance. The game's own end-of-day report leaves out miscellaneous income such as trash rewards, so the two can differ by that amount.
- While the park is empty the game has no satisfaction readings, so needs show as `n/a`.
- Advice about the ticket price is switched off until the meaning of the game's target price is confirmed.
- Net earnings per attraction, and the advice to build another, need one full day on record: a morning's takings cannot be set against a whole day's upkeep.
- Staff cost is judged against the income of recent full days, so that advice stays quiet until one day has been recorded.
- "Raised by" lists attractions whose use changes a need directly. An attraction that works another way, such as handing out an item, may be missing from it.

## Install

With [r2modman](https://thunderstore.io/package/ebkr/r2modman/): select Waterpark Simulator, install `BepInExPack_IL2CPP`, then install ParkStats.

By hand: install BepInEx 6 (IL2CPP) for the game, then copy `ParkStats.Plugin.dll` and `ParkStats.Core.dll` into `BepInEx/plugins/ParkStats/`.

## Settings

`BepInEx/config/com.github.kirby1997.parkstats.cfg`:

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Add the tabs |
| `FontScale` | `1` | Text size multiplier for the tabs |
| `HistoryDays` | `60` | Days kept by the History tab |
| `WriteDiagnostics` | `false` | Write `diagnostics.txt` and `method-addresses.txt` to `BepInEx/config/ParkStats/` when the page is opened |

If the mod misbehaves after a game update, set `WriteDiagnostics = true`, open the Park Management page once, and attach `diagnostics.txt` and `BepInEx/LogOutput.log` to an issue.

## Building

Requirements: the .NET SDK 8 or newer, the game, and BepInExPack_IL2CPP installed through r2modman. Start the game once with BepInEx installed so it generates `BepInEx/interop`.

```
dotnet test
dotnet build -c Release
```

The build copies the plugin into the r2modman `Default` profile. To use another location, create `GamePaths.user.props` next to `GamePaths.props`:

```xml
<Project>
  <PropertyGroup>
    <ProfileDir>D:\path\to\profile</ProfileDir>
  </PropertyGroup>
</Project>
```

To build the Thunderstore package into `dist/`:

```
dotnet build src/ParkStats.Plugin -c Release -t:PackThunderstore
```

## Layout

- `src/ParkStats.Core` has no game references: the data model, the advice rules, the history file and the text of every tab. This is where the tests point.
- `src/ParkStats.Plugin` is the BepInEx plugin: it reads the game, draws the panel and hooks the tablet.
- `tests/ParkStats.Tests` are the xUnit tests for Core.

## License

MIT. Not affiliated with the developers of Waterpark Simulator.
