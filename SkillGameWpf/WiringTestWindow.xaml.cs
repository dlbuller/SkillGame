using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SkillGame;

namespace SkillGameWpf
{
    // Walks the operator through pressing every switch; each tile turns green once it's seen wired.
    public partial class WiringTestWindow : Window
    {
        private readonly HardwareCoordinator? _coord;
        private readonly Dictionary<string, Border> _chips = new();
        private readonly HashSet<string> _wired = new();

        public WiringTestWindow(HardwareCoordinator? coord)
        {
            InitializeComponent();
            _coord = coord;
            BuildChips();
            UpdatePromptAndProgress();
            if (_coord != null) _coord.SwitchObserver = OnSwitch;
            Closed += (s, e) => { if (_coord != null) _coord.SwitchObserver = null; };
        }

        private void BuildChips()
        {
            _chips.Clear();
            ChipHost.Children.Clear();
            foreach (var sw in SwitchMap.All)
            {
                string num = sw.Id.Substring(1);
                var id = new TextBlock { Text = $"Switch {num}", FontFamily = (FontFamily)FindResource("UiFont"), FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = (Brush)FindResource("TextBrush") };
                var sub = new TextBlock { Text = sw.Kind == SwitchKind.Score ? $"(S{num}) · {sw.Points} pts" : $"(S{num}) · {sw.Kind}", FontSize = 10.5, Foreground = (Brush)FindResource("MutedBrush") };
                var stack = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
                stack.Children.Add(id); stack.Children.Add(sub);
                var chip = new Border
                {
                    Margin = new Thickness(4),
                    CornerRadius = new CornerRadius(9),
                    Background = new SolidColorBrush(Color.FromArgb(0x14, 0x20, 0x26, 0x34)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1), Child = stack,
                };
                _chips[sw.Id] = chip;
                ChipHost.Children.Add(chip);
            }
        }

        private void OnSwitch(SwitchDef def, bool high)
        {
            if (!high) return;
            if (_wired.Add(def.Id)) MarkWired(def.Id);
            UpdatePromptAndProgress();
        }

        private void MarkWired(string id)
        {
            if (!_chips.TryGetValue(id, out var chip)) return;
            var green = ((SolidColorBrush)FindResource("GreenBrush")).Color;
            chip.Background = new SolidColorBrush(green) { Opacity = 0.28 };
            chip.BorderBrush = new SolidColorBrush(green);
            chip.Effect = new DropShadowEffect { Color = green, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.7 };
        }

        private void UpdatePromptAndProgress()
        {
            int total = SwitchMap.All.Length;
            ProgressText.Text = $"{_wired.Count} / {total} wired";
            SwitchDef? next = null;
            foreach (var sw in SwitchMap.All) if (!_wired.Contains(sw.Id)) { next = sw; break; }
            if (next == null) { PromptText.Text = "ALL SWITCHES WIRED ✓"; PromptText.Foreground = (Brush)FindResource("GreenBrush"); }
            else PromptText.Text = next.Label;
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            foreach (var sw in SwitchMap.All) if (!_wired.Contains(sw.Id)) { _wired.Add(sw.Id); MarkWired(sw.Id); UpdatePromptAndProgress(); return; }
        }

        private void Restart_Click(object sender, RoutedEventArgs e)
        {
            _wired.Clear();
            BuildChips();
            PromptText.Foreground = (Brush)FindResource("TextBrush");
            UpdatePromptAndProgress();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
