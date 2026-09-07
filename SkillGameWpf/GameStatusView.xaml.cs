using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SkillGame;

namespace SkillGameWpf
{
    // The flagship screen: implements IGameView so the GameEngine pushes state into these elements; DEMO GAME plays a demo.
    public partial class GameStatusView : UserControl, IGameView
    {
        private readonly AuditStore _audits = AppState.Audits;
        private readonly GameEngine _engine;      // local, no-hardware engine used until the coordinator attaches
        private GameEngine _activeEngine;         // the engine the DEMO button drives (real one once hardware is up)
        private readonly DispatcherTimer _clock = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
        private CancellationTokenSource? _demoCts;
        private int _lastScoreShown;
        private string _lastRobotMood = "";
        private readonly Random _rnd = new();

        // Live playfield (a stylised recreation of the real yellow Skill-Roll board) + high-score chase.
        private readonly Dictionary<string, Ellipse> _holeDisc = new();
        private readonly Dictionary<string, TextBlock> _holeText = new();
        private readonly Dictionary<string, ScaleTransform> _holeScale = new();
        private readonly Dictionary<string, Color> _holeRing = new();     // ring colour per hole (for reset)
        private readonly Dictionary<string, Color> _holeOffFill = new();  // resting fill per hole (cream / black gobble)
        private readonly Dictionary<string, (double x, double y)> _holePos = new();  // hole centre on the canvas (for the coin)
        private readonly HashSet<string> _winnerHoles = new();
        private readonly Dictionary<int, Rectangle> _rowBand = new();    // level -> active-row highlight band
        private readonly Dictionary<string, bool> _pinPrev = new();      // rising-edge tracking for live hole lighting
        private Ellipse? _coin;                                          // the ball that rolls the board
        private readonly Dictionary<int, FrameworkElement> _tensLight = new();   // 10..90 score-scale lamps
        private readonly Dictionary<int, FrameworkElement> _hundLight = new();   // 100..400 reel lamps
        private bool _pfSubscribed, _pfQueued;
        private readonly Dictionary<string, List<Ellipse>> _faultMarks = new();   // stuck-switch fault rings on the board (S23 has one per gobble hole)

        // Ball physics against a rail collision bitmap (the black lines of the board).
        private bool[,]? _rail; private int _railW, _railH;
        private readonly DispatcherTimer _phys = new() { Interval = TimeSpan.FromMilliseconds(16) };
        private bool _ballLive;
        private const double BallR = 9;    // coin radius (visual coin is 22px)
        private int _highAtStart;      // the machine's best captured when a game starts (to detect a new record)
        private bool _beatHighFired;   // so the NEW HIGH banner fires once per game

        private readonly List<Border> _frontDots = new();
        private readonly Dictionary<string, Ellipse> _indDots = new();
        private readonly Dictionary<string, TextBlock> _indLabels = new();
        private readonly DispatcherTimer _fx = new() { Interval = TimeSpan.FromMilliseconds(110) };
        private int _fxFrame;
        private string _stripMode = "idle";
        private int _stripBurst;
        private bool _distractHeld;   // distract strip stays lit until the next switch
        private int _stripGrace;      // minimum frames the attract strip stays before following the music
        private Color _stripColor = Color.FromRgb(0x2F, 0xE0, 0x8C);   // colour the in-game "score" show runs in
        private int _attractInd = -1, _distractInd = -1;

        // Latest real LED-strip frame (from the hardware), so the SKILLGAME header mirrors the actual colors/pattern.
        private volatile System.Drawing.Color[]? _ledFrame;
        private long _ledFrameTick;

        public GameStatusView()
        {
            InitializeComponent();
            // Deferred audio sink so the demo's per-hole scoring sound fires when the on-screen coin drops through, not when the engine registers the hit; lamps/LEDs stay no-op in preview.
            _engine = new GameEngine(this, new DeferredScoreSink(AppState.Audio), new NoLampSink(), _audits, new NoLedSink())
            { TiltEnabled = AppState.Settings.TiltEnabled, TiltsAllowed = AppState.Settings.TiltsAllowed };
            _activeEngine = _engine;

            BuildFrontStrip();
            LedEffects.FrameProduced += OnLedFrame;   // mirror the real strip's colors on the SKILLGAME header
            Faults.Changed += OnFaultsChanged;         // surface stuck switches right on the board (outside bench mode)
            BuildIndicators();
            Loaded += (s, e) => { if (BenchToggleGame != null) BenchToggleGame.IsChecked = AppState.Settings.BenchMode; };   // sync with the Settings toggle each time shown
            DemoEndingCombo.ItemsSource = new[] { "WIN", "LOSE", "TILT" };
            DemoEndingCombo.SelectedIndex = 0;
            BuildPlayfield();
            SetPlayState(false);   // start idle: INSERT COIN showing
            _fx.Tick += (s, e) => FxTick();
            _fx.Start();
            _sparkTimer.Tick += (s, e) => SparkTick();
            _sparkTimer.Start();
            _phys.Tick += (s, e) => PhysTick();

            _clock.Tick += (s, e) => Tick();
            _clock.Start();
            Tick();
            Unloaded += (s, e) =>
            {
                // Leaving the page pauses a running demo (freeze the coin/timers) so it resumes on return.
                if (_demoCts != null && !_demoCts.IsCancellationRequested)
                {
                    _demoPaused = true;
                    _resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    AppState.Audio.StopMusicClip();   // don't let distract music bleed onto the next page
                }
                _clock.Stop(); _fx.Stop(); _phys.Stop(); _sparkTimer.Stop(); ClearSparks();
                if (_pfSubscribed) { PinActivity.Changed -= OnPlayfieldPins; _pfSubscribed = false; }
            };
            Loaded += (s, e) =>
            {
                if (!_clock.IsEnabled) { _clock.Start(); Tick(); }
                if (!_fx.IsEnabled) _fx.Start();
                if (!_sparkTimer.IsEnabled) _sparkTimer.Start();
                if (_demoPaused) ResumeDemo();   // pick the demo back up where it left off
                if (!_pfSubscribed) { PinActivity.Changed += OnPlayfieldPins; _pfSubscribed = true; }   // light holes as real switches close
                if (WalkMode)
                {
                    SetPlayState(true);
                    LaunchBall();                                   // coin drops in, rests at flipper 1 and WAITS
                    var w = Window.GetWindow(this);
                    if (w != null) w.KeyDown += OnWalkKey;          // SPACE fires the next shot (on your cue)
                }
                if (PickerMode) BuildPicker();                      // per-hole path tester (pick level+hole, SHOOT)
            };
        }

