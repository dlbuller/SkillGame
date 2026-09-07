using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SkillGame;

namespace SkillGameWpf
{
    // A flat, no-frills operator console: the essential settings and the live diagnostics on one page.
    // Reads the same shared state as the fancy screens (AppState / coordinator / PinActivity / Faults) — no duplicate logic.
    public partial class ServiceView : UserControl
    {
        private readonly OperatorSettings _s = AppState.Settings;
        private HardwareCoordinator? _coord;
        private readonly Dictionary<string, Border> _swTiles = new();
        private readonly Dictionary<string, Border> _lampTiles = new();
        private readonly Dictionary<int, Border> _boardPills = new();

        // Every lamp/solenoid the app drives, with a compact label for the monitor grid.
        private static readonly (string name, string label)[] Lamps =
        {
            ("10","10"),("20","20"),("30","30"),("40","40"),("50","50"),("60","60"),("70","70"),("80","80"),("90","90"),
            ("100","100"),("200","200"),("300","300"),("400","400"),
            ("Winner","WIN"),("GameOver","OVER"),("Tilt","TILT"),
            ("WinnerLock","W-LOCK"),("CoinLock","C-LOCK"),
        };
        private static string FriendlyLamp(string k) => k switch
        {
            "Winner" => "Winner lamp", "GameOver" => "Game Over lamp", "Tilt" => "Tilt lamp",
            "WinnerLock" => "Win-Lock solenoid", "CoinLock" => "Coin-Lock solenoid",
            _ => k.Length <= 2 ? k + " pts lamp" : k + " reel",
        };
        private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private long _lastPollCycles; private DateTime _lastPollSample = DateTime.UtcNow;
        private bool _loaded;
        private string _reportText = "";

        public ServiceView()
        {
            InitializeComponent();
            BuildBoardRow();
            BuildSwitchGrid();
            BuildLampGrid();
            _refresh.Tick += (s, e) => Refresh();
            Loaded += (s, e) =>
            {
                LoadFromSettings();
                PinActivity.Changed += OnPins;
                Faults.Changed += OnFaults;
                _lastPollCycles = _coord?.PollCycles ?? 0; _lastPollSample = DateTime.UtcNow;
                Refresh();
                _refresh.Start();
            };
            Unloaded += (s, e) =>
            {
                PinActivity.Changed -= OnPins;
                Faults.Changed -= OnFaults;
                _refresh.Stop();
                if (_coord?.BulbRunning == true) { _coord.StopBulbTest(); TestLampsBtn.Content = "TEST LAMPS"; }   // don't leave lamps cycling
                if (_ledTestOn) { _coord?.LedOff(); _ledTestOn = false; TestLedsBtn.Content = "TEST LEDS"; }        // or the strip running
            };
        }

        public void Attach(HardwareCoordinator coord) => _coord = coord;

        private void LoadFromSettings()
        {
            _loaded = false;
            BenchToggle.IsChecked = _s.BenchMode;
            TiltToggle.IsChecked = _s.TiltEnabled;
            TiltsSlider.Value = _s.TiltsAllowed; TiltsVal.Text = _s.TiltsAllowed.ToString();
            CountUp.Value = _s.CountUpStepMs; CountUpVal.Text = _s.CountUpStepMs + " ms";
            SoundToggle.IsChecked = _s.SoundFxOn;
            Brightness.Value = _s.ScreenBrightness; BrightnessVal.Text = _s.ScreenBrightness + "%";
            _loaded = true;
        }

        // ---- controls (write straight through to the shared settings, apply live) ----
        private void Bench_Click(object sender, RoutedEventArgs e) => AppState.SetBenchMode(BenchToggle.IsChecked == true);

        private void Tilt_Click(object sender, RoutedEventArgs e)
        { _s.TiltEnabled = TiltToggle.IsChecked == true; AppState.PersistSettings(); AppState.ApplySettings?.Invoke(_s); }

