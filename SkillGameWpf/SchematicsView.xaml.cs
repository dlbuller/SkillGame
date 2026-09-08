using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SkillGame;

namespace SkillGameWpf
{
    // An interactive system schematic: one big wiring diagram you can scroll to zoom, drag to pan, jump around by
    // section, and click any block to read its theory of operation (and see your own schematic image if you add one).
    public partial class SchematicsView : UserControl
    {
        private static readonly Color Controller = Color.FromRgb(0x33, 0xC9, 0xFF);
        private static readonly Color Inputs = Color.FromRgb(0x2F, 0xE0, 0x8C);
        private static readonly Color Outputs = Color.FromRgb(0xE7, 0xA9, 0x1D);
        private static readonly Color Lighting = Color.FromRgb(0xB4, 0x5A, 0xE0);
        private static readonly Color Power = Color.FromRgb(0xE2, 0x49, 0x3C);
        private static readonly Color Information = Color.FromRgb(0x9A, 0xA7, 0xBD);   // slate — reference/doc blocks, not part of the wired system

        // Drop your own schematic images here as <key>.png / .jpg to show them on a block's detail page.
        private const string SchemDir = @"C:\SkillGame\Schematics";

        private sealed class Blk
        {
            public string Key = "", Title = "", Sub = "", Theory = "";
            public double X, Y, W, H; public Color Accent;
            public bool HasSchem = true;   // false for blocks that are just a connection (no circuit of their own)
            public Rect R => new Rect(X, Y, W, H);
            public Border? El; public ScaleTransform? Scale; public DropShadowEffect? Glow;   // for the hover pop
        }

        private readonly List<Blk> _blocks = new();
        private readonly Dictionary<string, Blk> _byKey = new();
        private readonly Dictionary<string, Rect> _sections = new();
        private bool _built;

        // Live-status wiring: reflect the real machine on the diagram.
        private readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromMilliseconds(120) };
        private volatile int _stripArgb;                                   // latest LED-strip average colour (packed ARGB)
        private readonly Dictionary<string, long> _hotUntil = new();        // block key -> tick it stays "energised" until
        private readonly Dictionary<string, Ellipse> _boardDot = new();     // G1-G4 presence dots
        private Border? _stuckBadgeBox; private TextBlock? _stuckBadge;      // "N STUCK" on the matrix block
        private Rectangle? _stripSwatch;                                    // live LED-strip colour swatch

        public SchematicsView()
        {
            InitializeComponent();
            _live.Tick += LiveTick;
            Loaded += (s, e) =>
            {
                if (!_built) { BuildBlocks(); BuildSchematic(); BuildChips(); BuildLiveOverlays(); _built = true; }
                Dispatcher.BeginInvoke(new Action(() => ZoomTo(_sections["OVERVIEW"], false)), System.Windows.Threading.DispatcherPriority.Loaded);
                LedEffects.FrameProduced += OnLedFrame;
                _live.Start();
            };
            Unloaded += (s, e) => { LedEffects.FrameProduced -= OnLedFrame; _live.Stop(); };
        }

