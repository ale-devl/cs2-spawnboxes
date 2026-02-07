# CS2 Spawn Boxes Plugin

A CounterStrikeSharp plugin that creates visible, interactible boxes around competitive spawn points for both teams. Players can press the USE key (E) on these boxes to teleport to the exact spawn coordinates.

## Features

- **Visible Spawn Boxes**: 2D ground squares drawn at each spawn point using beam entities
- **Competitive Spawns Only**: Automatically filters for 5v5 competitive spawns (5-6 per team)
- **Team Colors**: Bright yellow for Terrorist spawns, bright blue for CT spawns (fully configurable)
- **Interactive Teleportation**: Press USE (E) key when near a spawn box to teleport
- **Exact Coordinates**: Teleports to the precise spawn point location and facing angle
- **MatchZy Integration**: Works seamlessly with MatchZy's `.prac` workflow
- **Hot Reload Support**: Reload plugin without server restart
- **Configurable**: Box size, beam width, colors, brightness, interaction distance, and more
- **Runtime Config Reload**: Change settings and reload without restarting server

## Installation

1. **Prerequisites**:
   - Counter-Strike 2 dedicated server
   - CounterStrikeSharp installed ([CSS Documentation](https://docs.cssharp.dev/))
   - .NET 8.0 SDK for building

2. **Build the Plugin**:
   ```bash
   dotnet build SpawnBoxes.csproj -c Release
   ```

3. **Deploy**:
   - Copy the built DLL from `bin/Release/net8.0/SpawnBoxes.dll` to:
     ```
     game/csgo/addons/counterstrikesharp/plugins/SpawnBoxes/SpawnBoxes.dll
     ```

4. **Configure** (Optional):
   - Create a config file at:
     ```
     game/csgo/addons/counterstrikesharp/configs/plugins/SpawnBoxes/SpawnBoxes.json
     ```
   - See Configuration section below for options

5. **Restart Server** or use `css_plugins reload`

## Usage

### For Players

1. Look for colored ground squares around the map (Yellow = T spawns, Blue = CT spawns)
2. Walk up to a spawn box (within 100 units by default)
3. Press **E** (USE key)
4. You will be teleported to that exact spawn point with a 1-second cooldown

### For Admins

**Commands**:
- `css_spawnboxes` - Toggle spawn boxes on/off
- `css_spawnboxes_enable` - Enable spawn boxes
- `css_spawnboxes_disable` - Disable spawn boxes
- `css_reloadspawns` - Reload config file and recreate spawn boxes
- `css_listspawns` - List all detected spawn points with coordinates
- `css_gotospawn <number>` - Teleport to a specific spawn number

**MatchZy Integration**:
- Works with `.prac` command - spawn boxes persist through practice mode
- Survives `mp_restartgame` and round restarts
- Use `css_spawnboxes_disable` if you want to disable during matches. Note that you can also call this via MatchZys config files for different modes.

## Configuration

Create `configs/plugins/SpawnBoxes/SpawnBoxes.json` or run the plugin once to autocreate it with defaults:

```json
{
  "Enabled": true,
  "BoxSize": 64.0,
  "InteractionDistance": 100.0,
  "ShowChatMessages": true,
  "DebugLog": false,
  "TickRate": 16,
  "BeamWidth": 2.0,
  "TerroristColorR": 255,
  "TerroristColorG": 255,
  "TerroristColorB": 0,
  "CounterTerroristColorR": 0,
  "CounterTerroristColorG": 150,
  "CounterTerroristColorB": 255
}
```

### Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Enabled` | bool | `true` | Enable/disable the plugin |
| `BoxSize` | float | `64.0` | Size of spawn boxes in units |
| `InteractionDistance` | float | `100.0` | Maximum distance to interact with boxes |
| `ShowChatMessages` | bool | `true` | Show chat message when teleporting |
| `DebugLog` | bool | `false` | Enable verbose console logging for debugging |
| `TickRate` | int | `16` | How often to check player proximity (lower = more frequent, higher = less CPU) |
| `BeamWidth` | float | `2.0` | Width of the beam lines in pixels |
| `TerroristColorR` | int | `255` | Red component for T spawn boxes (0-255) |
| `TerroristColorG` | int | `255` | Green component for T spawn boxes (0-255) |
| `TerroristColorB` | int | `0` | Blue component for T spawn boxes (0-255) |
| `CounterTerroristColorR` | int | `0` | Red component for CT spawn boxes (0-255) |
| `CounterTerroristColorG` | int | `150` | Green component for CT spawn boxes (0-255) |
| `CounterTerroristColorB` | int | `255` | Blue component for CT spawn boxes (0-255) |

**Color Presets**:
- **Bright Yellow (default T)**: R=255, G=255, B=0
- **Bright Blue (default CT)**: R=0, G=150, B=255
- **Cyan**: R=0, G=255, B=255
- **Magenta**: R=255, G=0, B=255
- **Green**: R=0, G=255, B=0
- **Orange**: R=255, G=165, B=0

**TickRate Performance Guide**:
- `8` = ~8 checks/sec (very responsive, higher CPU)
- `16` = ~4 checks/sec (default, good balance)
- `32` = ~2 checks/sec (lower CPU, still responsive)
- `64` = ~1 check/sec (minimal CPU, may feel sluggish)

**After changing config**, run `css_reloadspawns` to apply changes without restarting the server.

## How It Works

1. **Spawn Detection**: On map start/round start, the plugin finds all spawn entities using MatchZy's method
2. **Competitive Filtering**: Filters for minimum priority spawns (typically 5-6 per team for 5v5)
3. **Visual Creation**: Creates 4 beam entities per spawn to form a 2D ground square
4. **Trigger Setup**: Places trigger volumes at each spawn point
5. **Player Tracking**: Each tick, checks player distances to spawn points
6. **Interaction**: When player presses USE near a spawn, teleports them to exact coordinates with 1-second cooldown

## Technical Details

- **Framework**: CounterStrikeSharp v1.0.362+
- **Language**: C# (.NET 8.0)
- **Visuals**: CBeam entities (4 per spawn box - forming a 2D square)
- **Triggers**: CBaseTrigger entities
- **Events**: `OnMapStart`, `OnRoundStart`, `OnTick`
- **Spawn Detection**: Uses `SpawnPoint.Priority` filtering for competitive spawns
- **Persistence**: Survives round restarts and `mp_restartgame`

## Troubleshooting

**Boxes not appearing?**
- Check console for spawn detection messages (`[SpawnBoxes] Found X T spawns and Y CT spawns`) (Note: enable logging)
- Ensure map has spawn entities with correct priority
- Try `css_reloadspawns` command
- Verify plugin is enabled: `css_spawnboxes_enable`

**Can't interact with boxes?**
- Ensure you're within `InteractionDistance` (default 100 units)
- Check that plugin is enabled with `css_listspawns`
- Verify USE key is bound properly (default E)
- Check for 1-second cooldown between teleports

**Boxes hard to see?**
- Try brighter colors (255 values in RGB)
- Increase `BeamWidth` in config (try 1.0 or 2.0)
- Run `css_reloadspawns` after changing config

**Wrong spawns showing?**
- Plugin automatically filters for competitive spawns (minimum priority)
- All detected spawns are 5v5 competitive spawns, not deathmatch spawns
- Use `css_listspawns` to see all detected coordinates

## Credits

Spawn detection method inspired by [MatchZy](https://github.com/shobhit-pathak/MatchZy) by shobhit-pathak.

Color scheme inspired by Refrag.gg's practice mode implementation.

## License

MIT License - See [LICENSE](LICENSE) file for details.
