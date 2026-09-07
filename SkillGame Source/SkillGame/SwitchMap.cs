using System.Collections.Generic;
using System.Linq;

namespace SkillGame
{
    public enum SwitchKind { Score, Coin, Gobble, Tilt }

    /// <summary>One playfield switch: where it is wired, what it scores, and how it behaves.</summary>
    public sealed class SwitchDef
    {
        public int Board { get; }
        public int Pin { get; }            // logical FT232H pin (D0-D7=0-7, C0-C7=8-15)
        public string Id { get; }          // "S0".."S27"
        public string Label { get; }       // shown in lastSwitchTextBox
        public int Points { get; }
        public int Level { get; }          // level this switch scores at (Score kind)
        public SwitchKind Kind { get; }
        public bool IsWinner { get; }      // hitting it wins the game (S25/S26)
        public bool CancelBefore { get; }  // cancel any running lights before handling
        public bool Distract { get; }      // trigger distract lights/music after handling

        public SwitchDef(int board, int pin, string id, string label, int points, int level,
                         SwitchKind kind, bool isWinner = false, bool cancelBefore = false, bool distract = false)
        {
            Board = board; Pin = pin; Id = id; Label = label; Points = points; Level = level;
            Kind = kind; IsWinner = isWinner; CancelBefore = cancelBefore; Distract = distract;
        }
    }

    /// <summary>The whole switch matrix in one place.</summary>
    public static class SwitchMap
    {
        public static readonly SwitchDef[] All =
        {
            // GPIO1 — Coin + Rows 1-3
            new(1,  4, "S0",  "S0 - Coin Up",   0, 0, SwitchKind.Coin),
            new(1,  5, "S1",  "S1 - 10 Points", 10, 1, SwitchKind.Score),
            new(1,  6, "S2",  "S2 - 30 Points", 30, 1, SwitchKind.Score),
            new(1,  7, "S3",  "S3 - 50 Points", 50, 1, SwitchKind.Score),
            new(1,  8, "S4",  "S4 - 20 Points", 20, 1, SwitchKind.Score),
            new(1,  9, "S5",  "S5 - 20 Points", 20, 2, SwitchKind.Score),
            new(1, 10, "S6",  "S6 - 50 Points", 50, 2, SwitchKind.Score),
            new(1, 11, "S7",  "S7 - 30 Points", 30, 2, SwitchKind.Score),
            new(1, 12, "S8",  "S8 - 10 Points", 10, 2, SwitchKind.Score),
            new(1, 13, "S9",  "S9 - 10 Points", 10, 3, SwitchKind.Score),
            new(1, 14, "S10", "S10 - 30 Points",30, 3, SwitchKind.Score),
            new(1, 15, "S11", "S11 - 50 Points",50, 3, SwitchKind.Score),

            // GPIO2 — Rows 3-6 + Gobble
            new(2,  4, "S12", "S12 - 20 Points",20, 3, SwitchKind.Score),
            new(2,  5, "S13", "S13 - 10 Points",10, 4, SwitchKind.Score),
            new(2,  6, "S14", "S14 - 50 Points",50, 4, SwitchKind.Score),
            new(2,  7, "S15", "S15 - 30 Points",30, 4, SwitchKind.Score),
            new(2,  8, "S16", "S16 - 20 Points",20, 4, SwitchKind.Score),
            new(2,  9, "S17", "S17 - 10 Points",10, 5, SwitchKind.Score, distract: true),
            new(2, 10, "S18", "S18 - 30 Points",30, 5, SwitchKind.Score, distract: true),
            new(2, 11, "S19", "S19 - 50 Points",50, 5, SwitchKind.Score, distract: true),
            new(2, 12, "S20", "S20 - 20 Points",20, 5, SwitchKind.Score, distract: true),
            new(2, 13, "S21", "S21 - 60 Points",60, 6, SwitchKind.Score, cancelBefore: true, distract: true),
            new(2, 14, "S22", "S22 - 40 Points",40, 6, SwitchKind.Score, cancelBefore: true, distract: true),
            new(2, 15, "S23", "S23 - Gobble",    0, 0, SwitchKind.Gobble),

            // GPIO3 — Rows 7-8 + Tilt (inputs D4-D7)
            new(3,  4, "S24", "S24 - 70 Points",70, 7, SwitchKind.Score, cancelBefore: true, distract: true),
            new(3,  5, "S25", "S25 - 80 Points",80, 8, SwitchKind.Score, isWinner: true, cancelBefore: true),
            new(3,  6, "S26", "S26 - 80 Points",80, 8, SwitchKind.Score, isWinner: true, cancelBefore: true),
            new(3,  7, "S27", "S27 - Tilt",      0, 0, SwitchKind.Tilt),
        };

        private static readonly Dictionary<(int, int), SwitchDef> ByPin = All.ToDictionary(d => (d.Board, d.Pin));

        public static SwitchDef? Find(int board, int pin) => ByPin.TryGetValue((board, pin), out var d) ? d : null;
    }
}