        // ---- Front-panel show-light strip + indicator LEDs ----
        private static readonly Color StripDim = Color.FromArgb(0x40, 0x20, 0x26, 0x34);

        // 5x7 dot font for the header strip letters.
        private static readonly Dictionary<char, string[]> LedFont = new()
        {
            ['S'] = new[] { "#####", "#....", "#....", "#####", "....#", "....#", "#####" },
            ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
            ['I'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####" },
            ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
            ['G'] = new[] { "#####", "#....", "#....", "#.###", "#...#", "#...#", "#####" },
            ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['M'] = new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" },
            ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
        };

        // Lay the strip LEDs out spelling SKILLGAME, ordered left-to-right so the patterns flow across the word.
        private void BuildFrontStrip()
        {
            const double CELL = 9, DOT = 7, LETTERGAP = 7;
            var cells = new List<(double x, double y, int letter)>();
            double ox = 0; int li = 0;
            foreach (char ch in "SKILLGAME")
            {
                if (LedFont.TryGetValue(ch, out var rows))
                    for (int r = 0; r < rows.Length; r++)
                        for (int c = 0; c < rows[r].Length; c++)
                            if (rows[r][c] == '#') cells.Add((ox + c * CELL, r * CELL, li));
                ox += 5 * CELL + LETTERGAP; li++;
            }
            _letterCount = li;
            foreach (var cell in cells.OrderBy(p => p.x).ThenBy(p => p.y))
            {
                var b = new Border { Width = DOT, Height = DOT, CornerRadius = new CornerRadius(DOT / 2), Background = new SolidColorBrush(StripDim) };
                Canvas.SetLeft(b, cell.x + (CELL - DOT) / 2); Canvas.SetTop(b, cell.y + (CELL - DOT) / 2);
                FrontLedHost.Children.Add(b);
                _frontDots.Add(b); _dotLetter.Add(cell.letter);
            }
            FrontLedHost.Width = ox - LETTERGAP; FrontLedHost.Height = 7 * CELL;
        }

        private void BuildIndicators()
        {
            AddIndicator("ATTRACT", "TealBrush");
            AddIndicator("DISTRACT", "PurpleBrush");
            AddIndicator("IN PLAY", "GreenBrush");
            AddIndicator("WINNER", "AmberBrush");
            AddIndicator("TILT", "RedBrush");
        }

        private Border AddDot(Panel host, double size, Color fill)
        {
            var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Margin = new Thickness(2, 0, 2, 0), Background = new SolidColorBrush(fill), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            host.Children.Add(b);
            return b;
        }

        private void AddIndicator(string label, string colorKey)
        {
            var dot = new Ellipse { Width = 11, Height = 11, Fill = (Brush)FindResource("MutedBrush"), VerticalAlignment = VerticalAlignment.Center };
            var lbl = new TextBlock { Text = label, FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 12.5, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(dot); sp.Children.Add(lbl);
            var pill = new Border
            {
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1), Padding = new Thickness(12, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0), Child = sp,
            };
            _indDots[label] = dot; _indLabels[label] = lbl;
            StatusIndHost.Children.Add(pill);
        }

        // state: 0 = off (grey), 1 = on/armed (bright), 2 = active (firing, bright + glow)
        private void SetIndState(string key, int state, string colorKey)
        {
            if (!_indDots.TryGetValue(key, out var dot)) return;
            _indLabels.TryGetValue(key, out var lbl);
            bool toggle = key == "ATTRACT" || key == "DISTRACT";
            if (lbl != null && toggle) lbl.Text = key + (state > 0 ? " ON" : " OFF");
            if (state <= 0)
            {
                dot.Fill = (Brush)FindResource("MutedBrush"); dot.Effect = null;
                if (lbl != null) lbl.Foreground = (Brush)FindResource("MutedBrush");
                return;
            }
            var c = ((SolidColorBrush)FindResource(colorKey)).Color;
            dot.Fill = new SolidColorBrush(c);
            if (lbl != null) lbl.Foreground = new SolidColorBrush(c);
            dot.Effect = state >= 2 ? new System.Windows.Media.Effects.DropShadowEffect { Color = c, BlurRadius = 13, ShadowDepth = 0, Opacity = 0.9 } : null;
        }

        private void StripBurst(string mode, int frames) { _stripMode = mode; _stripBurst = frames; }

        private void FxTick()
        {
            // Distract holds until the next switch clears it; attract holds while its music plays (past a short floor).
            if (_distractHeld)
            {
            }
            else if (_stripMode == "attract")
            {
                if (_stripGrace > 0) _stripGrace--;
                if (_stripGrace <= 0 && !AppState.Audio.IsMusicPlaying)
                {
                    _stripMode = "idle";
                    if (StripLabel != null) StripLabel.Visibility = Visibility.Hidden;
                }
            }
            else if (_stripBurst > 0 && --_stripBurst == 0)
            {
                _stripMode = "idle";
                if (StripLabel != null) StripLabel.Visibility = Visibility.Hidden;   // Hidden (not Collapsed) so the LEDs don't shift
            }
            // Attract/Distract LEDs: dim when the mode is enabled, bright while it's actually firing (change-detected).
            int aState = _stripMode == "attract" ? 2 : ((AppState.AttractLightsOn || AppState.AttractSoundOn) ? 1 : 0);
            int dState = _stripMode == "distract" ? 2 : ((AppState.DistractLightsOn || AppState.DistractSoundOn) ? 1 : 0);
            if (aState != _attractInd) { _attractInd = aState; SetIndState("ATTRACT", aState, "TealBrush"); }
            if (dState != _distractInd) { _distractInd = dState; SetIndState("DISTRACT", dState, "PurpleBrush"); }
            // If the real LED strip is producing frames, mirror its exact colors/pattern across the SKILLGAME word.
            var fr = _ledFrame;
            if (fr != null && fr.Length > 0 && Environment.TickCount64 - _ledFrameTick < 250 && _frontDots.Count > 0)
            {
                int m = _frontDots.Count;
                for (int i = 0; i < m; i++)
                {
                    var sc = fr[(int)((long)i * (fr.Length - 1) / Math.Max(1, m - 1))];
                    ((SolidColorBrush)_frontDots[i].Background).Color = Color.FromRgb(sc.R, sc.G, sc.B);
                }
                return;
            }
            int n = _frontDots.Count, f = ++_fxFrame;
            // Idle "on the fritz" quirks (like an old marquee): flickering bulbs, bright sparks, a whole letter blinking,
            // or a whole letter burned out while the rest stay lit — plus the rare full flash.
            if (_stripMode == "idle" && n > 0)
            {
                if (_quirkFlash > 0) _quirkFlash--;
                foreach (var k in _quirk.Keys.ToList()) if (--_quirk[k] <= 0) _quirk.Remove(k);
                foreach (var k in _spark.Keys.ToList()) if (--_spark[k] <= 0) _spark.Remove(k);
                if (_deadLetterTtl > 0 && --_deadLetterTtl == 0) _deadLetter = -1;
                if (_blinkLetterTtl > 0 && --_blinkLetterTtl == 0) _blinkLetter = -1;

                if (_rnd.Next(28) == 0)
                {
                    int roll = _rnd.Next(100);
                    if (roll < 30) for (int q = _rnd.Next(2) + 1; q > 0; q--) _spark[_rnd.Next(n)] = 2 + _rnd.Next(3);        // spark pops
                    else if (roll < 55) for (int q = _rnd.Next(2) + 1; q > 0; q--) _quirk[_rnd.Next(n)] = 6 + _rnd.Next(12);  // flickering bulbs
                    else if (roll < 74 && _deadLetter < 0 && _letterCount > 0) { _deadLetter = _rnd.Next(_letterCount); _deadLetterTtl = 45 + _rnd.Next(90); }  // a letter burns out for a while
                    else if (roll < 92 && _blinkLetter < 0 && _letterCount > 0) { _blinkLetter = _rnd.Next(_letterCount); _blinkLetterTtl = 16 + _rnd.Next(26); }  // a letter blinks
                    else _quirkFlash = 2;   // rare quick flash of the whole word
                }
                if (_rnd.Next(110) == 0) SpawnSparkBurst(_rnd.Next(n));   // every so often, a real shower of sparks
            }
            else if (_quirk.Count > 0 || _spark.Count > 0 || _quirkFlash > 0 || _deadLetter >= 0 || _blinkLetter >= 0)
            { _quirk.Clear(); _spark.Clear(); _quirkFlash = 0; _deadLetter = -1; _blinkLetter = -1; _deadLetterTtl = _blinkLetterTtl = 0; }
            for (int i = 0; i < n; i++)
            {
                Color c;
                switch (_stripMode)
                {
                    case "attract": c = Mul(FromHue((i * 16 + f * 10) % 360), 1); break;
                    case "distract": c = (f % 2 == 0) ? Mul(Color.FromRgb(0xB4, 0x5A, 0xE0), 1) : StripDim; break;
                    case "score":
                    { int head = (f * 2) % (n + 8); int d = head - i; c = (d >= 0 && d < 8) ? Mul(_stripColor, 1 - d / 8.0) : StripDim; break; }
                    default: c = Mul(FromHue((i * 12 + f * 3) % 360), 0.30); break;   // gentle idle shimmer
                }
                if (_stripMode == "idle")
                {
                    int letter = i < _dotLetter.Count ? _dotLetter[i] : -1;
                    if (_quirkFlash > 0) c = Mul(Colors.White, 0.85);
                    else if (_spark.TryGetValue(i, out var sf)) c = sf > 1 ? Colors.White : Mul(Colors.White, 0.55);   // bright spark, quick decay
                    else if (letter == _deadLetter) c = StripDim;                                                     // whole letter burned out
                    else if (letter == _blinkLetter) c = (f / 5 % 2 == 0) ? Mul(FromHue((i * 12) % 360), 0.55) : StripDim;   // whole letter blinking
                    else if (_quirk.TryGetValue(i, out var qf)) c = (qf % 2 == 0) ? Mul(FromHue((i * 37) % 360), 1.0) : StripDim;   // dying-bulb flicker
                }
                ((SolidColorBrush)_frontDots[i].Background).Color = c;   // reuse brush (no per-frame allocation)
            }
        }
        // ---- real spark particles that shower out of the sign when it "shorts out" ----
        private sealed class Spark { public Ellipse El = null!; public double X, Y, Vx, Vy; public int Life, Max; }
        private readonly DispatcherTimer _sparkTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly List<Spark> _sparks = new();

        private void SparkTick()
        {
            if (_sparks.Count == 0) return;
            for (int i = _sparks.Count - 1; i >= 0; i--)
            {
                var s = _sparks[i];
                s.X += s.Vx; s.Y += s.Vy; s.Vy += 0.85; s.Vx *= 0.99;   // gravity + a little drag
                s.Life--;
                Canvas.SetLeft(s.El, s.X); Canvas.SetTop(s.El, s.Y);
                s.El.Opacity = Math.Max(0, (double)s.Life / s.Max);
                if (s.Life <= 0) { SparkLayer.Children.Remove(s.El); _sparks.RemoveAt(i); }
            }
        }

        // A shower of sparks bursting from one bulb (as if that spot just shorted out).
        private void SpawnSparkBurst(int dotIndex)
        {
            if (SparkLayer == null || dotIndex < 0 || dotIndex >= _frontDots.Count) return;
            var dot = _frontDots[dotIndex];
            Point p;
            try { p = dot.TransformToVisual(SparkLayer).Transform(new Point(dot.ActualWidth / 2, dot.ActualHeight / 2)); }
            catch { return; }
            int count = 7 + _rnd.Next(8);
            for (int k = 0; k < count; k++)
            {
                var el = new Ellipse
                {
                    Width = 3, Height = 3, IsHitTestVisible = false,
                    Fill = new SolidColorBrush(_rnd.Next(3) == 0 ? Colors.White : Color.FromRgb(0xFF, (byte)(0xC0 + _rnd.Next(0x40)), 0x28)),
                };
                Canvas.SetLeft(el, p.X); Canvas.SetTop(el, p.Y);
                SparkLayer.Children.Add(el);
                double ang = -Math.PI / 2 + (_rnd.NextDouble() - 0.5) * 2.4;   // mostly up-and-out
                double spd = 2.5 + _rnd.NextDouble() * 5.0;
                int life = 12 + _rnd.Next(16);
                _sparks.Add(new Spark { El = el, X = p.X, Y = p.Y, Vx = Math.Cos(ang) * spd, Vy = Math.Sin(ang) * spd, Life = life, Max = life });
            }
            _quirkFlash = Math.Max(_quirkFlash, 1);   // the shorting bulb flashes white as it pops
        }

        private void ClearSparks()
        {
            foreach (var s in _sparks) SparkLayer.Children.Remove(s.El);
            _sparks.Clear();
        }

        private readonly Dictionary<int, int> _quirk = new();   // idle flicker: dot index -> frames left
        private readonly Dictionary<int, int> _spark = new();   // idle spark: dot index -> frames left (bright white pop)
        private int _quirkFlash;                                 // idle: frames of a quick full-strip flash
        private readonly List<int> _dotLetter = new();           // which SKILLGAME letter each dot belongs to
        private int _letterCount;
        private int _deadLetter = -1, _deadLetterTtl;            // a whole letter gone dark while the rest stay lit
        private int _blinkLetter = -1, _blinkLetterTtl;          // a whole letter blinking on/off

        // Fired from the LED engine (off-thread) after each strip update; just stash the frame for FxTick to paint.
        private void OnLedFrame(System.Drawing.Color[] frame) { _ledFrame = frame; _ledFrameTick = Environment.TickCount64; }

        private void BenchToggle_Click(object sender, RoutedEventArgs e)
        {
            bool on = ((System.Windows.Controls.CheckBox)sender).IsChecked == true;
            AppState.SetBenchMode(on);   // on = clear stuck + go quiet; off = re-baseline so stuck/floating re-report
            System.Threading.Tasks.Task.Run(() => AppState.Audio.PlaySettingsSound(on ? "soundfxon" : "soundfxoff"));
        }

        private static Color Mul(Color c, double f) { f = Math.Max(0, Math.Min(1, f)); return Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f)); }
        private static Color FromHue(double h)
        {
            double x = 1 - Math.Abs((h / 60.0) % 2 - 1); double r, g, b;
            if (h < 60) { r = 1; g = x; b = 0; } else if (h < 120) { r = x; g = 1; b = 0; }
            else if (h < 180) { r = 0; g = 1; b = x; } else if (h < 240) { r = 0; g = x; b = 1; }
            else if (h < 300) { r = x; g = 0; b = 1; } else { r = 1; g = 0; b = x; }
            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        // The coordinator's real engine (wired to the FT232H lamps/sounds) takes over the DEMO button.
        public void AttachEngine(GameEngine engine) => _activeEngine = engine;

        // True while a demo game is running, so attract stays quiet during it.
        public bool DemoActive => _demoCts != null && !_demoCts.IsCancellationRequested;

        // Draw digits in the seven-segment font and letters in the UI font so switch ids read clearly (S12 not 512).
        private void SetMixedText(TextBlock tb, string text)
        {
            tb.Inlines.Clear();
            var seven = (FontFamily)FindResource("DisplayFont");
            var ui = (FontFamily)FindResource("UiFont");
            foreach (char ch in text)
                tb.Inlines.Add(new System.Windows.Documents.Run(ch.ToString()) { FontFamily = char.IsDigit(ch) ? seven : ui });
        }

        // Coordinator calls this when attract/distract fires; shows the label over the header strip and bursts the animation.
        public void FlashMode(string text, string colorKey)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => FlashMode(text, colorKey)); return; }
            var c = ((SolidColorBrush)FindResource(colorKey)).Color;
            StripLabel.Text = text;
            StripLabel.Foreground = new SolidColorBrush(c);
            StripLabel.Visibility = Visibility.Visible;
            if (text.Contains("ATTRACT")) { _stripMode = "attract"; _distractHeld = false; _stripGrace = 18; _stripBurst = 0; }
            else if (text.Contains("DISTRACT")) { _stripMode = "distract"; _distractHeld = true; _stripBurst = 0; }
        }

        // Stop the on-screen distract show and its music the moment the next switch is hit.
        public void StopDistractShow()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(StopDistractShow)); return; }
            _distractHeld = false; _stripBurst = 0; _stripMode = "idle";
            if (StripLabel != null) StripLabel.Visibility = Visibility.Hidden;
            AppState.Audio.StopMusicNow();   // cut the music the instant the switch activates
        }

        // In-game: name the event on the top show-strip and run a matching-colour show there (like attract/distract).
        private void ShowGameEvent(string label, string colorKey)
        {
            var c = ((SolidColorBrush)FindResource(colorKey)).Color;
            _stripColor = c;
            StripLabel.Text = label;
            StripLabel.Foreground = new SolidColorBrush(c);
            StripLabel.Visibility = Visibility.Visible;
            StripBurst("score", 22);
        }

        // A giant screen takeover for WINNER/GAME OVER/TILT; hardShake bobs violently, otherwise gently, flashing colour-to-white then fading.
        private void ShowBigBanner(string text, Color color, bool hardShake)
        {
            if (Content is not Grid root) return;
            StripBurst("score", 30);
            var scrim = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0x88, (byte)(color.R / 3), (byte)(color.G / 3), (byte)(color.B / 3))), IsHitTestVisible = false, Opacity = 0 };
            Panel.SetZIndex(scrim, 1999); root.Children.Add(scrim);
            var tb = new TextBlock
            {
                Text = text, FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 210, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color), TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = color, BlurRadius = 22, ShadowDepth = 0, Opacity = 1 }
            };
            var st = new ScaleTransform(0.3, 0.3); var tt = new TranslateTransform();
            var tg = new TransformGroup(); tg.Children.Add(st); tg.Children.Add(tt);
            tb.RenderTransform = tg; tb.RenderTransformOrigin = new Point(0.5, 0.5);
            Panel.SetZIndex(tb, 2000); root.Children.Add(tb);

            scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            tb.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            var pop = new DoubleAnimation(0.3, hardShake ? 1.25 : 1.35, TimeSpan.FromMilliseconds(460)) { EasingFunction = new BackEase { Amplitude = 0.8, EasingMode = EasingMode.EaseOut } };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            var shake = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            double[] offs = hardShake ? new double[] { 0, -26, 22, -18, 16, -12, 10, -6, 4, 0 } : new double[] { 0, -6, 6, -4, 4, 0 };
            for (int i = 0; i < offs.Length; i++) shake.KeyFrames.Add(new LinearDoubleKeyFrame(offs[i], KeyTime.FromPercent(i / (double)(offs.Length - 1))));
            shake.Duration = TimeSpan.FromMilliseconds(hardShake ? 220 : 900);
            tt.BeginAnimation(hardShake ? TranslateTransform.XProperty : TranslateTransform.YProperty, shake);
            var flash = new ColorAnimation(color, Colors.White, TimeSpan.FromMilliseconds(200)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            ((SolidColorBrush)tb.Foreground).BeginAnimation(SolidColorBrush.ColorProperty, flash);

            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500));
                fade.Completed += (a, b) => { root.Children.Remove(tb); root.Children.Remove(scrim); };
                tb.BeginAnimation(OpacityProperty, fade);
                scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500)));
            };
            t.Start();
        }

        // ---- IGameView (called by the game rules, possibly off-thread) ----
        public string SoundPackage => AppState.SoundPackage;

        public void ShowStatus(int score, int level, string lastSwitch, string status)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => ShowStatus(score, level, lastSwitch, status)); return; }
            string hitForLevel = (lastSwitch ?? "").Split(' ')[0];
            int shotLevel = LevelOf(hitForLevel);
            bool inProg0 = status.Contains("Progress");
            bool winnerNow = status.Contains("Winner");
            bool demoActive = _demoCts != null && !_demoCts.IsCancellationRequested;
            // In the demo, a scoring shot holds the score, lights, sound and hole-flash until the coin drops through (all fire together in PhysTick).
            bool scoringShot = demoActive && (inProg0 || winnerNow) && shotLevel > 0 && hitForLevel != "S23" && _holeDisc.ContainsKey(hitForLevel);
            if (scoringShot) _pendingScore = score;
            else { ScoreText.Text = score.ToString(); UpdateScoreLights(score); _pendingScore = -1; }
            // LEVEL shows the level being shot and only advances once the coin rests at the next one (in PhysTick), never ahead of the coin.
            if (shotLevel > 0) { LevelText.Text = Math.Min(8, shotLevel).ToString(); if (inProg0) _restLevelPending = Math.Min(8, shotLevel + 1); }
            else if (!demoActive || !inProg0) LevelText.Text = Math.Max(0, level).ToString();
            // LAST SWITCH also waits for the coin to drop through on a scoring shot (revealed in PhysTick).
            if (scoringShot) _pendingSwitch = lastSwitch;
            else SetMixedText(LastSwitchText, string.IsNullOrEmpty(lastSwitch) ? "None" : lastSwitch);
            StatusText.Text = status;
            StatusDot.Fill = status.Contains("Progress") ? (Brush)FindResource("GreenBrush")
                           : status.Contains("Over") || status.Contains("Winner") ? (Brush)FindResource("AmberBrush")
                           : (Brush)FindResource("MutedBrush");
            SetIndState("IN PLAY", status.Contains("Progress") ? 2 : 0, "GreenBrush");
            SetIndState("WINNER", status.Contains("Winner") ? 2 : 0, "AmberBrush");
            SetIndState("TILT", (lastSwitch ?? "").Contains("Tilt") ? 2 : 0, "RedBrush");   // tilt status reads "Game Over"; the tilt is in the switch

            bool inProg = status.Contains("Progress");
            bool winner = status.Contains("Winner");
            bool started = inProg && !_wasInProgress;   // a fresh game just began (real coin or demo)
            bool ended = !inProg && _wasInProgress;     // a game just finished (win / loss / tilt)
            _wasInProgress = inProg;

            if (started)
            {
                // A game starting kills any attract/background music immediately, then plays the coin sound.
                System.Threading.Tasks.Task.Run(() =>
                {
                    AppState.Audio.StopMusicClip();     // attract clips play on the music player
                    AppState.Audio.StopBackground();    // any screen-theme bg
                    AppState.Audio.PlaySettingsSound("coin");
                });
                _highAtStart = CurrentHigh();
                _beatHighFired = false;
                _lastTiltWarn = null;
                LastGameText.Visibility = Visibility.Collapsed;
                AnimateCoinDrop(() => SetPlayState(true));   // coin drops into the slot, then the INSERT COIN panel hides
            }

            bool tilt = (lastSwitch ?? "").Contains("Tilt");
            // A nudge below the tilt limit is a warning: small shake, no game-over. The engine's sink plays the chime.
            if (tilt && inProg && (lastSwitch ?? "").Contains("WARNING") && lastSwitch != _lastTiltWarn)
            {
                _lastTiltWarn = lastSwitch;
                TiltWarning();
            }
            // Robot stays happy during play; win/mad moods are set at game end (FinalizeEnd), deferred until the final coin drops.
            if (!_sick && inProg && _lastRobotMood != "happy") { _lastRobotMood = "happy"; Robot?.SetMood("happy"); }

            // Playfield active-row and high-score chase while a game is live; the chase bar waits for the coin-drop on a scoring shot (in PhysTick).
            if (inProg || winner) { SetActiveRow(shotLevel > 0 ? shotLevel : level); if (!scoringShot) UpdateChase(score); }
            // The coin is fired up the scoring rail, drops through the scored hole, and rolls down to the next flipper.
            if (started) LaunchBall();                                       // drop a fresh coin in at the top-left slot
            string hitId = (lastSwitch ?? "").Split(' ')[0];                 // "S3 - 50 Points" -> "S3"
            if (ended && !winner && !tilt) FireGobblePath();                 // a LOSS drains the coin into a gobble hole
            else if (!started && hitId != "S0" && hitId.Length > 1 && hitId[0] == 'S' && hitId != "S23" && _holeDisc.ContainsKey(hitId)) FireCoin(hitId);   // coin-up (S0) just drops in — no shot, no handle

            // NEW HIGH SCORE mid-game (only when there was a prior best to beat).
            if (inProg && _highAtStart > 0 && score > _highAtStart && !_beatHighFired)
            {
                _beatHighFired = true;
                ShowNewHighBanner($"PASSED {_highAtStart}  ·  NOW {score}");
            }

            if (ended)
            {
                // Tilt has no coin drop, so stop the distract here; win/loss stop when their final coin drops (PhysTick).
                if (tilt && _demoDistractActive) { _demoDistractActive = false; StopDistractShow(); }
                string outcome = winner ? "win" : tilt ? "tilt" : "lose";
                bool coinAnimating = _ballLive && _coinPath.Count >= 2 && _coinDist < _coinLen;
                if (!tilt && coinAnimating)
                {
                    // WIN/LOSS: hold the end sequence (banner, robot, GAME OVER text) until the final coin drops through.
                    _endIdlePending = true; _pendingEnd = outcome; _pendingEndScore = score;
                }
                else
                {
                    _endIdlePending = false; if (tilt) StopBall(); SetPlayState(false);   // tilt freezes instantly
                    FinalizeEnd(outcome, score);
                }
            }

            if (!scoringShot && !ended) { ReactToScore(score); PulseScore(); }
        }

        private int _pendingScore = -1;   // demo: score to reveal when the coin drops through (-1 = none)
        private string? _pendingSwitch;   // demo: LAST SWITCH text to reveal when the coin drops through
        private string? _pendingEnd;      // demo: end outcome ("win"/"lose") to declare when the final coin drops
        private int _pendingEndScore;

        // Declare the game result — robot mood, LAST GAME text, high-score, leaderboard, and the giant banner.
        private void FinalizeEnd(string outcome, int score)
        {
            if (!_sick)
            {
                string mood = outcome == "win" ? "win" : "mad";
                if (mood != _lastRobotMood) { _lastRobotMood = mood; Robot?.SetMood(mood); if (mood == "win") Robot?.Celebrate(true); else Robot?.Shake(); }
            }
            if (score > 0)
            {
                string lbl = outcome == "win" ? "WINNER" : outcome == "tilt" ? "TILT" : "GAME OVER";
                LastGameText.Text = $"LAST GAME:  {score}  ·  {lbl}";
                LastGameText.Visibility = Visibility.Visible;
                if (!_beatHighFired && score > _highAtStart) { _beatHighFired = true; ShowNewHighBanner($"SCORE  {score}"); }
            }
            RefreshLeaders();
            string pkg = AppState.SoundPackage;
            if (outcome == "win") { ShowBigBanner("WINNER!", Color.FromRgb(0xFF, 0xC8, 0x30), false); ConfettiBurst(); }
            else if (outcome == "tilt") { ShowBigBanner("TILT", Color.FromRgb(0xFF, 0x35, 0x2A), true); ShakePlayfield(22); }   // the engine's sink plays the tilt sound
            else { ShowBigBanner("GAME OVER", Color.FromRgb(0xFF, 0x4A, 0x3A), false); if (AppState.SoundFxOn) AppState.Audio.PlaySound(pkg, "Game Over"); }
            PulseScore();
        }
        private bool _wasInProgress;
        private string? _lastTiltWarn;

        // Shake the whole playfield (a nudge/tilt jolt). amp = how hard, in canvas px.
        private void ShakePlayfield(double amp)
        {
            if (PlayfieldShake == null) return;
            var kx = new DoubleAnimationUsingKeyFrames();
            var ky = new DoubleAnimationUsingKeyFrames();
            var rnd = _rnd;
            for (int i = 0; i <= 10; i++)
            {
                double f = 1 - i / 11.0;                                  // decay
                double x = (i == 10) ? 0 : (rnd.NextDouble() * 2 - 1) * amp * f;
                double y = (i == 10) ? 0 : (rnd.NextDouble() * 2 - 1) * amp * 0.6 * f;
                var kt = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 45));
                kx.KeyFrames.Add(new LinearDoubleKeyFrame(x, kt));
                ky.KeyFrames.Add(new LinearDoubleKeyFrame(y, kt));
            }
            PlayfieldShake.BeginAnimation(TranslateTransform.XProperty, kx);
            PlayfieldShake.BeginAnimation(TranslateTransform.YProperty, ky);
        }

        // A tilt WARNING (nudge under the limit): a quick red "TILT!" flash + shake, no game-over. The chime comes from the engine's sink.
        private void TiltWarning()
        {
            ShakePlayfield(10);
            if (!_sick) Robot?.Shake();
            if (Content is not Grid root) return;
            var red = ((SolidColorBrush)FindResource("RedBrush")).Color;
            var tt = new TranslateTransform();
            var tb = new TextBlock
            {
                Text = "TILT!", FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 92, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(red), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0, RenderTransform = tt, RenderTransformOrigin = new Point(0.5, 0.5), IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = red, BlurRadius = 18, ShadowDepth = 0, Opacity = 1 },
            };
            Panel.SetZIndex(tb, 1998); root.Children.Add(tb);
            tb.BeginAnimation(OpacityProperty, new DoubleAnimation(0.95, 0, TimeSpan.FromMilliseconds(600)) { BeginTime = TimeSpan.FromMilliseconds(70) });
            var shake = new DoubleAnimationUsingKeyFrames();
            foreach (var kf in new[] { (0, 0.0), (55, -16.0), (110, 13.0), (165, -8.0), (220, 4.0), (290, 0.0) })
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(kf.Item2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(kf.Item1))));
            tt.BeginAnimation(TranslateTransform.XProperty, shake);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(760) };
            t.Tick += (s, e) => { t.Stop(); root.Children.Remove(tb); };
            t.Start();
        }
        private bool _sick;

        // Coordinator's board-presence watch: the robot turns sick (red X-eyes) while any board is missing, until all are found.
        public void SetHardwareHealthy(bool ok) => SetHardwareHealthy(ok ? new[] { true, true, true, true } : new[] { false, false, false, false });

        public void SetHardwareHealthy(bool[] present)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => SetHardwareHealthy(present))); return; }
            bool ok = present != null && present.Length >= 4 && present[0] && present[1] && present[2] && present[3];
            // under-robot readout: green STATUS: OK when all good, else which boards are missing in red
            if (RobotAlert != null)
            {
                RobotAlert.Text = ok ? "STATUS: OK" : MainWindow.MissingBoardsText(present).ToUpperInvariant();
                var c = ok ? Color.FromRgb(0x2F, 0xE0, 0x8C) : Color.FromRgb(0xE2, 0x49, 0x3C);
                RobotAlert.Foreground = new SolidColorBrush(c);
                if (RobotAlert.Effect is System.Windows.Media.Effects.DropShadowEffect dse) dse.Color = c;
                RobotAlert.Visibility = Visibility.Visible;
            }
            bool sick = !ok;
            if (sick == _sick) return;
            _sick = sick;
            _lastRobotMood = sick ? "sick" : "happy";
            Robot?.SetMood(sick ? "sick" : "happy");
        }


        // ---- High-score chase bar + top-5 leaderboard ----
        private int CurrentHigh()
        {
            var hs = _audits.Data.HighScores;
            return hs != null && hs.Count > 0 ? hs[0].Score : 0;
        }

        private void UpdateChase(int score)
        {
            int high = CurrentHigh();
            double frac = high > 0 ? Math.Min(1.0, score / (double)high) : (score > 0 ? 1.0 : 0.0);
            ChaseFillCol.Width = new GridLength(frac, GridUnitType.Star);
            ChaseEmptyCol.Width = new GridLength(1 - frac, GridUnitType.Star);
            if (high <= 0) { ChaseText.Text = "FIRST ON THE BOARD!"; ChaseText.Foreground = (Brush)FindResource("GoldBrush"); }
            else if (score >= high) { ChaseText.Text = "BEST EVER · KEEP GOING!"; ChaseText.Foreground = (Brush)FindResource("GoldBrush"); }
            else { ChaseText.Text = $"{high - score} TO BEAT IT!"; ChaseText.Foreground = (Brush)FindResource("GreenBrush"); }
        }

        private void RefreshLeaders()
        {
            var hs = _audits.Data.HighScores;
            HighScoreText.Text = hs != null && hs.Count > 0 ? hs[0].Score.ToString() : "---";
            LeaderHost.Children.Clear();
            if (hs == null) return;
            for (int i = 1; i < hs.Count && i < 5; i++)
                LeaderHost.Children.Add(LeaderRow(i + 1, hs[i].Score, hs[i].When));
        }

        private UIElement LeaderRow(int rank, int score, string when)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Color medal = rank == 2 ? Color.FromRgb(0xC4, 0xCA, 0xD4) : rank == 3 ? Color.FromRgb(0xCD, 0x8B, 0x4E) : Color.FromRgb(0x2A, 0x30, 0x3C);
            bool bright = rank <= 3;
            var r = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(medal),
                Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = rank.ToString(), FontFamily = (FontFamily)FindResource("UiFont"), FontWeight = FontWeights.Bold, FontSize = 12, Foreground = new SolidColorBrush(bright ? Color.FromRgb(0x1A, 0x1A, 0x22) : Color.FromRgb(0xB8, 0xBE, 0xCC)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var s = new TextBlock { Text = score.ToString(), FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 22, Foreground = (Brush)FindResource("AmberBrush"), VerticalAlignment = VerticalAlignment.Center };
            var w = new TextBlock { Text = when, FontFamily = (FontFamily)FindResource("UiFont"), FontSize = 11, Foreground = (Brush)FindResource("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(s, 1); Grid.SetColumn(w, 2);
            g.Children.Add(r); g.Children.Add(s); g.Children.Add(w);
            return g;
        }

        // ---- NEW HIGH SCORE banner ----
        private void ShowNewHighBanner(string sub)
        {
            NewHighSub.Text = sub;
            NewHighBanner.Opacity = 1;
            NewHighBanner.Visibility = Visibility.Visible;
            var pop = new DoubleAnimation(0.6, 1.0, TimeSpan.FromMilliseconds(300)) { EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } };
            BannerScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            BannerScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            ConfettiBurst();
            BurstSparkles();
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.4) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(450));
                fade.Completed += (a, b) => { NewHighBanner.Visibility = Visibility.Collapsed; NewHighBanner.Opacity = 1; };
                NewHighBanner.BeginAnimation(OpacityProperty, fade);
            };
            t.Start();
        }

        public void ShowScoreLevel(int score, int level)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => ShowScoreLevel(score, level)); return; }
            ScoreText.Text = score.ToString();
            LevelText.Text = level.ToString();
            UpdateScoreLights(score);
            if (_wasInProgress) { SetActiveRow(level); UpdateChase(score); }
            ReactToScore(score);
            PulseScore();
        }

        // Score reaction: robot gets excited + wiggles; a big hit (>= 50) gets a wilder reaction + sparkles.
        private void ReactToScore(int score)
        {
            int delta = score - _lastScoreShown;
            _lastScoreShown = score;
            if (delta > 0)
            {
                if (!_sick) Robot?.React(delta >= 50);
                ShowGameEvent($"♪  {delta} PTS", delta >= 50 ? "GoldBrush" : "GreenBrush");   // the scoring sound + a matching show
            }
            if (delta >= 50) BurstSparkles();
        }

        // A quick pop and odometer-style roll when the score changes; no DropShadow animation since re-rendering the blur every frame is expensive on the big score.
        private void PulseScore()
        {
            var pop = new DoubleAnimation(1.14, 1.0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
            ScoreScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            ScoreScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            var roll = new DoubleAnimation(22, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut } };
            ScoreRoll.BeginAnimation(TranslateTransform.YProperty, roll);
        }

        // ---- Sparkle burst over the score (big hit) ----
        private void BurstSparkles()
        {
            if (SparkleCanvas.ActualWidth <= 0) return;
            Point c;
            try
            {
                var b = ScoreText.TransformToVisual(SparkleCanvas).TransformBounds(new Rect(ScoreText.RenderSize));
                c = new Point(b.X + b.Width * 0.45, b.Y + b.Height * 0.5);
            }
            catch { c = new Point(SparkleCanvas.ActualWidth * 0.3, SparkleCanvas.ActualHeight * 0.4); }

            string[] keys = { "GoldBrush", "TealBrush", "CyanBrush", "GreenBrush", "AmberBrush" };
            for (int i = 0; i < 16; i++)
            {
                var dot = new Ellipse
                {
                    Width = 8, Height = 8,
                    Fill = (Brush)FindResource(keys[_rnd.Next(keys.Length)]),
                };
                Canvas.SetLeft(dot, c.X); Canvas.SetTop(dot, c.Y);
                SparkleCanvas.Children.Add(dot);

                double ang = _rnd.NextDouble() * Math.PI * 2;
                double dist = 60 + _rnd.Next(120);
                double dur = 0.5 + _rnd.NextDouble() * 0.4;
                Animate(dot, Canvas.LeftProperty, c.X, c.X + Math.Cos(ang) * dist, dur);
                Animate(dot, Canvas.TopProperty, c.Y, c.Y + Math.Sin(ang) * dist, dur);
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(dur)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                fade.Completed += (s, e) => SparkleCanvas.Children.Remove(dot);
                dot.BeginAnimation(OpacityProperty, fade);
            }
        }

        private static void Animate(UIElement el, DependencyProperty p, double from, double to, double seconds)
        {
            var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            el.BeginAnimation(p, a);
        }

        // ---- Confetti + lamp-fireworks feel on a winner ----
        private void ConfettiBurst()
        {
            double w = ConfettiCanvas.ActualWidth, h = ConfettiCanvas.ActualHeight;
            if (w <= 0) return;
            string[] keys = { "GoldBrush", "TealBrush", "CyanBrush", "GreenBrush", "AmberBrush", "PurpleBrush", "RedBrush" };
            for (int i = 0; i < 60; i++)
            {
                var piece = new Rectangle
                {
                    Width = 7 + _rnd.Next(6), Height = 10 + _rnd.Next(8),
                    Fill = (Brush)FindResource(keys[_rnd.Next(keys.Length)]),
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new RotateTransform(_rnd.Next(360)),
                };
                double x = _rnd.NextDouble() * w;
                Canvas.SetLeft(piece, x); Canvas.SetTop(piece, -20);
                ConfettiCanvas.Children.Add(piece);

                double dur = 1.6 + _rnd.NextDouble() * 1.4;
                var fall = new DoubleAnimation(-20, h + 30, TimeSpan.FromSeconds(dur)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                fall.Completed += (s, e) => ConfettiCanvas.Children.Remove(piece);
                piece.BeginAnimation(Canvas.TopProperty, fall);
                Animate(piece, Canvas.LeftProperty, x, x + (_rnd.NextDouble() * 120 - 60), dur);
                var spin = new DoubleAnimation(_rnd.Next(360), _rnd.Next(360) + 360 * (_rnd.Next(2) == 0 ? 1 : -1), TimeSpan.FromSeconds(dur));
                ((RotateTransform)piece.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, spin);
            }
        }

        private void Tick()
        {
            var now = DateTime.Now;
            ClockText.Text = now.ToString("h:mm:ss tt");
            DateText.Text = now.ToString("dddd, MMMM d");
            RefreshLeaders();
            SessionText.Text = $"{_audits.SessionGames} games · {_audits.SessionHits} hits";
            SoundSetText.Text = AppState.SoundPackage.ToUpperInvariant();
        }

        // Clicking the coin slot / INSERT COIN drops a coin and kicks off a demo (when idle).
        private void InsertCoin_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DemoActive) return;                                        // a demo's already running
            if (_activeEngine != null && _activeEngine.GameInProgress) return;   // a real game is on
            DemoButton_Click(DemoButton, new RoutedEventArgs());           // same as pressing DEMO GAME
        }

        private async void DemoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_demoCts != null && !_demoCts.IsCancellationRequested)
            {
                _demoCts.Cancel();
                _demoPaused = false; _resume?.TrySetResult(true); _resume = null;   // release a paused loop so it exits
                var eng = _activeEngine;
                _ = Task.Run(() => { try { eng.ClearGame(); AppState.Audio.StopAll(); } catch (Exception ex) { Log.Error("demo stop clear failed", ex); } });   // lamp-clear + kill all audio off the UI thread
                // Reset straight to idle — pull the coin, drop any pending shot/end, and don't fire a game-over.
                StopBall();
                StopDistractShow();
                _demoDistractActive = false;
                _pendingEnd = null; _pendingScore = -1; _pendingSwitch = null; _pendingHole = null;
                _pendingFired = true; _soundFired = true; _endIdlePending = false; _restLevelPending = -1;
                SetActiveRow(0);
                _lastRobotMood = ""; if (!_sick) Robot?.SetMood("happy");
                _wasInProgress = false;   // idle status must not look like a game just ended
                SetPlayState(false);
                ShowStatus(0, 0, "None", "Game Not Started");
                DemoButton.Content = "DEMO GAME";
                return;
            }
            _demoCts = new CancellationTokenSource();   // DemoActive now true → hardware switches stop reaching the engine
            _demoDistractActive = false;   // fresh demo — no distract running yet
            _demoPaused = false; _resume = null;
            DemoButton.Content = "STOP DEMO";
            // Start from a clean slate so leftover (phantom-switch) game state can't suppress the coin-up.
            var startEng = _activeEngine;
            _ = Task.Run(() => { try { startEng.ClearGame(); } catch (Exception ex) { Log.Error("demo clear failed", ex); } });
            StopBall();
            _pendingEnd = null; _pendingScore = -1; _pendingSwitch = null; _pendingHole = null;
            _pendingFired = true; _soundFired = true; _endIdlePending = false; _restLevelPending = -1;
            _wasInProgress = false;
            SetActiveRow(0);
            SetPlayState(false);
            var ending = DemoEndingCombo.SelectedIndex switch { 1 => DemoEnding.Lose, 2 => DemoEnding.Tilt, _ => DemoEnding.Winner };
            // A slower beat between scores lets the count-up, lamp show and sound play out; the game loop runs on a background thread and the view marshals back to the UI, so nothing blocks it.
            try { await Task.Run(() => DemoGame.RunAsync(_activeEngine, 4500, _demoCts.Token, ending, DemoAwaitResume), _demoCts.Token); }   // beat between shots; distract holes linger longer
            catch (OperationCanceledException) { }
            finally { _demoCts = null; DemoButton.Content = "DEMO GAME"; Tick(); }   // reset so the next click starts a fresh demo (no double-click)
        }
    }
}
