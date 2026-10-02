# SkillGame v1.0.1

NEW
- In-app software updates: this screen. Check the WezeBull server, see what changed, and install with one tap - the machine swaps the files and restarts on its own.

FIXES & IMPROVEMENTS
- Solenoids now PULSE instead of being held on, so the lock coils can't overheat. Spring-return gate: plunger out blocks a coin, a pulse drops it.
- LED strip brightness is capped so a full-white frame can't brown out the 5 V supply.
- Power simplified to ONE 5 V / 10 A brick, split into two fused branches - F3 (board) and F4 (strip).
- Board design rules gated at real fab minimums (DRC clean).

This is the first release pushed through the updater itself - if you're reading it after tapping Update, the whole pipeline works.