        private void BuildBlocks()
        {
            void Add(string key, double x, double y, double w, double h, string title, string sub, Color a, string theory)
            {
                var b = new Blk { Key = key, X = x, Y = y, W = w, H = h, Title = title, Sub = sub, Accent = a, Theory = theory };
                _blocks.Add(b); _byKey[key] = b;
            }

            Add("pc", 40, 125, 180, 150, "CONTROL PC", "mini-PC · runs SkillGame", Controller,
                "The mini-PC runs the SkillGame app. It talks to the four FT232H boards over USB (FTDI D2XX): polling every switch input about 20 times a second and driving the lamp, solenoid and LED outputs. All game logic — scoring, levels, tilt, attract/distract — runs here.");
            Add("hub", 250, 150, 120, 100, "USB HUB", "4 × USB 2.0", Controller,
                "A USB hub fans the PC's port out to the four FT232H boards. Each board is programmed with a unique FTDI serial (GPIO1–GPIO4), so the software always binds the right board to the right job regardless of which physical port it lands on.");
            Add("g1", 470, 90, 220, 110, "FT232H · GPIO1", "inputs S0–S11\ncoin + rows 1–3", Controller,
                "FT232H #1 in MPSSE GPIO mode. Reads the coin switch (S0) and playfield rows 1–3 (S1–S11) on pins D0–D7 / C0–C3. Every input is pulled to ground, so an open switch reads LOW and only a real closure reads HIGH.");
            Add("g2", 470, 250, 220, 110, "FT232H · GPIO2", "inputs S12–S23\nrows 3–6 + gobble", Controller,
                "FT232H #2. Reads rows 3–6 and the gobble hole (S12–S23) on the same pulled-down inputs, polled together with the other boards on each scan.");
            Add("g3", 470, 410, 220, 110, "FT232H · GPIO3", "in: S24–S27 (tilt)\nout: reels + locks", Controller,
                "FT232H #3 does double duty. Inputs D4–D7 read the top rows and the tilt switch (S24–S27). Outputs C0–C7 drive the coin-lock / win-lock solenoid lines and the 100–400 reels plus the Winner and Game-Over lamps.");
            Add("g4", 470, 570, 220, 110, "FT232H · GPIO4", "out: 10–90 + tilt\nSPI → LED strip", Controller,
                "FT232H #4 drives the tens lamps (10–90) and the tilt lamp on D4–D7 / C0–C5, and clocks the WS2812b LED strip out its SPI MOSI line at 3 MHz.");
            Add("matrix", 790, 80, 330, 300, "SWITCH MATRIX", "28 playfield switches\neach pulled to GND (open = LOW)\nD0–D7 = 0–7 · C0–C7 = 8–15", Inputs,
                "The 28 playfield switches. Each one closes to +V and is tied to ground through a 10 kΩ pull-down resistor, so a floating or open input reads LOW and only a genuine closure reads HIGH — that's what stops electrical noise from registering as a hit. Each input also carries a 100 nF cap to GND (C1–C28, on the back of the board): with the pull-down it forms a ~1 ms RC filter that debounces contact chatter and noise on the long playfield leads. The software also edge-detects and debounces each pin, and flags one that stays closed too long as 'stuck'.");
            Add("driver", 820, 460, 230, 150, "OUTPUT DRIVERS", "on-board transistors\nPN2222A lamps · TIP120 coils", Outputs,
                "The FT232H logic pins can't switch lamp or coil current directly, so each output drives a small transistor on the board. PN2222A NPN transistors switch the lamps/reels/indicators; two TIP120 Darlingtons (each with a 1N4004 flyback diode) drive the Win-Lock and Coin-Lock solenoids. Fuses protect the coil supply.");
            Add("lamps", 1150, 400, 240, 120, "SCORE LAMPS", "tens 10–90 · reels 100–400\nWinner · Game Over · Tilt", Outputs,
                "The backglass score lamps are off-board #555 wedge LED bulbs (6.3 V, non-polar) in twist-lock sockets — the tens 10–90, the hundreds reels 100–400, and the Winner, Game-Over and Tilt indicators. Each plugs across a J2 pin pair; the board's lamp positions (R30–R62) are 0 Ω links because the #555 self-limits, and running a 6.3 V bulb on the 5 V rail keeps it safely under-driven. The 100 / 200 / 300 / 400 holes each drive TWO bulbs in parallel for double brightness.");
            Add("sol", 1150, 545, 240, 120, "SOLENOIDS", "Coin-Lock · Win-Lock\n(holds the winning coin)", Outputs,
                "Two small 5 V coils (SparkFun 'Solenoid - 5V Small') driven low-side by the TIP120 Darlingtons (Q7/Q8), each with a 1N4004 flyback diode across it and a fuse in its feed. The Win-Lock fires on a win to hold the coin at the bottom as proof you won, and releases on the next coin-up; the Coin-Lock manages the coin entry.\n\nFUSES — three, all 5×20 mm in Keystone 3517 holders:\n• F1 (Win-Lock, J4) and F2 (Coin-Lock, J5): T2A slow-blow each (Littelfuse 0218002.MXP) for the small 5 V solenoids.\n• F3 (T3.5A slow-blow) — an inline fuse in the 5 V supply lead into J3 — guards the whole lamp / LED / coil rail.\n• Slow-blow (T) rides out coil inrush; any 250 V 5×20 mm part clears 5 V fine.");
            Add("strip", 770, 645, 235, 110, "WS2812b STRIP", "150 NeoPixels · SPI MOSI @3 MHz\nattract / distract shows", Lighting,
                "150 addressable WS2812b NeoPixels on GPIO4's SPI MOSI line. The app renders each attract/distract pattern into a pixel buffer and streams the whole frame out at 3 MHz; the on-screen SKILLGAME header mirrors those same frames.");
            Add("board", 1445, 694, 250, 108, "3D BOARD", "the assembled PCB\nclick to see front + back", Information,
                "A 3D render of the assembled WezeBull Games board (KiCad). Click to open it large — use the FRONT / BACK button to flip between the component side and the solder side, and scroll to zoom / drag to pan.");
            Add("full", 1445, 462, 250, 100, "FULL SCHEMATIC", "the whole sheet · scroll to zoom", Information,
                "The complete KiCad schematic on one sheet — all four FT232H boards, the switch matrix, the PN2222A lamp drivers, the TIP120 solenoid drivers, the LED strip feed and the power inputs. Click to open it large; scroll to zoom and drag to pan.");
            Add("bom", 1445, 110, 250, 108, "BILL OF MATERIALS", "every part + qty\nclick to view", Information,
                "The full bill of materials — every component on the board with quantities and what it does, plus the mini-PC, monitor, USB hub and power adapters. Board counts come straight from the KiCad project.");
            Add("fuses", 1445, 230, 250, 100, "FUSES", "F1 · F2 · F3 ratings\n+ part numbers", Information,
                "The board's fuses: which line each protects, the recommended rating, and a real part number sized for your 5 x 20 mm Keystone 3517 holders. Click for the full listing.");
            Add("hookup", 1445, 342, 250, 108, "SYSTEM HOOKUP", "PC · hub · adapters\nmonitor · board", Information,
                "How everything physically connects: the mini-PC drives the QQU 15.6-inch touch monitor over HDMI and the four FT232H boards through a USB hub; the 3.3 V adapter feeds the switch commons (J1) and the 5 V adapter the lamps, LED strip and coils (J3 / LED±). Click for the full hookup drawing.");
            Add("ftdi", 1445, 574, 250, 108, "FT232H BOARD", "Adafruit breakout\nclick for full pinout", Information,
                "The Adafruit FT232H breakout — one for each of the four boards. It runs the FT232H in MPSSE mode over USB-C. D0–D3 are the SPI/JTAG pins and are reserved (GPIO4's D1/MOSI clocks the LED strip); all the general switch/lamp I/O lives on D4–D7 and C0–C7, with a shared GND tying it to the SkillGame board. Click to open the full pinout showing how every pin is wired here.");
            Add("power", 40, 720, 200, 150, "POWER", "3 V + 5 V + LED 5 V\nmultiple adapters in", Power,
                "Power comes in at several points, not one supply: a 3 VDC input (J1), a 5 VDC input (J3) for the boards, lamps and coils, and a dedicated 5 V feed for the WS2812b LED strip (LED+5 / LED−5). All the inputs share a common ground, which also ties the switch pull-downs and the FT232H boards together. Fuses protect the higher-current rails.");

            _byKey["pc"].HasSchem = false;    // just runs the software + a USB port
            _byKey["hub"].HasSchem = false;   // just a USB hub — no circuit of our own

            _sections["OVERVIEW"] = new Rect(0, 0, 1720, 980);
            _sections["CONTROLLER"] = Union("pc", "hub", "g1", "g2", "g3", "g4");
            _sections["INPUTS"] = Union("matrix");
            _sections["OUTPUTS"] = Union("driver", "lamps", "sol");
            _sections["LIGHTING"] = Union("strip");
            _sections["POWER"] = Union("power");
        }

        private Rect Union(params string[] keys)
        {
            Rect r = _byKey[keys[0]].R;
            foreach (var k in keys) r.Union(_byKey[k].R);
            r.Inflate(40, 40);
            return r;
        }

