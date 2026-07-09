# RUGS! — Devlog

Development log for **RUGS!**, a satirical street-trading mod for Big Ambitions. Newest entries first.
Versioning: `<major>.<minor>.<patch>` plus a fixed `.0.4.2.0` tail (e.g. `1.3.0.4.2.0`).

---

## 2026-07-09 — Balance re-rule: wash room 1.5×, intake follows → v1.6.2

1.6.1's doubling lasted about ten minutes. Boss looked at it again: no — **the wash room is the number he
wants to set**, and intake should be "the appropriate figure given the washroom." So the knobs got restructured
to match how he actually tunes:

- `RugLaunder.PlausibleInflation` **0.70 → 1.5** — a front now washes up to 1.5× its ORGANIC daily revenue
  (yes, past 1.0: the books run hotter than the register; the wash-log subtraction still anchors the cap to
  organic revenue, so there's no bootstrap).
- `RugSidecars.Factor` is now **DERIVED IN SOURCE**: `PlausibleInflation × 12/7` (≈2.57). The 12/7 is the
  original 0.6/0.35 washers-per-earner doctrine ratio, now encoded as a const expression — one master knob,
  intake follows, the two can never drift apart in a future tune. Verified the computed float (2.5714285…)
  baked into the release IL at the byte level.

Net vs what players have (1.6.0): ~4.3× the money on both sides, ratio untouched. 1.6.1 was tagged but never
uploaded — the Workshop jumps 1.6.0 → 1.6.2.

---

## 2026-07-09 — Balance: the money doubled → v1.6.1

Boss's ruling after sizing up realistic fleet counts against the take: rug money on a business should feel
like RUG money. **Both sides of the machine doubled, across the board** — rider intake `RugSidecars.Factor`
0.6 → **1.2** (dirty minted per $1 of the front's organic revenue), wash room `RugLaunder.PlausibleInflation`
0.35 → **0.70** (safe daily wash as a share of organic revenue).

The balance doctrine holds by construction: both knobs moved at the identical rate, so the washers-per-earner
ratio stays exactly 1.714 — the expansion pressure that forces empire-building is untouched, the empire just
pays twice as much at every size. Factory boosts multiply on top of the new bases and were left alone.

---

## 2026-07-09 — CROOKBOOKS™: the laundry-computer overhaul → v1.6.0

The UI debt release. The laundry computer had accreted across versions — v1.3's gold LAUNDERING screen with
v1.5's green GRAND LEDGER bolted on behind a door — two brands, two palettes, and the core loop (check
balance → collect → wash) hopping between them. UX ruling from the boss: one period-correct app, and the
menu hop IS the immersion — "it should FEEL like using a late-80s/early-90s computer."

- **CROOKBOOKS™ by RUGSOFT.** New `RugTerminal` owns the window: a type-on boot POST
  (`(C) 1989 RUGSOFT CORP — ALL RIGHTS DENIED`, `TWO SETS OF BOOKS ... FOUND`; any key skips), a main menu
  with a dealer quote and the glance strip (STASH · HEAT · WASH ROOM), numbered screens driven by keyboard
  (1–3, Esc backs out one level) or mouse. `RugLedgerUI`/`RugLaunderUI`/`RugDealersUI` are gone; the first
  two became content builders (`RugBooksScreen`/`RugWashScreen`) that render into the terminal.
- **THE BOOKS** (the GL, renamed) picked up **COLLECT ALL** — sweeps every rider's held cash with the exact
  per-district math of clicking each. **THE WASH** picked up **WASH ALL** — the night crew's biggest-room-first
  safe sweep, run by hand, no vig; the night-crew receipt stays the automation's paper trail (manual sweeps
  don't write it).
- **The glass.** `RugCrt`: procedural scanlines + radial vignette + a 0.5 Hz alpha flicker, laid over the
  panel as a raycast-transparent overlay — no assets, textures built once and cached. Plus the chrome:
  `C:\RUGS>` prompt lines and key-legend footers on every screen.
- **Feedback bug, not a math bug.** Boss's walkthrough flagged "ALL businesses show tapped out" — the caps
  were right; the sweep (WASH ALL / night crew) had simply used the day's room and the screen went silent
  about it. Rows now show what happened: "washed $X today" / "no sales history yet", with a night-crew-aware
  headline when the whole fleet is done.
- Verified via the Dev-build dance (F10 stress rows, F11 forced night crew), then byte-probed the release
  DLL: dev strings absent, feature strings present, `1.6.0.0.4.2.0` stamped.

---

## 2026-07-07 — Scrollbars, one console, receipts → v1.5.2

Launch-week polish, driven by watching real users hit real walls.

- **Visible scrollbars.** The 1.5.1 scroll fix WORKED — but invisibly: wheel-only scrolling gave no cue that
  more rows existed below the fold (the boss only knew to try because we were testing it). Every scrollable
  panel now shows a slim terminal-green scrollbar, draggable, auto-hidden when content fits.
- **Manage Dealers retired.** It was a strict subset of the Grand Ledger (hire/collect/cut with none of the
  books), and every panel-wide fix had to be applied twice. One ops console now; −284 lines.
- **Night-crew receipts.** The automation was unverifiable — fires only at day rollover, waits a night after
  being enabled, and left no trace beyond one Plug text. Every run now writes a receipt the GL displays
  ("last run: washed $X through N fronts" / "nothing needed washing"), even when it did nothing.
- **Dev test harness** (stripped from release, verified byte-level): F10 injects synthetic rows to stress the
  scroll UI without a 30-business save; F11 forces the night-crew sweep and dumps rider holdings.

**Launch-week ops lesson, logged for posterity:** the "1.5.1 didn't fix it" scare was a STALE-SESSION mirage —
Steam's API showed the Workshop updated 3 minutes after the final build; the complaining screenshots contained
strings that no longer exist in the fixed DLL. The mod loads at game START, so players (and the author, twice)
kept running old code after the file on disk was already new. Diagnosis discipline: check the artifact
(byte-level string probes), check the channel (Workshop time_updated), check what actually LOADED (Player.log's
ready-line) — before assuming the code is wrong.

---

## 2026-07-05 — Hotfix: scrollable panels for big fleets → v1.5.1

**First launch-week bug report** (neriku, Discord, with a screenshot): with a big fleet the laundry panel
grew TALLER THAN THE SCREEN — the wash buttons and the GL door sat unreachable below the bottom edge. Every
RUGS panel sized itself to its content with no scrolling; never tested at 30 businesses (the GL's whole
audience).

**Fix:** new shared `RugUi` helper — every panel (laundry, dealers, Grand Ledger, deal) is now a
**height-capped scroll container** (940px on the 1080 reference canvas): short content hugs exactly as
before, long content scrolls (mouse wheel / drag) instead of growing off-screen. All rebuild paths re-fit.

Also filed from the same conversation: "too OP with a lot of businesses" (~$1.5M/day from rider shops;
factories make it easy). Parked as a DESIGN question — scale is the on-ramp's point, but whether the
force-multiplier is the right factory approach (and at what rate) gets a proper think, not a hotfix.

---

## 2026-07-03 — The endgame drop: Drug-Wars pricing + the factory flywheel + the GL → v1.5.0

**Released:** v1.5.0 to the Steam Workshop — and it hit the FRONT PAGE of Big Ambitions mods on launch day.

The big one. This release realigns the mod around its true north star — **RUGS! is a companion to Big
Ambitions, not a second game** — and ships the economy that makes it work: *get stupid rich on drug money,
then buy 150 businesses to launder it.*

**Built (Release, deployed, validated in-engine; adversarially reviewed):**
- **Drug-Wars pricing, for real this time.** Replaced the global price + per-dealer margins with the actual
  dopewars model (bands checked against the GPL source): every district rolls its own price per rug daily
  within wide bands (commons ~4.4×, rares ~2.5×), and **buy = sell** — no dealer margin, no in-place flip;
  profit is purely spatial. The Plug's wire is now an arbitrage map (cheap-in / hot-in per rug). The home
  district's ONE privilege: the anchor buys all 9 rugs — the guaranteed offload. Fixes the "spreads too
  thin" community complaint (PetroS) at the root.
- **The factory force-multiplier.** A factory you own supercharges every store it supplies — the wash cap
  AND the dealer mint scale with its real output and each store's supply share, at an IDENTICAL rate on both
  sides (balance is the endgame; factories scale it without tilting it). Walks BA's own logistics graph,
  mirrors the engine's delivery gates (manned plans only, first MaxDestinations), pools per warehouse.
  Boosts read in the hundreds of percent (×11 cap). Warehouses are infrastructure — excluded from both roles.
- **The Grand Ledger ("the GL").** An 80s fiscal terminal on the laundry computer: every business, its role
  (FRONT washes / RIDER earns), intake/day, wash/day, factory feed — with the LEVERS inline (+RIDER /
  COLLECT / CUT) and the balance verdict that IS the endgame readout. Income and washcome, lifetime and daily.
- **The night crew (auto-wash).** 5+ businesses unlocks automatic nightly washing through every front —
  same caps, same rules, zero clicks. The Plug reports the run each morning.
- **Review hardening.** A multi-agent adversarial review swept the release; 9 confirmed findings fixed,
  including two exploits (cheap-lot overflow now refunds at cost; the boost ignores unmanned plans and no
  longer counts a warehouse once per plan) and the Esc-dodges-consequences hole (street moments must be
  faced). Deal panels re-roll their displayed prices at midnight.

**Design rulings (locked):** no bolt-on endgame — BA has none, so RUGS! has none; the balance flywheel IS
the endgame (notoriety track dropped). Rug production dropped for good (recipes are asset-locked; Harmony +
AssetBundles both forbidden) — factories participate as multipliers only. `docs/ROADMAP.md` rewritten to the
companion thesis. Next big build: supply lines (T3).

---

## 2026-06-28 — Events expansion (Drug-Wars pull-ins) + retro terminal skin → v1.4.0

**Bundled as v1.4.0** (Release build, deployed to ModsLocal; the Workshop upload is the boss's to push). A
second pass over street events — pulling in the faithful Drug-Wars beats we'd left on the table — plus a
retro-terminal reskin of the mod's own panels.

**Built (Release, deployed):**
- **Branching choices — the backbone.** The arrival panel was one message + one button; now an event can
  fork. `RugEvents.Arrival` carries an optional `List<Choice>` (each a label + an effect that returns the
  OUTCOME screen); `RugDealUI` renders a button per branch and reuses the existing continue→deal flow for the
  result. Near-zero new UI plumbing — the rest of the pass rides on this.
- **Four pull-ins, all sourced from the real games (no invention):**
  - **Interactive muscle (run / pay / stand).** The old silent shakedown is now a decision — pay a smaller
    certain cut, run (heat-weighted: clean / harder skim / jumped), or stand (they back off, or a beating).
    The bad branches reuse the existing `HospitalRespawn` (refactored into a shared `Jumped()`).
  - **Find money** — a dropped roll of dirty cash (no dealing heat — it isn't product).
  - **Flavor one-liners** — the corner-character beat, no stakes. Lines reused **with attribution** from
    *dopewars* (the open-source Drug Wars, GPL); the drug-specific and era/political ones were left out, not
    reworded. New `RugFlavor.cs`.
  - **Cheap lot** — a below-market rug lot, buy (dirty-first then wallet, through the shared gift path) or pass.
- **Retro terminal skin.** New `RugTheme` loads a monospace OS font (Consolas→Courier New→Lucida, with
  fallback) and dresses the deal / laundry / dealers panels with green box-drawing banner headers — the
  text-mode Drug-Wars look (**confirmed in-engine**). Literal Braille "drawille" art was ruled out (unreliable
  glyph coverage + the bundled-font asset wall); box-drawing gets the look without the friction. The Plug / IRS
  phone messages stay native (BA's own UI, not restyleable Harmony-free).

**Considered, declined:**
- **Combat.** Breaks both pillars — reuse-over-invention (BA has no combat substrate) and the no-cops /
  IRS-is-the-law fiction — so the choices stop at decisions, not fights. A genre pivot, not a pull-in.
- **Carry upgrade (the Drug-Wars trenchcoat).** Deferred: BA's `boxSize` gates per-stack cargo capacity
  (BA.cs:59235 et al.), so a hand-carry bump can't be a clean code-cap change — it'd inflate cart/storage
  capacity too and erode the carry-scarcity pillar the T2 haul loop leans on. Cheap-lot ships as the offer
  beat; the upgrade is parked as a deliberate design call.

---

## 2026-06-27 — Heat fix + T2 Phase A + cart fix → shipped as v1.3.2

**Released:** v1.3.2 to the Steam Workshop — bundles everything since 1.3.0: First Ones Free (T0.1), the T2
Phase A dealers, the cart-gift fix, and the heat fix below.

**Built (Release, deployed, validated in-engine — "works as designed"):**
- **Heat fix (Workshop bug report from hazarkoklu42).** Heat was a one-way ratchet — laundering didn't lower
  it and over-washing *added* it. Now **laundering cools heat** (washing dirty out sheds the matching exposure,
  `RugBooks.CleanLoad`), the books-wash is **hard-capped** (tap out → take the vig; the over-wash heat penalty
  is gone), and it's retuned to cool to safe in ~2-3 days (`CoolPerDay` 8→25, decay 0.80→0.72). Stuck saves
  recover by laundering + laying low.
- **T2 Phase A — your own dealers (the seam + the split).** `RugSidecars.cs` — the per-business sidecar model
  and the daily engine that READS a front's clean booked revenue (`orderHistory`) and **mints dirty cash on
  top** (× a flat Factor), never touching BA's money (Harmony-free); state in modData `rugs:dealers`.
  `RugDealersUI.cs` — the management panel, reached from the home laundry computer's new "Manage Dealers" door
  (hire/fire a dealer, collect its dirty take). `RugLaunder.AllOwned()`/`Fronts()` enforce the
  **earn-XOR-launder split**: a sidecar'd business drops out of the laundry venue list.
- **Cart-gift fix.** Event/freebie rug gifts are now cart-aware. `RugInventory.GiveRugs` routes a gift to a
  cart you're pushing or the matching box in hand, **never overwrites** what you're holding, and sells any
  overflow for dirty cash — a gift is never lost. Shared by the Find-a-stash event and First Ones Free.

**Designed (not built):**
- **T2 Phase B — the inventory gate.** Source ladder locked: a dealer sells from rugs you buy on the street
  and physically **haul** to the business (reuse the existing "stash at an owned business" model — no new
  stocking system); the daily take becomes `min(revenue × Factor, stored rug value)` and draws the stored
  units down. End-game framed as an **open kingpin sandbox** — no hard win; laundering capacity (capped by
  your legit empire) is the real constraint, the IRS is the clock.

---

## 2026-06-26 — T0.1 "First Ones Free" + T2 designed

**Shipped** (Release build, deployed):
- **First Ones Free (T0.1).** The first dealer you ever open fronts you a free starter stash (~20 REED),
  announced through the existing arrival panel, granted exactly once-ever (`rugs:firstFreeClaimed`), and it
  engages the mod. A broke player can now enter the loop with zero capital. New `RugFreebie.cs` + a one-line
  hook in `RugDealUI.Open` — reuses the find-stash grant and the arrival-announce flow wholesale.

**Designed** (not built):
- **T2 — Your Own Dealers.** Reframed from "rent a fake front" to a **sidecar bolted onto a business you
  already own.** The legit business runs untouched on BA's automation; we *read* its organic clean revenue
  and mint dirty cash on top (× a dealer-care factor) — never touching BA's money, so no Harmony. Hard rule:
  a business **either sells or launders, never both.** Per-sidecar inventory + dirty stash + importer-fed
  restock, built in phases. Full spec in the plan file.

---

## 2026-06-25 — 1.3.0.4.2.0 (the stakes layer)

The big content drop after the v1.1 ground floor. All Harmony-free, validated in-engine, shipped to the
Workshop.
- **Money laundering, reworked.** Wash dirty → clean by routing through a business you own — BA's own nightly
  bookkeeping books it as that business's taxed revenue (we fabricate an `Order` into
  `unprocessedCompletedOrders`). Plausibility-bound (capped by the business's real revenue); over-washing
  spikes IRS heat. Plus an instant quick-wash for a flat 30% cut.
- **The laundry computer.** Buy a computer, place it at home, click it → the laundry menu. A native BA item
  we hook (`EntityController`), not a custom object.
- **Street events on dealer approach.** Walking up to a dealer can trigger a moment before you trade — found
  stash / robbed / shakedown / hospital. At most one per neighborhood per day.
- **Dealer economy rework.** Home dealer buys ALL rugs; every other dealer buys 7–9 of 9 (commons + a few
  rares); rares are premium-priced (the payday). Universal sellability — you're never stuck holding product.
- **Plug wire** now reports your laundering capacity.

---

## 2026-06 — 1.1 (initial Workshop release)

The T0 ground floor — RUGS! goes live (409 visitors / 54 subs / 4 favorites at launch).
- 22 hand-placed dealers across 6 districts; a fluctuating market with per-dealer spreads (cross-town arbitrage).
- **"The Plug"** — a phone contact texting a daily wire (rates, your book, heat, tips).
- **Dirty-cash economy:** sells fill a hidden stash, buys spend it first; the visible wallet stays clean.
- **The IRS as the heat/consequence system** (no police in BA) — save-scum-proof audits scaled by heat.
- Rugs as real cargo (the vanilla box); market events (spikes/crashes); consequence events; a light laundering loop.
- Built **code-only, no Unity Editor** — clones vanilla assets at runtime. GPL-3.0.
