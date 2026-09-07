namespace SkillGame
{
    /// <summary>Pure coin-start validation: a game may only start on a genuine, momentary coin — one that releases
    /// quickly AND with the board otherwise idle. A stuck/floating coin (held high, or amid a floating-bus of many
    /// simultaneous highs) never starts a phantom game. No UI/hardware here, so it's unit-testable in isolation.</summary>
    public sealed class CoinGate
    {
        /// <summary>Longest a real coin holds the switch closed; longer than this = stuck/floating, not a coin.</summary>
        public int MaxHoldMs { get; set; } = 1500;

        private long _downAt = -1;   // when the coin last went high; -1 = currently up

        /// <summary>Feed a coin-switch transition. Returns true only when this release should START a game.</summary>
        /// <param name="high">the coin switch's new state</param>
        /// <param name="nowMs">current time in ms</param>
        /// <param name="otherHighCount">how many OTHER inputs are high right now (a floating bus has many)</param>
        public bool OnCoin(bool high, long nowMs, int otherHighCount)
        {
            if (high) { _downAt = nowMs; return false; }   // judge on the release, not the press
            long down = _downAt; _downAt = -1;
            if (down < 0 || nowMs - down > MaxHoldMs) return false;   // never released in time = not a real coin
            if (otherHighCount > 1) return false;                     // several inputs high at once = floating/unwired bus
            return true;
        }

        public void Reset() => _downAt = -1;
    }
}
