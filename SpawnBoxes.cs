using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SpawnBoxes;

public class PluginConfig : BasePluginConfig
{
    public bool Enabled { get; set; } = true;
    public float BoxSize { get; set; } = 30f;
    public float InteractionDistance { get; set; } = 50f;
    public bool ShowChatMessages { get; set; } = true;
    public bool DebugLog { get; set; } = false;
    public int TickRate { get; set; } = 16;
    public float BeamWidth { get; set; } = 0.5f;
    public int TerroristColorR { get; set; } = 255;
    public int TerroristColorG { get; set; } = 255;
    public int TerroristColorB { get; set; } = 0;
    public int CounterTerroristColorR { get; set; } = 0;
    public int CounterTerroristColorG { get; set; } = 150;
    public int CounterTerroristColorB { get; set; } = 255;
}

[MinimumApiVersion(80)]
public class SpawnBoxesPlugin : BasePlugin, IPluginConfig<PluginConfig>
{
    public override string ModuleName => "Spawn Boxes";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "CS2 Community";
    public override string ModuleDescription => "Creates visible, interactible boxes at spawn points for teleportation";

    private List<SavedSpawnPoint> _spawnPoints = new();
    private Dictionary<int, int> _playersNearSpawn = new(); // playerSlot -> spawnIndex
    private Dictionary<int, DateTime> _lastTeleportTime = new(); // playerSlot -> last teleport time
    private int _tickCounter = 0;

    public PluginConfig Config { get; set; } = new();

    public void OnConfigParsed(PluginConfig config)
    {
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        if (Config.DebugLog)
        {
            Console.WriteLine("==============================================");
            Console.WriteLine($"[SpawnBoxes] Version {ModuleVersion} loaded!");
            Console.WriteLine("==============================================");
        }

        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnTick>(OnTick);

        AddCommand("css_spawnboxes", "Toggle spawn boxes on/off", CommandToggleSpawnBoxes);
        AddCommand("css_spawnboxes_enable", "Enable spawn boxes", CommandEnableSpawnBoxes);
        AddCommand("css_spawnboxes_disable", "Disable spawn boxes", CommandDisableSpawnBoxes);
        AddCommand("css_reloadspawns", "Reload spawn points", CommandReloadSpawns);
        AddCommand("css_listspawns", "List all spawn points", CommandListSpawns);
        AddCommand("css_gotospawn", "Teleport to spawn number", CommandGoToSpawn);

        if (hotReload)
        {
            // If hot reloaded, detect spawns immediately
            Server.NextFrame(DetectSpawnPoints);
        }
    }

    public void CommandToggleSpawnBoxes(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null && !player.IsValid) return;

        Config.Enabled = !Config.Enabled;

