using System;
using System.IO;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace PredictEverything;

public static class ModLogger
{
    private static string? _logPath;
    private static readonly object _lock = new();

    public static void Init()
    {
        string executablePath = OS.GetExecutablePath();
        string directoryName = Path.GetDirectoryName(executablePath) ?? "";
        _logPath = Path.Combine(directoryName, "mods", "PredictEverything", "logs", "predict_everything.log");
        try
        {
            var dir = Path.GetDirectoryName(_logPath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(_logPath, "", Encoding.UTF8);  // Truncate on each run
            // Write PID file for auto-deploy
            var pidPath = Path.Combine(dir!, "pid.txt");
            File.WriteAllText(pidPath, global::System.Environment.ProcessId.ToString());
        }
        catch
        {
            try
            {
                _logPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "mods", "PredictEverything", "logs", "predict_everything.log");
                var dir = Path.GetDirectoryName(_logPath);
                if (dir != null) { Directory.CreateDirectory(dir); File.WriteAllText(_logPath, "", Encoding.UTF8); }
                // Write PID file for auto-deploy
                var pidPath = Path.Combine(dir!, "pid.txt");
                File.WriteAllText(pidPath, global::System.Environment.ProcessId.ToString());
            }
            catch { _logPath = null; }
        }
    }

    public static void Info(string msg) { Log.Info($"[PredictEverything] {msg}"); WriteFile("INFO", msg); }
    public static void Warn(string msg) { Log.Warn($"[PredictEverything] {msg}"); WriteFile("WARN", msg); }
    public static void Error(string msg) { Log.Error($"[PredictEverything] {msg}"); WriteFile("ERROR", msg); }
    public static void Error(string msg, Exception ex) { Log.Error($"[PredictEverything] {msg}: {ex}"); WriteFile("ERROR", $"{msg}: {ex}"); }

    private static void WriteFile(string level, string msg)
    {
        try
        {
            var line = $"{DateTime.UtcNow:HH:mm:ss.fff} [{level}] {msg}{System.Environment.NewLine}";
            lock (_lock) { if (_logPath != null) File.AppendAllText(_logPath, line, Encoding.UTF8); }
        }
        catch { }
    }
}
