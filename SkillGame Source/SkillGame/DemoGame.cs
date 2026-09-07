using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SkillGame
{
    /// <summary>How a demo game ends, so all three outcomes (and their effects/faces) can be shown.</summary>
    public enum DemoEnding { Winner, Lose, Tilt }

    /// <summary>Bench self-test that drives a GameEngine through a scripted game with no playfield attached, ending as a winner, a gobble loss, or a tilt.</summary>
    public static class DemoGame
    {
        private static readonly System.Random _rng = new System.Random();

        public static async Task RunAsync(GameEngine engine, int stepMs = 450, CancellationToken token = default,
                                          DemoEnding ending = DemoEnding.Winner, Func<CancellationToken, Task>? awaitResume = null)
        {
            async Task Gate() { if (awaitResume != null) await awaitResume(token); }

            engine.RecordAudits = false;   // the demo is software-driven, not physical play — it must never touch the audits
            try
            {
            await Gate();
            engine.Coin(true);
            await Wait(stepMs, token, awaitResume);

            // A winner needs all 8 levels, a loss plays down into the gobble region, and a tilt can happen anywhere.
            int topLevel = ending switch { DemoEnding.Winner => 8, DemoEnding.Lose => 6, _ => 3 };
            for (int lvl = 1; lvl <= topLevel; lvl++)
            {
                token.ThrowIfCancellationRequested();
                await Gate();
                var choices = SwitchMap.All.Where(s => s.Kind == SwitchKind.Score && s.Level == lvl).ToList();
                SwitchDef? hit = PickHole(lvl, choices);   // random hole, avoiding last run's pick on this row
                if (hit != null) engine.Hit(hit, true);
                int wait = hit != null && hit.Distract ? stepMs + 2000 : stepMs;   // linger on a distract so its show plays
                await Wait(wait, token, awaitResume);
            }

            await Gate();
            // The level-8 hit above already wins; a loss or tilt ends the game here.
            if (ending == DemoEnding.Lose) engine.Gobble(true);
            else if (ending == DemoEnding.Tilt)
            {
                // Show the nudge mechanic: two warnings, then the tilt that ends the game.
                int allowed = engine.TiltsAllowed;
                engine.TiltsAllowed = 3;
                for (int i = 0; i < 3; i++)
                {
                    await Gate();
                    engine.Tilt(true);
                    if (i < 2) await Wait(1700, token, awaitResume);
                }
                engine.TiltsAllowed = allowed;
            }
            }
            finally { engine.RecordAudits = true; }   // real play counts again the moment the demo ends
        }

        // Pick a random hole in the row, skipping the one used on this row last run so consecutive demos vary.
        private static readonly Dictionary<int, string> _lastByLevel = new();
        private static SwitchDef? PickHole(int lvl, List<SwitchDef> choices)
        {
            if (choices.Count == 0) return null;
            List<SwitchDef> pool = choices;
            if (choices.Count > 1 && _lastByLevel.TryGetValue(lvl, out var last))
            {
                var others = choices.Where(c => c.Id != last).ToList();
                if (others.Count > 0) pool = others;
            }
            var pick = pool[_rng.Next(pool.Count)];
            _lastByLevel[lvl] = pick.Id;
            return pick;
        }

        // Delay that only counts down while the demo is running, so time doesn't pass while the page is away.
        private static async Task Wait(int ms, CancellationToken token, Func<CancellationToken, Task>? awaitResume)
        {
            const int chunk = 120;
            for (int e = 0; e < ms; e += chunk)
            {
                if (awaitResume != null) await awaitResume(token);
                await Task.Delay(System.Math.Min(chunk, ms - e), token);
            }
        }
    }
}
