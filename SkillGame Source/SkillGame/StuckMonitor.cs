using System.Collections.Generic;

namespace SkillGame
{
    /// <summary>Pure stuck-switch detection: a switch held high past StuckSecs is stuck. No UI/hardware/threading here,
    /// so it can be unit-tested directly; the HardwareCoordinator owns an instance and guards it with its own lock.</summary>
    public sealed class StuckMonitor
    {
        private readonly Dictionary<string, long> _highSince = new();   // switch id -> tick (ms) it went high
        public int StuckSecs { get; set; } = 6;

        /// <summary>Record a switch going high/low at <paramref name="nowMs"/> (the rising edge starts its clock).</summary>
        public void Set(string id, bool high, long nowMs)
        {
            if (high) { if (!_highSince.ContainsKey(id)) _highSince[id] = nowMs; }
            else _highSince.Remove(id);
        }

        public void Clear() => _highSince.Clear();

        /// <summary>How many inputs are high right now (used to tell a real coin from a floating bus).</summary>
        public int HighCount => _highSince.Count;

        /// <summary>The switches that have been high continuously for at least StuckSecs as of <paramref name="nowMs"/>.</summary>
        public List<string> StuckAt(long nowMs)
        {
            var stuck = new List<string>();
            foreach (var kv in _highSince)
                if (nowMs - kv.Value >= StuckSecs * 1000L) stuck.Add(kv.Key);
            return stuck;
        }
    }
}
