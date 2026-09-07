using SkillGame;
using Xunit;

namespace SkillGame.Tests
{
    // The stuck-detection + coin-start logic that used to live tangled inside the coordinator, now pure and tested.
    public class StuckMonitorTests
    {
        [Fact]
        public void Flags_a_switch_only_after_it_has_been_high_long_enough()
        {
            var m = new StuckMonitor { StuckSecs = 6 };
            m.Set("S5", true, 0);
            Assert.Empty(m.StuckAt(5_000));          // 5s in — not yet
            Assert.Contains("S5", m.StuckAt(6_000)); // 6s — stuck
        }

        [Fact]
        public void A_switch_that_goes_low_is_not_stuck()
        {
            var m = new StuckMonitor { StuckSecs = 6 };
            m.Set("S5", true, 0);
            m.Set("S5", false, 3_000);               // released before the threshold
            Assert.Empty(m.StuckAt(10_000));
        }

        [Fact]
        public void HighCount_tracks_simultaneous_highs()
        {
            var m = new StuckMonitor();
            m.Set("S1", true, 0); m.Set("S2", true, 0); m.Set("S3", true, 0);
            Assert.Equal(3, m.HighCount);
            m.Set("S2", false, 100);
            Assert.Equal(2, m.HighCount);
        }

        [Fact]
        public void Rising_edge_does_not_reset_the_clock()
        {
            var m = new StuckMonitor { StuckSecs = 6 };
            m.Set("S5", true, 0);
            m.Set("S5", true, 5_000);                // a redundant "high" must not restart the timer
            Assert.Contains("S5", m.StuckAt(6_000));
        }

        [Fact]
        public void Clear_wipes_everything()
        {
            var m = new StuckMonitor();
            m.Set("S1", true, 0); m.Clear();
            Assert.Equal(0, m.HighCount);
            Assert.Empty(m.StuckAt(100_000));
        }
    }

    public class CoinGateTests
    {
        [Fact]
        public void A_momentary_coin_on_an_idle_board_starts_a_game()
        {
            var g = new CoinGate { MaxHoldMs = 1500 };
            Assert.False(g.OnCoin(true, 0, 0));      // press — wait for release
            Assert.True(g.OnCoin(false, 120, 0));    // released quickly, nothing else high -> start
        }

        [Fact]
        public void A_held_coin_never_starts_a_game()
        {
            var g = new CoinGate { MaxHoldMs = 1500 };
            g.OnCoin(true, 0, 0);
            Assert.False(g.OnCoin(false, 5_000, 0)); // released after 5s -> stuck, not a coin
        }

        [Fact]
        public void A_floating_bus_does_not_start_a_game()
        {
            var g = new CoinGate { MaxHoldMs = 1500 };
            g.OnCoin(true, 0, 0);
            Assert.False(g.OnCoin(false, 100, 7));   // brief, but 7 other inputs high = floating bus
        }

        [Fact]
        public void One_stray_high_is_tolerated()
        {
            var g = new CoinGate { MaxHoldMs = 1500 };
            g.OnCoin(true, 0, 0);
            Assert.True(g.OnCoin(false, 100, 1));    // a single stray high still counts as a real insert
        }

        [Fact]
        public void A_release_with_no_prior_press_does_nothing()
        {
            var g = new CoinGate();
            Assert.False(g.OnCoin(false, 100, 0));
        }
    }

    public class SwitchRouterTests
    {
        // helper with the common "healthy live game" defaults
        static EngineRoute R(SwitchKind kind, bool demo = false, bool stuck = false, bool gameMode = true, bool ready = true, bool inProgress = true)
            => SwitchRouter.Route(kind, demo, stuck, gameMode, ready, inProgress);

        [Fact] public void Coin_starts_a_game_in_game_mode() => Assert.Equal(EngineRoute.Coin, R(SwitchKind.Coin, inProgress: false));
        [Fact] public void Score_routes_as_play_during_a_game() => Assert.Equal(EngineRoute.Play, R(SwitchKind.Score));
        [Fact] public void Gobble_routes_as_play_during_a_game() => Assert.Equal(EngineRoute.Play, R(SwitchKind.Gobble));
        [Fact] public void Tilt_routes_as_play_during_a_game() => Assert.Equal(EngineRoute.Play, R(SwitchKind.Tilt));

        [Fact] public void A_score_with_no_game_running_is_ignored() => Assert.Equal(EngineRoute.None, R(SwitchKind.Score, inProgress: false));
        [Fact] public void A_stuck_switch_never_reaches_the_engine() => Assert.Equal(EngineRoute.None, R(SwitchKind.Coin, stuck: true));
        [Fact] public void A_stuck_score_is_ignored_even_mid_game() => Assert.Equal(EngineRoute.None, R(SwitchKind.Score, stuck: true));
        [Fact] public void The_demo_owns_the_engine_so_live_switches_are_ignored() => Assert.Equal(EngineRoute.None, R(SwitchKind.Score, demo: true));
        [Fact] public void Coin_is_ignored_during_a_demo() => Assert.Equal(EngineRoute.None, R(SwitchKind.Coin, demo: true, inProgress: false));
        [Fact] public void Nothing_routes_outside_game_mode() => Assert.Equal(EngineRoute.None, R(SwitchKind.Coin, gameMode: false, inProgress: false));
        [Fact] public void Nothing_routes_without_an_engine() => Assert.Equal(EngineRoute.None, R(SwitchKind.Score, ready: false));
    }
}