        if (Config.Enabled)
        {
            Server.NextFrame(DetectSpawnPoints);
            string msg = "[SpawnBoxes] Spawn boxes enabled";
            if (player != null) player.PrintToChat(msg);
            else if (Config.DebugLog) Console.WriteLine(msg);
        }
        else
        {
            CleanupEntities();
            _spawnPoints.Clear();
            string msg = "[SpawnBoxes] Spawn boxes disabled";
            if (player != null) player.PrintToChat(msg);
            else if (Config.DebugLog) Console.WriteLine(msg);
        }
    }

    public void CommandEnableSpawnBoxes(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null && !player.IsValid) return;

        if (Config.Enabled)
        {
            string msg = "[SpawnBoxes] Spawn boxes already enabled";
            if (player != null) player.PrintToChat(msg);
            else if (Config.DebugLog) Console.WriteLine(msg);
            return;
        }

        Config.Enabled = true;
        Server.NextFrame(DetectSpawnPoints);

        string enableMsg = "[SpawnBoxes] Spawn boxes enabled";
        if (player != null) player.PrintToChat(enableMsg);
        else if (Config.DebugLog) Console.WriteLine(enableMsg);
    }

    public void CommandDisableSpawnBoxes(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null && !player.IsValid) return;

        if (!Config.Enabled)
        {
            string msg = "[SpawnBoxes] Spawn boxes already disabled";
            if (player != null) player.PrintToChat(msg);
            else if (Config.DebugLog) Console.WriteLine(msg);
            return;
        }

        Config.Enabled = false;
        CleanupEntities();
        _spawnPoints.Clear();
        string disableMsg = "[SpawnBoxes] Spawn boxes disabled";
        if (player != null) player.PrintToChat(disableMsg);
        else if (Config.DebugLog) Console.WriteLine(disableMsg);
    }

    public void CommandReloadSpawns(CCSPlayerController? player, CommandInfo command)
    {
        try
        {
            if (Config.DebugLog)
            {
                Logger.LogInformation("=================================================");
                Logger.LogInformation($">>> RELOAD COMMAND TRIGGERED {ModuleVersion} <<<");
                Logger.LogInformation($"CommandReloadSpawns called, Enabled={Config.Enabled}");
                Logger.LogInformation("=================================================");
            }

            // Reload config file from configs directory (not plugin directory)
            // Config is at: addons/counterstrikesharp/configs/plugins/SpawnBoxes/SpawnBoxes.json
            // Module is at: addons/counterstrikesharp/plugins/SpawnBoxes/
            var pluginsDir = Directory.GetParent(ModuleDirectory)?.FullName; // Get plugins directory
            var cssharpDir = Directory.GetParent(pluginsDir ?? "")?.FullName; // Get counterstrikesharp directory
            var configPath = Path.Combine(cssharpDir ?? "", "configs", "plugins", "SpawnBoxes", "SpawnBoxes.json");

            if (Config.DebugLog)
                Logger.LogInformation($"Looking for config at: {configPath}");

            bool fileExists = File.Exists(configPath);

            if (fileExists)
            {
                try
                {
                    var configJson = File.ReadAllText(configPath);
                    if (Config.DebugLog)
                        Logger.LogInformation($"Config file found, length: {configJson.Length} chars");

                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };

                    var newConfig = JsonSerializer.Deserialize<PluginConfig>(configJson, options);
                    if (newConfig != null)
                    {
                        OnConfigParsed(newConfig);
                        if (Config.DebugLog)
                        {
                            Logger.LogInformation($"Config reloaded successfully!");
                            Logger.LogInformation($"  BeamWidth: {Config.BeamWidth}");
                            Logger.LogInformation($"  T Color: ({Config.TerroristColorR}, {Config.TerroristColorG}, {Config.TerroristColorB})");
                            Logger.LogInformation($"  CT Color: ({Config.CounterTerroristColorR}, {Config.CounterTerroristColorG}, {Config.CounterTerroristColorB})");
                        }

                        string configMsg = " \x04[SpawnBoxes]\x01 Config reloaded! Recreating spawns...";
                        if (player != null) player.PrintToChat(configMsg);
                    }
                    else
                    {
                        if (Config.DebugLog)
                            Logger.LogWarning("Config deserialization returned null!");
                    }
                }
                catch (Exception ex)
                {
                    if (Config.DebugLog)
                    {
                        Logger.LogError($"Error reading/parsing config: {ex.Message}");
                        Logger.LogError($"Stack trace: {ex.StackTrace}");
                    }
                }
            }
            else
            {
                if (Config.DebugLog)
                    Logger.LogWarning("Config file not found! Creating default config...");

                // Create default config
                try
                {
                    // Ensure directory exists
                    var configDir = Path.GetDirectoryName(configPath);
                    if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
                    {
                        Directory.CreateDirectory(configDir);
                    }

                    var defaultConfig = new PluginConfig();
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = null
                    };
                    var json = JsonSerializer.Serialize(defaultConfig, options);
                    File.WriteAllText(configPath, json);

                    if (Config.DebugLog)
                        Logger.LogInformation("Default config file created successfully!");

                    // Now reload with the new config
                    OnConfigParsed(defaultConfig);

                    if (player != null)
                    {
                        player.PrintToChat(" \x0A[SpawnBoxes]\x01 Created default config file!");
                    }
                }
                catch (Exception ex)
                {
                    if (Config.DebugLog)
                        Logger.LogError($"Failed to create default config: {ex.Message}");
                }
            }

            if (!Config.Enabled)
            {
                if (Config.DebugLog)
                    Logger.LogInformation("Config is disabled, returning");
                string disabledMsg = " \x02[SpawnBoxes]\x01 Plugin is disabled in config";
                if (player != null) player.PrintToChat(disabledMsg);
                return;
            }

            if (Config.DebugLog)
                Logger.LogInformation("Scheduling DetectSpawnPoints via Server.NextFrame");

            Server.NextFrame(() => {
                if (Config.DebugLog)
                    Logger.LogInformation(">>>>>>> NextFrame callback executing, about to call DetectSpawnPoints");
                DetectSpawnPoints();
                if (Config.DebugLog)
                    Logger.LogInformation(">>>>>>> DetectSpawnPoints call completed");
            });

            string msg = " \x0A[SpawnBoxes]\x01 Reloading spawn points...";
            if (player != null) player.PrintToChat(msg);
        }
        catch (Exception ex)
        {
            if (Config.DebugLog)
            {
                Logger.LogError($"FATAL ERROR IN COMMAND: {ex.Message}");
                Logger.LogError($"Stack: {ex.StackTrace}");
            }
        }
    }

    public void CommandListSpawns(CCSPlayerController? player, CommandInfo command)
    {
        if (_spawnPoints.Count == 0)
        {
            string msg = "[SpawnBoxes] No spawns detected yet";
            if (player != null) player.PrintToChat(msg);
            else if (Config.DebugLog) Console.WriteLine(msg);
            return;
        }

        if (player != null)
        {
            player.PrintToChat($" \x04[SpawnBoxes]\x01 {_spawnPoints.Count} spawns loaded:");
            int ctCount = 0, tCount = 0;
            foreach (var spawn in _spawnPoints)
            {
                if (spawn.Team == CsTeam.CounterTerrorist)
                {
                    ctCount++;
                    player.PrintToChat($" \x0BCT #{ctCount}:\x01 ({spawn.Position.X:F0}, {spawn.Position.Y:F0}, {spawn.Position.Z:F0})");
                }
                else
                {
                    tCount++;
                    player.PrintToChat($" \x08T #{tCount}:\x01 ({spawn.Position.X:F0}, {spawn.Position.Y:F0}, {spawn.Position.Z:F0})");
                }
            }
        }
        else if (Config.DebugLog)
        {
            Console.WriteLine($"[SpawnBoxes] {_spawnPoints.Count} spawns loaded:");
            foreach (var spawn in _spawnPoints)
            {
                string team = spawn.Team == CsTeam.CounterTerrorist ? "CT" : "T";
                Console.WriteLine($"  {team}: ({spawn.Position.X:F0}, {spawn.Position.Y:F0}, {spawn.Position.Z:F0})");
            }
        }
    }

    public void CommandGoToSpawn(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        if (_spawnPoints.Count == 0)
        {
            player.PrintToChat(" \x02[SpawnBoxes]\x01 No spawns detected");
            return;
        }

        if (command.ArgCount < 2)
        {
            player.PrintToChat($" \x02[SpawnBoxes]\x01 Usage: css_gotospawn <1-{_spawnPoints.Count}>");
            return;
        }

        if (!int.TryParse(command.ArgByIndex(1), out int spawnIndex) || spawnIndex < 1 || spawnIndex > _spawnPoints.Count)
        {
            player.PrintToChat($" \x02[SpawnBoxes]\x01 Invalid spawn number. Use 1-{_spawnPoints.Count}");
            return;
        }

        TeleportToSpawn(player, _spawnPoints[spawnIndex - 1], spawnIndex - 1);
        player.PrintToChat($" \x0A[SpawnBoxes]\x01 Teleported to spawn #{spawnIndex}");
    }

    public override void Unload(bool hotReload)
    {
        if (Config.DebugLog)
            Console.WriteLine($"[SpawnBoxes] Unloading plugin (HotReload: {hotReload})...");

        try
        {
            CleanupEntities();
            _spawnPoints?.Clear();
            _playersNearSpawn?.Clear();
            _lastTeleportTime?.Clear();

            // Reinitialize collections for hot reload
            _spawnPoints = new();
            _playersNearSpawn = new();
            _lastTeleportTime = new();
        }
        catch (Exception ex)
        {
            if (Config.DebugLog)
                Console.WriteLine($"[SpawnBoxes] Error during unload: {ex.Message}");
        }

        if (Config.DebugLog)
            Console.WriteLine($"[SpawnBoxes] Plugin unloaded successfully");
    }

    private void OnMapStart(string mapName)
    {
        if (Config.DebugLog)
            Console.WriteLine($"[SpawnBoxes] MAP START: {mapName}, Enabled: {Config.Enabled}");

        // Clear spawns on map change
        CleanupEntities();
        _spawnPoints.Clear();

        // If enabled, detect spawns on next frame
        if (Config.Enabled)
        {
            Server.NextFrame(() => {
                DetectSpawnPoints();
            });
        }
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if (!Config.Enabled)
            return HookResult.Continue;

        if (Config.DebugLog)
            Console.WriteLine($"[SpawnBoxes] Round start - detecting spawns");

        Server.NextFrame(() => {
            DetectSpawnPoints();
        });

        return HookResult.Continue;
    }

    private void OnTick()
    {
        if (!Config.Enabled) return;

        // Throttle based on config (default 16 = ~4 times/sec instead of 64) for performance
        if (++_tickCounter % Config.TickRate != 0) return;

        // Check which players are near spawn points and handle USE key
        var players = Utilities.GetPlayers();

        foreach (var player in players)
        {
            if (player == null || !player.IsValid || !player.PawnIsAlive) continue;

            var pawn = player.PlayerPawn.Get();
            if (pawn == null || pawn.AbsOrigin == null) continue;

            int nearestSpawnIndex = -1;
            float nearestDistanceSquared = Config.InteractionDistance * Config.InteractionDistance;

            // Find closest spawn point within range (using squared distance to avoid sqrt)
            for (int i = 0; i < _spawnPoints.Count; i++)
            {
                var spawn = _spawnPoints[i];
                float distanceSquared = CalculateDistanceSquared(pawn.AbsOrigin, spawn.Position);

                if (distanceSquared < nearestDistanceSquared)
                {
                    nearestDistanceSquared = distanceSquared;
                    nearestSpawnIndex = i;
                }
            }

            // Update player's nearest spawn
            if (nearestSpawnIndex >= 0)
            {
                _playersNearSpawn[player.Slot] = nearestSpawnIndex;

                // Check if player is pressing USE button
                if ((player.Buttons & PlayerButtons.Use) != 0)
                {
                    // Check cooldown (prevent spam)
                    if (_lastTeleportTime.TryGetValue(player.Slot, out DateTime lastTeleport))
                    {
                        if ((DateTime.Now - lastTeleport).TotalSeconds < 1.0) // 1 second cooldown
                        {
                            continue;
                        }
                    }

                    // Teleport to spawn point
                    if (!_playersNearSpawn.ContainsKey(player.Slot) || _playersNearSpawn[player.Slot] == nearestSpawnIndex)
                    {
                        TeleportToSpawn(player, _spawnPoints[nearestSpawnIndex], nearestSpawnIndex);
                        _lastTeleportTime[player.Slot] = DateTime.Now;
                    }
                }
            }
            else
            {
                _playersNearSpawn.Remove(player.Slot);
            }
        }
    }

    private float CalculateDistanceSquared(Vector pos1, Vector pos2)
    {
        float dx = pos1.X - pos2.X;
        float dy = pos1.Y - pos2.Y;
        float dz = pos1.Z - pos2.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    private void TeleportToSpawn(CCSPlayerController player, SavedSpawnPoint spawn, int spawnIndex)
    {
        var pawn = player.PlayerPawn.Get();
        if (pawn == null || !pawn.IsValid) return;

        // Teleport to exact spawn coordinates
        pawn.Teleport(spawn.Position, spawn.Angles, new Vector(0, 0, 0));

        // Send feedback message if enabled
        if (Config.ShowChatMessages)
        {
            // Calculate team-specific spawn number (count spawns of same team before this one)
            int teamSpawnNumber = 1;
            for (int i = 0; i < spawnIndex; i++)
            {
                if (_spawnPoints[i].Team == spawn.Team)
                {
                    teamSpawnNumber++;
                }
            }

            string teamName = spawn.Team == CsTeam.Terrorist ? "T" : "CT";
            player.PrintToChat($" \x04[SpawnBoxes]\x01 Teleported to {teamName} spawn #{teamSpawnNumber}");
        }
    }

    private void DetectSpawnPoints()
    {
        try
        {
            if (!Config.Enabled)
                return;

            if (Config.DebugLog)
                Console.WriteLine($"[SpawnBoxes] DetectSpawnPoints started");

            CleanupEntities();
            _spawnPoints.Clear();

            int tSpawns = 0;
            int ctSpawns = 0;

            // Find minimum priority for competitive spawns
            int minPriority = int.MaxValue;

            var ctSpawnEntities = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_counterterrorist");

            foreach (var spawn in ctSpawnEntities)
        {
            if (spawn != null && spawn.IsValid && spawn.Enabled && spawn.Priority < minPriority)
            {
                minPriority = spawn.Priority;
            }
        }

        var tSpawnEntities = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_terrorist");
        foreach (var spawn in tSpawnEntities)
        {
            if (spawn != null && spawn.IsValid && spawn.Enabled && spawn.Priority < minPriority)
            {
                minPriority = spawn.Priority;
            }
        }

        // Find CT spawn points with minimum priority (competitive spawns)
        foreach (var spawn in ctSpawnEntities)
        {
            if (spawn == null || !spawn.IsValid || !spawn.Enabled || spawn.Priority != minPriority) continue;

            // Access position through CBodyComponent.SceneNode (MatchZy method)
            var position = spawn.CBodyComponent?.SceneNode?.AbsOrigin;
            var angles = spawn.CBodyComponent?.SceneNode?.AbsRotation;

            if (position == null)
                continue;

            var spawnPoint = new SavedSpawnPoint
            {
                Position = new Vector(position.X, position.Y, position.Z),
                Angles = angles ?? new QAngle(0, 0, 0),
                Team = CsTeam.CounterTerrorist
            };

            _spawnPoints.Add(spawnPoint);
            ctSpawns++;
        }

        // Find T spawn points with minimum priority (competitive spawns)
        foreach (var spawn in tSpawnEntities)
        {
            if (spawn == null || !spawn.IsValid || !spawn.Enabled || spawn.Priority != minPriority) continue;

            // Access position through CBodyComponent.SceneNode (MatchZy method)
            var position = spawn.CBodyComponent?.SceneNode?.AbsOrigin;
            var angles = spawn.CBodyComponent?.SceneNode?.AbsRotation;

            if (position == null)
                continue;

            var spawnPoint = new SavedSpawnPoint
            {
                Position = new Vector(position.X, position.Y, position.Z),
                Angles = angles ?? new QAngle(0, 0, 0),
                Team = CsTeam.Terrorist
            };

            _spawnPoints.Add(spawnPoint);
            tSpawns++;
        }

        if (Config.DebugLog)
            Console.WriteLine($"[SpawnBoxes] Loaded {tSpawns} T spawns and {ctSpawns} CT spawns");

        // Create visuals and triggers for all spawn points
        Server.NextFrame(() =>
        {
            foreach (var spawn in _spawnPoints)
            {
                try
                {
                    CreateSpawnVisuals(spawn);
                    CreateSpawnTrigger(spawn);
                }
                catch (Exception ex)
                {
                    if (Config.DebugLog)
                        Console.WriteLine($"[SpawnBoxes] ERROR creating spawn visual: {ex.Message}");
                }
            }
        });
        }
        catch (Exception ex)
        {
            if (Config.DebugLog)
            {
                Console.WriteLine($"[SpawnBoxes] !!!!! EXCEPTION in DetectSpawnPoints: {ex.Message}");
                Console.WriteLine($"[SpawnBoxes] !!!!! Stack trace: {ex.StackTrace}");
            }
        }
    }

    private void RecreateVisuals()
    {
        // Just clear entity references without trying to remove (game already cleaned them up)
        foreach (var spawn in _spawnPoints)
        {
            spawn.BeamEntities.Clear();
            spawn.TriggerEntity = null;
        }

        // Recreate visuals for existing spawn points
        Server.NextFrame(() =>
        {
            foreach (var spawn in _spawnPoints)
            {
                try
                {
                    CreateSpawnVisuals(spawn);
                    CreateSpawnTrigger(spawn);
                }
                catch (Exception ex)
                {
                    if (Config.DebugLog)
                        Console.WriteLine($"[SpawnBoxes] ERROR recreating spawn visual: {ex.Message}");
                }
            }
        });
    }

    private void CreateSpawnVisuals(SavedSpawnPoint spawn)
    {

        // Create a 2D square on the ground using beam entities
        float halfWidth = Config.BoxSize / 2f;
        var color = GetTeamColor(spawn.Team);
        var pos = spawn.Position;

        // Define 4 corners of the ground square
        Vector[] corners = new Vector[4];
        corners[0] = new Vector(pos.X - halfWidth, pos.Y - halfWidth, pos.Z + 2);
        corners[1] = new Vector(pos.X + halfWidth, pos.Y - halfWidth, pos.Z + 2);
        corners[2] = new Vector(pos.X + halfWidth, pos.Y + halfWidth, pos.Z + 2);
        corners[3] = new Vector(pos.X - halfWidth, pos.Y + halfWidth, pos.Z + 2);

        // Create beams for the 4 edges
        spawn.BeamEntities.Add(CreateBeam(corners[0], corners[1], color));
        spawn.BeamEntities.Add(CreateBeam(corners[1], corners[2], color));
        spawn.BeamEntities.Add(CreateBeam(corners[2], corners[3], color));
        spawn.BeamEntities.Add(CreateBeam(corners[3], corners[0], color));
    }

    private Color GetTeamColor(CsTeam team)
    {
        if (team == CsTeam.Terrorist)
        {
            return Color.FromArgb(255, Config.TerroristColorR, Config.TerroristColorG, Config.TerroristColorB);
        }
        else
        {
            return Color.FromArgb(255, Config.CounterTerroristColorR, Config.CounterTerroristColorG, Config.CounterTerroristColorB);
        }
    }

    private CEntityInstance? CreateBeam(Vector start, Vector end, Color color)
    {
        var beam = Utilities.CreateEntityByName<CBeam>("beam");
        if (beam == null)
        {
            if (Config.DebugLog)
                Console.WriteLine("[SpawnBoxes] ERROR: Failed to create beam entity");
            return null;
        }

        try
        {
            beam.Render = color;
            beam.Width = Config.BeamWidth;
            beam.HDRColorScale = 1.0f;
            beam.FrameRate = 0;  // Disable animation to prevent flickering

            beam.Teleport(start, new QAngle(0, 0, 0), new Vector(0, 0, 0));
            beam.EndPos.X = end.X;
            beam.EndPos.Y = end.Y;
            beam.EndPos.Z = end.Z;

            beam.DispatchSpawn();
        }
        catch (Exception ex)
        {
            if (Config.DebugLog)
                Console.WriteLine($"[SpawnBoxes] ERROR creating beam: {ex.Message}");
            return null;
        }

        return beam;
    }

    private void CreateSpawnTrigger(SavedSpawnPoint spawn)
    {
        var trigger = Utilities.CreateEntityByName<CBaseTrigger>("trigger_multiple");
        if (trigger == null)
        {
            if (Config.DebugLog)
                Console.WriteLine("[SpawnBoxes] Failed to create trigger entity");
            return;
        }

        // Position the trigger at the spawn point
        trigger.Teleport(spawn.Position, new QAngle(0, 0, 0), new Vector(0, 0, 0));

        trigger.DispatchSpawn();

        spawn.TriggerEntity = trigger;

        // Enable the trigger
        trigger.AcceptInput("Enable");
    }

    private void CleanupEntities()
    {
        foreach (var spawn in _spawnPoints)
        {
            spawn.Cleanup();
        }
    }
}

public class SavedSpawnPoint
{
    public Vector Position { get; set; } = new Vector(0, 0, 0);
    public QAngle Angles { get; set; } = new QAngle(0, 0, 0);
    public CsTeam Team { get; set; }
    public List<CEntityInstance?> BeamEntities { get; set; } = new();
    public CBaseTrigger? TriggerEntity { get; set; }

    public void Cleanup()
    {
        foreach (var entity in BeamEntities)
        {
            if (entity != null && entity.IsValid)
            {
                entity.Remove();
            }
        }
        BeamEntities.Clear();

        if (TriggerEntity != null && TriggerEntity.IsValid)
        {
            TriggerEntity.Remove();
        }
        TriggerEntity = null;
    }
}