        private void Tilts_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TiltsVal != null) TiltsVal.Text = ((int)TiltsSlider.Value).ToString();
            if (!_loaded) return;
            _s.TiltsAllowed = (int)TiltsSlider.Value; AppState.PersistSettings(); AppState.ApplySettings?.Invoke(_s);
        }

        private void CountUp_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (CountUpVal != null) CountUpVal.Text = (int)CountUp.Value + " ms";
            if (!_loaded) return;
            _s.CountUpStepMs = (int)CountUp.Value; AppState.PersistSettings(); AppState.ApplySettings?.Invoke(_s);
        }

        private void Sound_Click(object sender, RoutedEventArgs e)
        { AppState.SoundFxOn = SoundToggle.IsChecked == true; AppState.PersistSettings(); }

        private void Brightness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (BrightnessVal != null) BrightnessVal.Text = (int)Brightness.Value + "%";
            if (!_loaded) return;
            int pct = (int)Brightness.Value; _s.ScreenBrightness = pct;
            (Window.GetWindow(this) as MainWindow)?.SetBrightness(pct); AppState.PersistSettings();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        { _s.Clamp(); AppState.PersistSettings(); AppState.ApplySettings?.Invoke(_s); DevText.Text = "Settings saved."; }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            _s.ResetToDefaults(); _s.Clamp(); _s.Save(); _s.ApplyToQuietHours();
            AppState.ApplyAccent(_s.AccentColor); AppState.ApplySettings?.Invoke(_s);
            AppState.SetBenchMode(_s.BenchMode);   // propagate the reset bench state to the coordinator
            (Window.GetWindow(this) as MainWindow)?.SetBrightness(_s.ScreenBrightness);
            LoadFromSettings();
            DevText.Text = "Settings reset to defaults.";
        }

        // ---- live diagnostics ----
        private void BuildBoardRow()
        {
            for (int i = 1; i <= 4; i++)
            {
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 6, 0),
                    Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                    BorderBrush = (Brush)FindResource("MutedBrush"), BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = "G" + i, FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 12, Foreground = (Brush)FindResource("MutedBrush") },
                };
                _boardPills[i] = pill; BoardRow.Children.Add(pill);
            }
        }

        private void BuildSwitchGrid()
        {
            foreach (var sw in SwitchMap.All)
            {
                var t = new Border
                {
                    CornerRadius = new CornerRadius(6), Margin = new Thickness(3),
                    Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                    BorderBrush = (Brush)FindResource("CardBorderBrush"), BorderThickness = new Thickness(1),
                    ToolTip = sw.Label,
                    Child = new TextBlock { Text = sw.Id, FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 17, Foreground = (Brush)FindResource("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                _swTiles[sw.Id] = t; SwitchGrid.Children.Add(t);
            }
        }

        private void OnPins() { if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(OnPins)); return; } RefreshSwitches(); RefreshLamps(); }
        private void OnFaults() { if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(RefreshStuck)); return; } RefreshStuck(); }

        private void BuildLampGrid()
        {
            foreach (var (name, label) in Lamps)
            {
                var t = new Border
                {
                    CornerRadius = new CornerRadius(6), Margin = new Thickness(3),
                    Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                    BorderBrush = (Brush)FindResource("CardBorderBrush"), BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand, ToolTip = FriendlyLamp(name) + " — click to toggle", Tag = name,
                    Child = new TextBlock { Text = label, FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 15, Foreground = (Brush)FindResource("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                t.MouseLeftButtonUp += LampTile_Click;
                _lampTiles[name] = t; LampGrid.Children.Add(t);
            }
        }

        private void RefreshLamps()
        {
            foreach (var (name, _) in Lamps)
            {
                if (!_lampTiles.TryGetValue(name, out var t) || !LampController.Outputs.TryGetValue(name, out var loc)) continue;
                bool on = PinActivity.IsActive(loc.board, loc.pin);
                var lit = Color.FromRgb(0xE7, 0xA9, 0x1D);
                Brush c = on ? new SolidColorBrush(lit) : (Brush)FindResource("MutedBrush");
                Color bg = on ? Color.FromArgb(0x33, lit.R, lit.G, lit.B) : Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF);
                t.BorderBrush = c; t.Background = new SolidColorBrush(bg); ((TextBlock)t.Child).Foreground = c;
            }
        }

        // Manually flip a lamp/solenoid to check it on the cabinet (the page has no game running to fight over it).
        private void LampTile_Click(object sender, MouseButtonEventArgs e)
        {
            if (_coord == null || sender is not Border b || b.Tag is not string name) return;
            if (!LampController.Outputs.TryGetValue(name, out var loc)) return;
            _coord.SetLamp(name, !PinActivity.IsActive(loc.board, loc.pin));
            RefreshLamps();
        }

        private void TestLamps_Click(object sender, RoutedEventArgs e)
        {
            if (_coord == null) return;
            if (_coord.BulbRunning) { _coord.StopBulbTest(); TestLampsBtn.Content = "TEST LAMPS"; }
            else { TestLampsBtn.Content = "STOP TEST"; _ = _coord.BulbTestAsync(); }
        }

        private bool _ledTestOn;
        private void TestLeds_Click(object sender, RoutedEventArgs e)
        {
            if (_coord == null) return;
            if (_ledTestOn) { _coord.LedOff(); _ledTestOn = false; TestLedsBtn.Content = "TEST LEDS"; }
            else { _coord.LedPattern("Rainbow Wave"); _ledTestOn = true; TestLedsBtn.Content = "STOP LEDS"; }
        }

        private void Refresh()
        {
            var pres = AppState.BoardPresence;
            for (int i = 1; i <= 4; i++)
            {
                bool present = pres != null && pres.Length >= i && pres[i - 1];
                var pill = _boardPills[i];
                var c = (Brush)FindResource(present ? "GreenBrush" : "RedBrush");
                pill.BorderBrush = c; ((TextBlock)pill.Child).Foreground = c;
                pill.Background = new SolidColorBrush(present ? Color.FromArgb(0x1E, 0x2F, 0xE0, 0x8C) : Color.FromArgb(0x1E, 0xE2, 0x49, 0x3C));
            }
            if (_coord != null)
            {
                long cyc = _coord.PollCycles; var t = DateTime.UtcNow; double secs = (t - _lastPollSample).TotalSeconds;
                if (secs >= 0.4)
                {
                    double hz = (cyc - _lastPollCycles) / 3.0 / secs;
                    PollText.Text = $"≈{Math.Round(hz)} Hz";
                    _lastPollCycles = cyc; _lastPollSample = t;
                    DevText.Text = $"FT232H  G1 {Ago(_coord.LastSeenAgoMs(1))} · G2 {Ago(_coord.LastSeenAgoMs(2))} · G3 {Ago(_coord.LastSeenAgoMs(3))} · G4 out";
                }
            }
            else PollText.Text = "no hardware";
            RefreshSwitches();
            RefreshLamps();
            RefreshStuck();
        }

        private void RefreshSwitches()
        {
            bool bench = _s.BenchMode;
            foreach (var sw in SwitchMap.All)
            {
                if (!_swTiles.TryGetValue(sw.Id, out var t)) continue;
                bool stuck = Faults.Contains(sw.Id);
                bool on = !bench && PinActivity.IsActive(sw.Board, sw.Pin);
                Brush c; Color bg;
                if (stuck) { c = (Brush)FindResource("RedBrush"); bg = Color.FromArgb(0x30, 0xE2, 0x49, 0x3C); }
                else if (on) { c = (Brush)FindResource("GreenBrush"); bg = Color.FromArgb(0x30, 0x2F, 0xE0, 0x8C); }
                else { c = (Brush)FindResource("MutedBrush"); bg = Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF); }
                t.BorderBrush = c; t.Background = new SolidColorBrush(bg); ((TextBlock)t.Child).Foreground = c;
            }
        }

        private void RefreshStuck()
        {
            if (_s.BenchMode) { StuckText.Text = "Bench mode ON — inputs ignored"; Tint(StuckBanner, StuckText, "MutedBrush", 0x18); return; }
            int n = Faults.Count;
            if (n == 0) { StuckText.Text = "No stuck switches"; Tint(StuckBanner, StuckText, "GreenBrush", 0x14); }
            else { StuckText.Text = $"{n} switch{(n > 1 ? "es" : "")} STUCK:  {string.Join(", ", Faults.Snapshot())}"; Tint(StuckBanner, StuckText, "RedBrush", 0x1E); }
        }

        private void Tint(Border box, TextBlock txt, string brushKey, byte alpha)
        {
            var c = ((SolidColorBrush)FindResource(brushKey)).Color;
            box.BorderBrush = (Brush)FindResource(brushKey);
            box.Background = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            txt.Foreground = (Brush)FindResource(brushKey);
        }

        private static string Ago(long ms) => ms < 0 ? "—" : ms < 1000 ? ms + "ms" : (ms / 1000) + "s";

        // ---- multi-check self-test with a saveable report ----
        private async void SelfTest_Click(object sender, RoutedEventArgs e)
        {
            ReportRows.Children.Clear();
            var lines = new List<(string kind, string text)>();
            bool bench = _s.BenchMode;
            var (ok, present) = _coord != null ? await _coord.SelfTestAsync() : (false, AppState.BoardPresence);

            for (int i = 1; i <= 4; i++)
            {
                bool p = present != null && present.Length >= i && present[i - 1];
                lines.Add((p ? "ok" : "warn", $"Board GPIO{i}: {(p ? "present" : "MISSING")}"));
            }
            if (bench) lines.Add(("warn", "Bench mode ON — switch inputs were NOT tested."));
            else
            {
                var stuck = Faults.Snapshot();
                lines.Add((stuck.Count == 0 ? "ok" : "warn", stuck.Count == 0 ? "Switch inputs: none stuck" : $"Stuck: {string.Join(", ", stuck)}"));
            }
            if (_coord != null) lines.Add((_coord.PollCycles > 0 ? "ok" : "warn", _coord.PollCycles > 0 ? "Switch polling: running" : "Switch polling: idle"));
            lines.Add(("info", "LED strip: pulsed white — confirm it lit (outputs have no feedback to verify automatically)."));

            bool clean = ok && (bench || Faults.Count == 0);
            lines.Insert(0, (clean ? "ok" : "warn", clean ? "SELF-TEST: PASS" : "SELF-TEST: ATTENTION NEEDED"));
            if (!bench && Faults.Count > 0) lines.Add(("info", "→ Fix the stuck switches (wiring / pull-downs to ground), or turn Bench Mode on to ignore inputs."));
            if (present != null && System.Array.IndexOf(present, false) >= 0) lines.Add(("info", "→ Re-seat any missing FT232H board and its USB cable, then run again."));

            var sb = new StringBuilder();
            sb.AppendLine($"SkillGame Service Self-Test   {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(new string('-', 44));
            foreach (var (kind, text) in lines)
            {
                ReportRows.Children.Add(Row(kind, text));
                sb.AppendLine((kind == "ok" ? "[OK]  " : kind == "warn" ? "[!!]  " : "      ") + text);
            }
            _reportText = sb.ToString();
            ReportBox.Visibility = Visibility.Visible;
        }

        private FrameworkElement Row(string kind, string text)
        {
            string key = kind == "ok" ? "GreenBrush" : kind == "warn" ? "RedBrush" : "MutedBrush";
            string glyph = kind == "ok" ? "✓" : kind == "warn" ? "⚠" : "•";
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            sp.Children.Add(new TextBlock { Text = glyph, Foreground = (Brush)FindResource(key), FontWeight = FontWeights.Bold, FontSize = 12.5, Width = 18 });
            sp.Children.Add(new TextBlock { Text = text, Foreground = (Brush)FindResource("TextBrush"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });
            return sp;
        }

        private void SaveReport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_reportText)) return;
            try
            {
                Directory.CreateDirectory(@"C:\SkillGame");
                string path = $@"C:\SkillGame\service_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                File.WriteAllText(path, _reportText);
                DevText.Text = "Saved " + path;
            }
            catch (Exception ex) { DevText.Text = "Save failed: " + ex.Message; }
        }

        private void Tutorial_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.StartTutorial();

        private void AllOff_Click(object sender, RoutedEventArgs e)
        {
            _coord?.StopBulbTest(); TestLampsBtn.Content = "TEST LAMPS";
            _ledTestOn = false; TestLedsBtn.Content = "TEST LEDS";
            _coord?.AllOff();   // clears lamps + LED strip
            RefreshLamps();
        }
    }
}
