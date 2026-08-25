using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MelonLoader;

namespace CrimeSceneCoop;

public sealed class CoopConfig
{
    public static CoopConfig Current { get; private set; } = new();

    public int DirectPort { get; set; } = 27040;
    public float PoseHz { get; set; } = 20f;
    public float SnapshotHz { get; set; } = 2f;
    public bool InjectMainMenuButton { get; set; } = true;
    public bool ShowMenuHint { get; set; } = true;

    // optional exact type names from probe.json if auto detect misses
    public List<string> ExtraStainTypeNames { get; set; } = new();
    public List<string> ExtraCleanMethodNames { get; set; } = new();
    public string PlayerTypeName { get; set; } = "";

    public static string Dir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData", "TwoClean");
    public static string PathFile => Path.Combine(Dir, "config.json");

    public static void Load()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (File.Exists(PathFile))
            {
                var json = File.ReadAllText(PathFile);
                Current = JsonSerializer.Deserialize<CoopConfig>(json) ?? new CoopConfig();
            }
            else
            {
                Save();
            }
        }
        catch (Exception ex)
        {
            CoopLog.Warn("Config load failed: " + ex.Message);
            Current = new CoopConfig();
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(PathFile, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            CoopLog.Warn("Config save failed: " + ex.Message);
        }
    }
}
