using System;
using System.IO;

namespace SkillGame
{
    /// <summary>Minimal always-on file logger writing timestamped lines to %LOCALAPPDATA%\SkillGame\skillgame.log; never throws so logging can't take the game down.</summary>
    public static class Log
    {
        private static readonly object _gate = new();
        private static readonly string _path = BuildPath();

        private static string BuildPath()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkillGame");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "skillgame.log");
            }
            catch { return ""; }
        }

        /// <summary>Full path to the log file (empty if it couldn't be created).</summary>
        public static string FilePath => _path;

        public static void Info(string message) => Write("INFO", message);

        public static void Error(string message, Exception? ex = null) =>
            Write("ERROR", ex is null ? message : $"{message} :: {ex}");

        /// <summary>The last <paramref name="lines"/> lines of the log, newest last; never throws.</summary>
        public static string Tail(int lines)
        {
            if (string.IsNullOrEmpty(_path)) return "(logging unavailable)";
            try
            {
                lock (_gate)
                {
                    if (!File.Exists(_path)) return "(log is empty)";
                    // Read via a shared stream so it doesn't fight AppendAllText.
                    using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs);
                    var all = sr.ReadToEnd().Split('\n');
                    int start = Math.Max(0, all.Length - lines - 1);
                    return string.Join("\n", all[start..]).TrimEnd('\n', '\r');
                }
            }
            catch (Exception ex) { return "(could not read log: " + ex.Message + ")"; }
        }

        private static void Write(string level, string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
            System.Diagnostics.Debug.WriteLine(line);
            if (string.IsNullOrEmpty(_path)) return;
            try { lock (_gate) File.AppendAllText(_path, line + Environment.NewLine); } catch { }
        }
    }
}
