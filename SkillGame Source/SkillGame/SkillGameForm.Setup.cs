using System;
using System.Drawing;
using System.Windows.Forms;

namespace SkillGame
{
    // "OPERATOR SETUP" panel on the Settings tab: the tunable knobs, saved to operator_settings.json.
    // Every row has a hover tooltip explaining what it does.
    public partial class SkillGameForm
    {
        private readonly OperatorSettings _opSettings = OperatorSettings.Load();
        internal OperatorSettings OpSettings => _opSettings;

        private Label _setCountUp = null!, _setAttractLight = null!, _setAttractSound = null!,
                      _setStart = null!, _setEnd = null!, _setVol = null!, _setSaved = null!;
        private GlassButton _setTilt = null!, _setQuiet = null!;

        private void BuildOperatorSetupUI()
        {
            var tab = settingsTab;
            tab.Controls.Add(Lbl("OPERATOR SETUP", 1040, 18, 20, Gold, true));

            var p = BorderedPanel(1040, 80, 662, 612, Gold);
            tab.Controls.Add(p);

            TipCap(p, "COUNT-UP SPEED", 22, Cyan,
                "How fast the score lamps 'count up' to a new total after a hit - milliseconds per step. Lower = faster sweep.");
            _setCountUp = ValueLabel(p, 322, 22);
            Adjuster(p, 470, 22, Cyan, () => _opSettings.CountUpStepMs -= 5, () => _opSettings.CountUpStepMs += 5,
                "Score count-up sweep speed (ms per step).");

            TipCap(p, "ATTRACT LIGHT", 88, Cyan,
                "When no game is being played, the LED strip plays an idle 'attract' show. This is how many seconds between shows.");
            _setAttractLight = ValueLabel(p, 322, 88);
            Adjuster(p, 470, 88, Cyan, () => _opSettings.AttractLightSeconds -= 10, () => _opSettings.AttractLightSeconds += 10,
                "Seconds between idle attract light shows.");

            TipCap(p, "ATTRACT SOUND", 154, Cyan,
                "When no game is being played, the machine plays an idle 'attract' sound. This is how many seconds between plays.");
            _setAttractSound = ValueLabel(p, 322, 154);
            Adjuster(p, 470, 154, Cyan, () => _opSettings.AttractSoundSeconds -= 10, () => _opSettings.AttractSoundSeconds += 10,
                "Seconds between idle attract sounds.");

            TipCap(p, "TILT SWITCH", 220, Cyan,
                "Turn the tilt switch on or off. OFF ignores a finicky tilt so games don't end early.");
            _setTilt = MiniButton("ON", 322, 212, 150, 48, Green, (s, e) => Adjust(() => _opSettings.TiltEnabled = !_opSettings.TiltEnabled),
                "Turn the tilt switch on or off.");
            p.Controls.Add(_setTilt);

            TipCap(p, "QUIET HOURS", 292, Cyan,
                "Cap the master volume overnight (between the Start and End hours below) so the cabinet isn't loud after hours.");
            _setQuiet = MiniButton("OFF", 322, 284, 150, 48, Green, (s, e) => Adjust(() => _opSettings.QuietHoursEnabled = !_opSettings.QuietHoursEnabled),
                "Enable / disable the overnight volume cap.");
            p.Controls.Add(_setQuiet);

            TipCap(p, "   START HOUR", 360, Dim, "When the overnight quiet period begins.");
            _setStart = ValueLabel(p, 322, 360);
            Adjuster(p, 470, 360, Dim, () => _opSettings.QuietStartHour = (_opSettings.QuietStartHour + 23) % 24,
                () => _opSettings.QuietStartHour = (_opSettings.QuietStartHour + 1) % 24, "Quiet-hours start time.");

            TipCap(p, "   END HOUR", 420, Dim, "When the overnight quiet period ends.");
            _setEnd = ValueLabel(p, 322, 420);
            Adjuster(p, 470, 420, Dim, () => _opSettings.QuietEndHour = (_opSettings.QuietEndHour + 23) % 24,
                () => _opSettings.QuietEndHour = (_opSettings.QuietEndHour + 1) % 24, "Quiet-hours end time.");

            TipCap(p, "   VOLUME CAP", 480, Dim, "The master-volume ceiling (%) while quiet hours are active.");
            _setVol = ValueLabel(p, 322, 480);
            Adjuster(p, 470, 480, Dim, () => _opSettings.QuietVolumePercent -= 5, () => _opSettings.QuietVolumePercent += 5,
                "Volume ceiling (%) during quiet hours.");

            var save = MiniButton("SAVE", 22, 544, 220, 56, Gold, (s, e) => SaveOperatorSettings(),
                "Save these settings to disk and apply them now (no restart needed).");
            save.ForeColor = Gold;
            save.Font = new Font("Seven Segment", 14F, FontStyle.Bold);
            p.Controls.Add(save);
            _setSaved = Lbl("", 262, 560, 12, Green, true);
            p.Controls.Add(_setSaved);

            RefreshSetupValues();
        }

