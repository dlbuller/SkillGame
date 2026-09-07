using System;
using System.Collections.Generic;
using System.Device.Gpio;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Iot.Device.FtCommon;
using SkillGame;

namespace SkillGameWpf
{
    /// <summary>Owns the FT232H boards, builds the audio/lamp/LED sinks, polls switches, and drives the game engine.</summary>
    public sealed class HardwareCoordinator
    {
        private readonly GameStatusView _game;
        private readonly DiagnosticsView _diag;
        private readonly Action<bool[]> _onHardwareState;
        private readonly Dispatcher _ui;

        private readonly FtHardware _hardware = new();
        private readonly AudioFunctions _audio = AppState.Audio;
        private LampController _lamps = null!;
        private LedEffects? _led;
        private GameEngine? _engine;

        private readonly CancellationTokenSource _cts = new();
        private readonly List<string>[] _last = { new(), new(), new() };

        // Always-on stuck detection (pure logic in StuckMonitor) published to the thread-safe Faults store so every
        // screen reads one truth. The monitor is touched on the poll thread + the UI timer, so guard it with this lock.
        private readonly object _faultLock = new();
        private readonly StuckMonitor _stuckMon = new();
        private DispatcherTimer? _faultTimer;

        // A real coin is momentary; a floating/held-high coin (or a floating-bus of many highs) must not start a game.
        private readonly CoinGate _coinGate = new();

        private long _pollCycles;                             // one per board per poll pass (for a live rate readout)
        private readonly long[] _lastSeenTicks = new long[4]; // last successful read per board (index 0..3 = board 1..4)
        /// <summary>Total switch-poll passes across the input boards (sample over time for a Hz rate).</summary>
        public long PollCycles => System.Threading.Interlocked.Read(ref _pollCycles);
        /// <summary>Milliseconds since board N (1-based) was last read; -1 if never (board 4 is output-only).</summary>
        public long LastSeenAgoMs(int board) { long t = _lastSeenTicks[board - 1]; return t == 0 ? -1 : Environment.TickCount64 - t; }

        private readonly Random _rnd = new();
        private bool _distractActive;   // a distract show is running until the next switch is hit
        private DispatcherTimer? _attractLightsTimer;
        private DispatcherTimer? _attractSoundTimer;
        private static readonly string[] AttractPatterns =
            { "Marquee", "Rainbow Wave", "Comet", "Breathe", "Crazy Strobe", "Plasma Wave", "Meteor Rain",
              "Sparkle", "Knight Rider", "Bouncing Balls", "Lightning", "Confetti", "DNA Helix", "Quad Strobe" };
        private static readonly string[] DistractPatterns =
            { "Marquee", "Breathe", "Crazy Strobe", "Sparkle", "Lightning", "Confetti", "Quad Strobe" };

        /// <summary>"game" or "diag" — which screen is up, so switch changes are routed correctly.</summary>
        private string _mode = "game";
        public string Mode
        {
            get => _mode;
            set { _mode = value; if (value != "game") StopAttractShow(); }   // attract only runs on the Game Status screen
        }

        // Kill any attract/distract lights + music the moment we leave the Game Status screen.
        private void StopAttractShow()
        {
            _distractActive = false;
            try { _led?.CancelLights(); } catch { }
            try { _lamps?.StopLampPattern(); } catch { }
            Task.Run(() => { try { _audio.StopMusicClip(); } catch { } });
        }

        /// <summary>When set (by the Wiring Test), every switch change routes here exclusively.</summary>
        public Action<SwitchDef, bool>? SwitchObserver;

        /// <summary>True after a fully successful hardware init.</summary>
        public bool Ready { get; private set; }
        private bool _wasReady;              // we successfully opened the boards at least once
        private volatile bool _degraded;     // a board dropped; waiting for it to come back
        private volatile bool _recovering;   // a re-open is in progress

        public LampController Lamps => _lamps;

