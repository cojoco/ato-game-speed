# Game Speed

A small BepInEx 5 mod for **Across the Obelisk** that makes the game run faster, in single player and in co-op, so there is less idle time between turns.

**Status:** built 2026-10-21, not tested in game yet.

## What it does

- **Single player:** the game runs at `GameSpeed` (default 1.7x): animations, enemy turns and the game's own pauses are all quicker. Optionally also turns on the game's hidden **Ultrafast** mode (`Ultrafast = true`), which skips some effects and pauses entirely.
- **Co-op:** the host's `GameSpeed` is sent to the room and every player with Game Speed runs at that speed. Everyone has to run at the same speed, because co-op combat waits for every player at each step: a faster player would only wait longer for the others. If the host does not have the mod, everyone plays at normal speed. Ultrafast is never used in co-op (the game turns it off there itself).
- Works in any co-op room, with or without [More Players](https://github.com/cojoco/ato-more-players).
- Changes nothing about the rules of the game, so runs still go to the Steam leaderboards.

## Settings (`BepInEx/config/com.cojoco.gamespeed.cfg`)

| Section / key | Default | Meaning |
|---|---|---|
| `[Speed] GameSpeed` | `1.7` | How fast the game runs, 1 to 4 (1 = normal). In co-op only the host's value counts. |
| `[Speed] Ultrafast` | `false` | Single player only: also use the game's Ultrafast mode. Overrides the game's Fast mode option while on. |

Both can be changed while the game runs (F5 config manager, if installed); in co-op the host's change reaches everyone at once.

## Logs

`BepInEx/LogOutput.log`:
- `Game Speed <version> loaded. GameSpeed=1.7x, Ultrafast=False.` at startup.
- `Game speed: 1.7x (single player).`, `(co-op, the host's setting)`, `(online, not in a room)` (always 1x, while picking a region or room) whenever the speed changes.
- Host only: `Co-op game speed set to 1.7x ...`, then one line per player saying whether they have Game Speed. A player without it plays at 1x and everyone waits for them.

## Notes

- Do not use it together with another speed mod (such as binbin's Speed Up): both set Unity's time scale and in a co-op room Game Speed wins.
- Install: copy `GameSpeed.dll` into `BepInEx/plugins` (see `INSTALL.txt`). Every co-op player who should run fast needs it.

## Building

`dotnet build -c Release` (netstandard2.1). The game folder defaults to the Steam path on Linux; override with `-p:GameDir=...`. The DLL is copied into `BepInEx/plugins` after each build.

## License

GNU GPL v3, see `LICENSE`.
