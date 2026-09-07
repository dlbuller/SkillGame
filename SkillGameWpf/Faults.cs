using System;
using System.Collections.Generic;

namespace SkillGameWpf
{
    // The single, thread-safe source of truth for stuck-switch faults. The coordinator computes the set (SetStuck);
    // every screen reads it (Contains / Count / Any / Snapshot) and reacts to Changed. Guarded so the switch-poll
    // thread and the UI thread can both touch it safely.
    public static class Faults
    {
        private static readonly object _gate = new();
        private static HashSet<string> _stuck = new();   // swapped whole on change, never mutated in place

        /// <summary>Raised (on the caller's thread) whenever the stuck set actually changes.</summary>
        public static event Action? Changed;

        /// <summary>Replace the whole stuck set. Returns true (and raises Changed) only if it actually changed.</summary>
        public static bool SetStuck(IEnumerable<string> ids)
        {
            var next = new HashSet<string>(ids);
            lock (_gate) { if (next.SetEquals(_stuck)) return false; _stuck = next; }
            Changed?.Invoke();
            return true;
        }

        public static bool Contains(string id) { lock (_gate) return _stuck.Contains(id); }
        public static int Count { get { lock (_gate) return _stuck.Count; } }
        public static bool Any { get { lock (_gate) return _stuck.Count > 0; } }
        public static IReadOnlyList<string> Snapshot() { lock (_gate) return new List<string>(_stuck); }
    }
}
