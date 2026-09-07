using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using SkillGame;

namespace SkillGameWpf
{
    /// <summary>
    /// One shared audit store and operator-settings object so every screen reads and writes the same state.
    /// </summary>
    public static class AppState
    {
        public static readonly AuditStore Audits = new();
        public static OperatorSettings Settings = OperatorSettings.Load();

        /// <summary>One shared sound engine for the whole app (scoring, attract, settings clicks, tests).</summary>
        public static readonly AudioFunctions Audio = new();

        // Sound/lighting toggles — backed by the persisted OperatorSettings so they survive restarts.
        public static string SoundPackage { get => Settings.SoundPackage; set => Settings.SoundPackage = value; }
        public static bool SoundFxOn { get => Settings.SoundFxOn; set => Settings.SoundFxOn = value; }
        public static bool AttractSoundOn { get => Settings.AttractSoundOn; set => Settings.AttractSoundOn = value; }
        public static bool AttractLightsOn { get => Settings.AttractLightsOn; set => Settings.AttractLightsOn = value; }
        public static bool DistractSoundOn { get => Settings.DistractSoundOn; set => Settings.DistractSoundOn = value; }
        public static bool DistractLightsOn { get => Settings.DistractLightsOn; set => Settings.DistractLightsOn = value; }
        public static void PersistSettings() => Settings.Save();

        // Selectable UI accent themes: (main, dim, bright) — mutating the shared brushes recolors the whole app.
        public static readonly Dictionary<string, (string main, string dim, string bright)> Accents = new()
        {
            ["Gold"] = ("#E7A91D", "#7A5A12", "#F0AA1E"),
            ["Cyan"] = ("#33C9FF", "#1A6B8C", "#7FDCFF"),
            ["Green"] = ("#2FE08C", "#1A7A4C", "#7FF0B8"),
            ["Purple"] = ("#B45AE0", "#6A2A8C", "#CE8CF0"),
            ["Red"] = ("#E2493C", "#8A2A22", "#F07A6E"),
        };

        public static void ApplyAccent(string name)
        {
            if (!Accents.TryGetValue(name, out var a)) { a = Accents["Gold"]; name = "Gold"; }
            Settings.AccentColor = name;
            SetBrush("GoldBrush", a.main); SetBrush("GoldDimBrush", a.dim); SetBrush("AmberBrush", a.bright);
            SetColor("AccentColor", a.main); SetColor("AccentBright", a.bright); SetColor("AccentDim", a.dim);
        }

        // Colors are structs (never frozen), so replacing the entry is enough; DynamicResource users (control glows) update.
        private static void SetColor(string key, string hex)
        {
            try
            {
                var res = Application.Current?.Resources;
                if (res != null) res[key] = (Color)ColorConverter.ConvertFromString(hex);
            }
            catch { }
        }

        // The accent brushes are referenced via DynamicResource, so replacing the dictionary entry recolors every
        // user live. Mutating in place won't work: WPF freezes Application-level brushes.
        private static void SetBrush(string key, string hex)
        {
            try
            {
                var res = Application.Current?.Resources;
                if (res != null) res[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch { }
        }

        /// <summary>Set by the HardwareCoordinator; the Settings screen calls it after a Save so count-up
        /// speed / tilt take effect on the live subsystems immediately.</summary>
        public static Action<OperatorSettings>? ApplySettings;

        /// <summary>Set by the HardwareCoordinator; re-baselines the switch poll so every input re-reports its
        /// current state (used when Bench Mode turns off, so stuck/floating switches get re-detected).</summary>
        public static Action? ResyncInputs;

        /// <summary>Latest per-board presence (index 0..3 = board 1..4). Pushed by the coordinator via MainWindow so
        /// any screen (e.g. the live Schematic) can show which FT232H boards are connected.</summary>
        public static bool[] BoardPresence = new bool[4];
        public static void SetBoardPresence(bool[] p) { if (p != null && p.Length >= 4) BoardPresence = p; }

        /// <summary>Raised whenever Bench Mode is switched. The coordinator clears stuck-switch tracking when it
        /// turns on (so a bench run starts clean) and re-baselines the inputs when it turns off; Diagnostics
        /// clears its own stuck display to match.</summary>
        public static Action<bool>? BenchModeChanged;

        /// <summary>Single entry point both toggles (Settings + Game Status) use, so turning Bench Mode on/off
        /// always clears/re-checks stuck switches everywhere.</summary>
        public static void SetBenchMode(bool on)
        {
            Settings.BenchMode = on;
            Settings.Save();
            BenchModeChanged?.Invoke(on);
        }

        static AppState()
        {
            Audits.Load();
            Settings.Clamp();
            Settings.ApplyToQuietHours();
        }
    }

    /// <summary>The screen-specific sound packages available under C:\SkillGame\Sounds (enumerated at
    /// runtime; falls back to a sensible default when the folder isn't present).</summary>
    public static class SoundPackages
    {
        private static readonly string Root = @"C:\SkillGame\Sounds";
        private static readonly string[] Reserved = { "Startup", "Attract", "Distract", "Settings" };

        public static System.Collections.Generic.List<string> List()
        {
            var packs = new System.Collections.Generic.List<string>();
            try
            {
                if (System.IO.Directory.Exists(Root))
                    foreach (var d in System.IO.Directory.GetDirectories(Root))
                    {
                        string name = System.IO.Path.GetFileName(d);
                        if (System.Array.IndexOf(Reserved, name) < 0) packs.Add(name);
                    }
            }
            catch { }
            if (packs.Count == 0) packs.Add("8-Bit");
            return packs;
        }
    }
}
