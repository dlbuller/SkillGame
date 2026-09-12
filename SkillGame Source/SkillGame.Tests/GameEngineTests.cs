using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SkillGame;
using Xunit;

namespace SkillGame.Tests
{
    // ---- lightweight test doubles for the game's dependencies ----
    internal class FakeView : IGameView
    {
        public string SoundPackage { get; set; } = "8-Bit";
        public int Score, Level;
        public string Last = "", Status = "";
        public void ShowStatus(int score, int level, string lastSwitch, string status)
        { Score = score; Level = level; Last = lastSwitch; Status = status; }
        public void ShowScoreLevel(int score, int level) { Score = score; Level = level; }
    }

    internal class FakeAudio : IAudioSink
    {
        public readonly List<(string pkg, string file)> Played = new();
        public void PlaySound(string soundPackage, string file) => Played.Add((soundPackage, file));
    }

    internal class FakeLamps : ILampSink
    {
        public int? LastTally;
        public int WinnerCalls, FlourishCalls;
        public readonly List<(string lamp, bool on)> Lamps = new();
        public readonly List<string> Pulses = new();
        public void TallyScore(int value) => LastTally = value;
        public void Winner() => WinnerCalls++;
        public void SetLamp(string lamp, bool on) => Lamps.Add((lamp, on));
        public void Flourish(int finalScore) { FlourishCalls++; LastTally = finalScore; }
        public void PulseSolenoid(string name, int ms = 250, int startDelayMs = 0) => Pulses.Add(name);
    }

    internal class FakeAudit : IAuditSink
    {
        public int Started, Ended, WonCount;
        public int LastFinalScore = -1;
        public readonly List<string> Hits = new();
        public void GameStarted() => Started++;
        public void GameEnded(int finalScore) { Ended++; LastFinalScore = finalScore; }
        public void Won() => WonCount++;
        public void SwitchHit(string switchId) => Hits.Add(switchId);
    }

    internal class FakeLed : ILedSink
    {
        public int Coins, Scores, Winners;
        public bool? LastGameOverTilt;
        public void Coin() => Coins++;
        public int LastLevel;
        public void Score(int points, int level) { Scores++; LastLevel = level; }
        public void Winner() => Winners++;
        public void GameOver(bool tilt) => LastGameOverTilt = tilt;
    }

    public class GameEngineTests
    {
        private static SwitchDef Hole(int points, int level, bool winner = false) =>
            new SwitchDef(1, 5, "Sx", "Sx", points, level, SwitchKind.Score, isWinner: winner);

        private static (GameEngine e, FakeView v, FakeAudio a, FakeLamps l) New()
        {
            var v = new FakeView(); var a = new FakeAudio(); var l = new FakeLamps();
            return (new GameEngine(v, a, l), v, a, l);
        }

        [Fact]
        public void Coin_arms_game_at_level_1()
        {
            var (e, _, _, _) = New();
            e.Coin(true);
            Assert.True(e.GameInProgress);
            Assert.Equal(1, e.Level);
            Assert.Equal(0, e.Score);
        }

        [Fact]
        public void Coin_low_signal_does_not_arm()
        {
            var (e, _, _, _) = New();
            e.Coin(false);
            Assert.False(e.GameInProgress);
            Assert.Equal(0, e.Level);
        }

        [Fact]
        public void Hit_on_wrong_level_is_ignored()
        {
            var (e, _, a, _) = New();
            e.Coin(true);                       // level 1
            e.Hit(Hole(50, level: 2), true);    // ball is not on this row
            Assert.Equal(0, e.Score);
            Assert.Equal(1, e.Level);
            Assert.Empty(a.Played);
        }

        [Fact]
        public void Hit_on_right_level_scores_advances_and_plays_spaced_key()
        {
            var (e, _, a, l) = New();
            e.Coin(true);                       // level 1
            e.Hit(Hole(50, level: 1), true);
            Assert.Equal(50, e.Score);
            Assert.Equal(2, e.Level);
            Assert.Equal(50, l.LastTally);
            Assert.Contains(("8-Bit", "50 pts"), a.Played);   // spaced key — the bug fix
        }

        [Fact]
        public void Points_accumulate_across_rows()
        {
            var (e, _, _, _) = New();
            e.Coin(true);
            e.Hit(Hole(50, 1), true);   // -> level 2, score 50
            e.Hit(Hole(30, 2), true);   // -> level 3, score 80
            Assert.Equal(80, e.Score);
            Assert.Equal(3, e.Level);
        }

