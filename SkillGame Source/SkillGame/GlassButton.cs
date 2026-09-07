using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SkillGame
{
    /// <summary>
    /// A rounded, glass-look button. Owner-drawn from the button's own BackColor: a vertical gradient
    /// with a soft top "sheen", a subtle lighter border, and brighten-on-hover / darken-on-press.
    /// Corners are rounded via the control Region so they blend into whatever is behind. Repaints on
    /// resize, so it stays crisp as the form scales. Drop-in replacement for Button (same events/props).
    /// </summary>
    public class GlassButton : Button
    {
        private bool _hover, _down;

        /// <summary>Optional accent color for the border (used to color-code buttons by section).
        /// When left as Empty, the border is derived from BackColor as before.</summary>
        public Color AccentColor { get; set; } = Color.Empty;

        public GlassButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
        }

        private int Radius => Math.Max(6, Math.Min(Width, Height) / 4);

        private static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) { p.AddRectangle(r); return p; }
            int d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // f > 0 lightens toward white, f < 0 darkens toward black.
        private static Color Shift(Color c, float f)
        {
            float target = f < 0 ? 0f : 255f, a = Math.Abs(f);
            return Color.FromArgb(c.A,
                (int)(c.R + (target - c.R) * a),
                (int)(c.G + (target - c.G) * a),
                (int)(c.B + (target - c.B) * a));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width > 0 && Height > 0)
                using (var p = Round(new Rectangle(0, 0, Width, Height), Radius))
                    Region = new Region(p);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Color.Black);

            var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            int rad = Radius;
            Color baseC = BackColor;
            if (_down) baseC = Shift(baseC, -0.12f);
            else if (_hover) baseC = Shift(baseC, 0.16f);

            using (var body = Round(rect, rad))
            {
                using (var grad = new LinearGradientBrush(rect, Shift(baseC, 0.22f), Shift(baseC, -0.18f), LinearGradientMode.Vertical))
                    g.FillPath(grad, body);

                // glass sheen across the top half, clipped to the rounded body
                var save = g.Save();
                g.SetClip(body, CombineMode.Replace);
                var topRect = new Rectangle(0, 0, Width, Math.Max(2, (int)(Height * 0.5)));
                using (var sheen = new LinearGradientBrush(new Rectangle(0, 0, Width, topRect.Height + 1),
                        Color.FromArgb(80, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                    g.FillRectangle(sheen, topRect);
                g.Restore(save);

                Color borderC = AccentColor.IsEmpty ? Shift(baseC, 0.38f) : AccentColor;
                float borderW = AccentColor.IsEmpty ? 1.4f : 1.8f;
                using (var pen = new Pen(borderC, borderW))
                    g.DrawPath(pen, body);
            }

            var textRect = new Rectangle(3, 0, Math.Max(1, Width - 6), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }
}
