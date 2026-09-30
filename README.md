# ParkStats

A Waterpark Simulator mod that adds a proper stats breakdown to the tablet.

The game's Park Management page shows the ticket price, the age split, a visitor count and eight average need bars. The game tracks much more than that. ParkStats adds a row of tabs to the same page:

| Tab | Shows |
|---|---|
| Park | The game's own page, unchanged |
| Today | Net result so far, visitors, satisfaction, and the three most urgent things to fix |
| Money | Today's income and expenses by reason, balance, lifetime totals, loans, staff cost |
| Rides | Every attraction: open, closed or broken, uses today, price against ideal price, cleanliness, durability, queue |
| Guests | Each need with its average and how many guests are running low, average cash, top complaints, reasons for leaving |
| History | Visitors, net result and satisfaction for past days |
| Advice | A ranked list of what is holding the park back |

The mod only reads the game's state. It changes nothing in your park or your save.

## Status

Early. The data and advice logic is covered by tests. The in-game panel is new and still being checked against the running game, so expect rough edges.

Known limits:

- Most of the detail exists only on the host. When you join someone else's park, those values show as `n/a`.
- History starts from the first full day played with the mod installed, and is kept per park name.
- Complaints are counted from the moment the park is loaded.

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
| `WriteDiagnostics` | `true` | Write `BepInEx/config/ParkStats/diagnostics.txt` when the page is opened |

If the mod misbehaves after a game update, attach `diagnostics.txt` and `BepInEx/LogOutput.log` to an issue.

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
