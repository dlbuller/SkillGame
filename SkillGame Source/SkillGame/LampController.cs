using System;
using System.Collections.Generic;
using System.Device.Gpio;
using System.Threading;
using System.Threading.Tasks;

namespace SkillGame
{
    /// <summary>Owns every lamp and solenoid output on GPIO3/GPIO4 and runs the score-lamp animations; pure hardware output with no UI or game rules.</summary>
    public class LampController : ILampSink
    {
        private readonly GpioController? _g3;   // null when the boards aren't connected (preview mode)
        private readonly GpioController? _g4;
        private readonly int[] _g3Out;
        private readonly int[] _g4Out;

        // The score lamps in playfield order: 10..90 (GPIO4 pins 4–12), then 100..400 (GPIO3 pins 10–13).
        private static readonly (int board, int pin)[] ScoreSeq =
        {
            (4, 4), (4, 5), (4, 6), (4, 7), (4, 8), (4, 9), (4, 10), (4, 11), (4, 12),
            (3, 10), (3, 11), (3, 12), (3, 13),
        };

        // One animation at a time; starting a new one cancels the previous.
        private int _lastScore = 0;
        private CancellationTokenSource _animCts = new CancellationTokenSource();
        /// <summary>Delay between count-up steps in milliseconds; lower is faster.</summary>
        public int CountUpStepMs = 60;

        // Named lamp/solenoid -> (board, pin); public so the I/O Map and pin-activity reporting can resolve a name to its FT232H pin.
        public static readonly Dictionary<string, (int board, int pin)> Outputs = new()
        {
            ["10"] = (4, 4),   ["20"] = (4, 5),   ["30"] = (4, 6),   ["40"] = (4, 7),
            ["50"] = (4, 8),   ["60"] = (4, 9),   ["70"] = (4, 10),  ["80"] = (4, 11),  ["90"] = (4, 12),
            ["100"] = (3, 10), ["200"] = (3, 11), ["300"] = (3, 12), ["400"] = (3, 13),
            ["GameOver"] = (3, 14), ["Winner"] = (3, 15),
            ["Tilt"] = (4, 13),
            ["WinnerLock"] = (3, 9), ["CoinLock"] = (3, 8),
        };

        public LampController(GpioController? gpio3, GpioController? gpio4, int[] gpio3Out, int[] gpio4Out)
        {
            _g3 = gpio3; _g4 = gpio4; _g3Out = gpio3Out; _g4Out = gpio4Out;
        }

        private GpioController? Ctrl(int board) => board == 3 ? _g3 : _g4;

        // Every physical pin write goes through here so the I/O Activity Map always reflects what is driven, even when no boards are connected.
        private void Drive(int board, int pin, bool on)
        {
            var c = Ctrl(board);
            // The FT232H MPSSE channel isn't thread-safe, so serialize every access on the controller since board 3 is also polled for inputs on a background thread.
            // A write to an unplugged/disposed board must never take down the caller (UI thread) — swallow it; the on-screen map still updates below.
            if (c != null) { try { lock (c) { c.Write(pin, on ? PinValue.High : PinValue.Low); } } catch { } }
            PinActivity.Set(board, pin, on);
        }

        private void Set((int board, int pin) loc, bool on) => Drive(loc.board, loc.pin, on);
        private void ClearScoreLamps() { foreach (var loc in ScoreSeq) Set(loc, false); }

        // Cancel whatever animation is running and hand back a fresh token for the new one.
        private CancellationToken NewAnim()
        {
            _animCts.Cancel();
            _animCts = new CancellationTokenSource();
            return _animCts.Token;
        }

        // ---------------------------------------------------------------- score count-up

        /// <summary>Animate the score lamps up to a new total like an EM score reel; value 0 clears everything instantly.</summary>
        public async void TallyScore(int value)
        {
            CancellationToken token = NewAnim();

            if (value <= 0)
            {
                _lastScore = 0;
                SetScoreLamps(0);   // clears all lamps + solenoids
                return;
            }

            int from = _lastScore;
            _lastScore = value;

            try
            {
                for (int v = from + 10; v < value; v += 10)
                {
                    SetScoreLamps(v);                        // light this step, clear the previous
                    await Task.Delay(CountUpStepMs, token);
                }
            }
            catch (OperationCanceledException)
            {
                return;   // a newer animation took over
            }

            SetScoreLamps(value);   // rest on the final total
        }

