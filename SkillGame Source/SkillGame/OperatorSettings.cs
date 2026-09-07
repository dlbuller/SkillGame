using System;
using System.IO;
using System.Text.Json;

namespace SkillGame
{
    /// <summary>Operator-tunable game settings, persisted to operator_settings.json next to the exe and pushed into the live subsystems at startup.</summary>
    public sealed class OperatorSettings
    {
        public int CountUpStepMs { get; set; } = 60;      // score count-up sweep speed (lower = faster)
        public bool TiltEnabled { get; set; } = true;     // honor the tilt switch (off = ignore a finicky tilt)
        public int TiltsAllowed { get; set; } = 0;        // free tilt nudges before game over (0 = first tilt ends it; N = N warnings first)
        public int AttractLightSeconds { get; set; } = 120;  // how often the idle light show plays
        public int AttractSoundSeconds { get; set; } = 120;  // how often the idle attract sound plays
        public bool QuietHoursEnabled { get; set; } = false;
        public int QuietStartHour { get; set; } = 22;     // 0-23
        public int QuietEndHour { get; set; } = 8;        // 0-23
        public int QuietVolumePercent { get; set; } = 20; // cap master volume to this while quiet

        // Sound and lighting, persisted across restarts.
        public string SoundPackage { get; set; } = "8-Bit";
        public bool SoundFxOn { get; set; } = true;
        public bool AttractSoundOn { get; set; } = true;
        public bool AttractLightsOn { get; set; } = true;
        public bool DistractSoundOn { get; set; } = true;
        public bool DistractLightsOn { get; set; } = true;
        public bool ShowGremlin { get; set; } = true;   // the diagnostics mechanic gremlin
        public string AccentColor { get; set; } = "Gold";   // UI accent theme
        public int ScreenBrightness { get; set; } = 100;    // 30-100; dims the whole screen for the cabinet
        public bool BenchMode { get; set; }   // ignore live switch inputs (for bench testing with nothing wired)
        public bool TutorialSeen { get; set; } // the first-run tour has been shown once (replayable from Service)

        private static string FileLocation =>
            Path.Combine(AppContext.BaseDirectory, "operator_settings.json");

        public static OperatorSettings Load()
        {
            try
            {
                if (File.Exists(FileLocation))
                    return JsonSerializer.Deserialize<OperatorSettings>(File.ReadAllText(FileLocation)) ?? new OperatorSettings();
            }
            catch (Exception ex) { Log.Error("OperatorSettings load failed", ex); }
            return new OperatorSettings();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FileLocation, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                Log.Info("Operator settings saved.");
            }
            catch (Exception ex) { Log.Error("OperatorSettings save failed", ex); }
        }

        /// <summary>Restore every setting to its factory default (in place, so existing references stay valid).</summary>
        public void ResetToDefaults()
        {
            var d = new OperatorSettings();
            CountUpStepMs = d.CountUpStepMs; TiltEnabled = d.TiltEnabled; TiltsAllowed = d.TiltsAllowed;
            AttractLightSeconds = d.AttractLightSeconds; AttractSoundSeconds = d.AttractSoundSeconds;
            QuietHoursEnabled = d.QuietHoursEnabled; QuietStartHour = d.QuietStartHour; QuietEndHour = d.QuietEndHour; QuietVolumePercent = d.QuietVolumePercent;
            SoundPackage = d.SoundPackage; SoundFxOn = d.SoundFxOn; AttractSoundOn = d.AttractSoundOn; AttractLightsOn = d.AttractLightsOn;
            DistractSoundOn = d.DistractSoundOn; DistractLightsOn = d.DistractLightsOn;
            ShowGremlin = d.ShowGremlin; AccentColor = d.AccentColor; ScreenBrightness = d.ScreenBrightness; BenchMode = d.BenchMode;
        }

        /// <summary>Clamps every field to a sane range.</summary>
        public void Clamp()
        {
            CountUpStepMs = Math.Clamp(CountUpStepMs, 10, 300);
            TiltsAllowed = Math.Clamp(TiltsAllowed, 0, 5);
            AttractLightSeconds = Math.Clamp(AttractLightSeconds, 10, 600);
            AttractSoundSeconds = Math.Clamp(AttractSoundSeconds, 10, 600);
            QuietStartHour = Math.Clamp(QuietStartHour, 0, 23);
            QuietEndHour = Math.Clamp(QuietEndHour, 0, 23);
            QuietVolumePercent = Math.Clamp(QuietVolumePercent, 0, 100);
            ScreenBrightness = Math.Clamp(ScreenBrightness, 30, 100);
        }

        /// <summary>Push the quiet-hours values into the live QuietHours config.</summary>
        public void ApplyToQuietHours()
        {
            QuietHours.Enabled = QuietHoursEnabled;
            QuietHours.StartHour = QuietStartHour;
            QuietHours.EndHour = QuietEndHour;
            QuietHours.QuietPercent = QuietVolumePercent;
        }
    }
}
