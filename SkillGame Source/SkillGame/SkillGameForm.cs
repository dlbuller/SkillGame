using AcrylicUI.Controls;
using AcrylicUI.Forms;
using AcrylicUI.Resources;
using Iot.Device.Ft232H;
using Iot.Device.FtCommon;
using Iot.Device.Graphics;
using Iot.Device.Seatalk1.Messages;
using Iot.Device.Ws28xx;
using System;
using System.Device.Gpio;
using System.Device.Spi;
using System.Drawing;
using System.Numerics;


namespace SkillGame
{
    public partial class SkillGameForm : AcrylicForm, IGameView
    {
        #region Variables and Initialization
        // Variables
        GPIOFunctions gpio = null!;
        private LayoutScaler _scaler;

        bool redLEDButtonClicked = false;
        bool greenLEDButtonClicked = false;
        bool blueLEDButtonClicked = false;
        bool yellowLEDButtonClicked = false;
        bool purpleLEDButtonClicked = false;
        bool whiteLEDButtonClicked = false;
        bool controlledLEDsButtonClicked = false;
        bool loaded = false;
        bool _lampPatternRunning = false;
        bool _quietApplied = false;

        public int SelectedTabIndex => tabControl1.SelectedIndex;

        // LED
        private Random rnd = new Random();
        CancellationTokenSource _cts = new CancellationTokenSource();
        LightFunctions lights = new LightFunctions();

        // Audio
        AudioFunctions audio = new AudioFunctions();

        List<FtDevice> devices = new List<FtDevice>();

        public SkillGameForm()
        {
            InitializeComponent();
            _auditStore.Load();
            BuildAuditsUI();
            BuildHardwareStatusUI();
            BuildGameStatusExtras();
            BuildBulbTestButton();
            BuildOperatorSetupUI();
            BuildGameStatusInfo();
            BuildSettingsInfo();
            BuildBranding();
            ApplySectionColors();
            FrameSections();
            BuildToolTips();
            _opSettings.ApplyToQuietHours();
            ApplyAttractIntervals();
            StartExtrasTimer();
            _scaler = new LayoutScaler(this);
            _scaler.Record();
            Resize += (s, e) => _scaler.Apply();
            WindowState = FormWindowState.Maximized;
            this.Text = "SkillGame V1.0";
            this.IsAcrylic = true;
            this.BlurOpacity = 10;
            BackColor = Colors.GreyBackground;
            _ = GetVolumeAsync(_cts.Token);
        }

