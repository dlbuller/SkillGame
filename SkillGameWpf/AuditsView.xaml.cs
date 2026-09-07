using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SkillGame;

namespace SkillGameWpf
{
    public partial class AuditsView : UserControl
    {
        private readonly AuditStore _audits = AppState.Audits;

        public AuditsView()
        {
            InitializeComponent();
            Loaded += (s, e) => Refresh();
        }

        public sealed class SwitchRow
        {
            public string Name { get; init; } = "";
            public string Points { get; init; } = "";
            public string Hits { get; init; } = "";        // lifetime wear count (never reset)
            public string SinceReset { get; init; } = "";  // resettable count
            public double BarWidth { get; init; }
        }

        public sealed class ScoreRow
        {
            public string Rank { get; init; } = "";
            public string Score { get; init; } = "";
            public string When { get; init; } = "";
        }

        private void Refresh()
        {
            var d = _audits.Data;

            StatGames.Text = d.GamesPlayed.ToString("N0");
            StatGamesSub.Text = $"{_audits.SessionGames} this session";

            StatTime.Text = FormatDuration(d.TotalPlaySeconds);
            StatTimeSub.Text = d.TotalPlaySeconds >= 3600 ? "hours : minutes" : "minutes : seconds";

            StatWinners.Text = d.Winners.ToString("N0");
            StatWinnersSub.Text = d.GamesPlayed > 0
                ? $"{(100.0 * d.Winners / d.GamesPlayed):0.#}% win rate"
                : "games won";

            long total = _audits.TotalHits();
            StatHits.Text = total.ToString("N0");
            StatHitsSub.Text = $"{_audits.SessionHits} this session";

            long on = d.PowerOnSeconds;
            StatPowerOn.Text = $"{on / 3600}h {(on % 3600) / 60}m";

            // Score-switch activity — the lifetime wear count is the headline, scaled to the busiest switch.
            var scoreSwitches = SwitchMap.All.Where(sw => sw.Kind == SwitchKind.Score).ToList();
            long max = 1;
            foreach (var sw in scoreSwitches) max = Math.Max(max, _audits.LifetimeHitsFor(sw.Id));

            var rows = new List<SwitchRow>();
            foreach (var sw in scoreSwitches)
            {
                long life = _audits.LifetimeHitsFor(sw.Id);
                long since = _audits.HitsFor(sw.Id);
                rows.Add(new SwitchRow
                {
                    Name = sw.Id,
                    Points = $"{sw.Points} pts",
                    Hits = life.ToString("N0"),
                    SinceReset = $"{since:N0} since reset",
                    BarWidth = life > 0 ? 8 + 150.0 * life / max : 3,
                });
            }
            SwitchList.ItemsSource = rows;

            // High scores.
            var hs = new List<ScoreRow>();
            for (int i = 0; i < d.HighScores.Count; i++)
                hs.Add(new ScoreRow { Rank = $"#{i + 1}", Score = d.HighScores[i].Score.ToString("N0"), When = d.HighScores[i].When });
            if (hs.Count == 0) hs.Add(new ScoreRow { Rank = "—", Score = "no scores yet", When = "" });
            HighScoreList.ItemsSource = hs;
        }

        // Clock-style so it reads cleanly in the seven-segment font (letters like s/m look like digits).
        private static string FormatDuration(long seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}:{t.Minutes:00}";
            return $"{t.Minutes}:{t.Seconds:00}";
        }

        private async void Backup_Click(object sender, RoutedEventArgs e)
        {
            string path = await Task.Run(() => _audits.Backup());   // file copy off the UI thread
            AppDialog.Info("Backup Audits", string.IsNullOrEmpty(path) ? "Backup failed — see the log." : "Audits backed up to:\n" + path);
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            string path = await Task.Run(() => _audits.ExportCsv());   // CSV write off the UI thread
            AppDialog.Info("Export CSV", string.IsNullOrEmpty(path) ? "Export failed — see the log." : "Audits exported to:\n" + path);
        }

        private void ClearScores_Click(object sender, RoutedEventArgs e)
        {
            if (AppDialog.Confirm("Clear High Scores", "Clear the high-score table?\n\nGames played, hits and play time are kept.", "CLEAR", "CANCEL"))
            { _audits.ClearHighScores(); Refresh(); }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (AppDialog.Confirm("Reset Counters",
                "Zero the resettable counters and clear the high-score table?\n\nTotal Games Played is permanent and will be kept.", "RESET", "CANCEL"))
            { _audits.Reset(); Refresh(); }
        }
    }
}
