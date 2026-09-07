using System;
using System.Collections.Generic;
using System.Device.Gpio;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SkillGame
{
    // Switch input layer: three FT232H polling loops feed one dispatcher.
    // On the game tab a change goes to the GameEngine; on the diagnostics tab it lights a check box.
    public partial class GPIOFunctions
    {
        private GameEngine engine = null!;                  // built in InitializeGPIO
        private Dictionary<string, CheckBox[]>? _diagBoxes;

        // When set (by the Wiring Test window), every recognised switch change is routed here instead of
        // to the game / diagnostics check boxes, so the test has exclusive use of the switches.
        private Action<SwitchDef, bool>? _switchObserver;
        public void SetSwitchObserver(Action<SwitchDef, bool>? observer) => _switchObserver = observer;

        private void StartGPIO()
        {
            // Seed the "last value" lists with the current pin states.
            foreach (int pin in hardware.PinsGPIO1Input) GPIOpinsLastValueBoard1.Add(hardware.ControllerGPIO1!.Read(pin).ToString());
            foreach (int pin in hardware.PinsGPIO2Input) GPIOpinsLastValueBoard2.Add(hardware.ControllerGPIO2!.Read(pin).ToString());
            foreach (int pin in hardware.PinsGPIO3Input) GPIOpinsLastValueBoard3.Add(hardware.ControllerGPIO3!.Read(pin).ToString());

            // One polling task per board (each reads an independent FT232H device).
            Task.Run(() => PollBoard(1, hardware.ControllerGPIO1!, hardware.PinsGPIO1Input, GPIOpinsLastValueBoard1));
            Task.Run(() => PollBoard(2, hardware.ControllerGPIO2!, hardware.PinsGPIO2Input, GPIOpinsLastValueBoard2));
            Task.Run(() => PollBoard(3, hardware.ControllerGPIO3!, hardware.PinsGPIO3Input, GPIOpinsLastValueBoard3));
        }

        // Poll one board's pins; fire OnSwitch whenever a pin changes state. Stops when _cts is cancelled.
        private async Task PollBoard(int board, GpioController controller, int[] pins, List<string> last)
        {
            while (!_cts.IsCancellationRequested)
            {
                for (int i = 0; i < pins.Length; i++)
                {
                    string value = controller.Read(pins[i]).ToString();
                    if (value != last[i])
                    {
                        last[i] = value;
                        await OnSwitch(board, pins[i], value == "High");
                    }
                }
                await Task.Delay(50); // polling interval
            }
        }

        // Route a switch change: game tab -> scoring/game rules; diagnostics tab -> check box.
        private async Task OnSwitch(int board, int pin, bool high)
        {
            SwitchDef? def = SwitchMap.Find(board, pin);
            if (def == null) return;

            PinActivity.Set(board, pin, high);   // light this input on the I/O Map

            // Wiring Test (when open) takes over the switches entirely.
            var observer = _switchObserver;
            if (observer != null) { observer(def, high); return; }

            string tab = CheckTab(_mainForm);
            if (tab == "gameStatusTab")
            {
                if (def.CancelBefore) CancelLights();

                switch (def.Kind)
                {
                    case SwitchKind.Coin:   engine.Coin(high); break;
                    case SwitchKind.Score:  engine.Hit(def, high); break;
                    case SwitchKind.Gobble: engine.Gobble(high); break;
                    case SwitchKind.Tilt:   engine.Tilt(high); break;
                }

                if (def.Distract)
                {
                    if (_mainForm.mainDistractLightTextBox.Text == "ON") DistractLights();
                    if (_mainForm.mainDistractTextBox.Text == "ON") await audio.PlayDistractMusic();
                }
            }
            else if (tab == "diagnosticsTab")
            {
                UpdateDiagnostic(def, high);
            }
        }

        // Diagnostics tab: reflect the switch on its check box(es). Gobble (S23) drives all seven.
        private void UpdateDiagnostic(SwitchDef def, bool high)
        {
            _diagBoxes ??= BuildDiagnosticBoxes();
            if (!_diagBoxes.TryGetValue(def.Id, out var boxes)) return;

            Color color = high ? Color.FromArgb(73, 79, 85) : Color.Black;
            _mainForm.Invoke(new Action(() =>
            {
                foreach (CheckBox cb in boxes) { cb.Checked = high; cb.BackColor = color; }
            }));
        }

        // Switch id -> the diagnostics check box(es) it drives. References existing form controls only.
        private Dictionary<string, CheckBox[]> BuildDiagnosticBoxes() => new()
        {
            ["S0"]  = new[] { _mainForm.coinUpCheckBox },
            ["S1"]  = new[] { _mainForm.L1_10PointCheckBox },
            ["S2"]  = new[] { _mainForm.L1_30PointCheckBox },
            ["S3"]  = new[] { _mainForm.L1_50PointCheckBox },
            ["S4"]  = new[] { _mainForm.L1_20PointCheckBox },
            ["S5"]  = new[] { _mainForm.L2_20PointCheckBox },
            ["S6"]  = new[] { _mainForm.L2_50PointCheckBox },
            ["S7"]  = new[] { _mainForm.L2_30PointCheckBox },
            ["S8"]  = new[] { _mainForm.L2_10PointCheckBox },
            ["S9"]  = new[] { _mainForm.L3_10PointCheckBox },
            ["S10"] = new[] { _mainForm.L3_30PointCheckBox },
            ["S11"] = new[] { _mainForm.L3_50PointCheckBox },
            ["S12"] = new[] { _mainForm.L3_20PointCheckBox },
            ["S13"] = new[] { _mainForm.L4_10PointCheckbox },
            ["S14"] = new[] { _mainForm.L4_50PointCheckbox },
            ["S15"] = new[] { _mainForm.L4_30PointCheckbox },
            ["S16"] = new[] { _mainForm.L4_20PointCheckbox },
            ["S17"] = new[] { _mainForm.L5_10PointCheckbox },
            ["S18"] = new[] { _mainForm.L5_30PointCheckbox },
            ["S19"] = new[] { _mainForm.L5_50PointCheckbox },
            ["S20"] = new[] { _mainForm.L5_20PointCheckbox },
            ["S21"] = new[] { _mainForm.L6_60PointCheckbox },
            ["S22"] = new[] { _mainForm.L6_40PointCheckbox },
            ["S23"] = new[] { _mainForm.L6_Gobble1Checkbox, _mainForm.L6_Gobble2Checkbox,
                              _mainForm.L7_Gobble3CheckBox, _mainForm.L7_Gobble4Checkbox,
                              _mainForm.L8_Gobble5Checkbox, _mainForm.L8_Gobble6Checkbox, _mainForm.L8_Gobble7Checkbox },
            ["S24"] = new[] { _mainForm.L7_70PointCheckbox },
            ["S25"] = new[] { _mainForm.L8_80PointCheckbox },
            ["S26"] = new[] { _mainForm.L8_80Points2Checkbox },
            ["S27"] = new[] { _mainForm.tiltCheckBox },
        };
    }
}
