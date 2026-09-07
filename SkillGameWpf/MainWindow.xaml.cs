using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SkillGame;

namespace SkillGameWpf
{
    public partial class MainWindow : Window
    {
        private readonly GameStatusView _gameView = new();
        private readonly DiagnosticsView _diagView = new();
        private readonly SettingsView _setView = new();
        private readonly AuditsView _auditView = new();
        private readonly SchematicsView _schemView = new();
        private readonly ServiceView _serviceView = new();
        private readonly HardwareCoordinator _coord;

        public MainWindow()
        {
            InitializeComponent();
            MainContent.Content = _gameView;
            AppState.ApplyAccent(AppState.Settings.AccentColor);   // apply the saved UI accent
            SetBrightness(AppState.Settings.ScreenBrightness);

            _coord = new HardwareCoordinator(_gameView, _diagView, OnHardwareState, Dispatcher);
            _serviceView.Attach(_coord);   // the flat operator console reads poll rate / self-test from the coordinator

            Faults.Changed += () => Dispatcher.BeginInvoke(new Action(RecomputeHealth));   // stuck switch appeared/cleared
            Loaded += (s, e) =>
            {
                _coord.Initialize(); _navReady = true; StartPowerOnMeter();
                if (!AppState.Settings.TutorialSeen) StartTutorial();   // first-run guided tour
            };
            Closing += (s, e) => { TickPowerOn(); AppState.Audits.Save(); _coord.Shutdown(); };
        }

        // Odometer for how long the machine has been powered on; accrues every minute and on close.
        private readonly System.Windows.Threading.DispatcherTimer _onTimer = new() { Interval = TimeSpan.FromSeconds(60) };
        private DateTime _onLast;
        private void StartPowerOnMeter()
        {
            _onLast = DateTime.UtcNow;
            _onTimer.Tick += (s, e) => { TickPowerOn(); AppState.Audits.Save(); };
            _onTimer.Start();
        }
        private void TickPowerOn()
        {
            var now = DateTime.UtcNow;
            AppState.Audits.AddPowerOnSeconds((long)(now - _onLast).TotalSeconds);
            _onLast = now;
        }

        // Dim the whole screen for the cabinet: 100 = full brightness, 30 = quite dim.
        public void SetBrightness(int pct)
        {
            pct = Math.Max(30, Math.Min(100, pct));
            DimOverlay.Opacity = (100 - pct) / 100.0 * 0.85;
        }

