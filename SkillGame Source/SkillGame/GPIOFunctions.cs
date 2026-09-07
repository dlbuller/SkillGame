using Iot.Device.Ft232H;
using Iot.Device.FtCommon;
using Iot.Device.Seesaw;
using Iot.Device.Ws28xx;
using System;
using System.Collections.Generic;
using System.Device.Gpio;
using System.Device.Spi;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace SkillGame
{
    public partial class GPIOFunctions
    {
        FtHardware hardware = new FtHardware();

        // Audio
        AudioFunctions audio = new AudioFunctions();
        // Lamp / solenoid outputs (GPIO3/GPIO4)
        LampController? lamps;
        LedEffects? led;


        #region Pin Value Variables
        // Current pin read + last-known values per board (compared each poll to detect switch changes)
        string pinValueBoard1 = string.Empty;
        string pinValueBoard2 = string.Empty;
        string pinValueBoard3 = string.Empty;
        List<string> GPIOpinsLastValueBoard1 = new List<string>();
        List<string> GPIOpinsLastValueBoard2 = new List<string>();
        List<string> GPIOpinsLastValueBoard3 = new List<string>();
        #endregion Pin Value Variables

        // LED Strip
        private Random rnd = new Random();

        CancellationTokenSource _cts = new CancellationTokenSource();

        public SkillGameForm _mainForm;

        /// <summary>True only after a fully successful hardware init. When false, every function no-ops.</summary>
        public bool Ready { get; private set; }

        public GPIOFunctions(SkillGameForm mainForm)
        {
            _mainForm = mainForm;
        }

        public string CheckTab(SkillGameForm mainForm)
        {
            if (mainForm.tabControl1.InvokeRequired)
            {
                return (string)mainForm.tabControl1.Invoke(new Func<string>(() => CheckTab(mainForm)));
            }

            return mainForm.tabControl1.SelectedTab?.Name ?? string.Empty;
        }

        public void InitializeGPIO()
        {
            try
            {
                hardware.Initialize();
                led = new LedEffects(hardware.Neo);
                Ready = true;
            }
            catch (Exception ex)
            {
                Log.Error("FT232H initialization failed", ex);
                MessageBox.Show(_mainForm, ex.Message, "SkillGame - Hardware Not Ready", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // The lamp controller is built either way. With boards it drives the real score lamps/reels
            // and solenoids; without boards its pin writes no-op, but it still mirrors every lamp button,
            // Bulb Test and pattern onto the I/O Activity Map - a live preview of what would be driven.
            lamps = new LampController(hardware.ControllerGPIO3, hardware.ControllerGPIO4, hardware.PinsGPIO3Output, hardware.PinsGPIO4Output);
            ApplyOperatorSettings(_mainForm.OpSettings);

            // The game engine + switch polling + power-on self test need real hardware.
            if (Ready)
            {
                engine = new GameEngine(_mainForm, audio, lamps, _mainForm.Audits, led);
                ApplyOperatorSettings(_mainForm.OpSettings);
                StartGPIO();
                Log.Info("FT232H hardware initialized; switch polling started.");
                RunPowerOnSelfTest();
            }
        }

        /// <summary>Push operator-tunable settings into the live subsystems (count-up speed, tilt enable).</summary>
        public void ApplyOperatorSettings(OperatorSettings s)
        {
            if (lamps != null) lamps.CountUpStepMs = s.CountUpStepMs;
            if (engine != null) engine.TiltEnabled = s.TiltEnabled;
        }

        /// <summary>
        /// Power-on self test: confirm all four boards enumerated, then briefly flash every lamp and the
        /// LED strip as a visible "alive" check. Logs the result and reports it to the Hardware Status
        /// readout. Never throws — a failed POST just shows red, it doesn't stop the game.
        /// </summary>
        public async void RunPowerOnSelfTest()
        {
            string result;
            bool ok = true;
            try
            {
                bool[] present = GetBoardPresence();
                var missing = new List<string>();
                for (int i = 0; i < 4; i++) if (!present[i]) missing.Add("GPIO" + (i + 1));
                ok = missing.Count == 0;

                // visible lamp + strip flash so you can see the machine woke up
                lamps?.SetLamp("All", true);
                led?.SolidColor(Color.White);
                await Task.Delay(400);
                lamps?.SetLamp("All", false);
                led?.LEDOFF();

                result = ok ? "POST: PASS" : "POST: FAIL (" + string.Join(", ", missing) + ")";
            }
            catch (Exception ex)
            {
                ok = false;
                result = "POST: FAIL (" + ex.Message + ")";
            }
            Log.Info(result);
            _mainForm.ShowPostResult(ok ? "POST: PASS" : "POST: FAIL", ok);   // short label; detail is in the log
        }

        /// <summary>Stops the background switch-polling loops (called on shutdown).</summary>
        public void StopGPIO() => _cts.Cancel();

        // ---- service / diagnostics helpers ----

        private CancellationTokenSource? _demoCts;

        /// <summary>True while the bench demo game is running.</summary>
        public bool DemoRunning => _demoCts != null && !_demoCts.IsCancellationRequested;

        /// <summary>Bench self-test: play a full scripted game on the real lamps/sounds (no playfield),
        /// paced ~5 s between scores so the sounds and light animations have time to play out.</summary>
        public void RunDemo()
        {
            if (engine == null || DemoRunning) return;
            _demoCts = new CancellationTokenSource();
            _ = RunDemoAsync(_demoCts.Token);
        }

        private async Task RunDemoAsync(CancellationToken token)
        {
            try { await DemoGame.RunAsync(engine, 5000, token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Error("Demo game failed", ex); }
            finally { _mainForm.OnDemoFinished(); }
        }

        /// <summary>Cancel a running demo and reset the game/lamps/strip to idle.</summary>
        public void StopDemo()
        {
            _demoCts?.Cancel();
            engine?.ClearGame();
            lamps?.StopLampPattern();
            led?.CancelLights();
            led?.LEDOFF();
        }

        /// <summary>Sequentially light every score lamp/reel on then off, to check each one. Drives the
        /// real lamps when boards are present, and always walks the I/O Activity Map as a preview.</summary>
        public async Task LampBurnIn()
        {
            string[] seq = { "10", "20", "30", "40", "50", "60", "70", "80", "90",
                             "100", "200", "300", "400", "Winner", "GameOver", "Tilt" };
            foreach (var lamp in seq)
            {
                lamps?.SetLamp(lamp, true);
                await Task.Delay(300);
                lamps?.SetLamp(lamp, false);
                await Task.Delay(60);
            }
        }

        /// <summary>Which of GPIO1-4 are enumerated right now (for the board-status readout).</summary>
        public string GetBoardStatus()
        {
            try
            {
                var serials = FtCommon.GetDevices().Select(d => d.SerialNumber).ToList();
                string[] want = { "GPIO1", "GPIO2", "GPIO3", "GPIO4" };
                return string.Join("   ", want.Select(w => serials.Contains(w) ? w + " OK" : w + " --"));
            }
            catch { return "FTDI query failed"; }
        }

        /// <summary>Per-board presence for GPIO1-4 (true = enumerated). Independent of open handles.</summary>
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

        #region LED Strip Functions
        public void CancelLights() => led?.CancelLights();
        public void StartupLights() => led?.StartupLights();
        public void LEDStripTest(string pattern, string type) => led?.LEDStripTest(pattern, type);
        public void SolidColor(Color color) => led?.SolidColor(color);
        public void LEDOFF() => led?.LEDOFF();
        public void BrightnessUp(Color color) => led?.BrightnessUp(color);
        public void BrightnessDown(Color color) => led?.BrightnessDown(color);

        public async void DistractLights()
        {
            string[] patterns = { "Marquee", "Breathe", "Crazy Strobe", "Sparkle", "Lightning", "Confetti", "Quad Strobe" };
            LEDStripTest(patterns[rnd.Next(0, patterns.Length)], "Distract");
            await Task.Delay(100);
        }

        public async void AttractLights()
        {
            string[] patterns = { "Marquee", "Rainbow Wave", "Comet", "Breathe", "Crazy Strobe", "Plasma Wave", "Meteor Rain", "Sparkle", "Knight Rider", "Bouncing Balls", "Lightning", "Confetti", "DNA Helix", "Quad Strobe" };
            if (!(engine?.GameInProgress ?? false) && _mainForm.mainAttractLightTextBox.Text == "ON")
            {
                LEDStripTest(patterns[rnd.Next(0, patterns.Length)], "Attract");
                await Task.Delay(100);
            }
        }

        /// <summary>Idle attract for the physical score lamps: kick off a RANDOM lamp pattern (Chase /
        /// Inside Out / Sweep / Cascade / Flash). Runs while no game is in progress.</summary>
        public void AttractLamps()
        {
            if (lamps == null || (engine?.GameInProgress ?? false)) return;
            switch (rnd.Next(5))
            {
                case 0: lamps.ChasePattern(); break;
                case 1: lamps.InsideOutPattern(); break;
                case 2: lamps.SweepBounce(); break;
                case 3: lamps.Cascade(); break;
                default: lamps.FlashAll(); break;
            }
        }
        #endregion LED Strip Functions

        #region Score Lamp Functions
        public void TallyScore(int value) => lamps?.TallyScore(value);

        // The write goes through LampController.Drive, which mirrors the pin onto the I/O Activity Map.
        public void DiagnosticScoreLamps(string lamp, bool state) => lamps?.SetLamp(lamp, state);

        // Score-lamp attract patterns (wire these to diagnostics buttons; call StopLampPattern to end)
        public void StartLampChase() => lamps?.ChasePattern();
        public void StartLampInsideOut() => lamps?.InsideOutPattern();
        public void StartLampSweep() => lamps?.SweepBounce();
        public void StartLampCascade() => lamps?.Cascade();
        public void FlashLamps() => lamps?.FlashAll();
        public void StopLampPattern() => lamps?.StopLampPattern();
        #endregion Score Lamp Functions 
    }
}
