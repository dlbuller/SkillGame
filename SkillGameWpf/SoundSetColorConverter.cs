using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SkillGameWpf
{
    // A themed colour chip behind each sound-set icon, so the picker reads in colour (WPF renders emoji monochrome).
    public sealed class SoundSetColorConverter : IValueConverter
    {
        private static SolidColorBrush B(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string n = (value?.ToString() ?? "").ToLowerInvariant();
            if (n.Contains("custom") || n.Contains("fart") || n.Contains("toot")) return B(0x6D, 0xBE, 0x45);   // green
            if (n.Contains("rad") || n.Contains("rock") || n.Contains("metal") || n.Contains("guitar")) return B(0xE2, 0x49, 0x3C);   // red
            if (n.Contains("west") || n.Contains("cowboy") || n.Contains("horse")) return B(0xC0, 0x8A, 0x3E);   // tan
            if (n.Contains("xyl") || n.Contains("marimba") || n.Contains("mallet") || n.Contains("vibra")) return B(0x2A, 0x2E, 0x38);   // dark, so the rainbow bars pop
            if (n.Contains("medieval") || n.Contains("castle") || n.Contains("knight") || n.Contains("fantasy") || n.Contains("sword")) return B(0x8A, 0x93, 0xA6);   // steel
            if (n.Contains("8") || n.Contains("bit") || n.Contains("chip") || n.Contains("arcade") || n.Contains("retro")) return B(0xB4, 0x5A, 0xE0);   // purple
            if (n.Contains("space") || n.Contains("sci") || n.Contains("galax") || n.Contains("cosmic")) return B(0x5A, 0x7C, 0xE0);   // indigo
            if (n.Contains("disco") || n.Contains("dance") || n.Contains("funk")) return B(0xE0, 0x5A, 0xA8);   // pink
            if (n.Contains("jazz") || n.Contains("sax")) return B(0xE7, 0xA9, 0x1D);   // gold
            if (n.Contains("piano") || n.Contains("classic")) return B(0x9A, 0xA3, 0xAE);   // slate
            if (n.Contains("horror") || n.Contains("spook") || n.Contains("scary")) return B(0x7A, 0x4A, 0xE0);   // violet
            if (n.Contains("holiday") || n.Contains("xmas") || n.Contains("christmas")) return B(0xD1, 0x4B, 0x3C);   // holiday red
            if (n.Contains("drum") || n.Contains("beat")) return B(0xE6, 0x79, 0x1C);   // orange
            if (n.Contains("robot") || n.Contains("mech")) return B(0x33, 0xC9, 0xFF);   // cyan
            return B(0x6A, 0x6A, 0x76);   // muted default
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