        public HardwareCoordinator(GameStatusView game, DiagnosticsView diag, Action<bool[]> onHardwareState, Dispatcher ui)
        {
            _game = game; _diag = diag; _onHardwareState = onHardwareState; _ui = ui;
        }

        /// <summary>Enumerate + open the boards off the UI thread, then finish wiring on the UI thread.</summary>
        public void Initialize()
        {
            _audio.PlayStartupSound();   // play it immediately on load, not after the (slow) board enumeration
            Task.Run(() =>
            {
                bool ready = false;
                try { _hardware.Initialize(); ready = true; }
                catch (Exception ex) { Log.Error("FT232H init failed (running in preview mode)", ex); }
                _ui.Invoke(() => FinishInit(ready));
            });
        }

        private void FinishInit(bool ready)
        {
            Ready = ready;

            // Build the lamp controller; with no boards its writes just mirror onto the I/O map.
            _lamps = new LampController(_hardware.ControllerGPIO3, _hardware.ControllerGPIO4,
                                       _hardware.PinsGPIO3Output, _hardware.PinsGPIO4Output);
            _lamps.CountUpStepMs = AppState.Settings.CountUpStepMs;
            AppState.ApplySettings = ApplyOperatorSettings;   // Settings screen pushes live edits here
            AppState.ResyncInputs = ResyncInputs;             // Bench Mode off re-baselines the poll so inputs re-report
            _diag.AttachHardware(this, ready);
            _diag.SetBoardPresence(GetBoardPresence(), ready);   // colour G1-G4 green/red immediately

            if (ready)
            {
                _led = new LedEffects(_hardware.Neo);
                _engine = new GameEngine(_game, new DeferredScoreSink(_audio), _lamps, AppState.Audits, _led)
                { TiltEnabled = AppState.Settings.TiltEnabled, TiltsAllowed = AppState.Settings.TiltsAllowed };
                _game.AttachEngine(_engine);
                _wasReady = true;
                StartPolling();
                _ = RunPostAsync();
                Log.Info("FT232H hardware ready; switch polling started.");
            }

            _onHardwareState(GetBoardPresence());
            StartAttractTimers();
            StartPresenceWatch();

            AppState.BenchModeChanged += OnBenchChanged;
            _faultTimer = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromSeconds(1) };
            _faultTimer.Tick += (s, e) => UpdateFaults();
            _faultTimer.Start();
        }

        // Recompute the stuck-switch fault set once a second (UI thread) and publish it if anything changed.
        private void UpdateFaults()
        {
            List<string> newStuck;
            lock (_faultLock)
            {
                if (AppState.Settings.BenchMode) { _stuckMon.Clear(); newStuck = new List<string>(); }
                else newStuck = _stuckMon.StuckAt(Environment.TickCount64);
            }
            if (Faults.SetStuck(newStuck) && newStuck.Count > 0)
                StopDistract();   // a stuck switch kills any distract show (lights + sound)
        }

        // Bench Mode on: wipe stuck tracking so a bench run starts clean. Off: re-baseline the poll so real
        // stuck/floating switches fire a fresh edge and get re-detected.
        private void OnBenchChanged(bool on)
        {
            if (on)
            {
                lock (_faultLock) _stuckMon.Clear();
                // Clear any stale input highs so the switch monitor + stuck detector don't keep seeing them in bench.
                foreach (var sw in SwitchMap.All) PinActivity.Set(sw.Board, sw.Pin, false);
                Faults.SetStuck(System.Array.Empty<string>());
            }
            else ResyncInputs();
        }

