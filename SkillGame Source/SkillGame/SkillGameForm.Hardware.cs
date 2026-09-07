using System;
using System.Drawing;
using System.Windows.Forms;

namespace SkillGame
{
    // "Hardware Status" strip along the bottom of the Diagnostics tab. Shows the four FT232H USB
    // boards (GPIO1-GPIO4). A board that answers is GREEN; one that is missing or unplugged goes RED.
    // A 4-second timer re-checks while the Diagnostics tab is open, so pulling a USB cable shows up
    // on its own without restarting the game. Built in code (not the Designer) so it can share the
    // same little arcade-styled builders (Lbl / BorderedPanel / colors) used by the Audits page.
    public partial class SkillGameForm
    {
        private Label[] _boardLabels = null!;
        private Label _boardSummary = null!;
        private Label _postLabel = null!;
        private System.Windows.Forms.Timer _boardTimer = null!;
        private IoMapForm? _ioMap;

        private GlassButton HeaderButton(string text, int x, int w, float fontSize, EventHandler onClick, string tip = "")
        {
            var b = new GlassButton
            {
                Text = text, Location = new Point(x, 854), Size = new Size(w, 34),
                BackColor = CharBg, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
                Font = new Font("Seven Segment", fontSize, FontStyle.Bold), UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderColor = Cyan;
            b.FlatAppearance.BorderSize = 1;
            b.Click += onClick;
            if (tip.Length > 0) _tips.SetToolTip(b, tip);
            return b;
        }

        // Open the live I/O map as a modeless, always-on-top window so it stays visible while you push
        // buttons on the Diagnostics tab. Only one instance at a time.
        private void OpenIoMap()
        {
            if (_ioMap == null || _ioMap.IsDisposed)
            {
                _ioMap = new IoMapForm();
                _ioMap.FormClosed += (s, e) => _ioMap = null;
                _ioMap.Show(this);
            }
            else _ioMap.BringToFront();
        }

        private void BuildHardwareStatusUI()
        {
            var tab = diagnosticsTab;

            tab.Controls.Add(Lbl("HARDWARE STATUS", 22, 858, 14, Gold, true));

            // power-on self-test result (set by GPIOFunctions.RunPowerOnSelfTest at startup)
            _postLabel = Lbl("POST: -", 330, 860, 11, Dim, true);
            tab.Controls.Add(_postLabel);

            // service tools: on-screen log, guided wiring test, live I/O activity map
            tab.Controls.Add(HeaderButton("VIEW LOG", 500, 116, 10F,
                (s, e) => { using var f = new LogViewerForm(); f.ShowDialog(this); },
                "Show the tail of skillgame.log (recent events + errors) on screen."));
            tab.Controls.Add(HeaderButton("WIRING TEST", 622, 156, 9F,
                (s, e) => { using var f = new WiringTestForm(gpio != null && gpio.Ready ? gpio : null); f.ShowDialog(this); },
                "Step through every switch and confirm it's wired to the pin the software expects."));
            tab.Controls.Add(HeaderButton("I/O MAP", 784, 150, 10F, (s, e) => OpenIoMap(),
                "Open a floating live map that lights each FT232H pin as inputs fire / outputs energize."));

            var panel = BorderedPanel(20, 892, 972, 84, Gold);
            tab.Controls.Add(panel);

            _boardLabels = new Label[4];
            const int chipW = 178, gap = 12, x0 = 14;
            for (int i = 0; i < 4; i++)
            {
                var chip = BorderedPanel(x0 + i * (chipW + gap), 12, chipW, 58, Color.FromArgb(72, 72, 82));
                var lab = new Label
                {
                    Text = "GPIO" + (i + 1),
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    ForeColor = Color.FromArgb(120, 120, 130),
                    BackColor = CharBg,
                    Font = new Font("Seven Segment", 16F, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleCenter,
                };
                chip.Controls.Add(lab);
                panel.Controls.Add(chip);
                _boardLabels[i] = lab;
            }

            _boardSummary = new Label
            {
                Text = "",
                AutoSize = false,
                ForeColor = Dim,
                BackColor = Color.Transparent,
                Font = new Font("Seven Segment", 12F, FontStyle.Bold),
                Location = new Point(x0 + 4 * (chipW + gap) + 10, 12),
                Size = new Size(190, 58),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            panel.Controls.Add(_boardSummary);

            // re-check whenever the Diagnostics tab is shown, and on a slow timer while it's open
            tabControl1.SelectedIndexChanged += (s, e) => { if (tabControl1.SelectedTab == diagnosticsTab) UpdateBoardStatus(); };
            _boardTimer = new System.Windows.Forms.Timer { Interval = 4000 };
            _boardTimer.Tick += (s, e) => { if (tabControl1.SelectedTab == diagnosticsTab) UpdateBoardStatus(); };
            _boardTimer.Start();

            UpdateBoardStatus();
        }

        private void UpdateBoardStatus()
        {
            if (_boardLabels == null) return;
            bool[]? present = gpio?.GetBoardPresence();
            int missing = 0;
            for (int i = 0; i < 4; i++)
            {
                if (present == null) { _boardLabels[i].ForeColor = Color.FromArgb(120, 120, 130); continue; }
                bool ok = present[i];
                if (!ok) missing++;
                _boardLabels[i].ForeColor = ok ? Green : Red;   // only a missing board is red
            }
            if (_boardSummary != null)
            {
                if (present == null) { _boardSummary.Text = "- - -"; _boardSummary.ForeColor = Dim; }
                else if (missing == 0) { _boardSummary.Text = "ALL OK"; _boardSummary.ForeColor = Green; }
                else { _boardSummary.Text = missing + " MISSING"; _boardSummary.ForeColor = Red; }
            }
        }

        /// <summary>Show the power-on self-test outcome (called from the init thread, so marshal to UI).</summary>
        public void ShowPostResult(string text, bool ok)
        {
            if (_postLabel == null) return;
            if (InvokeRequired) { BeginInvoke((MethodInvoker)(() => ShowPostResult(text, ok))); return; }
            _postLabel.Text = text;
            _postLabel.ForeColor = ok ? Green : Red;
        }
    }
}
