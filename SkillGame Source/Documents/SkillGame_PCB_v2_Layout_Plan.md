# SkillGame — PCB v2 Layout Plan (tidy the routing, keep it correct)

**Status of the current board:** electrically **complete and correct** — 0 DRC violations, 0 unconnected
nets, ground pour present, sockets silk-labeled GPIO1–GPIO4. It works and is manufacturable. The only
weakness is that the **layout** is hand-routed and loose. This plan is how to make it *nice* without
breaking what already works.

> **Why this is a plan and not an automated edit:** re-placement/re-routing is a visual, iterative KiCad
> task. Doing it blind (scripted, no canvas, no interactive DRC) would almost certainly introduce new
> violations and could leave the board unroutable — strictly worse than the loose-but-working board you
> have. Work through this interactively in KiCad instead. **The schematic is verified correct — do not
> change it; only move footprints and re-route copper.**

## Ground rules
1. **Branch it.** Copy the board folder first (`SkillGame_v2/`) so v1 stays intact.
2. **Never touch the schematic / netlist.** Placement + routing only. Re-import netlist only if you
   deliberately changed the schematic (you shouldn't).
3. **Re-run DRC after every stage** (`kicad-cli pcb drc SkillGame.kicad_pcb`) and keep it at **0**.
4. Do it in the order below — each stage makes the next one easier.

## Stage 1 — Placement (biggest win, do this first)
The messy routing is a *symptom* of placement. Fix placement and most traces shorten on their own.
- For each FT232H socket (U1–U4), pull its **own** support parts right up against it:
  - the 28 input **10 kΩ pull-downs** next to the pins they hold down,
  - the 16 **PN2222A** lamp drivers + their 1 kΩ base / 220 Ω limit resistors beside the pins that drive them,
  - the 2 **TIP120** + 2.2 kΩ base + **1N4004** flyback + fuse next to GPIO3's solenoid pins.
- Group by socket so each board becomes a self-contained cluster (U1 cluster, U2 cluster, …).
- Keep the edge connectors (J1 3VDC, SPI1 5VDC, J2 lamp harness, solenoid conn) where they are — on the edges.

## Stage 2 — Re-route the (now short) signals
- Rip up the top-layer "spaghetti" (Route → un-route, or delete the long segments) and re-route each
  cluster locally. With parts adjacent to their pins, most runs become short and straight.
- Keep signal on the top layer; reserve the bottom for ground (Stage 3).

## Stage 3 — Solid ground return
- The current bottom pour is cut into islands (294 bottom traces, only **7 vias**).
- After Stage 2 there should be far fewer bottom signals. Then **add ground-stitching vias** — a loose
  grid of GND vias across the pour, and especially:
  - a via near every driver transistor's emitter/ground,
  - vias flanking the 3 MHz WS2812b data line and the solenoid traces (inductive kick).
- Re-pour (`B.Cu` GNDREF) and confirm the pour is continuous, not fragmented.

## Stage 4 — Trace widths (net classes)
- Add net classes in Board Setup → Net Classes:
  - **PWR_5V** (WS2812b +5 V feed) and **PWR_SOL** (solenoid V+) → wider (e.g. 0.6–0.8 mm),
  - **PWR_LAMP** → medium,
  - default signal class stays thin.
- Assign the 5 V / solenoid / lamp-V+ nets to those classes, then re-route (or "change width") those nets.
  Do this *after* routing so you don't fight the autorouter-less manual routes.

## Stage 5 — Rebalance & finish
- The right third (GPIO4 + LED connector) is sparse while the left/center is packed. Nudge clusters to
  even out density; it shortens cross-board runs.
- Final checks: `kicad-cli pcb drc` = 0, ratsnest fully routed, pour continuous, silkscreen still legible,
  fab outputs (Gerbers/drill) regenerate cleanly.

## Definition of done
- 0 DRC, 0 unrouted.
- Each FT232H's support parts sit beside it; no long cross-board fan-out.
- Continuous bottom ground pour with stitching vias.
- Power/solenoid nets on wider classes.
- v1 board preserved untouched in its own folder.

*Happy to pair on this interactively in KiCad — I can drive placement/routing decisions stage by stage
and re-run DRC with you, which is the safe way to do it.*