        // Poll board presence so the Diagnostics pills + sidebar update live if a board is unplugged/replugged.
        private DispatcherTimer? _presenceTimer;
        private void StartPresenceWatch()
        {
            _presenceTimer = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromSeconds(3) };
            _presenceTimer.Tick += (s, e) =>
            {
                // GetDevices() enumerates USB and can take 100ms+, so run it off the UI thread.
                Task.Run(() =>
                {
                    var present = GetBoardPresence();
                    bool all = present[0] && present[1] && present[2] && present[3];
                    if (!all && _wasReady) _degraded = true;   // something's gone; remember to re-open it
                    _ui.BeginInvoke(new Action(() =>
                    {
                        _diag.SetBoardPresence(present, all);
                        _onHardwareState(present);
                    }));
                    // boards are all physically back but our handles are stale — re-open them
                    if (all && _wasReady && _degraded && !_recovering) _ = RecoverAsync();
                });
            };
            _presenceTimer.Start();
        }

        // Hot-replug recovery: re-open every board's handle, then rebuild the lamp/LED/engine wiring so outputs work
        // again. The poll tasks read the fresh handles on their own, so switch input resumes without a restart.
        private async Task RecoverAsync()
        {
            if (_recovering) return;
            _recovering = true;
            try
            {
                bool ok = await Task.Run(() =>
                {
                    try { _hardware.Reinitialize(); return true; }
                    catch (Exception ex) { Log.Error("hardware recovery: re-open failed (will retry)", ex); return false; }
                });
                if (!ok) return;   // board dropped again mid-recovery; the next presence tick retries
                _ui.Invoke(RewireAfterRecovery);
                _degraded = false;
                Log.Info("FT232H hardware recovered — boards re-opened.");
            }
            catch (Exception ex) { Log.Error("hardware recovery failed", ex); }
            finally { _recovering = false; }
        }

        // Rebuild everything that captured the old (now-disposed) board handles, exactly like the ready path of init.
        private void RewireAfterRecovery()
        {
            try { _lamps?.StopLampPattern(); } catch { }
            _lamps = new LampController(_hardware.ControllerGPIO3, _hardware.ControllerGPIO4,
                                       _hardware.PinsGPIO3Output, _hardware.PinsGPIO4Output)
            { CountUpStepMs = AppState.Settings.CountUpStepMs };
            _led = new LedEffects(_hardware.Neo);
            _engine = new GameEngine(_game, new DeferredScoreSink(_audio), _lamps, AppState.Audits, _led)
            { TiltEnabled = AppState.Settings.TiltEnabled, TiltsAllowed = AppState.Settings.TiltsAllowed };
            _game.AttachEngine(_engine);
            _diag.AttachHardware(this, true);   // re-points the diag view's lamp map at the fresh controller
            Ready = true;
        }

        public void ApplyOperatorSettings(OperatorSettings s)
        {
            if (_lamps != null) _lamps.CountUpStepMs = s.CountUpStepMs;
            if (_engine != null) { _engine.TiltEnabled = s.TiltEnabled; _engine.TiltsAllowed = s.TiltsAllowed; }
            if (_attractLightsTimer != null) _attractLightsTimer.Interval = TimeSpan.FromSeconds(Math.Max(10, s.AttractLightSeconds));
            if (_attractSoundTimer != null) _attractSoundTimer.Interval = TimeSpan.FromSeconds(Math.Max(10, s.AttractSoundSeconds));
        }

        // Idle attract: play the light show + score-lamp pattern + attract sound on the operator's cadence.
        private void StartAttractTimers()
        {
            _attractLightsTimer = new DispatcherTimer(DispatcherPriority.Background, _ui)
            { Interval = TimeSpan.FromSeconds(Math.Max(10, AppState.Settings.AttractLightSeconds)) };
            _attractLightsTimer.Tick += (s, e) =>
            {
                if (Mode != "game" || (_engine?.GameInProgress ?? false) || _game.DemoActive || !AppState.AttractLightsOn) return;
                string ap = AttractPatterns[_rnd.Next(AttractPatterns.Length)];
                if (Ready) Task.Run(() =>   // LED strip (SPI) + lamp pattern off the UI timer thread
                {
                    try { _led?.LEDStripTest(ap, "Attract"); AttractLamp(); }
                    catch (Exception ex) { Log.Error("attract lights failed", ex); }
                });
                _game.FlashMode("ATTRACT · " + ap, "TealBrush");   // self-marshals to the UI thread
            };
            _attractLightsTimer.Start();

            _attractSoundTimer = new DispatcherTimer(DispatcherPriority.Background, _ui)
            { Interval = TimeSpan.FromSeconds(Math.Max(10, AppState.Settings.AttractSoundSeconds)) };
            _attractSoundTimer.Tick += (s, e) =>
            {
                if (Mode != "game" || (_engine?.GameInProgress ?? false) || _game.DemoActive || !AppState.AttractSoundOn) return;
                _ = _audio.PlayAttractMusic();
            };
            _attractSoundTimer.Start();
        }

        private void AttractLamp()
        {
            switch (_rnd.Next(5))
            {
                case 0: _lamps.ChasePattern(); break;
                case 1: _lamps.InsideOutPattern(); break;
                case 2: _lamps.SweepBounce(); break;
                case 3: _lamps.Cascade(); break;
                default: _lamps.FlashAll(); break;
            }
        }

        // ---------------- switch polling ----------------
        private void StartPolling()
        {
            for (int b = 1; b <= 3; b++) { var last = _last[b - 1]; foreach (var _ in BoardInputPins(b)) last.Add("Low"); }
            Task.Run(() => PollBoard(1));
            Task.Run(() => PollBoard(2));
            Task.Run(() => PollBoard(3));
        }

        // Fetch the CURRENT handle each cycle so a re-opened board (after replug) is picked up without restarting the task.
        private GpioController? BoardController(int board) => board switch
        {
            1 => _hardware.ControllerGPIO1, 2 => _hardware.ControllerGPIO2, 3 => _hardware.ControllerGPIO3, _ => null
        };
        private int[] BoardInputPins(int board) => board switch
        {
            1 => _hardware.PinsGPIO1Input, 2 => _hardware.PinsGPIO2Input, 3 => _hardware.PinsGPIO3Input, _ => System.Array.Empty<int>()
        };

        // Force every input to re-report on the next poll pass by wiping the "last seen" baseline. After Bench Mode
        // turns off this makes stuck/floating switches fire a fresh edge so the Diagnostics stuck detector re-runs.
        public void ResyncInputs()
        {
            foreach (var last in _last)
                for (int i = 0; i < last.Count; i++) last[i] = "?";
        }

        private async Task PollBoard(int board)
        {
            var last = _last[board - 1];
            var pins = BoardInputPins(board);
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var controller = BoardController(board);
                    if (controller == null) { await Task.Delay(400); continue; }
                    for (int i = 0; i < pins.Length && i < last.Count; i++)
                    {
                        // Lock so switch reads don't collide with lamp writes on the same board; OnSwitch runs outside the lock.
                        string value;
                        lock (controller) { value = controller.Read(pins[i]).ToString(); }
                        if (value != last[i]) { last[i] = value; OnSwitch(board, pins[i], value == "High"); }
                    }
                    System.Threading.Interlocked.Increment(ref _pollCycles);   // one healthy pass of this board
                    _lastSeenTicks[board - 1] = Environment.TickCount64;
                    await Task.Delay(50);
                }
                catch (Exception ex)
                {
                    _degraded = true;   // a board went away — the presence watch will re-open it when it's back
                    Log.Error($"Board {board} switch read failed (unplugged?) — backing off", ex);
                    await Task.Delay(1000);   // don't spin on a dead board; resume if it comes back
                }
            }
        }

        private void OnSwitch(int board, int pin, bool high)
        {
            SwitchDef? def = SwitchMap.Find(board, pin);
            if (def == null) return;

            var obs = SwitchObserver;
            if (obs != null) { _ui.BeginInvoke(new Action(() => obs(def, high))); return; }   // wiring test takes over (explicit test, works even in bench mode)

            // Bench mode: ignore switch inputs completely — no Diagnostics activity, no stuck flags, no phantom games.
            if (AppState.Settings.BenchMode) return;

            PinActivity.Set(board, pin, high);   // lights the input on the Diagnostics switch monitor

            // Track how long this input has been held high, for the fault timer's stuck detection.
            lock (_faultLock) _stuckMon.Set(def.Id, high, Environment.TickCount64);

            // Pure decision for what this switch does to the engine (bench/observer handled above; monitor done above).
            var route = SwitchRouter.Route(def.Kind, _game.DemoActive, Faults.Contains(def.Id),
                                           Mode == "game", _engine != null, _engine?.GameInProgress ?? false);
            if (route == EngineRoute.None) return;
            if (route == EngineRoute.Coin) { HandleCoin(high); return; }   // validated further by CoinGate

            // route == Play: a score/gobble/tilt hit into the running game, plus the show side-effects.
            if (def.CancelBefore) _led?.CancelLights();
            if (high && _distractActive)   // the next switch ends any running distract show
            {
                _distractActive = false;
                _led?.CancelLights();
                _game.StopDistractShow();
            }
            switch (def.Kind)
            {
                case SwitchKind.Score: _engine!.Hit(def, high); break;
                case SwitchKind.Gobble: _engine!.Gobble(high); break;
                case SwitchKind.Tilt: _engine!.Tilt(high); break;
            }
            // Scoring in the danger levels (5-7, where gobble holes can end the ball) fires a distraction that
            // runs until the next shot — random lights/sound meant to rattle the player. Never while a switch is stuck.
            if (high && def.Distract && !Faults.Any)
            {
                _distractActive = true;
                string dp = DistractPatterns[_rnd.Next(DistractPatterns.Length)];
                if (AppState.DistractLightsOn) _led?.LEDStripTest(dp, "Distract");
                if (AppState.DistractSoundOn) _ = _audio.PlayDistractMusic();
                _game.FlashMode("DISTRACT · " + dp, "PurpleBrush");
            }
        }

        // Kill any running distract show (lights + music); used when a switch goes stuck.
        private void StopDistract()
        {
            if (!_distractActive) return;
            _distractActive = false;
            try { _led?.CancelLights(); } catch { }
            _game.StopDistractShow();
            Task.Run(() => { try { _audio.StopMusicClip(); } catch { } });
        }

        // Start a game only on a genuine, momentary coin: it must release quickly (a stuck/floating coin sits high and
        // never releases in time) AND the board must otherwise be idle — several inputs high at once is a floating bus,
        // not a real coin insert, so we refuse to start a phantom game from it.
        private void HandleCoin(bool high)
        {
            int others; lock (_faultLock) others = _stuckMon.HighCount;   // the coin itself was already removed on this low edge
            if (_coinGate.OnCoin(high, Environment.TickCount64, others)) _engine?.Coin(true);
        }

        // ---------------- diagnostics actions ----------------
        public void Pattern(string name)
        {
            switch (name)
            {
                case "chase": _lamps.ChasePattern(); break;
                case "inout": _lamps.InsideOutPattern(); break;
                case "sweep": _lamps.SweepBounce(); break;
                case "cascade": _lamps.Cascade(); break;
                case "allon": _lamps.SetLamp("All", true); break;
                case "random":
                    switch (new Random().Next(5))
                    {
                        case 0: _lamps.ChasePattern(); break;
                        case 1: _lamps.InsideOutPattern(); break;
                        case 2: _lamps.SweepBounce(); break;
                        case 3: _lamps.Cascade(); break;
                        default: _lamps.FlashAll(); break;
                    }
                    break;
            }
        }

        public void AllOff() { _lamps.StopLampPattern(); _lamps.ClearAll(); _led?.CancelLights(); _led?.LEDOFF(); }

        private CancellationTokenSource? _bulbCts;
        public bool BulbRunning => _bulbCts != null && !_bulbCts.IsCancellationRequested;
        public void StopBulbTest() { _bulbCts?.Cancel(); _lamps.ClearAll(); }

        // Walk every lamp in turn, repeating until stopped (click again / ALL OFF).
        public async Task BulbTestAsync()
        {
            _bulbCts?.Cancel();
            _bulbCts = new CancellationTokenSource();
            var token = _bulbCts.Token;
            string[] seq = { "10", "20", "30", "40", "50", "60", "70", "80", "90",
                             "100", "200", "300", "400", "Winner", "GameOver", "Tilt" };
            _lamps.StopLampPattern();
            try
            {
                while (!token.IsCancellationRequested)
                    foreach (var lamp in seq)
                    {
                        if (token.IsCancellationRequested) break;
                        _lamps.SetLamp(lamp, true);
                        await Task.Delay(280, token);
                        _lamps.SetLamp(lamp, false);
                        await Task.Delay(60, token);
                    }
            }
            catch (OperationCanceledException) { }
            _lamps.ClearAll();
        }

        public void LedTest() => _led?.LEDStripTest("Rainbow Wave", "Attract");

        public void SoundTest() => _audio.PlaySettingsSound("select");
        public void StopSound() { try { _audio.StopAudio(); } catch { } }

        // ---- LED strip ----
        public void LedPattern(string name) => _led?.LEDStripTest(name, "Attract");
        public void LedSolid(string colorName) => _led?.SolidColor(NamedColor(colorName));
        public void LedOff() => _led?.LEDOFF();
        public void LedBrightnessUp() => _led?.BrightnessUp(Color.White);
        public void LedBrightnessDown() => _led?.BrightnessDown(Color.White);

        private static Color NamedColor(string n) => n switch
        {
            "Red" => Color.Red, "Green" => Color.Lime, "Blue" => Color.Blue,
            "Yellow" => Color.Yellow, "Purple" => Color.MediumPurple, "White" => Color.White,
            _ => Color.White,
        };

        // ---- Solenoids / special lamps (Winner, Game Over, Tilt, Winner-Lock, Coin-Lock) ----
        public void SetLamp(string name, bool on) => _lamps.SetLamp(name, on);
        public async Task PulseAsync(string name, int ms = 150)
        {
            _lamps.SetLamp(name, true);
            await Task.Delay(ms);
            _lamps.SetLamp(name, false);
        }

        // ---- Sound (package + file), e.g. PlaySound("8-Bit","50 pts") ----
        public void PlaySound(string package, string file) => _audio.PlaySound(package, file);

        public async Task FireSolenoidAsync()
        {
            _lamps.SetLamp("WinnerLock", true);
            await Task.Delay(120);
            _lamps.SetLamp("WinnerLock", false);
        }

        public async Task<(bool ok, bool[] present)> SelfTestAsync()
        {
            bool[] present = GetBoardPresence();
            bool ok = present.All(p => p);
            _lamps.SetLamp("All", true);
            _led?.SolidColor(Color.White);
            await Task.Delay(400);
            _lamps.SetLamp("All", false);
            _led?.LEDOFF();
            Log.Info(ok ? "POST: PASS" : "POST: FAIL");
            return (ok, present);
        }

        private async Task RunPostAsync()
        {
            var (ok, present) = await Task.Run(async () => await SelfTestAsync());   // POST lamp/LED flash off the UI thread
            _diag.SetBoardPresence(present, ok);   // await resumes on the UI context
        }

        public bool[] GetBoardPresence()
        {
            var present = new bool[4];
            try
            {
                var serials = FtCommon.GetDevices().Select(d => d.SerialNumber).ToList();
                for (int i = 0; i < 4; i++) present[i] = serials.Contains("GPIO" + (i + 1));
            }
            catch { }
            return present;
        }

        public void Shutdown()
        {
            _cts.Cancel();
            _attractLightsTimer?.Stop();
            _attractSoundTimer?.Stop();
            _presenceTimer?.Stop();
            _faultTimer?.Stop();
            AppState.BenchModeChanged -= OnBenchChanged;
            try { _lamps?.ClearAll(); _led?.LEDOFF(); } catch { }
        }
    }
}
