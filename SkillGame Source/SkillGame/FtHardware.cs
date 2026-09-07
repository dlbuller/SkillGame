using Iot.Device.Ft232H;
using Iot.Device.FtCommon;
using Iot.Device.Ws28xx;
using System.Collections.Generic;
using System.Device.Gpio;
using System.Device.Spi;

namespace SkillGame
{
    /// <summary>Owns the four FT232H boards, opening each board's pins by FTDI serial (GPIO1–GPIO4) and bringing up the WS2812b strip on GPIO4's SPI line; logical pins are D0–D7 = 0–7, C0–C7 = 8–15.</summary>
    public class FtHardware
    {
        public GpioController ControllerGPIO1 = null!;
        public GpioController ControllerGPIO2 = null!;
        public GpioController ControllerGPIO3 = null!;
        public GpioController ControllerGPIO4 = null!;
        public Ws2812b Neo = null!;

        private const int NumLeds = 150;

        // GPIO1 inputs — Coin + Rows 1–3 (S0–S11)
        public readonly int[] PinsGPIO1Input = { 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
        // GPIO2 inputs — Rows 3–6 + gobble (S12–S23)
        public readonly int[] PinsGPIO2Input = { 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
        // GPIO3 inputs — Rows 7–8 + tilt (S24–S27: D4/D5/D6/D7)
        public readonly int[] PinsGPIO3Input = { 4, 5, 6, 7 };
        // GPIO3 outputs — CoinLock, WinLock, 100–400 reels, Game Over, Winner (C0–C7)
        public readonly int[] PinsGPIO3Output = { 8, 9, 10, 11, 12, 13, 14, 15 };
        // GPIO4 outputs — 10–90 tens lamps + Tilt lamp (D4–D7, C0–C5); D1 = SPI/NeoPixel data
        public readonly int[] PinsGPIO4Output = { 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 };

        private Ft232HDevice? _dev1, _dev2, _dev3, _dev4;

        /// <summary>Re-open all four boards after a hot unplug/replug. The FTDI handles must be fully closed first —
        /// re-opening fails with "DeviceNotOpen" if the old device is still open — so dispose the devices too, then settle. Throws if a board is still missing.</summary>
        public void Reinitialize()
        {
            foreach (GpioController c in new[] { ControllerGPIO1, ControllerGPIO2, ControllerGPIO3, ControllerGPIO4 })
                try { c?.Dispose(); } catch { }
            foreach (Ft232HDevice? d in new[] { _dev1, _dev2, _dev3, _dev4 })
                try { (d as System.IDisposable)?.Dispose(); } catch { }
            ControllerGPIO1 = ControllerGPIO2 = ControllerGPIO3 = ControllerGPIO4 = null!;
            _dev1 = _dev2 = _dev3 = _dev4 = null;
            System.Threading.Thread.Sleep(400);   // let the FTDI driver release the handles before re-enumerating
            Initialize();
        }

        /// <summary>Discovers the boards, opens all pins, and starts the LED strip; throws if a board is missing.</summary>
        public void Initialize()
        {
            List<FtDevice> devices = FtCommon.GetDevices();

            if (devices.Count == 0)
                throw new InvalidOperationException(
                    "No FT232H devices were detected.\n\n" +
                    "Connect the four FT232H boards (FTDI serial numbers GPIO1, GPIO2, GPIO3 and GPIO4) " +
                    "to the mini-PC's USB ports, then restart SkillGame.");

            Ft232HDevice? g1 = null, g2 = null, g3 = null, g4 = null;
            foreach (FtDevice device in devices)
            {
                switch (device.SerialNumber)
                {
                    case "GPIO1": g1 = new Ft232HDevice(device); break;
                    case "GPIO2": g2 = new Ft232HDevice(device); break;
                    case "GPIO3": g3 = new Ft232HDevice(device); break;
                    case "GPIO4": g4 = new Ft232HDevice(device); break;
                }
            }

            List<string> missing = new List<string>();
            if (g1 is null) missing.Add("GPIO1");
            if (g2 is null) missing.Add("GPIO2");
            if (g3 is null) missing.Add("GPIO3");
            if (g4 is null) missing.Add("GPIO4");
            if (missing.Count > 0)
                throw new InvalidOperationException(
                    $"Found {devices.Count} FTDI device(s), but these required board(s) are missing or mis-labeled: " +
                    $"{string.Join(", ", missing)}.\n\n" +
                    "Each FT232H board's FTDI serial number must be programmed to GPIO1, GPIO2, GPIO3 or GPIO4. " +
                    "Check the USB connections, then restart SkillGame.");

            _dev1 = g1; _dev2 = g2; _dev3 = g3; _dev4 = g4;   // keep the device handles so a reconnect can close them cleanly

            ControllerGPIO1 = g1!.CreateGpioController();
            ControllerGPIO2 = g2!.CreateGpioController();
            ControllerGPIO3 = g3!.CreateGpioController();
            ControllerGPIO4 = g4!.CreateGpioController();

            foreach (int pin in PinsGPIO1Input) { ControllerGPIO1.OpenPin(pin); ControllerGPIO1.SetPinMode(pin, PinMode.Input); }
            foreach (int pin in PinsGPIO2Input) { ControllerGPIO2.OpenPin(pin); ControllerGPIO2.SetPinMode(pin, PinMode.Input); }
            foreach (int pin in PinsGPIO3Input) { ControllerGPIO3.OpenPin(pin); ControllerGPIO3.SetPinMode(pin, PinMode.Input); }
            foreach (int pin in PinsGPIO3Output) { ControllerGPIO3.OpenPin(pin); ControllerGPIO3.SetPinMode(pin, PinMode.Output); ControllerGPIO3.Write(pin, PinValue.Low); }
            foreach (int pin in PinsGPIO4Output) { ControllerGPIO4.OpenPin(pin); ControllerGPIO4.SetPinMode(pin, PinMode.Output); ControllerGPIO4.Write(pin, PinValue.Low); }

            // WS2812b strip on GPIO4 SPI (MOSI), 3 MHz clock
            SpiDevice spi = g4!.CreateSpiDevice(new SpiConnectionSettings(0, 3)
            {
                ClockFrequency = 3_000_000,
                Mode = SpiMode.Mode0,
            });
            Neo = new Ws2812b(spi, NumLeds);
        }
    }
}