        private void SkillGameForm_Load(object sender, EventArgs e)
        {
            // Create the coordinator up front so navigating tabs never NREs, even if hardware init fails.
            gpio = new GPIOFunctions(this);
            try
            {
                soundPackageComboBox.SelectedIndex = 0;
                soundPackageCombo.SelectedIndex = 0;
                soundComboBox.SelectedIndex = 0;
                controlledLEDsCombobox.SelectedIndex = 0;
                lampPatternComboBox.SelectedIndex = 0;
                currentSoundPackageTextBox.Text = soundPackageCombo.SelectedItem?.ToString() ?? "";
                audio.PlayStartupSound();
                gpio.InitializeGPIO();   // shows a "Hardware Not Ready" message itself if boards are missing

                // Attract mode + startup lights only make sense with real hardware. Without it, the form
                // still opens and every tab is viewable - the game functions just no-op.
                if (gpio.Ready)
                {
                    loaded = true;
                    attractTimer.Enabled = true;
                    attractSoundTimer.Enabled = true;
                    gpio.StartupLights();
                }
                else
                {
                    // No boards: every tab still opens; the game/lamp/LED functions just no-op.
                    ShowPostResult("POST: NO HW", false);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Form load failed", ex);
                MessageBox.Show("Error during startup: " + ex.Message);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Stop the background loops cleanly on shutdown (no visual change).
            try { _cts.Cancel(); } catch { }
            try { gpio?.StopGPIO(); } catch { }
            try { _boardTimer?.Stop(); } catch { }
            try { _extrasTimer?.Stop(); } catch { }
            try { _auditStore.Save(); } catch { }
            base.OnFormClosing(e);
        }

        // ---- IGameView: the game rules (GameEngine) push state here; safe from the poll thread ----
        public string SoundPackage => currentSoundPackageTextBox.Text;

        public void ShowStatus(int score, int level, string lastSwitch, string status)
        {
            if (InvokeRequired) { BeginInvoke((MethodInvoker)(() => ShowStatus(score, level, lastSwitch, status))); return; }
            scoreTextBox.Text = score.ToString();
            levelTextBox.Text = level.ToString();
            lastSwitchTextBox.Text = lastSwitch;
            statusTextBox.Text = status;
        }

        public void ShowScoreLevel(int score, int level)
        {
            if (InvokeRequired) { BeginInvoke((MethodInvoker)(() => ShowScoreLevel(score, level))); return; }
            scoreTextBox.Text = score.ToString();
            levelTextBox.Text = level.ToString();
        }


        #endregion

        #region Tab Button Click Handlers
        private void gameStatusButton_Click_1(object sender, EventArgs e)
        {
            if (tabControl1.SelectedTab != gameStatusTab)
            {
                audio.StopAudio();
                gpio.CancelLights();
                gpio.LEDOFF();
                tabControl1.SelectedTab = gameStatusTab;
                System.Threading.Thread.Sleep(200);
                audio.PlaySettingsSound("home");
                currentSoundPackageTextBox.Text = soundPackageCombo.SelectedItem?.ToString() ?? "";
                
                if (mainAttractLightTextBox.Text == "ON")
                {
                    attractTimer.Enabled = true;
                }
                if (mainAttractTextBox.Text == "ON")
                {
                    attractSoundTimer.Enabled = true;
                }
            }
        }

        private async void settingsButton_Click_1(object sender, EventArgs e)
        {
            if (tabControl1.SelectedTab != settingsTab)
            {
                tabControl1.SelectedTab = settingsTab;
                gpio.CancelLights();
                gpio.StopLampPattern();
                audio.StopAudio();
                Music("Settings");
                attractTimer.Enabled = false;
                attractTimer.Stop();
                attractSoundTimer.Enabled = false;
                attractSoundTimer.Stop();
                gpio.LEDOFF();
                await GetVolumeAsync(_cts.Token);
            }
        }

        private void diagnosticsButton_Click_1(object sender, EventArgs e)
        {
            if (tabControl1.SelectedTab != diagnosticsTab)
            {
                tabControl1.SelectedTab = diagnosticsTab;
                gpio.CancelLights();
                gpio.StopLampPattern();
                audio.StopAudio();
                Music("Diagnostics");
                attractSoundTimer.Enabled = false;
                attractSoundTimer.Stop();
                attractTimer.Enabled = false;
                attractTimer.Stop();
                gpio.LEDOFF();
            }
        }

        private void auditsButton_Click(object sender, EventArgs e)
        {
            if (tabControl1.SelectedTab != auditsTab)
            {
                tabControl1.SelectedTab = auditsTab;
                gpio.CancelLights();
                gpio.StopLampPattern();
                audio.StopAudio();
                Music("Audits");
                attractSoundTimer.Enabled = false;
                attractTimer.Enabled = false;
                attractTimer.Stop();
                attractSoundTimer.Stop();
                gpio.LEDOFF();
            }
        }
        #endregion Tab Button Click Handlers

        #region Volume Control Button Click Handlers
        private void volumeDownButton_Click_1(object sender, EventArgs e)
        {
            audio.PlaySettingsSound("volume");
            audio.SetMasterVolumeDown();
            volumeLabel.Text = "Volume: " + audio.GetMasterVolume().ToString() + "%";
        }

        private void volumeUpButton_Click_1(object sender, EventArgs e)
        {
            audio.PlaySettingsSound("volume");
            audio.SetMasterVolumeUp();
            volumeLabel.Text = "Volume: " + audio.GetMasterVolume().ToString() + "%";
        }

        public async Task GetVolumeAsync(CancellationToken token)
        {
            string volume = "";

            try
            {
                while (!token.IsCancellationRequested)
                {
                    volume = audio.GetMasterVolume();
                    if (volume.Contains("*"))
                    {
                        volumeLabel.Invoke((MethodInvoker)(() => volumeLabel.Text = "Volume: " + volume + "%"));
                        volumeStatusTextBox.Invoke((MethodInvoker)(() => volumeStatusTextBox.Text = volume + "%"));
                        volumeMuteButton.BackColor = Color.Tomato;
                        volumeMuteButton.Text = "MUTED";
                        volumeMuteTextBox.Invoke((MethodInvoker)(() => volumeMuteTextBox.Text = "MUTED"));
                    }
                    else
                    {
                        volumeMuteButton.BackColor = Color.Black;
                        volumeMuteButton.Text = "mute";
                        volumeLabel.Invoke((MethodInvoker)(() => volumeLabel.Text = "Volume: " + volume + "%"));
                        volumeStatusTextBox.Invoke((MethodInvoker)(() => volumeStatusTextBox.Text = volume + "%"));
                        volumeMuteTextBox.Invoke((MethodInvoker)(() => volumeMuteTextBox.Text = "Unmuted"));
                    }


                    // Optional quiet hours: cap the volume once when the window starts (off by default).
                    if (QuietHours.IsQuietNow(DateTime.Now))
                    {
                        if (!_quietApplied) { audio.SetMasterVolumePercent(QuietHours.QuietPercent); _quietApplied = true; }
                    }
                    else _quietApplied = false;

                    // Non-blocking wait
                    await Task.Delay(250, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
            }
        }
        #endregion Volume Control Button Click Handlers

        #region Music Button Click Handlers
        public async void Music(string music)
        {

            try
            {
                await audio.PlayThemeMusic(music);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Music playback error: " + ex.Message);
            }
        }
        #endregion Music Button Click Handlers

        #region LED Button Click Handlers

        private void controlledLEDsButton_Click(object sender, EventArgs e)
        {
            if (!controlledLEDsButtonClicked)
            {
                audio.PlaySettingsSound("ledpatternon");
                controlledLEDsButtonClicked = true;
                // Reset other color buttons

                redLEDButton.BackColor = Color.Black;
                redLEDButtonClicked = false;
                blueLEDButton.BackColor = Color.Black;
                blueLEDButtonClicked = false;
                yellowLEDButton.BackColor = Color.Black;
                yellowLEDButtonClicked = false;
                purpleLEDButton.BackColor = Color.Black;
                purpleLEDButtonClicked = false;
                greenLEDButton.BackColor = Color.Black;
                greenLEDButtonClicked = false;
                whiteLEDButton.BackColor = Color.Black;
                whiteLEDButtonClicked = false;

                controlledLEDsButton.BackColor = Color.FromArgb(3, 0, 46);
                controlledLEDsButton.Text = "STOP";
                gpio.LEDStripTest(controlledLEDsCombobox.SelectedItem?.ToString() ?? "", "");
            }
            else
            {
                audio.PlaySettingsSound("ledpatternoff");
                gpio.CancelLights();
                gpio.SolidColor(Color.Black);
                controlledLEDsButtonClicked = false;
                controlledLEDsButton.BackColor = Color.Black;
                controlledLEDsButton.Text = "START";
            }
        }

        private void redLEDButton_Click(object sender, EventArgs e)
        {
            if (controlledLEDsButtonClicked)
            {
                gpio.CancelLights();
                gpio.SolidColor(Color.Black);
                controlledLEDsButtonClicked = false;
                controlledLEDsButton.BackColor = Color.Black;
                controlledLEDsButton.Text = "START";
            }
            if (!redLEDButtonClicked)
            {
                audio.PlaySettingsSound("solidledon");
                redLEDButtonClicked = true;
                redLEDButton.BackColor = Color.FromArgb(3, 0, 46);
                gpio.SolidColor(Color.FromArgb(128, 0, 0));

                // Reset other color buttons
                greenLEDButton.BackColor = Color.Black;
                greenLEDButtonClicked = false;
                blueLEDButton.BackColor = Color.Black;
                blueLEDButtonClicked = false;
                yellowLEDButton.BackColor = Color.Black;
                yellowLEDButtonClicked = false;
                purpleLEDButton.BackColor = Color.Black;
                purpleLEDButtonClicked = false;
                whiteLEDButton.BackColor = Color.Black;
                whiteLEDButtonClicked = false;
            }
            else
            {
                audio.PlaySettingsSound("solidledoff");
                redLEDButtonClicked = false;
                redLEDButton.BackColor = Color.Black;
                gpio.SolidColor(Color.Black);
            }
        }

        private void clearAllLEDsButton_Click(object sender, EventArgs e)
        {
            audio.PlaySettingsSound("brightnessoff");
            if (controlledLEDsButtonClicked)
            {
                gpio.CancelLights();
                gpio.SolidColor(Color.Black);
                controlledLEDsButtonClicked = false;
                controlledLEDsButton.BackColor = Color.Black;
                controlledLEDsButton.Text = "START";
            }

            gpio.SolidColor(Color.Black);
            redLEDButton.BackColor = Color.Black;
            redLEDButtonClicked = false;
            greenLEDButton.BackColor = Color.Black;
            greenLEDButtonClicked = false;
            blueLEDButton.BackColor = Color.Black;
            blueLEDButtonClicked = false;
            yellowLEDButton.BackColor = Color.Black;
            yellowLEDButtonClicked = false;
            purpleLEDButton.BackColor = Color.Black;
            purpleLEDButtonClicked = false;
            whiteLEDButton.BackColor = Color.Black;
            whiteLEDButtonClicked = false;
        }

        private void greenLEDButton_Click(object sender, EventArgs e)
        {
            if (controlledLEDsButtonClicked)
            {
                gpio.CancelLights();
                gpio.SolidColor(Color.Black);
                controlledLEDsButtonClicked = false;
                controlledLEDsButton.BackColor = Color.Black;
                controlledLEDsButton.Text = "START";
            }

            if (!greenLEDButtonClicked)
            {
                audio.PlaySettingsSound("solidledon");
                greenLEDButtonClicked = true;
                greenLEDButton.BackColor = Color.FromArgb(3, 0, 46);
                gpio.SolidColor(Color.FromArgb(0, 128, 0));

                // Reset other color buttons
                redLEDButton.BackColor = Color.Black;
                redLEDButtonClicked = false;
                blueLEDButton.BackColor = Color.Black;
                blueLEDButtonClicked = false;
                yellowLEDButton.BackColor = Color.Black;
                yellowLEDButtonClicked = false;
                purpleLEDButton.BackColor = Color.Black;
                purpleLEDButtonClicked = false;
                whiteLEDButton.BackColor = Color.Black;
                whiteLEDButtonClicked = false;
            }
            else
            {
                audio.PlaySettingsSound("solidledoff");
                greenLEDButtonClicked = false;
                greenLEDButton.BackColor = Color.Black;
                gpio.SolidColor(Color.Black);
            }
        }

        private void blueLEDButton_Click(object sender, EventArgs e)
        {
            if (controlledLEDsButtonClicked)
            {
                gpio.CancelLights();
                gpio.SolidColor(Color.Black);
                controlledLEDsButtonClicked = false;
                controlledLEDsButton.BackColor = Color.Black;
                controlledLEDsButton.Text = "START";
            }

            if (!blueLEDButtonClicked)
            {
                audio.PlaySettingsSound("solidledon");
                blueLEDButtonClicked = true;
                blueLEDButton.BackColor = Color.FromArgb(3, 0, 46);
                gpio.SolidColor(Color.FromArgb(0, 0, 128));
                // Reset other color buttons
                redLEDButton.BackColor = Color.Black;
                redLEDButtonClicked = false;
                greenLEDButton.BackColor = Color.Black;
                greenLEDButtonClicked = false;
                yellowLEDButton.BackColor = Color.Black;
                yellowLEDButtonClicked = false;
                purpleLEDButton.BackColor = Color.Black;
                purpleLEDButtonClicked = false;
                whiteLEDButton.BackColor = Color.Black;
                whiteLEDButtonClicked = false;
            }
            else
            {
                audio.PlaySettingsSound("solidledoff");
                blueLEDButtonClicked = false;
                blueLEDButton.BackColor = Color.Black;
                gpio.SolidColor(Color.Black);
            }
        }

        private void yellowLEDButton_Click(object sender, EventArgs e)
        {
            if (controlledLEDsButtonClicked)
            {
                gpio.CancelLights();
                gpio.SolidColor(Color.Black);
                controlledLEDsButtonClicked = false;
                controlledLEDsButton.BackColor = Color.Black;
                controlledLEDsButton.Text = "START";
            }

            if (!yellowLEDButtonClicked)
            {
                audio.PlaySettingsSound("solidledon");
                yellowLEDButtonClicked = true;
                yellowLEDButton.BackColor = Color.FromArgb(3, 0, 46);
                gpio.SolidColor(Color.FromArgb(128, 128, 0));
                // Reset other color buttons
                redLEDButton.BackColor = Color.Black;
                redLEDButtonClicked = false;
                greenLEDButton.BackColor = Color.Black;
                greenLEDButtonClicked = false;
                blueLEDButton.BackColor = Color.Black;
                blueLEDButtonClicked = false;
                purpleLEDButton.BackColor = Color.Black;
                purpleLEDButtonClicked = false;
                whiteLEDButton.BackColor = Color.Black;
                whiteLEDButtonClicked = false;
            }
            else
            {
                audio.PlaySettingsSound("solidledoff");
                yellowLEDButtonClicked = false;
                yellowLEDButton.BackColor = Color.Black;
                gpio.SolidColor(Color.Black);
            }
        }


        private void purpleLEDButton_Click(object sender, EventArgs e)
        {
            if (controlledLEDsButtonClicked)
            {
                gpio.CancelLights();
                gpio.SolidColor(Color.Black);
                controlledLEDsButtonClicked = false;
                controlledLEDsButton.BackColor = Color.Black;
                controlledLEDsButton.Text = "START";
            }

            if (!purpleLEDButtonClicked)
            {
                audio.PlaySettingsSound("solidledon");
                purpleLEDButtonClicked = true;
                purpleLEDButton.BackColor = Color.FromArgb(3, 0, 46);
                gpio.SolidColor(Color.FromArgb(64, 0, 64));
                // Reset other color buttons
                redLEDButton.BackColor = Color.Black;
                redLEDButtonClicked = false;
                greenLEDButton.BackColor = Color.Black;
                greenLEDButtonClicked = false;
                blueLEDButton.BackColor = Color.Black;
                blueLEDButtonClicked = false;
                yellowLEDButton.BackColor = Color.Black;
                yellowLEDButtonClicked = false;
                whiteLEDButton.BackColor = Color.Black;
                whiteLEDButtonClicked = false;
            }
            else
            {
                audio.PlaySettingsSound("solidledoff");
                purpleLEDButtonClicked = false;
                purpleLEDButton.BackColor = Color.Black;
                gpio.SolidColor(Color.Black);
            }
        }
        private void whiteLEDButton_Click(object sender, EventArgs e)
        {
            if (controlledLEDsButtonClicked)
            {
                gpio.CancelLights();
                gpio.SolidColor(Color.Black);
                controlledLEDsButtonClicked = false;
                controlledLEDsButton.BackColor = Color.Black;
                controlledLEDsButton.Text = "START";
            }

            if (!whiteLEDButtonClicked)
            {
                audio.PlaySettingsSound("solidledon");
                whiteLEDButtonClicked = true;
                whiteLEDButton.BackColor = Color.FromArgb(3, 0, 46);
                gpio.SolidColor(Color.FromArgb(64, 64, 64));
                // Reset other color buttons
                redLEDButton.BackColor = Color.Black;
                redLEDButtonClicked = false;
                greenLEDButton.BackColor = Color.Black;
                greenLEDButtonClicked = false;
                blueLEDButton.BackColor = Color.Black;
                blueLEDButtonClicked = false;
                yellowLEDButton.BackColor = Color.Black;
                yellowLEDButtonClicked = false;
                purpleLEDButton.BackColor = Color.Black;
                purpleLEDButtonClicked = false;
            }
            else
            {
                audio.PlaySettingsSound("solidledoff");
                whiteLEDButtonClicked = false;
                whiteLEDButton.BackColor = Color.Black;
                gpio.SolidColor(Color.Black);
            }
        }

        #endregion LED Button Click Handlers

        #region Brightness Button Click Handlers
        private void brightnessUpButton_Click(object sender, EventArgs e)
        {
            audio.PlaySettingsSound("brightnessup");
            if (controlledLEDsButtonClicked)
            {
                gpio.CancelLights();
                gpio.SolidColor(Color.Black);
                controlledLEDsButtonClicked = false;
                controlledLEDsButton.BackColor = Color.Black;
                controlledLEDsButton.Text = "START";
            }

            Color color = Color.White;

            if (redLEDButtonClicked)
            {
                color = Color.Red;
            }
            else if (greenLEDButtonClicked)
            {
                color = Color.Green;
            }
            else if (blueLEDButtonClicked)
            {
                color = Color.Blue;
            }
            else if (yellowLEDButtonClicked)
            {
                color = Color.Yellow;
            }
            else if (purpleLEDButtonClicked)
            {
                color = Color.Purple;
            }
            gpio.BrightnessUp(color);
        }

        private void brightnessDownButton_Click(object sender, EventArgs e)
        {
            audio.PlaySettingsSound("brightnessdown");
            if (controlledLEDsButtonClicked)
            {
                gpio.CancelLights();
                gpio.SolidColor(Color.Black);
                controlledLEDsButtonClicked = false;
                controlledLEDsButton.BackColor = Color.Black;
                controlledLEDsButton.Text = "START";
            }

            Color color = Color.White;

            if (redLEDButtonClicked)
            {
                color = Color.Red;
            }
            else if (greenLEDButtonClicked)
            {
                color = Color.Green;
            }
            else if (blueLEDButtonClicked)
            {
                color = Color.Blue;
            }
            else if (yellowLEDButtonClicked)
            {
                color = Color.Yellow;
            }
            else if (purpleLEDButtonClicked)
            {
                color = Color.Purple;
            }
            gpio.BrightnessDown(color);
        }

        #endregion Brightness Button Click Handlers

        #region Sound Button Click Handlers
        private void diagnosticSoundButton_Click(object sender, EventArgs e)
        {
            audio.PlaySound(soundPackageComboBox.SelectedItem?.ToString() ?? "", soundComboBox.SelectedItem?.ToString() ?? "");
        }

        private void lampPatternButton_Click(object sender, EventArgs e)
        {
            // Toggle: first click starts the selected pattern (it runs on its own
            // cancellable task inside LampController); a second click stops it.
            if (_lampPatternRunning)
            {
                gpio.StopLampPattern();
                _lampPatternRunning = false;
                lampPatternButton.Text = "START PATTERN";
                lampPatternButton.BackColor = Color.Black;
                audio.PlaySettingsSound("ledpatternoff");
            }
            else
            {
                switch (lampPatternComboBox.SelectedItem?.ToString())
                {
                    case "Inside Out": gpio.StartLampInsideOut(); break;
                    case "Sweep": gpio.StartLampSweep(); break;
                    case "Cascade": gpio.StartLampCascade(); break;
                    case "Flash": gpio.FlashLamps(); break;
                    default: gpio.StartLampChase(); break;
                }
                _lampPatternRunning = true;
                lampPatternButton.Text = "STOP";
                lampPatternButton.BackColor = Color.Firebrick;
                audio.PlaySettingsSound("ledpatternon");
            }
        }

        private void volumeMuteButton_Click(object sender, EventArgs e)
        {
            string mute = audio.GetMasterVolume();

            if (mute.Contains("*"))
            {
                audio.MuteVolume(true);
                audio.PlaySettingsSound("mutebutton");
                volumeMuteButton.BackColor = Color.Black;
                volumeMuteButton.Text = "MUTED";
            }
            else
            {
                volumeMuteButton.BackColor = Color.Tomato;
                audio.PlaySettingsSound("muteoff");
                System.Threading.Thread.Sleep(200);
                audio.MuteVolume(false);

                volumeMuteButton.Text = "mute";
            }
        }
        #endregion Sound Button Click Handlers

        #region Score Lamp Button Click Handlers
        private void scoreLamp10Button_Click(object sender, EventArgs e)
        {
            if (scoreLamp10Button.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("ledbutton");
                scoreLamp10Button.BackColor = Color.SeaGreen;
                gpio.DiagnosticScoreLamps("10", true);
            }
            else
            {
                audio.PlaySettingsSound("ledbuttonoff");
                scoreLamp10Button.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("10", false);
                gpio.SolidColor(Color.Black);
            }
        }

        private void scoreLamp20Button_Click(object sender, EventArgs e)
        {
            ;
            if (scoreLamp20Button.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("ledbutton");
                scoreLamp20Button.BackColor = Color.SeaGreen;
                gpio.DiagnosticScoreLamps("20", true);
            }
            else
            {
                audio.PlaySettingsSound("ledbuttonoff");
                scoreLamp20Button.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("20", false);
                gpio.SolidColor(Color.Black);
            }
        }

        private void scoreLamp30Button_Click(object sender, EventArgs e)
        {
            if (scoreLamp30Button.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("ledbutton");
                scoreLamp30Button.BackColor = Color.SeaGreen;
                gpio.DiagnosticScoreLamps("30", true);
            }
            else
            {
                audio.PlaySettingsSound("ledbuttonoff");
                scoreLamp30Button.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("30", false);
                gpio.SolidColor(Color.Black);
            }
        }

        private void scoreLamp40Button_Click(object sender, EventArgs e)
        {
            if (scoreLamp40Button.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("ledbutton");
                scoreLamp40Button.BackColor = Color.SeaGreen;
                gpio.DiagnosticScoreLamps("40", true);
            }
            else
            {
                audio.PlaySettingsSound("ledbuttonoff");
                scoreLamp40Button.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("40", false);
                gpio.SolidColor(Color.Black);
            }
        }

        private void scoreLamp50Button_Click(object sender, EventArgs e)
        {
            if (scoreLamp50Button.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("ledbutton");
                scoreLamp50Button.BackColor = Color.SeaGreen;
                gpio.DiagnosticScoreLamps("50", true);
            }
            else
            {
                audio.PlaySettingsSound("ledbuttonoff");
                scoreLamp50Button.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("50", false);
                gpio.SolidColor(Color.Black);
            }
        }

        private void scoreLamp60Button_Click(object sender, EventArgs e)
        {
            if (scoreLamp60Button.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("ledbutton");
                scoreLamp60Button.BackColor = Color.SeaGreen;
                gpio.DiagnosticScoreLamps("60", true);
            }
            else
            {
                audio.PlaySettingsSound("ledbuttonoff");
                scoreLamp60Button.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("60", false);
                gpio.SolidColor(Color.Black);
            }
        }

        private void scoreLamp70Button_Click(object sender, EventArgs e)
        {
            if (scoreLamp70Button.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("ledbutton");
                scoreLamp70Button.BackColor = Color.SeaGreen;
                gpio.DiagnosticScoreLamps("70", true);
            }
            else
            {
                audio.PlaySettingsSound("ledbuttonoff");
                scoreLamp70Button.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("70", false);
                gpio.SolidColor(Color.Black);
            }
        }

        private void scoreLamp80Button_Click(object sender, EventArgs e)
        {
            if (scoreLamp80Button.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("ledbutton");
                scoreLamp80Button.BackColor = Color.SeaGreen;
                gpio.DiagnosticScoreLamps("80", true);
            }
            else
            {
                audio.PlaySettingsSound("ledbuttonoff");
                scoreLamp80Button.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("80", false);
                gpio.SolidColor(Color.Black);
            }
        }

        private void scoreLamp90Button_Click(object sender, EventArgs e)
        {
            if (scoreLamp90Button.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("ledbutton");
                scoreLamp90Button.BackColor = Color.SeaGreen;
                gpio.DiagnosticScoreLamps("90", true);
            }
            else
            {
                audio.PlaySettingsSound("ledbuttonoff");
                scoreLamp90Button.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("90", false);
                gpio.SolidColor(Color.Black);
            }
        }

        private void scoreLamp100Button_Click(object sender, EventArgs e)
        {
            if (scoreLamp100Button.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("ledbutton");
                scoreLamp100Button.BackColor = Color.SeaGreen;
                gpio.DiagnosticScoreLamps("100", true);
            }
            else
            {
                audio.PlaySettingsSound("ledbuttonoff");
                scoreLamp100Button.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("100", false);
                gpio.SolidColor(Color.Black);
            }
        }

        private void scoreLamp200Button_Click(object sender, EventArgs e)
        {
            if (scoreLamp200Button.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("ledbutton");
                scoreLamp200Button.BackColor = Color.SeaGreen;
                gpio.DiagnosticScoreLamps("200", true);
            }
            else
            {
                audio.PlaySettingsSound("ledbuttonoff");
                scoreLamp200Button.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("200", false);
                gpio.SolidColor(Color.Black);
            }
        }

        private void scoreLamp300Button_Click(object sender, EventArgs e)
        {
            if (scoreLamp300Button.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("ledbutton");
                scoreLamp300Button.BackColor = Color.SeaGreen;
                gpio.DiagnosticScoreLamps("300", true);
            }
            else
            {
                audio.PlaySettingsSound("ledbuttonoff");
                scoreLamp300Button.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("300", false);
                gpio.SolidColor(Color.Black);
            }
        }

        private void scoreLamp400Button_Click(object sender, EventArgs e)
        {
            if (scoreLamp400Button.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("ledbutton");
                scoreLamp400Button.BackColor = Color.SeaGreen;
                gpio.DiagnosticScoreLamps("400", true);
            }
            else
            {
                audio.PlaySettingsSound("ledbuttonoff");
                scoreLamp400Button.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("400", false);
                gpio.SolidColor(Color.Black);
            }
        }

        private void winnerButton_Click(object sender, EventArgs e)
        {
            if (winnerButton.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("ledbutton");
                winnerButton.BackColor = Color.SeaGreen;
                gpio.DiagnosticScoreLamps("Winner", true);
            }
            else
            {
                audio.PlaySettingsSound("ledbuttonoff");
                winnerButton.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("Winner", false);
                gpio.SolidColor(Color.Black);
            }
        }

        private void gameOverButton_Click(object sender, EventArgs e)
        {
            if (gameOverButton.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("ledbutton");
                gameOverButton.BackColor = Color.SeaGreen;
                gpio.DiagnosticScoreLamps("GameOver", true);
            }
            else
            {
                audio.PlaySettingsSound("ledbuttonoff");
                gameOverButton.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("GameOver", false);
                gpio.SolidColor(Color.Black);
            }
        }

        private void tiltButton_Click(object sender, EventArgs e)
        {
            if (tiltButton.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("ledbutton");
                tiltButton.BackColor = Color.SeaGreen;
                gpio.DiagnosticScoreLamps("Tilt", true);
            }
            else
            {
                audio.PlaySettingsSound("ledbuttonoff");
                tiltButton.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("Tilt", false);
                gpio.SolidColor(Color.Black);
            }
        }

        private void allScoreLampsButton_Click(object sender, EventArgs e)
        {
            if (allScoreLampsButton.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("ledbutton");
                allScoreLampsButton.BackColor = Color.SeaGreen;
                scoreLamp10Button.BackColor = Color.SeaGreen;
                scoreLamp20Button.BackColor = Color.SeaGreen;
                scoreLamp30Button.BackColor = Color.SeaGreen;
                scoreLamp40Button.BackColor = Color.SeaGreen;
                scoreLamp50Button.BackColor = Color.SeaGreen;
                scoreLamp60Button.BackColor = Color.SeaGreen;
                scoreLamp70Button.BackColor = Color.SeaGreen;
                scoreLamp80Button.BackColor = Color.SeaGreen;
                scoreLamp90Button.BackColor = Color.SeaGreen;
                scoreLamp100Button.BackColor = Color.SeaGreen;
                scoreLamp200Button.BackColor = Color.SeaGreen;
                scoreLamp300Button.BackColor = Color.SeaGreen;
                scoreLamp400Button.BackColor = Color.SeaGreen;
                winnerButton.BackColor = Color.SeaGreen;
                gameOverButton.BackColor = Color.SeaGreen;
                tiltButton.BackColor = Color.SeaGreen;
                gpio.DiagnosticScoreLamps("All", true);
                gpio.DiagnosticScoreLamps("10", true);
                gpio.DiagnosticScoreLamps("20", true);
                gpio.DiagnosticScoreLamps("30", true);
                gpio.DiagnosticScoreLamps("40", true);
                gpio.DiagnosticScoreLamps("50", true);
                gpio.DiagnosticScoreLamps("60", true);
                gpio.DiagnosticScoreLamps("70", true);
                gpio.DiagnosticScoreLamps("80", true);
                gpio.DiagnosticScoreLamps("90", true);
                gpio.DiagnosticScoreLamps("100", true);
                gpio.DiagnosticScoreLamps("200", true);
                gpio.DiagnosticScoreLamps("300", true);
                gpio.DiagnosticScoreLamps("400", true);
                gpio.DiagnosticScoreLamps("Winner", true);
                gpio.DiagnosticScoreLamps("GameOver", true);
                gpio.DiagnosticScoreLamps("Tilt", true);
            }
            else
            {
                audio.PlaySettingsSound("ledbuttonoff");
                allScoreLampsButton.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("All", false);
                scoreLamp10Button.BackColor = Color.Black;
                scoreLamp20Button.BackColor = Color.Black;
                scoreLamp30Button.BackColor = Color.Black;
                scoreLamp40Button.BackColor = Color.Black;
                scoreLamp50Button.BackColor = Color.Black;
                scoreLamp60Button.BackColor = Color.Black;
                scoreLamp70Button.BackColor = Color.Black;
                scoreLamp80Button.BackColor = Color.Black;
                scoreLamp90Button.BackColor = Color.Black;
                scoreLamp100Button.BackColor = Color.Black;
                scoreLamp200Button.BackColor = Color.Black;
                scoreLamp300Button.BackColor = Color.Black;
                scoreLamp400Button.BackColor = Color.Black;
                winnerButton.BackColor = Color.Black;
                gameOverButton.BackColor = Color.Black;
                tiltButton.BackColor = Color.Black;
                gpio.SolidColor(Color.Black);
                gpio.DiagnosticScoreLamps("10", false);
                gpio.DiagnosticScoreLamps("20", false);
                gpio.DiagnosticScoreLamps("30", false);
                gpio.DiagnosticScoreLamps("40", false);
                gpio.DiagnosticScoreLamps("50", false);
                gpio.DiagnosticScoreLamps("60", false);
                gpio.DiagnosticScoreLamps("70", false);
                gpio.DiagnosticScoreLamps("80", false);
                gpio.DiagnosticScoreLamps("90", false);
                gpio.DiagnosticScoreLamps("100", false);
                gpio.DiagnosticScoreLamps("200", false);
                gpio.DiagnosticScoreLamps("300", false);
                gpio.DiagnosticScoreLamps("400", false);
                gpio.DiagnosticScoreLamps("Winner", false);
                gpio.DiagnosticScoreLamps("GameOver", false);
                gpio.DiagnosticScoreLamps("Tilt", false);
            }
        }
        #endregion Score Lamp Button Click Handlers

        #region Solenoid Button Click Handlers
        private void winnerLockButton_Click(object sender, EventArgs e)
        {
            if (winnerLockButton.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("solenoidon");
                winnerLockButton.BackColor = Color.Navy;
                gpio.DiagnosticScoreLamps("WinnerLock", true);
            }
            else
            {
                audio.PlaySettingsSound("solenoidoff");
                winnerLockButton.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("WinnerLock", false);
                gpio.SolidColor(Color.Black);
            }
        }

        private void coinLockSolenoid_Click(object sender, EventArgs e)
        {
            if (coinLockSolenoid.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("solenoidon");
                coinLockSolenoid.BackColor = Color.Navy;
                gpio.DiagnosticScoreLamps("CoinLock", true);
            }
            else
            {
                audio.PlaySettingsSound("solenoidoff");
                coinLockSolenoid.BackColor = Color.Black;
                gpio.DiagnosticScoreLamps("CoinLock", false);
                gpio.SolidColor(Color.Black);
            }
        }
        #endregion Solenoid Button Click Handlers


        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loaded == true)
            {
                audio.PlaySettingsSound("select");
            }
        }

        private void comboBox1_DropDown(object sender, EventArgs e)
        {
            audio.PlaySettingsSound("settings");
        }

        private void controlledLEDsCombobox_DropDown(object sender, EventArgs e)
        {
            audio.PlaySettingsSound("settings");
        }

        private void controlledLEDsCombobox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loaded == true)
            {
                audio.PlaySettingsSound("select");
            }
        }

