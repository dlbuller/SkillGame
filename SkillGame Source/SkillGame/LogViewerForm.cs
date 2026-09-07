using System;
using System.Drawing;
using System.Windows.Forms;

namespace SkillGame
{
    /// <summary>
    /// A small read-only window that shows the tail of skillgame.log so you can see recent events and
    /// errors right on the cabinet, without digging the file out of %LOCALAPPDATA%. REFRESH re-reads it.
    /// </summary>
    public class LogViewerForm : Form
    {
        private readonly TextBox _box;

        public LogViewerForm()
        {
            Text = "SkillGame Log";
            Size = new Size(940, 620);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(24, 24, 30);

            _box = new TextBox
            {
                Multiline = true, ReadOnly = true, WordWrap = false,
                ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill,
                BackColor = Color.Black, ForeColor = Color.FromArgb(0, 220, 140),
                Font = new Font("Consolas", 10F), BorderStyle = BorderStyle.None,
            };

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Color.FromArgb(24, 24, 30) };
            var refresh = new Button
            {
                Text = "REFRESH", Location = new Point(8, 8), Size = new Size(130, 36),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(34, 34, 40), ForeColor = Color.White,
                Font = new Font("Seven Segment", 10F, FontStyle.Bold), UseVisualStyleBackColor = false,
            };
            refresh.FlatAppearance.BorderColor = Color.FromArgb(0, 200, 255);
            refresh.Click += (s, e) => Reload();
            var path = new Label
            {
                Text = Log.FilePath, AutoSize = false, Dock = DockStyle.Fill, ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8F), TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(0, 0, 12, 0),
            };
            bar.Controls.Add(path);
            bar.Controls.Add(refresh);

            Controls.Add(_box);
            Controls.Add(bar);
            Reload();
        }

        private void Reload()
        {
            _box.Text = Log.Tail(300);
            _box.SelectionStart = _box.TextLength;
            _box.ScrollToCaret();
        }
    }
}
