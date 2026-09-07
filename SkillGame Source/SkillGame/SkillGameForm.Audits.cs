using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SkillGame
{
    // Read-only "books" for the machine. The UI is built in code (populated from AuditStore) and is
    // display-only — every control is a Label, so the player can't edit the numbers.
    public partial class SkillGameForm
    {
        private readonly AuditStore _auditStore = new();
        internal AuditStore Audits => _auditStore;

        private readonly Dictionary<string, Label> _auditHitLabels = new();
        private Label _auditGames = null!, _auditTime = null!, _auditWinners = null!, _auditTotalHits = null!;
        private readonly List<Label> _hiScoreLabels = new();

        // arcade palette
        private static readonly Color CharBg = Color.FromArgb(24, 24, 30);
        private static readonly Color Gold = Color.FromArgb(231, 169, 29);
        private static readonly Color Cyan = Color.FromArgb(0, 200, 255);
        private static readonly Color Green = Color.FromArgb(0, 220, 140);
        private static readonly Color Red = Color.FromArgb(210, 70, 60);
        private static readonly Color Dim = Color.FromArgb(150, 150, 160);

        private void BuildAuditsUI()
        {
            var tab = auditsTab;

            tab.Controls.Add(Lbl("SYSTEM AUDITS", 30, 12, 24, Gold, true));

            _auditGames     = StatCard(tab, "GAMES PLAYED", Green, 30,   72);
            _auditTime      = StatCard(tab, "TIME PLAYED",  Cyan,  430,  72);
            _auditWinners   = StatCard(tab, "WINNERS",      Gold,  830,  72);
            _auditTotalHits = StatCard(tab, "TOTAL HITS",   Red,   1230, 72);
            _tips.SetToolTip(_auditGames, "Total games played over the machine's lifetime. This counter is permanent - it is never reset.");
            _tips.SetToolTip(_auditTime, "Total time spent in active games, all games added together.");
            _tips.SetToolTip(_auditWinners, "How many games ended with a WINNER (a top-row / Row-8 hole was hit).");
            _tips.SetToolTip(_auditTotalHits, "Total scoring-hole hits counted across every game.");

            tab.Controls.Add(Lbl("SCORE-SWITCH HITS", 30, 250, 16, Cyan, true));

            int y = 302;
            for (int lvl = 1; lvl <= 8; lvl++)
            {
                tab.Controls.Add(Lbl($"LEVEL {lvl}", 30, y + 14, 14, Gold, true));
                int x = 190;
                foreach (var sw in SwitchMap.All.Where(s => s.Kind == SwitchKind.Score && s.Level == lvl))
                {
                    SwitchChip(tab, sw, x, y);
                    x += 250;
                }
                y += 72;
            }

            // right column: high-score table
            tab.Controls.Add(Lbl("HIGH SCORES", 1210, 250, 16, Cyan, true));
            var hs = BorderedPanel(1210, 302, 495, 430, Cyan);
            tab.Controls.Add(hs);
            for (int i = 0; i < AuditStore.MaxHighScores; i++)
            {
                var row = new Label
                {
                    Text = "", AutoSize = false, ForeColor = Color.White, BackColor = CharBg,
                    Font = new Font("Seven Segment", 15F, FontStyle.Bold),
                    Location = new Point(16, 14 + i * 40), Size = new Size(463, 34),
                    TextAlign = ContentAlignment.MiddleLeft,
                };
                hs.Controls.Add(row);
                _hiScoreLabels.Add(row);
            }

            // service buttons
            AddServiceButton(tab, "RESET", 1210, 748, () =>
            {
                if (MessageBox.Show("Erase audits and high scores? (Total games played is kept.) This cannot be undone.",
                        "Reset Audits", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                { _auditStore.Reset(); RefreshAudits(); }
            }, "Erase the audits and high scores. Total Games Played is always kept.");
            AddServiceButton(tab, "EXPORT", 1332, 748, () =>
            {
                string path = _auditStore.ExportCsv();
                MessageBox.Show(string.IsNullOrEmpty(path) ? "Export failed (see log)." : "Exported to:\n" + path, "Export Audits");
            }, "Write all audit counters and high scores to a CSV file next to the game.");

            tab.Controls.Add(Lbl(
                "Read-only · saved automatically to skillgame_audits.json beside the game",
                30, y + 8, 10, Dim, false));

            tabControl1.SelectedIndexChanged += (s, e) => { if (tabControl1.SelectedTab == auditsTab) RefreshAudits(); };

            RefreshAudits();
        }

        private void RefreshAudits()
        {
            if (_auditGames is null) return;
            var d = _auditStore.Data;
            _auditGames.Text     = d.GamesPlayed.ToString();
            _auditTime.Text      = FormatDuration(d.TotalPlaySeconds);
            _auditWinners.Text   = d.Winners.ToString();
            _auditTotalHits.Text = _auditStore.TotalHits().ToString();
            foreach (var kv in _auditHitLabels)
            {
                kv.Value.Text = _auditStore.HitsFor(kv.Key).ToString();
                // switch health: dim any scoring switch not exercised this session
                kv.Value.ForeColor = _auditStore.SessionHitsFor(kv.Key) > 0 ? Green : Color.FromArgb(90, 90, 100);
            }
            var hi = _auditStore.Data.HighScores;
            for (int i = 0; i < _hiScoreLabels.Count; i++)
                _hiScoreLabels[i].Text = i < hi.Count ? $"{i + 1,2}.   {hi[i].Score,7}    {hi[i].When}" : $"{i + 1,2}.   ---";
        }

        private static string FormatDuration(long seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
            return $"{t.Minutes}m {t.Seconds}s";
        }

        // ---- tiny UI builders ----
        private static Label Lbl(string s, int x, int y, float size, Color color, bool bold)
        {
            return new Label
            {
                Text = s,
                AutoSize = true,
                ForeColor = color,
                BackColor = Color.Transparent,
                Font = new Font("Seven Segment", size, bold ? FontStyle.Bold : FontStyle.Regular),
                Location = new Point(x, y),
            };
        }

        private static Panel BorderedPanel(int x, int y, int w, int h, Color border)
        {
            var p = new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = CharBg };
            p.Paint += (s, e) =>
            {
                using var pen = new Pen(border, 2);
                e.Graphics.DrawRectangle(pen, 1, 1, p.Width - 3, p.Height - 3);
            };
            return p;
        }

        private static Label StatCard(Control parent, string caption, Color accent, int x, int y)
        {
            var card = BorderedPanel(x, y, 360, 150, accent);
            card.Controls.Add(new Label
            {
                Text = caption, AutoSize = true, ForeColor = accent, BackColor = CharBg,
                Font = new Font("Seven Segment", 13F, FontStyle.Bold), Location = new Point(16, 14),
            });
            var value = new Label
            {
                Text = "0", AutoSize = false, ForeColor = Color.White, BackColor = CharBg,
                Font = new Font("Seven Segment", 32F, FontStyle.Bold),
                Location = new Point(16, 58), Size = new Size(328, 78),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            card.Controls.Add(value);
            parent.Controls.Add(card);
            return value;
        }

        private void SwitchChip(Control parent, SwitchDef sw, int x, int y)
        {
            var chip = BorderedPanel(x, y, 235, 60, Color.FromArgb(72, 72, 82));
            chip.Controls.Add(new Label
            {
                Text = $"{sw.Id} · {sw.Points} PTS", AutoSize = true, ForeColor = Dim, BackColor = CharBg,
                Font = new Font("Seven Segment", 11F, FontStyle.Regular), Location = new Point(12, 8),
            });
            var count = new Label
            {
                Text = "0", AutoSize = false, ForeColor = Green, BackColor = CharBg,
                Font = new Font("Seven Segment", 17F, FontStyle.Bold),
                Location = new Point(12, 30), Size = new Size(211, 26),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            chip.Controls.Add(count);
            parent.Controls.Add(chip);
            _auditHitLabels[sw.Id] = count;
        }

        private GlassButton _demoButton = null!;

        // DEMO on the Game Status screen: kicks off a full self-test game (no coin / no playfield) that
        // you watch play out on the scoreboard - score climbs, levels advance, lamps and sounds fire.
        // Toggles: click to start, click again (STOP DEMO) to cancel and reset the game to idle.
        private void BuildGameStatusExtras()
        {
            gameStatusTab.Controls.Add(Lbl("SELF-TEST", 1500, 782, 9, Dim, false));
            _demoButton = new GlassButton
            {
                Text = "DEMO GAME", Location = new Point(1500, 806), Size = new Size(200, 76),
                BackColor = CharBg, ForeColor = Gold, FlatStyle = FlatStyle.Flat,
                Font = new Font("Seven Segment", 13F, FontStyle.Bold), UseVisualStyleBackColor = false,
            };
            _demoButton.FlatAppearance.BorderColor = Gold;
            _demoButton.FlatAppearance.BorderSize = 1;
            _demoButton.Click += (s, e) => ToggleDemo();
            _tips.SetToolTip(_demoButton, "Play a full self-test game (no coin or playfield) - watch it on the scoreboard. Click again to stop and reset.");
            gameStatusTab.Controls.Add(_demoButton);
        }

        private void ToggleDemo()
        {
            if (gpio == null || !gpio.Ready) return;   // needs hardware; no-ops without it
            if (gpio.DemoRunning)
            {
                gpio.StopDemo();
                ShowStatus(0, 0, "None", "Game Not Started");   // reset the scoreboard
                SetDemoButton(false);
            }
            else
            {
                gpio.RunDemo();
                SetDemoButton(true);
            }
        }

        private void SetDemoButton(bool running)
        {
            if (_demoButton == null) return;
            if (_demoButton.InvokeRequired) { _demoButton.BeginInvoke((MethodInvoker)(() => SetDemoButton(running))); return; }
            _demoButton.Text = running ? "STOP DEMO" : "DEMO GAME";
            _demoButton.ForeColor = running ? Red : Gold;
        }

        /// <summary>Called by GPIOFunctions when a demo finishes (naturally or cancelled).</summary>
        public void OnDemoFinished() => SetDemoButton(false);

        // BULB TEST lives with the lamps on the Diagnostics tab (it's a hardware test, not an audit):
        // it walks every score lamp on-then-off, one at a time, so you can watch each bulb + driver.
        // Placed in the empty column under the "90" button, inside the LAMPS group.
        private void BuildBulbTestButton()
        {
            var b = new GlassButton
            {
                Text = "BULB TEST", Location = new Point(250, 122), Size = new Size(138, 104),
                BackColor = CharBg, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
                Font = new Font("Seven Segment", 10F, FontStyle.Bold), UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderColor = Cyan;
            b.FlatAppearance.BorderSize = 1;
            b.Click += async (s, e) =>
            {
                if (gpio == null) return;
                b.Enabled = false;
                b.Text = "TESTING";
                b.ForeColor = Green;                 // lit while the walk runs
                await gpio.LampBurnIn();
                b.Text = "BULB TEST";
                b.ForeColor = Color.White;
                b.Enabled = true;
            };
            _tips.SetToolTip(b, "Light every lamp one at a time (10..400, Winner, Game Over, Tilt) to check each bulb and its driver.");
            scoreLightsGroupBox.Controls.Add(b);
        }

        private void AddServiceButton(Control parent, string text, int x, int y, System.Action onClick, string tip = "")
        {
            var b = new GlassButton
            {
                Text = text, Location = new Point(x, y), Size = new Size(112, 56),
                BackColor = CharBg, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
                Font = new Font("Seven Segment", 10F, FontStyle.Bold), UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderColor = Gold;
            b.FlatAppearance.BorderSize = 1;
            b.Click += (s, e) => onClick();
            if (tip.Length > 0) _tips.SetToolTip(b, tip);
            parent.Controls.Add(b);
        }
    }
}
