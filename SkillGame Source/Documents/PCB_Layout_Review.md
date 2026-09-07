# SkillGame — PCB Layout Review

**Board:** `SkillGame.kicad_pcb` (original lives in `OneDrive…\SkillGame MiniPC\SkillGame Board\`)
**Renders:** `SkillGame_PCB_top.png` (top copper + silk), `SkillGame_PCB_bottom.png` (bottom copper + GND pour)
**Method:** rendered both copper layers with `kicad-cli pcb export svg`, parsed the board for stats, and ran `kicad-cli pcb drc`.

## Bottom line

The board is **complete, correct, and manufacturable** — but the routing/placement is **not streamlined**. It passes every rule; it's just hand-routed and loose. Nothing here is a "must fix" — these are tidiness/robustness improvements for a v2 spin, not defects.

## Objective results

| Check | Result |
|---|---|
| DRC violations | **0** |
| Unconnected (unrouted) items | **0** |
| Board size | 200.8 × 121.6 mm (2-layer) |
| Footprints | 135 (all through-hole) |
| Track segments | 869 (575 top / 294 bottom) |
| Vias | 7 |
| Copper zones | 1 — `GNDREF` ground pour on **B.Cu** |

## What's good

- **Electrically finished** — fully routed, 0 DRC errors, 0 unconnected nets.
- **Has a ground plane** — bottom-side `GNDREF` pour.
- **Clean mechanicals** — rounded outline, four corner mounting holes, sensible size.
- **Professional touches** — title block "WezeBull Games – Game Board V1.0" with logo; the four FT232H sockets are silk-labeled **GPIO1–GPIO4** (resolves the serial/assembly concern).
- **Logical edge I/O** — power (3VDC / 5VDC) and load connectors (LEDs, solenoid) at the board edges.

## What isn't streamlined (improvements, most-impactful first)

1. **Placement forces long fan-out.** The 28 input pull-down resistors and 18 driver transistors sit in banks *away* from the FT232H pins they serve, so each pin runs a long trace. **Fix:** put each pull-down / driver right next to its socket pad — most of the messy routing then disappears on its own.
2. **Top-layer "spaghetti."** Long parallel bundles snake left-to-center with diagonal fans out of GPIO3/GPIO4. Functional, but longer/busier than needed (a symptom of #1).
3. **Fragmented ground return.** 294 traces on the bottom but only 7 vias, so the `GNDREF` pour is cut into islands. For the solenoids (inductive kick) and the 3 MHz WS2812b line, move more signals off the bottom and add ground-stitching vias.
4. **Uneven density.** Left/center is packed; the right third (GPIO4 + LEDs connector) is sparse. Rebalancing shortens cross-board runs.
5. **Confirm current-carrying widths.** Set a net class so the 5 V NeoPixel feed and solenoid traces are wider than signal traces.

## Note

Re-routing is a hands-on KiCad task and was **not** automated — doing so blindly would more likely break the current clean DRC than help. If a v2 is wanted, start with item #1 (placement); the rest follows.
