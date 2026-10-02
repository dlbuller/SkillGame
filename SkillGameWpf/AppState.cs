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

        // ---- Full UI themes (palette presets). Each entry is the whole palette; switching restarts the app so the
        //      cached views re-skin cleanly (most surfaces use StaticResource, resolved once per view).
        // Order: BgTop,BgBottom, Card,Card2,Border, Gold,GoldDim,Teal,Cyan,Green,Amber,Purple,Red, Text,Muted, Accent,AccentBright,AccentDim
        public static readonly (string name, string blurb)[] ThemeList =
        {
            ("Classy",   "warm walnut & brass"),
            ("Fun",      "bright arcade neon"),
            ("Plain",    "calm neutral graphite"),
            ("Blackout", "OLED black & amber"),
        };
        private static readonly Dictionary<string, string[]> _themes = new()
        {
            ["Classy"]   = new[]{"#241A12","#140D07","#241A11","#2F2318","#47331E","#D7A33C","#7A5A1E","#6E9E8A","#E6D5AC","#97A95E","#E0A736","#BE8A5E","#C25A4C","#F1EAD9","#A7957C","#D7A33C","#E8B84A","#7A5A1E"},
            ["Fun"]      = new[]{"#12121A","#0A0A10","#191922","#20202C","#31313F","#E7A91D","#7A5A12","#16C8B0","#33C9FF","#2FE08C","#F0AA1E","#B45AE0","#E2493C","#ECECF2","#8A8A98","#E7A91D","#F0AA1E","#7A5A12"},
            ["Plain"]    = new[]{"#1A1E26","#10131A","#1F2531","#28303F","#3B4556","#8FB4D6","#45586E","#74A6A6","#BBCBDE","#8FB488","#C2C3CE","#9E9BC4","#CC7A70","#E7EBF1","#8793A3","#8FB4D6","#B6D2EA","#45586E"},
            ["Blackout"] = new[]{"#000000","#000000","#0A0A0A","#141414","#2A2A2A","#FFB000","#8A5F00","#46B9A8","#E0C080","#7BC86A","#FFC94D","#C88AD0","#FF5A4A","#EDEDED","#8A8A8A","#FFB000","#FFC94D","#8A5F00"},
        };
        private static readonly string[] _brushKeys =
        { "","", "CardBrush","CardBrush2","CardBorderBrush","GoldBrush","GoldDimBrush","TealBrush","CyanBrush","GreenBrush","AmberBrush","PurpleBrush","RedBrush","TextBrush","MutedBrush" };

        public static string ThemeAccent(string name) => (_themes.TryGetValue(name, out var p) ? p : _themes["Classy"])[15];

        /// <summary>Swap the whole palette to a named theme. Call before the windows load (App startup); in-session the
        /// Settings screen restarts afterwards so cached views pick it up cleanly.</summary>
        public static void ApplyTheme(string name)
        {
            if (!_themes.TryGetValue(name, out var p)) { p = _themes["Classy"]; name = "Classy"; }
            Settings.Theme = name;
            SetColor("BgTop", p[0]); SetColor("BgBottom", p[1]);
            for (int i = 2; i <= 14; i++) SetBrush(_brushKeys[i], p[i]);
            SetColor("AccentColor", p[15]); SetColor("AccentBright", p[16]); SetColor("AccentDim", p[17]);
            try
            {
                var res = Application.Current?.Resources; if (res == null) return;
                Color bgTop = C(p[0]), bgBot = C(p[1]), card = C(p[2]), card2 = C(p[3]);
                res["AppBg"] = new LinearGradientBrush(
                    new GradientStopCollection { new GradientStop(bgTop, 0), new GradientStop(bgBot, 1) },
                    new Point(0, 0), new Point(0, 1));
                res["GlassFill"] = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 0),
                    new GradientStop(Color.FromArgb(0x14, card2.R, card2.G, card2.B), 0.10),
                    new GradientStop(Color.FromArgb(0x10, card.R, card.G, card.B), 0.55),
                    new GradientStop(Color.FromArgb(0x16, bgBot.R, bgBot.G, bgBot.B), 1),
                }, new Point(0.15, 0), new Point(0.55, 1));
            }
            catch { }
        }
        private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        /// <summary>Relaunch the app (used after a theme change so every cached view rebuilds with the new palette).</summary>
        public static void Restart()
        {
            try
            {
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe)) System.Diagnostics.Process.Start(exe);
            }
            catch { }
            Application.Current?.Shutdown();
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
