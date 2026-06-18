using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace PredictEverything;

public class PredictEverythingConfig
{
    public static PredictEverythingConfig Instance { get; private set; } = null!;

    public bool EnableTransparentMask { get; set; } = true;
    public bool EnablePredictionPanel { get; set; } = true;
    public bool EnableHoverPopup { get; set; } = true;
    public bool EnableLockedDashboard { get; set; } = true;
    public bool VerboseLogging { get; set; } = false;
    public float PanelX { get; set; } = -1;
    public float PanelY { get; set; } = -1;
    public float PanelW { get; set; } = -1;
    public float PanelH { get; set; } = -1;
    public bool TutorialShown { get; set; } = false;

    private static string ConfigPath => Path.Combine(
        OS.GetUserDataDir(), "mod_configs", "PredictEverything", "config.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                Instance = JsonSerializer.Deserialize<PredictEverythingConfig>(json) ?? new PredictEverythingConfig();
            }
            else { Instance = new PredictEverythingConfig(); }
        }
        catch (Exception ex) { ModLogger.Error($"Failed to load config: {ex.Message}"); Instance = new PredictEverythingConfig(); }
    }

    public static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(Instance);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex) { ModLogger.Error($"Failed to save config: {ex.Message}"); }
    }
}
