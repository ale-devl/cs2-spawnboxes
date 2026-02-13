# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build

```bash
dotnet build SpawnBoxes.csproj -c Release
```

Output goes to `bin/Release/net8.0/SpawnBoxes.dll`. There are no tests or linting configured.

## Architecture

Single-file CounterStrikeSharp plugin (`SpawnBoxes.cs`) targeting .NET 8.0. The plugin creates visible beam-based boxes at CS2 competitive spawn points and lets players teleport to them by pressing USE (E).

### Key Classes

- **`PluginConfig`** (top of file) — All configurable settings with defaults. Config is auto-loaded from `configs/plugins/SpawnBoxes/SpawnBoxes.json` by CounterStrikeSharp, and can be hot-reloaded via `css_reloadspawns`.
- **`SpawnBoxesPlugin`** — Main plugin class implementing `BasePlugin` and `IPluginConfig<PluginConfig>`. Registers event handlers and commands in `Load()`.
- **`SavedSpawnPoint`** — Data class holding a spawn's position, angles, team, and references to its beam/trigger entities. Owns cleanup of its entities.

### Core Flow

1. **Spawn detection** (`DetectSpawnPoints`) — On map/round start, finds `info_player_terrorist` and `info_player_counterterrorist` entities, filters to minimum-priority (competitive 5v5) spawns using MatchZy's approach (`SpawnPoint.Priority`).
2. **Visual creation** (`CreateSpawnVisuals`) — Builds a 2D square from 4 `CBeam` entities at each spawn, offset vertically by `BoxHeightOffset`. Colors come from config RGB values per team.
3. **Trigger creation** (`CreateSpawnTrigger`) — Places a `trigger_multiple` entity at each spawn point.
4. **Tick processing** (`OnTick`) — Throttled by `TickRate`, checks player proximity to spawns using squared-distance comparison and handles USE key teleportation with a 1-second cooldown.
5. **Cleanup** (`CleanupEntities` → `SavedSpawnPoint.Cleanup`) — Removes all beam and trigger entities before recreating.

### Entity Lifecycle

All entity creation uses `Server.NextFrame()` to defer to the game thread. Entities are tracked per-spawn in `SavedSpawnPoint.BeamEntities` and `.TriggerEntity`, and cleaned up via `entity.Remove()`. The plugin survives hot reloads and round restarts by re-detecting spawns on each round start.

### Config Reload

`css_reloadspawns` manually reads the JSON config from the CounterStrikeSharp configs directory (navigated relative to `ModuleDirectory`), deserializes it, and calls `OnConfigParsed` before re-running spawn detection.

## Deployment

Deploy `SpawnBoxes.dll` to `game/csgo/addons/counterstrikesharp/plugins/SpawnBoxes/` on a CS2 server with CounterStrikeSharp installed.
