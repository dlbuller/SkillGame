using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SkillGameWpf
{
    // A cute gremlin mechanic that roams the diagnostics screen, climbs, tightens bolts, and has moods.
    public partial class MechanicBot : UserControl
    {
        private readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromMilliseconds(2200) };
        private readonly Random _r = new();
        private string _mood = "happy";

        public MechanicBot()
        {
            InitializeComponent();
            SetMood("happy");
            _idle.Tick += (s, e) => IdleTick();
            Loaded += (s, e) => _idle.Start();
            Unloaded += (s, e) => _idle.Stop();
        }

        private void IdleTick()
        {
            if (_mood == "dizzy") return;
            Blink();
            if (_r.Next(2) == 0) Look((_r.Next(3) - 1) * 2.0);
        }

        private void Blink()
        {
            var a = new DoubleAnimation(_mood == "focused" ? 0.55 : 1, 0.1, TimeSpan.FromMilliseconds(90)) { AutoReverse = true };
            EyeLScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            EyeRScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        private void Look(double dx) =>
            EyesLook.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(dx, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase() });

        // happy | focused | surprised | worried | dizzy | mad
        public void SetMood(string mood)
        {
            _mood = mood;
            bool teeth = mood == "happy" || mood == "mad";   // grin, or gritted-when-mad
            ToothL.Visibility = ToothR.Visibility = teeth ? Visibility.Visible : Visibility.Collapsed;
            Brows.Visibility = mood == "mad" ? Visibility.Visible : Visibility.Collapsed;
            bool dizzy = mood == "dizzy";
            Dizzy.Visibility = dizzy ? Visibility.Visible : Visibility.Collapsed;
            Dizzy.Opacity = dizzy ? 1 : 0;   // the stars/birds start hidden (Opacity 0), so light them up here
            Mouth.Data = Geometry.Parse(mood switch
            {
                "focused"   => "M 27,32 L 37,32",
                "surprised" => "M 28,31 Q 32,26 36,31 Q 32,37 28,31 Z",
                "worried"   => "M 26,34 Q 32,29 38,34",
                "dizzy"     => "M 25,32 Q 28,29 31,32 Q 34,35 37,32 Q 40,29 40,32",
                "mad"       => "M 25,33 L 39,33",
                _           => "M 24,29 Q 32,38 40,29 Z",
            });
            double eyeY = mood == "surprised" ? 1.25 : mood == "dizzy" ? 0.5 : mood == "mad" ? 0.8 : 1;
            EyeLScale.ScaleY = eyeY; EyeRScale.ScaleY = eyeY;
            EyeLScale.ScaleX = EyeRScale.ScaleX = 1;   // clear any bug-out from a grab
        }

        public void FaceRight(bool right) =>
            Flip.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(right ? 1 : -1, TimeSpan.FromMilliseconds(140)));

        public void SetWalking(bool on)
        {
            if (on)
            {
                var swing = new DoubleAnimation(-24, 24, TimeSpan.FromMilliseconds(260)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                var swingB = new DoubleAnimation(24, -24, TimeSpan.FromMilliseconds(260)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                LegLRot.BeginAnimation(RotateTransform.AngleProperty, swing);
                LegRRot.BeginAnimation(RotateTransform.AngleProperty, swingB);
                Bob.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -2.5, TimeSpan.FromMilliseconds(130)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            }
            else
            {
                LegLRot.BeginAnimation(RotateTransform.AngleProperty, null); LegLRot.Angle = 0;
                LegRRot.BeginAnimation(RotateTransform.AngleProperty, null); LegRRot.Angle = 0;
                Bob.BeginAnimation(TranslateTransform.YProperty, null); Bob.Y = 0;
            }
        }

        private void HideTools()
        {
            WrenchTool.Visibility = DriverTool.Visibility = HammerTool.Visibility = SawTool.Visibility =
                SqueegeeTool.Visibility = TowelTool.Visibility = Sandwich.Visibility = PopCan.Visibility = Visibility.Collapsed;
            ArmSlide.BeginAnimation(TranslateTransform.XProperty, null); ArmSlide.X = 0;
        }

        public void SetHardHat(bool on) => HardHat.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        private readonly DispatcherTimer _lunch = new() { Interval = TimeSpan.FromMilliseconds(1250) };
        private int _lunchStep;

        // Lunch break: sit on a ledge, kick the dangling legs, take bites of a sandwich and swigs of pop.
        public void SetLunch(bool on)
        {
            HideTools();
            LunchBox.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on)
            {
                _lunch.Stop();
                Sandwich.Visibility = Visibility.Collapsed; PopCan.Visibility = Visibility.Collapsed;
                LegLRot.BeginAnimation(RotateTransform.AngleProperty, null); LegLRot.Angle = 0;
                LegRRot.BeginAnimation(RotateTransform.AngleProperty, null); LegRRot.Angle = 0;
                ArmRot.BeginAnimation(RotateTransform.AngleProperty, null); ArmRot.Angle = 20;
                WrenchTool.Visibility = Visibility.Visible;
                SetMood("happy");
                return;
            }
            ShowFace(true);
            SetMood("happy");
            LArmRot.BeginAnimation(RotateTransform.AngleProperty, null); LArmRot.Angle = 14;
            LegLRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-13, 11, TimeSpan.FromMilliseconds(540)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            LegRRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(11, -13, TimeSpan.FromMilliseconds(540)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            _lunchStep = 0;
            _lunch.Tick -= LunchTick; _lunch.Tick += LunchTick;
            Bite();
            _lunch.Start();
        }

        private void LunchTick(object? s, EventArgs e) { _lunchStep++; if (_lunchStep % 4 == 0) Sip(); else Bite(); }

        // Raise the sandwich to his mouth for a bite.
        private void Bite()
        {
            PopCan.Visibility = Visibility.Collapsed;
            Sandwich.Visibility = Visibility.Visible;
            ArmRot.BeginAnimation(RotateTransform.AngleProperty, null);
            ArmRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-28, -66, TimeSpan.FromMilliseconds(340)) { AutoReverse = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        // Tip the can up for a couple of swigs.
        private void Sip()
        {
            Sandwich.Visibility = Visibility.Collapsed;
            PopCan.Visibility = Visibility.Visible;
            ArmRot.BeginAnimation(RotateTransform.AngleProperty, null);
            ArmRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-72, -104, TimeSpan.FromMilliseconds(420)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2), EasingFunction = new SineEase() });
        }

        // Work on something: pull out a hammer (pounds), a wrench (twists), or a saw (saws back and forth), with sparks.
        public void Work()
        {
            if (_mood == "dizzy") return;
            SetMood("focused");
            HideTools();
            int sparkReps = 4;
            switch (_r.Next(3))
            {
                case 0:   // HAMMER — swing up high and pound straight down, hard
                    HammerTool.Visibility = Visibility.Visible;
                    var pound = new DoubleAnimation(-50, 26, TimeSpan.FromMilliseconds(160)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                    pound.Completed += WorkDone;
                    ArmRot.BeginAnimation(RotateTransform.AngleProperty, pound);
                    break;
                case 1:   // WRENCH — crank it back and forth
                    WrenchTool.Visibility = Visibility.Visible;
                    var twist = new DoubleAnimation(24, -20, TimeSpan.FromMilliseconds(170)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4) };
                    twist.Completed += WorkDone;
                    ArmRot.BeginAnimation(RotateTransform.AngleProperty, twist);
                    break;
                default:  // SAW — hold steady and stroke the blade in and out
                    SawTool.Visibility = Visibility.Visible;
                    ArmRot.BeginAnimation(RotateTransform.AngleProperty, null); ArmRot.Angle = 6;
                    sparkReps = 7;
                    var saw = new DoubleAnimation(-12, 12, TimeSpan.FromMilliseconds(120)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(7), EasingFunction = new SineEase() };
                    saw.Completed += WorkDone;
                    ArmSlide.BeginAnimation(TranslateTransform.XProperty, saw);
                    break;
            }
            Sparks.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(sparkReps) });
        }

        private void WorkDone(object? s, EventArgs e)
        {
            ArmRot.BeginAnimation(RotateTransform.AngleProperty, null); ArmRot.Angle = 20;
            ArmSlide.BeginAnimation(TranslateTransform.XProperty, null); ArmSlide.X = 0;
            if (_mood == "focused") SetMood("happy");
        }

        // Grabbed by the mouse: bug-eyed and squished wide, arms and legs flailing.
        public void SetGrabbed(bool on)
        {
            if (on)
            {
                _idle.Stop();
                HideTools();
                ShowFace(true);
                SetMood("surprised");
                var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
                Squeeze.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.2, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease });
                Squeeze.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.84, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease });
                EyeLScale.ScaleX = EyeRScale.ScaleX = 1.7;   // eyes bug way out
                EyeLScale.ScaleY = EyeRScale.ScaleY = 2.1;
                ArmRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-78, -112, TimeSpan.FromMilliseconds(110)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                LArmRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-78, -112, TimeSpan.FromMilliseconds(130)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                LegLRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-26, 26, TimeSpan.FromMilliseconds(120)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                LegRRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(26, -26, TimeSpan.FromMilliseconds(120)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            }
            else
            {
                Squeeze.BeginAnimation(ScaleTransform.ScaleXProperty, null); Squeeze.ScaleX = 1;
                Squeeze.BeginAnimation(ScaleTransform.ScaleYProperty, null); Squeeze.ScaleY = 1;
                ArmRot.BeginAnimation(RotateTransform.AngleProperty, null); ArmRot.Angle = 20;
                LArmRot.BeginAnimation(RotateTransform.AngleProperty, null); LArmRot.Angle = 14;
                LegLRot.BeginAnimation(RotateTransform.AngleProperty, null); LegLRot.Angle = 0;
                LegRRot.BeginAnimation(RotateTransform.AngleProperty, null); LegRRot.Angle = 0;
            }
        }

        // Tossed and landed: gritted teeth, fists shaking, an angry little huff. The caller resumes his work after.
        public void SetMad(bool on)
        {
            if (on)
            {
                HideTools();
                ShowFace(true);
                Squeeze.BeginAnimation(ScaleTransform.ScaleXProperty, null); Squeeze.ScaleX = 1;
                Squeeze.BeginAnimation(ScaleTransform.ScaleYProperty, null); Squeeze.ScaleY = 1;
                SetMood("mad");
                Bob.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-2.5, 2.5, TimeSpan.FromMilliseconds(55)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(8) });   // angry shake
                ArmRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-68, -96, TimeSpan.FromMilliseconds(120)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(5) });   // fists up
                LArmRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-58, -86, TimeSpan.FromMilliseconds(120)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(5) });
            }
            else
            {
                Bob.BeginAnimation(TranslateTransform.XProperty, null); Bob.X = 0;
                ArmRot.BeginAnimation(RotateTransform.AngleProperty, null); ArmRot.Angle = 20;
                LArmRot.BeginAnimation(RotateTransform.AngleProperty, null); LArmRot.Angle = 14;
                WrenchTool.Visibility = Visibility.Visible;
                SetMood("happy");
            }
        }

        // Hide the face so he reads as turned away (you don't climb looking outward).
        private void ShowFace(bool v)
        {
            var vis = v ? Visibility.Visible : Visibility.Collapsed;
            EyeLWhite.Visibility = EyeRWhite.Visibility = Pupils.Visibility = vis;
            Mouth.Visibility = ToothL.Visibility = ToothR.Visibility = vis;
        }

        // Scale a vertical surface: back to us, both hands clawing upward in alternation and the feet pushing.
        public void SetClimbing(bool on)
        {
            if (on)
            {
                HideTools();
                SetMood("focused");
                SetHardHat(true);  // safety first, up high
                ShowFace(false);   // turn around and face the wall
                var reachA = new DoubleAnimation(-118, -66, TimeSpan.FromMilliseconds(240)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                var reachB = new DoubleAnimation(-66, -118, TimeSpan.FromMilliseconds(240)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };   // opposite phase
                ArmRot.BeginAnimation(RotateTransform.AngleProperty, reachA);
                LArmRot.BeginAnimation(RotateTransform.AngleProperty, reachB);
                var kick = new DoubleAnimation(-20, 16, TimeSpan.FromMilliseconds(240)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                var kickB = new DoubleAnimation(16, -20, TimeSpan.FromMilliseconds(240)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                LegLRot.BeginAnimation(RotateTransform.AngleProperty, kick);
                LegRRot.BeginAnimation(RotateTransform.AngleProperty, kickB);
            }
            else
            {
                ArmRot.BeginAnimation(RotateTransform.AngleProperty, null); ArmRot.Angle = 20;
                LArmRot.BeginAnimation(RotateTransform.AngleProperty, null); LArmRot.Angle = 14;
                WrenchTool.Visibility = Visibility.Visible;
                SetHardHat(false);   // hat off, back on the ground
                ShowFace(true);   // face us again once he's off the wall
                SetWalking(false);
                SetMood("happy");
            }
        }

        public void Surprised() => SetMood("surprised");

        // Spotted a real problem: shocked face, one arm jabbing/pointing at it, the other waving for attention, "!" bubble.
        public void SetAlert(bool on)
        {
            if (!on)
            {
                Alarm.Visibility = Visibility.Collapsed;
                Alarm.BeginAnimation(OpacityProperty, null); Alarm.Opacity = 1;
                ArmRot.BeginAnimation(RotateTransform.AngleProperty, null); ArmRot.Angle = 20;
                LArmRot.BeginAnimation(RotateTransform.AngleProperty, null); LArmRot.Angle = 14;
                WrenchTool.Visibility = Visibility.Visible;
                SetMood("happy");
                return;
            }
            HideTools();
            ShowFace(true);
            SetMood("surprised");
            Alarm.Visibility = Visibility.Visible;
            Alarm.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(340)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            ArmRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-96, -118, TimeSpan.FromMilliseconds(170)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });   // jab / point up at it
            LArmRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-58, -108, TimeSpan.FromMilliseconds(190)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });   // wave for attention
        }

        // Wash the screen: grab a squeegee or towel and sweep it back and forth (bubbles/water come from the caller).
        public void Clean()
        {
            if (_mood == "dizzy") return;
            SetMood("focused");
            HideTools();
            if (_r.Next(2) == 0) SqueegeeTool.Visibility = Visibility.Visible; else TowelTool.Visibility = Visibility.Visible;
            var sweep = new DoubleAnimation(-34, 26, TimeSpan.FromMilliseconds(260)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(5) };
            sweep.Completed += (s, e) => { ArmRot.Angle = 20; HideTools(); WrenchTool.Visibility = Visibility.Visible; if (_mood == "focused") SetMood("happy"); };
            ArmRot.BeginAnimation(RotateTransform.AngleProperty, sweep);
        }

        // Float down under a parachute, arms up gripping the strings.
        public void SetParachute(bool on)
        {
            SetWalking(false);
            Parachute.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            ArmRot.BeginAnimation(RotateTransform.AngleProperty, null); ArmRot.Angle = on ? -72 : 20;
            LArmRot.BeginAnimation(RotateTransform.AngleProperty, null); LArmRot.Angle = on ? -72 : 14;
            if (on) SetMood("surprised");
        }

        // Boards down: hold up a sign with what's missing. He either hangs his head sadly or sits there
        // knocked out with stars and birds circling — the caller alternates the two.
        // All four boards gone: knocked out cold — flat on his back with X-X eyes and stars/birds, tombstone behind.
        public void SetGraveyard(bool on, string text = "")
        {
            SetWalking(false);
            ShowFace(true);
            Alarm.Visibility = Visibility.Collapsed; Alarm.BeginAnimation(OpacityProperty, null);
            Sign.Visibility = Visibility.Collapsed;
            SignRot.BeginAnimation(RotateTransform.AngleProperty, null);
            Flip.BeginAnimation(ScaleTransform.ScaleXProperty, null); Flip.ScaleX = 1;
            Tombstone.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            XEyes.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            Pupils.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            ArmRot.BeginAnimation(RotateTransform.AngleProperty, null);
            LArmRot.BeginAnimation(RotateTransform.AngleProperty, null);
            LegLRot.BeginAnimation(RotateTransform.AngleProperty, null);
            LegRRot.BeginAnimation(RotateTransform.AngleProperty, null);
            if (!on)
            {
                LayRot.BeginAnimation(RotateTransform.AngleProperty, null); LayRot.Angle = 0; LayShift.Y = 0;
                ArmRot.Angle = 20; LArmRot.Angle = 14; LegLRot.Angle = 0; LegRRot.Angle = 0;
                DizzyRot.BeginAnimation(RotateTransform.AngleProperty, null);
                EyesLook.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
                SetMood("happy");
                return;
            }
            HideTools();
            TombText.Text = text;
            ArmRot.Angle = -50; LArmRot.Angle = 46; LegLRot.Angle = -22; LegRRot.Angle = 22;   // sprawled
            SetMood("dizzy");   // wavy mouth + stars/birds (X-eyes overlay the squinted whites)
            DizzyRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900)) { RepeatBehavior = RepeatBehavior.Forever });
            LayShift.Y = 30;
            LayRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 90, TimeSpan.FromMilliseconds(560)) { EasingFunction = new BounceEase { Bounces = 1, Bounciness = 3, EasingMode = EasingMode.EaseOut } });   // keel over
        }

        public void SetSad(bool on, string text = "", bool knockedOut = false)
        {
            SetWalking(false);
            ShowFace(true);   // in case he was mid-climb (back turned)
            Alarm.Visibility = Visibility.Collapsed; Alarm.BeginAnimation(OpacityProperty, null);
            Tombstone.Visibility = Visibility.Collapsed;
            XEyes.Visibility = Visibility.Collapsed; Pupils.Visibility = Visibility.Visible;   // stand back up from any knocked-out pose
            LayRot.BeginAnimation(RotateTransform.AngleProperty, null); LayRot.Angle = 0; LayShift.Y = 0;
            Sign.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            Flip.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            Flip.ScaleX = 1;   // face forward so the sign lettering reads correctly (never mirrored)
            LArmRot.BeginAnimation(RotateTransform.AngleProperty, null);
            LArmRot.Angle = on ? 26 : 14;   // arm droops
            if (!on)
            {
                DizzyRot.BeginAnimation(RotateTransform.AngleProperty, null);
                SignRot.BeginAnimation(RotateTransform.AngleProperty, null);
                EyesLook.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
                SetMood("happy");
                return;
            }
            SignText.Text = text;
            SignRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-5, 5, TimeSpan.FromMilliseconds(750)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });   // wave the sign a little
            if (knockedOut)
            {
                SetMood("dizzy");   // squint + stars/birds overlay
                DizzyRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900)) { RepeatBehavior = RepeatBehavior.Forever });
            }
            else
            {
                DizzyRot.BeginAnimation(RotateTransform.AngleProperty, null);
                SetMood("worried");
                EyesLook.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(3, TimeSpan.FromMilliseconds(300)));
            }
        }

        // Hardware's back: grin, throw a fist up, and wave the "HARDWARE READY" sign for joy.
        public void Celebrate(string text)
        {
            SetWalking(false);
            ShowFace(true);
            Alarm.Visibility = Visibility.Collapsed; Alarm.BeginAnimation(OpacityProperty, null);
            Tombstone.Visibility = Visibility.Collapsed;
            XEyes.Visibility = Visibility.Collapsed; Pupils.Visibility = Visibility.Visible;
            LayRot.BeginAnimation(RotateTransform.AngleProperty, null); LayRot.Angle = 0; LayShift.Y = 0;   // back on his feet
            Flip.BeginAnimation(ScaleTransform.ScaleXProperty, null); Flip.ScaleX = 1;   // face forward so the sign reads right
            DizzyRot.BeginAnimation(RotateTransform.AngleProperty, null);
            EyesLook.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
            SetMood("happy");
            Sign.Visibility = Visibility.Visible;
            SignText.Text = text;
            LArmRot.BeginAnimation(RotateTransform.AngleProperty, null); LArmRot.Angle = 22;
            ArmRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-40, -95, TimeSpan.FromMilliseconds(240)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });   // fist pump
            SignRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-16, 16, TimeSpan.FromMilliseconds(300)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });   // wave it big
        }

        public void EndCelebrate()
        {
            ArmRot.BeginAnimation(RotateTransform.AngleProperty, null); ArmRot.Angle = 20;
            SignRot.BeginAnimation(RotateTransform.AngleProperty, null);
            Sign.Visibility = Visibility.Collapsed;
            SetMood("happy");
        }

        // Land dazed: X-ish squint + spinning stars, then perk back up.
        public void SetDizzy(bool on)
        {
            SetMood(on ? "dizzy" : "happy");
            if (on)
                DizzyRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900)) { RepeatBehavior = RepeatBehavior.Forever });
            else
                DizzyRot.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }
}
