namespace SkillGame
{
    /// <summary>What a live switch read should do to the game engine, once it has been recorded on the monitor.</summary>
    public enum EngineRoute
    {
        None,   // don't touch the engine (idle/floating/stuck, wrong mode, or no game running)
        Coin,   // attempt to start a game (validated further by CoinGate)
        Play,   // route a score/gobble/tilt hit into the running game
    }

    /// <summary>Pure decision for how a switch reaches the game engine. Bench/observer are handled before this; the
    /// input is always recorded on the Diagnostics monitor first, then this decides the engine action. No UI/hardware,
    /// so it's unit-testable in isolation.</summary>
    public static class SwitchRouter
    {
        public static EngineRoute Route(SwitchKind kind, bool demoActive, bool isStuck,
                                        bool inGameMode, bool engineReady, bool gameInProgress)
        {
            if (demoActive) return EngineRoute.None;        // the demo scripts the engine itself
            if (isStuck) return EngineRoute.None;           // a stuck/floating input is a fault, never a play
            if (!inGameMode || !engineReady) return EngineRoute.None;
            if (kind == SwitchKind.Coin) return EngineRoute.Coin;   // only a coin can START a game
            if (!gameInProgress) return EngineRoute.None;   // nothing else does anything unless a game is running
            return kind is SwitchKind.Score or SwitchKind.Gobble or SwitchKind.Tilt ? EngineRoute.Play : EngineRoute.None;
        }
    }
}
