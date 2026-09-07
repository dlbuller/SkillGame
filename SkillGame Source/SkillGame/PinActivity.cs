using System;
using System.Collections.Generic;

namespace SkillGame
{
    /// <summary>Tracks which FT232H pins are currently active and raises an event when that changes, so the I/O Map window can light the matching pins.</summary>
    public static class PinActivity
    {
        private static readonly Dictionary<(int board, int pin), bool> _state = new();
        private static readonly object _gate = new();

        /// <summary>Fired whenever a pin's active state actually changes.</summary>
        public static event Action? Changed;

        public static void Set(int board, int pin, bool active)
        {
            lock (_gate)
            {
                bool cur = _state.TryGetValue((board, pin), out var v) && v;
                if (cur == active) return;
                _state[(board, pin)] = active;
            }
            Changed?.Invoke();
        }

        public static bool IsActive(int board, int pin)
        {
            lock (_gate) return _state.TryGetValue((board, pin), out var v) && v;
        }

        public static void Reset()
        {
            lock (_gate) _state.Clear();
            Changed?.Invoke();
        }
    }
}
