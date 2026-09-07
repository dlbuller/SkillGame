using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using SkillGame;

namespace SkillGameWpf
{
    public partial class DiagnosticsView : UserControl
    {
        // Physical backglass order: tens 10..90 (left→right), then hundreds reels 100..400.
        private static readonly int[] TensVals = { 10, 20, 30, 40, 50, 60, 70, 80, 90 };
        private static readonly int[] HundredsVals = { 100, 200, 300, 400 };
        private static readonly int[] Order = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 200, 300, 400 };

        // Preview lamp controller (no boards) until the coordinator hands over the real one.
        private static readonly int[] G3Out = { 8, 9, 10, 11, 12, 13, 14, 15 };
        private static readonly int[] G4Out = { 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 };

        private readonly Dictionary<int, Border> _lampBorder = new();
        private readonly Dictionary<int, TextBlock> _lampText = new();
        private readonly Dictionary<int, (int board, int pin)> _lampLoc = new();
        private readonly Dictionary<int, bool> _lampState = new();
        private readonly Dictionary<string, bool> _switchState = new();
        private readonly Dictionary<string, bool> _specialState = new();
        private bool _refreshQueued;
        private readonly Dictionary<string, Border> _switchTiles = new();
        private readonly Dictionary<string, Ellipse> _pillDot = new();
        private TextBlock? _postText;

        private LampController _lamps = new(null, null, G3Out, G4Out);
        private HardwareCoordinator? _coord;
        private double _brightness = 1.0;
        private bool _hasHardware;
        private bool _subscribed;
        private Button? _activePatternBtn;
        private bool _soundActive;

        private readonly List<Border> _vuBars = new();
        private readonly DispatcherTimer _vuTimer = new() { Interval = TimeSpan.FromMilliseconds(55) };
        private readonly Random _vuRnd = new();

        private readonly List<Border> _ledDots = new();
        private readonly Dictionary<string, Border> _specialLampBorder = new();
        private readonly Dictionary<string, TextBlock> _specialLampText = new();
        private readonly Dictionary<string, Ellipse> _solDots = new();
        // Latched solenoids: name -> (auto-off timer, its button); press again or ALL OFF / leave to release.
        private readonly Dictionary<string, (DispatcherTimer timer, Button btn)> _solLatch = new();
        private const int SolenoidLatchSeconds = 4;

        // Switch health: per-switch hit counts, last-fired + stuck detection; a live event log; poll-rate readout.
        private readonly DispatcherTimer _healthTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly Dictionary<string, TextBlock> _switchHitText = new();
        private readonly Dictionary<string, long> _switchHits = new();
        private readonly Dictionary<string, long> _switchLastFiredTick = new();
        private readonly HashSet<string> _stuck = new();   // mirror of Faults for the tiles/gremlin/self-test (the coordinator owns detection)
        private readonly Dictionary<string, bool> _solLogState = new();           // change-detect solenoids for the event log
        private readonly List<string> _events = new();
        private long _lastPollCycles;
        private DateTime _lastPollSample = DateTime.UtcNow;
        private bool _identifyMode;
        // Lamps set to blink steadily while IDENTIFY is on, toggled by _identifyTimer.
        private readonly DispatcherTimer _identifyTimer = new() { Interval = TimeSpan.FromMilliseconds(320) };
        private readonly HashSet<string> _identifyBlink = new();
        private bool _identifyPhase;
        private Button? _activeColorBtn;
        private readonly DispatcherTimer _soundTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
        private readonly DispatcherTimer _ledTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
        private double _ledBright = 1.0;
        private Color _ledColor = Colors.White;

        // Live signal scope below the LAMPS LIT ring: drifting carrier + grass + sporadic pulses so it never repeats.
        private readonly DispatcherTimer _scopeTimer = new() { Interval = TimeSpan.FromMilliseconds(45) };
        private readonly Random _scopeRnd = new();
        private Polyline? _scopeLine;
        private const int ScopeSamples = 130;
        private readonly double[] _scope = new double[ScopeSamples];
        private double _scopePhase;
        private double _scopeEnergy;      // decays each tick; bumped on activity
        private double _scopeA1 = 0.12, _scopeA2 = 0.06, _scopeDrift;   // slowly random-walking carrier
        private int _beatTtl, _beatLen;   // a travelling pulse currently entering the trace
        private double _beatAmp; private int _beatSign;

        // The special reels/lamps that live on the map, and the solenoids that get indicator LEDs.
        private static readonly (string name, string label, string accent)[] SpecialLamps =
            { ("Winner", "WINNER", "AmberBrush"), ("GameOver", "GAME OVER", "GoldBrush"), ("Tilt", "TILT", "RedBrush") };
        private static readonly (string name, string label)[] Solenoids =
            { ("WinnerLock", "WINNER LOCK"), ("CoinLock", "COIN LOCK") };

        public DiagnosticsView()
        {
            InitializeComponent();
            BuildPills();
            BuildSwitches();
            BuildLamps();
            BuildSpecialLamps();
            BuildVu();
            BuildSolenoidInd();
            BuildLedStrip();
            LedPatternCombo.ItemsSource = LedPatterns;
            LedPatternCombo.SelectedIndex = 0;   // default shown; on-screen preview only (no hardware) at load
            _ledReady = true;
            SetHardware(false);
            _vuTimer.Tick += VuTick;
            _ledTimer.Tick += LedTick;
            _soundTimer.Tick += SoundTick;
            _healthTimer.Tick += HealthTick;
            _scopeTimer.Tick += ScopeTick;
            _identifyTimer.Tick += IdentifyTick;
            BuildScope();

            // Stuck detection lives in the coordinator now; the tiles just mirror the shared Faults set when it changes.
            Faults.Changed += () => Dispatcher.BeginInvoke(new Action(SyncStuck));

            Loaded += (s, e) =>
            {
                if (!_subscribed) { PinActivity.Changed += OnPinsChanged; _subscribed = true; }
                _lamps.StopLampPattern();   // don't inherit a still-running attract sweep from the game screen
                RefreshFromPins();   // reflect current state (idle = all off) — no auto-running pattern
                SyncStuck();         // reflect any faults that are already present
                _lastPollCycles = _coord?.PollCycles ?? 0; _lastPollSample = DateTime.UtcNow;
                _healthTimer.Start();
                _scopeTimer.Start();
                _diagLoaded = true;
                ApplyGremlin();
            };
            Unloaded += (s, e) =>
            {
                if (_subscribed) { PinActivity.Changed -= OnPinsChanged; _subscribed = false; }
                _lamps.StopLampPattern();   // leaving the screen ends any preview pattern
                ClearSolenoidLatches(true); // and releases any latched solenoid
                _ledTimer.Stop();
                _vuTimer.Stop();
                _soundTimer.Stop();
                _healthTimer.Stop();
                _scopeTimer.Stop();
                StopIdentifyBlink();
                _soundActive = false;
                _diagLoaded = false;
                StopRoam();
                // clean up any in-progress grab/throw so he doesn't resume mid-air on return
                _throwTimer.Stop(); _madTimer.Stop();
                if (_dragging) { _dragging = false; Bot.ReleaseMouseCapture(); Bot.SetGrabbed(false); }
            };
        }

        // ---- Roaming mechanic bot ----
        private readonly DispatcherTimer _roamPause = new();
        private readonly TranslateTransform _botMove = new();
        private readonly RotateTransform _botRot = new();
        private readonly Random _botRnd = new();
        private bool _roaming, _diagLoaded, _botPlaced, _pendingReturn;
        private double _botX, _botY;

        private double FloorY() => Math.Max(0, BotLayer.ActualHeight - 96 - 34);

        private bool _botXformReady;
        private void EnsureBotTransform()
        {
            if (_botXformReady) return;
            Bot.RenderTransformOrigin = new Point(0.5, 0.6);
            var grp = new TransformGroup(); grp.Children.Add(_botMove); grp.Children.Add(_botRot);
            Bot.RenderTransform = grp;
            Bot.MouseLeftButtonDown += Bot_Down;   // grab-and-throw
            Bot.MouseMove += Bot_Drag;
            Bot.MouseLeftButtonUp += Bot_Up;
            _throwTimer.Tick += (s, e) => ThrowTick();
            _madTimer.Tick += (s, e) => { _madTimer.Stop(); Bot.SetMad(false); if (AppState.Settings.ShowGremlin && _diagLoaded) StartRoam(); };   // huff over, back to work
            _botXformReady = true;
        }

        // ---- grab, drag, and throw the gremlin (just for fun) ----
        private bool _dragging;
        private double _grabDx, _grabDy, _botVx, _botVy;
        private Point _lastDragPt;
        private readonly DispatcherTimer _throwTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
        private readonly DispatcherTimer _madTimer = new() { Interval = TimeSpan.FromMilliseconds(1600) };

