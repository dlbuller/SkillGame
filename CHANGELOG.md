# SkillGame changelog

Every released version and what changed. The in-app updater (Service → Check for Updates)
shows these notes before installing. Cut a new release with `tools/publish-update.ps1`.

## v1.1.0 — 2026-10-03
- **12 UI themes** (Settings → Theme): Classy, Fun, Plain, Blackout, Vintage, Maple, Midnight, Synthwave, Lava, Circuit, Galaxy, Ruby — each reskins the whole UI and the playfield art.
- **Original wood playfield** replaces the Bally artwork (same rail/hole layout, so coin paths are unchanged).
- **Updater hardened** — unzip + copy + relaunch only; no PC reboot and no temp script (that pattern tripped endpoint security).
- **Install any version** — the updater lists every release and installs forward or back, with the latest tagged; auto-checks on boot + every 6 h.
- **Diagnostics** — each board's hardware tests are disabled when that board is missing (GPIO3 = reels/Winner/Game-Over + lock coils, GPIO4 = 10-90 lamps/Tilt + LED strip).
- Marquee is warm maple instead of the rainbow; "Roll · Climb · Win" subtitle; fixed the boards-missing tombstone clipping on Diagnostics.

## v1.0.1 — 2026-10-01
**New**
- In-app software updates: check the WezeBull server, see what changed, install with one tap; the machine swaps the files and restarts itself.

**Fixes & improvements**
- Solenoids pulse instead of being held on (no coil overheat); spring-return gate — plunger out blocks a coin, a pulse drops it.
- LED strip brightness capped so a full-white frame can't brown out the 5 V supply.
- Power simplified to one 5 V / 10 A brick, split into two fused branches (F3 board, F4 strip).
- Board design rules gated at real fab minimums (DRC clean).

## v1.0.0
- Initial build: Game Status, Diagnostics, Audits, Service, interactive Schematic; 4× FT232H hardware layer; demo/attract mode; unit-tested game core.