        private bool _navReady;   // true once loaded, so the startup selection doesn't click

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent == null) return;
            int index = 0;
            if (sender == NavGame) { MainContent.Content = _gameView; _coord.Mode = "game"; index = 0; }
            else if (sender == NavDiag) { MainContent.Content = _diagView; _coord.Mode = "diag"; index = 1; }
            else if (sender == NavSet) { MainContent.Content = _setView; _coord.Mode = "set"; index = 2; }
            else if (sender == NavAudit) { MainContent.Content = _auditView; _coord.Mode = "audit"; index = 3; }
            else if (sender == NavSchem) { MainContent.Content = _schemView; _coord.Mode = "schem"; index = 4; }
            else if (sender == NavService) { MainContent.Content = _serviceView; _coord.Mode = "service"; index = 5; }
            MoveNavHighlight(index);
            if (_navReady) System.Threading.Tasks.Task.Run(() => AppState.Audio.PlaySettingsSound("menuselect"));   // click on every nav press
            // Each screen plays its own theme (Game Status is quiet); play off the UI thread since SoundPlayer loads the whole wav synchronously.
            if (sender == NavGame) System.Threading.Tasks.Task.Run(() => { AppState.Audio.StopBackground(); AppState.Audio.StopMusicClip(); });   // quiet the game screen but keep the click
            else if (sender == NavDiag) PlayTheme("Diagnostics");
            else if (sender == NavSet) PlayTheme("Settings");
            else if (sender == NavAudit) PlayTheme("Audits");
            else if (sender == NavSchem || sender == NavService) System.Threading.Tasks.Task.Run(() => { AppState.Audio.StopBackground(); AppState.Audio.StopMusicClip(); });   // schematic + service stay quiet
        }

        private static void PlayTheme(string screen) => System.Threading.Tasks.Task.Run(() => AppState.Audio.PlayScreenTheme(screen));

        private void MoveNavHighlight(int index)
        {
            if (NavHi == null) return;
            const double pitch = 58;   // button height 52 + 3px top/bottom margin
            var anim = new DoubleAnimation(index * pitch, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            NavHi.BeginAnimation(TranslateTransform.YProperty, anim);
        }

        private void Min_Click(object sender, RoutedEventArgs e)
        {
            System.Threading.Tasks.Task.Run(() => AppState.Audio.PlaySettingsSound("home"));
            WindowState = WindowState.Minimized;
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            System.Threading.Tasks.Task.Run(() => AppState.Audio.PlaySettingsSound("poweroff"));   // fires instantly, in-process
            Hide();   // vanish right away so it feels closed, then fully exit once the sound has played
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            t.Tick += (s, e2) => { t.Stop(); Close(); };
            t.Start();
        }

        // Build "Board 3 Missing" / "Board 1, 4 Missing" from the per-board presence (index 0..3 = board 1..4).
        public static string MissingBoardsText(bool[] present)
        {
            if (present == null || present.Length == 0) return "no hardware";
            var missing = new System.Collections.Generic.List<int>();
            for (int i = 0; i < present.Length; i++) if (!present[i]) missing.Add(i + 1);
            if (missing.Count == 0) return "Hardware Ready";
            return $"{(missing.Count > 1 ? "Boards" : "Board")} {string.Join(", ", missing)} Missing";
        }

        // Coordinator reports per-board presence; reflect it in the sidebar and under the robot.
        private bool[] _present = new bool[4];
        private void OnHardwareState(bool[] present)
        {
            bool ready = present != null && present.Length >= 4 && present[0] && present[1] && present[2] && present[3];
            HwDot.Fill = (Brush)FindResource(ready ? "GreenBrush" : "MutedBrush");
            HwText.Text = MissingBoardsText(present);
            HwText.Foreground = (Brush)FindResource(ready ? "GreenBrush" : "MutedBrush");
            _gameView.SetHardwareHealthy(present);   // robot goes "sick" (red X-eyes) + shows which boards are missing
            if (present != null && present.Length >= 4) _present = present;
            AppState.SetBoardPresence(_present);      // the live Schematic reads this to colour the board blocks
            RecomputeHealth();
        }

        // The Diagnostics nav badge: green check when everything's good, a pulsing red ! the moment something's wrong.
        private bool _badgeProblem;
        private void RecomputeHealth()
        {
            if (DiagBadge == null) return;
            bool ready = _present.Length >= 4 && _present[0] && _present[1] && _present[2] && _present[3];
            int stuck = Faults.Count;
            bool problem = !ready || stuck > 0;
            string tip;
            if (!ready) tip = MissingBoardsText(_present) + " — open Diagnostics";
            else if (stuck > 0) tip = $"{stuck} switch{(stuck > 1 ? "es" : "")} reading stuck — open Diagnostics";
            else tip = AppState.Settings.BenchMode ? "All boards OK · Bench Mode on" : "All systems OK";

            DiagBadge.Visibility = Visibility.Visible;
            DiagBadge.ToolTip = tip;
            if (problem)
            {
                var red = Color.FromRgb(0xE2, 0x49, 0x3C);
                DiagBadge.Background = new SolidColorBrush(red);
                DiagBadgeText.Text = "!";
                DiagBadgeGlow.Color = red;
                if (!_badgeProblem)
                    DiagBadgeGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
                        new DoubleAnimation(0.35, 1.0, TimeSpan.FromMilliseconds(650)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            }
            else
            {
                var green = Color.FromRgb(0x2F, 0xE0, 0x8C);
                DiagBadge.Background = new SolidColorBrush(green);
                DiagBadgeText.Text = "✓";
                DiagBadgeGlow.Color = green;
                DiagBadgeGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null);
                DiagBadgeGlow.Opacity = 0.85;
            }
            _badgeProblem = problem;
        }

        // ---- First-run guided tour ----
        private int _tutStep;
        private (RadioButton nav, string title, string body)[]? _tut;

        private void BuildTutSteps() => _tut = new (RadioButton, string, string)[]
        {
            (NavGame, "Welcome", "This is the SKILLGAME control panel for your cabinet — the physical coin is the game piece, and this screen mirrors and runs the machine. Here's the two-minute tour."),
            (NavGame, "Game Status", "The live playfield. Drop a real coin to play, or hit DEMO GAME to watch one roll. If a switch sticks, it rings red right here on the board."),
            (NavDiag, "Diagnostics", "Every switch and lamp, live. A switch stuck closed flags red — and the badge on this Diagnostics tab turns red from any screen. The little gremlin wanders over and points at real faults."),
            (NavSet, "Settings", "Tune how the game plays — count-up speed, tilt, attract shows, the accent colour, brightness. Hover any control for a hint."),
            (NavSet, "Bench Mode", "Testing on the bench with nothing wired? Turn on BENCH MODE so floating inputs can't start phantom games or read as stuck. Turn it off once the switch harness is connected."),
            (NavAudit, "Audits", "The machine's permanent books — games played, power-on hours, winners, and a switch-wear log showing how much life each switch has left."),
            (NavSchem, "Schematic", "A live wiring diagram: current flows along the wires and each block lights up with the real hardware. Click any block for its theory of operation and pinout."),
            (NavService, "Service", "Everything on one flat page — the key controls plus live diagnostics, RUN SELF-TEST and TEST LAMPS. You can replay this tour anytime from the button here."),
        };

        public void StartTutorial()
        {
            if (_tut == null) BuildTutSteps();
            _tutStep = 0;
            TutorialOverlay.Visibility = Visibility.Visible;
            ShowTutStep();
        }

        private void ShowTutStep()
        {
            if (_tut == null) return;
            var (nav, title, body) = _tut[_tutStep];
            nav.IsChecked = true;   // bring the described screen up behind the overlay
            TutStepText.Text = $"STEP {_tutStep + 1} OF {_tut.Length}";
            TutTitle.Text = title;
            TutBody.Text = body;
            TutBackBtn.IsEnabled = _tutStep > 0;
            TutNextBtn.Content = _tutStep == _tut.Length - 1 ? "Finish" : "Next";
        }

        private void Tut_Next(object sender, RoutedEventArgs e)
        {
            if (_tut == null || _tutStep >= _tut.Length - 1) { EndTutorial(); return; }
            _tutStep++; ShowTutStep();
        }
        private void Tut_Back(object sender, RoutedEventArgs e) { if (_tutStep > 0) { _tutStep--; ShowTutStep(); } }
        private void Tut_Skip(object sender, RoutedEventArgs e) => EndTutorial();

        private void EndTutorial()
        {
            TutorialOverlay.Visibility = Visibility.Collapsed;
            AppState.Settings.TutorialSeen = true;
            AppState.PersistSettings();
        }

        private bool _fullscreen;
        // F11 toggles borderless fullscreen (for the cabinet); Esc leaves fullscreen.
        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.F11 || (e.Key == System.Windows.Input.Key.Escape && _fullscreen))
            {
                _fullscreen = e.Key == System.Windows.Input.Key.F11 && !_fullscreen;
                WindowStyle = _fullscreen ? WindowStyle.None : WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;        // toggle so a borderless window repaints full-screen
                WindowState = WindowState.Maximized;
            }
        }

        // Used by the offscreen render harness to capture a specific screen.
        public void ShowView(string view)
        {
            switch (view)
            {
                case "diag": NavDiag.IsChecked = true; break;
                case "set": NavSet.IsChecked = true; break;
                case "audit": NavAudit.IsChecked = true; break;
                case "schem": NavSchem.IsChecked = true; break;
                case "service": NavService.IsChecked = true; break;
                default: NavGame.IsChecked = true; break;
            }
        }
    }
}
