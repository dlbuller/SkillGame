using Iot.Device.FtCommon;
using Iot.Device.Seesaw;
using Iot.Device.Ws28xx;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;   // System.Drawing.Color, imported explicitly so WPF resolves it too.
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SkillGame
{
    public class LightFunctions
    {
        private Random rnd = new Random();
        private Color[]? _ledState = null;
        int[] brightnessRed = new int[] { 128, 0, 0 };
        int[] brightnessGreen = new int[] { 0, 128, 0 };
        int[] brightnessBlue = new int[] { 0, 0, 128 };
        int[] brightnessWhite = new int[] { 64, 64, 64 };
        int[] brightnessYellow = new int[] { 128, 128, 0 };
        int[] brightnessPurple = new int[] { 64, 0, 64 };

        public LightFunctions()
        {

        }

        #region Light Patterns

        /// <summary>Sets every LED to a solid color.</summary>
        /// <param name="device"></param>
        /// <param name="color"></param>
        public void SolidColor(Ws2812b device, Color color)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;

            try
            {
                for (int i = 0; i < img.Width; i++)
                {
                    img.SetPixel(i, 0, color);
                }
                device.Update();
            }
            catch (Exception)
            {
                
                // Handle cancellation gracefully
            }
        }

        public async Task StartupLights(Ws2812b device, int delayMs, Color color, CancellationToken token)
        {
            var img = device.Image;
            int count = img.Width;
            Random rand = new Random();
            Color neonAzure = Color.FromArgb(0, 195, 255);

            try
            {

                Stopwatch timer = Stopwatch.StartNew();
                Random rng = new Random();

                // Phase 1: random colored sparks popping in and out.
                while (timer.Elapsed.TotalSeconds < 4.0)
                {
                    device.Image.Clear();
                    for (int i = 0; i < 4; i++)
                    {
                        Color randomColor = Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256));
                        device.Image.SetPixel(rng.Next(count), 0, randomColor);
                    }
                    device.Update();
                    await Task.Delay(rng.Next(30, 100)); // Glitchy timing
                }

                // Phase 2: an accelerating high-speed rainbow wave.
                int offset = 0;
                while (timer.Elapsed.TotalSeconds < 7.0)
                {
                    double progress = (timer.Elapsed.TotalSeconds - 3.0) / 3.0; // 0.0 to 1.0
                    int delay = (int)(30 - (25 * progress)); // Gets faster

                    for (int i = 0; i < count; i++)
                    {
                        int hue = (i + offset) * (360 / count);
                        device.Image.SetPixel(i, 0, HsvToRgb(hue % 360, 1, 1));
                    }
                    device.Update();
                    offset += 3;
                    await Task.Delay(Math.Max(5, delay));
                }

                // Phase 3: an accelerating multicolor strobe.
                while (timer.Elapsed.TotalSeconds < 8.0)
                {
                    double strobeProgress = (timer.Elapsed.TotalSeconds - 6.0) / 2.0;
                    int strobeDelay = (int)(60 - (50 * strobeProgress));

                    // Flash on with a random vibrant color.
                    Fill(device, count, Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256)));
                    await Task.Delay(strobeDelay);

                    // Flash Off
                    Fill(device, count, Color.Black);
                    await Task.Delay(strobeDelay);
                }

                SolidColor(device, Color.FromArgb(20, 20, 20));
            }

            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Creates a marquee effect.</summary>
        /// <param name="device"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task Marquee(Ws2812b device, int delayMs, Color color, CancellationToken token)
        {
            var img = device.Image;
            int count = img.Width;
            bool toggle = false;

            try
            {

                while (!token.IsCancellationRequested)
                {
                    img.Clear();

                    for (int i = 0; i < count; i++)
                    {
                        if ((i % 2 == 0) == toggle)
                        {
                            img.SetPixel(i, 0, color);
                        }
                    }

                    device.Update();

                    toggle = !toggle;
                    await Task.Delay(150, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Cycles a rainbow wave along the strip.</summary>
        /// <param name="device"></param>
        /// <param name="speedDelay"></param>
        /// <param name="waveLength"></param>
        /// <param name="cycls"></param>
        public async Task RainbowWave(Ws2812b device, int delayMs, CancellationToken token)
        {
            var img = device.Image;
            int count = img.Width;
            int offset = 0;
            Stopwatch watch = new Stopwatch();
            watch.Start();

            try
            {
                while (watch.ElapsedMilliseconds < 10000)
                {
                    for (int i = 0; i < count; i++)
                    {
                        // Give each pixel a color from its position plus a moving offset.
                        int colorPos = (i * (256 / count) + offset) & 255;
                        img.SetPixel(i, 0, GetWheelColor(colorPos));
                    }

                    device.Update();

                    // Increment offset to move the wave forward
                    offset = (offset + 1) & 255;

                    // Non-blocking wait
                    await Task.Delay(delayMs, token);
                }

                watch.Stop();
                watch.Reset();
                img.Clear();
                device.Update();
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Moves a comet with a fading tail along the strip.</summary>
        /// <param name="device"></param>
        /// <param name="color"></param>
        /// <param name="tailLength"></param>
        /// <param name="delayMs"></param>
        public async Task Comet(Ws2812b device, Color color, byte tailDecay, int delayMs, CancellationToken token)
        {
            var img = device.Image;
            int count = img.Width;

            try
            {
                // Initialize the shadow buffer if needed
                if (_ledState == null || _ledState.Length != count) _ledState = new Color[count];

                int headPos = 0;

                while (!token.IsCancellationRequested)
                {
                    // 1. Fade the entire strip in the shadow buffer
                    for (int i = 0; i < count; i++)
                    {
                        // Lower scale factor (e.g. 0.7) = shorter tail, Higher (e.g. 0.9) = longer tail
                        _ledState[i] = ScaleColor(_ledState[i], 0.8);
                        img.SetPixel(i, 0, _ledState[i]);
                    }

                    // 2. Draw the bright Comet head
                    _ledState[headPos] = color;
                    img.SetPixel(headPos, 0, color);

                    // 3. Push to hardware
                    device.Update();

                    // 4. Move head
                    headPos = (headPos + 1) % count;

                    // Pause based on your delayMs speed
                    await Task.Delay(delayMs, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Smoothly pulses one color's brightness up and down.</summary>
        /// <param name="device"></param>
        /// <param name="color"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task Breathe(Ws2812b device, Color color, int speedDelay, CancellationToken token)
        {
            var img = device.Image;
            int count = img.Width;

            // One full breath is a 2*PI sine cycle.
            try
            {
                while (!token.IsCancellationRequested)
                {
                    for (double i = 0; i < Math.PI * 2; i += 0.02)
                    {
                        // Brightness eases 0 -> 1 -> 0 via a shifted sine.
                        double brightness = (Math.Sin(i - (Math.PI / 2)) + 1) / 2;

                        // Apply brightness to the color
                        Color pulsedColor = ScaleColor(color, brightness);

                        // Fill the strip
                        for (int p = 0; p < count; p++)
                        {
                            img.SetPixel(p, 0, pulsedColor);
                        }

                        device.Update();

                        // Bail out before waiting if cancellation was requested.
                        if (token.IsCancellationRequested) return;

                        // delayMs here acts as the 'breath rate'
                        await Task.Delay(speedDelay, token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Rapidly strobes the strip in yellow and blue.</summary>
        /// <param name="device"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task CrazyStrobe(Ws2812b device, int delayMs, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    for (int j = 0; j < 30; j++)
                    {
                        for (int q = 0; q < 3; q++)
                        {
                            for (int i = 0; i < img.Width; i += 3)
                            {
                                if (i + q < img.Width) img.SetPixel(i + q, 0, Color.Yellow);
                            }
                            device.Update();


                            for (int i = 0; i < img.Width; i += 3)
                            {
                                if (i + q < img.Width) img.SetPixel(i + q, 0, Color.Blue);
                            }
                            device.Update();


                            for (int i = 0; i < img.Width; i += 3)
                            {
                                if (i + q < img.Width) img.SetPixel(i + q, 0, Color.Black);
                            }
                        }

                        await Task.Delay(delayMs, token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Randomly lights and fades single LEDs.</summary>
        /// <param name="device"></param>
        /// <param name="color"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task Sparkle(Ws2812b device, Color color, int delayMs, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;
            _ledState = new Color[img.Width];

            try
            {
                while (!token.IsCancellationRequested)
                {
                    for (int i = 0; i < img.Width; i++)
                    {
                        _ledState[i] = ScaleColor(_ledState[i], 0.9); // Slow fade
                        img.SetPixel(i, 0, _ledState[i]);
                    }

                    // Add a random sparkle
                    int pos = rnd.Next(img.Width);
                    _ledState[pos] = color;
                    img.SetPixel(pos, 0, color);

                    device.Update();
                    await Task.Delay(delayMs, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Sweeps a bright eye back and forth with a fading tail.</summary>
        /// <param name="device"></param>
        /// <param name="color"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task KnightRider(Ws2812b device, Color color, int delayMs, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;
            int count = img.Width;

            try
            {
                if (_ledState == null || _ledState.Length != count) _ledState = new Color[count];

                int direction = 1; // 1 for right, -1 for left
                int position = 0;

                while (!token.IsCancellationRequested)
                {
                    // 1. Fade the entire strip (the tail)
                    for (int i = 0; i < count; i++)
                    {
                        _ledState[i] = ScaleColor(_ledState[i], 0.85); // Fade by 15%
                        img.SetPixel(i, 0, _ledState[i]);
                    }

                    // 2. Place the bright "eye"
                    _ledState[position] = color;
                    img.SetPixel(position, 0, color);

                    // 3. Push to hardware and wait
                    device.Update();
                    await Task.Delay(delayMs, token);

                    // 4. Move the position
                    position += direction;

                    // 5. Bounce off the ends
                    if (position == count - 1 || position == 0)
                    {
                        direction *= -1;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Renders a plasma wave from sine-driven RGB values.</summary>
        /// <param name="device"></param>
        /// <param name="speed"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task PlasmaWave(Ws2812b device, int speed, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;

            try
            {
                int count = img.Width;
                if (_ledState == null || _ledState.Length != count) _ledState = new Color[count];
                int time = 0;

                while (!token.IsCancellationRequested)
                {
                    for (int i = 0; i < count; i++)
                    {
                        // Use time and pixel position to calculate a unique RGB value via sine waves
                        int r = (int)(Math.Sin((i + time) * 0.1) * 127 + 128);
                        int g = (int)(Math.Sin((i + time) * 0.05) * 127 + 128);
                        int b = (int)(Math.Sin((i + time) * 0.15) * 127 + 128);

                        _ledState[i] = Color.FromArgb(r, g, b);
                        img.SetPixel(i, 0, _ledState[i]);
                    }

                    device.Update();
                    await Task.Delay(speed, token);
                    time++; // Move the wave slightly for the next frame
                }
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Bounces several colored balls along the strip.</summary>
        /// <param name="device"></param>
        /// <param name="ballColors"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task BouncingBalls(Ws2812b device, Color[] ballColors, int delayMs, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;
            int count = img.Width;
            float[] pos = new float[ballColors.Length];
            float[] vel = new float[ballColors.Length];

            try
            {
                // Initialize random positions and speeds
                for (int i = 0; i < ballColors.Length; i++)
                {
                    pos[i] = rnd.Next(count);
                    vel[i] = (float)(rnd.NextDouble() * 0.5 + 0.2);
                }

                while (!token.IsCancellationRequested)
                {
                    // Clear frame
                    for (int i = 0; i < count; i++) img.SetPixel(i, 0, Color.Black);

                    for (int i = 0; i < ballColors.Length; i++)
                    {
                        pos[i] += vel[i];
                        // Bounce off walls
                        if (pos[i] >= count - 1 || pos[i] <= 0) vel[i] *= -1;

                        int p = (int)Math.Clamp(pos[i], 0, count - 1);
                        img.SetPixel(p, 0, ballColors[i]);
                    }

                    device.Update();
                    await Task.Delay(delayMs, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Randomly flashes sections of the strip like lightning.</summary>
        /// <param name="device"></param>
        /// <param name="lightningColor"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task Lightning(Ws2812b device, Color lightningColor, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    // Wait for a random time between strikes
                    await Task.Delay(rnd.Next(10, 1000), token);

                    int strikePosition = rnd.Next(0, img.Width);
                    int strikeWidth = rnd.Next(2, 10);

                    // Flash 3-6 times rapidly
                    for (int flash = 0; flash < rnd.Next(3, 7); flash++)
                    {
                        // Flash ON
                        for (int i = 0; i < strikeWidth; i++)
                        {
                            int p = (strikePosition + i) % img.Width;
                            img.SetPixel(p, 0, lightningColor);
                        }
                        device.Update();
                        await Task.Delay(rnd.Next(10, 50), token);

                        // Flash OFF
                        for (int i = 0; i < img.Width; i++) img.SetPixel(i, 0, Color.Black);
                        device.Update();
                        await Task.Delay(rnd.Next(10, 50), token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Moves a meteor with a fading trail along the strip.</summary>
        /// <param name="device"></param>
        /// <param name="meteorColor"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task MeteorRain(Ws2812b device, Color meteorColor, int delayMs, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;
            int width = img.Width;
            _ledState = new Color[width]; // Initialize shadow buffer

            try
            {
                while (!token.IsCancellationRequested)
                {
                    for (int i = 0; i < width * 2; i++)
                    {
                        // 1. Fade the shadow buffer
                        for (int j = 0; j < width; j++)
                        {
                            // Use ScaleColor on the local array instead of GetPixel.
                            _ledState[j] = ScaleColor(_ledState[j], 0.75);
                            img.SetPixel(j, 0, _ledState[j]);
                        }

                        // 2. Draw meteor head into shadow buffer and image
                        if (i < width)
                        {
                            _ledState[i] = meteorColor;
                            img.SetPixel(i, 0, meteorColor);
                        }

                        device.Update();
                        await Task.Delay(delayMs, token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Flashes the whole strip in four quick bursts.</summary>
        /// <param name="device"></param>
        /// <param name="color"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task QuadStrobe(Ws2812b device, Color color, int delayMs, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;
            int count = img.Width;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    for (int burst = 0; burst < 4; burst++) // Four rapid bursts
                    {
                        for (int i = 0; i < count; i++) img.SetPixel(i, 0, color);
                        device.Update();
                        await Task.Delay(30, token); // Short "ON" time

                        for (int i = 0; i < count; i++) img.SetPixel(i, 0, Color.Black);
                        device.Update();
                        await Task.Delay(30, token); // Short "OFF" time
                    }
                    await Task.Delay(delayMs, token); // Longer pause between quad-bursts
                }
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Animates two heads crossing in opposite directions.</summary>
        /// <param name="device"></param>
        /// <param name="c1"></param>
        /// <param name="c2"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task DNAHelix(Ws2812b device, Color c1, Color c2, int delayMs, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;
            int count = img.Width;
            int pos = 0;
            _ledState = new Color[img.Width];

            try
            {
                while (!token.IsCancellationRequested)
                {
                    for (int i = 0; i < count; i++)
                    {
                        // Clear current frame in shadow buffer
                        _ledState[i] = Color.Black;

                        // Head 1 (Forward)
                        if (i == pos) _ledState[i] = c1;
                        // Head 2 (Reverse)
                        if (i == (count - 1 - pos))
                        {
                            // If they occupy the same pixel, blend them to White
                            if (_ledState[i] != Color.Black) _ledState[i] = Color.White;
                            else _ledState[i] = c2;
                        }

                        img.SetPixel(i, 0, _ledState[i]);
                    }

                    device.Update();
                    pos = (pos + 1) % count;
                    await Task.Delay(delayMs, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Scatters a few random-colored pixels on black.</summary>
        /// <param name="device"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task Confetti(Ws2812b device, int delayMs, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;
            int count = img.Width;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    // Start with a mostly black frame
                    for (int i = 0; i < count; i++) img.SetPixel(i, 0, Color.Black);

                    // Throw 3 random colored pixels
                    for (int i = 0; i < 3; i++)
                    {
                        int pos = rnd.Next(0, count);
                        Color randColor = GetWheelColor(rnd.Next(0, 255));
                        img.SetPixel(pos, 0, randColor);
                    }

                    device.Update();
                    await Task.Delay(delayMs, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }


        public async Task MarqueeAttract(Ws2812b device, int delayMs, Color color, CancellationToken token)
        {
            var img = device.Image;
            int count = img.Width;
            bool toggle = false;
            Stopwatch watch = new Stopwatch();
            watch.Start();

            try
            {

                while (watch.ElapsedMilliseconds < 10000)
                {
                    img.Clear();

                    for (int i = 0; i < count; i++)
                    {
                        if ((i % 2 == 0) == toggle)
                        {
                            img.SetPixel(i, 0, color);
                        }
                    }

                    device.Update();

                    toggle = !toggle;
                    await Task.Delay(150);
                }

                watch.Stop();
                watch.Reset();
                img.Clear();
                device.Update();
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Cycles a rainbow wave along the strip.</summary>
        /// <param name="device"></param>
        /// <param name="speedDelay"></param>
        /// <param name="waveLength"></param>
        /// <param name="cycls"></param>
        public async Task RainbowWaveAttract(Ws2812b device, int delayMs, CancellationToken token)
        {
            var img = device.Image;
            int count = img.Width;
            int offset = 0;
            Stopwatch watch = new Stopwatch();
            watch.Start();

            try
            {
                while (watch.ElapsedMilliseconds < 10000 && !token.IsCancellationRequested)
                {
                    for (int i = 0; i < count; i++)
                    {
                        // Give each pixel a color from its position plus a moving offset.
                        int colorPos = (i * (256 / count) + offset) & 255;
                        img.SetPixel(i, 0, GetWheelColor(colorPos));
                    }

                    device.Update();

                    // Increment offset to move the wave forward
                    offset = (offset + 1) & 255;

                    // Non-blocking wait
                    await Task.Delay(delayMs);
                }
                watch.Stop();
                watch.Reset();
                img.Clear();
                device.Update();
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Moves a comet with a fading tail along the strip.</summary>
        /// <param name="device"></param>
        /// <param name="color"></param>
        /// <param name="tailLength"></param>
        /// <param name="delayMs"></param>
        public async Task CometAttract(Ws2812b device, Color color, byte tailDecay, int delayMs, CancellationToken token)
        {
            var img = device.Image;
            int count = img.Width;
            Stopwatch watch = new Stopwatch();
            watch.Start();

            try
            {
                // Initialize the shadow buffer if needed
                if (_ledState == null || _ledState.Length != count) _ledState = new Color[count];

                int headPos = 0;
                while (watch.ElapsedMilliseconds < 10000)
                {
                    // 1. Fade the entire strip in the shadow buffer
                    for (int i = 0; i < count; i++)
                    {
                        // Lower scale factor (e.g. 0.7) = shorter tail, Higher (e.g. 0.9) = longer tail
                        _ledState[i] = ScaleColor(_ledState[i], 0.8);
                        img.SetPixel(i, 0, _ledState[i]);
                    }

                    // 2. Draw the bright Comet head
                    _ledState[headPos] = color;
                    img.SetPixel(headPos, 0, color);

                    // 3. Push to hardware
                    device.Update();

                    // 4. Move head
                    headPos = (headPos + 1) % count;

                    // Pause based on your delayMs speed
                    await Task.Delay(delayMs, token);
                }
                watch.Stop();
                watch.Reset();
                img.Clear();
                device.Update();
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Smoothly pulses one color's brightness up and down.</summary>
        /// <param name="device"></param>
        /// <param name="color"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task BreatheAttract(Ws2812b device, Color color, int speedDelay, CancellationToken token)
        {
            var img = device.Image;
            int count = img.Width;

            // One full breath is a 2*PI sine cycle.
            try
            {
                for (double i = 0; i < Math.PI * 2; i += 0.02)
                {
                    // Brightness eases 0 -> 1 -> 0 via a shifted sine.
                    double brightness = (Math.Sin(i - (Math.PI / 2)) + 1) / 2;

                    // Apply brightness to the color
                    Color pulsedColor = ScaleColor(color, brightness);

                    // Fill the strip
                    for (int p = 0; p < count; p++)
                    {
                        img.SetPixel(p, 0, pulsedColor);
                    }

                    device.Update();

                    // delayMs here acts as the 'breath rate'
                    await Task.Delay(speedDelay, token);
                }
                for (double i = 0; i < Math.PI * 2; i += 0.02)
                {
                    // Brightness eases 0 -> 1 -> 0 via a shifted sine.
                    double brightness = (Math.Sin(i - (Math.PI / 2)) + 1) / 2;

                    // Apply brightness to the color
                    Color pulsedColor = ScaleColor(color, brightness);

                    // Fill the strip
                    for (int p = 0; p < count; p++)
                    {
                        img.SetPixel(p, 0, pulsedColor);
                    }

                    device.Update();

                    // delayMs here acts as the 'breath rate'
                    await Task.Delay(speedDelay, token);
                }
                img.Clear();
                device.Update();
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Rapidly strobes the strip in yellow and blue.</summary>
        /// <param name="device"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task CrazyStrobeAttract(Ws2812b device, int delayMs, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;

            try
            {
                for (int z = 0; z < 3; z++)
                {
                    for (int j = 0; j < 30; j++)
                    {
                        for (int q = 0; q < 3; q++)
                        {
                            for (int i = 0; i < img.Width; i += 3)
                            {
                                if (i + q < img.Width) img.SetPixel(i + q, 0, Color.Yellow);
                            }
                            device.Update();


                            for (int i = 0; i < img.Width; i += 3)
                            {
                                if (i + q < img.Width) img.SetPixel(i + q, 0, Color.Blue);
                            }
                            device.Update();


                            for (int i = 0; i < img.Width; i += 3)
                            {
                                if (i + q < img.Width) img.SetPixel(i + q, 0, Color.Black);
                            }
                        }

                        await Task.Delay(delayMs, token);
                    }
                }
                img.Clear();
                device.Update();
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Randomly lights and fades single LEDs.</summary>
        /// <param name="device"></param>
        /// <param name="color"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task SparkleAttract(Ws2812b device, Color color, int delayMs, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;
            _ledState = new Color[img.Width];
            Stopwatch watch = new Stopwatch();
            watch.Start();

            try
            {
                while (watch.ElapsedMilliseconds < 10000)
                {
                    for (int i = 0; i < img.Width; i++)
                    {
                        _ledState[i] = ScaleColor(_ledState[i], 0.9); // Slow fade
                        img.SetPixel(i, 0, _ledState[i]);
                    }

                    // Add a random sparkle
                    int pos = rnd.Next(img.Width);
                    _ledState[pos] = color;
                    img.SetPixel(pos, 0, color);

                    device.Update();
                    await Task.Delay(delayMs, token);
                }
                watch.Stop();
                watch.Reset();
                img.Clear();
                device.Update();
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Sweeps a bright eye back and forth with a fading tail.</summary>
        /// <param name="device"></param>
        /// <param name="color"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task KnightRiderAttract(Ws2812b device, Color color, int delayMs, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;
            int count = img.Width;
            Stopwatch watch = new Stopwatch();
            watch.Start();

            try
            {

                if (_ledState == null || _ledState.Length != count) _ledState = new Color[count];

                int direction = 1; // 1 for right, -1 for left
                int position = 0;

                while (watch.ElapsedMilliseconds < 10000)
                {
                    // 1. Fade the entire strip (the tail)
                    for (int i = 0; i < count; i++)
                    {
                        _ledState[i] = ScaleColor(_ledState[i], 0.85); // Fade by 15%
                        img.SetPixel(i, 0, _ledState[i]);
                    }

                    // 2. Place the bright "eye"
                    _ledState[position] = color;
                    img.SetPixel(position, 0, color);

                    // 3. Push to hardware and wait
                    device.Update();
                    await Task.Delay(delayMs, token);

                    // 4. Move the position
                    position += direction;

                    // 5. Bounce off the ends
                    if (position == count - 1 || position == 0)
                    {
                        direction *= -1;
                    }
                }
                watch.Stop();
                watch.Reset();
                img.Clear();
                device.Update();
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Renders a plasma wave from sine-driven RGB values.</summary>
        /// <param name="device"></param>
        /// <param name="speed"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task PlasmaWaveAttract(Ws2812b device, int speed, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;
            Stopwatch watch = new Stopwatch();
            watch.Start();

            try
            {
                int count = img.Width;
                if (_ledState == null || _ledState.Length != count) _ledState = new Color[count];
                int time = 0;

                while (watch.ElapsedMilliseconds < 10000)
                {
                    for (int i = 0; i < count; i++)
                    {
                        // Use time and pixel position to calculate a unique RGB value via sine waves
                        int r = (int)(Math.Sin((i + time) * 0.1) * 127 + 128);
                        int g = (int)(Math.Sin((i + time) * 0.05) * 127 + 128);
                        int b = (int)(Math.Sin((i + time) * 0.15) * 127 + 128);

                        _ledState[i] = Color.FromArgb(r, g, b);
                        img.SetPixel(i, 0, _ledState[i]);
                    }

                    device.Update();
                    await Task.Delay(speed, token);
                    time++; // Move the wave slightly for the next frame
                }
                watch.Stop();
                watch.Reset();
                img.Clear();
                device.Update();
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Bounces several colored balls along the strip.</summary>
        /// <param name="device"></param>
        /// <param name="ballColors"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task BouncingBallsAttract(Ws2812b device, Color[] ballColors, int delayMs, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;
            int count = img.Width;
            float[] pos = new float[ballColors.Length];
            float[] vel = new float[ballColors.Length];
            Stopwatch watch = new Stopwatch();
            watch.Start();

            try
            {

                // Initialize random positions and speeds
                for (int i = 0; i < ballColors.Length; i++)
                {
                    pos[i] = rnd.Next(count);
                    vel[i] = (float)(rnd.NextDouble() * 0.5 + 0.2);
                }
                while (watch.ElapsedMilliseconds < 10000)
                {
                    // Clear frame
                    for (int i = 0; i < count; i++) img.SetPixel(i, 0, Color.Black);

                    for (int i = 0; i < ballColors.Length; i++)
                    {
                        pos[i] += vel[i];
                        // Bounce off walls
                        if (pos[i] >= count - 1 || pos[i] <= 0) vel[i] *= -1;

                        int p = (int)Math.Clamp(pos[i], 0, count - 1);
                        img.SetPixel(p, 0, ballColors[i]);
                    }

                    device.Update();
                    await Task.Delay(delayMs, token);
                }
                watch.Stop();
                watch.Reset();
                img.Clear();
                device.Update();
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Randomly flashes sections of the strip like lightning.</summary>
        /// <param name="device"></param>
        /// <param name="lightningColor"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task LightningAttract(Ws2812b device, Color lightningColor, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;
            Stopwatch watch = new Stopwatch();
            watch.Start();

            try
            {
                while (watch.ElapsedMilliseconds < 10000)
                {
                    // Wait for a random time between strikes
                    await Task.Delay(rnd.Next(10, 1000));

                    int strikePosition = rnd.Next(0, img.Width);
                    int strikeWidth = rnd.Next(2, 10);

                    // Flash 3-6 times rapidly
                    for (int flash = 0; flash < rnd.Next(3, 7); flash++)
                    {
                        // Flash ON
                        for (int i = 0; i < strikeWidth; i++)
                        {
                            int p = (strikePosition + i) % img.Width;
                            img.SetPixel(p, 0, lightningColor);
                        }
                        device.Update();
                        await Task.Delay(rnd.Next(10, 50), token);

                        // Flash OFF
                        for (int i = 0; i < img.Width; i++) img.SetPixel(i, 0, Color.Black);
                        device.Update();
                        await Task.Delay(rnd.Next(10, 50));
                    }
                }
                watch.Stop();
                watch.Reset();
                img.Clear();
                device.Update();
            }

            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Moves a meteor with a fading trail along the strip.</summary>
        /// <param name="device"></param>
        /// <param name="meteorColor"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task MeteorRainAttract(Ws2812b device, Color meteorColor, int delayMs, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;
            int width = img.Width;
            _ledState = new Color[width]; // Initialize shadow buffer

            try
            {
                for (int i = 0; i < width * 2; i++)
                {
                    // 1. Fade the shadow buffer
                    for (int j = 0; j < width; j++)
                    {
                        // Use ScaleColor on the local array instead of GetPixel.
                        _ledState[j] = ScaleColor(_ledState[j], 0.75);
                        img.SetPixel(j, 0, _ledState[j]);
                    }

                    // 2. Draw meteor head into shadow buffer and image
                    if (i < width)
                    {
                        _ledState[i] = meteorColor;
                        img.SetPixel(i, 0, meteorColor);
                    }

                    device.Update();
                    await Task.Delay(delayMs, token);
                }
                img.Clear();
                device.Update();
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Flashes the whole strip in four quick bursts.</summary>
        /// <param name="device"></param>
        /// <param name="color"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task QuadStrobeAttract(Ws2812b device, Color color, int delayMs, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;
            int count = img.Width;
            Stopwatch watch = new Stopwatch();
            watch.Start();

            try
            {
                while (watch.ElapsedMilliseconds < 10000 && !token.IsCancellationRequested)
                {
                    for (int burst = 0; burst < 4; burst++) // Four rapid bursts
                    {
                        for (int i = 0; i < count; i++) img.SetPixel(i, 0, color);
                        device.Update();
                        await Task.Delay(30); // Short "ON" time

                        for (int i = 0; i < count; i++) img.SetPixel(i, 0, Color.Black);
                        device.Update();
                        await Task.Delay(30); // Short "OFF" time
                    }
                    await Task.Delay(delayMs); // Longer pause between quad-bursts
                }
                watch.Stop();
                watch.Reset();
                img.Clear();
                device.Update();
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Animates two heads crossing in opposite directions.</summary>
        /// <param name="device"></param>
        /// <param name="c1"></param>
        /// <param name="c2"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task DNAHelixAttract(Ws2812b device, Color c1, Color c2, int delayMs, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;
            int count = img.Width;
            int pos = 0;
            _ledState = new Color[img.Width];
            Stopwatch watch = new Stopwatch();
            watch.Start();

            try
            {
                while (watch.ElapsedMilliseconds < 10000)
                {
                    for (int i = 0; i < count; i++)
                    {
                        // Clear current frame in shadow buffer
                        _ledState[i] = Color.Black;

                        // Head 1 (Forward)
                        if (i == pos) _ledState[i] = c1;
                        // Head 2 (Reverse)
                        if (i == (count - 1 - pos))
                        {
                            // If they occupy the same pixel, blend them to White
                            if (_ledState[i] != Color.Black) _ledState[i] = Color.White;
                            else _ledState[i] = c2;
                        }

                        img.SetPixel(i, 0, _ledState[i]);
                    }

                    device.Update();
                    pos = (pos + 1) % count;
                    await Task.Delay(delayMs, token);
                }
                watch.Stop();
                watch.Reset();
                img.Clear();
                device.Update();
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        /// <summary>Scatters a few random-colored pixels on black.</summary>
        /// <param name="device"></param>
        /// <param name="delayMs"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task ConfettiAttract(Ws2812b device, int delayMs, CancellationToken token)
        {
            RawPixelContainer img = device.Image as RawPixelContainer;
            int count = img.Width;
            Stopwatch watch = new Stopwatch();
            watch.Start();

            try
            {
                while (watch.ElapsedMilliseconds < 10000)
                {
                    // Start with a mostly black frame
                    for (int i = 0; i < count; i++) img.SetPixel(i, 0, Color.Black);

                    // Throw 3 random colored pixels
                    for (int i = 0; i < 3; i++)
                    {
                        int pos = rnd.Next(0, count);
                        Color randColor = GetWheelColor(rnd.Next(0, 255));
                        img.SetPixel(pos, 0, randColor);
                    }

                    device.Update();
                    await Task.Delay(delayMs, token);
                }
                watch.Stop();
                watch.Reset();
                img.Clear();
                device.Update();
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }

        #endregion Light Patterns

        #region Light Helpers

        /// <summary>
        /// Generates an RGB color based on the specified position on a color wheel.
        /// </summary>
        /// <param name="pos">The position on the color wheel, typically in the range 0 to 255.</param>
        /// <returns>A Color representing the RGB value at the given wheel position.</returns>
        public Color GetWheelColor(int pos)
        {
            if (pos < 85) return Color.FromArgb(pos * 3, 255 - pos * 3, 0);
            if (pos < 170) { pos -= 85; return Color.FromArgb(255 - pos * 3, 0, pos * 3); }
            pos -= 170; return Color.FromArgb(0, pos * 3, 255 - pos * 3);
        }

        /// <summary>
        /// Scales the brightness of a color by a specified factor.
        /// </summary>
        /// <param name="color">The color to scale.</param>
        /// <param name="factor">The factor by which to scale the color's brightness, between 0.0 and 1.0.</param>
        /// <returns>A new color with its RGB components scaled by the given factor.</returns>
        public Color ScaleColor(Color color, double factor)
        {
            // Ensure the factor stays between 0.0 (off) and 1.0 (full brightness)
            factor = Math.Max(0.0, Math.Min(1.0, factor));

            return Color.FromArgb(
                (int)(color.R * factor),
                (int)(color.G * factor),
                (int)(color.B * factor)
            );
        }

        public void BrightnessUP(Ws2812b device, Color color)
        {
            var img = device.Image;

            if (color == Color.Red)
            {
                brightnessRed[0] = brightnessRed[0] + 10;
                if (brightnessRed[0] > 255)
                {
                    brightnessRed[0] = 255;
                }
                img.Clear(Color.FromArgb(brightnessRed[0], 0, 0));
                device.Update();
            }
            else if (color == Color.Green)
            {
                brightnessGreen[1] = brightnessGreen[1] + 10;
                if (brightnessGreen[1] > 255)
                {
                    brightnessGreen[1] = 255;
                }
                img.Clear(Color.FromArgb(0, brightnessGreen[1], 0));
                device.Update();
            }
            else if (color == Color.Blue)
            {
                brightnessBlue[2] = brightnessBlue[2] + 10;
                if (brightnessBlue[2] > 255)
                {
                    brightnessBlue[2] = 255;
                }
                img.Clear(Color.FromArgb(0, 0, brightnessBlue[2]));
                device.Update();
            }
            else if (color == Color.Yellow)
            {
                brightnessYellow[0] = brightnessYellow[0] + 10;
                brightnessYellow[1] = brightnessYellow[1] + 10;
                if (brightnessYellow[0] > 255)
                {
                    brightnessYellow[0] = 255;
                }
                if (brightnessYellow[1] > 255)
                {
                    brightnessYellow[1] = 255;
                }
                img.Clear(Color.FromArgb(brightnessYellow[0], brightnessYellow[1], 0));
                device.Update();
            }
            else if (color == Color.Purple)
            {
                brightnessPurple[0] = brightnessPurple[0] + 10;
                brightnessPurple[2] = brightnessPurple[2] + 10;
                if (brightnessPurple[0] > 128)
                {
                    brightnessPurple[0] = 128;
                }
                if (brightnessPurple[2] > 128)
                {
                    brightnessPurple[2] = 128;
                }
                img.Clear(Color.FromArgb(brightnessPurple[0], 0, brightnessPurple[2]));
                device.Update();

            }

            else
            {
                brightnessWhite[0] = brightnessWhite[0] + 10;
                if (brightnessWhite[0] > 255)
                {
                    brightnessWhite[0] = 255;
                }

                img.Clear(Color.FromArgb(brightnessWhite[0], brightnessWhite[0], brightnessWhite[0]));
                device.Update();
            }
        }

        public void BrightnessDown(Ws2812b device, Color color)
        {

            var img = device.Image;

            if (color == Color.Red)
            {
                brightnessRed[0] = brightnessRed[0] - 10;
                if (brightnessRed[0] < 0)
                {
                    brightnessRed[0] = 0;
                }
                img.Clear(Color.FromArgb(brightnessRed[0], 0, 0));
                device.Update();
            }
            else if (color == Color.Green)
            {
                brightnessGreen[1] = brightnessGreen[1] - 10;
                if (brightnessGreen[1] < 0)
                {
                    brightnessGreen[1] = 0;
                }
                img.Clear(Color.FromArgb(0, brightnessGreen[1], 0));
                device.Update();
            }
            else if (color == Color.Blue)
            {
                brightnessBlue[2] = brightnessBlue[2] - 10;
                if (brightnessBlue[2] < 0)
                {
                    brightnessBlue[2] = 0   ;
                }
                img.Clear(Color.FromArgb(0, 0, brightnessBlue[2]));
                device.Update();
            }
            else if (color == Color.Yellow)
            {
                brightnessYellow[0] = brightnessYellow[0] - 10;
                brightnessYellow[1] = brightnessYellow[1] - 10;
                if (brightnessYellow[0] < 0)
                {
                    brightnessYellow[0] = 0;
                }
                if (brightnessYellow[1] < 0)
                {
                    brightnessYellow[1] = 0;
                }
                img.Clear(Color.FromArgb(brightnessYellow[0], brightnessYellow[1], 0));
                device.Update();
            }
            else if (color == Color.Purple)
            {
                brightnessPurple[0] = brightnessPurple[0] - 10;
                brightnessPurple[2] = brightnessPurple[2] - 10;
                if (brightnessPurple[0] < 0)
                {
                    brightnessPurple[0] = 0;
                }
                if (brightnessPurple[2] < 0)
                {
                    brightnessPurple[2] = 0;
                }
                img.Clear(Color.FromArgb(brightnessPurple[0], 0, brightnessPurple[2]));
                device.Update();

            }

            else
            {
                brightnessWhite[0] = brightnessWhite[0] - 10;
                if (brightnessWhite[0] < 0)
                {
                    brightnessWhite[0] = 0;
                }

                img.Clear(Color.FromArgb(brightnessWhite[0], brightnessWhite[0], brightnessWhite[0]));
                device.Update();
            }
        }

        void Fill(Ws2812b strip, int count, Color c)
        {
            for (int i = 0; i < count; i++) strip.Image.SetPixel(i, 0, c);
            strip.Update();
        }

        Color HsvToRgb(double h, double s, double v)
        {
            int hi = (int)Math.Floor(h / 60) % 6;
            double f = h / 60 - Math.Floor(h / 60);
            int v_val = (int)(v * 255), p = (int)(v * 255 * (1 - s)), q = (int)(v * 255 * (1 - f * s)), t = (int)(v * 255 * (1 - (1 - f) * s));
            return hi switch
            {
                0 => Color.FromArgb(v_val, t, p),
                1 => Color.FromArgb(q, v_val, p),
                2 => Color.FromArgb(p, v_val, t),
                3 => Color.FromArgb(p, q, v_val),
                4 => Color.FromArgb(t, p, v_val),
                _ => Color.FromArgb(v_val, p, q)
            };
        }

        #endregion Light Helpers

    }
}
