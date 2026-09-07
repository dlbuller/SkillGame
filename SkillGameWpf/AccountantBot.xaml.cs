using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SkillGameWpf
{
    // The auditor gremlin: sits at his desk logging plays — games, hits, wins, scores — nodding and blinking as the tally climbs. He audits the game, never money.
    public partial class AccountantBot : UserControl
    {
        private readonly DispatcherTimer _beat = new() { Interval = TimeSpan.FromMilliseconds(700) };
        private readonly DispatcherTimer _tally = new() { Interval = TimeSpan.FromMilliseconds(1600) };
        private readonly Random _r = new();
        private long _running = 1240;
        private int _tick;

        // Lines he grumbles when you click him — the system auditor, tallying plays (never money).
        private static readonly string[] Sayings = { "Crunchin the numbers", "Myeah", "Countin' the hits", "Loggin' the wins", "Games all tallied", "Well, back to it then", "Success" };
        private readonly DispatcherTimer _bubble = new() { Interval = TimeSpan.FromMilliseconds(2200) };
        private int _sayIdx = -1;
        private int _bubbleGen;

        public AccountantBot()
        {
            InitializeComponent();
            _beat.Tick += Beat;
            _tally.Tick += (s, e) => FloatNumber();
            _bubble.Tick += (s, e) => HideBubble();
            Loaded += (s, e) => { StartScribble(); _beat.Start(); _tally.Start(); };
            Unloaded += (s, e) => { _beat.Stop(); _tally.Stop(); _bubble.Stop(); };
        }

        // Click him: pop a random line in the speech bubble (no immediate repeat) and give a quick nod + blink.
        private void Bot_Click(object sender, MouseButtonEventArgs e)
        {
            int i; do { i = _r.Next(Sayings.Length); } while (Sayings.Length > 1 && i == _sayIdx);
            _sayIdx = i;
            BubbleText.Text = Sayings[i] + "!";
            Bubble.Visibility = Visibility.Visible;
            Bubble.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
            Nod(); Blink();
            _bubbleGen++;
            _bubble.Stop(); _bubble.Start();
        }

        private void HideBubble()
        {
            _bubble.Stop();
            int gen = _bubbleGen;
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260));
            fade.Completed += (s, e) => { if (gen == _bubbleGen) Bubble.Visibility = Visibility.Collapsed; };
            Bubble.BeginAnimation(OpacityProperty, fade);
        }

        // A steady scribbling wobble on the pen hand.
        private void StartScribble()
        {
            var scribble = new DoubleAnimation(-7, 9, TimeSpan.FromMilliseconds(170)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            PenRot.BeginAnimation(RotateTransform.AngleProperty, scribble);
        }

        private void Beat(object? sender, EventArgs e)
        {
            _tick++;
            // running tally of plays/points keeps climbing (stats, not money)
            _running += 10 + _r.Next(90);
            CalcDisplay.Text = _running.ToString("N0");
            if (_tick % 3 == 0) Blink();
            if (_tick % 4 == 0) Nod();
        }

        private void Blink()
        {
            var a = new DoubleAnimation(1, 0.1, TimeSpan.FromMilliseconds(90)) { AutoReverse = true };
            EyeLScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            EyeRScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        // Glance down at the ledger, then back up.
        private void Nod()
        {
            var a = new DoubleAnimation(0, 7, TimeSpan.FromMilliseconds(320)) { AutoReverse = true, EasingFunction = new SineEase() };
            HeadRot.BeginAnimation(RotateTransform.AngleProperty, a);
        }

        // A game-stat figure floats up off the desk and fades — he audits play, never money.
        private void FloatNumber()
        {
            string txt = _r.Next(6) switch
            {
                0 => "+" + (_r.Next(1, 9) * 10) + " pts",
                1 => "HIT",
                2 => "WIN!",
                3 => (_r.Next(10, 49) * 10).ToString("N0"),   // a score
                4 => _r.Next(1, 40) + " games",
                _ => "TILT",
            };
            var tb = new TextBlock
            {
                Text = txt, FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 14, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(_r.Next(2) == 0 ? Color.FromRgb(0x5C, 0xE0, 0x8C) : Color.FromRgb(0xE7, 0xA9, 0x1D)),
                Opacity = 0,
            };
            double x = 60 + _r.Next(110);
            Canvas.SetLeft(tb, x); Canvas.SetTop(tb, 96);
            NumberHost.Children.Add(tb);
            tb.BeginAnimation(OpacityProperty, MakeFade());
            var rise = new DoubleAnimation(96, 40 + _r.Next(24), TimeSpan.FromMilliseconds(1500)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            rise.Completed += (s, e) => NumberHost.Children.Remove(tb);
            tb.BeginAnimation(Canvas.TopProperty, rise);
        }

        private static DoubleAnimationUsingKeyFrames MakeFade()
        {
            var f = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1500) };
            f.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            f.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.2)));
            f.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.6)));
            f.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1.0)));
            return f;
        }
    }
}
