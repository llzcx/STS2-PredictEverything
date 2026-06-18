using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace PredictEverything;

/// <summary>
/// Mod entry point. Called by the game's ModManager when the mod DLL is loaded.
/// Initializes logging, localization, config, and applies Harmony patches.
/// </summary>
[ModInitializer("Initialize")]
public static class PredictEverythingMod
{
    private static Harmony? _harmony;

    public static void Initialize()
    {
        ModLogger.Init();
        ModLogger.Info("PredictEverything mod loading...");

        I18n.Initialize();
        PredictEverythingConfig.Load();

        _harmony = new Harmony("com.shian.PredictEverything");
        try
        {
            var patcher = new PatchClassProcessor(_harmony, typeof(PredictEverythingPatches));
            var report = patcher.Patch();
            ModLogger.Info($"Harmony: {report.Count} patches applied");
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Harmony PatchAll failed: {ex}");
        }

        ModLogger.Info(string.Format(
            "PredictEverything v1.0.0 loaded — lang={0}, transparentMask={1}",
            I18n.CurrentLang,
            PredictEverythingConfig.Instance.EnableTransparentMask));
    }
}
