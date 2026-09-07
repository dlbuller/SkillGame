using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SkillGame
{
    /// <summary>
    /// Guided wiring test. Walks the switch matrix one switch at a time ("Activate: S1 - 10 Points"),
    /// and as you trip each playfield switch it confirms the switch is wired to the pin the software
    /// expects. If a different switch fires it flags a possible miswire (and tells you which one it saw).
    /// SKIP marks the current one untested and moves on. Takes exclusive use of the switches while open
    /// (via GPIOFunctions.SetSwitchObserver) and hands them back when closed.
    /// </summary>
    public class WiringTestForm : Form
    {
        private static readonly Color Bg = Color.FromArgb(24, 24, 30);
        private static readonly Color Gold = Color.FromArgb(231, 169, 29);
        private static readonly Color Green = Color.FromArgb(0, 220, 140);
        private static readonly Color Red = Color.FromArgb(210, 70, 60);
        private static readonly Color Cyan = Color.FromArgb(0, 200, 255);

        private readonly GPIOFunctions? _gpio;
        private readonly SwitchDef[] _seq = SwitchMap.All;
        private readonly Dictionary<string, string> _result = new();   // switch id -> OK / SKIP
        private int _i;

        private readonly Label _prompt = Row(30, 34, "Seven Segment", 20F, Gold, true);
        private readonly Label _progress = Row(30, 90, "Seven Segment", 14F, Cyan, false);
        private readonly Label _hint = Row(30, 128, "Segoe UI", 11F, Color.Silver, false);
        private readonly ListBox _list = new();

        public WiringTestForm(GPIOFunctions? gpio)
        {
            _gpio = gpio;
            Text = "Wiring Test";
            Size = new Size(760, 660);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Bg;

            _prompt.AutoSize = false; _prompt.Size = new Size(700, 56);
            _progress.AutoSize = false; _progress.Size = new Size(700, 30);
            _hint.AutoSize = false; _hint.Size = new Size(700, 30);
            Controls.Add(_prompt);
            Controls.Add(_progress);
            Controls.Add(_hint);

            _list.Location = new Point(30, 170);
            _list.Size = new Size(700, 360);
            _list.BackColor = Color.Black;
            _list.ForeColor = Color.White;
            _list.Font = new Font("Consolas", 11F);
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.SelectionMode = SelectionMode.None;
            Controls.Add(_list);

            Controls.Add(MakeButton("SKIP", 30, 548, 150, 52, Cyan, (s, e) => { Mark("SKIP"); Advance(); }));
            Controls.Add(MakeButton("RESTART", 200, 548, 170, 52, Cyan, (s, e) => { _i = 0; _result.Clear(); _hint.Text = ""; ShowCurrent(); }));
            Controls.Add(MakeButton("CLOSE", 560, 548, 170, 52, Gold, (s, e) => Close()));

            if (_gpio == null)
                _hint.Text = "No hardware - connect boards & restart.";
            else
                _gpio.SetSwitchObserver(OnObservedSwitch);

            ShowCurrent();
        }

        // Called from the polling thread -> marshal onto the UI thread.
        private void OnObservedSwitch(SwitchDef def, bool high)
        {
            if (!high || IsDisposed) return;
            try { BeginInvoke((MethodInvoker)(() => HandleSwitch(def))); } catch { }
        }

        private void HandleSwitch(SwitchDef def)
        {
            if (_i >= _seq.Length) return;
            var expected = _seq[_i];
            if (def.Id == expected.Id)
            {
                _hint.Text = "";
                Mark("OK");
                Advance();
            }
            else
            {
                _hint.Text = $"Saw {def.Id} ({def.Label}) - expected {expected.Id}. Possible miswire, or you tripped the wrong switch.";
            }
        }

        private void Mark(string status)
        {
            if (_i < _seq.Length) _result[_seq[_i].Id] = status;
        }

        private void Advance()
        {
            _i++;
            ShowCurrent();
        }

        private void ShowCurrent()
        {
            RebuildList();
            if (_i >= _seq.Length)
            {
                int ok = _result.Values.Count(v => v == "OK");
                _prompt.Text = "DONE";
                _progress.Text = $"{ok} of {_seq.Length} confirmed" + (ok < _seq.Length ? $"  ({_seq.Length - ok} skipped)" : " - all good!");
                _prompt.ForeColor = ok == _seq.Length ? Green : Gold;
                return;
            }
            var d = _seq[_i];
            _prompt.ForeColor = Gold;
            _prompt.Text = "Activate:  " + d.Label;
            _progress.Text = $"Switch {_i + 1} of {_seq.Length}   (board GPIO{d.Board}, pin {d.Pin})";
        }

        private void RebuildList()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            for (int k = 0; k < _seq.Length; k++)
            {
                var d = _seq[k];
                string tag = _result.TryGetValue(d.Id, out var r) ? r : (k == _i ? "<-- now" : "");
                _list.Items.Add($"{d.Id,-4} {d.Label,-20} {tag}");
            }
            _list.EndUpdate();
        }

        private static Label Row(int x, int y, string font, float size, Color color, bool bold)
            => new()
            {
                Location = new Point(x, y),
                ForeColor = color,
                BackColor = Color.Transparent,
                Font = new Font(font, size, bold ? FontStyle.Bold : FontStyle.Regular),
            };

        private static Button MakeButton(string text, int x, int y, int w, int h, Color border, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(34, 34, 40), ForeColor = Color.White,
                Font = new Font("Seven Segment", 12F, FontStyle.Bold), UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderColor = border;
            b.Click += onClick;
            return b;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _gpio?.SetSwitchObserver(null);   // hand the switches back to the game
            base.OnFormClosed(e);
        }
    }
}