        [Fact]
        public void Hit_low_signal_is_ignored()
        {
            var (e, _, a, _) = New();
            e.Coin(true);
            e.Hit(Hole(50, 1), false);
            Assert.Equal(0, e.Score);
            Assert.Empty(a.Played);
        }

        [Fact]
        public void Winner_hole_lights_the_winner_lamp()
        {
            var (e, _, _, l) = New();
            e.Coin(true);                                   // level 1
            e.Hit(Hole(80, level: 1, winner: true), true);
            Assert.Equal(1, l.WinnerCalls);
        }

        [Fact]
        public void Coin_up_pulses_the_coin_lock_to_drop_the_coin()
        {
            var (e, _, _, l) = New();
            e.Coin(true);
            Assert.Contains("CoinLock", l.Pulses);       // the inserted coin is dropped onto the rails
        }

        [Fact]
        public void Winning_does_not_energize_a_coil()
        {
            var (e, _, _, l) = New();
            e.Coin(true);                                    // pulses CoinLock (the coin-up drop)
            l.Pulses.Clear();
            e.Hit(Hole(80, level: 1, winner: true), true);   // win — the coin is held by spring, no coil fires
            Assert.Empty(l.Pulses);
        }

        [Fact]
        public void Winner_coin_is_released_on_the_next_coin_up()
        {
            var (e, _, _, l) = New();
            e.Coin(true);
            e.Hit(Hole(80, level: 1, winner: true), true);   // win — Win-Lock holds the coin
            l.Pulses.Clear();
            e.Coin(true);                                    // next game: release the held winner + drop the new coin
            Assert.Contains("WinnerLock", l.Pulses);
            Assert.Contains("CoinLock", l.Pulses);
        }

        [Fact]
        public void Demo_game_never_touches_a_coil()
        {
            var (e, _, _, l) = New();
            e.RecordAudits = false;                          // attract/demo play drives the engine in software only
            e.Coin(true);
            e.Hit(Hole(80, level: 1, winner: true), true);
            e.Coin(true);
            Assert.Empty(l.Pulses);
        }

        [Fact]
        public void Gobble_ends_game_and_lights_gameover()
        {
            var (e, _, _, l) = New();
            e.Coin(true);
            e.Gobble(true);
            Assert.False(e.GameInProgress);
            Assert.Contains(("GameOver", true), l.Lamps);
        }

        [Fact]
        public void Tilt_ends_game_and_lights_tilt_and_gameover()
        {
            var (e, _, _, l) = New();
            e.Coin(true);
            e.Tilt(true);
            Assert.False(e.GameInProgress);
            Assert.Contains(("Tilt", true), l.Lamps);
            Assert.Contains(("GameOver", true), l.Lamps);
        }

        [Fact]
        public void Big_hit_triggers_lamp_flourish()
        {
            var (e, _, _, l) = New();
            e.Coin(true);
            e.Hit(Hole(50, level: 1), true);   // >= 50 pts -> crazy flourish, then the score
            Assert.Equal(1, l.FlourishCalls);
            Assert.Equal(50, l.LastTally);
        }

        [Fact]
        public void Small_hit_uses_normal_countup()
        {
            var (e, _, _, l) = New();
            e.Coin(true);
            e.Hit(Hole(10, level: 1), true);   // < 50 pts -> normal count-up, no flourish
            Assert.Equal(0, l.FlourishCalls);
            Assert.Equal(10, l.LastTally);
        }

        [Fact]
        public void Tilt_disabled_is_ignored()
        {
            var (e, _, _, l) = New();
            e.TiltEnabled = false;
            e.Coin(true);
            e.Tilt(true);
            Assert.True(e.GameInProgress);                 // game keeps going
            Assert.DoesNotContain(("Tilt", true), l.Lamps); // tilt lamp not lit
        }

        [Fact]
        public void ClearGame_resets_score_level_and_lamps()
        {
            var (e, v, _, l) = New();
            e.Coin(true);
            e.Hit(Hole(50, 1), true);
            e.ClearGame();
            Assert.Equal(0, e.Score);
            Assert.Equal(0, e.Level);
            Assert.Equal(0, l.LastTally);   // lamps cleared
            Assert.Equal(0, v.Score);       // view updated
        }

