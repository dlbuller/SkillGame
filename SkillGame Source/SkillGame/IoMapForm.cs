using System;
using System.Drawing;
using System.Windows.Forms;

namespace SkillGame
{
    /// <summary>
    /// A floating, always-on-top "I/O Activity Map". Shows all four FT232H boards and their used pins;
    /// as you trip a switch (input) or push a diagnostics button that energizes a lamp/solenoid (output),
    /// the matching pin lights up - green for an active input, amber for an energized output. Modeless, so
    /// it stays visible over the Diagnostics tab while you push buttons. Reads <see cref="PinActivity"/>.
    /// </summary>
    public class IoMapForm : Form
    {
        private static readonly Color Bg = Color.FromArgb(18, 18, 24);
        private static readonly Color Off = Color.FromArgb(48, 48, 56);
        private static readonly Color InputOn = Color.FromArgb(40, 220, 90);
        private static readonly Color OutputOn = Color.FromArgb(240, 170, 30);
        private static readonly Color Gold = Color.FromArgb(231, 169, 29);

        private readonly MapPanel _panel;

        public IoMapForm()
        {
            Text = "I/O Activity Map";
            Size = new Size(1380, 520);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Bg;
            TopMost = true;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowIcon = false;

            _panel = new MapPanel { Dock = DockStyle.Fill };
            Controls.Add(_panel);

            var legend = new Label
            {
                Dock = DockStyle.Bottom, Height = 34, ForeColor = Color.Silver,
                BackColor = Color.FromArgb(24, 24, 30), Font = new Font("Segoe UI", 9.5F),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "GREEN = input active      AMBER = output energized      "
                     + "(push a Diagnostics button, or trip a switch)",
            };
            Controls.Add(legend);

            PinActivity.Changed += OnActivityChanged;
        }

        private void OnActivityChanged()
        {
            if (IsDisposed) return;
            try { BeginInvoke((MethodInvoker)(() => _panel.Invalidate())); } catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            PinActivity.Changed -= OnActivityChanged;
            base.OnFormClosed(e);
        }

        // The grid: one column per board, a lit indicator + pad + function per used pin.
        private sealed class MapPanel : Panel
        {
            public MapPanel() { DoubleBuffered = true; }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Bg);

                const int margin = 16, top = 46, rowH = 26;
                int colW = (Width - margin * 2) / 4;

                using var headF = new Font("Seven Segment", 15F, FontStyle.Bold);
                using var padF = new Font("Consolas", 10F, FontStyle.Bold);
                using var labF = new Font("Consolas", 9.5F);
                using var boxPen = new Pen(Color.FromArgb(90, 90, 100));

                for (int b = 1; b <= 4; b++)
                {
                    int x = margin + (b - 1) * colW;
                    using (var gold = new SolidBrush(Gold))
                        g.DrawString("GPIO" + b, headF, gold, x + 8, 8);
                    using (var sep = new Pen(Color.FromArgb(40, 40, 48)))
                        if (b > 1) g.DrawLine(sep, x, 8, x, Height - 8);

                    int y = top;
                    foreach (var pin in PinCatalog.ForBoard(b))
                    {
                        bool on = PinActivity.IsActive(pin.Board, pin.Pin);
                        Color boxColor = on ? (pin.IsOutput ? OutputOn : InputOn) : Off;
                        using (var bb = new SolidBrush(boxColor)) g.FillRectangle(bb, x + 8, y + 2, 16, 16);
                        g.DrawRectangle(boxPen, x + 8, y + 2, 16, 16);

                        Color txt = on ? Color.White : Color.FromArgb(150, 150, 160);
                        using var tb = new SolidBrush(txt);
                        g.DrawString(pin.Pad, padF, tb, x + 32, y);
                        g.DrawString(pin.Label, labF, tb, x + 74, y + 1);
                        y += rowH;
                    }
                }
            }
        }
    }
}