        /// <summary>Instantly light the tens (10–90) and hundreds (100–400) lamps for a single score value.</summary>
        private void SetScoreLamps(int value)
        {
            if (value == 0)
            {
                ClearAll();
                return;
            }

            int tensDigit = (value / 10) % 10;   // 0..9
            int tensPin = 3 + tensDigit;          // digit 1 -> pin 4 (10pt) ... digit 9 -> pin 12 (90pt)
            foreach (int pin in _g4Out)
                Drive(4, pin, tensDigit >= 1 && pin == tensPin);

            if (value >= 100)
            {
                int hundreds = Math.Min(value / 100, 4);   // 1..4
                int hundredsPin = 9 + hundreds;            // 1 -> pin 10 (100pt) ... 4 -> pin 13 (400pt)
                for (int pin = 10; pin <= 13; pin++)
                    Drive(3, pin, pin == hundredsPin);
            }
        }

        // ---------------------------------------------------------------- attract patterns

        /// <summary>Stop any running lamp animation and turn the score lamps off.</summary>
        public void StopLampPattern()
        {
            _animCts.Cancel();
            _animCts = new CancellationTokenSource();
            ClearScoreLamps();
        }

        /// <summary>A single lit lamp chases 10 → 400 and repeats.</summary>
        public async void ChasePattern(int stepMs = 90)
        {
            CancellationToken token = NewAnim();
            try
            {
                int i = 0;
                while (!token.IsCancellationRequested)
                {
                    for (int k = 0; k < ScoreSeq.Length; k++) Set(ScoreSeq[k], k == i);
                    i = (i + 1) % ScoreSeq.Length;
                    await Task.Delay(stepMs, token);
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>Lamps bloom outward from the centre of the backglass and shrink back, repeating, matching the physical row layout.</summary>
        public async void InsideOutPattern(int stepMs = 120)
        {
            CancellationToken token = NewAnim();
            try
            {
                while (!token.IsCancellationRequested)
                {
                    for (int d = 0; d <= 4 && !token.IsCancellationRequested; d++) { SetBloom(d); await Task.Delay(stepMs, token); }
                    for (int d = 3; d >= 0 && !token.IsCancellationRequested; d--) { SetBloom(d); await Task.Delay(stepMs, token); }
                }
            }
            catch (OperationCanceledException) { }
        }

        // One frame of the inside-out bloom at radius d (0 = just the centre lamps).
        private void SetBloom(int d)
        {
            for (int k = 0; k <= 8; k++) Set(ScoreSeq[k], Math.Abs(k - 4) <= d);   // tens 10-90 bloom from 50 (index 4)
            Set(ScoreSeq[10], true);      // 200 - upper-row centre, always lit
            Set(ScoreSeq[11], true);      // 300 - upper-row centre, always lit
            Set(ScoreSeq[9], d >= 1);     // 100
            Set(ScoreSeq[12], d >= 1);    // 400
        }

        /// <summary>All score lamps flash on and off together.</summary>
        public async void FlashAll(int stepMs = 350)
        {
            CancellationToken token = NewAnim();
            try
            {
                bool on = true;
                while (!token.IsCancellationRequested)
                {
                    foreach (var loc in ScoreSeq) Set(loc, on);
                    on = !on;
                    await Task.Delay(stepMs, token);
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>A single lit lamp sweeps left-to-right along the tens row (10 -> 90) then bounces
        /// back (90 -> 10), repeating - a scanner across the full width of the backglass.</summary>
        // Full end-to-end sweep order: every score lamp (10..400) plus the three specials,
        // so "sweep" walks a single lit lamp across the whole board and back.
        private static readonly (int board, int pin)[] SweepSeq =
        {
            (4, 4), (4, 5), (4, 6), (4, 7), (4, 8), (4, 9), (4, 10), (4, 11), (4, 12),   // 10..90
            (3, 10), (3, 11), (3, 12), (3, 13),                                           // 100..400
            (3, 15), (3, 14), (4, 13),                                                    // Winner, Game Over, Tilt
        };

        public async void SweepBounce(int stepMs = 90)
        {
            CancellationToken token = NewAnim();
            int n = SweepSeq.Length;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    for (int i = 0; i < n && !token.IsCancellationRequested; i++) { LightOne(i); await Task.Delay(stepMs, token); }        // 10 -> Tilt
                    for (int i = n - 2; i >= 1 && !token.IsCancellationRequested; i--) { LightOne(i); await Task.Delay(stepMs, token); }   // bounce back
                }
            }
            catch (OperationCanceledException) { }
        }

        // Light exactly one lamp in the full sweep sequence; every other sweep lamp off.
        private void LightOne(int lit)
        {
            for (int k = 0; k < SweepSeq.Length; k++) Set(SweepSeq[k], k == lit);
        }

        /// <summary>The tens row fills up 10 -> 90 (each stays lit), then the hundreds reels tick over
        /// 100 -> 400 like an EM score reel; hold, clear, and repeat.</summary>
        public async void Cascade(int stepMs = 130)
        {
            CancellationToken token = NewAnim();
            try
            {
                while (!token.IsCancellationRequested)
                {
                    for (int i = 0; i <= 8 && !token.IsCancellationRequested; i++)
                    {
                        for (int k = 0; k <= 8; k++) Set(ScoreSeq[k], k <= i);   // cumulative fill of the tens
                        await Task.Delay(stepMs, token);
                    }
                    for (int h = 9; h <= 12 && !token.IsCancellationRequested; h++)
                    {
                        Set(ScoreSeq[h], true);                                   // hundreds tick over, tens stay full
                        await Task.Delay(stepMs * 2, token);
                    }
                    await Task.Delay(stepMs * 4, token);                          // hold the full board
                    ClearScoreLamps();
                    await Task.Delay(stepMs, token);
                }
            }
            catch (OperationCanceledException) { }
        }

        // ---------------------------------------------------------------- direct lamp control

        /// <summary>Turn a named lamp/solenoid on or off; "All" lights every lamp (not solenoids) or clears everything.</summary>
        public void SetLamp(string lamp, bool on)
        {
            if (lamp == "All")
            {
                if (on)
                {
                    foreach (int pin in _g4Out) Drive(4, pin, true);
                    for (int pin = 10; pin <= 15; pin++) Drive(3, pin, true);
                }
                else
                {
                    ClearAll();
                }
                return;
            }

            if (Outputs.TryGetValue(lamp, out var loc))
            {
                Drive(loc.board, loc.pin, on);
            }
        }

        /// <summary>Winner LED + Game-Over LED. The winning coin is held as proof by the Win-Lock's spring-extended
        /// plunger — no power needed — and is released by a pulse on the next coin-up, so the coil is never energized here.</summary>
        public void Winner()
        {
            Drive(3, 15, true); // Winner LED (stays on)
            Drive(3, 14, true); // Game Over LED (stays on)
        }

        /// <summary>Fire a lock coil as a brief pulse: energize to retract (drop the coin), then release so the
        /// spring pushes the plunger back out. These are intermittent-duty solenoids — they must never be held on.
        /// startDelayMs offsets the pulse so two coils fired together (e.g. on a post-win coin-up) never overlap.</summary>
        public async void PulseSolenoid(string name, int ms = 250, int startDelayMs = 0)
        {
            if (!Outputs.TryGetValue(name, out var loc)) return;
            try { if (startDelayMs > 0) await System.Threading.Tasks.Task.Delay(startDelayMs); } catch { }
            Drive(loc.board, loc.pin, true);
            try { await System.Threading.Tasks.Task.Delay(ms); } catch { }
            Drive(loc.board, loc.pin, false);
        }

        /// <summary>Turn off every lamp and solenoid on both output boards.</summary>
        public void ClearAll()
        {
            foreach (int pin in _g4Out) Drive(4, pin, false);
            foreach (int pin in _g3Out) Drive(3, pin, false);
        }

        /// <summary>A short celebration on the score lamps for a big hit, settling back onto <paramref name="finalScore"/>.</summary>
        public async void Flourish(int finalScore)
        {
            CancellationToken token = NewAnim();
            _lastScore = finalScore;   // so the next real count-up starts from the right place
            try
            {
                for (int i = 0; i < 6 && !token.IsCancellationRequested; i++)   // rapid all-flash
                {
                    foreach (var loc in ScoreSeq) Set(loc, i % 2 == 0);
                    await Task.Delay(80, token);
                }
                for (int rep = 0; rep < 2 && !token.IsCancellationRequested; rep++)   // two fast chases
                    for (int i = 0; i < ScoreSeq.Length && !token.IsCancellationRequested; i++)
                    {
                        for (int k = 0; k < ScoreSeq.Length; k++) Set(ScoreSeq[k], k == i);
                        await Task.Delay(35, token);
                    }
            }
            catch (OperationCanceledException) { return; }   // a newer animation / the next hit took over
            SetScoreLamps(finalScore);   // back to the real score
        }
    }
}
