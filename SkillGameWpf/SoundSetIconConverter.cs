using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SkillGameWpf
{
    // Maps a sound-set folder name to a little icon for the picker. Usually an emoji glyph, but for concepts with no
    // emoji (e.g. a xylophone) it returns a small drawn vector instead. Falls back to a speaker.
    public sealed class SoundSetIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string n = (value?.ToString() ?? "").ToLowerInvariant();
            if (n.Contains("xyl") || n.Contains("marimba") || n.Contains("mallet") || n.Contains("vibra")) return Xylophone();
            if (n.Contains("custom") || n.Contains("fart") || n.Contains("toot")) return "💨";   // the "custom" set is farts
            if (n.Contains("rad") || n.Contains("rock") || n.Contains("metal") || n.Contains("guitar")) return "🎸";
            if (n.Contains("west") || n.Contains("cowboy") || n.Contains("horse")) return "🐴";
            if (n.Contains("medieval") || n.Contains("castle") || n.Contains("knight") || n.Contains("fantasy") || n.Contains("sword")) return "🏰";
            if (n.Contains("8") || n.Contains("bit") || n.Contains("chip") || n.Contains("arcade") || n.Contains("retro")) return "🕹️";
            if (n.Contains("space") || n.Contains("sci") || n.Contains("galax") || n.Contains("cosmic")) return "🚀";
            if (n.Contains("disco") || n.Contains("dance") || n.Contains("funk")) return "🪩";
            if (n.Contains("jazz") || n.Contains("sax")) return "🎷";
            if (n.Contains("piano") || n.Contains("classic")) return "🎹";
            if (n.Contains("horror") || n.Contains("spook") || n.Contains("scary")) return "👻";
            if (n.Contains("holiday") || n.Contains("xmas") || n.Contains("christmas")) return "🔔";
            if (n.Contains("drum") || n.Contains("beat")) return "🥁";
            if (n.Contains("robot") || n.Contains("mech")) return "🤖";
            return "🔊";
        }

        // A tiny five-bar xylophone (there's no xylophone emoji), each bar a different colour.
        private static UIElement Xylophone()
        {
            var bars = new (byte r, byte g, byte b, double h)[]
            {
                (0xE2, 0x49, 0x3C, 9), (0xE6, 0x79, 0x1C, 11), (0xE7, 0xA9, 0x1D, 13), (0x2F, 0xE0, 0x8C, 15), (0x33, 0xC9, 0xFF, 17),
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var (r, g, b, h) in bars)
                sp.Children.Add(new Rectangle
                {
                    Width = 3, Height = h, RadiusX = 1.4, RadiusY = 1.4, Margin = new Thickness(0.8, 0, 0.8, 0),
                    VerticalAlignment = VerticalAlignment.Center, Fill = new SolidColorBrush(Color.FromRgb(r, g, b)),
                });
            return sp;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