        // caption label with a tooltip
        private Label TipCap(Control parent, string text, int y, Color color, string tip)
        {
            var l = Lbl(text, 22, y, 12, color, true);
            parent.Controls.Add(l);
            _tips.SetToolTip(l, tip);
            return l;
        }

        // a "-" / "+" pair
        private void Adjuster(Control parent, int x, int y, Color border, Action minus, Action plus, string tip)
        {
            parent.Controls.Add(MiniButton("-", x, y - 4, 64, 48, border, (s, e) => Adjust(minus), tip));
            parent.Controls.Add(MiniButton("+", x + 72, y - 4, 64, 48, border, (s, e) => Adjust(plus), tip));
        }

        private void Adjust(Action change)
        {
            change();
            _opSettings.Clamp();
            RefreshSetupValues();
        }

        private void RefreshSetupValues()
        {
            if (_setCountUp == null) return;
            _setCountUp.Text = _opSettings.CountUpStepMs + " ms";
            _setAttractLight.Text = _opSettings.AttractLightSeconds + " s";
            _setAttractSound.Text = _opSettings.AttractSoundSeconds + " s";
            _setTilt.Text = _opSettings.TiltEnabled ? "ON" : "OFF";
            _setTilt.ForeColor = _opSettings.TiltEnabled ? Green : Red;
            _setQuiet.Text = _opSettings.QuietHoursEnabled ? "ON" : "OFF";
            _setQuiet.ForeColor = _opSettings.QuietHoursEnabled ? Green : Red;
            _setStart.Text = Hour12(_opSettings.QuietStartHour);
            _setEnd.Text = Hour12(_opSettings.QuietEndHour);
            _setVol.Text = _opSettings.QuietVolumePercent + "%";
            _setSaved.Text = "";
        }

        private void SaveOperatorSettings()
        {
            _opSettings.Clamp();
            _opSettings.Save();
            _opSettings.ApplyToQuietHours();
            ApplyAttractIntervals();
            gpio?.ApplyOperatorSettings(_opSettings);
            _setSaved.Text = "SAVED";
        }

        /// <summary>Push the attract intervals into the idle timers (also called at startup).</summary>
        private void ApplyAttractIntervals()
        {
            attractTimer.Interval = Math.Max(1000, _opSettings.AttractLightSeconds * 1000);
            attractSoundTimer.Interval = Math.Max(1000, _opSettings.AttractSoundSeconds * 1000);
        }

        private static Label ValueLabel(Control parent, int x, int y)
        {
            var l = new Label
            {
                Text = "", AutoSize = false, ForeColor = Color.White, BackColor = CharBg,
                Font = new Font("Seven Segment", 15F, FontStyle.Bold),
                Location = new Point(x, y), Size = new Size(140, 44),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            parent.Controls.Add(l);
            return l;
        }

        private GlassButton MiniButton(string text, int x, int y, int w, int h, Color border, EventHandler onClick, string tip = "")
        {
            var b = new GlassButton
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h),
                BackColor = CharBg, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
                Font = new Font("Seven Segment", 12F, FontStyle.Bold), UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderColor = border;
            b.FlatAppearance.BorderSize = 1;
            b.Click += onClick;
            if (tip.Length > 0) _tips.SetToolTip(b, tip);
            return b;
        }

        // 0-23 hour -> 12-hour clock label, e.g. 22 -> "10 PM", 8 -> "8 AM", 0 -> "12 AM".
        private static string Hour12(int h)
        {
            int display = h % 12;
            if (display == 0) display = 12;
            return display + (h < 12 ? " AM" : " PM");
        }
    }
}
