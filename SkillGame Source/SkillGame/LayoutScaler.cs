using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SkillGame
{
    /// <summary>
    /// Makes a form's layout grow/shrink to fill any screen resolution. Records the design-time
    /// bounds and font size of every control once (the 100% baseline), then rescales them
    /// proportionally to the form's current client size on every resize. Control positions/sizes
    /// scale on each axis; fonts scale by the smaller axis factor so text keeps its shape.
    /// </summary>
    public sealed class LayoutScaler
    {
        private readonly Form _form;
        private Size _baseline;
        private readonly Dictionary<Control, Rectangle> _bounds = new();
        private readonly Dictionary<Control, float> _fonts = new();
        private bool _recorded;
        private bool _busy;

        public LayoutScaler(Form form) => _form = form;

        /// <summary>Capture the current layout as the 100% baseline. Call once, before the form is resized.</summary>
        public void Record()
        {
            _baseline = _form.ClientSize;
            _bounds.Clear();
            _fonts.Clear();
            Walk(_form, c => { _bounds[c] = c.Bounds; _fonts[c] = c.Font.Size; });
            _recorded = true;
        }

        /// <summary>Rescale every recorded control to the form's current client size.</summary>
        public void Apply()
        {
            if (!_recorded || _busy) return;
            if (_baseline.Width == 0 || _baseline.Height == 0) return;
            Size client = _form.ClientSize;
            if (client.Width == 0 || client.Height == 0) return;

            float sx = (float)client.Width / _baseline.Width;
            float sy = (float)client.Height / _baseline.Height;
            float sf = Math.Min(sx, sy);

            _busy = true;
            _form.SuspendLayout();
            Walk(_form, c =>
            {
                // TabPage bounds are managed by the TabControl; only scale its children.
                if (c is not TabPage && _bounds.TryGetValue(c, out Rectangle b))
                {
                    c.Bounds = new Rectangle(
                        (int)Math.Round(b.X * sx), (int)Math.Round(b.Y * sy),
                        (int)Math.Round(b.Width * sx), (int)Math.Round(b.Height * sy));
                }
                if (_fonts.TryGetValue(c, out float fs))
                {
                    float ns = Math.Max(1f, fs * sf);
                    if (Math.Abs(c.Font.Size - ns) > 0.05f)
                        c.Font = new Font(c.Font.FontFamily, ns, c.Font.Style);
                }
            });
            _form.ResumeLayout();
            _busy = false;
        }

        private static void Walk(Control root, Action<Control> act)
        {
            foreach (Control c in root.Controls)
            {
                act(c);
                if (c.Controls.Count > 0) Walk(c, act);
            }
        }
    }
}