        [Fact]
        public void Audit_and_led_reactions_fire_on_events()
        {
            var au = new FakeAudit(); var led = new FakeLed();
            var e = new GameEngine(new FakeView(), new FakeAudio(), new FakeLamps(), au, led);
            e.Coin(true);
            Assert.Equal(1, au.Started);
            Assert.Equal(1, led.Coins);
            e.Hit(Hole(80, level: 1, winner: true), true);
            Assert.Contains("Sx", au.Hits);
            Assert.Equal(1, au.WonCount);
            Assert.Equal(1, led.Winners);
            Assert.Equal(1, au.Ended);   // the winner already ended the game
        }

        [Fact]
        public void Tilt_signals_a_tilt_gameover_to_the_led_layer()
        {
            var au = new FakeAudit(); var led = new FakeLed();
            var e = new GameEngine(new FakeView(), new FakeAudio(), new FakeLamps(), au, led) { TiltsAllowed = 0 };
            e.Coin(true);
            e.Tilt(true);
            Assert.False(e.GameInProgress);
            Assert.Equal(1, au.Ended);
            Assert.Equal(true, led.LastGameOverTilt);
        }

        // --- tilt "warnings before game over" semantics: N allowed = N warnings, then the (N+1)th ends it ---
        [Fact]
        public void TiltsAllowed_zero_ends_on_the_first_tilt()
        {
            var (e, _, _, _) = New();
            e.TiltsAllowed = 0;
            e.Coin(true);
            e.Tilt(true);
            Assert.False(e.GameInProgress);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void TiltsAllowed_gives_that_many_warnings_then_ends(int allowed)
        {
            var (e, v, _, _) = New();
            e.TiltsAllowed = allowed;
            e.Coin(true);
            for (int i = 0; i < allowed; i++)
            {
                e.Tilt(true);
                Assert.True(e.GameInProgress);            // still a warning
                Assert.Contains("WARNING", v.Last);
            }
            e.Tilt(true);                                  // one past the allowance
            Assert.False(e.GameInProgress);               // now it tilts out
        }

        [Fact]
        public async Task Demo_self_test_plays_one_complete_game()
        {
            var au = new FakeAudit();
            var e = new GameEngine(new FakeView(), new FakeAudio(), new FakeLamps(), au, new FakeLed());
            await DemoGame.RunAsync(e, 0);
            // The demo choreographs its own hole sequence, so don't couple to an exact score — assert it ran one
            // complete game end-to-end while recording NOTHING to the audits (it's software-driven, not physical play).
            Assert.False(e.GameInProgress);          // the game finished
            Assert.True(e.Score > 0);                // it actually scored along the way
            Assert.Equal(0, au.Started);             // demo play never counts as a game
            Assert.Equal(0, au.Ended);
            Assert.Equal(0, au.WonCount);            // nor as a winner
            Assert.Empty(au.Hits);                   // and never wears the switch-hit log
            Assert.True(e.RecordAudits);             // audits are re-armed for real play once the demo ends
        }
    }

    public class QuietHoursTests
    {
        [Theory]
        [InlineData(23, true)]
        [InlineData(2, true)]
        [InlineData(7, true)]
        [InlineData(8, false)]
        [InlineData(12, false)]
        [InlineData(21, false)]
        public void Overnight_window_wraps_midnight(int hour, bool quiet)
            => Assert.Equal(quiet, QuietHours.InWindow(hour, 22, 8));

        [Theory]
        [InlineData(10, true)]
        [InlineData(9, false)]
        [InlineData(12, false)]
        public void Same_day_window(int hour, bool quiet)
            => Assert.Equal(quiet, QuietHours.InWindow(hour, 10, 11));
    }

    public class OperatorSettingsTests
    {
        [Fact]
        public void Clamp_bounds_every_field()
        {
            var s = new OperatorSettings
            {
                CountUpStepMs = 5000, QuietStartHour = 99, QuietEndHour = -3, QuietVolumePercent = 250
            };
            s.Clamp();
            Assert.Equal(300, s.CountUpStepMs);
            Assert.InRange(s.QuietStartHour, 0, 23);
            Assert.InRange(s.QuietEndHour, 0, 23);
            Assert.Equal(100, s.QuietVolumePercent);
        }

        [Fact]
        public void ApplyToQuietHours_pushes_values_into_config()
        {
            var s = new OperatorSettings
            {
                QuietHoursEnabled = true, QuietStartHour = 23, QuietEndHour = 6, QuietVolumePercent = 15
            };
            s.ApplyToQuietHours();
            Assert.True(QuietHours.Enabled);
            Assert.Equal(23, QuietHours.StartHour);
            Assert.Equal(6, QuietHours.EndHour);
            Assert.Equal(15, QuietHours.QuietPercent);
        }
    }
}
