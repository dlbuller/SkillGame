using Iot.Device.Ft232H;
using Iot.Device.FtCommon;
using Iot.Device.Seesaw;
using Iot.Device.Ws28xx;
using Iot.Device.Graphics;
using NAudio.Wave;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace SkillGame
{
    /// <summary>All WS2812b NeoPixel strip effects; owns the strip handle and a CancellationTokenSource so any running pattern can be cancelled.</summary>
    public class LedEffects : ILedSink
    {
        private Ws2812b neo;
        private RawPixelContainer image;
        private LightFunctions lights = new LightFunctions();
        private Random rnd = new Random();
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public LedEffects(Ws2812b strip)
        {
            neo = strip;
            image = neo.Image;
            _frame = new Color[image.Width];
        }


        public void CancelLights()
        {
            _cts.Cancel();
            // Not disposed on purpose: another thread may still hold the old token, so disposing here could crash it.
            _cts = new CancellationTokenSource();
        }

        /// <summary>Fires after every strip update with the live pixel colors, so the on-screen LEDs mirror the real ones.
        /// Static so it survives the LedEffects instance being rebuilt on a hardware reconnect.</summary>
        public static Action<Color[]>? FrameProduced;

        // Shadow copy of the strip (RawPixelContainer is write-only), kept in sync by SetPix/ClearPix and published on Present.
        private Color[] _frame = Array.Empty<Color>();
        private void SetPix(int x, int y, Color c) { image.SetPixel(x, y, c); if (y == 0 && (uint)x < (uint)_frame.Length) _frame[x] = c; }
        private void ClearPix() { image.Clear(); for (int i = 0; i < _frame.Length; i++) _frame[i] = Color.Black; }

        // Push the frame to the strip and hand the same pixels to any on-screen mirror.
        private void Present()
        {
            neo.Update();
            var cb = FrameProduced;
            if (cb != null) { try { cb((Color[])_frame.Clone()); } catch { } }
        }

        public async void StartupLights()
        {
            await lights.StartupLights(neo, 100, Color.Azure, _cts.Token);
        }

        public void LEDStripTest(string pattern, string type)
        {

            switch (pattern)
            {
                case "Marquee":
                    if (type == "Attract")
                    {
                        MarqueeAttract((lights.GetWheelColor(rnd.Next(0, 255))));
                    }
                    else if (type == "Distract")
                    {
                        MarqueeDistract((lights.GetWheelColor(rnd.Next(0, 255))));
                    }
                    else
                    {
                        Marquee((lights.GetWheelColor(rnd.Next(0, 255))));
                    }
                        break;
                case "Rainbow Wave":
                    if (type == "Attract")
                    {
                        RainbowWaveAttract();
                        
                    }
                    else
                    {
                        RainbowWave();
                    }
                    break;
                case "Comet":
                    if (type == "Attract")
                    {
                        CometAttract(lights.GetWheelColor(rnd.Next(0, 255)), 20, 10);
                    }
                    else
                    {
                        Comet(lights.GetWheelColor(rnd.Next(0, 255)), 20, 10);
                    }
                    break;
                case "Breathe":
                    if (type == "Attract")
                    {
                        BreatheAttract(lights.GetWheelColor(rnd.Next(0, 255)), 5);
                    }
                    else if (type == "Distract")
                    {
                        BreatheDistract(lights.GetWheelColor(rnd.Next(0, 255)), 1);
                    }
                    else
                    {
                        Breathe(lights.GetWheelColor(rnd.Next(0, 255)), 5);
                    }
                    break;
                case "Crazy Strobe":
                    if (type == "Attract")
                    {
                        CrazyStrobeAttract(20);
                    }
                    else if (type == "Distract")
                    {
                        CrazyStrobe(2);
                    }
                    else
                    {
                        CrazyStrobe(5);
                    }
                    break;
                case "Plasma Wave":
                    if (type == "Attract")
                    {
                        PlasmaWaveAttract(20);
                    }
                    else
                    {
                        PlasmaWave(20);
                    }
                        break;
                case "Meteor Rain":
                    if (type == "Attract")
                    {
                        MeteorRainAttract(lights.GetWheelColor(rnd.Next(0, 255)), 5);
                    }
                    else
                    {
                        MeteorRain(lights.GetWheelColor(rnd.Next(0, 255)), 5);
                    }
                    break;
                case "Sparkle":
                    if (type == "Attract")
                    {
                        SparkleAttract(lights.GetWheelColor(rnd.Next(0, 255)), 10);
                    }
                    else
                    {
                        Sparkle(lights.GetWheelColor(rnd.Next(0, 255)), 10);
                    }
                    break;
                case "Knight Rider":
                    if (type == "Attract")
                    {
                        KnightRiderAttract(Color.Red, 20);
                    }
                    else
                    {
                        KnightRider(Color.Red, 20);
                    }
                        break;
                case "Bouncing Balls":
                    if (type == "Attract")
                    {
                        BouncingBallsAttract(new Color[] { Color.Red, Color.Green, Color.Blue }, 20);
                    }
                    else
                    {
                        BouncingBalls(new Color[] { Color.Red, Color.Green, Color.Blue }, 20);
                    }
                        break;
                case "Lightning":
                    if (type == "Attract")
                    {
                        LightningAttract(lights.GetWheelColor(rnd.Next(0, 255)));
                    }
                    else
                    {
                        Lightning(lights.GetWheelColor(rnd.Next(0, 255)));
                    }
                        break;
                case "Confetti":
                    if (type == "Attract")
                    {
                        ConfettiAttract(50);
                    }
                    else
                    {
                        Confetti(50);
                    }
                        break;
                case "DNA Helix":
                    if (type == "Attract")
                    {
                        DNAHelixAttract(lights.GetWheelColor(rnd.Next(0, 255)), lights.GetWheelColor(rnd.Next(0, 255)), 50);
                    }
                    else
                    {
                        DNAHelix(lights.GetWheelColor(rnd.Next(0, 255)), lights.GetWheelColor(rnd.Next(0, 255)), 50);
                    }
                        break;
                case "Quad Strobe":
                    if (type == "Attract")
                    {
                        QuadStrobeAttract(lights.GetWheelColor(rnd.Next(0, 255)), 500);
                    }
                    else
                    {
                        QuadStrobe(lights.GetWheelColor(rnd.Next(0, 255)), 500);
                    }
                    break;
                case "Fireworks":
                    Fireworks();
                    break;
                case "Starfield":
                    Starfield();
                    break;
                case "Aurora":
                    Aurora();
                    break;
                case "VU Meter":
                    VUMeter();
                    break;
            }
        }



        public void SolidColor(Color color)
        {
            lights.SolidColor(neo, color);
        }

        public void LEDOFF()
        {
            ClearPix();
            Present();
        }

        public async void Marquee(Color color)
        {
            await lights.Marquee(neo, 150, color, _cts.Token);
        }

        public async void MarqueeDistract(Color color)
        {
            await lights.Marquee(neo, 50, color, _cts.Token);
        }

        public async void MarqueeAttract(Color color)
        {
            await lights.MarqueeAttract(neo, 150, color, _cts.Token);
        }

        public async void RainbowWave()
        {
            await lights.RainbowWave(neo, 2, _cts.Token);
        }

        public async void Comet(Color color, byte tailLength, int speed)
        {
            await lights.Comet(neo, color, tailLength, speed, _cts.Token);
        }

        public async void Breathe(Color color, int speed)
        {
            await lights.Breathe(neo, color, speed, _cts.Token);
        }

        public async void BreatheDistract(Color color, int speed)
        {
            await lights.Breathe(neo, color, speed, _cts.Token);
        }

        public async void CrazyStrobe(int speed)
        {
            await lights.CrazyStrobe(neo, speed, _cts.Token);
        }

        public async void PlasmaWave(int speed)
        {
            await lights.PlasmaWave(neo, speed, _cts.Token);
        }

        public async void MeteorRain(Color color, int speed)
        {
            await lights.MeteorRain(neo, color, speed, _cts.Token);
        }

        public async void Sparkle(Color color, int speed)
        {
            await lights.Sparkle(neo, color, speed, _cts.Token);
        }

        public async void KnightRider(Color color, int speed)
        {
            await lights.KnightRider(neo, color, speed, _cts.Token);
        }

        public async void BouncingBalls(Color[] colors, int speed)
        {
            await lights.BouncingBalls(neo, colors, speed, _cts.Token);
        }

        public async void Lightning(Color color)
        {
            await lights.Lightning(neo, color, _cts.Token);
        }

        public async void Confetti(int speed)
        {
            await lights.Confetti(neo, speed, _cts.Token);
        }

        public async void DNAHelix(Color color1, Color color2, int speed)
        {
            await lights.DNAHelix(neo, color1, color2, speed, _cts.Token);
        }

        public async void QuadStrobe(Color color, int speed)
        {
            await lights.QuadStrobe(neo, color, speed, _cts.Token);
        }

        public async void RainbowWaveAttract()
        {
            await lights.RainbowWaveAttract(neo, 2, _cts.Token);
            SolidColor(Color.Black);
        }

        public async void CometAttract(Color color, byte tailLength, int speed)
        {
            await lights.CometAttract(neo, color, tailLength, speed, _cts.Token);
            SolidColor(Color.Black);
        }

        public async void BreatheAttract(Color color, int speed)
        {
            await lights.BreatheAttract(neo, color, speed, _cts.Token);
            SolidColor(Color.Black);
        }

        public async void CrazyStrobeAttract(int speed)
        {
            await lights.CrazyStrobeAttract(neo, speed, _cts.Token);
            SolidColor(Color.Black);
        }

        public async void PlasmaWaveAttract(int speed)
        {
            await lights.PlasmaWaveAttract(neo, speed, _cts.Token);
            SolidColor(Color.Black);
        }

        public async void MeteorRainAttract(Color color, int speed)
        {
            await lights.MeteorRainAttract(neo, color, speed, _cts.Token);
            SolidColor(Color.Black);
        }

        public async void SparkleAttract(Color color, int speed)
        {
            await lights.SparkleAttract(neo, color, speed, _cts.Token);
            SolidColor(Color.Black);
        }

        public async void KnightRiderAttract(Color color, int speed)
        {
            await lights.KnightRiderAttract(neo, color, speed, _cts.Token);
            SolidColor(Color.Black);
        }

        public async void BouncingBallsAttract(Color[] colors, int speed)
        {
            await lights.BouncingBallsAttract(neo, colors, speed, _cts.Token);
            SolidColor(Color.Black);
        }

        public async void LightningAttract(Color color)
        {
            await lights.LightningAttract(neo, color, _cts.Token);
            SolidColor(Color.Black);
        }

        public async void ConfettiAttract(int speed)
        {
            await lights.ConfettiAttract(neo, speed, _cts.Token);
            SolidColor(Color.Black);
        }

        public async void DNAHelixAttract(Color color1, Color color2, int speed)
        {
            await lights.DNAHelixAttract(neo, color1, color2, speed, _cts.Token);
            SolidColor(Color.Black);
        }

        public async void QuadStrobeAttract(Color color, int speed)
        {
            await lights.QuadStrobeAttract(neo, color, speed, _cts.Token);
            SolidColor(Color.Black);
        }

        public void BrightnessUp(Color color)
        {
            lights.BrightnessUP(neo, color);
        }

        public void BrightnessDown(Color color)
        {
            lights.BrightnessDown(neo, color);
        }

        // ================= effects & reactive show lighting =================
        private static Color Dim(Color c, float f)
        {
            f = f < 0f ? 0f : (f > 1f ? 1f : f);
            return Color.FromArgb((int)(c.R * f), (int)(c.G * f), (int)(c.B * f));
        }
        private void Fill(Color c)
        {
            for (int p = 0; p < image.Width; p++) SetPix(p, 0, c);
            Present();
        }

        /// <summary>Random expanding color bursts then clear; also the winner celebration.</summary>
        public async void Fireworks()
        {
            var token = _cts.Token;
            try
            {
                int n = image.Width;
                for (int burst = 0; burst < 6 && !token.IsCancellationRequested; burst++)
                {
                    int center = rnd.Next(n);
                    Color c = lights.GetWheelColor(rnd.Next(255));
                    for (int r = 0; r < 14 && !token.IsCancellationRequested; r++)
                    {
                        ClearPix();
                        for (int d = -r; d <= r; d++) { int p = center + d; if (p >= 0 && p < n) SetPix(p, 0, Dim(c, 1f - r / 14f)); }
                        Present();
                        await Task.Delay(18, token);
                    }
                }
                ClearPix(); Present();
            }
            catch (Exception) { }
        }

        /// <summary>Random twinkling white/blue stars that fade.</summary>
        public async void Starfield()
        {
            var token = _cts.Token;
            try
            {
                int n = image.Width;
                var b = new double[n];
                while (!token.IsCancellationRequested)
                {
                    if (rnd.NextDouble() < 0.35) b[rnd.Next(n)] = 1.0;
                    ClearPix();
                    for (int p = 0; p < n; p++)
                        if (b[p] > 0.02) { SetPix(p, 0, Dim(rnd.Next(4) == 0 ? Color.SkyBlue : Color.White, (float)b[p])); b[p] *= 0.90; }
                    Present();
                    await Task.Delay(30, token);
                }
            }
            catch (Exception) { }
        }

        /// <summary>Slow drifting green/blue aurora bands.</summary>
        public async void Aurora()
        {
            var token = _cts.Token;
            try
            {
                int n = image.Width; double t = 0;
                while (!token.IsCancellationRequested)
                {
                    for (int p = 0; p < n; p++)
                    {
                        double v = (Math.Sin(p * 0.15 + t) + 1) / 2;
                        SetPix(p, 0, Color.FromArgb((int)(v * 40), (int)(v * 180), (int)((1 - v) * 160)));
                    }
                    Present(); t += 0.08;
                    await Task.Delay(30, token);
                }
            }
            catch (Exception) { }
        }

        /// <summary>A bright pulse that runs the length of the strip once (coin insert).</summary>
        public async void CoinRipple()
        {
            CancelLights();
            var token = _cts.Token;
            try
            {
                int n = image.Width;
                for (int head = 0; head < n + 7 && !token.IsCancellationRequested; head++)
                {
                    ClearPix();
                    for (int t = 0; t <= 6; t++) { int p = head - t; if (p >= 0 && p < n) SetPix(p, 0, Dim(Color.Gold, 1f - t / 7f)); }
                    Present();
                    await Task.Delay(8, token);
                }
                ClearPix(); Present();
            }
            catch (Exception) { }
        }

        /// <summary>Flash for the points just scored, then hold a persistent 1..8 level meter (green -&gt; red) until the next event.</summary>
        public async void ScoreHit(int points, int level)
        {
            CancelLights();
            var token = _cts.Token;
            try
            {
                int n = image.Width;
                Color c = lights.GetWheelColor((points * 3) % 255);
                for (int i = 0; i < 2 && !token.IsCancellationRequested; i++)
                {
                    Fill(c); await Task.Delay(45, token);
                    Fill(Color.Black); await Task.Delay(45, token);
                }
                if (token.IsCancellationRequested) return;
                int lit = Math.Max(0, Math.Min(n, (int)(n * level / 8.0)));
                ClearPix();
                for (int p = 0; p < lit; p++)
                {
                    double frac = (double)p / n;
                    Color bar = frac < 0.6 ? Color.Lime : (frac < 0.85 ? Color.Yellow : Color.Red);
                    SetPix(p, 0, bar);
                }
                Present();   // left lit (persistent) until the next hit / coin / game-over
            }
            catch (Exception) { }
        }

        /// <summary>Rapid red strobe, then dark (tilt).</summary>
        public async void TiltStrobe()
        {
            CancelLights();
            var token = _cts.Token;
            try
            {
                for (int i = 0; i < 8 && !token.IsCancellationRequested; i++)
                {
                    Fill(Color.Red); await Task.Delay(60, token);
                    Fill(Color.Black); await Task.Delay(60, token);
                }
            }
            catch (Exception) { }
        }

        /// <summary>Audio-reactive VU meter that captures system output via WASAPI loopback and lights the strip as a green-to-red level bar.</summary>
        public async void VUMeter()
        {
            var token = _cts.Token;
            WasapiLoopbackCapture? capture = null;
            float level = 0f;
            object gate = new object();
            try
            {
                capture = new WasapiLoopbackCapture();
                capture.DataAvailable += (s, a) =>
                {
                    float max = 0f;
                    for (int i = 0; i + 4 <= a.BytesRecorded; i += 4)
                    {
                        float v = Math.Abs(BitConverter.ToSingle(a.Buffer, i));
                        if (v > max) max = v;
                    }
                    lock (gate) level = max;
                };
                capture.StartRecording();

                int n = image.Width;
                double shown = 0;
                while (!token.IsCancellationRequested)
                {
                    float cur; lock (gate) cur = level;
                    double target = Math.Min(1.0, cur * 1.8);
                    shown = target > shown ? target : (shown * 0.85 + target * 0.15);   // fast attack, slow decay
                    int litN = (int)(shown * n);
                    ClearPix();
                    for (int p = 0; p < litN; p++)
                    {
                        double frac = (double)p / n;
                        Color c = frac < 0.6 ? Color.Lime : (frac < 0.85 ? Color.Yellow : Color.Red);
                        SetPix(p, 0, c);
                    }
                    Present();
                    await Task.Delay(28, token);
                }
            }
            catch (Exception) { }
            finally { try { capture?.StopRecording(); capture?.Dispose(); } catch { } }
        }

        // ---- ILedSink: the game rules call these on events ----
        public void Coin() => CoinRipple();
        public void Score(int points, int level) => ScoreHit(points, level);
        public void Winner() { CancelLights(); Fireworks(); }
        public void GameOver(bool tilt) { if (tilt) TiltStrobe(); else { CancelLights(); LEDOFF(); } }
    }
}
