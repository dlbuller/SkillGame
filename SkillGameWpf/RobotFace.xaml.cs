using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace SkillGameWpf
{
    // A cute animated robot that blinks, glances around, and reacts to the game (idle / happy / win / tilt).
    public partial class RobotFace : UserControl
    {
        private readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(2400) };
        private readonly Random _r = new();

        public RobotFace()
        {
            InitializeComponent();
            SetMood("happy");   // the robot is cheerful by default, all the time
            _t.Tick += (s, e) => Idle();
            Loaded += (s, e) => _t.Start();
            Unloaded += (s, e) => _t.Stop();
        }

        private void Idle()
        {
            if (_mood == "sick") return;   // hold the sick face steady until the boards are back
            Blink();
            if (_r.Next(2) == 0) Look((_r.Next(3) - 1) * 7.0);   // glance left / centre / right
            if (_mood == "happy" && _r.Next(3) == 0)   // occasional happy mouth life
                FlashMouth(_r.Next(2) == 0 ? "M 64,129 Q 90,148 116,129" : "M 72,133 Q 90,127 108,133");
        }

        private void Blink()
        {
            var a = new DoubleAnimation(1, 0.12, TimeSpan.FromMilliseconds(90)) { AutoReverse = true };
            LeftEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            RightEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        private void Look(double dx)
        {
            LookT.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(dx, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        private string _mood = "happy";

        // Change expression: "happy" (default, always) | "excited" | "win" | "mad" | "sick"
        public void SetMood(string mood)
        {
            _mood = mood;
            bool sick = mood == "sick";   // boards missing: red X-eyes, queasy, until they're found

            SickX.Visibility = sick ? Visibility.Visible : Visibility.Collapsed;
            LeftEye.Opacity = sick ? 0 : 1;   // hide the round eyes behind the X's
            RightEye.Opacity = sick ? 0 : 1;
            HeadBorder.BorderBrush = new SolidColorBrush(sick ? Color.FromRgb(0xE2, 0x49, 0x3C) : Color.FromRgb(0x31, 0x39, 0x4A));
            ((DropShadowEffect)AntGlow).Color = sick ? Color.FromRgb(0xE2, 0x49, 0x3C) : Color.FromRgb(0xE7, 0xA9, 0x1D);

            Color eye = mood switch
            {
                "win" => Color.FromRgb(0x2F, 0xE0, 0x8C),
                "mad" => Color.FromRgb(0xE2, 0x49, 0x3C),
                "excited" => Color.FromRgb(0x8A, 0xE6, 0xFF),
                _ => Color.FromRgb(0x33, 0xC9, 0xFF),     // happy
            };
            LeftEye.Background = new SolidColorBrush(eye);
            RightEye.Background = new SolidColorBrush(eye);
            ((DropShadowEffect)LeftEye.Effect).Color = eye;
            ((DropShadowEffect)RightEye.Effect).Color = eye;

            Mouth.Stroke = new SolidColorBrush(sick ? Color.FromRgb(0xE2, 0x49, 0x3C) : Color.FromRgb(0x8A, 0xA0, 0xB4));
            Mouth.Data = Geometry.Parse(mood switch
            {
                "win" => "M 58,126 Q 90,152 122,126",     // huge grin
                "excited" => "M 62,127 Q 90,150 118,127", // big open grin
                "mad" => "M 62,140 Q 90,122 118,140",     // angry frown
                "sick" => "M 62,134 Q 71,127 80,134 Q 89,141 98,134 Q 107,127 116,134", // queasy squiggle
                _ => "M 66,130 Q 90,143 114,130",         // happy smile (default)
            });
        }

        // Snap the mouth to a shape briefly, then revert to the current mood (adds mouth life).
        private void FlashMouth(string geo, int ms = 650)
        {
            Mouth.Data = Geometry.Parse(geo);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (s, e) => { t.Stop(); SetMood(_mood); };
            t.Start();
        }

        // React to a score by getting excited and wiggling; a big hit gets a wilder, longer reaction.
        public void React(bool big = false)
        {
            if (_mood == "sick") return;   // don't perk up while the hardware is down
            SetMood("excited");
            Celebrate(big);
            var pop = new DoubleAnimation(1, big ? 1.42 : 1.24, TimeSpan.FromMilliseconds(150)) { AutoReverse = true, EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } };
            LeftEyeScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            RightEyeScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            AntGlow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(1.0, 0.4, TimeSpan.FromMilliseconds(600)));
            var back = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(big ? 1300 : 700) };
            back.Tick += (s, e) => { back.Stop(); if (_mood == "excited") SetMood("happy"); };
            back.Start();
        }

        // A happy wiggle — bigger and more repeats for a big hit / a winner.
        public void Celebrate(bool big = false)
        {
            double amp = big ? 15 : 8;
            var wig = new DoubleAnimationUsingKeyFrames { RepeatBehavior = new RepeatBehavior(big ? 4 : 2) };
            wig.KeyFrames.Add(new LinearDoubleKeyFrame(-amp, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
            wig.KeyFrames.Add(new LinearDoubleKeyFrame(amp, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(270))));
            wig.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360))));
            RootRot.BeginAnimation(RotateTransform.AngleProperty, wig);
        }

        // Tilt: a startled shake.
        public void Shake()
        {
            var sh = new DoubleAnimationUsingKeyFrames { RepeatBehavior = new RepeatBehavior(4) };
            sh.KeyFrames.Add(new LinearDoubleKeyFrame(-7, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(50))));
            sh.KeyFrames.Add(new LinearDoubleKeyFrame(7, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
            sh.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
            RootTrans.BeginAnimation(TranslateTransform.XProperty, sh);
        }
    }
}
