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
    public partial class GameStatusView
    {
        // Playfield palette (echoing the real board): yellow field, red/blue hole rings, black ball-rails.
        private const double PfW = 440, PfH = 661;
        private const double PfTop = 124, PfBottom = 104;
        private static readonly Color YellowBg = Color.FromRgb(0xE7, 0xC1, 0x1C);
        private static readonly Color RailBlk = Color.FromRgb(0x1A, 0x18, 0x10);
        private static readonly Color RingRed = Color.FromRgb(0xC1, 0x27, 0x27);
        private static readonly Color RingBlue = Color.FromRgb(0x1E, 0x4E, 0xA6);
        private static readonly Color Cream = Color.FromRgb(0xF4, 0xEC, 0xCE);
        private static readonly Color OrangeAr = Color.FromRgb(0xE6, 0x79, 0x1C);
        private static readonly Color SxLabel = Color.FromRgb(0x6A, 0x52, 0x08);
        private const double HoleD = 30;

        // Hole centres traced from the real (perspective-corrected) board photo, in the 404×648 canvas.
        private static readonly Dictionary<string, (double x, double y)> HolePos = new()
        {
            ["S0"] = (24, 138),                                                      // coin-up light — far left corner, ON the board (left of START)
            // Number-disc centres (the point circles): traced dead-on each printed number from the board photo.
            ["S1"] = (167, 161), ["S2"] = (260, 151), ["S3"] = (337, 147), ["S4"] = (414, 159),
            ["S5"] = (22, 233), ["S6"] = (100, 240), ["S7"] = (192, 222), ["S8"] = (265, 232),
            ["S9"] = (168, 296), ["S10"] = (254, 288), ["S11"] = (349, 299), ["S12"] = (412, 305),
            ["S13"] = (23, 368), ["S14"] = (83, 368), ["S15"] = (142, 368), ["S16"] = (263, 363),
            ["S17"] = (167, 426), ["S18"] = (237, 419), ["S19"] = (338, 412), ["S20"] = (406, 436),
            ["S21"] = (100, 471), ["S22"] = (187, 481),
            ["S24"] = (335, 550),
            ["S25"] = (97, 595), ["S26"] = (199, 595),
            ["S23"] = (250, 545),                                                    // wired gobble/drain
        };

        // Hole cup positions (drop-through points); the number discs above are only labels, and the coin drops through the cup to the rail below.
        private static readonly Dictionary<string, (double x, double y)> CupPos = new()
        {
            ["S1"] = (167, 179), ["S2"] = (261, 171), ["S3"] = (336, 165), ["S4"] = (413, 181),
            ["S5"] = (22, 252), ["S6"] = (104, 258), ["S7"] = (194, 240), ["S8"] = (266, 248),
            ["S9"] = (168, 313), ["S10"] = (254, 307), ["S11"] = (350, 305), ["S12"] = (410, 325),
            ["S13"] = (26, 383), ["S14"] = (84, 385), ["S15"] = (142, 385), ["S16"] = (263, 378),
            ["S17"] = (169, 443), ["S18"] = (238, 435), ["S19"] = (338, 432), ["S20"] = (407, 455),
            ["S21"] = (100, 489), ["S22"] = (186, 498),
            ["S24"] = (333, 569),
            ["S25"] = (95, 612), ["S26"] = (199, 612),
        };

        // Sx label anchor points (label centre); each sits below-left of its switch, clear of the cup and rails.
        private static readonly Dictionary<string, (double x, double y)> LabelPos = new()
        {
            ["S1"] = (147, 190), ["S2"] = (241, 183), ["S3"] = (317, 177), ["S4"] = (390, 185),
            ["S5"] = (21, 267), ["S6"] = (99, 273), ["S7"] = (190, 257), ["S8"] = (256, 258),
            ["S9"] = (150, 325), ["S10"] = (233, 316), ["S11"] = (332, 334), ["S12"] = (392, 332),
            ["S13"] = (19, 399), ["S14"] = (77, 399), ["S15"] = (135, 398), ["S16"] = (253, 391),
            ["S17"] = (152, 455), ["S18"] = (221, 447), ["S19"] = (316, 436), ["S20"] = (387, 460),
            ["S21"] = (94, 505), ["S22"] = (179, 515), ["S24"] = (312, 574),
            ["S25"] = (88, 624), ["S26"] = (195, 629), ["S23"] = (246, 528),
            ["S0"] = (15, 160),                                                      // coin-up label in the yellow band left of START
        };

        // Score-scale lighting positions on the header art (canvas 440×661).
        private static readonly Dictionary<int, (double x, double y)> TensLightPos = new()
        {
            [10] = (92, 78), [20] = (125, 78), [30] = (157, 78), [40] = (190, 77), [50] = (222, 77),
            [60] = (254, 77), [70] = (286, 77), [80] = (318, 78), [90] = (351, 78),
        };
        private static readonly Dictionary<int, (double x, double y)> HundLightPos = new()
        {
            [100] = (177, 36), [200] = (207, 36), [300] = (237, 36), [400] = (267, 36),
        };

        // ---- Live playfield: the board photo with invisible flash targets over each hole; the coin travels the true hole positions. ----
        private void BuildPlayfield()
        {
            var cv = PlayfieldCanvas;
            cv.Children.Clear();
            _holeDisc.Clear(); _holeText.Clear(); _holeScale.Clear(); _holeRing.Clear(); _holeOffFill.Clear(); _holePos.Clear(); _winnerHoles.Clear(); _rowBand.Clear(); _faultMarks.Clear();

            var bg = new Rectangle { Width = PfW, Height = PfH, RadiusX = 14, RadiusY = 14, Fill = new SolidColorBrush(YellowBg) };
            Canvas.SetLeft(bg, 0); Canvas.SetTop(bg, 0); cv.Children.Add(bg);

            // The board artwork (full backglass: score reels + 10-90 scale + the whole playfield).
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(@"C:\SkillGame\playfield.png");
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit();
                var board = new Image { Source = bmp, Width = PfW, Height = PfH, IsHitTestVisible = false, Stretch = Stretch.Fill };
                Canvas.SetLeft(board, 0); Canvas.SetTop(board, 0); cv.Children.Add(board);
            }
            catch (Exception ex) { Log.Error("board image load failed", ex); }

            BuildCollisionMap();   // build rails+components early so the Sx labels can be placed clear of each switch cup
            LoadDrawnPaths();      // load Dan's hand-drawn exact coin paths (C:\SkillGame\paths\S#.json), if present

            // Score-scale lamps (10-90) + reel lamps (100-400) — invisible until the score lights them.
            _tensLight.Clear(); _hundLight.Clear();
            foreach (var kv in TensLightPos) _tensLight[kv.Key] = AddScoreLight(cv, kv.Value.x, kv.Value.y, 28, 22, 11);   // white pill
            foreach (var kv in HundLightPos) _hundLight[kv.Key] = AddScoreLight(cv, kv.Value.x, kv.Value.y, 26, 48, 7);    // reel column

            // Invisible flash targets over each drawn hole (glow + coin-drop on hit); the art shows the holes.
            foreach (var sw in SwitchMap.All)
                if (sw.Kind == SwitchKind.Score && HolePos.TryGetValue(sw.Id, out var p))
                    AddFlashTarget(cv, sw.Id, p.x, p.y, (sw.Points == 20 || sw.Points == 30) ? RingBlue : RingRed, true);
            var gob = SwitchMap.All.FirstOrDefault(s => s.Kind == SwitchKind.Gobble);
            if (gob != null && HolePos.TryGetValue(gob.Id, out var gp)) AddFlashTarget(cv, gob.Id, gp.x, gp.y, RingRed, true);
            foreach (var glp in GobbleLabelPos) AddLabelChip(cv, gob?.Id ?? "S23", glp.x, glp.y);   // every gobble hole is the same switch — label them all

            // Switch 0 (coin-up) — invisible flash target only (no drawn coin-slot marker).
            var (s0x, s0y) = HolePos["S0"];
            AddFlashTarget(cv, "S0", s0x, s0y, RingRed, true);   // coin/START (flash only, no visible ring)

            // A little cartoon gold coin (image) that rolls the board.
            System.Windows.Media.Brush coinFill;
            try
            {
                var cb = new System.Windows.Media.Imaging.BitmapImage();
                cb.BeginInit(); cb.UriSource = new Uri(@"C:\SkillGame\coin.png");
                cb.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; cb.EndInit();
                coinFill = new System.Windows.Media.ImageBrush { ImageSource = cb, Stretch = Stretch.Uniform };
            }
            catch { coinFill = new SolidColorBrush(Color.FromRgb(0xF5, 0xC5, 0x42)); }
            _coin = new Ellipse
            {
                Width = 22, Height = 22, Fill = coinFill,
                Visibility = Visibility.Collapsed, IsHitTestVisible = false,
            };
            Canvas.SetLeft(_coin, HolePos["S0"].x - 11); Canvas.SetTop(_coin, HolePos["S0"].y - 11);
            cv.Children.Add(_coin);

            BuildFlippers(cv);
            if (DebugPath) DrawDebugPath();
            OnFaultsChanged();   // re-apply any live stuck-switch markers after a rebuild
        }

        // The gobble/drain holes are all wired to one switch (S23), so a stuck S23 gets a ring on every one of them.
        private static readonly (double x, double y)[] GobbleHolePos =
        {
            (50, 494), (313, 508), (249, 549), (379, 584), (300, 602), (54, 616), (148, 624),
        };

        // Surface flagged-stuck switches right on the playfield (outside Bench Mode): a pulsing red ring on the
        // hole; hover it to read what's wrong. A live view of real faults while you play or run the demo.
        private void OnFaultsChanged()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(OnFaultsChanged)); return; }
            // A stuck switch shuts distract off in every mode — kill any demo distract that's running.
            if (Faults.Any && _demoDistractActive) { _demoDistractActive = false; StopDistractShow(); }
            foreach (var id in _faultMarks.Keys.ToList())
                if (!Faults.Contains(id))
                { foreach (var r in _faultMarks[id]) PlayfieldCanvas.Children.Remove(r); _faultMarks.Remove(id); }
            foreach (var id in Faults.Snapshot())
            {
                if (_faultMarks.ContainsKey(id)) continue;
                var pts = FaultPositions(id);
                if (pts.Count == 0) continue;
                var rings = new List<Ellipse>();
                foreach (var p in pts) rings.Add(AddFaultRing(p.x, p.y, FaultTip(id)));
                _faultMarks[id] = rings;
            }
        }

        // Where to mark a stuck switch: every gobble hole for S23, otherwise the switch's own hole.
        private List<(double x, double y)> FaultPositions(string id)
        {
            var gob = SwitchMap.All.FirstOrDefault(s => s.Kind == SwitchKind.Gobble);
            if (gob != null && id == gob.Id) return GobbleHolePos.ToList();
            if (_holePos.TryGetValue(id, out var p)) return new List<(double, double)> { p };
            return new List<(double, double)>();
        }

        private Ellipse AddFaultRing(double x, double y, string tip)
        {
            var ring = new Ellipse
            {
                Width = 34, Height = 34,
                Stroke = new SolidColorBrush(Color.FromRgb(0xE2, 0x49, 0x3C)), StrokeThickness = 3.5,
                Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xE2, 0x49, 0x3C)),   // faint fill so the whole disc is hoverable
                ToolTip = tip,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                { Color = Color.FromRgb(0xE2, 0x49, 0x3C), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.9 },
            };
            Canvas.SetLeft(ring, x - 17); Canvas.SetTop(ring, y - 17);
            Panel.SetZIndex(ring, 800);
            PlayfieldCanvas.Children.Add(ring);
            ring.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(600))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            return ring;
        }

        private static string FaultTip(string id)
        {
            var sw = SwitchMap.All.FirstOrDefault(s => s.Id == id);
            string name = sw != null && !string.IsNullOrEmpty(sw.Label) ? sw.Label : id;
            return $"⚠ {name}\nStuck — reading closed for 6 s+.\nCheck the switch and its wiring (needs a pull-down to ground),\nor turn on Bench Mode to ignore inputs.";
        }

        // The side flipper handles (like the real machine): a bolted mount plate + a lever that hangs down
        // and curves inward. The coin fires from it — you pull the handle down and it snaps back up.
        private readonly Dictionary<int, RotateTransform> _flipperArm = new();
        private void BuildFlippers(Canvas cv)
        {
            _flipperArm.Clear();
            var lever = new SolidColorBrush(Color.FromRgb(0xC2, 0xC9, 0xD2));
            foreach (var kv in FlipperPos)
            {
                int i = kv.Key; var (fx, fy) = kv.Value;
                double dir = (i % 2) == 1 ? -1 : 1;   // point OUT: left handles to the left, right handles to the right

                // bolted mount plate
                var plate = new Ellipse
                {
                    Width = 20, Height = 20, Fill = new SolidColorBrush(Color.FromRgb(0x3C, 0x42, 0x4B)),
                    Stroke = new SolidColorBrush(Color.FromRgb(0x84, 0x8D, 0x99)), StrokeThickness = 2.5,
                };
                Canvas.SetLeft(plate, fx - 10); Canvas.SetTop(plate, fy - 10); cv.Children.Add(plate);
                foreach (var (bx, by) in new[] { (0.0, -6.0), (5.5, 3.0), (-5.5, 3.0) })   // three bolts
                {
                    var bolt = new Ellipse { Width = 3, Height = 3, Fill = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAE)) };
                    Canvas.SetLeft(bolt, fx + bx - 1.5); Canvas.SetTop(bolt, fy + by - 1.5); cv.Children.Add(bolt);
                }

                // straight lever pointing down-and-out toward the side (no knob), overlapping the black edge
                var handle = new Canvas();
                var rot = new RotateTransform();
                handle.RenderTransform = rot;
                var arm = new System.Windows.Shapes.Path
                {
                    Stroke = lever, StrokeThickness = 6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    Data = Geometry.Parse($"M 0,0 L {26 * dir},0"),   // straight out to the side (perpendicular); pulls down when fired
                };
                handle.Children.Add(arm);
                Canvas.SetLeft(handle, fx); Canvas.SetTop(handle, fy);
                Panel.SetZIndex(handle, 50); cv.Children.Add(handle);
                _flipperArm[i] = rot;
            }
        }

        // Snap the flipper nearest the coin's launch point: pull the handle down, then it springs back up (the shot).
        private void FlipNearest(double x, double y)
        {
            int best = -1; double bd = double.MaxValue;
            foreach (var kv in FlipperPos)
            {
                double d = (kv.Value.x - x) * (kv.Value.x - x) + (kv.Value.y - y) * (kv.Value.y - y);
                if (d < bd) { bd = d; best = kv.Key; }
            }
            if (best < 0 || !_flipperArm.TryGetValue(best, out var rot)) return;
            if (DemoActive) System.Threading.Tasks.Task.Run(() => AppState.Audio.PlaySettingsSound("flipper"));   // one snap per flip, demo only
            double dir = (best % 2) == 1 ? -1 : 1;   // pull-down direction (left handles rotate the opposite way from right)
            var a = new DoubleAnimationUsingKeyFrames();
            a.KeyFrames.Add(new EasingDoubleKeyFrame(34 * dir, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150)), new CubicEase { EasingMode = EasingMode.EaseOut }));   // pull down
            a.KeyFrames.Add(new EasingDoubleKeyFrame(-14 * dir, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(240)), new CubicEase { EasingMode = EasingMode.EaseIn }));   // release / snap up past rest
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400)), new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut })); // settle
            rot.BeginAnimation(RotateTransform.AngleProperty, a);
        }

        private const bool DebugPath = false;  // draw the computed coin path on the board for verification
        private const bool WalkMode = false;   // step through the coin shot-by-shot (SPACE advances)
        private const bool PickerMode = false; // per-hole path tester (off); the demo drives the coin along the drawn paths
        private int _walkLevel = 0;
        private ComboBox? _pickLevel, _pickHole;

        private void BuildPicker()
        {
            if (Content is not Grid root) return;
            SetPlayState(true);
            // A compact bar across the TOP: controls on the first row, overlay checkboxes flowing across below.
            var panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, MaxWidth = 540,
                Margin = new Thickness(0), Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x10, 0x10, 0x18)),
            };
            Panel.SetZIndex(panel, 999);
            var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 2), VerticalAlignment = VerticalAlignment.Center };
            row1.Children.Add(new TextBlock { Text = "PATH TESTER", Foreground = System.Windows.Media.Brushes.Gold, FontWeight = FontWeights.Bold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 10, 0) });
            _pickLevel = new ComboBox { Margin = new Thickness(4, 0, 4, 0), Width = 110, VerticalAlignment = VerticalAlignment.Center };
            for (int L = 1; L <= 8; L++) _pickLevel.Items.Add($"Level {L}");
            _pickLevel.Items.Add("Gobbles");   // game-over drain paths G1-G7
            _pickLevel.SelectedIndex = 0;
            _pickLevel.SelectionChanged += (s, e) => { RefreshPickerHoles(); ClearTrail(); PlaceCoinAtFlipper(_pickLevel.SelectedIndex + 1); };
            _pickHole = new ComboBox { Margin = new Thickness(4, 0, 4, 0), Width = 130, VerticalAlignment = VerticalAlignment.Center };
            // changing the hole re-parks the coin at that level AND previews its path right away (not on shoot)
            _pickHole.SelectionChanged += (s, e) => { ClearTrail(); PlaceCoinAtFlipper(_pickLevel.SelectedIndex + 1); if (_pickHole.SelectedItem is string hs) PreviewHole(hs.Split(' ')[0]); };
            var shoot = new Button { Content = "SHOOT", Margin = new Thickness(4, 0, 4, 0), Padding = new Thickness(14, 5, 14, 5), FontWeight = FontWeights.Bold };
            shoot.Click += (s, e) =>
            {
                if (_pickLevel == null || _pickHole == null || _pickHole.SelectedItem is not string sid) return;
                PickerShoot(_pickLevel.SelectedIndex + 1, sid.Split(' ')[0]);
            };
            var reset = new Button { Content = "RESET", Margin = new Thickness(4, 0, 4, 0), Padding = new Thickness(14, 5, 14, 5) };
            reset.Click += (s, e) => { ClearTrail(); PlaceCoinAtFlipper(_pickLevel.SelectedIndex + 1); };   // coin back to the selected level's flipper
            row1.Children.Add(_pickLevel); row1.Children.Add(_pickHole); row1.Children.Add(shoot); row1.Children.Add(reset);
            row1.Children.Add(new TextBlock { Text = "Overlay paths:", Foreground = System.Windows.Media.Brushes.Gold, FontWeight = FontWeights.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 6, 0) });
            var clearAll = new Button { Content = "CLEAR", Margin = new Thickness(4, 0, 4, 0), Padding = new Thickness(10, 5, 10, 5) };
            row1.Children.Add(clearAll);
            panel.Children.Add(row1);

            // Overlay checkboxes flow horizontally across the top, at a readable size.
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Width = 500, Margin = new Thickness(6, 0, 6, 6) };
            foreach (var sw in SwitchMap.All)
            {
                if (sw.Kind != SwitchKind.Score) continue;
                var col = SwitchColor(sw.Id); string sid = sw.Id;
                var cb = new CheckBox { Content = sid, Width = 64, Margin = new Thickness(4, 3, 4, 3), Foreground = new SolidColorBrush(col), FontWeight = FontWeights.Bold, FontSize = 15, VerticalContentAlignment = VerticalAlignment.Center };
                cb.Checked += (s, e) => AddColoredPath(sid, col);
                cb.Unchecked += (s, e) => RemoveColoredPath(sid);
                wrap.Children.Add(cb);
            }
            for (int i = 1; i <= 7; i++)   // gobble/game-over drain paths
            {
                string gid = $"G{i}"; if (!_drawnPaths.ContainsKey(gid)) continue;
                var col = HsvToColor((i * 40) % 360, 0.35, 0.95);   // muted/gray-ish so gobbles read differently
                var cb = new CheckBox { Content = gid, Width = 58, Margin = new Thickness(4, 3, 4, 3), Foreground = new SolidColorBrush(col), FontWeight = FontWeights.Bold, FontSize = 15, VerticalContentAlignment = VerticalAlignment.Center };
                cb.Checked += (s, e) => AddColoredPath(gid, col);
                cb.Unchecked += (s, e) => RemoveColoredPath(gid);
                wrap.Children.Add(cb);
            }
            clearAll.Click += (s, e) => { foreach (var c in wrap.Children) if (c is CheckBox cb) cb.IsChecked = false; };
            panel.Children.Add(wrap);
            root.Children.Add(panel);
            RefreshPickerHoles();
            PlaceCoinAtFlipper(1);   // coin waiting at flipper 1
            if (DiagRender) { var dt = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) }; dt.Tick += (s, e) => { dt.Stop(); DiagRenderPaths(); }; dt.Start(); }
            if (AutoTestFlash)
            {
                var seq = new[] { "S7", "S8", "S1", "S25" }; int qi = 0;
                var at = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                at.Tick += (s, e) => { if (qi >= seq.Length) { at.Stop(); return; } PlaceCoinAtFlipper(LevelOf(seq[qi]) is int lv && lv > 0 ? lv : 1); PickerShoot(LevelOf(seq[qi]) > 0 ? LevelOf(seq[qi]) : 1, seq[qi]); qi++; };
                at.Start();
            }
        }
        private const bool AutoTestFlash = false;   // auto-shoot a few holes to test hole flashing

        private const bool DiagRender = false;   // render each hole's computed path to files on startup
        private void DiagRenderPaths()
        {
            string dir = @"C:\Users\DBULLE~1\AppData\Local\Temp\claude\C--Users-dbullerman\4cadf2ba-ac4d-427e-9570-6dcad6bc2a9d\scratchpad\trace";
            foreach (var sw in SwitchMap.All)
            {
                if (sw.Kind != SwitchKind.Score) continue;
                ClearTrail();
                PlaceCoinAtFlipper(sw.Level);
                BuildCoinPath(_restX, _restY, sw.Id);
                PlayfieldCanvas.UpdateLayout();
                RenderPlayfield($@"{dir}\app_{sw.Id}.png");
            }
            ClearTrail();
            // dump rail bands at each cup x to compare the collision map to the sim
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (var kv in CupPos)
                {
                    int xi = (int)Math.Round(kv.Value.x);
                    sb.Append(kv.Key + " x" + xi + " bands:");
                    int yy = 118;
                    while (yy < _railH && _rail != null)
                    {
                        if (_rail[xi, yy]) { int y0 = yy; while (yy < _railH && _rail[xi, yy]) yy++; sb.Append($" [{y0}-{yy - 1}]"); }
                        else yy++;
                    }
                    sb.AppendLine();
                }
                System.IO.File.WriteAllText($@"{dir}\app_rail_bands.txt", sb.ToString());
                var pb = new System.Text.StringBuilder();
                foreach (var hid in new[] { "S21", "S22", "S25", "S26" })
                {
                    var swx = System.Array.Find(SwitchMap.All.ToArray(), z => z.Id == hid);
                    PlaceCoinAtFlipper(swx.Level);
                    BuildCoinPath(_restX, _restY, hid);
                    pb.AppendLine(hid + " L" + swx.Level + " start(" + _restX + "," + _restY + ") pts=" + _coinPath.Count);
                    for (int i = 0; i < _coinPath.Count; i += 2) pb.Append($"({_coinPath[i].x:0},{_coinPath[i].y:0}) ");
                    pb.AppendLine(); pb.AppendLine();
                }
                System.IO.File.WriteAllText($@"{dir}\app_paths.txt", pb.ToString());
            }
            catch (Exception ex) { try { System.IO.File.WriteAllText($@"{dir}\app_err.txt", ex.ToString()); } catch { } }
        }

        private void ClearTrail()
        {
            if (_trail != null) { PlayfieldCanvas.Children.Remove(_trail); _trail = null; }
        }

        // Park the coin (no animation) at the given level's flipper — the start of that level's shot.
        private void PlaceCoinAtFlipper(int level)
        {
            if (_coin == null) return;
            _ballLive = true;
            (double x, double y) f;
            if (level >= 9 && _pickHole?.SelectedItem is string gg && _drawnPaths.TryGetValue(gg.Split(' ')[0], out var gp) && gp.Count > 0)
                f = (gp[0].Item1, gp[0].Item2);   // gobble path: park at the drain path's start flipper
            else f = FlipperPos.TryGetValue(level, out var fp) ? fp : (x: 34.0, y: 180.0);
            _restX = f.x; _restY = f.y;
            _coinPath = new(); _coinLen = 0;   // stop any in-flight path so the coin holds here
            _coin.BeginAnimation(Canvas.LeftProperty, null); _coin.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetLeft(_coin, _restX - 11); Canvas.SetTop(_coin, _restY - 11);
            _coin.Visibility = Visibility.Visible;
            if (!_phys.IsEnabled) _phys.Start();
        }

        private void RefreshPickerHoles()
        {
            if (_pickHole == null || _pickLevel == null) return;
            _pickHole.Items.Clear();
            if (_pickLevel.SelectedItem as string == "Gobbles")   // list the loaded gobble drain paths
            {
                for (int i = 1; i <= 7; i++) if (_drawnPaths.ContainsKey($"G{i}")) _pickHole.Items.Add($"G{i} (drain)");
            }
            else
            {
                int L = _pickLevel.SelectedIndex + 1;
                foreach (var sw in SwitchMap.All)
                    if (sw.Kind == SwitchKind.Score && sw.Level == L) _pickHole.Items.Add($"{sw.Id} ({sw.Points})");
            }
            if (_pickHole.Items.Count > 0) _pickHole.SelectedIndex = 0;
        }

        private void PickerShoot(int level, string holeId)
        {
            if (_coin == null || _rail == null) return;
            _ballLive = true;
            var f = FlipperPos.TryGetValue(level, out var fp) ? fp : (x: 34.0, y: 180.0);
            _restX = f.x; _restY = f.y;
            _coin.BeginAnimation(Canvas.LeftProperty, null); _coin.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetLeft(_coin, _restX - 11); Canvas.SetTop(_coin, _restY - 11); _coin.Visibility = Visibility.Visible;
            if (!_phys.IsEnabled) _phys.Start();
            FireCoin(holeId);   // builds + animates the path from flipper[level] to the chosen hole
        }

        private void DoNextShot()
        {
            if (!WalkMode || _walkLevel >= 8) return;
            _walkLevel++;
            foreach (var sw in SwitchMap.All)
                if (sw.Kind == SwitchKind.Score && sw.Level == _walkLevel) { FireCoin(sw.Id); break; }
            try { System.IO.File.AppendAllText(@"C:\Users\DBULLE~1\AppData\Local\Temp\claude\C--Users-dbullerman\4cadf2ba-ac4d-427e-9570-6dcad6bc2a9d\scratchpad\walklog.txt", $"shot {_walkLevel}: coin will rest at ({_restX:0},{_restY:0})\n"); } catch { }
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(4200) };   // after the coin fully settles, snapshot
            t.Tick += (a, b) => { t.Stop(); RenderPlayfield($@"C:\Users\DBULLE~1\AppData\Local\Temp\claude\C--Users-dbullerman\4cadf2ba-ac4d-427e-9570-6dcad6bc2a9d\scratchpad\walk{_walkLevel}.png"); };
            t.Start();
        }

        private void RenderPlayfield(string file)
        {
            try
            {
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)PfW, (int)PfH, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(PlayfieldCanvas);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using var fs = System.IO.File.Create(file);
                enc.Save(fs);
            }
            catch (Exception ex) { Log.Error("walk render failed", ex); }
        }

        private void OnWalkKey(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Space) { DoNextShot(); e.Handled = true; }
        }
        private void DrawDebugPath() { }   // (debug overlay disabled)

        // One row's ball-rail: a line through the real hole positions, passing just under each hole where the ball rests.
        private void DrawRowTrack(Canvas cv, List<(double x, double y)> hs)
        {
            const double off = 13;   // rail sits just below the hole centre
            var pl = new Polyline { Stroke = new SolidColorBrush(RailBlk), StrokeThickness = 5, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            pl.Points.Add(new Point(6, hs[0].y + off));
            foreach (var h in hs) pl.Points.Add(new Point(h.x, h.y + off));
            pl.Points.Add(new Point(398, hs[^1].y + off));
            cv.Children.Add(pl);
        }

        // An invisible flash target on the wired gobble drain (the traced art already shows the black hole).
        private void AddInvisibleTarget(Canvas cv, string id, double x, double y)
        {
            const double d = 30;
            var disc = new Ellipse { Width = d, Height = d, Fill = System.Windows.Media.Brushes.Transparent, Stroke = System.Windows.Media.Brushes.Transparent, StrokeThickness = 3 };
            var num = new TextBlock { Text = "", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var scale = new ScaleTransform(1, 1);
            var g = new Grid { Width = d, Height = d, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale, ToolTip = id + " · GOBBLE (drain)" };
            g.Children.Add(disc); g.Children.Add(num);
            Canvas.SetLeft(g, x - d / 2); Canvas.SetTop(g, y - d / 2); cv.Children.Add(g);
            _holeDisc[id] = disc; _holeText[id] = num; _holeScale[id] = scale; _holeRing[id] = RingRed; _holeOffFill[id] = Colors.Transparent; _holePos[id] = (x, y);
        }

        // A transparent flash disc over a drawn hole (glows/rings + pops when hit); the coin drops here.
        private void AddFlashTarget(Canvas cv, string id, double x, double y, Color ring, bool label)
        {
            const double d = 32;
            var disc = new Ellipse { Width = d, Height = d, Fill = System.Windows.Media.Brushes.Transparent, Stroke = System.Windows.Media.Brushes.Transparent, StrokeThickness = 4 };
            var scale = new ScaleTransform(1, 1);
            var g = new Grid { Width = d, Height = d, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale, ToolTip = id };
            g.Children.Add(disc);
            Canvas.SetLeft(g, x - d / 2); Canvas.SetTop(g, y - d / 2); cv.Children.Add(g);
            _holeDisc[id] = disc; _holeScale[id] = scale; _holeRing[id] = ring; _holeOffFill[id] = Colors.Transparent; _holePos[id] = (x, y);
            if (label) AddSxLabel(cv, id, x, y, d);
        }

        // A score-scale lamp overlay: a blue outline shaped like the indicator, hidden until the score lights it.
        private static readonly Color ScoreLit = Color.FromRgb(0x35, 0xB6, 0xFF);
        private FrameworkElement AddScoreLight(Canvas cv, double cx, double cy, double w, double h, double radius)
        {
            // Shades the WHOLE indicator its shape (translucent blue fill + rim + glow) when lit.
            var r = new Rectangle
            {
                Width = w, Height = h, RadiusX = radius, RadiusY = radius, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
                Fill = new SolidColorBrush(Color.FromArgb(0x82, 0x35, 0xB6, 0xFF)),
                Stroke = new SolidColorBrush(ScoreLit), StrokeThickness = 2,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = ScoreLit, BlurRadius = 15, ShadowDepth = 0, Opacity = 1 },
            };
            Canvas.SetLeft(r, cx - w / 2); Canvas.SetTop(r, cy - h / 2); cv.Children.Add(r);
            return r;
        }

        // Light the score scale (10-90 tens + 100-400 reel) for the current score, like the real backglass.
        public void UpdateScoreLights(int score)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => UpdateScoreLights(score))); return; }
            int tens = score > 0 ? ((score / 10) % 10) * 10 : 0;
            int hund = Math.Min(score / 100, 4) * 100;
            foreach (var kv in _tensLight) kv.Value.Visibility = kv.Key == tens ? Visibility.Visible : Visibility.Collapsed;
            foreach (var kv in _hundLight) kv.Value.Visibility = (kv.Key == hund && hund > 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        // Every gobble/drain hole is the same switch (S23); each one still gets its own label. Traced spots.
        private static readonly (double x, double y)[] GobbleLabelPos =
        {
            (46, 476), (314, 491), (382, 567), (291, 588), (50, 600), (144, 606),
        };

        // A small switch-id label placed at its traced spot (below-left of the switch, clear of the cup).
        private void AddSxLabel(Canvas cv, string id, double x, double y, double d)
        {
            if (LabelPos.TryGetValue(id, out var lp)) AddLabelChip(cv, id, lp.x, lp.y);
            else AddLabelChip(cv, id, x - 12, y + 26);
        }

        private void AddLabelChip(Canvas cv, string text, double cx, double cy)
        {
            var lbl = new TextBlock { Text = text, FontFamily = (FontFamily)FindResource("UiFont"), FontSize = 8, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)) };
            var chip = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(4, 0.5, 4, 1.5),
                IsHitTestVisible = false, Child = lbl, SnapsToDevicePixels = true,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 2.5, ShadowDepth = 0, Opacity = 0.5 },
            };
            chip.Measure(new Size(100, 100));
            double cw = chip.DesiredSize.Width, chh = chip.DesiredSize.Height;
            Canvas.SetLeft(chip, Math.Round(cx - cw / 2)); Canvas.SetTop(chip, Math.Round(cy - chh / 2));
            cv.Children.Add(chip);
        }

        private void AddHole(Canvas cv, SwitchDef sw, double x, double y)
        {
            Color ring = (sw.Points == 20 || sw.Points == 30) ? RingBlue : RingRed;
            var disc = new Ellipse { Width = HoleD, Height = HoleD, Fill = new SolidColorBrush(Cream), Stroke = new SolidColorBrush(ring), StrokeThickness = 3.5 };
            var num = new TextBlock
            {
                Text = sw.Points.ToString(), FontFamily = (FontFamily)FindResource("UiFont"), FontWeight = FontWeights.Bold, FontSize = 16,
                Foreground = new SolidColorBrush(ring), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var scale = new ScaleTransform(1, 1);
            var g = new Grid
            {
                Width = HoleD, Height = HoleD, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale,
                ToolTip = $"{sw.Id} · row {sw.Level} · {sw.Points} pts" + (sw.IsWinner ? " · WINNER" : ""),
            };
            g.Children.Add(disc); g.Children.Add(num);
            Canvas.SetLeft(g, x - HoleD / 2); Canvas.SetTop(g, y - HoleD / 2);
            cv.Children.Add(g);
            AddSxLabel(cv, sw.Id, x, y, HoleD);
            _holeDisc[sw.Id] = disc; _holeText[sw.Id] = num; _holeScale[sw.Id] = scale; _holeRing[sw.Id] = ring;
            _holeOffFill[sw.Id] = Cream; _holePos[sw.Id] = (x, y);
            if (sw.IsWinner) _winnerHoles.Add(sw.Id);
        }

        // Gobble/drain hole — a black "OUT" outlet. Pass the wired Gobble switch to make it flash + drain the coin.
        private void AddGobbleHole(Canvas cv, double x, double y, SwitchDef? sw)
        {
            Color off = Color.FromRgb(0x12, 0x12, 0x0C);
            const double d = 30;
            var disc = new Ellipse { Width = d, Height = d, Fill = new SolidColorBrush(off), Stroke = new SolidColorBrush(Color.FromRgb(0x7A, 0x18, 0x18)), StrokeThickness = 3 };
            var num = new TextBlock { Text = "OUT", FontFamily = (FontFamily)FindResource("UiFont"), FontWeight = FontWeights.Bold, FontSize = 8.5, Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x6A, 0x6A)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var scale = new ScaleTransform(1, 1);
            var g = new Grid { Width = d, Height = d, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale, ToolTip = sw != null ? sw.Id + " · GOBBLE (drain)" : "drain hole" };
            g.Children.Add(disc); g.Children.Add(num);
            Canvas.SetLeft(g, x - d / 2); Canvas.SetTop(g, y - d / 2); cv.Children.Add(g);
            if (sw != null)
            {
                AddSxLabel(cv, sw.Id, x, y, d);
                _holeDisc[sw.Id] = disc; _holeText[sw.Id] = num; _holeScale[sw.Id] = scale; _holeRing[sw.Id] = RingRed; _holeOffFill[sw.Id] = off; _holePos[sw.Id] = (x, y);
            }
        }

        // START / coin entry (level 0). A red indicator that flashes on coin-up; the coin appears here.
        private void AddStart(Canvas cv, double cx, double cy)
        {
            var t = new TextBlock { Text = "START", FontFamily = (FontFamily)FindResource("UiFont"), FontWeight = FontWeights.Bold, FontSize = 13, Foreground = new SolidColorBrush(RingRed) };
            Canvas.SetLeft(t, cx - 14); Canvas.SetTop(t, cy - 36); cv.Children.Add(t);

            var coin = SwitchMap.All.FirstOrDefault(s => s.Kind == SwitchKind.Coin);
            if (coin != null)
            {
                const double d = 28;
                var disc = new Ellipse { Width = d, Height = d, Fill = new SolidColorBrush(Cream), Stroke = new SolidColorBrush(RingRed), StrokeThickness = 3 };
                var num = new TextBlock { Text = "¢", FontFamily = (FontFamily)FindResource("UiFont"), FontWeight = FontWeights.Bold, FontSize = 14, Foreground = new SolidColorBrush(RingRed), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var scale = new ScaleTransform(1, 1);
                var g = new Grid { Width = d, Height = d, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale, ToolTip = coin.Id + " · COIN (start)" };
                g.Children.Add(disc); g.Children.Add(num);
                Canvas.SetLeft(g, cx - d / 2); Canvas.SetTop(g, cy - d / 2); cv.Children.Add(g);
                _holeDisc[coin.Id] = disc; _holeText[coin.Id] = num; _holeScale[coin.Id] = scale; _holeRing[coin.Id] = RingRed; _holeOffFill[coin.Id] = Cream; _holePos[coin.Id] = (cx, cy);
            }
            AddArrow(cv, cx + 24, cy, false);   // arrow leading into row 1
        }

        private void AddArrow(Canvas cv, double x, double y, bool pointLeft)
        {
            var poly = new Polygon
            {
                Fill = new SolidColorBrush(OrangeAr),
                Points = pointLeft
                    ? new PointCollection { new Point(0, 9), new Point(18, 0), new Point(18, 18) }
                    : new PointCollection { new Point(0, 0), new Point(18, 9), new Point(0, 18) },
            };
            Canvas.SetLeft(poly, x); Canvas.SetTop(poly, y - 9); cv.Children.Add(poly);
        }

        private void AddPlaque(Canvas cv, double cx, double cy)
        {
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = "A GAME OF SKILL", FontFamily = (FontFamily)FindResource("UiFont"), FontWeight = FontWeights.Bold, FontSize = 12, Foreground = new SolidColorBrush(RingRed), HorizontalAlignment = HorizontalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = "for amusement only", FontFamily = (FontFamily)FindResource("UiFont"), FontStyle = FontStyles.Italic, FontSize = 8.5, Foreground = new SolidColorBrush(RingRed), HorizontalAlignment = HorizontalAlignment.Center });
            var box = new Border { Width = 156, Height = 40, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Cream), BorderBrush = new SolidColorBrush(RingRed), BorderThickness = new Thickness(1.5), Child = sp };
            Canvas.SetLeft(box, cx - 78); Canvas.SetTop(box, cy - 20); cv.Children.Add(box);
        }

        // Flash a hole — glowing ring + tint + pop over the drawn hole when the ball drops in, then settle back.
        private void HighlightHole(string id)
        {
            if (!_holeDisc.TryGetValue(id, out var disc)) return;
            Color ring = _holeRing[id];
            disc.Stroke = new SolidColorBrush(ring);
            disc.Fill = new SolidColorBrush(Color.FromArgb(0x66, ring.R, ring.G, ring.B));
            disc.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = ring, BlurRadius = 14, ShadowDepth = 0, Opacity = 1 };
            if (_holeText.TryGetValue(id, out var tx)) tx.Foreground = new SolidColorBrush(Cream);
            var scale = _holeScale[id];
            var pop = new DoubleAnimation(1, 1.45, TimeSpan.FromMilliseconds(150)) { AutoReverse = true, EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            t.Tick += (s, e) => { t.Stop(); ResetHole(id); };
            t.Start();
        }

        private void ResetHole(string id)
        {
            if (!_holeDisc.TryGetValue(id, out var disc)) return;
            disc.Stroke = System.Windows.Media.Brushes.Transparent;
            disc.Fill = new SolidColorBrush(_holeOffFill.TryGetValue(id, out var off) ? off : Colors.Transparent);
            disc.Effect = null;
            if (_holeText.TryGetValue(id, out var tx)) tx.Foreground = new SolidColorBrush(_holeRing[id]);
        }

        // Highlight the row (level) the ball is on with a light band; 0 = none (idle / game over).
        private void SetActiveRow(int level)
        {
            foreach (var kv in _rowBand)
                kv.Value.Fill = kv.Key == level ? new SolidColorBrush(Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF)) : System.Windows.Media.Brushes.Transparent;
        }

        // The coin travels the serpentine board — appears at START on coin-up, then slides to each hole it hits.
        private void PlaceCoinAt(string id)
        {
            if (_coin == null || !_holePos.TryGetValue(id, out var p)) return;
            _coin.BeginAnimation(Canvas.LeftProperty, null);
            _coin.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetLeft(_coin, p.x - 10); Canvas.SetTop(_coin, p.y - 10);
            _coin.Visibility = Visibility.Visible;
        }

        private static int LevelOf(string id)
        {
            foreach (var sw in SwitchMap.All) if (sw.Id == id) return sw.Level;
            return 0;
        }

        // The ball rides on the rail, rolls the row flashing the hole it passes, then falls off the end onto the next row's rail; a drain falls in.
        private void RollCoin(string id)
        {
            if (_coin == null || !_holePos.TryGetValue(id, out var p)) return;
            _coin.Visibility = Visibility.Visible;
            double curL = Canvas.GetLeft(_coin), curT = Canvas.GetTop(_coin);
            if (double.IsNaN(curL)) curL = p.x - 10;
            if (double.IsNaN(curT)) curT = p.y - 10;
            int lvl = LevelOf(id);
            var xk = new DoubleAnimationUsingKeyFrames();
            var yk = new DoubleAnimationUsingKeyFrames();
            var grav = new KeySpline(0.4, 0.0, 0.9, 0.45);   // accelerating fall
            var land = new KeySpline(0.2, 0.0, 0.5, 1.0);

            if (lvl <= 0)   // drain: roll to its column, then fall straight in
            {
                var tR = TimeSpan.FromMilliseconds(340); var tE = TimeSpan.FromMilliseconds(720);
                xk.KeyFrames.Add(new LinearDoubleKeyFrame(p.x - 10, KeyTime.FromTimeSpan(tR)));
                xk.KeyFrames.Add(new LinearDoubleKeyFrame(p.x - 10, KeyTime.FromTimeSpan(tE)));
                yk.KeyFrames.Add(new LinearDoubleKeyFrame(curT, KeyTime.FromTimeSpan(tR)));
                yk.KeyFrames.Add(new SplineDoubleKeyFrame(p.y - 10, KeyTime.FromTimeSpan(tE), grav));
                _coin.BeginAnimation(Canvas.LeftProperty, xk);
                _coin.BeginAnimation(Canvas.TopProperty, yk);
                HighlightHole(id);
                return;
            }

            const double leftEdge = 14, rightEdge = 426;
            bool ltr = lvl % 2 == 1;                        // odd rows sweep left→right, even right→left
            double entryX = ltr ? leftEdge : rightEdge, exitX = ltr ? rightEdge : leftEdge;
            // the row's rail slant, taken from its edge holes
            double minX = 1e9, maxX = -1e9, leftY = p.y, rightY = p.y;
            foreach (var sw in SwitchMap.All)
                if (sw.Kind == SwitchKind.Score && sw.Level == lvl && HolePos.TryGetValue(sw.Id, out var q))
                { if (q.x < minX) { minX = q.x; leftY = q.y; } if (q.x > maxX) { maxX = q.x; rightY = q.y; } }
            double exitY = (ltr ? rightY : leftY) - 6 - 10;              // ride ~6px above the rail line
            double nextRailY = LevelRailY(lvl + 1, (ltr ? rightY : leftY) + 64) - 6 - 10;

            var tSweep = TimeSpan.FromMilliseconds(1000);   // roll the length of the rail (gravity down the slant)
            var tEnd = TimeSpan.FromMilliseconds(1280);     // drop off the end onto the next rail

            xk.KeyFrames.Add(new LinearDoubleKeyFrame(exitX - 10, KeyTime.FromTimeSpan(tSweep)));   // sweep to the far end
            xk.KeyFrames.Add(new LinearDoubleKeyFrame(exitX - 10, KeyTime.FromTimeSpan(tEnd)));     // hold while dropping
            yk.KeyFrames.Add(new LinearDoubleKeyFrame(exitY, KeyTime.FromTimeSpan(tSweep)));        // follow the rail slant
            yk.KeyFrames.Add(new SplineDoubleKeyFrame(nextRailY, KeyTime.FromTimeSpan(tEnd), grav)); // fall to the next rail
            _coin.BeginAnimation(Canvas.LeftProperty, xk);
            _coin.BeginAnimation(Canvas.TopProperty, yk);

            // the scored hole lights as the ball rolls over it
            double denom = exitX - entryX;
            double f = Math.Abs(denom) < 1 ? 0 : (p.x - entryX) / denom;
            f = Math.Max(0.05, Math.Min(0.95, f));
            var flashT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(f * 1000) };
            flashT.Tick += (s, e) => { flashT.Stop(); HighlightHole(id); };
            flashT.Start();
        }

        // The coin enters the machine top-left and drops down into START.
        private void CoinEnter()
        {
            if (_coin == null || !_holePos.TryGetValue("S0", out var p)) return;
            _coin.BeginAnimation(Canvas.LeftProperty, null);
            _coin.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetLeft(_coin, p.x - 10); Canvas.SetTop(_coin, -24);
            _coin.Visibility = Visibility.Visible;
            var yk = new DoubleAnimationUsingKeyFrames();
            yk.KeyFrames.Add(new SplineDoubleKeyFrame(p.y - 10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(460)), new KeySpline(0.4, 0.0, 0.9, 0.5)));
            _coin.BeginAnimation(Canvas.TopProperty, yk);
        }

        // Average hole y for a level's rail (fallback if the level doesn't exist).
        private double LevelRailY(int lvl, double fallback)
        {
            double sum = 0; int n = 0;
            foreach (var sw in SwitchMap.All)
                if (sw.Kind == SwitchKind.Score && sw.Level == lvl && HolePos.TryGetValue(sw.Id, out var q)) { sum += q.y; n++; }
            return n > 0 ? sum / n : fallback;
        }

        // Live hole lighting from real hardware presses (coalesced); the demo instead lights holes via lastSwitch in ShowStatus.
        private void OnPlayfieldPins()
        {
            if (_pfQueued) return;
            _pfQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _pfQueued = false;
                foreach (var sw in SwitchMap.All)
                {
                    if (sw.Kind != SwitchKind.Score) continue;
                    bool on = PinActivity.IsActive(sw.Board, sw.Pin);
                    bool prev = _pinPrev.TryGetValue(sw.Id, out var p) && p;
                    _pinPrev[sw.Id] = on;
                    if (on && !prev) HighlightHole(sw.Id);
                }
            }));
        }

        // Show the chase bar while playing; INSERT COIN + last-game recap when idle.
        private void SetPlayState(bool playing)
        {
            if (IdlePanel != null) IdlePanel.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
            if (LastSwitchBox != null) LastSwitchBox.Visibility = playing ? Visibility.Visible : Visibility.Hidden;   // hide behind the INSERT COIN button while idle
            if (ChasePanel != null) ChasePanel.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
            if (!playing) { SetActiveRow(0); StopBall(); }
        }

        // Drop a coin into the slot when a game (or demo) starts, then run onDone (hides the INSERT COIN panel).
        private void AnimateCoinDrop(Action? onDone = null)
        {
            if (SlotCoin == null) { onDone?.Invoke(); return; }
            SlotCoin.BeginAnimation(Canvas.TopProperty, null);
            SlotCoin.BeginAnimation(OpacityProperty, null);
            SlotCoin.Opacity = 1;
            var drop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(600) };
            drop.KeyFrames.Add(new EasingDoubleKeyFrame(-20, KeyTime.FromPercent(0)));
            drop.KeyFrames.Add(new EasingDoubleKeyFrame(16, KeyTime.FromPercent(0.62), new CubicEase { EasingMode = EasingMode.EaseIn }));   // reaches the slot mouth
            drop.KeyFrames.Add(new EasingDoubleKeyFrame(30, KeyTime.FromPercent(1.0), new CubicEase { EasingMode = EasingMode.EaseIn }));    // sinks in
            var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(600) };
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.62)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1.0)));
            fade.Completed += (s, e) => { if (SlotCoin != null) SlotCoin.Opacity = 0; onDone?.Invoke(); };
            SlotCoin.BeginAnimation(Canvas.TopProperty, drop);
            SlotCoin.BeginAnimation(OpacityProperty, fade);
        }
    }
}
