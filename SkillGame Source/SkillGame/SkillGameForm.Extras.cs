using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace SkillGame
{
    // Game Status live readouts (clock / high score / session / quiet), the Settings Machine Info panel
    // and the SKILLGAME banner, and per-section colour coding of the diagnostics buttons.
    public partial class SkillGameForm
    {
        private readonly DateTime _launchTime = DateTime.Now;

        private Label _clockTime = null!, _clockDate = null!, _hiScoreVal = null!,
                      _sessionVal = null!, _quietVal = null!, _uptimeVal = null!;
        private System.Windows.Forms.Timer _extrasTimer = null!;

        // mid-century Bally accents
        private static readonly Color Amber = Color.FromArgb(240, 170, 30);
        private static readonly Color Purple = Color.FromArgb(180, 90, 220);
        private static readonly Color Teal = Color.FromArgb(20, 150, 135);
        private static readonly Color Walnut = Color.FromArgb(46, 30, 20);
        private static readonly Color Cream = Color.FromArgb(232, 217, 176);

        // ---------------------------------------------------------------- Game Status readouts

        private void BuildGameStatusInfo()
        {
            var tab = gameStatusTab;

            // Clock (top-right corner)
            _clockTime = Lbl("", 1440, 8, 22, Cyan, true);
            _clockTime.AutoSize = false; _clockTime.Size = new Size(270, 44); _clockTime.TextAlign = ContentAlignment.MiddleRight;
            tab.Controls.Add(_clockTime);
            _clockDate = Lbl("", 1440, 52, 12, Dim, false);
            _clockDate.AutoSize = false; _clockDate.Size = new Size(270, 24); _clockDate.TextAlign = ContentAlignment.MiddleRight;
            tab.Controls.Add(_clockDate);

            // Framed "STATUS" dashboard tile in the band below the groups (matches the other framed sections)
            var sp = BorderedPanel(600, 782, 852, 106, Gold);
            tab.Controls.Add(sp);

            sp.Controls.Add(Lbl("HIGH SCORE", 24, 12, 13, Gold, true));
            _hiScoreVal = Lbl("---", 24, 44, 26, Amber, true);
            _hiScoreVal.AutoSize = false; _hiScoreVal.Size = new Size(280, 48);
            sp.Controls.Add(_hiScoreVal);

            sp.Controls.Add(Lbl("SESSION", 322, 12, 13, Gold, true));
            _sessionVal = Lbl("0 GAMES", 322, 50, 15, Color.White, true);
            _sessionVal.AutoSize = false; _sessionVal.Size = new Size(310, 36);
            sp.Controls.Add(_sessionVal);

            sp.Controls.Add(Lbl("QUIET", 664, 12, 13, Gold, true));
            _quietVal = Lbl("OFF", 664, 44, 22, Dim, true);
            _quietVal.AutoSize = false; _quietVal.Size = new Size(170, 44);
            sp.Controls.Add(_quietVal);
        }

        // ---------------------------------------------------------------- Settings (Machine Info, below the boxes)

        private void BuildSettingsInfo()
        {
            var tab = settingsTab;
            tab.Controls.Add(Lbl("MACHINE INFO", 30, 650, 15, Gold, true));   // below the volume/sound/lighting boxes (which end ~643)
            var p = BorderedPanel(30, 690, 660, 288, Teal);
            tab.Controls.Add(p);

            string cfg = Path.Combine(AppContext.BaseDirectory, "operator_settings.json");
            _uptimeVal = InfoRow(p, "UPTIME", "", 14);
            InfoRow(p, "VERSION", SafeVersion(), 48);
            InfoRow(p, "SOUNDS", @"C:\SkillGame\Sounds", 86, path: true);
            InfoRow(p, "LOG", Log.FilePath, 120, path: true);
            InfoRow(p, "AUDITS", _auditStore.FileLocation, 154, path: true);
            InfoRow(p, "CONFIG", cfg, 188, path: true);

            var backup = MiniButton("BACKUP AUDITS", 18, 224, 250, 52, Gold, (s, e) =>
            {
                string dest = _auditStore.Backup();
                MessageBox.Show(string.IsNullOrEmpty(dest) ? "Backup failed (see log)." : "Audits backed up to:\n" + dest, "Backup Audits");
            }, "Write a datestamped copy of the audit file next to the game (a safety copy).");
            p.Controls.Add(backup);
        }

        private static Label InfoRow(Control parent, string caption, string value, int y, bool path = false)
        {
            parent.Controls.Add(new Label
            {
                Text = caption, AutoSize = true, ForeColor = Cyan, BackColor = Color.Transparent,
                Font = new Font("Seven Segment", 11F, FontStyle.Bold), Location = new Point(18, y),
            });
            var val = new Label
            {
                Text = value, AutoSize = false, ForeColor = Color.White, BackColor = Color.Transparent,
                Font = new Font(path ? "Consolas" : "Seven Segment", path ? 9.5F : 13F, FontStyle.Regular),
                Location = new Point(180, path ? y + 2 : y), Size = new Size(464, 26),
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
            };
            parent.Controls.Add(val);
            return val;
        }

        private static string SafeVersion()
        {
            try { return Application.ProductVersion.Split('+')[0]; } catch { return "1.0"; }
        }

        // ---------------------------------------------------------------- SKILLGAME banner (Settings)

        private void BuildBranding()
        {
            // Bottom band of the Settings tab, clear of the boxes (end ~643), Machine Info (x<690) and
            // the OPERATOR SETUP panel (ends ~692).
            AddMarquee(settingsTab, 700, 700, 1000, 272);
        }

        private void AddMarquee(Control parent, int x, int y, int w, int h)
        {
            var banner = new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = Walnut };
            banner.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                int bw = banner.Width, bh = banner.Height;
                using (var pen = new Pen(Gold, 3)) g.DrawRectangle(pen, 5, 5, bw - 11, bh - 11);
                using (var pen = new Pen(Color.FromArgb(110, Gold.R, Gold.G, Gold.B), 1)) g.DrawRectangle(pen, 13, 13, bw - 27, bh - 27);
                using (var bulb = new SolidBrush(Color.FromArgb(200, Amber.R, Amber.G, Amber.B)))
                    for (int bx = 30; bx < bw - 24; bx += 34) g.FillEllipse(bulb, bx, 20, 7, 7);
                DrawFit(g, "SKILLGAME", "Bahnschrift", FontStyle.Bold, Gold, bw, bw * 0.80f, 64f, bh * 0.14f);
                DrawFit(g, "A Bally Skill-Roll recreation", "Bahnschrift", FontStyle.Regular, Cream, bw, bw * 0.70f, 20f, bh * 0.72f);
            };
            parent.Controls.Add(banner);
        }

        private static void DrawFit(Graphics g, string text, string family, FontStyle style, Color color, float areaW, float maxW, float startPt, float cy)
        {
            float pt = startPt;
            Font f = new Font(family, pt, style);
            while (pt > 7f && g.MeasureString(text, f).Width > maxW) { f.Dispose(); pt -= 2f; f = new Font(family, pt, style); }
            var size = g.MeasureString(text, f);
            using var b = new SolidBrush(color);
            g.DrawString(text, f, b, (areaW - size.Width) / 2f, cy);
            f.Dispose();
        }

        // ---------------------------------------------------------------- per-section button colours

        private void ApplySectionColors()
        {
            SetAccents(Amber, "scoreLamp10Button", "scoreLamp20Button", "scoreLamp30Button", "scoreLamp40Button",
                "scoreLamp50Button", "scoreLamp60Button", "scoreLamp70Button", "scoreLamp80Button", "scoreLamp90Button",
                "scoreLamp100Button", "scoreLamp200Button", "scoreLamp300Button", "scoreLamp400Button",
                "winnerButton", "gameOverButton", "tiltButton", "allScoreLampsButton", "lampPatternButton");
            SetAccents(Cyan, "controlledLEDsButton", "redLEDButton", "greenLEDButton", "blueLEDButton",
                "yellowLEDButton", "purpleLEDButton", "whiteLEDButton", "brightnessUpButton", "brightnessDownButton", "clearAllLEDsButton");
            SetAccents(Purple, "diagnosticSoundButton", "soundFXButton");
            SetAccents(Red, "winnerLockButton", "coinLockSolenoid");
        }

        // Draw matching gold frames around the Game Status sections so they read as one set of tiles
        // (their own faint gray GroupBox border is overdrawn by the bolder gold rectangle).
        private void FrameSections()
        {
            GoldFrame(gameGroupBox);
            GoldFrame(currentSettingsGroupBox);
            GoldFrame(groupBox10);   // SOUND
            GoldFrame(groupBox11);   // LIGHTING
        }

        private void GoldFrame(Control g)
        {
            g.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(Gold, 2f);
                e.Graphics.DrawRectangle(pen, 1, 1, g.Width - 3, g.Height - 3);
            };
            g.Invalidate();
        }

        private void SetAccents(Color c, params string[] names)
        {
            foreach (var n in names)
            {
                var found = Controls.Find(n, true);
                if (found.Length > 0 && found[0] is GlassButton gb) gb.AccentColor = c;
            }
        }

        // ---------------------------------------------------------------- live updates

        private void StartExtrasTimer()
        {
            _extrasTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _extrasTimer.Tick += (s, e) => UpdateExtras();
            _extrasTimer.Start();
            UpdateExtras();
        }

        private void UpdateExtras()
        {
            var now = DateTime.Now;
            if (_clockTime != null) _clockTime.Text = now.ToString("h:mm:ss tt");
            if (_clockDate != null) _clockDate.Text = now.ToString("MM/dd/yyyy");
            if (_hiScoreVal != null)
            {
                var hs = _auditStore.Data.HighScores;
                _hiScoreVal.Text = hs != null && hs.Count > 0 ? hs[0].Score.ToString() : "---";
            }
            if (_sessionVal != null)
                _sessionVal.Text = $"{_auditStore.SessionGames} GAMES · {_auditStore.SessionHits} HITS";
            if (_quietVal != null)
            {
                bool q = QuietHours.IsQuietNow(now);
                _quietVal.Text = q ? "ACTIVE" : (QuietHours.Enabled ? "ARMED" : "OFF");
                _quietVal.ForeColor = q ? Gold : Dim;
            }
            if (_uptimeVal != null)
            {
                var up = now - _launchTime;
                _uptimeVal.Text = up.TotalHours >= 1 ? $"{(int)up.TotalHours}h {up.Minutes}m" : $"{up.Minutes}m {up.Seconds}s";
            }
        }
    }
}
