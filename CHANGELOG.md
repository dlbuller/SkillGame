# SkillGame changelog

Every released version and what changed. The in-app updater (Service → Check for Updates)
shows these notes before installing. Cut a new release with `tools/publish-update.ps1`.

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