        private void Bot_Down(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!AppState.Settings.ShowGremlin) return;
            _throwTimer.Stop(); _madTimer.Stop();
            StopRoam();
            _dragging = true;
            _botMove.BeginAnimation(TranslateTransform.XProperty, null);
            _botMove.BeginAnimation(TranslateTransform.YProperty, null);
            var p = e.GetPosition(BotLayer);
            _grabDx = p.X - _botX; _grabDy = p.Y - _botY;
            _lastDragPt = p; _botVx = _botVy = 0;
            Bot.SetGrabbed(true);
            Bot.CaptureMouse();
            e.Handled = true;
        }

        private void Bot_Drag(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_dragging) return;
            var p = e.GetPosition(BotLayer);
            double w = BotLayer.ActualWidth, h = BotLayer.ActualHeight;
            _botX = Math.Max(0, Math.Min(w - 58, p.X - _grabDx));
            _botY = Math.Max(-20, Math.Min(h - 40, p.Y - _grabDy));
            _botMove.X = _botX; _botMove.Y = _botY;
            _botVx = 0.55 * _botVx + 0.45 * (p.X - _lastDragPt.X);   // smoothed throw velocity
            _botVy = 0.55 * _botVy + 0.45 * (p.Y - _lastDragPt.Y);
            _lastDragPt = p;
        }

        private void Bot_Up(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            Bot.ReleaseMouseCapture();
            _botPlaced = true;
            _throwTimer.Start();   // let physics carry the toss (or just drop him)
        }

        private void ThrowTick()
        {
            double w = BotLayer.ActualWidth, floor = FloorY();
            _botVy += 1.5;                 // gravity
            _botVx *= 0.995;
            _botX += _botVx; _botY += _botVy;
            if (_botX < 4) { _botX = 4; _botVx = -_botVx * 0.5; }
            if (_botX > w - 58) { _botX = w - 58; _botVx = -_botVx * 0.5; }
            bool onFloor = false;
            if (_botY >= floor)
            {
                _botY = floor;
                if (Math.Abs(_botVy) > 3.5) { _botVy = -_botVy * 0.45; _botVx *= 0.6; }   // bounce
                else { _botVy = 0; onFloor = true; }
            }
            _botMove.X = _botX; _botMove.Y = _botY;
            if (onFloor && Math.Abs(_botVx) < 1.2)
            {
                _throwTimer.Stop();
                _botVx = 0;
                Bot.SetGrabbed(false);
                Bot.SetMad(true);                       // "hey!" — dusts himself off, shakes a fist
                _madTimer.Stop(); _madTimer.Start();
            }
        }

        private bool[] _lastPresent = new bool[4];

        // Show/hide the gremlin per the operator setting and restore the right state.
        private void ApplyGremlin()
        {
            bool on = AppState.Settings.ShowGremlin;
            if (GremlinBtn != null) GremlinBtn.Content = on ? "GREMLIN: ON" : "GREMLIN: OFF";
            if (BotLayer == null) return;
            BotLayer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) { StopRoam(); return; }
            EnsureBotTransform();   // wire up grab/drag/throw whenever he's on screen (even when not roaming)
            bool all = _lastPresent.Length >= 4 && _lastPresent[0] && _lastPresent[1] && _lastPresent[2] && _lastPresent[3];
            _botSad = false;
            if (all) { Bot.SetSad(false); _pendingReturn = true; StopRoam(); StartRoam(); }   // returning to the page: appear somewhere fresh (or at a fault)
            else UpdateBotBoardState(_lastPresent, false);
        }

        private void Gremlin_Click(object sender, RoutedEventArgs e)
        {
            AppState.Settings.ShowGremlin = !AppState.Settings.ShowGremlin;
            AppState.PersistSettings();
            ApplyGremlin();
        }

        private void StartRoam()
        {
            if (_roaming || !AppState.Settings.ShowGremlin || !_diagLoaded) return;
            _roaming = true;
            EnsureBotTransform();
            if (_roamPause.Interval == default) _roamPause.Tick += (s, e) => { _roamPause.Stop(); try { RoamNext(); } catch (Exception ex) { Log.Error("roam tick failed", ex); ScheduleNext(1500); } };   // a stray error must reschedule, never freeze him
            // wait for layout so the width/height are known
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_roaming) return;
                if (!_botPlaced) { _botX = 60; _botY = FloorY(); _botMove.X = _botX; _botMove.Y = _botY; _botPlaced = true; }
                // else keep his current spot — recovering from a board loss shouldn't teleport him back to the start
                ScheduleNext(500);
            }), DispatcherPriority.Loaded);
        }

        private void StopRoam()
        {
            _roaming = false;
            _roamPause.Stop();
            Bot.SetWalking(false);
            _botMove.BeginAnimation(TranslateTransform.XProperty, null);
            _botMove.BeginAnimation(TranslateTransform.YProperty, null);
        }

        private void ScheduleNext(int ms)
        {
            if (!_roaming) return;
            _roamPause.Interval = TimeSpan.FromMilliseconds(ms);
            _roamPause.Stop(); _roamPause.Start();
        }

        // Either scale one of the control panels like a wall, or walk the floor and fix something.
        private void RoamNext()
        {
            if (!_roaming || !_diagLoaded) return;
            double w = BotLayer.ActualWidth, floor = FloorY();
            if (w <= 0 || floor <= 0) { ScheduleNext(600); return; }

            // Just came back to the page: appear at a real fault (point at it) or somewhere fresh — as if he'd been working while you were away.
            if (_pendingReturn) { _pendingReturn = false; ReturnToPage(w, floor); return; }

            // a real problem (stuck switch) always gets his attention first (but not in bench mode, where inputs are ignored)
            if (_stuck.Count > 0 && !AppState.Settings.BenchMode && ProblemSpot() is (double px, double py)) { AlertRoutine(px, py); return; }

            var edges = ClimbEdges();
            int r = _botRnd.Next(100);
            if (edges.Count > 0 && r < 12) { ClimbWall(edges[_botRnd.Next(edges.Count)]); return; }   // now and then, scale a panel
            if (r < 20 && LunchLedge() is (double lx, double ly)) { LunchBreak(lx, ly); return; }       // now and then, a lunch break

            // walk a bit along the floor (where he never covers the panels), then fix / clean something
            double step = _botRnd.NextDouble() * 260 - 130;
            double tx = Math.Max(30, Math.Min(w - 130, _botX + step));
            Action fix = _botRnd.Next(4) == 0 ? (Action)(() => CleanRoutine(NextAfterFix)) : () => { Bot.Work(); NextAfterFix(); };
            WalkTo(tx, floor - _botRnd.Next(0, 24), fix);
        }

        // On returning to the page: prioritize a real fault (stand at it, point, shocked); otherwise show up somewhere new with a task.
        private void ReturnToPage(double w, double floor)
        {
            if (_stuck.Count > 0 && !AppState.Settings.BenchMode && ProblemSpot() is (double px, double py))
            {
                Teleport(Math.Max(30, Math.Min(w - 130, px)), floor - _botRnd.Next(0, 16));
                AlertRoutine(px, py);
                return;
            }
            Teleport(30 + _botRnd.NextDouble() * (w - 130), floor - _botRnd.Next(0, 24));
            Bot.Work();
            NextAfterFix();
        }

        // Snap him to a spot instantly (used between page visits, while the page was hidden).
        private void Teleport(double x, double y)
        {
            _botMove.BeginAnimation(TranslateTransform.XProperty, null);
            _botMove.BeginAnimation(TranslateTransform.YProperty, null);
            _botX = x; _botY = y; _botMove.X = x; _botMove.Y = y; _botPlaced = true;
        }

        // A moderate cadence: he potters around and fixes things fairly often, but not nonstop pacing.
        private void NextAfterFix() => ScheduleNext(3000 + _botRnd.Next(3500));

        // The top edge of the LIVE LAMP MAP panel, used as a ledge he can sit on for lunch.
        private (double x, double y)? LunchLedge()
        {
            if (PanelLamp == null || !PanelLamp.IsVisible || PanelLamp.ActualWidth < 60) return null;
            var tl = PanelLamp.TransformToVisual(BotLayer).Transform(new Point(0, 0));
            double x = tl.X + 26;      // near the left of the panel's top edge
            double y = tl.Y - 66;      // seat on the top border so his legs dangle over it
            return y < 2 ? null : (x, y);
        }

        // Where a currently-stuck switch's tile is (in BotLayer coords), so he can go stand under it and point.
        private (double x, double y)? ProblemSpot()
        {
            foreach (var id in _stuck)
                if (_switchTiles.TryGetValue(id, out var tile) && tile.IsVisible && tile.ActualWidth > 0)
                {
                    var tl = tile.TransformToVisual(BotLayer).Transform(new Point(0, 0));
                    double x = Math.Max(6, Math.Min(BotLayer.ActualWidth - 70, tl.X + tile.ActualWidth / 2 - 32));
                    double y = Math.Min(FloorY(), Math.Max(6, tl.Y + tile.ActualHeight + 4));   // stand just below the flagged tile
                    return (x, y);
                }
            return null;
        }

        // Rush over to the problem, point and wave with a shocked face, and jump to get attention — "fix this, dummy!"
        private void AlertRoutine(double x, double y)
        {
            Bot.FaceRight(true);
            WalkTo(x, y, () =>
            {
                if (!_roaming) return;
                Bot.SetAlert(true);
                double baseY = _botY;
                _botMove.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(baseY, baseY - 22, TimeSpan.FromMilliseconds(240))
                    { AutoReverse = true, RepeatBehavior = new RepeatBehavior(6), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3800) };
                t.Tick += (s, e) =>
                {
                    t.Stop();
                    if (!_roaming) return;
                    Bot.SetAlert(false);
                    _botMove.BeginAnimation(TranslateTransform.YProperty, null); _botMove.Y = baseY;
                    NextAfterFix();
                };
                t.Start();
            });
        }

        // Amble to the ledge, sit and eat for a few seconds, then get back to work.
        private void LunchBreak(double x, double y)
        {
            Bot.FaceRight(x >= _botX);
            Bot.SetWalking(true);
            double dur = Math.Max(320, Dist(_botX, _botY, x, y) / 120.0 * 1000.0);
            AnimXY(x, y, dur, new CubicEase { EasingMode = EasingMode.EaseInOut }, () =>
            {
                if (!_roaming) return;
                Bot.SetLunch(true);
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3800 + _botRnd.Next(2600)) };
                t.Tick += (s, e) => { t.Stop(); Bot.SetLunch(false); if (_roaming) NextAfterFix(); };
                t.Start();
            });
        }

        // The left and right outer edges of each control panel, in BotLayer coordinates — the walls he can scale.
        private List<(double x, double topY, double baseY, bool faceRight)> ClimbEdges()
        {
            var list = new List<(double, double, double, bool)>();
            foreach (var p in new FrameworkElement?[] { PanelSwitch, PanelLamp, PanelOutput })
            {
                if (p == null || p.ActualWidth < 20 || p.ActualHeight < 40 || !p.IsVisible) continue;
                var xf = p.TransformToVisual(BotLayer);
                var tl = xf.Transform(new Point(0, 0));
                var br = xf.Transform(new Point(p.ActualWidth, p.ActualHeight));
                double topY = Math.Max(6, tl.Y - 4);
                double baseY = Math.Min(FloorY(), br.Y - 40);
                if (baseY - topY < 60) continue;
                list.Add((tl.X - 30, topY, baseY, true));    // left edge: cling on the outside, facing into the panel
                list.Add((br.X - 34, topY, baseY, false));   // right edge
            }
            return list;
        }

        // Walk to the foot of a panel edge, climb straight up it, fix something up top, then come back down.
        private void ClimbWall((double x, double topY, double baseY, bool faceRight) edge)
        {
            double climbX = Math.Max(6, Math.Min(BotLayer.ActualWidth - 70, edge.x));
            double top = edge.topY + 6 + _botRnd.Next(0, 46);
            WalkTo(climbX, edge.baseY, () =>
            {
                if (!_roaming) return;
                Bot.FaceRight(edge.faceRight);
                ClimbTo(climbX, top, () =>
                {
                    if (!_roaming) return;
                    Bot.Work();
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300 + _botRnd.Next(700)) };
                    t.Tick += (s, e) =>
                    {
                        t.Stop();
                        if (!_roaming) return;
                        if (_botRnd.Next(10) < 6) ClimbTo(climbX, edge.baseY, NextAfterFix);   // climb back down the wall
                        else Descend(30 + _botRnd.NextDouble() * (BotLayer.ActualWidth - 130), FloorY(), NextAfterFix);
                    };
                    t.Start();
                });
            });
        }

        // Wash the screen: the gremlin scrubs while soap bubbles rise and pop, then water rinses down.
        private void CleanRoutine(Action onDone)
        {
            Bot.Clean();
            double bx = _botX + 34, by = _botY + 24;
            for (int i = 0; i < 9; i++) SpawnBubble(bx + _botRnd.Next(-16, 30), by + _botRnd.Next(-6, 18), _botRnd.Next(0, 500));
            var rinse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1150) };
            rinse.Tick += (s, e) => { rinse.Stop(); if (!_roaming) { onDone(); return; } for (int i = 0; i < 7; i++) SpawnDrop(bx + _botRnd.Next(-14, 30), by, _botRnd.Next(0, 300)); };
            rinse.Start();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2200) };
            t.Tick += (s, e) => { t.Stop(); onDone(); };
            t.Start();
        }

        private void SpawnBubble(double x, double y, int delayMs)
        {
            var b = new Ellipse { Width = 7 + _botRnd.Next(6), Height = 7 + _botRnd.Next(6), Fill = new SolidColorBrush(Color.FromArgb(0x80, 0xE8, 0xF4, 0xFF)), Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1, Opacity = 0 };
            Canvas.SetLeft(b, x); Canvas.SetTop(b, y); BotLayer.Children.Add(b);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs + 1) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                b.BeginAnimation(OpacityProperty, new DoubleAnimation(0.9, 0, TimeSpan.FromMilliseconds(900)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
                var rise = new DoubleAnimation(y, y - 34 - _botRnd.Next(20), TimeSpan.FromMilliseconds(900));
                rise.Completed += (s2, e2) => BotLayer.Children.Remove(b);
                b.BeginAnimation(Canvas.TopProperty, rise);
            };
            t.Start();
        }

        private void SpawnDrop(double x, double y, int delayMs)
        {
            var d = new Ellipse { Width = 4, Height = 7, Fill = new SolidColorBrush(Color.FromArgb(0xC0, 0x5A, 0xC8, 0xFF)) };
            Canvas.SetLeft(d, x); Canvas.SetTop(d, y); BotLayer.Children.Add(d);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs + 1) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                var fall = new DoubleAnimation(y, y + 42, TimeSpan.FromMilliseconds(600)) { EasingFunction = new PowerEase { Power = 2, EasingMode = EasingMode.EaseIn } };
                fall.Completed += (s2, e2) => BotLayer.Children.Remove(d);
                d.BeginAnimation(Canvas.TopProperty, fall);
                d.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(600)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            };
            t.Start();
        }

        // Animate both axes together; commit the position and fire done when they finish.
        private void AnimXY(double x, double y, double durMs, IEasingFunction ease, Action done)
        {
            var ax = new DoubleAnimation(_botX, x, TimeSpan.FromMilliseconds(durMs)) { EasingFunction = ease };
            var ay = new DoubleAnimation(_botY, y, TimeSpan.FromMilliseconds(durMs)) { EasingFunction = ease };
            ax.Completed += (s, e) =>
            {
                _botX = x; _botY = y;
                _botMove.BeginAnimation(TranslateTransform.XProperty, null); _botMove.BeginAnimation(TranslateTransform.YProperty, null);
                _botMove.X = x; _botMove.Y = y;
                done?.Invoke();
            };
            _botMove.BeginAnimation(TranslateTransform.XProperty, ax);
            _botMove.BeginAnimation(TranslateTransform.YProperty, ay);
        }

        private static double Dist(double x0, double y0, double x1, double y1) => Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));

        private void WalkTo(double x, double y, Action onArrive)
        {
            Bot.FaceRight(x >= _botX);
            Bot.SetWalking(true);
            double dur = Math.Max(220, Dist(_botX, _botY, x, y) / 135.0 * 1000.0);
            AnimXY(x, y, dur, new CubicEase { EasingMode = EasingMode.EaseInOut }, () => { Bot.SetWalking(false); onArrive?.Invoke(); });
        }

        private void ClimbTo(double x, double y, Action onArrive)
        {
            Bot.SetClimbing(true);   // caller already set which way he faces the wall
            double dur = Math.Max(600, Dist(_botX, _botY, x, y) / 65.0 * 1000.0);   // slower — he's climbing
            AnimXY(x, y, dur, new CubicEase { EasingMode = EasingMode.EaseInOut }, () => { Bot.SetClimbing(false); onArrive?.Invoke(); });
        }

        // Mostly a plain hop down; now and then he tumbles or drifts down under a parachute.
        private void Descend(double x, double y, Action onArrive)
        {
            int r = _botRnd.Next(100);
            if (r < 58) JumpDown(x, y, onArrive);
            else if (r < 84) FallDown(x, y, onArrive);
            else Parachute(x, y, onArrive);
        }

        // Tumble down, sometimes landing knocked out (stars + birds), then carry on.
        private void FallDown(double x, double y, Action onArrive)
        {
            Bot.Surprised();
            double dur = Math.Max(360, (y - _botY) * 4);
            _botRot.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, _botRnd.Next(2) == 0 ? -340 : 340, TimeSpan.FromMilliseconds(dur)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            AnimXY(x, y, dur, new PowerEase { Power = 2.3, EasingMode = EasingMode.EaseIn }, () =>
            {
                _botRot.BeginAnimation(RotateTransform.AngleProperty, null); _botRot.Angle = 0;
                _botRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 6, TimeSpan.FromMilliseconds(90)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2) });
                if (_botRnd.Next(2) == 0)   // knocked out
                {
                    Bot.SetDizzy(true);
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1700) };
                    t.Tick += (s, e) => { t.Stop(); if (!_roaming) return; Bot.SetDizzy(false); onArrive?.Invoke(); };
                    t.Start();
                }
                else onArrive?.Invoke();
            });
        }

        // A little hop-down arc.
        private void JumpDown(double x, double y, Action onArrive)
        {
            Bot.FaceRight(x >= _botX);
            const double dur = 720;
            var ax = new DoubleAnimation(_botX, x, TimeSpan.FromMilliseconds(dur));
            var ay = new DoubleAnimationUsingKeyFrames();
            ay.KeyFrames.Add(new EasingDoubleKeyFrame(Math.Min(_botY, y) - 26, KeyTime.FromPercent(0.32), new CubicEase { EasingMode = EasingMode.EaseOut }));
            ay.KeyFrames.Add(new EasingDoubleKeyFrame(y, KeyTime.FromPercent(1.0), new BounceEase { Bounces = 1, Bounciness = 2, EasingMode = EasingMode.EaseOut }));
            ax.Completed += (s, e) =>
            {
                _botX = x; _botY = y;
                _botMove.BeginAnimation(TranslateTransform.XProperty, null); _botMove.BeginAnimation(TranslateTransform.YProperty, null);
                _botMove.X = x; _botMove.Y = y; onArrive?.Invoke();
            };
            _botMove.BeginAnimation(TranslateTransform.XProperty, ax);
            _botMove.BeginAnimation(TranslateTransform.YProperty, ay);
        }

        // Float down gently under a parachute.
        private void Parachute(double x, double y, Action onArrive)
        {
            Bot.SetParachute(true);
            double dur = Math.Max(1300, (y - _botY) * 9);
            AnimXY(x, y, dur, new SineEase { EasingMode = EasingMode.EaseInOut }, () => { Bot.SetParachute(false); onArrive?.Invoke(); });
        }

        // Called by the coordinator once boards are (or aren't) found.
        public void AttachHardware(HardwareCoordinator coord, bool ready)
        {
            _lamps.StopLampPattern();
            _coord = coord;
            _lamps = coord.Lamps;
            SetHardware(ready);
            RefreshFromPins();
        }

        // ---------------- hardware presence ----------------
        private void SetHardware(bool present)
        {
            _hasHardware = present;
            WarnBanner.Visibility = present ? Visibility.Collapsed : Visibility.Visible;
            // Controls stay usable without boards — they preview on-screen and drive real outputs once connected.
            DevText.Text = present
                ? "FT232H: G1 ● · G2 ● · G3 ● · G4 ●   (connected)"
                : "FT232H: G1 — · G2 — · G3 — · G4 —   (not connected)";
            PollText.Text = present ? "20 Hz poll" : "preview mode";
        }

        public void SetBoardPresence(bool[] present, bool postOk)
        {
            for (int i = 0; i < 4 && i < present.Length; i++)
                if (_pillDot.TryGetValue("G" + (i + 1), out var dot))
                    dot.Fill = Brush(present[i] ? "GreenBrush" : "RedBrush");
            if (_pillDot.TryGetValue("POST", out var pd)) pd.Fill = Brush(postOk ? "GreenBrush" : "RedBrush");
            if (_postText != null) _postText.Text = postOk ? "POST OK" : "POST FAIL";

            _lastPresent = present;
            bool allPresent = present.Length >= 4 && present[0] && present[1] && present[2] && present[3];
            UpdateBotBoardState(present, allPresent);
        }

        private bool _botSad, _celebrating, _allMissing;
        private string _sadText = "";
        private DispatcherTimer? _unsadDebounce, _celebrateEnd;

        // Some boards missing → he stops working and holds a sad sign. ALL boards gone → he's knocked out cold
        // (stars + birds) in front of a tombstone that carries the bad news. When all four come stably back he
        // cheers — jumps and waves a HARDWARE READY sign — then gets back to work.
        private void UpdateBotBoardState(bool[] present, bool allPresent)
        {
            if (!allPresent)
            {
                _unsadDebounce?.Stop();
                if (_celebrating) StopCelebrate();   // a board dropped again mid-cheer
                _sadText = MainWindow.MissingBoardsText(present).ToUpperInvariant();
                bool allMissing = true;
                if (present != null) foreach (var p in present) if (p) { allMissing = false; break; }
                if (!_botSad || allMissing != _allMissing) { _botSad = true; _allMissing = allMissing; EnterSad(allMissing); }
                else if (allMissing) Bot.SetGraveyard(true, _sadText);   // keep the tombstone text current
                else Bot.SetSad(true, _sadText, false);                  // keep the sign text current
            }
            else if (_botSad && !_celebrating)
            {
                // boards back — wait a moment so a flicker doesn't false-trigger, then celebrate
                _unsadDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _unsadDebounce.Tick -= UnsadTick; _unsadDebounce.Tick += UnsadTick;
                _unsadDebounce.Stop(); _unsadDebounce.Start();
            }
            else if (!_botSad && !_celebrating && !_roaming) StartRoam();
        }

        private void UnsadTick(object? s, EventArgs e)
        {
            _unsadDebounce?.Stop();
            _botRot.BeginAnimation(RotateTransform.AngleProperty, null); _botRot.Angle = 0;
            _celebrating = true;
            EnsureBotTransform();
            double cx = _botMove.X, cy = _botMove.Y;   // celebrate right where he was waiting
            _botMove.BeginAnimation(TranslateTransform.XProperty, null); _botMove.X = cx;
            Bot.Celebrate("HARDWARE READY");
            _botMove.BeginAnimation(TranslateTransform.YProperty, null); _botMove.Y = cy;
            _botMove.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(cy, cy - 26, TimeSpan.FromMilliseconds(230))
                { AutoReverse = true, RepeatBehavior = new RepeatBehavior(6), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            _botX = cx; _botY = cy;
            _celebrateEnd ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _celebrateEnd.Tick -= CelebrateEndTick; _celebrateEnd.Tick += CelebrateEndTick;
            _celebrateEnd.Stop(); _celebrateEnd.Start();
        }

        private void CelebrateEndTick(object? s, EventArgs e)
        {
            _celebrateEnd?.Stop();
            double cy = _botMove.Y;
            _botMove.BeginAnimation(TranslateTransform.YProperty, null); _botMove.Y = cy;
            _botX = _botMove.X; _botY = cy;
            Bot.EndCelebrate();
            _celebrating = false; _botSad = false;
            StartRoam();   // back to fixing things, from where he stands
        }

        private void StopCelebrate()
        {
            _celebrateEnd?.Stop();
            _celebrating = false;
            _botMove.BeginAnimation(TranslateTransform.YProperty, null);
            Bot.EndCelebrate();
        }

        private void EnterSad(bool allMissing)
        {
            double cx = _botMove.X, cy = _botMove.Y;   // freeze wherever he is right now (before StopRoam clears the animation)
            StopRoam();
            EnsureBotTransform();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _botMove.BeginAnimation(TranslateTransform.XProperty, null);
                _botMove.BeginAnimation(TranslateTransform.YProperty, null);
                _botMove.X = cx; _botMove.Y = cy; _botX = cx; _botY = cy;   // sad / knocked out on the spot, not back at the start
                _botRot.BeginAnimation(RotateTransform.AngleProperty, null);
                _botRot.Angle = allMissing ? 0 : 8;   // upright & dazed when out cold; a slump when just sad
                if (allMissing) Bot.SetGraveyard(true, _sadText);
                else Bot.SetSad(true, _sadText, false);
            }), DispatcherPriority.Loaded);
        }

        // ---------------- pills ----------------
        private void BuildPills()
        {
            AddPill("G1"); AddPill("G2"); AddPill("G3"); AddPill("G4"); AddPill("POST");
        }

        private void AddPill(string key)
        {
            bool wide = key == "POST";
            var dot = new Ellipse { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center, Fill = Brush("MutedBrush") };
            var txt = new TextBlock { Text = wide ? "POST —" : key, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontFamily = Font("DisplayFont"), FontSize = 13, Foreground = Brush("TextBrush") };
            if (wide) _postText = txt;
            _pillDot[key] = dot;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(dot); sp.Children.Add(txt);
            PillHost.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(13, 6, 15, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Child = sp,
                ToolTip = wide
                    ? "POST = Power-On Self-Test. Green once all four boards respond and the lamps/LEDs flash to prove the machine woke up. Run it with the RUN SELF-TEST button."
                    : $"FT232H board {key}: green = detected, red = missing / not labeled \"{key}\".",
            });
        }

        // ---------------- switch monitor ----------------
        private void BuildSwitches()
        {
            foreach (var sw in SwitchMap.All)
            {
                string accent = sw.IsWinner ? "AmberBrush"
                    : sw.Kind == SwitchKind.Coin ? "GoldBrush"
                    : sw.Kind == SwitchKind.Gobble ? "PurpleBrush"
                    : sw.Kind == SwitchKind.Tilt ? "RedBrush"
                    : "TealBrush";

                // Readable label — the seven-segment "S0" reads like "50", so use the UI font here.
                string num = sw.Id.Substring(1);
                var id = new TextBlock { Text = $"Switch {num}", FontFamily = Font("UiFont"), FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = Brush("TextBrush") };
                string subText = sw.Kind == SwitchKind.Score ? $"(S{num}) · {sw.Points} pts" : $"(S{num}) · {sw.Kind}";
                var sub = new TextBlock { Text = subText, FontFamily = Font("UiFont"), FontSize = 10.5, Foreground = Brush("MutedBrush") };
                var hits = new TextBlock { Text = "—", FontFamily = Font("UiFont"), FontSize = 10, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 1, 0, 0) };
                _switchHitText[sw.Id] = hits;
                var stack = new StackPanel { Margin = new Thickness(10, 6, 8, 6) };
                stack.Children.Add(id); stack.Children.Add(sub); stack.Children.Add(hits);

                var accentBar = new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = Brush(accent), Margin = new Thickness(0, 4, 0, 4) };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(stack, 1);
                grid.Children.Add(accentBar); grid.Children.Add(stack);

                var tile = new Border
                {
                    Width = 116, Height = 62, Margin = new Thickness(4),
                    CornerRadius = new CornerRadius(9),
                    Background = new SolidColorBrush(Color.FromArgb(0x14, 0x20, 0x26, 0x34)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    Child = grid,
                    Tag = accent,
                };
                _switchTiles[sw.Id] = tile;
                SwitchHost.Children.Add(tile);
            }
        }

        public void SetSwitch(string id, bool active)
        {
            if (!_switchTiles.TryGetValue(id, out var tile)) return;
            // A stuck switch stays red no matter what the pin reads — otherwise the next pin refresh
            // would repaint a still-high stuck input back to its normal active colour.
            if (_stuck.Contains(id))
            {
                var rc = ColorOf("RedBrush");
                tile.Background = new SolidColorBrush(rc) { Opacity = 0.30 };
                tile.BorderBrush = Brush("RedBrush");
                tile.Effect = new DropShadowEffect { Color = rc, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.85 };
                return;
            }
            string accent = tile.Tag as string ?? "TealBrush";
            if (active)
            {
                tile.Background = new SolidColorBrush(ColorOf(accent)) { Opacity = 0.30 };
                tile.BorderBrush = Brush(accent);
                tile.Effect = new DropShadowEffect { Color = ColorOf(accent), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.7 };
            }
            else
            {
                tile.Background = new SolidColorBrush(Color.FromArgb(0x14, 0x20, 0x26, 0x34));
                tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
                tile.Effect = null;
            }
        }

        // ---------------- lamp map ----------------
        private void BuildLamps()
        {
            foreach (int v in HundredsVals) LampHundredsHost.Children.Add(MakeLamp(v, 98, 72, 28));
            foreach (int v in TensVals) LampTensHost.Children.Add(MakeLamp(v, 72, 72, 23));
            foreach (int v in Order) _lampLoc[v] = LampController.Outputs[v.ToString()];
        }

        private Border MakeLamp(int value, double w, double h, double fs)
        {
            var t = new TextBlock
            {
                Text = value.ToString(), FontFamily = Font("DisplayFont"), FontSize = fs, Foreground = Brush("MutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var b = new Border
            {
                Width = w, Height = h, Margin = new Thickness(5), CornerRadius = new CornerRadius(h / 2),
                Background = new SolidColorBrush(Color.FromArgb(0x14, 0x20, 0x26, 0x34)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1), Child = t,
                Cursor = Cursors.Hand, Tag = value, ToolTip = $"Click to toggle lamp {value}",
            };
            b.MouseLeftButtonUp += Lamp_Click;
            _lampBorder[value] = b; _lampText[value] = t;
            return b;
        }

        // Mirror pin state onto the map, coalescing PinActivity.Changed bursts into one refresh per frame.
        private void OnPinsChanged()
        {
            if (_refreshQueued) return;
            _refreshQueued = true;
            Dispatcher.BeginInvoke(new Action(() => { _refreshQueued = false; RefreshFromPins(); }));
        }

        private void RefreshFromPins()
        {
            if (_lampLoc.Count == 0) return;   // not built yet (a slider ValueChanged can fire during XAML parse)
            int lit = 0, tens = 0, hund = 0;
            foreach (int v in Order)
            {
                var (b, p) = _lampLoc[v];
                bool on = PinActivity.IsActive(b, p);
                if (on) { lit++; if (v < 100) tens = Math.Max(tens, v); else hund = Math.Max(hund, v); }
                if (!_lampState.TryGetValue(v, out var was) || was != on) { _lampState[v] = on; ApplyLamp(v, on); }  // only touch changed tiles
            }
            if (LampValueText != null) LampValueText.Text = (tens + hund).ToString();
            foreach (var sw in SwitchMap.All)
            {
                bool on = PinActivity.IsActive(sw.Board, sw.Pin);
                bool known = _switchState.TryGetValue(sw.Id, out var was);
                if (!known || was != on)
                {
                    _switchState[sw.Id] = on;
                    SetSwitch(sw.Id, on);
                    if (on && (!known || !was))   // rising edge = a hit
                    {
                        _switchHits[sw.Id] = (_switchHits.TryGetValue(sw.Id, out var n) ? n : 0) + 1;
                        _switchLastFiredTick[sw.Id] = Environment.TickCount64;
                        AddEvent(sw.Id + "  ▸  " + (sw.Kind == SwitchKind.Score ? sw.Points + " PTS" : sw.Kind.ToString().ToUpperInvariant()));
                        UpdateSwitchHitText(sw.Id);
                        _scopeEnergy = Math.Min(1.0, _scopeEnergy + 0.75);   // a hit spikes the scope
                    }
                    else if (!on)                 // falling edge
                    {
                        UpdateSwitchHitText(sw.Id);
                    }
                }
            }
            // Special lamps on the map (Winner / Game Over / Tilt)
            foreach (var (name, _, accent) in SpecialLamps)
                if (_specialLampBorder.ContainsKey(name) && LampController.Outputs.TryGetValue(name, out var sloc))
                {
                    bool on = PinActivity.IsActive(sloc.board, sloc.pin);
                    bool known = _specialState.TryGetValue(name, out var was);
                    if (!known || was != on)
                    {
                        _specialState[name] = on; ApplySpecialLamp(name, accent, on);
                        if (known) AddEvent(name.ToUpperInvariant() + " LAMP " + (on ? "ON" : "OFF"));   // only real transitions
                    }
                }
            // Solenoid indicator LEDs
            foreach (var (name, _) in Solenoids)
                if (_solDots.TryGetValue(name, out var dot) && LampController.Outputs.TryGetValue(name, out var qloc))
                {
                    bool on = PinActivity.IsActive(qloc.board, qloc.pin);
                    dot.Fill = on ? Brush("RedBrush") : Brush("MutedBrush");
                    dot.Effect = on ? new DropShadowEffect { Color = ColorOf("RedBrush"), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.85 } : null;
                    bool known = _solLogState.TryGetValue(name, out var was);
                    if (!known || was != on) { _solLogState[name] = on; if (known) AddEvent(name.ToUpperInvariant() + " " + (on ? "FIRED" : "OFF")); }
                }

            SetRing(lit / (double)Order.Length);
            RingText.Text = lit.ToString();
        }

        private void ApplySpecialLamp(string name, string accent, bool on)
        {
            var b = _specialLampBorder[name]; var t = _specialLampText[name];
            if (on)
            {
                var c = ColorOf(accent);
                b.Background = new SolidColorBrush(c) { Opacity = 0.30 };
                b.BorderBrush = Brush(accent);
                b.Effect = new DropShadowEffect { Color = c, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.8 };
                t.Foreground = Brush("TextBrush");
            }
            else
            {
                b.Background = new SolidColorBrush(Color.FromArgb(0x14, 0x20, 0x26, 0x34));
                b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
                b.Effect = null;
                t.Foreground = Brush("MutedBrush");
            }
        }

        private void ApplyLamp(int value, bool on)
        {
            var b = _lampBorder[value]; var t = _lampText[value];
            string accent = value >= 100 ? "GoldBrush" : "TealBrush";
            if (on)
            {
                var c = ColorOf(accent);
                b.Background = new SolidColorBrush(c) { Opacity = 0.30 * _brightness + 0.10 };
                b.BorderBrush = Brush(accent);
                b.Effect = new DropShadowEffect { Color = c, BlurRadius = 18 * _brightness, ShadowDepth = 0, Opacity = 0.85 * _brightness };
                t.Foreground = Brush("TextBrush");
            }
            else
            {
                b.Background = new SolidColorBrush(Color.FromArgb(0x14, 0x20, 0x26, 0x34));
                b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
                b.Effect = null;
                t.Foreground = Brush("MutedBrush");
            }
        }

        // ---------------- controls ----------------
        // Patterns and ALL OFF both act on the same LampController instance so STOP is guaranteed.
        private void Pattern_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            if (_activePatternBtn == btn) { AllOff_Click(sender, e); return; }   // click the lit button again = stop
            SetActivePattern(btn);
            Sfx("ledpatternon");
            string tag = (string)btn.Tag;
            int pick = _vuRnd.Next(5);   // pick on the UI thread (Random isn't thread-safe)
            Bg(() =>
            {
                switch (tag)
                {
                    case "chase": _lamps.ChasePattern(); break;
                    case "inout": _lamps.InsideOutPattern(); break;
                    case "sweep": _lamps.SweepBounce(); break;
                    case "cascade": _lamps.Cascade(); break;
                    case "allon": _lamps.SetLamp("All", true); break;
                    case "random":
                        switch (pick)
                        {
                            case 0: _lamps.ChasePattern(); break;
                            case 1: _lamps.InsideOutPattern(); break;
                            case 2: _lamps.SweepBounce(); break;
                            case 3: _lamps.Cascade(); break;
                            default: _lamps.FlashAll(); break;
                        }
                        break;
                }
            });
        }

        private void AllOff_Click(object sender, RoutedEventArgs e)
        {
            var coord = _coord; var lamps = _lamps;
            Bg(() =>
            {
                lamps.StopLampPattern();   // cancel the running animation
                lamps.ClearAll();          // force every lamp/solenoid pin off right now
                coord?.AllOff();           // also kills the LED strip + bulb test
            });
            SetActivePattern(null);
            ResetButtonLook(BulbTestBtn);
            ClearSolenoidLatches(false);   // ClearAll already dropped the pins; just reset the buttons/timers
            StopLedPreview();
            if (_activeColorBtn != null) { ResetButtonLook(_activeColorBtn); _activeColorBtn = null; }   // drop the lit solid-color button
            StopIdentifyBlink();           // stop any lamp we were blinking to identify
            if (_identifyMode) { _identifyMode = false; ResetButtonLook(IdentifyBtn); }   // and leave identify mode
            Sfx("ledpatternoff");
        }

        // Highlight the running pattern's button (and un-highlight the previous one).
        private void SetActivePattern(Button? btn)
        {
            if (_activePatternBtn != null) ResetButtonLook(_activePatternBtn);
            _activePatternBtn = btn;
            if (btn != null) SetButtonActiveLook(btn, "TealBrush");
        }

        private void SetButtonActiveLook(Button b, string accent)
        {
            var c = ColorOf(accent);
            b.Background = new SolidColorBrush(Color.FromArgb(0x30, c.R, c.G, c.B));
            b.BorderBrush = Brush(accent);
            b.Foreground = Brush(accent);
            b.Effect = new DropShadowEffect { Color = c, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.55 };
        }

        private static void ResetButtonLook(Button b)
        {
            b.ClearValue(BackgroundProperty);
            b.ClearValue(BorderBrushProperty);
            b.ClearValue(ForegroundProperty);
            b.ClearValue(EffectProperty);
        }

        // Click a lamp on the map to toggle it (real lamp when boards are present; preview otherwise).
        private void Lamp_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not Border b || b.Tag is not int v) return;
            string key = v.ToString();
            if (_identifyMode) { BlinkLamp(key); Sfx("ledbutton"); return; }   // blink to find it, don't latch
            var (bd, pin) = _lampLoc[v];
            bool on = !PinActivity.IsActive(bd, pin);
            var lamps = _lamps;
            Bg(() => lamps.SetLamp(key, on));
            Sfx(on ? "ledbutton" : "ledbuttonoff");
        }

        private string _reportText = "";

        private async void SelfTest_Click(object sender, RoutedEventArgs e)
        {
            SelfTestBtn.IsEnabled = false;
            bool[] present = _coord?.GetBoardPresence() ?? _lastPresent ?? new bool[4];
            bool ok = false;
            try
            {
                if (_coord != null)
                {
                    var res = await Task.Run(async () => await _coord.SelfTestAsync());   // exercises lamps/LEDs off the UI thread
                    ok = res.ok; present = res.present;
                    SetBoardPresence(present, ok);
                }
            }
            catch (Exception ex) { Log.Error("self-test failed", ex); }
            BuildReport(present, ok);
            SelfTestOverlay.Visibility = Visibility.Visible;
            SelfTestOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            SelfTestBtn.IsEnabled = true;
        }

        // Look over the whole machine and write a findings + suggestions report.
        private void BuildReport(bool[] present, bool ok)
        {
            ReportHost.Children.Clear();
            var sb = new System.Text.StringBuilder();
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            SelfTestStamp.Text = stamp;
            sb.AppendLine("SkillGame — Self-Test Report").AppendLine(stamp).AppendLine(new string('-', 44));

            var missing = new List<string>();

            Section("HARDWARE", sb);
            int n = 0; for (int i = 0; i < present.Length; i++) if (present[i]) n++;
            Row(n >= 4 ? "ok" : "fail", $"FT232H boards detected: {n} / 4", sb);
            for (int i = 0; i < 4; i++)
            {
                bool p = i < present.Length && present[i];
                Row(p ? "ok" : "fail", $"GPIO{i + 1}: {(p ? "present" : "MISSING")}", sb);
                if (!p) missing.Add("GPIO" + (i + 1));
            }
            Row(ok ? "ok" : "warn", $"Power-on self-test: {(ok ? "PASS" : "FAIL")}", sb);
            if (_coord != null)
                for (int b = 1; b <= 3; b++)
                {
                    long ms = _coord.LastSeenAgoMs(b);
                    if (ms < 0) Row(present[b - 1] ? "warn" : "info", $"GPIO{b} switch poll: no reads yet", sb);
                    else Row(ms < 1500 ? "ok" : "warn", $"GPIO{b} switch poll: last read {ms} ms ago", sb);
                }

            Section("INPUTS", sb);
            bool bench = AppState.Settings.BenchMode;
            var stuck = _stuck.ToList();
            if (bench)
            {
                Row("warn", "Bench mode is ON — switch inputs are ignored, so these were NOT tested:", sb);
                Row("info", "     · live switch reads (Switch Monitor)", sb);
                Row("info", "     · stuck-switch detection", sb);
                Row("info", "     · coin-up / scoring input to the game", sb);
            }
            else
            {
                Row("info", "Bench mode: off", sb);
                Row(stuck.Count == 0 ? "ok" : "warn", stuck.Count == 0 ? "Stuck switches: none" : $"Stuck switches: {stuck.Count} ({string.Join(", ", stuck)})", sb);
            }

            Section("OUTPUTS", sb);
            Row("info", "Lamps + LED strip exercised (all-on flash)", sb);

            Section("AUDITS", sb);
            var d = AppState.Audits.Data;
            Row("info", $"Games played (lifetime): {d.GamesPlayed:N0}", sb);
            long on = d.PowerOnSeconds;
            Row("info", $"Power-on time: {on / 3600}h {(on % 3600) / 60}m", sb);

            Section("SETTINGS", sb);
            var st = AppState.Settings;
            Row("info", $"Tilt: {(st.TiltEnabled ? "enabled" : "disabled")} · tilts allowed {st.TiltsAllowed}", sb);
            Row("info", $"Quiet hours: {(st.QuietHoursEnabled ? $"{st.QuietStartHour}:00–{st.QuietEndHour}:00 @ {st.QuietVolumePercent}%" : "off")}", sb);

            Section("SUGGESTIONS", sb);
            bool clean = true;
            if (missing.Count > 0) { Row("fail", $"Board(s) not detected: {string.Join(", ", missing)}. Check each board's USB cable and that its FTDI serial (GPIO1–GPIO4) is programmed correctly.", sb); clean = false; }
            if (stuck.Count > 0 && !bench) { Row("warn", "Switches are reading stuck. If nothing is wired to the boards yet, that's floating inputs — wire the switches (with pull-downs to ground) or turn Bench Mode ON. If they are wired, inspect those switches.", sb); clean = false; }
            if (bench)
            {
                if (missing.Count == 0 && ok) Row("ok", "Hardware checks passed (boards, POST, outputs).", sb);
                Row("warn", "Bench Mode is ON, so the switch-input tests were skipped. Turn Bench Mode off (with the switches wired) to test the inputs and play.", sb);
                clean = false;
            }
            if (!ok && missing.Count == 0) { Row("warn", "POST reported a fault even though all boards appear present — try re-running the self-test.", sb); clean = false; }
            if (clean) Row("ok", "All systems nominal — ready to play.", sb);

            _reportText = sb.ToString();
        }

        private void Section(string title, System.Text.StringBuilder sb)
        {
            sb.AppendLine().AppendLine("[" + title + "]");
            ReportHost.Children.Add(new TextBlock { Text = title, FontFamily = Font("DisplayFont"), FontWeight = FontWeights.Bold, FontSize = 13, Foreground = Brush("CyanBrush"), Margin = new Thickness(0, 14, 0, 6) });
        }

        private void Row(string lvl, string text, System.Text.StringBuilder sb)
        {
            string icon = lvl switch { "ok" => "✓", "warn" => "⚠", "fail" => "✕", _ => "•" };
            string brush = lvl switch { "ok" => "GreenBrush", "warn" => "AmberBrush", "fail" => "RedBrush", _ => "MutedBrush" };
            sb.AppendLine($"  {icon} {text}");
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            sp.Children.Add(new TextBlock { Text = icon, Foreground = Brush(brush), FontSize = 14, Width = 22, VerticalAlignment = VerticalAlignment.Top });
            sp.Children.Add(new TextBlock { Text = text, Foreground = Brush("TextBrush"), FontSize = 13.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 560, LineHeight = 19, LineStackingStrategy = LineStackingStrategy.BlockLineHeight });
            ReportHost.Children.Add(sp);
        }

        private void SelfTestSave(object sender, RoutedEventArgs e)
        {
            try
            {
                System.IO.Directory.CreateDirectory(@"C:\SkillGame");
                string path = System.IO.Path.Combine(@"C:\SkillGame", $"selftest_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                System.IO.File.WriteAllText(path, _reportText);
                AppDialog.Info("Self-Test", "Report saved to:\n" + path);
            }
            catch (Exception ex) { Log.Error("save self-test failed", ex); AppDialog.Info("Self-Test", "Couldn't save the report:\n" + ex.Message); }
        }

        private void SelfTestClose(object sender, RoutedEventArgs e) => SelfTestOverlay.Visibility = Visibility.Collapsed;

        private async void Hw_Click(object sender, RoutedEventArgs e)
        {
            if ((string)((Button)sender).Tag != "bulb" || _coord == null) return;
            if (_coord.BulbRunning) { _coord.StopBulbTest(); ResetButtonLook(BulbTestBtn); return; }
            SetButtonActiveLook(BulbTestBtn, "TealBrush");
            try { await Task.Run(async () => await _coord.BulbTestAsync()); }   // the whole walk runs off the UI thread
            catch (Exception ex) { Log.Error("bulb test failed", ex); }
            ResetButtonLook(BulbTestBtn);
        }

        private void SoundPlay_Click(object sender, RoutedEventArgs e) => ToggleSound();

        private void ViewLog_Click(object sender, RoutedEventArgs e)
        {
            new LogWindow { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        private void WiringTest_Click(object sender, RoutedEventArgs e)
        {
            new WiringTestWindow(_coord) { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        // A solenoid latches on and releases after a few seconds or when pressed again; real drive is off the UI thread.
        private void Solenoid_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            string name = (string)btn.Tag; var lamps = _lamps;

            if (_solLatch.TryGetValue(name, out var held))   // already latched → release now
            {
                held.timer.Stop();
                _solLatch.Remove(name);
                Bg(() => lamps.SetLamp(name, false));
                ResetButtonLook(btn);
                Sfx("solenoidoff");
                return;
            }

            SetButtonActiveLook(btn, "RedBrush");   // latch on
            Sfx("solenoidon");
            Bg(() => lamps.SetLamp(name, true));

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(SolenoidLatchSeconds) };
            timer.Tick += (s, ev) =>
            {
                timer.Stop();
                _solLatch.Remove(name);
                Bg(() => lamps.SetLamp(name, false));
                ResetButtonLook(btn);
                Sfx("solenoidoff");
            };
            _solLatch[name] = (timer, btn);
            timer.Start();
        }

        // Release every latched solenoid (used by ALL OFF and when leaving the screen).
        private void ClearSolenoidLatches(bool turnOff)
        {
            foreach (var kv in _solLatch)
            {
                kv.Value.timer.Stop();
                ResetButtonLook(kv.Value.btn);
                if (turnOff) { var lamps = _lamps; string n = kv.Key; Bg(() => lamps.SetLamp(n, false)); }
            }
            _solLatch.Clear();
        }

        private static readonly string[] LedPatterns =
        {
            "Rainbow Wave", "Comet", "Marquee", "Breathe", "Crazy Strobe", "Plasma Wave", "Meteor Rain",
            "Sparkle", "Knight Rider", "Bouncing Balls", "Lightning", "Confetti", "DNA Helix", "Quad Strobe",
        };
        private string _ledPatternName = "Rainbow Wave";
        private int _ledFrame;
        private bool _ledReady;
        private readonly Random _ledRnd = new();

        private void LedPatternCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (LedPatternCombo.SelectedItem is not string name) return;
            _ledPatternName = name; _ledFrame = 0;
            if (!_ledTimer.IsEnabled) _ledTimer.Start();          // on-screen preview always
            if (_ledReady) { var c = _coord; Bg(() => c?.LedPattern(name)); Sfx("ledpatternon"); }   // drive real strip off the UI thread
        }

        private void LedStop_Click(object sender, RoutedEventArgs e)
        {
            StopLedPreview();      // stop the on-screen preview
            var c = _coord; Bg(() => c?.LedOff());   // stop the real strip (off the UI thread)
            Sfx("ledpatternoff");  // dropdown keeps its selection
        }

        private void Led_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            string c = (string)btn.Tag;
            StopLedPreview();
            var coord = _coord;
            // click the lit color again = turn the strip back off
            if (_activeColorBtn == btn)
            {
                ResetButtonLook(btn); _activeColorBtn = null;
                LedDim(); Bg(() => coord?.LedOff()); Sfx("solidledoff");
                return;
            }
            if (_activeColorBtn != null) ResetButtonLook(_activeColorBtn);
            _ledColor = MediaColor(c); LedAllDots(_ledColor); string cs = c; Bg(() => coord?.LedSolid(cs));
            SetButtonActiveLook(btn, "CyanBrush"); _activeColorBtn = btn; Sfx("solidledon");
        }

        private void LedBright_Click(object sender, RoutedEventArgs e)
        {
            var coord = _coord;
            if ((string)((Button)sender).Tag == "up") { _ledBright = Math.Min(1.0, _ledBright + 0.2); Bg(() => coord?.LedBrightnessUp()); Sfx("brightnessup"); }
            else { _ledBright = Math.Max(0.2, _ledBright - 0.2); Bg(() => coord?.LedBrightnessDown()); Sfx("brightnessdown"); }
            if (!_ledTimer.IsEnabled) LedAllDots(_ledColor);   // reapply current solid at new brightness
        }

        // ---------------- LED strip on-screen preview ----------------
        private void BuildLedStrip()
        {
            for (int i = 0; i < 18; i++)
            {
                var d = new Border
                {
                    Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Margin = new Thickness(2, 0, 2, 0),
                    Background = new SolidColorBrush(Color.FromArgb(0x40, 0x20, 0x26, 0x34)),
                };
                _ledDots.Add(d);
                LedDotHost.Children.Add(d);
            }
        }

        // Winner / Game Over / Tilt lamps live on the backglass map — clickable + testable like the score lamps.
        private void BuildSpecialLamps()
        {
            foreach (var (name, label, _) in SpecialLamps)
            {
                var t = new TextBlock { Text = label, FontFamily = Font("DisplayFont"), FontSize = 18, Foreground = Brush("MutedBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var b = new Border
                {
                    Height = 56, MinWidth = 112, Margin = new Thickness(6, 0, 6, 0), Padding = new Thickness(14, 0, 14, 0),
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.FromArgb(0x14, 0x20, 0x26, 0x34)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1), Child = t,
                    Cursor = Cursors.Hand, Tag = name, ToolTip = $"Click to toggle the {label} lamp",
                };
                b.MouseLeftButtonUp += SpecialLamp_Click;
                _specialLampBorder[name] = b; _specialLampText[name] = t;
                LampSpecialHost.Children.Add(b);
            }
        }

        private void SpecialLamp_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not Border b || b.Tag is not string name) return;
            if (_identifyMode) { BlinkLamp(name); Sfx("ledbutton"); return; }   // blink to find it, don't latch
            var loc = LampController.Outputs[name];
            bool on = !PinActivity.IsActive(loc.board, loc.pin);
            var lamps = _lamps;
            Bg(() => lamps.SetLamp(name, on));
            Sfx(on ? "ledbutton" : "ledbuttonoff");
        }

        // Solenoids get indicator LEDs that light while energized.
        private void BuildSolenoidInd()
        {
            foreach (var (name, label) in Solenoids)
            {
                var dot = new Ellipse { Width = 13, Height = 13, Fill = Brush("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center };
                var lbl = new TextBlock { Text = label, FontFamily = Font("DisplayFont"), FontSize = 10.5, Foreground = Brush("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
                var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                sp.Children.Add(dot); sp.Children.Add(lbl);
                _solDots[name] = dot;
                SolenoidHost.Children.Add(sp);
            }
        }

        private void LedAllDots(Color c)
        {
            var b = new SolidColorBrush(Scale(c));
            foreach (var d in _ledDots) d.Background = b;
        }
        private void LedDim()
        {
            var b = new SolidColorBrush(Color.FromArgb(0x40, 0x20, 0x26, 0x34));
            foreach (var d in _ledDots) d.Background = b;
        }
        private void StopLedPreview() { _ledTimer.Stop(); }

        private static readonly Color DimC = Color.FromArgb(0x40, 0x20, 0x26, 0x34);
        private Color Mul(Color c, double f) { f = Math.Max(0, Math.Min(1, f)) * _ledBright; return Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f)); }

        // The on-screen strip mimics the chosen WS2812b pattern.
        private void LedTick(object? sender, EventArgs e)
        {
            int n = _ledDots.Count; if (n == 0) return;
            int f = ++_ledFrame;
            for (int i = 0; i < n; i++)
            {
                Color col;
                switch (_ledPatternName)
                {
                    case "Marquee":
                        col = ((i + f) % 4) < 2 ? Mul(Color.FromRgb(0xF0, 0xC4, 0x37), 1) : DimC; break;
                    case "Comet":
                    { int head = f % (n + 8); int d = head - i; col = (d >= 0 && d < 8) ? Mul(Color.FromRgb(0x33, 0xC9, 0xFF), 1 - d / 8.0) : DimC; break; }
                    case "Meteor Rain":
                    { int head = f % (n + 6); int d = head - i; col = (d >= 0 && d < 6) ? Mul(Color.FromRgb(0xF0, 0x8A, 0x2A), 1 - d / 6.0) : DimC; break; }
                    case "Knight Rider":
                    { int per = 2 * (n - 1); int p = f % per; int pos = p < n ? p : per - p; int d = Math.Abs(i - pos); col = d == 0 ? Mul(Color.FromRgb(0xE2, 0x49, 0x3C), 1) : d == 1 ? Mul(Color.FromRgb(0xE2, 0x49, 0x3C), 0.35) : DimC; break; }
                    case "Bouncing Balls":
                    { int per = 2 * (n - 1); int a = f % per; int pa = a < n ? a : per - a; int b = (f + n) % per; int pb = b < n ? b : per - b; col = i == pa ? Mul(Color.FromRgb(0x2F, 0xE0, 0x8C), 1) : i == pb ? Mul(Color.FromRgb(0xB4, 0x5A, 0xE0), 1) : DimC; break; }
                    case "Breathe":
                        col = Mul(Color.FromRgb(0x16, 0xC8, 0xB0), 0.25 + 0.75 * (0.5 + 0.5 * Math.Sin(f * 0.12))); break;
                    case "Crazy Strobe":
                        col = (f % 3 == 0) ? Mul(FromHue(_ledRnd.Next(360)), 1) : DimC; break;
                    case "Quad Strobe":
                    { int q = i * 4 / n; col = ((q + f / 3) % 2 == 0) ? Mul(Color.FromRgb(0xED, 0xED, 0xF2), 1) : DimC; break; }
                    case "Lightning":
                        col = _ledRnd.Next(9) == 0 ? Mul(Color.FromRgb(0xED, 0xED, 0xF2), 1) : DimC; break;
                    case "Sparkle":
                    case "Confetti":
                        col = _ledRnd.Next(3) == 0 ? Mul(FromHue(_ledRnd.Next(360)), 1) : DimC; break;
                    case "DNA Helix":
                        col = Mul(FromHue((i * 18 + f * 6) % 360), 0.3 + 0.7 * (0.5 + 0.5 * Math.Sin(i * 0.6 + f * 0.18))); break;
                    default: // Rainbow Wave / Plasma Wave
                        col = Mul(FromHue((i * 20 + f * 8) % 360), 1); break;
                }
                _ledDots[i].Background = new SolidColorBrush(col);
            }
        }

        private Color Scale(Color c) => Color.FromRgb((byte)(c.R * _ledBright), (byte)(c.G * _ledBright), (byte)(c.B * _ledBright));

        private static Color MediaColor(string n) => n switch
        {
            "Red" => Color.FromRgb(0xE2, 0x49, 0x3C), "Green" => Color.FromRgb(0x2F, 0xE0, 0x8C),
            "Blue" => Color.FromRgb(0x33, 0xC9, 0xFF), "Yellow" => Color.FromRgb(0xF0, 0xDE, 0x3C),
            "Purple" => Color.FromRgb(0xB4, 0x5A, 0xE0), "White" => Color.FromRgb(0xED, 0xED, 0xF2),
            _ => Colors.White,
        };

        private static Color FromHue(double h)
        {
            double x = 1 - Math.Abs((h / 60.0) % 2 - 1);
            double r, g, b;
            if (h < 60) { r = 1; g = x; b = 0; } else if (h < 120) { r = x; g = 1; b = 0; }
            else if (h < 180) { r = 0; g = 1; b = x; } else if (h < 240) { r = 0; g = x; b = 1; }
            else if (h < 300) { r = x; g = 0; b = 1; } else { r = 1; g = 0; b = x; }
            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private static readonly string[] SoundFiles =
            { "10 pts", "20 pts", "30 pts", "40 pts", "50 pts", "60 pts", "70 pts", "80 pts", "Game Over" };

        private void ToggleSound()
        {
            if (_soundActive) { StopSoundTest(); return; }
            _soundActive = true;
            SetButtonActiveLook(SoundTestBtn, "GreenBrush");
            SoundTestBtn.Content = "STOP SOUND";
            StartVu();
            PlayRandomSound();
            _soundTimer.Start();   // keep firing random sounds until stopped
        }

        private void StopSoundTest()
        {
            _soundActive = false;
            _soundTimer.Stop();
            _vuTimer.Stop();
            ResetButtonLook(SoundTestBtn);
            SoundTestBtn.Content = "SOUND TEST";
            foreach (var bar in _vuBars) { bar.BeginAnimation(HeightProperty, null); bar.Height = 2; }
            AppState.Audio.StopAudio();
        }

        private void SoundTick(object? sender, EventArgs e)
        {
            _soundTimer.Stop();               // one-shot: re-armed to the next clip's length
            if (!_soundActive) return;
            PlayRandomSound();
            _soundTimer.Start();
        }

        // Play a random scoring sound in full, then time the next one to this clip's length (+ a short gap).
        private void PlayRandomSound()
        {
            string snd = SoundFiles[_vuRnd.Next(SoundFiles.Length)];
            AppState.Audio.PlaySound(AppState.SoundPackage, snd);
            double ms = AppState.Audio.ClipLengthMs(AppState.SoundPackage, snd);
            _soundTimer.Interval = TimeSpan.FromMilliseconds(ms > 0 ? ms + 350 : 900);
        }

        // ---------------- audio VU meter ----------------
        private void BuildVu()
        {
            for (int i = 0; i < 20; i++)
            {
                var bar = new Border
                {
                    Width = 6, Height = 2, Margin = new Thickness(2, 0, 2, 0),
                    CornerRadius = new CornerRadius(3),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Background = Brush("TealBrush"),
                };
                _vuBars.Add(bar);
                VuHost.Children.Add(bar);
            }
        }

        private void StartVu() { if (!_vuTimer.IsEnabled) _vuTimer.Start(); }

        private void VuTick(object? sender, EventArgs e)
        {
            if (!_soundActive) { _vuTimer.Stop(); return; }   // meter runs while sound is playing
            for (int i = 0; i < _vuBars.Count; i++)
            {
                double center = 1.0 - Math.Abs(i - (_vuBars.Count - 1) / 2.0) / (_vuBars.Count / 2.0); // taller in the middle
                double target = 4 + _vuRnd.NextDouble() * 40 * (0.4 + 0.6 * center);
                _vuBars[i].Background = Brush(target > 30 ? "AmberBrush" : target > 16 ? "GreenBrush" : "TealBrush");
                var a = new System.Windows.Media.Animation.DoubleAnimation(target, TimeSpan.FromMilliseconds(70));
                _vuBars[i].BeginAnimation(HeightProperty, a);
            }
        }

        // ---------------- live signal scope ----------------
        private void BuildScope()
        {
            if (ScopeHost == null) return;
            _scopeLine = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(63, 240, 150)),
                StrokeThickness = 1.8,
                StrokeLineJoin = PenLineJoin.Round,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                { Color = Color.FromRgb(47, 224, 140), BlurRadius = 7, ShadowDepth = 0, Opacity = 0.75 },
            };
            ScopeHost.Children.Add(_scopeLine);
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        private void ScopeTick(object? sender, EventArgs e)
        {
            if (_scopeLine == null || ScopeHost == null) return;
            double w = ScopeHost.ActualWidth, h = ScopeHost.ActualHeight;
            if (w < 4 || h < 4) return;

            // random-walk the carrier so it never quite repeats; irrational freq ratio between the two tones
            _scopeA1 = Clamp(_scopeA1 + (_scopeRnd.NextDouble() - 0.5) * 0.03, 0.05, 0.22);
            _scopeA2 = Clamp(_scopeA2 + (_scopeRnd.NextDouble() - 0.5) * 0.02, 0.02, 0.12);
            _scopeDrift += (_scopeRnd.NextDouble() - 0.5) * 0.18;
            _scopePhase += 0.28 + _scopeRnd.NextDouble() * 0.06;

            // sporadic travelling pulse — more frequent when the machine is active
            if (_beatTtl <= 0 && _scopeRnd.NextDouble() < 0.035 + _scopeEnergy * 0.5)
            {
                _beatTtl = _beatLen = 4 + _scopeRnd.Next(6);
                _beatAmp = (0.28 + _scopeRnd.NextDouble() * 0.32) * (0.5 + _scopeEnergy);
                _beatSign = _scopeRnd.NextDouble() < 0.5 ? -1 : 1;
            }

            double carrier = Math.Sin(_scopePhase) * _scopeA1
                           + Math.Sin(_scopePhase * 2.31732 + _scopeDrift) * _scopeA2;
            double grass = (_scopeRnd.NextDouble() - 0.5) * (0.085 + _scopeEnergy * 0.8);   // always-on jitter
            double beat = 0;
            if (_beatTtl > 0)
            {
                double p = 1.0 - (_beatTtl - 1) / (double)_beatLen;      // 0..1 across the pulse
                beat = _beatSign * _beatAmp * Math.Sin(Math.PI * p);     // a clean hump that scrolls off
                _beatTtl--;
            }

            for (int i = 0; i < ScopeSamples - 1; i++) _scope[i] = _scope[i + 1];
            _scope[ScopeSamples - 1] = Clamp(carrier + grass + beat, -1, 1);
            _scopeEnergy *= 0.92;

            double mid = h / 2.0;
            var pts = new PointCollection(ScopeSamples);
            for (int i = 0; i < ScopeSamples; i++)
                pts.Add(new Point(i / (double)(ScopeSamples - 1) * w, mid - _scope[i] * mid * 0.5));   // keep peaks/troughs well inside the frame
            _scopeLine.Points = pts;
        }

        // ---------------- ring gauge ----------------
        private void SetRing(double frac)
        {
            if (frac <= 0) { RingArc.Data = null; return; }
            frac = Math.Min(frac, 0.9999);
            const double r = 70, cx = 82, cy = 82;
            var start = new Point(cx, cy - r);
            double ang = Math.PI * 2 * frac - Math.PI / 2;
            var end = new Point(cx + r * Math.Cos(ang), cy + r * Math.Sin(ang));
            var fig = new System.Windows.Media.PathFigure { StartPoint = start, IsClosed = false };
            fig.Segments.Add(new System.Windows.Media.ArcSegment(end, new Size(r, r), 0, frac > 0.5, SweepDirection.Clockwise, true));
            RingArc.Data = new System.Windows.Media.PathGeometry(new[] { fig });
        }

        // ---------------- helpers ----------------
        private Brush Brush(string key) => (Brush)FindResource(key);
        private FontFamily Font(string key) => (FontFamily)FindResource(key);
        private Color ColorOf(string key) => ((SolidColorBrush)FindResource(key)).Color;
        private static void Sfx(string name) => AppState.Audio.PlaySettingsSound(name);

        // Run hardware I/O off the UI thread (wrapped in try/catch) so clicks stay snappy and a board pulled mid-op can't crash the app.
        private static void Bg(Action work) => Task.Run(() =>
        {
            try { work(); } catch (Exception ex) { Log.Error("diagnostics hardware op failed", ex); }
        });

        // ---- Live event log (most recent switch / solenoid / special-lamp events) ----
        private void AddEvent(string text)
        {
            _events.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + text);
            if (_events.Count > 40) _events.RemoveAt(_events.Count - 1);
            RenderEvents();
        }

        private void RenderEvents()
        {
            if (EventHost == null) return;
            EventHost.Children.Clear();
            for (int i = 0; i < _events.Count && i < 7; i++)
                EventHost.Children.Add(new TextBlock
                {
                    Text = _events[i], FontFamily = Font("MonoFont"), FontSize = 11.5,
                    Foreground = i == 0 ? Brush("TextBrush") : Brush("MutedBrush"),
                    Margin = new Thickness(0, 1, 0, 1),
                });
        }

        // ---- Per-switch hit count / last-fired / stuck readout on the tile's third line ----
        private void UpdateSwitchHitText(string id)
        {
            if (!_switchHitText.TryGetValue(id, out var tb)) return;
            long now = Environment.TickCount64;
            if (Faults.Contains(id))
            {
                tb.Text = "⚠ STUCK"; tb.Foreground = Brush("RedBrush"); return;
            }
            long hits = _switchHits.TryGetValue(id, out var h) ? h : 0;
            if (hits <= 0) { tb.Text = "—"; tb.Foreground = Brush("MutedBrush"); return; }
            string ago = "";
            if (_switchLastFiredTick.TryGetValue(id, out var last) && last > 0)
            {
                long a = now - last;
                ago = a < 1500 ? "just now" : a < 60000 ? (a / 1000) + "s ago" : (a / 60000) + "m ago";
            }
            tb.Text = $"×{hits}   {ago}"; tb.Foreground = Brush("MutedBrush");
        }

        // A switch held high far too long is almost certainly jammed — flag its tile red.
        private void MarkStuck(string id, bool stuck)
        {
            bool was = _stuck.Contains(id);
            if (stuck == was) return;
            if (stuck) _stuck.Add(id); else _stuck.Remove(id);
            if (_switchTiles.TryGetValue(id, out var tile))
            {
                if (stuck)
                {
                    var c = ColorOf("RedBrush");
                    tile.Background = new SolidColorBrush(c) { Opacity = 0.30 };
                    tile.BorderBrush = Brush("RedBrush");
                    tile.Effect = new DropShadowEffect { Color = c, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.85 };
                }
                else SetSwitch(id, _switchState.TryGetValue(id, out var on) && on);   // restore the normal look
            }
            if (stuck) AddEvent(id + "  ⚠ STUCK");
            UpdateSwitchHitText(id);
        }

        // Mirror the shared Faults set onto the switch tiles (the coordinator decides what's stuck).
        private void SyncStuck()
        {
            foreach (var sw in SwitchMap.All)
            {
                bool s = Faults.Contains(sw.Id);
                if (s != _stuck.Contains(sw.Id)) MarkStuck(sw.Id, s);
            }
        }

        // ---- 1 Hz health pass: refresh "last fired", flag stuck switches, show live poll rate + last-seen ----
        private void HealthTick(object? sender, EventArgs e)
        {
            // Stuck flags are driven by Faults.Changed; here just refresh each tile's "×N · Ns ago / STUCK" line.
            foreach (var sw in SwitchMap.All) UpdateSwitchHitText(sw.Id);
            if (_coord != null && _hasHardware)
            {
                long cyc = _coord.PollCycles; var t = DateTime.UtcNow;
                double secs = (t - _lastPollSample).TotalSeconds;
                if (secs >= 0.5)
                {
                    double hz = (cyc - _lastPollCycles) / 3.0 / secs;   // three input boards
                    PollText.Text = $"≈{Math.Round(hz)} Hz";
                    _lastPollCycles = cyc; _lastPollSample = t;
                    DevText.Text = $"FT232H  G1 {Ago(_coord.LastSeenAgoMs(1))} · G2 {Ago(_coord.LastSeenAgoMs(2))} · G3 {Ago(_coord.LastSeenAgoMs(3))} · G4 out   (connected)";
                }
            }
        }

        private static string Ago(long ms) => ms < 0 ? "—" : ms < 1000 ? ms + "ms" : (ms / 1000) + "s";

        // ---- Identify a lamp: blink it so you can find it on the playfield (toggle mode) ----
        private void Identify_Click(object sender, RoutedEventArgs e)
        {
            _identifyMode = !_identifyMode;
            if (_identifyMode) { SetButtonActiveLook(IdentifyBtn, "CyanBrush"); SeedIdentifyFromLit(); }
            else { ResetButtonLook(IdentifyBtn); StopIdentifyBlink(); }
            Sfx(_identifyMode ? "ledbutton" : "ledbuttonoff");
        }

        // Turning identify on makes any lamp already lit on the map start blinking, so you can point at a live board and find it.
        private void SeedIdentifyFromLit()
        {
            void Seed(string name, (int board, int pin) loc)
            {
                if (PinActivity.IsActive(loc.board, loc.pin)) _identifyBlink.Add(name);
            }
            foreach (var kv in _lampLoc) Seed(kv.Key.ToString(), kv.Value);
            foreach (var s in SpecialLamps)
                if (LampController.Outputs.TryGetValue(s.name, out var loc)) Seed(s.name, loc);
            if (_identifyBlink.Count > 0) { _identifyPhase = true; if (!_identifyTimer.IsEnabled) _identifyTimer.Start(); }
        }

        // Clicking a lamp in identify mode toggles a steady blink; it keeps going until clicked again or identify is shut off.
        private void BlinkLamp(string name)
        {
            if (_identifyBlink.Contains(name))
            {
                _identifyBlink.Remove(name);
                try { _lamps.SetLamp(name, false); } catch (Exception ex) { Log.Error("identify off failed", ex); }
                if (_identifyBlink.Count == 0) _identifyTimer.Stop();
            }
            else
            {
                _identifyBlink.Add(name);
                _identifyPhase = true;
                try { _lamps.SetLamp(name, true); } catch (Exception ex) { Log.Error("identify on failed", ex); }
                if (!_identifyTimer.IsEnabled) _identifyTimer.Start();
            }
        }

        private void IdentifyTick(object? sender, EventArgs e)
        {
            _identifyPhase = !_identifyPhase;
            foreach (var n in _identifyBlink)
                try { _lamps.SetLamp(n, _identifyPhase); } catch (Exception ex) { Log.Error("identify blink failed", ex); }
            if (_identifyBlink.Count == 0) _identifyTimer.Stop();
        }

        private void StopIdentifyBlink()
        {
            _identifyTimer.Stop();
            foreach (var n in _identifyBlink)
                try { _lamps.SetLamp(n, false); } catch (Exception ex) { Log.Error("identify clear failed", ex); }
            _identifyBlink.Clear();
        }
    }
}