        // ---- diagram ----
        private void BuildSchematic()
        {
            // wires first so the boxes sit on top; every endpoint is a box-edge point, so no dot ever floats.
            // Each wire gets its own `lane` (turn-point offset) so parallel runs never sit on top of each other.
            // Ordered top-to-bottom so no two wires ever cross: higher board -> higher entry point on each target.
            Connect("pc", 'R', .5, "hub", 'L', .5, Controller);
            Connect("hub", 'R', .25, "g1", 'L', .5, Controller, 18);    // reversed lanes: the lowest board (g4) turns
            Connect("hub", 'R', .45, "g2", 'L', .5, Controller, 6);     // FIRST (leftmost lane) and drops straight down,
            Connect("hub", 'R', .62, "g3", 'L', .5, Controller, -6);    // so no wire ever cuts across another's entry stub
            Connect("hub", 'R', .80, "g4", 'L', .5, Controller, -18);

            Connect("g1", 'R', .5, "matrix", 'L', .15, Inputs, -12);    // inputs -> matrix (top region), monotonic
            Connect("g2", 'R', .5, "matrix", 'L', .45, Inputs, 0);
            Connect("g3", 'R', .32, "matrix", 'L', .72, Inputs, 12);

            Connect("g3", 'R', .62, "driver", 'L', .3, Outputs, -8);    // outputs -> driver (below matrix), monotonic
            Connect("g4", 'R', .40, "driver", 'L', .72, Outputs, 8);
            Connect("g4", 'R', .72, "strip", 'L', .5, Lighting, 20);    // g4 also feeds the LED strip (below driver)

            Connect("driver", 'R', .3, "lamps", 'L', .5, Outputs, -8);
            Connect("driver", 'R', .72, "sol", 'L', .5, Outputs, 8);

            // power routed through clear channels so it never crosses a block or another wire
            WirePts(Power, new Point(240, 795), new Point(560, 795), new Point(560, 680));                        // PSU -> boards (g4 bottom)
            WirePts(Power, new Point(240, 862), new Point(1025, 862), new Point(1025, 610), new Point(1000, 610)); // PSU -> driver bottom (5V): riser sits under the driver, clear of the strip and the solenoid wire

            DrawRefPanel(1425, 74, 290, 748);   // the "not wired into the system" reference docs live in their own panel
            foreach (var b in _blocks) DrawBox(b);
            Legend(40, 352);
        }

