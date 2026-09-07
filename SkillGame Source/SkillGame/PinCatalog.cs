using System.Collections.Generic;
using System.Linq;

namespace SkillGame
{
    /// <summary>One FT232H pin as shown on the I/O Map: where it is, what it does, and its direction.</summary>
    public sealed class PinInfo
    {
        public int Board { get; }
        public int Pin { get; }
        public string Pad { get; }     // D0-D7 / C0-C7
        public string Label { get; }   // short function label
        public bool IsOutput { get; }

        public PinInfo(int board, int pin, string label, bool isOutput)
        {
            Board = board; Pin = pin; Label = label; IsOutput = isOutput;
            Pad = pin < 8 ? "D" + pin : "C" + (pin - 8);   // logical pin -> pad name
        }
    }

    /// <summary>
    /// The full FT232H pin directory for the I/O Map, built from the same sources the game uses:
    /// inputs from <see cref="SwitchMap"/>, outputs from <see cref="LampController.Outputs"/>, plus the
    /// WS2812b data line. No duplicated pin numbers - it stays in sync with the rest of the code.
    /// </summary>
    public static class PinCatalog
    {
        public static readonly PinInfo[] All = Build();

        private static PinInfo[] Build()
        {
            var list = new List<PinInfo>();

            foreach (var s in SwitchMap.All)
            {
                string label = s.Kind switch
                {
                    SwitchKind.Coin => s.Id + " Coin",
                    SwitchKind.Gobble => s.Id + " Gobble",
                    SwitchKind.Tilt => s.Id + " Tilt",
                    _ => s.Id + " " + s.Points + "pt",
                };
                list.Add(new PinInfo(s.Board, s.Pin, label, false));
            }

            foreach (var kv in LampController.Outputs)
                list.Add(new PinInfo(kv.Value.board, kv.Value.pin, OutputLabel(kv.Key), true));

            list.Add(new PinInfo(4, 1, "WS2812b", true));   // NeoPixel data (SPI MOSI)

            return list.ToArray();
        }

        private static string OutputLabel(string name) => name switch
        {
            "GameOver" => "Game Over",
            "WinnerLock" => "Win Lock",
            "CoinLock" => "Coin Lock",
            _ when int.TryParse(name, out _) => name + "pt lamp",
            _ => name,
        };

        public static IEnumerable<PinInfo> ForBoard(int board) =>
            All.Where(p => p.Board == board).OrderBy(p => p.Pin);
    }
}