        private void soundPackageComboBox_DropDown(object sender, EventArgs e)
        {
            audio.PlaySettingsSound("settings");
        }

        private void soundPackageComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loaded == true)
            {
                audio.PlaySettingsSound("select");
            }
        }

        private void soundComboBox_DropDown(object sender, EventArgs e)
        {
            audio.PlaySettingsSound("settings");
        }

        private void soundComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loaded == true)
            {
                audio.PlaySettingsSound("select");
            }
        }

        private void soundFXButton_Click(object sender, EventArgs e)
        {
            if (soundFXButton.BackColor == Color.Black)
            {
                audio.PlaySettingsSound("soundfxon");
                soundFXButton.BackColor = Color.SeaGreen;
            }
            else
            {
                audio.PlaySettingsSound("soundfxoff");
                soundFXButton.BackColor = Color.Black;
            }
        }

        private void attractButton_Click(object sender, EventArgs e)
        {
            if (attractButton.BackColor == Color.Black)
            {
                attractButton.BackColor = Color.SeaGreen;
                mainAttractTextBox.Text = "ON";
                attractButton.Text = "ON";
                audio.PlaySettingsSound("attracton");
            }
            else
            {
                attractButton.BackColor = Color.Black;
                mainAttractTextBox.Text = "OFF";
                attractButton.Text = "OFF";
                audio.PlaySettingsSound("attractoff");
            }
        }

        private void distractButton_Click(object sender, EventArgs e)
        {
            if (distractButton.BackColor == Color.Black)
            {
                distractButton.BackColor = Color.SeaGreen;
                mainDistractTextBox.Text = "ON";
                distractButton.Text = "ON";
                audio.PlaySettingsSound("distracton");
            }
            else
            {
                distractButton.BackColor = Color.Black;
                mainDistractTextBox.Text = "OFF";
                distractButton.Text = "OFF";
                audio.PlaySettingsSound("distractoff");
            }
        }

        private void attractLightingButton_Click(object sender, EventArgs e)
        {
            if (attractLightingButton.BackColor == Color.Black)
            {
                attractLightingButton.BackColor = Color.SeaGreen;
                mainAttractLightTextBox.Text = "ON";
                attractLightingButton.Text = "ON";
                audio.PlaySettingsSound("attracton");
            }
            else
            {
                attractLightingButton.BackColor = Color.Black;
                mainAttractLightTextBox.Text = "OFF";
                attractLightingButton.Text = "OFF";
                audio.PlaySettingsSound("attractoff");
            }
        }

        private void distractLightingButton_Click(object sender, EventArgs e)
        {


            if (distractLightingButton.BackColor == Color.Black)
            {
                distractLightingButton.BackColor = Color.SeaGreen;
                mainDistractLightTextBox.Text = "ON";
                distractLightingButton.Text = "ON";
                audio.PlaySettingsSound("distracton");
            }
            else
            {
                gpio.CancelLights();
                distractLightingButton.BackColor = Color.Black;
                mainDistractLightTextBox.Text = "OFF";
                distractLightingButton.Text = "OFF";
                audio.PlaySettingsSound("distractoff");
            }
        }

        private void ledLightingONButton_Click(object sender, EventArgs e)
        {
            if (ledLightingONButton.BackColor == Color.Black)
            {
                ledLightingONButton.BackColor = Color.SeaGreen;
                allLightingTextBox.Text = "ON";
                ledLightingONButton.Text = "ON";
                audio.PlaySettingsSound("distracton");
            }
            else
            {
                ledLightingONButton.BackColor = Color.Black;
                allLightingTextBox.Text = "OFF";
                ledLightingONButton.Text = "OFF";
                audio.PlaySettingsSound("distractoff");
                gpio.CancelLights();
            }

        }

        private void attractTimer_Tick(object sender, EventArgs e)
        {
            if (mainAttractLightTextBox.Text == "ON" && statusTextBox.Text == "Game Not Started")
            {
                gpio.AttractLights();
                gpio.LEDOFF();
                gpio.AttractLamps();   // also run a random score-lamp pattern during attract
            }

        }

        private async void attractSoundTimer_Tick(object sender, EventArgs e)
        {
            if (mainAttractTextBox.Text == "ON" && statusTextBox.Text == "Game Not Started")
            {
                await audio.PlayAttractMusic();
            }
        }
    }
}