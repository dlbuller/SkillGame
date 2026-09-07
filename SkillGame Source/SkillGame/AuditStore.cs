using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SkillGame
{
    /// <summary>One high-score entry.</summary>
    public class ScoreEntry
    {
        public int Score { get; set; }
        public string When { get; set; } = "";
    }

    /// <summary>Serializable audit counters (the on-disk shape).</summary>
    public class AuditData
    {
        public long GamesPlayed { get; set; }
        public long Winners { get; set; }
        public long TotalPlaySeconds { get; set; }
        public long PowerOnSeconds { get; set; }   // lifetime time the machine has been powered on
        public Dictionary<string, long> SwitchHits { get; set; } = new();          // resettable — "since last reset"
        public Dictionary<string, long> LifetimeSwitchHits { get; set; } = new();  // permanent switch-wear log, never cleared
        public List<ScoreEntry> HighScores { get; set; } = new();
    }

    /// <summary>Persistent audit log (games, play time, winners, per-switch hits, and a top-10 high-score table) saved as JSON next to the exe; every operation is guarded and never throws.</summary>
    public class AuditStore : IAuditSink
    {
        public const int MaxHighScores = 10;
        private static readonly string FilePath =
            Path.Combine(AppContext.BaseDirectory, "skillgame_audits.json");
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        private readonly object _gate = new();
        private readonly Dictionary<string, long> _sessionHits = new();
        private DateTime? _gameStart;

        public AuditData Data { get; private set; } = new();
        public string FileLocation => FilePath;

        public void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var d = JsonSerializer.Deserialize<AuditData>(File.ReadAllText(FilePath));
                    if (d != null)
                    {
                        d.SwitchHits ??= new(); d.HighScores ??= new(); d.LifetimeSwitchHits ??= new();
                        if (d.LifetimeSwitchHits.Count == 0 && d.SwitchHits.Count > 0)
                            d.LifetimeSwitchHits = new Dictionary<string, long>(d.SwitchHits);   // seed the wear log from prior totals
                        Data = d;
                    }
                }
                Log.Info($"Audits loaded ({Data.GamesPlayed} games) from {FilePath}");
            }
            catch (Exception ex) { Log.Error("Audit load failed", ex); }
        }

        public void Save()
        {
            try { lock (_gate) File.WriteAllText(FilePath, JsonSerializer.Serialize(Data, JsonOpts)); }
            catch (Exception ex) { Log.Error("Audit save failed", ex); }
        }

        /// <summary>Saves, then writes a datestamped copy beside the audit file; returns the backup path, or "" on failure.</summary>
        public string Backup()
        {
            try
            {
                Save();
                string dest = Path.Combine(AppContext.BaseDirectory, $"skillgame_audits_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                lock (_gate) File.Copy(FilePath, dest, true);
                Log.Info("Audits backed up to " + dest);
                return dest;
            }
            catch (Exception ex) { Log.Error("Audit backup failed", ex); return ""; }
        }

        public long HitsFor(string switchId)
        {
            lock (_gate) return Data.SwitchHits.TryGetValue(switchId, out long n) ? n : 0;
        }

        /// <summary>Permanent lifetime hit count for a switch — the wear log a reset never clears.</summary>
        public long LifetimeHitsFor(string switchId)
        {
            lock (_gate) return Data.LifetimeSwitchHits.TryGetValue(switchId, out long n) ? n : 0;
        }

        public long SessionHitsFor(string switchId)
        {
            lock (_gate) return _sessionHits.TryGetValue(switchId, out long n) ? n : 0;
        }

        public long TotalHits()
        {
            lock (_gate) { long s = 0; foreach (long v in Data.SwitchHits.Values) s += v; return s; }
        }

        /// <summary>Games started since the app launched (this power-cycle).</summary>
        public int SessionGames { get; private set; }

        /// <summary>Score-switch hits since the app launched (this power-cycle).</summary>
        public long SessionHits
        {
            get { lock (_gate) { long s = 0; foreach (long v in _sessionHits.Values) s += v; return s; } }
        }

        /// <summary>Zeroes the resettable counters and high-score table, but preserves the permanent Total Games Played.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                long keepGames = Data.GamesPlayed;              // permanent lifetime total, never cleared
                long keepOn = Data.PowerOnSeconds;             // power-on hours is an odometer — keep it too
                var keepWear = Data.LifetimeSwitchHits;         // switch-wear log survives a reset (how much life is left)
                Data = new AuditData { GamesPlayed = keepGames, PowerOnSeconds = keepOn, LifetimeSwitchHits = keepWear };
                _sessionHits.Clear();
                _gameStart = null;
            }
            Save();
            Log.Info("Audits reset (total games played preserved)");
        }

        /// <summary>Clears only the high-score table, keeping games, hits and play time; saves immediately.</summary>
        public void ClearHighScores()
        {
            lock (_gate) Data.HighScores = new();
            Save();
            Log.Info("High scores cleared");
        }

        /// <summary>Writes the audits out as CSV and returns the path, or "" on failure.</summary>
        public string ExportCsv()
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory,
                    "skillgame_audits_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                var sb = new StringBuilder();
                lock (_gate)
                {
                    sb.AppendLine("Metric,Value");
                    sb.AppendLine($"Games played,{Data.GamesPlayed}");
                    sb.AppendLine($"Winners,{Data.Winners}");
                    sb.AppendLine($"Total play seconds,{Data.TotalPlaySeconds}");
                    sb.AppendLine($"Power-on seconds,{Data.PowerOnSeconds}");
                    sb.AppendLine($"Total switch hits,{TotalHitsNoLock()}");
                    sb.AppendLine();
                    sb.AppendLine("Switch,Hits (lifetime),Hits (since reset),Hits (session)");
                    foreach (var sw in SwitchMap.All)
                    {
                        Data.LifetimeSwitchHits.TryGetValue(sw.Id, out long life);
                        Data.SwitchHits.TryGetValue(sw.Id, out long all);
                        _sessionHits.TryGetValue(sw.Id, out long ses);
                        sb.AppendLine($"{sw.Id} ({sw.Label}),{life},{all},{ses}");
                    }
                    sb.AppendLine();
                    sb.AppendLine("Rank,High score,When");
                    for (int i = 0; i < Data.HighScores.Count; i++)
                        sb.AppendLine($"{i + 1},{Data.HighScores[i].Score},{Data.HighScores[i].When}");
                }
                File.WriteAllText(path, sb.ToString());
                Log.Info("Audits exported to " + path);
                return path;
            }
            catch (Exception ex) { Log.Error("Audit export failed", ex); return ""; }
        }

        private long TotalHitsNoLock() { long s = 0; foreach (long v in Data.SwitchHits.Values) s += v; return s; }

        // ---- IAuditSink (called by GameEngine) ----
        public void GameStarted()
        {
            lock (_gate) { Data.GamesPlayed++; _gameStart = DateTime.Now; }
            SessionGames++;
            Save();
        }

        public void GameEnded(int finalScore)
        {
            lock (_gate)
            {
                if (_gameStart is DateTime start)
                {
                    Data.TotalPlaySeconds += (long)Math.Max(0, (DateTime.Now - start).TotalSeconds);
                    _gameStart = null;
                }
                if (finalScore > 0)
                {
                    Data.HighScores.Add(new ScoreEntry { Score = finalScore, When = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
                    Data.HighScores = Data.HighScores.OrderByDescending(s => s.Score).Take(MaxHighScores).ToList();
                }
            }
            Save();
        }

        public void Won() { lock (_gate) Data.Winners++; }

        /// <summary>Accumulate power-on time (called on a timer while the app runs).</summary>
        public void AddPowerOnSeconds(long seconds) { lock (_gate) Data.PowerOnSeconds += Math.Max(0, seconds); }

        public void SwitchHit(string switchId)
        {
            lock (_gate)
            {
                Data.SwitchHits.TryGetValue(switchId, out long n);
                Data.SwitchHits[switchId] = n + 1;
                Data.LifetimeSwitchHits.TryGetValue(switchId, out long life);
                Data.LifetimeSwitchHits[switchId] = life + 1;   // wear log — never reset
                _sessionHits.TryGetValue(switchId, out long s);
                _sessionHits[switchId] = s + 1;
            }
        }
    }
}
