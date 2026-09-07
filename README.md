# SkillGame

A modern recreation of the 1955 **Bally Skill-Roll** coin-roll arcade game — real hardware, real coin, modern control software.

> **ALL HUFF · NO BULL** — WezeBull Games

The player rolls a physical coin up the playfield; it rides the rails, skims the scoring holes, and drops into one to score. Advance through the levels without losing the coin to a gobble hole (or tilting) to win. A mini-PC drives the whole machine over four FT232H boards and shows the backglass, diagnostics and operator console on a touch monitor.

![Game Status](docs/screenshots/game.png)

## Screens

| | |
|---|---|
| **Schematic** — interactive, clickable system diagram | **Diagnostics** — live switch + lamp maps, self-test |
| ![Schematic](docs/screenshots/schem.png) | ![Diagnostics](docs/screenshots/diag.png) |
| **Audits** — plays, wins, hits, high scores (the auditor gremlin) | **Service** — one-page operator console |
| ![Audits](docs/screenshots/audit.png) | ![Service](docs/screenshots/service.png) |

## Layout

| Folder | What it is |
|--------|-----------|
| `SkillGameWpf/` | The WPF (.NET 8) front-end / operator console — Game Status, Diagnostics, Settings, Audits, Schematic, Service. |
| `SkillGame Source/SkillGame/` | The UI-agnostic game core (engine, switch map, audits, hardware coordinator). The WPF app links these files directly. |
| `SkillGame Source/SkillGame.Tests/` | xUnit tests for the core (engine, stuck detection, coin gate, switch router). |
| `hardware/` | The KiCad 9 schematic + PCB for the WezeBull Games control board. |
| `docs/` | Sourced BOM (xlsx), IKEA-style assembly guide (PDF), and the schematic-page images. |

## Build & test

```bash
cd SkillGameWpf
dotnet build -c Debug -p:Platform=x64
```

```bash
cd "SkillGame Source/SkillGame.Tests"
dotnet test
```

The app runs on Windows (WPF, `net8.0-windows`, x64). It drives four **Adafruit FT232H** breakouts (GPIO1–GPIO4) over USB in MPSSE mode; with no boards attached it runs in a demo/preview mode.

## Hardware notes

- **4× FT232H** — inputs on GPIO1–3 (28 switches; S23 = 7 parallel gobble holes), outputs on GPIO3/4 (16 lamps via PN2222A, 2 solenoids via TIP120 + 1N4004 flyback), LED strip on GPIO4 SPI.
- **Power:** 3.3 V adapter → J1 (switch commons — the FT232H inputs are **not** 5 V-tolerant), 5 V adapter → J3 / LED± (lamps, WS2812b strip, coils).
- **Fuses:** F1/F2 = T2A slow-blow (5×20 mm, Keystone 3517 clips) on the two solenoids; F3 = T3.5A slow-blow **inline** in the 5 V supply lead.
- The full parts list with vendor links + prices and a step-by-step build guide are in `docs/`.

| Board (3D render) | System hookup |
|---|---|
| ![Board front](docs/schematics/board_front.png) | ![Hookup](docs/schematics/hookup.png) |

![Full schematic](docs/schematics/full.png)

## Credits

Scott Wezeman & Dan Bullerman — WezeBull Games.
