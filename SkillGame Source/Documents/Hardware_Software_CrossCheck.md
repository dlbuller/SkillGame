# SkillGame — Hardware ↔ Software Cross-Check

**Sources reconciled:**
1. **Software** — `GPIOFunctions` / `GPIOFunctions.Switches.cs` (pin maps + per-switch handlers)
2. **Spec** — `tofrom.xlsx` ("to/from" pin list + switch matrix)
3. **Playfield** — annotated `Switch Locations.jpg`
4. **Board** — `SkillGame.kicad_sch`, verified via an exported netlist (`kicad-cli sch export netlist`, parsed programmatically)

## Verdict

**Everything lines up.** All four sources agree on every switch, point value, row, lamp, and solenoid. The PCB wires each FT232H pin to exactly the function the software expects. No electrical or logical mismatches were found. The only issues are cosmetic (listed at the end).

## What the board actually is (from the netlist)

| Item | Count | Notes |
|---|---|---|
| FT232H sockets | 4 | refs **U1–U4** (22-pin module footprint) |
| Playfield switches | 34 | S0–S27, plus S23 split into 7 parallel gobble switches (SW23-1…SW23-7) |
| Lamp drivers (PN2222A) | 16 | score reels, tens lamps, tilt, game-over, winner |
| Solenoid drivers (TIP120) | 2 | Coin Lock (Q7), Win Lock (Q8) |
| Flyback diodes (1N4004) | 2 | across the two solenoids |

**U1→GPIO1, U2→GPIO2, U3→GPIO3, U4→GPIO4** — confirmed by which switches/loads each socket wires to. Identity is assigned by the FTDI **serial number** in firmware, not the socket.

## Verified pin map

Format: `FT pad` (logical pin in code) → switch/load. Inputs active-High (3.3 V + 10 k pull-down). Full color version: `SkillGame_GPIO_Map.html`.

### GPIO1 / U1 — inputs (Coin + Rows 1–3)
`D4`(4)→S0 Coin · `D5`(5)→S1 10 · `D6`(6)→S2 30 · `D7`(7)→S3 50 · `C0`(8)→S4 20 · `C1`(9)→S5 20 · `C2`(10)→S6 50 · `C3`(11)→S7 30 · `C4`(12)→S8 10 · `C5`(13)→S9 10 · `C6`(14)→S10 30 · `C7`(15)→S11 50

### GPIO2 / U2 — inputs (Rows 3–6 + Gobble)
`D4`(4)→S12 20 · `D5`(5)→S13 10 · `D6`(6)→S14 50 · `D7`(7)→S15 30 · `C0`(8)→S16 20 · `C1`(9)→S17 10 · `C2`(10)→S18 30 · `C3`(11)→S19 50 · `C4`(12)→S20 20 · `C5`(13)→S21 60 · `C6`(14)→S22 40 · `C7`(15)→**S23 Gobble (G1–G7, 7 switches paralleled)**

### GPIO3 / U3 — inputs (Rows 7–8, Tilt) + reel/solenoid outputs
Inputs: `D4`(4)→S24 70 (Row 7) · `D5`(5)→S25 80 (Row 8) · `D6`(6)→S26 80 (Row 8) · `D7`(7)→S27 Tilt
Outputs: `C0`(8)→Coin Lock **[TIP120 Q7]** · `C1`(9)→Win Lock **[TIP120 Q8]** · `C2`(10)→100 · `C3`(11)→200 · `C4`(12)→300 · `C5`(13)→400 · `C6`(14)→Game Over · `C7`(15)→Winner  *(lamps via PN2222A Q1–Q6)*

### GPIO4 / U4 — tens-lamp outputs + NeoPixel
`D1`(1)→**WS2812b data (SPI MOSI, 150 px @ 5 V)** · `D4`(4)→10 · `D5`(5)→20 · `D6`(6)→30 · `D7`(7)→40 · `C0`(8)→50 · `C1`(9)→60 · `C2`(10)→70 · `C3`(11)→80 · `C4`(12)→90 · `C5`(13)→Tilt lamp · `C6`(14)/`C7`(15)→**NC**  *(lamps via PN2222A Q9–Q18)*

## Playfield scoring (per hole)

| Row (level) | Holes (points / switch) |
|---|---|
| 0 | Coin Up / S0 |
| 1 | 10/S1 · 30/S2 · 50/S3 · 20/S4 |
| 2 | 20/S5 · 50/S6 · 30/S7 · 10/S8 |
| 3 | 10/S9 · 30/S10 · 50/S11 · 20/S12 |
| 4 | 10/S13 · 50/S14 · 30/S15 · 20/S16 |
| 5 | 10/S17 · 30/S18 · 50/S19 · 20/S20 |
| 6 | 60/S21 · 40/S22 · G1,G2 (S23) |
| 7 | 70/S24 · G3,G4 (S23) |
| 8 | 80/S25 · 80/S26 · G5,G6,G7 (S23) |
| — | Tilt / S27 |

## Cosmetic issues (no functional impact)

1. **Stale comment block** — *Resolved (2026-07 refactor).* Levels now live in `SwitchMap.cs` as data (S24 = Level 7, S25/S26 = Level 8); the old per-pin comment block is gone.
2. **Score-sound keys** — *Resolved.* `GameEngine.Hit` now sends `"<points> pts"` (with the space, matching `AudioFunctions` and the diagnostics preview) and Gobble/Tilt play `"Game Over"`, so score/end sounds actually play. Covered by unit tests.
3. **Socket vs. serial** — the software binds each device by serial (`GPIO1`…`GPIO4`), and the PCB already silk-labels the sockets **GPIO1–GPIO4** (U1–U4), which is exactly right. Just confirm at assembly that the module programmed `GPIOn` goes in the socket labeled `GPIOn`.
4. **Symbol name** — the FT232H socket symbol is named `Conn_01x11_Socket_FT232H` but exposes ~22 module pins. Cosmetic only.
