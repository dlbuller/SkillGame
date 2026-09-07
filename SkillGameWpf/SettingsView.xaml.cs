using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SkillGame;

namespace SkillGameWpf
{
    public partial class SettingsView : UserControl
    {
        private readonly OperatorSettings _s = AppState.Settings;

        private static readonly string[] TestSounds =
            { "10 pts", "20 pts", "30 pts", "40 pts", "50 pts", "60 pts", "70 pts", "80 pts", "Game Over" };
        private readonly DispatcherTimer _testTimer = new() { Interval = TimeSpan.FromMilliseconds(1100) };
        private int _testIdx;
        private bool _testActive;

        public SettingsView()
        {
            InitializeComponent();
            _testTimer.Tick += TestTick;
            BuildMedalSparkle();
            Loaded += (s, e) => { LoadFromSettings(); BuildGears(); BuildGearMachine(); };
            Unloaded += (s, e) => StopTest();
        }

        // Give the Wheezing Bull medallion life: a metallic shine sweeping across the coin plus a few twinkling stars.
        private void BuildMedalSparkle()
        {
            if (MedalGrid == null) return;

            // A bright diagonal glint that sweeps across the bull disc, clipped to the coin face.
            var shineHost = new Grid
            {
                Width = 78, Height = 78, IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Clip = new EllipseGeometry(new Point(39, 39), 39, 39),
            };
            var streak = new System.Windows.Shapes.Rectangle
            { Width = 22, Height = 150, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var lg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            lg.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.0));
            lg.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF), 0.5));
            lg.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0));
            streak.Fill = lg;
            var move = new TranslateTransform(-60, 0);
            streak.RenderTransform = new TransformGroup { Children = { new RotateTransform(22), move } };
            shineHost.Children.Add(streak);
            MedalGrid.Children.Add(shineHost);

            var sweep = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            sweep.KeyFrames.Add(new LinearDoubleKeyFrame(-60, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            sweep.KeyFrames.Add(new LinearDoubleKeyFrame(60, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(950))));
            sweep.KeyFrames.Add(new LinearDoubleKeyFrame(60, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(4200))));  // rest between glints
            move.BeginAnimation(TranslateTransform.XProperty, sweep);

            // Twinkling four-point stars around the coin ring, staggered so they shimmer.
            var star = Geometry.Parse("M0,-6 L1.6,-1.6 L6,0 L1.6,1.6 L0,6 L-1.6,1.6 L-6,0 L-1.6,-1.6 Z");
            var spots = new (double x, double y, int delay)[]
            { (54, -46, 0), (-50, 42, 520), (62, 26, 1040), (-46, -40, 1560), (28, 58, 780) };
            const int period = 2800;
            foreach (var (sx, sy, delay) in spots)
            {
                var p = new System.Windows.Shapes.Path
                {
                    Data = star, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xC9)), Opacity = 0,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    RenderTransformOrigin = new Point(0.5, 0.5), IsHitTestVisible = false,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    { Color = Color.FromRgb(0xFF, 0xE7, 0x9A), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 },
                };
                var sc = new ScaleTransform(0.2, 0.2);
                p.RenderTransform = new TransformGroup { Children = { sc, new TranslateTransform(sx, sy) } };
                MedalGrid.Children.Add(p);

                var begin = TimeSpan.FromMilliseconds(delay);
                var op = new DoubleAnimationUsingKeyFrames { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever };
                op.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                op.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280))));
                op.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(680))));
                op.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(period))));
                p.BeginAnimation(OpacityProperty, op);

                var scAnim = new DoubleAnimationUsingKeyFrames { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever };
                scAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0.2, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                scAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280)), new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut }));
                scAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0.2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(680))));
                scAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0.2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(period))));
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, scAnim);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, scAnim.Clone());
            }
        }

        // ---- Test all sounds in the selected set ----
        private void TestSet_Click(object sender, RoutedEventArgs e)
        {
            if (_testActive) { StopTest(); return; }
            _testActive = true; _testIdx = 0;
            TestSetBtn.Content = "STOP";
            TestNextBtn.Visibility = Visibility.Visible;
            TestNowText.Visibility = Visibility.Visible;
            PlayCurrentAndSchedule();
        }

        // Auto-advance once the current clip has finished (one-shot timer set to the clip's length).
        private void TestTick(object? sender, EventArgs e)
        {
            _testTimer.Stop();
            if (_testActive) Advance();
        }

        // NEXT: skip ahead without waiting for the current clip to finish.
        private void TestNext_Click(object sender, RoutedEventArgs e)
        {
            if (!_testActive) return;
            _testTimer.Stop();
            Advance();
        }

        private void Advance()
        {
            _testIdx++;
            if (_testIdx >= TestSounds.Length) { StopTest(); return; }
            PlayCurrentAndSchedule();
        }

        // Play the current sound in full, show what's playing, then schedule auto-advance for clip length + a gap.
        private void PlayCurrentAndSchedule()
        {
            string pkg = SoundPkgCombo.SelectedItem?.ToString() ?? AppState.SoundPackage;
            string snd = TestSounds[_testIdx];
            if (TestNowText != null) TestNowText.Text = $"NOW: {snd.ToUpperInvariant()}   ({_testIdx + 1}/{TestSounds.Length})";
            AppState.Audio.PlaySound(pkg, snd);
            double ms = AppState.Audio.ClipLengthMs(pkg, snd);
            _testTimer.Interval = TimeSpan.FromMilliseconds(ms > 0 ? ms + 400 : 1200);   // whole clip + a short gap
            _testTimer.Start();
        }

        private void StopTest()
        {
            _testActive = false;
            _testTimer.Stop();
            AppState.Audio.StopAudio();
            if (TestSetBtn != null) TestSetBtn.Content = "TEST";
            if (TestNextBtn != null) TestNextBtn.Visibility = Visibility.Collapsed;
            if (TestNowText != null) TestNowText.Visibility = Visibility.Collapsed;
        }

        private void LoadFromSettings()
        {
            CountUp.Value = _s.CountUpStepMs;
            AttractLight.Value = _s.AttractLightSeconds;
            AttractSound.Value = _s.AttractSoundSeconds;
            TiltToggle.IsChecked = _s.TiltEnabled;
            BenchToggle.IsChecked = _s.BenchMode;
            TiltsSlider.Value = _s.TiltsAllowed;
            QuietToggle.IsChecked = _s.QuietHoursEnabled;
            QuietStart.Value = _s.QuietStartHour;
            QuietEnd.Value = _s.QuietEndHour;
            QuietVol.Value = _s.QuietVolumePercent;
            Brightness.Value = _s.ScreenBrightness;

            CountUp_Changed(null, null); AttractLight_Changed(null, null); AttractSound_Changed(null, null);
            QuietStart_Changed(null, null); QuietEnd_Changed(null, null); QuietVol_Changed(null, null); TiltsAllowed_Changed(null, null);
            Brightness_Changed(null, null); BuildAccents();

            SoundFxToggle.IsChecked = AppState.SoundFxOn;
            AttractSoundToggle.IsChecked = AppState.AttractSoundOn;
            AttractLightsToggle.IsChecked = AppState.AttractLightsOn;
            DistractSoundToggle.IsChecked = AppState.DistractSoundOn;
            DistractLightsToggle.IsChecked = AppState.DistractLightsOn;

            var pkgs = SoundPackages.List();
            SoundPkgCombo.ItemsSource = pkgs;
            SoundPkgCombo.SelectedItem = pkgs.Contains(AppState.SoundPackage) ? AppState.SoundPackage : (pkgs.Count > 0 ? pkgs[0] : null);
            _pkgReady = true;
            _loaded = true;
            UpdateVolText();

            BuildInfo();
        }

        private bool _pkgReady;

        // ---- Sound & Lighting ----
        // The master-volume API uses MMDevice COM (endpoint enumeration) which can be slow; do it off the
        // UI thread and marshal the resulting text back.
        private void UpdateVolText()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                string v; try { v = AppState.Audio.GetMasterVolume(); } catch (Exception ex) { Log.Error("get volume failed", ex); v = "Error"; }
                Dispatcher.BeginInvoke(new Action(() => SetVolText(v)));
            });
        }

        private void SetVolText(string v)
        {
            if (v == "Error") { VolText.Text = "--"; return; }
            bool muted = v.StartsWith("*");
            VolText.Text = muted ? "MUTE" : (v + "%");
            if (MuteBtn == null) return;
            if (muted)
            {
                MuteBtn.Content = "MUTED";
                MuteBtn.Foreground = (Brush)FindResource("RedBrush");
                MuteBtn.BorderBrush = (Brush)FindResource("RedBrush");
                MuteBtn.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xE2, 0x49, 0x3C));
            }
            else
            {
                MuteBtn.Content = "MUTE";
                MuteBtn.ClearValue(ForegroundProperty);
                MuteBtn.ClearValue(BorderBrushProperty);
                MuteBtn.ClearValue(BackgroundProperty);
            }
        }

        private static void Sfx(string n) => AppState.Audio.PlaySettingsSound(n);

        private void VolUp_Click(object sender, RoutedEventArgs e) { AdjustVolume(() => AppState.Audio.SetMasterVolumeUp()); Sfx("volume"); }
        private void VolDown_Click(object sender, RoutedEventArgs e) { AdjustVolume(() => AppState.Audio.SetMasterVolumeDown()); Sfx("volume"); }

        // Change + re-read the master volume together on one bg thread (keeps set-then-get in order).
        private void AdjustVolume(Action change)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                string v;
                try { change(); v = AppState.Audio.GetMasterVolume(); }
                catch (Exception ex) { Log.Error("volume change failed", ex); v = "Error"; }
                Dispatcher.BeginInvoke(new Action(() => SetVolText(v)));
            });
        }

        // A throttled tick when an operator slider moves.
        private int _lastSliderSfx;
        private bool _loaded;
        private void SliderTick()
        {
            if (!_loaded) return;
            int now = Environment.TickCount;
            if (now - _lastSliderSfx < 130) return;
            _lastSliderSfx = now;
            Sfx("volume");
        }
        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                string v;
                try
                {
                    bool wasMuted = AppState.Audio.GetMasterVolume().StartsWith("*");
                    AppState.Audio.MuteVolume(wasMuted);   // MuteVolume(true) unmutes, (false) mutes
                    Sfx(wasMuted ? "muteoff" : "mutebutton");
                    v = AppState.Audio.GetMasterVolume();
                }
                catch (Exception ex) { Log.Error("mute toggle failed", ex); v = "Error"; }
                Dispatcher.BeginInvoke(new Action(() => SetVolText(v)));
            });
        }

        private void SoundPkg_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (SoundPkgCombo.SelectedItem is string p)
            {
                AppState.SoundPackage = p;
                if (_pkgReady) { Sfx("select"); AppState.PersistSettings(); }   // persist the chosen set
            }
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            bool on = ((System.Windows.Controls.CheckBox)sender).IsChecked == true;
            switch ((string)((System.Windows.FrameworkElement)sender).Tag)
            {
                case "soundfx": AppState.SoundFxOn = on; Sfx(on ? "soundfxon" : "soundfxoff"); AppState.PersistSettings(); break;
                case "attractsound": AppState.AttractSoundOn = on; Sfx(on ? "attracton" : "attractoff"); AppState.PersistSettings(); break;
                case "attractlights": AppState.AttractLightsOn = on; Sfx(on ? "attracton" : "attractoff"); AppState.PersistSettings(); break;
                case "distractsound": AppState.DistractSoundOn = on; Sfx(on ? "distracton" : "distractoff"); AppState.PersistSettings(); break;
                case "distractlights": AppState.DistractLightsOn = on; Sfx(on ? "distracton" : "distractoff"); AppState.PersistSettings(); break;
                case "bench": AppState.SetBenchMode(on); Sfx(on ? "soundfxon" : "soundfxoff"); break;   // on = clear stuck; off = re-check the inputs
                default: Sfx("select"); break;   // tilt / quiet-hours persist on SAVE
            }
        }

        private void CountUp_Changed(object? s, RoutedPropertyChangedEventArgs<double>? e)
        { if (CountUpVal != null) CountUpVal.Text = $"{(int)CountUp.Value} ms"; SliderTick(); }
        private void AttractLight_Changed(object? s, RoutedPropertyChangedEventArgs<double>? e)
        { if (AttractLightVal != null) AttractLightVal.Text = $"{(int)AttractLight.Value} s"; SliderTick(); }
        private void AttractSound_Changed(object? s, RoutedPropertyChangedEventArgs<double>? e)
        { if (AttractSoundVal != null) AttractSoundVal.Text = $"{(int)AttractSound.Value} s"; SliderTick(); }
        private void QuietStart_Changed(object? s, RoutedPropertyChangedEventArgs<double>? e)
        { if (QuietStartVal != null) QuietStartVal.Text = Hour12((int)QuietStart.Value); SliderTick(); }
        private void QuietEnd_Changed(object? s, RoutedPropertyChangedEventArgs<double>? e)
        { if (QuietEndVal != null) QuietEndVal.Text = Hour12((int)QuietEnd.Value); SliderTick(); }
        private void QuietVol_Changed(object? s, RoutedPropertyChangedEventArgs<double>? e)
        { if (QuietVolVal != null) QuietVolVal.Text = $"{(int)QuietVol.Value}%"; SliderTick(); }
        private void TiltsAllowed_Changed(object? s, RoutedPropertyChangedEventArgs<double>? e)
        { if (TiltsVal != null) TiltsVal.Text = ((int)TiltsSlider.Value).ToString(); SliderTick(); }

        private void Brightness_Changed(object? s, RoutedPropertyChangedEventArgs<double>? e)
        {
            if (BrightnessVal != null) BrightnessVal.Text = $"{(int)Brightness.Value}%";
            if (!_loaded) return;
            int pct = (int)Brightness.Value;
            _s.ScreenBrightness = pct;
            (Window.GetWindow(this) as MainWindow)?.SetBrightness(pct);
            AppState.PersistSettings();
            SliderTick();
        }

        // Accent-color swatches; clicking one recolors the whole UI live and saves it.
        private void BuildAccents()
        {
            AccentSwatches.Children.Clear();
            foreach (var kv in AppState.Accents)
            {
                string name = kv.Key;
                var col = (Color)ColorConverter.ConvertFromString(kv.Value.main);
                bool sel = string.Equals(name, _s.AccentColor, StringComparison.OrdinalIgnoreCase);
                var ring = new Border
                {
                    Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 10, 0),
                    Background = new SolidColorBrush(col), BorderThickness = new Thickness(sel ? 3 : 0),
                    BorderBrush = new SolidColorBrush(Colors.White), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = name,
                };
                if (sel) { ring.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = col, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 }; if (AccentName != null) AccentName.Text = name.ToUpperInvariant(); }
                ring.MouseLeftButtonDown += (s, e) => { AppState.ApplyAccent(name); AppState.PersistSettings(); Sfx("select"); BuildAccents(); TintGears(); };
                AccentSwatches.Children.Add(ring);
            }
        }

        // Two interlocking cogs in the SOUND · LIGHTING header, spinning opposite ways — the machine humming along.
        private System.Windows.Shapes.Path? _gearA, _gearB;
        private void BuildGears()
        {
            if (GearHost == null) return;
            GearHost.Children.Clear();
            _gearA = MakeGear(20, 20, 14, 9.5, 5, 9, "GoldBrush");
            _gearB = MakeGear(46, 22, 11, 7.5, 4, 8, "CyanBrush");
            GearHost.Children.Add(_gearA);
            GearHost.Children.Add(_gearB);
            Spin(_gearA, 5200, false);
            Spin(_gearB, 4100, true);
        }

        // A cog: toothed rim, center hole, optional spoke cut-outs; squashY < 1 tips it onto a horizontal axle (edge-on).
        private System.Windows.Shapes.Path MakeGear(double cx, double cy, double rOut, double rIn, double rHole, int teeth, string brushKey, int spokes = 0, double squashY = 1.0)
        {
            var fig = new PathFigure { IsClosed = true };
            int steps = teeth * 2;
            for (int i = 0; i <= steps; i++)
            {
                double a = Math.PI * 2 * i / steps;
                double r = (i % 2 == 0) ? rOut : rIn;
                var p = new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
                if (i == 0) fig.StartPoint = p; else fig.Segments.Add(new LineSegment(p, true));
            }
            var geo = new PathGeometry(); geo.Figures.Add(fig);
            var holes = new GeometryGroup();
            holes.Children.Add(new EllipseGeometry(new Point(cx, cy), rHole, rHole));
            if (spokes > 0)
            {
                double rmid = (rHole + rIn) / 2 + 1, rsp = Math.Max(1.6, (rIn - rHole) / 2.6);
                for (int k = 0; k < spokes; k++)
                {
                    double a = Math.PI * 2 * k / spokes;
                    holes.Children.Add(new EllipseGeometry(new Point(cx + rmid * Math.Cos(a), cy + rmid * Math.Sin(a)), rsp, rsp));
                }
            }
            var body = new CombinedGeometry(GeometryCombineMode.Exclude, geo, holes);
            var rot = new RotateTransform(0, cx, cy);
            Transform xform = rot;
            if (Math.Abs(squashY - 1) > 0.001)
            {
                var g = new TransformGroup(); g.Children.Add(rot); g.Children.Add(new ScaleTransform(1, squashY, cx, cy)); xform = g;
            }
            return new System.Windows.Shapes.Path { Data = body, Fill = (Brush)FindResource(brushKey), Opacity = 0.85, RenderTransform = xform };
        }

        private static void Spin(System.Windows.Shapes.Path gear, int ms, bool reverse)
        {
            RotateTransform? rot = gear.RenderTransform as RotateTransform;
            if (rot == null && gear.RenderTransform is TransformGroup g)
                foreach (var t in g.Children) if (t is RotateTransform r) { rot = r; break; }
            rot?.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, reverse ? -360 : 360, TimeSpan.FromMilliseconds(ms)) { RepeatBehavior = RepeatBehavior.Forever });
        }

        private void TintGears()
        {
            if (_gearA != null) _gearA.Fill = (Brush)FindResource("GoldBrush");
            if (_gearB != null) _gearB.Fill = (Brush)FindResource("CyanBrush");
        }

        // A busy little gearworks at the bottom of the SOUND · LIGHTING panel: meshing cogs of every size, a
        // sideways flywheel, and a rack-and-pinion bar sliding along the bottom — all turning at their own speed.
        private void BuildGearMachine()
        {
            if (GearMachineHost == null || GearMachineHost.Children.Count > 0) return;
            void Add(double cx, double cy, double rO, double rI, double rH, int teeth, int spokes, string brush, int ms, bool rev, double sq = 1)
            {
                var g = MakeGear(cx, cy, rO, rI, rH, teeth, brush, spokes, sq);
                GearMachineHost.Children.Add(g);
                Spin(g, ms, rev);
            }
            // left meshing train — big drives small, alternating directions, speed scaling with size
            Add(50, 56, 38, 27, 11, 13, 6, "GoldBrush", 7000, false);   // big driver
            Add(104, 34, 22, 15, 7, 10, 5, "CyanBrush", 5000, true);
            Add(142, 58, 15, 10, 4, 8, 0, "PurpleBrush", 4000, false);
            Add(168, 28, 11, 7, 3, 7, 0, "GreenBrush", 3000, true);
            // center flywheel on a horizontal axle (edge-on)
            Add(228, 52, 36, 25, 8, 18, 6, "MutedBrush", 6400, false, 0.40);   // big sideways flywheel
            // right cluster
            Add(296, 50, 28, 20, 8, 12, 6, "GoldBrush", 6000, false);   // second big gear
            Add(342, 32, 16, 11, 5, 9, 0, "CyanBrush", 3800, true);
            Add(366, 56, 11, 7, 3, 7, 0, "PurpleBrush", 2800, false);
            BuildRack();
        }

        // A rack bar that slides back and forth like it's driven by the right-hand pinion.
        private void BuildRack()
        {
            var rack = new Canvas();
            var steel = (Brush)FindResource("MutedBrush");
            var bar = new System.Windows.Shapes.Rectangle { Width = 150, Height = 7, RadiusX = 2, RadiusY = 2, Fill = steel, Opacity = 0.65 };
            Canvas.SetTop(bar, 6); rack.Children.Add(bar);
            for (int i = 0; i < 23; i++)
            {
                var tooth = new System.Windows.Shapes.Rectangle { Width = 3, Height = 4, Fill = steel, Opacity = 0.65 };
                Canvas.SetLeft(tooth, i * 6.4 + 2); Canvas.SetTop(tooth, 2); rack.Children.Add(tooth);
            }
            Canvas.SetLeft(rack, 214); Canvas.SetTop(rack, 92);
            var tt = new TranslateTransform(); rack.RenderTransform = tt;
            GearMachineHost.Children.Add(rack);
            tt.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(6, -44, TimeSpan.FromMilliseconds(2800)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }

        private static string Hour12(int h24)
        {
            int h = h24 % 12; if (h == 0) h = 12;
            return $"{h} {(h24 < 12 ? "AM" : "PM")}";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _s.CountUpStepMs = (int)CountUp.Value;
            _s.AttractLightSeconds = (int)AttractLight.Value;
            _s.AttractSoundSeconds = (int)AttractSound.Value;
            _s.TiltEnabled = TiltToggle.IsChecked == true;
            _s.TiltsAllowed = (int)TiltsSlider.Value;
            _s.QuietHoursEnabled = QuietToggle.IsChecked == true;
            _s.QuietStartHour = (int)QuietStart.Value;
            _s.QuietEndHour = (int)QuietEnd.Value;
            _s.QuietVolumePercent = (int)QuietVol.Value;
            _s.Clamp();
            _s.Save();
            _s.ApplyToQuietHours();
            AppState.ApplySettings?.Invoke(_s);   // count-up speed / tilt take effect live
            Sfx("settings");
            BuildInfo();

            // brief confirmation flash on the button
            SaveBtn.Content = "SAVED ✓";
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
            t.Tick += (a, b) => { t.Stop(); SaveBtn.Content = "SAVE SETTINGS"; };
            t.Start();
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (!AppDialog.Confirm("Reset Settings", "Reset all settings to their factory defaults?", "RESET", "CANCEL")) return;
            _s.ResetToDefaults();
            _s.Clamp();
            _s.Save();
            _s.ApplyToQuietHours();
            AppState.ApplyAccent(_s.AccentColor);          // restore the default accent brushes
            AppState.ApplySettings?.Invoke(_s);            // push count-up speed / tilt to the live game
            LoadFromSettings();                            // refresh every control + accent swatches + brightness
            Sfx("settings");
            ResetBtn.Content = "RESET ✓";
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
            t.Tick += (a, b) => { t.Stop(); ResetBtn.Content = "RESET TO DEFAULTS"; };
            t.Start();
        }

        private void BuildInfo()
        {
            InfoStack.Children.Clear();
            var a = AppState.Audits.Data;
            AddInfo("Version", "1.0");
            AddInfo("Total games (lifetime)", a.GamesPlayed.ToString("N0"));
            AddInfo("High score", a.HighScores.Count > 0 ? a.HighScores[0].Score.ToString("N0") : "—");
            AddInfo("This session", $"{AppState.Audits.SessionGames} games");
            AddInfo("Quiet hours", _s.QuietHoursEnabled ? $"{Hour12(_s.QuietStartHour)} – {Hour12(_s.QuietEndHour)} @ {_s.QuietVolumePercent}%" : "off");
            AddInfo("Tilt switch", _s.TiltEnabled ? (_s.TiltsAllowed == 0 ? "enabled · instant" : $"enabled · {_s.TiltsAllowed} warning{(_s.TiltsAllowed > 1 ? "s" : "")}") : "ignored");
            var sep = new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 12, 0, 10) };
            InfoStack.Children.Add(sep);
            AddInfo("Audit file", "skillgame_audits.json", small: true);
        }

        private void AddInfo(string label, string value, bool small = false)
        {
            var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = new TextBlock { Text = label, Foreground = (Brush)FindResource("MutedBrush"), FontSize = small ? 11 : 13, VerticalAlignment = VerticalAlignment.Center };
            var v = new TextBlock
            {
                Text = value,
                Foreground = (Brush)FindResource(small ? "MutedBrush" : "TextBrush"),
                FontFamily = (FontFamily)FindResource(small ? "UiFont" : "DisplayFont"),
                FontSize = small ? 11 : 16,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(v, 1);
            g.Children.Add(l); g.Children.Add(v);
            InfoStack.Children.Add(g);
        }
    }
}