        // A wire through explicit waypoints (for hand-routing around blocks), with dots on the first and last points.
        private void WirePts(Color c, params Point[] pts)
        {
            Diagram.Children.Add(new Polyline { Stroke = new SolidColorBrush(c), StrokeThickness = 2, Opacity = 0.8, StrokeLineJoin = PenLineJoin.Round, Points = new PointCollection(pts) });
            foreach (var p in new[] { pts[0], pts[^1] })
            {
                var d = new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(c) };
                Canvas.SetLeft(d, p.X - 3.5); Canvas.SetTop(d, p.Y - 3.5); Diagram.Children.Add(d);
            }
            AddFlow(new PointCollection(pts), c, false);   // power flows out from the PSU
        }

        private Point EdgeAt(Rect r, char side, double f) => side switch
        {
            'L' => new Point(r.Left, r.Top + r.Height * f),
            'R' => new Point(r.Right, r.Top + r.Height * f),
            'T' => new Point(r.Left + r.Width * f, r.Top),
            'B' => new Point(r.Left + r.Width * f, r.Bottom),
            _ => new Point(r.Left, r.Top),
        };

        private void Connect(string aKey, char aSide, double aFrac, string bKey, char bSide, double bFrac, Color c, double lane = 0)
        {
            Point p1 = EdgeAt(_byKey[aKey].R, aSide, aFrac);
            Point p2 = EdgeAt(_byKey[bKey].R, bSide, bFrac);
            var pts = new PointCollection { p1 };
            bool aH = aSide is 'L' or 'R', bH = bSide is 'L' or 'R';
            // `lane` shifts the turn point so parallel wires each get their own channel instead of stacking.
            if (aH && bH) { double mx = (p1.X + p2.X) / 2 + lane; pts.Add(new Point(mx, p1.Y)); pts.Add(new Point(mx, p2.Y)); }
            else if (!aH && !bH) { double my = (p1.Y + p2.Y) / 2 + lane; pts.Add(new Point(p1.X, my)); pts.Add(new Point(p2.X, my)); }
            else if (aH) pts.Add(new Point(p2.X, p1.Y));   // horizontal out of A, then down/up into B
            else pts.Add(new Point(p1.X, p2.Y));           // vertical out of A, then across into B
            pts.Add(p2);
            Diagram.Children.Add(new Polyline { Stroke = new SolidColorBrush(c), StrokeThickness = 2, Opacity = 0.8, StrokeLineJoin = PenLineJoin.Round, Points = pts });
            foreach (var p in new[] { p1, p2 })
            {
                var d = new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(c) };
                Canvas.SetLeft(d, p.X - 3.5); Canvas.SetTop(d, p.Y - 3.5); Diagram.Children.Add(d);
            }
            AddFlow(new PointCollection(pts), c, c == Inputs);   // switch data flows back toward the boards
        }

        // Little "current" dots that travel along a wire in the direction of flow, so the diagram feels alive.
        private void AddFlow(PointCollection pts, Color c, bool reverse)
        {
            const double period = 3.2;   // dash(0.01) + gap(3.19) in stroke-widths; shift by one period = seamless loop
            var pl = new Polyline
            {
                Points = pts, Stroke = new SolidColorBrush(Lighten(c, 0.55)), StrokeThickness = 4, Opacity = 0.9,
                StrokeDashCap = PenLineCap.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round, StrokeDashArray = new DoubleCollection { 0.01, period - 0.01 },
                IsHitTestVisible = false,
            };
            Diagram.Children.Add(pl);
            pl.BeginAnimation(Shape.StrokeDashOffsetProperty,
                new DoubleAnimation(0, reverse ? period : -period, TimeSpan.FromMilliseconds(260)) { RepeatBehavior = RepeatBehavior.Forever });
        }

        private static Color Lighten(Color c, double f) => Color.FromRgb(
            (byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));

        private void DrawBox(Blk blk)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = blk.Title, FontFamily = (FontFamily)FindResource("DisplayFont"), FontWeight = FontWeights.Bold, FontSize = 15, Foreground = new SolidColorBrush(blk.Accent) });
            if (!string.IsNullOrEmpty(blk.Sub))
                panel.Children.Add(new TextBlock { Text = blk.Sub, FontSize = 11.5, Foreground = (Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0), LineHeight = 15, LineStackingStrategy = LineStackingStrategy.BlockLineHeight });
            panel.Children.Add(new TextBlock { Text = "▸ details", FontSize = 10.5, Foreground = new SolidColorBrush(blk.Accent), Opacity = 0.7, Margin = new Thickness(0, 6, 0, 0) });
            var glow = new DropShadowEffect { Color = blk.Accent, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.22 };
            var scale = new ScaleTransform(1, 1);
            var b = new Border
            {
                Width = blk.W, Height = blk.H, CornerRadius = new CornerRadius(10), Padding = new Thickness(13, 11, 13, 11),
                Background = new SolidColorBrush(Color.FromArgb(0x16, blk.Accent.R, blk.Accent.G, blk.Accent.B)),
                BorderBrush = new SolidColorBrush(blk.Accent), BorderThickness = new Thickness(1.5), Child = panel,
                Effect = glow, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale,
            };
            blk.El = b; blk.Scale = scale; blk.Glow = glow;
            Canvas.SetLeft(b, blk.X); Canvas.SetTop(b, blk.Y); Diagram.Children.Add(b);
        }

        private void Legend(double x, double y)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "LEGEND", FontFamily = (FontFamily)FindResource("DisplayFont"), FontWeight = FontWeights.Bold, FontSize = 17, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 0, 0, 10) });
            void Row(Color c, string t)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                sp.Children.Add(new Rectangle { Width = 24, Height = 6, RadiusX = 3, RadiusY = 3, Fill = new SolidColorBrush(c), VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = t, FontSize = 15, Foreground = (Brush)FindResource("TextBrush"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                panel.Children.Add(sp);
            }
            Row(Controller, "Controller / USB");
            Row(Inputs, "Switch inputs");
            Row(Outputs, "Lamp / solenoid outputs");
            Row(Lighting, "LED strip");
            Row(Power, "Power");
            Row(Information, "Information (reference)");
            var b = new Border { Padding = new Thickness(18, 15, 22, 17), CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(0x38, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1), Child = panel };
            Canvas.SetLeft(b, x); Canvas.SetTop(b, y); Diagram.Children.Add(b);
        }

        // A bordered container behind the reference/doc blocks, so it reads as "not part of the wired system".
        private void DrawRefPanel(double x, double y, double w, double h)
        {
            var panel = new Border
            {
                Width = w, Height = h, CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(0x22, Information.R, Information.G, Information.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, Information.R, Information.G, Information.B)),
                BorderThickness = new Thickness(1.5),
            };
            Canvas.SetLeft(panel, x); Canvas.SetTop(panel, y); Panel.SetZIndex(panel, -1); Diagram.Children.Add(panel);
            var title = new TextBlock
            {
                Text = "REFERENCE  ·  not wired into the system", FontFamily = (FontFamily)FindResource("DisplayFont"),
                FontWeight = FontWeights.Bold, FontSize = 13, Foreground = new SolidColorBrush(Information),
            };
            Canvas.SetLeft(title, x + 20); Canvas.SetTop(title, y + 16); Diagram.Children.Add(title);
        }

        // ---- live hardware status on the diagram ----
        private void BuildLiveOverlays()
        {
            foreach (var k in new[] { "g1", "g2", "g3", "g4" })
            {
                var r = _byKey[k].R;
                var dot = new Ellipse
                {
                    Width = 13, Height = 13, Fill = new SolidColorBrush(Inputs),
                    Stroke = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0F)), StrokeThickness = 1.5, IsHitTestVisible = false,
                };
                Canvas.SetLeft(dot, r.Right - 18); Canvas.SetTop(dot, r.Top + 8); Panel.SetZIndex(dot, 30);
                Diagram.Children.Add(dot); _boardDot[k] = dot;
            }

            var mr = _byKey["matrix"].R;
            _stuckBadge = new TextBlock { Text = "0 STUCK", Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12 };
            _stuckBadgeBox = new Border
            {
                Background = new SolidColorBrush(Power), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 2, 9, 3),
                Visibility = Visibility.Collapsed, IsHitTestVisible = false, Child = _stuckBadge,
                Effect = new DropShadowEffect { Color = Power, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.8 },
            };
            Canvas.SetLeft(_stuckBadgeBox, mr.Right - 104); Canvas.SetTop(_stuckBadgeBox, mr.Top + 10); Panel.SetZIndex(_stuckBadgeBox, 31);
            Diagram.Children.Add(_stuckBadgeBox);

            var sr = _byKey["strip"].R;
            _stripSwatch = new Rectangle
            {
                Width = 40, Height = 13, RadiusX = 3, RadiusY = 3, Fill = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                Stroke = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0F)), StrokeThickness = 1, IsHitTestVisible = false,
            };
            Canvas.SetLeft(_stripSwatch, sr.Right - 50); Canvas.SetTop(_stripSwatch, sr.Top + 10); Panel.SetZIndex(_stripSwatch, 30);
            Diagram.Children.Add(_stripSwatch);
        }

        // Average the live LED frame into one colour (fires off the LED thread, so just stash a packed int).
        private void OnLedFrame(System.Drawing.Color[] frame)
        {
            if (frame == null || frame.Length == 0) { _stripArgb = 0; return; }
            long r = 0, g = 0, b = 0; int n = frame.Length;
            foreach (var c in frame) { r += c.R; g += c.G; b += c.B; }
            _stripArgb = (255 << 24) | ((int)(r / n) << 16) | ((int)(g / n) << 8) | (int)(b / n);
        }

        private void LiveTick(object? sender, EventArgs e)
        {
            long now = Environment.TickCount64;
            void MarkHot(string key, bool active) { if (active) _hotUntil[key] = now + 280; }
            bool IsHot(string key) => _hotUntil.TryGetValue(key, out var t) && now < t;

            // Inputs + outputs currently energised — PinActivity tracks both switch reads and lamp/solenoid drives.
            bool inAny = false;
            foreach (var sw in SwitchMap.All) if (PinActivity.IsActive(sw.Board, sw.Pin)) { inAny = true; break; }
            bool lampsOn = false, solOn = false;
            foreach (var kv in LampController.Outputs)
            {
                if (!PinActivity.IsActive(kv.Value.board, kv.Value.pin)) continue;
                if (kv.Key == "WinnerLock" || kv.Key == "CoinLock") solOn = true; else lampsOn = true;
            }
            MarkHot("matrix", inAny); MarkHot("lamps", lampsOn); MarkHot("sol", solOn); MarkHot("driver", lampsOn || solOn);

            var pres = AppState.BoardPresence;
            for (int i = 1; i <= 4; i++)
            {
                bool present = pres == null || pres.Length < i || pres[i - 1];
                bool act = false;
                foreach (var sw in SwitchMap.All) if (sw.Board == i && PinActivity.IsActive(sw.Board, sw.Pin)) { act = true; break; }
                if (!act) foreach (var kv in LampController.Outputs) if (kv.Value.board == i && PinActivity.IsActive(i, kv.Value.pin)) { act = true; break; }
                MarkHot("g" + i, act);
                ApplyBoard("g" + i, present, IsHot("g" + i));
            }

            ApplyMatrix(IsHot("matrix"), Faults.Count);
            ApplyOut("driver", IsHot("driver"));
            ApplyOut("lamps", IsHot("lamps"));
            ApplyOut("sol", IsHot("sol"));
            ApplyStrip();
        }

        private void ApplyBoard(string key, bool present, bool hot)
        {
            if (!_byKey.TryGetValue(key, out var b) || b.Glow == null || b.El == null) return;
            b.Glow.Color = present ? Controller : Power;
            b.Glow.BlurRadius = present ? (hot ? 26 : 14) : 20;
            b.Glow.Opacity = present ? (hot ? 0.85 : 0.28) : 0.7;
            b.El.BorderBrush = new SolidColorBrush(present ? Controller : Power);
            b.El.Opacity = present ? 1.0 : 0.5;
            if (_boardDot.TryGetValue(key, out var dot)) dot.Fill = new SolidColorBrush(present ? Inputs : Power);
        }

        private void ApplyMatrix(bool hot, int stuck)
        {
            if (!_byKey.TryGetValue("matrix", out var b) || b.Glow == null || b.El == null) return;
            if (stuck > 0)
            {
                b.Glow.Color = Power; b.Glow.BlurRadius = 26; b.Glow.Opacity = 0.85;
                b.El.BorderBrush = new SolidColorBrush(Power);
                if (_stuckBadgeBox != null) { _stuckBadgeBox.Visibility = Visibility.Visible; _stuckBadge!.Text = stuck + " STUCK"; }
            }
            else
            {
                b.Glow.Color = Inputs; b.Glow.BlurRadius = hot ? 26 : 14; b.Glow.Opacity = hot ? 0.8 : 0.28;
                b.El.BorderBrush = new SolidColorBrush(Inputs);
                if (_stuckBadgeBox != null) _stuckBadgeBox.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyOut(string key, bool hot)
        {
            if (!_byKey.TryGetValue(key, out var b) || b.Glow == null) return;
            b.Glow.Color = Outputs;
            b.Glow.BlurRadius = hot ? 24 : 12;
            b.Glow.Opacity = hot ? 0.85 : 0.18;
        }

        private void ApplyStrip()
        {
            if (!_byKey.TryGetValue("strip", out var b)) return;
            int a = _stripArgb;
            var col = Color.FromRgb((byte)(a >> 16), (byte)(a >> 8), (byte)a);
            bool lit = col.R + col.G + col.B > 24;
            if (b.Glow != null)
            {
                b.Glow.Color = lit ? col : Lighting;
                b.Glow.BlurRadius = lit ? 26 : 12;
                b.Glow.Opacity = lit ? 0.85 : 0.2;
            }
            if (_stripSwatch != null) _stripSwatch.Fill = new SolidColorBrush(lit ? col : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        }

        // ---- section chips ----
        private void BuildChips()
        {
            foreach (var name in new[] { "OVERVIEW", "CONTROLLER", "INPUTS", "OUTPUTS", "LIGHTING", "POWER" })
            {
                string key = name;
                var btn = new Button { Content = name, Style = (Style)FindResource("PillButton"), Height = 38, Margin = new Thickness(0, 0, 8, 8), MinWidth = 96 };
                btn.Click += (s, e) => ZoomTo(_sections[key], true);
                ChipHost.Children.Add(btn);
            }
            // a thin divider, then quick-open buttons for the reference docs (tinted with the Information accent)
            ChipHost.Children.Add(new Border { Width = 1, Height = 26, Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(4, 0, 12, 8), VerticalAlignment = VerticalAlignment.Center });
            var infoBrush = new SolidColorBrush(Information);
            foreach (var (label, key) in new[] { ("BOM", "bom"), ("FUSES", "fuses"), ("HOOKUP", "hookup"), ("SCHEMATIC", "full"), ("FT232H", "ftdi"), ("3D BOARD", "board") })
            {
                string k = key;
                var btn = new Button { Content = label, Style = (Style)FindResource("PillButton"), Height = 38, Margin = new Thickness(0, 0, 8, 8), MinWidth = 72, Foreground = infoBrush, BorderBrush = infoBrush };
                btn.Click += (s, e) => { if (_byKey.TryGetValue(k, out var blk)) ShowDetail(blk); };
                ChipHost.Children.Add(btn);
            }
        }

        // ---- pan / zoom ----
        private void ZoomTo(Rect r, bool animate)
        {
            double vw = Viewport.ActualWidth, vh = Viewport.ActualHeight;
            if (vw < 4 || vh < 4 || r.Width < 1 || r.Height < 1) return;
            double scale = Math.Max(0.2, Math.Min(3.0, Math.Min(vw / r.Width, vh / r.Height) * 0.92));
            ApplyView(scale, vw / 2 - (r.X + r.Width / 2) * scale, vh / 2 - (r.Y + r.Height / 2) * scale, animate);
        }

        private void ApplyView(double scale, double panX, double panY, bool animate)
        {
            if (animate)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
                var d = TimeSpan.FromMilliseconds(420);
                Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, d) { EasingFunction = ease });
                Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, d) { EasingFunction = ease });
                Pan.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(panX, d) { EasingFunction = ease });
                Pan.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(panY, d) { EasingFunction = ease });
            }
            else
            {
                Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, null); Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                Pan.BeginAnimation(TranslateTransform.XProperty, null); Pan.BeginAnimation(TranslateTransform.YProperty, null);
                Zoom.ScaleX = Zoom.ScaleY = scale; Pan.X = panX; Pan.Y = panY;
            }
            ZoomText.Text = $"{scale * 100:0}%";
        }

        private void Viewport_Wheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            double curScale = Zoom.ScaleX, curPanX = Pan.X, curPanY = Pan.Y;
            Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, null); Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            Pan.BeginAnimation(TranslateTransform.XProperty, null); Pan.BeginAnimation(TranslateTransform.YProperty, null);
            Zoom.ScaleX = Zoom.ScaleY = curScale; Pan.X = curPanX; Pan.Y = curPanY;

            var m = e.GetPosition(Viewport);
            double ns = Math.Max(0.2, Math.Min(3.0, curScale * (e.Delta > 0 ? 1.15 : 1 / 1.15)));
            double dpx = (m.X - curPanX) / curScale, dpy = (m.Y - curPanY) / curScale;
            Zoom.ScaleX = Zoom.ScaleY = ns;
            Pan.X = m.X - dpx * ns; Pan.Y = m.Y - dpy * ns;
            ZoomText.Text = $"{ns * 100:0}%";
        }

        private bool _dragging; private Point _dragStart; private double _panStartX, _panStartY;
        private void Viewport_Down(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _dragging = true; _dragStart = e.GetPosition(Viewport); _panStartX = Pan.X; _panStartY = Pan.Y;
            Viewport.CaptureMouse();
        }
        private void Viewport_Up(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _dragging = false; Viewport.ReleaseMouseCapture();
            var up = e.GetPosition(Viewport);
            if ((up - _dragStart).Length >= 6) return;   // that was a pan, not a click
            var dp = new Point((up.X - Pan.X) / Zoom.ScaleX, (up.Y - Pan.Y) / Zoom.ScaleY);   // to diagram space
            foreach (var b in _blocks) if (b.R.Contains(dp)) { ShowDetail(b); break; }
        }
        private void Viewport_Move(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_dragging)
            {
                var md = e.GetPosition(Viewport);
                Pan.BeginAnimation(TranslateTransform.XProperty, null); Pan.BeginAnimation(TranslateTransform.YProperty, null);
                Pan.X = _panStartX + (md.X - _dragStart.X); Pan.Y = _panStartY + (md.Y - _dragStart.Y);
                SetHover(null); return;   // no hover pop while panning
            }
            var m = e.GetPosition(Viewport);
            var dp = new Point((m.X - Pan.X) / Zoom.ScaleX, (m.Y - Pan.Y) / Zoom.ScaleY);   // to diagram space
            Blk? over = null;
            foreach (var b in _blocks) if (b.R.Contains(dp)) { over = b; break; }
            SetHover(over);
            Viewport.Cursor = over != null ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
        }

        private void Viewport_Leave(object sender, System.Windows.Input.MouseEventArgs e) => SetHover(null);

        // Gently pop the block under the cursor (scale up + brighten its glow) so it's clear which one you'll open.
        private Blk? _hover;
        private void SetHover(Blk? b)
        {
            if (ReferenceEquals(_hover, b)) return;
            if (_hover != null) AnimateHover(_hover, false);
            _hover = b;
            if (_hover != null) AnimateHover(_hover, true);
        }
        private static void AnimateHover(Blk b, bool on)
        {
            if (b.El == null || b.Scale == null) return;
            var d = TimeSpan.FromMilliseconds(140);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            double s = on ? 1.05 : 1.0;
            Panel.SetZIndex(b.El, on ? 20 : 0);   // hovered block rides above its neighbours
            // Scale only — the block's glow is driven by live hardware status, so hover mustn't fight it.
            b.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(s, d) { EasingFunction = ease });
            b.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(s, d) { EasingFunction = ease });
        }

        // ---- detail (a big zoom/pan view of the schematic + theory of operation) ----
        private void ShowDetail(Blk b)
        {
            DetailTitle.Text = b.Title;
            DetailTitle.Foreground = new SolidColorBrush(b.Accent);
            DetailAccent.Background = new SolidColorBrush(b.Accent);
            DetailTheory.Text = b.Theory;
            FillPinout(b);

            // No circuit of its own (PC / USB hub): drop the picture pane entirely and let the theory fill the width.
            if (!b.HasSchem)
            {
                ImgBox.Visibility = Visibility.Collapsed;
                Grid.SetColumn(TheoryPanel, 0); Grid.SetColumnSpan(TheoryPanel, 2);
                TheoryPanel.Width = double.NaN; TheoryPanel.BorderThickness = new Thickness(0);
                DetailOverlay.Visibility = Visibility.Visible;
                DetailOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
                return;
            }

            ImgBox.Visibility = Visibility.Visible;
            Grid.SetColumn(TheoryPanel, 1); Grid.SetColumnSpan(TheoryPanel, 1);
            TheoryPanel.Width = 330; TheoryPanel.BorderThickness = new Thickness(1, 0, 0, 0);

            // Power: a stacked deck of the separate supply rails instead of one schematic crop.
            bool isPower = b.Key == "power";
            PowerDeck.Visibility = isPower ? Visibility.Visible : Visibility.Collapsed;
            PowerHint.Visibility = isPower ? Visibility.Visible : Visibility.Collapsed;
            if (isPower)
            {
                BoardSideBtn.Visibility = Visibility.Collapsed;
                ImgSchem.Visibility = Visibility.Collapsed; ImgPlaceholder.Visibility = Visibility.Collapsed; DetailZoomText.Visibility = Visibility.Collapsed;
                BuildPowerDeck();
                DetailOverlay.Visibility = Visibility.Visible;
                DetailOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
                return;
            }

            // The 3D board block flips between front/back renders; everything else shows its one schematic crop.
            bool isBoard = b.Key == "board";
            BoardSideBtn.Visibility = isBoard ? Visibility.Visible : Visibility.Collapsed;
            _boardSide = "front"; if (isBoard) BoardSideBtn.Content = "BACK ▸";
            string? found = isBoard ? BoardImg("front") : FindImg(b.Key);
            if (found != null)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(found); bmp.EndInit();
                    ImgSchem.Source = bmp; ImgSchem.Width = bmp.PixelWidth; ImgSchem.Height = bmp.PixelHeight;
                    ImgSchem.Visibility = Visibility.Visible; ImgPlaceholder.Visibility = Visibility.Collapsed; DetailZoomText.Visibility = Visibility.Visible;
                }
                catch { found = null; }
            }
            if (found == null)
            {
                ImgSchem.Source = null; ImgSchem.Visibility = Visibility.Collapsed;
                ImgPlaceholder.Visibility = Visibility.Visible; DetailZoomText.Visibility = Visibility.Collapsed;
                ImgPlaceholderText.Text = $"Add your schematic here:\n{System.IO.Path.Combine(SchemDir, b.Key + ".png")}";
            }
            DetailOverlay.Visibility = Visibility.Visible;
            DetailOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            if (found != null) Dispatcher.BeginInvoke(new Action(FitDetail), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private string _boardSide = "front";
        private static string? BoardImg(string side)
        {
            var p = System.IO.Path.Combine(SchemDir, "board_" + side + ".png");
            return File.Exists(p) ? p : null;
        }

        private void BoardSide_Click(object sender, RoutedEventArgs e)
        {
            _boardSide = _boardSide == "front" ? "back" : "front";
            BoardSideBtn.Content = _boardSide == "front" ? "BACK ▸" : "◂ FRONT";
            var p = BoardImg(_boardSide);
            if (p == null) return;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(p); bmp.EndInit();
                ImgSchem.Source = bmp; ImgSchem.Width = bmp.PixelWidth; ImgSchem.Height = bmp.PixelHeight;
                Dispatcher.BeginInvoke(new Action(FitDetail), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch { }
        }

        // ---- Power: a stacked deck of the separate supply rails; click a card to bring the next forward ----
        private readonly List<Border> _powerCards = new();
        private int _powerTop;

        private static readonly (string volt, string conn, string accent, string title, string body, string pic)[] PowerRails =
        {
            ("3 VDC", "input · J1", "CyanBrush", "LOGIC SUPPLY",
             "Feeds the switch-common rail. Every playfield switch closes to this 3.3 V, and a 10 kΩ pull-down holds each FT232H input LOW until a real closure. Keep it at 3.3 V — the FT232H inputs are not 5 V-tolerant.", "pwr_3v.png"),
            ("5 VDC", "input · J3 (fused)", "GoldBrush", "MAIN RAIL",
             "The workhorse rail: the score lamps (off-board #555 wedge LED bulbs, self-limiting — board R = 0 Ω links), the two solenoid coils (via fuses F1/F2 and the TIP120 drivers), and the board logic. Fuses protect the coil feed.", "pwr_5v.png"),
            ("5 V", "input · LED+5 / LED−5", "TealBrush", "LED STRIP",
             "A dedicated 5 V feed for the 150-pixel WS2812b strip, so its heavy current draw can't sag the lamp and logic rail. Ties into the same common ground as everything else.", "pwr_led.png"),
        };

        private void BuildPowerDeck()
        {
            if (_powerCards.Count == 0)
            {
                foreach (var r in PowerRails)
                {
                    var accent = (Brush)FindResource(r.accent); var col = ((SolidColorBrush)FindResource(r.accent)).Color;
                    var sp = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
                    // the adapter picture leads the card (it already shows the voltage + connector)
                    var picPath = System.IO.Path.Combine(SchemDir, r.pic);
                    if (File.Exists(picPath))
                    {
                        try
                        {
                            var pb = new BitmapImage(); pb.BeginInit(); pb.CacheOption = BitmapCacheOption.OnLoad; pb.UriSource = new Uri(picPath); pb.EndInit();
                            sp.Children.Add(new Image { Source = pb, Stretch = Stretch.Uniform, MaxHeight = 120, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) });
                        }
                        catch { }
                    }
                    sp.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(0x33, col.R, col.G, col.B)), Margin = new Thickness(0, 0, 0, 12) });
                    sp.Children.Add(new TextBlock { Text = r.title, FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 16, Foreground = accent, Margin = new Thickness(0, 0, 0, 8) });
                    sp.Children.Add(new TextBlock { Text = r.body, FontSize = 15, LineHeight = 22, Foreground = (Brush)FindResource("TextBrush"), TextWrapping = TextWrapping.Wrap });

                    var card = new Border
                    {
                        Width = 400, MinHeight = 340, Background = new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x1C)),
                        BorderBrush = accent, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(16),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        RenderTransformOrigin = new Point(0.5, 0.5), Child = sp,
                        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 28, ShadowDepth = 0, Opacity = 0.55 },
                    };
                    _powerCards.Add(card); PowerDeck.Children.Add(card);
                }
            }
            _powerTop = 0;
            LayoutPowerDeck(false);
        }

        // Position each card by its depth behind the front one (offset + slight rotation for the deck look).
        private void LayoutPowerDeck(bool animate)
        {
            int n = _powerCards.Count;
            for (int i = 0; i < n; i++)
            {
                int depth = (i - _powerTop + n) % n;               // 0 = front
                var card = _powerCards[i];
                Panel.SetZIndex(card, n - depth);
                card.Opacity = depth == 0 ? 1.0 : 0.85;
                double dx = depth * 18, dy = depth * 16, rot = depth * 4;
                var tg = new TransformGroup();
                tg.Children.Add(new RotateTransform(rot));
                tg.Children.Add(new TranslateTransform(dx, dy));
                card.RenderTransform = tg;
            }
        }

        private void PowerDeck_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_powerCards.Count == 0) return;
            _powerTop = (_powerTop + 1) % _powerCards.Count;
            LayoutPowerDeck(true);
        }

        private string? FindImg(string key)
        {
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            { var p = System.IO.Path.Combine(SchemDir, key + ext); if (File.Exists(p)) return p; }
            return null;
        }

        // Real pin map for the opened block (drawn straight from the code), shown under the theory of operation.
        private void FillPinout(Blk b)
        {
            PinoutRows.Children.Clear();
            var rows = new List<(string pin, string sig)>();
            string title = "PINOUT";
            switch (b.Key)
            {
                case "g1": case "g2": case "g3": case "g4":
                    int bd = b.Key[1] - '0';
                    title = $"FT232H · GPIO{bd} PINS";
                    foreach (var sw in SwitchMap.All) if (sw.Board == bd) rows.Add((PinName(sw.Pin), sw.Label));
                    foreach (var kv in LampController.Outputs) if (kv.Value.board == bd) rows.Add((PinName(kv.Value.pin), OutName(kv.Key)));
                    if (bd == 4) rows.Add(("SPI MOSI", "WS2812b strip data → 150 px"));
                    break;
                case "matrix":
                    title = "SWITCHES";
                    foreach (var sw in SwitchMap.All) rows.Add(($"G{sw.Board}·{PinName(sw.Pin)}", sw.Label));
                    break;
                case "lamps":
                    title = "LAMP OUTPUTS";
                    foreach (var kv in LampController.Outputs) if (kv.Key != "WinnerLock" && kv.Key != "CoinLock") rows.Add(($"G{kv.Value.board}·{PinName(kv.Value.pin)}", OutName(kv.Key)));
                    break;
                case "sol":
                    title = "SOLENOID OUTPUTS";
                    foreach (var kv in LampController.Outputs) if (kv.Key == "WinnerLock" || kv.Key == "CoinLock") rows.Add(($"G{kv.Value.board}·{PinName(kv.Value.pin)}", OutName(kv.Key)));
                    break;
                case "strip":
                    title = "LED STRIP";
                    rows.Add(("G4 SPI MOSI", "serial data @ 3 MHz"));
                    rows.Add(("150 px", "WS2812b NeoPixels"));
                    break;
            }
            if (rows.Count == 0) { PinoutPanel.Visibility = Visibility.Collapsed; return; }
            PinoutTitle.Text = title;
            foreach (var (pin, sig) in rows) PinoutRows.Children.Add(PinRow(pin, sig, b.Accent));
            PinoutPanel.Visibility = Visibility.Visible;
        }

        private static string PinName(int pin) => pin < 8 ? "D" + pin : "C" + (pin - 8);

        private static string OutName(string key) => key switch
        {
            "GameOver" => "Game Over lamp",
            "Winner" => "Winner lamp",
            "Tilt" => "Tilt lamp",
            "WinnerLock" => "Win-Lock solenoid",
            "CoinLock" => "Coin-Lock solenoid",
            _ => key.Length <= 2 ? key + " pts lamp" : key + " reel",
        };

        private FrameworkElement PinRow(string pin, string sig, Color accent)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var chip = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x2A, accent.R, accent.G, accent.B)),
                BorderBrush = new SolidColorBrush(accent), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(0, 1, 0, 1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = pin, Foreground = new SolidColorBrush(accent), FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 11.5, Margin = new Thickness(7, 0, 7, 0) },
            };
            Grid.SetColumn(chip, 0); g.Children.Add(chip);
            var t = new TextBlock { Text = sig, Foreground = (Brush)FindResource("TextBrush"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(t, 1); g.Children.Add(t);
            return g;
        }

        private void FitDetail()
        {
            if (ImgSchem.Source == null) return;
            double vw = DetailViewport.ActualWidth, vh = DetailViewport.ActualHeight, iw = ImgSchem.Width, ih = ImgSchem.Height;
            if (vw < 4 || vh < 4 || iw < 1 || ih < 1) return;
            double scale = Math.Min(vw / iw, vh / ih) * 0.98;
            SetDetailView(scale, (vw - iw * scale) / 2, (vh - ih * scale) / 2);
        }

        private void SetDetailView(double scale, double px, double py)
        {
            DZoom.ScaleX = DZoom.ScaleY = scale; DPan.X = px; DPan.Y = py;
            DetailZoomText.Text = $"{scale * 100:0}%";
        }

        private void DImg_Wheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (ImgSchem.Source == null) return;
            var m = e.GetPosition(DetailViewport);
            double cur = DZoom.ScaleX, ns = Math.Max(0.1, Math.Min(6.0, cur * (e.Delta > 0 ? 1.15 : 1 / 1.15)));
            double dpx = (m.X - DPan.X) / cur, dpy = (m.Y - DPan.Y) / cur;
            SetDetailView(ns, m.X - dpx * ns, m.Y - dpy * ns);
        }

        private bool _dDrag; private Point _dStart; private double _dPanX, _dPanY;
        private void DImg_Down(object sender, System.Windows.Input.MouseButtonEventArgs e)
        { if (ImgSchem.Source == null) return; _dDrag = true; _dStart = e.GetPosition(DetailViewport); _dPanX = DPan.X; _dPanY = DPan.Y; DetailViewport.CaptureMouse(); }
        private void DImg_Up(object sender, System.Windows.Input.MouseButtonEventArgs e) { _dDrag = false; DetailViewport.ReleaseMouseCapture(); }
        private void DImg_Move(object sender, System.Windows.Input.MouseEventArgs e)
        { if (!_dDrag) return; var m = e.GetPosition(DetailViewport); DPan.X = _dPanX + (m.X - _dStart.X); DPan.Y = _dPanY + (m.Y - _dStart.Y); }

        private void ZoomDetailCenter(double factor)
        {
            if (ImgSchem.Source == null) return;
            double vw = DetailViewport.ActualWidth, vh = DetailViewport.ActualHeight, cur = DZoom.ScaleX;
            double ns = Math.Max(0.1, Math.Min(6.0, cur * factor));
            double cx = vw / 2, cy = vh / 2, dpx = (cx - DPan.X) / cur, dpy = (cy - DPan.Y) / cur;
            SetDetailView(ns, cx - dpx * ns, cy - dpy * ns);
        }
        private void DetailZoomIn(object sender, RoutedEventArgs e) => ZoomDetailCenter(1.25);
        private void DetailZoomOut(object sender, RoutedEventArgs e) => ZoomDetailCenter(1 / 1.25);
        private void DetailFit(object sender, RoutedEventArgs e) => FitDetail();

        private void CloseDetail(object sender, RoutedEventArgs e) => DetailOverlay.Visibility = Visibility.Collapsed;
        private void DetailScrim_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => DetailOverlay.Visibility = Visibility.Collapsed;
    }
}
