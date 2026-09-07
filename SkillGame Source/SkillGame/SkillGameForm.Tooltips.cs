using System.Collections.Generic;
using System.Windows.Forms;

namespace SkillGame
{
    // Hover help for every interactive control, so anyone servicing the machine knows exactly what a
    // button does. One shared ToolTip; Designer controls are set here by name, and the code-built
    // controls (Audits / Hardware / Setup / Game Status) set their own tips as they're created.
    public partial class SkillGameForm
    {
        internal readonly ToolTip _tips = new()
        {
            AutoPopDelay = 20000, InitialDelay = 350, ReshowDelay = 100, ShowAlways = true,
        };

        private void BuildToolTips()
        {
            var map = new Dictionary<string, string>
            {
                // --- Diagnostics: score lamps (green while lit) ---
                ["scoreLamp10Button"] = "Toggle the 10-point score lamp (GPIO4 D4). Green = lit.",
                ["scoreLamp20Button"] = "Toggle the 20-point score lamp (GPIO4 D5). Green = lit.",
                ["scoreLamp30Button"] = "Toggle the 30-point score lamp (GPIO4 D6). Green = lit.",
                ["scoreLamp40Button"] = "Toggle the 40-point score lamp (GPIO4 D7). Green = lit.",
                ["scoreLamp50Button"] = "Toggle the 50-point score lamp (GPIO4 C0). Green = lit.",
                ["scoreLamp60Button"] = "Toggle the 60-point score lamp (GPIO4 C1). Green = lit.",
                ["scoreLamp70Button"] = "Toggle the 70-point score lamp (GPIO4 C2). Green = lit.",
                ["scoreLamp80Button"] = "Toggle the 80-point score lamp (GPIO4 C3). Green = lit.",
                ["scoreLamp90Button"] = "Toggle the 90-point score lamp (GPIO4 C4). Green = lit.",
                ["scoreLamp100Button"] = "Toggle the 100-point reel (GPIO3 C2). Green = lit.",
                ["scoreLamp200Button"] = "Toggle the 200-point reel (GPIO3 C3). Green = lit.",
                ["scoreLamp300Button"] = "Toggle the 300-point reel (GPIO3 C4). Green = lit.",
                ["scoreLamp400Button"] = "Toggle the 400-point reel (GPIO3 C5). Green = lit.",
                ["winnerButton"] = "Toggle the Winner lamp (GPIO3 C7). Green = lit.",
                ["gameOverButton"] = "Toggle the Game Over lamp (GPIO3 C6). Green = lit.",
                ["tiltButton"] = "Toggle the Tilt lamp (GPIO4 C5). Green = lit.",
                ["allScoreLampsButton"] = "Light every score lamp at once, or clear them all.",
                ["lampPatternButton"] = "Start / stop the selected score-lamp attract pattern.",
                ["lampPatternComboBox"] = "Pick the score-lamp pattern: Chase, Inside Out, or Flash.",

                // --- Diagnostics: WS2812b LED strip ---
                ["controlledLEDsButton"] = "Start / stop the selected LED-strip animation.",
                ["controlledLEDsCombobox"] = "Pick the LED-strip animation to run.",
                ["redLEDButton"] = "Set the whole LED strip to solid red.",
                ["greenLEDButton"] = "Set the whole LED strip to solid green.",
                ["blueLEDButton"] = "Set the whole LED strip to solid blue.",
                ["yellowLEDButton"] = "Set the whole LED strip to solid yellow.",
                ["purpleLEDButton"] = "Set the whole LED strip to solid purple.",
                ["whiteLEDButton"] = "Set the whole LED strip to solid white.",
                ["brightnessUpButton"] = "Step the LED-strip brightness up.",
                ["brightnessDownButton"] = "Step the LED-strip brightness down.",
                ["clearAllLEDsButton"] = "Turn the LED strip off.",

                // --- Diagnostics: sound + solenoids ---
                ["diagnosticSoundButton"] = "Play the chosen sound from the chosen package.",
                ["soundPackageComboBox"] = "Sound package to preview from.",
                ["soundComboBox"] = "Which sound to preview.",
                ["soundFXButton"] = "Play a quick sound-effect test.",
                ["winnerLockButton"] = "Fire the Win-Lock solenoid (GPIO3 C1). Blue = energized.",
                ["coinLockSolenoid"] = "Fire the Coin-Lock solenoid (GPIO3 C0). Blue = energized.",

                // --- Settings ---
                ["volumeUpButton"] = "Raise the Windows master volume.",
                ["volumeDownButton"] = "Lower the Windows master volume.",
                ["volumeMuteButton"] = "Mute / unmute the master volume.",
                ["soundPackageCombo"] = "Choose the sound package the game plays from.",
                ["attractButton"] = "ATTRACT sound: when no one is playing, the machine plays an attract sound every so often to draw players in (interval set in Operator Setup). On/off.",
                ["distractButton"] = "DISTRACT sound: extra sound effects during play when a high-value hole is hit. On/off.",
                ["ledLightingONButton"] = "Master on/off for the whole WS2812b LED strip.",
                ["attractLightingButton"] = "ATTRACT light show: when idle, the LED strip plays an animation at the set interval (Operator Setup). On/off.",
                ["distractLightingButton"] = "DISTRACT lights: a burst of LED animation during play when a high-value hole is hit. On/off.",

                // --- nav buttons (if present) ---
                ["gameStatusButton"] = "Go to the Game Status page.",
                ["settingsButton"] = "Go to the Settings page.",
                ["diagnosticsButton"] = "Go to the Diagnostics page.",
                ["auditsButton"] = "Go to the Audits page.",
            };

            foreach (var kv in map)
            {
                var found = Controls.Find(kv.Key, true);
                if (found.Length > 0) _tips.SetToolTip(found[0], kv.Value);
            }
        }
    }
}
