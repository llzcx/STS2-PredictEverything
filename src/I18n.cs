using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace PredictEverything;

public static class I18n
{
    public static string CurrentLang { get; private set; } = "zh";
    public static event Action? LanguageChanged;

    private static Dictionary<string, string> _current = new();
    private static readonly Dictionary<string, Dictionary<string, string>> _cache = new();

    private static string LocaleDir => Path.Combine(
        Path.GetDirectoryName(OS.GetExecutablePath()) ?? "", "mods", "PredictEverything", "locale");

    public static void Initialize()
    {
        LoadLocaleIntoCache("zh");
        LoadLocaleIntoCache("en");
        _current = _cache.GetValueOrDefault(CurrentLang, _cache["zh"]);
        ModLogger.Info($"I18n initialized — lang={CurrentLang}, strings={_current.Count}");
    }

    public static string Tr(string key)
    {
        if (_current.TryGetValue(key, out var value)) return value;
        if (_cache.TryGetValue("zh", out var zh) && zh.TryGetValue(key, out var zhVal)) return zhVal;
        return key;
    }

    public static void SetLanguage(string lang)
    {
        if (CurrentLang == lang) return;
        CurrentLang = lang;
        _current = _cache.GetValueOrDefault(lang, _cache["zh"]);
        LanguageChanged?.Invoke();
    }

    private static void LoadLocaleIntoCache(string lang)
    {
        var path = Path.Combine(LocaleDir, $"{lang}.json");
        if (!File.Exists(path)) { ModLogger.Warn($"Locale file not found: {path}"); return; }
        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<LocaleFile>(json);
            if (doc?.Strings != null) _cache[lang] = doc.Strings;
        }
        catch (Exception ex) { ModLogger.Error($"Failed to load locale {lang}: {ex.Message}"); }
    }

    private class LocaleFile
    {
        [JsonPropertyName("strings")]
        public Dictionary<string, string> Strings { get; set; } = new();
    }
}
