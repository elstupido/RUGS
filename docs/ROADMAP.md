# RUGS! — Roadmap & Design Vision

> **Status (2026-07-02):** v1.4.0 shipped (events + retro skin). Built and awaiting in-engine validation:
> the **Drug-Wars pricing overhaul** (per-district bands, buy=sell) and the **factory force-multiplier**
> (with role exclusion + UI surfacing). Next ship: **v1.5.0**. Last big build remaining: **supply lines**.

> **Versioning:** `<major>.<minor>.<patch>` + a fixed `.0.4.2.0` tail (e.g. `1.5.0.0.4.2.0`).

## North star — RUGS! is a COMPANION, not a second game

**Big Ambitions has no endgame — so RUGS! doesn't bolt one on either.** RUGS! is a companion layer that
rides BA's *actual* gameplay the whole way up. Wherever BA's own progression takes the player — first shop,
retail chain, warehouses, logistics, **factories**, vertical integration — RUGS! is there making that same
rung *dirty*. We never build a parallel ladder; we make BA's ladder the laundering machine.

**The logline:** *get stupid rich on drug money — then buy 150 businesses to launder it.* The thing vanilla
players call the pointless late-game grind (own everything, automate everything) is, in RUGS!, the entire
point: every legit business is a wash cycle, every factory a force multiplier, and the IRS is hunting all
of it.

**The endgame IS the flywheel** (no trophy, no rank ladder, no win screen — same as BA):

```
deal dirty cash  →  need washing  →  buy legit businesses (washers + earners)
      ↑                                            ↓
run a bigger dirty operation  ←  factories supply your stores → multiply wash & earn
```

When BA eventually ships its real late game, RUGS! players are already standing in it — rich, hunted, and
motivated. Until then, RUGS! supplies the one thing BA's rich-but-bored wall lacks: **a reason the empire
matters** (documented community gap: dynamic pressure, risk, "money can't buy" stakes).

**Two pillars, in priority order:**
1. **The fantasy (micro).** Being a dealer: working corners, hauling product across town, the branching
   street encounters, the self-authored stories. Feed this first — it's the heart.
2. **The arc (macro).** Dirty income outgrows any one front's wash capacity → the player backs into
   acquiring a Manhattan empire *as a cover story* → factories multiply the network → the taxman scales
   with the ambition.

## How a BA business participates (the three ways)

| BA thing | RUGS! role | Rule |
|---|---|---|
| Retail/service business you own | **Washer** — launder dirty cash through its real books (plausibility-capped/day) | A business **either washes or earns, never both** |
| Retail/service business you own | **Earner** — dealer sidecar reads its clean revenue, mints dirty on top | same split |
| **Warehouse/factory** | **Force multiplier** — never a role-holder; it boosts the wash cap AND the dirty mint of every store it *supplies*, scaled by its real output and each store's share | infrastructure only (excluded from both role pools) |

The multiplier reskins BA's real vertical-integration bond — "a factory makes the stores it feeds more
profitable" becomes *"...better at the dirty game."* Strategy emerges from **wash/earn distribution**:
which stores earn, which wash, and where the factory supply flows.

## Built (the companion layer today)

- **T0 street hustle** — 22 hand-placed dealers / 6 districts; rugs as real contraband cargo (worth $0 to
  vanilla); one-box carry scarcity; the anchor dealer by home **buys all 9 rugs** (guaranteed offload — the
  home district's only special rule).
- **Drug-Wars pricing** — per-district wide price bands (commons ~4.4×, rares ~2.5×), re-rolled daily,
  **buy = sell** (no dealer margin): profit is purely spatial. District spike/crash events layer on top.
  The Plug's wire is the arbitrage map (cheap-in / hot-in per rug).
- **The money model** — hidden dirty stash (modData), the risk triangle (dirty = street-robbable/unspendable;
  wallet = IRS-exposed; laundering bridges). Heat = un-laundered dealing exposure; laundering cools it;
  save-scum-proof audits fine the wallet.
- **Laundering** — fabricated sales routed through BA's own nightly books (clean, taxed — the tax is the
  fee), hard-capped by each front's believable revenue; instant quick-wash vig as the release valve; the
  home laundry computer + "Manage Dealers" panels.
- **T2 dealers (earners)** — sidecars on owned businesses minting dirty off real clean revenue.
- **Factory force-multiplier** — supplied stores wash & earn bigger (`RugFactoryBoost`); warehouses excluded
  from both role pools; boost surfaced in both panels + the wire.
- **Events** — market spikes/crashes; branching arrivals (pay/run/stand muscle, find stash/money, cheap-lot
  offer, dopewars flavor lines); hospital/robbery/shakedown consequences; once per neighborhood/day.
- **Non-interference** — everything dormant until the first deal. Install-and-ignore == vanilla BA.

## Remaining work

1. **Validate + tune the unreleased stack** (pricing bands, factory boost knobs `BoostScale`/`MaxBoost`) —
   in-engine, user-driven.
2. **Ship v1.5.0** — version bump, DEVLOG/DIARY, STORE.md what's-new, Workshop upload.
3. **Supply lines (the last big build).** Reskin BA's importer / import-partnership system as the
   "connect" who smuggles rugs in bulk — the sourcing upgrade above street buys. Design-first with the
   user (payment clean-vs-dirty, delivery destination, heat, pricing vs street). After this, the
   RUGS-native systems are **complete**; everything else is tuning, polish, and riding BA's future content.
4. **T2 Phase B (paused, user's call)** — dealer inventory gate (sidecars consume hauled-in rugs).

## Dropped / parked (deliberate rulings — don't re-litigate)

- **T4 rug production — DROPPED.** Impossible code-only (recipes are addressable ScriptableObjects, no
  runtime hook → would need Harmony + AssetBundles, both forbidden), and the ride-the-shell workaround was
  vetoed. Factories participate as multipliers instead.
- **Notoriety / prestige track — DROPPED.** BA has no endgame; RUGS! doesn't bolt one on. The flywheel is
  the endgame.
- **Combat — DROPPED.** No BA substrate (pure invention) and it breaks the no-cops/IRS-is-the-law fiction.
- **Carry upgrade — PARKED.** BA's `boxSize` gates per-stack capacity; bumping it silently inflates
  cart/storage too and erodes carry scarcity.
- **Literal Braille "drawille" — PARKED.** Font coverage + the AssetBundle wall; the retro skin is
  box-drawing art instead.

## Design laws (standing)

| Law | Meaning |
|---|---|
| **Companion, not competitor** | Ride BA's systems and progression; never build a parallel game |
| **Reuse over invention** | Find the BA system that already does the thing; re-skin it for rugs |
| **Harmony-free, AssetBundle-free** | Public APIs + runtime clones only; if it can't be done code-only, it isn't done |
| **Wash XOR earn** | A business holds one dirty role; factories hold none (multiplier only) |
| **Always offloadable** | The home anchor buys all 9 rugs; other dealers buy 7–9 of 9 |
| **The IRS is the law** | No police, no combat — enforcement is heat, audits, repossession |
| **Non-interference** | Dormant until the first deal |
| **Design-first** | Think it through with the author before coding; when misaligned, stop and quiz |

## Tuning knobs

- **Price bands:** `RugMarket` — `CommonLow/High` (0.50–2.20), `RareLow/High` (0.65–1.60).
- **Market events:** `RugEvents` — `DailyEventChance`, `SpikeMin/Max`, `CrashMin/Max`, `DaysMin/Max`, `MaxActive`.
- **Arrival events:** `RugEvents` — `ArrivalChance`, per-event weights in `RollConsequence`.
- **Factory multiplier:** `RugFactoryBoost` — `BoostScale` (1000: $1k/day of supply = +100%), `MaxBoost` (10 = ×11 ceiling).
- **Laundering:** `RugLaunder` — `PlausibleInflation` (0.35), `WindowDays` (7), `DealerVig` (0.30),
  `AutoWashMinBusinesses` (5 — the night-crew unlock, toggled in the Grand Ledger).
- **Sidecars:** `RugSidecars` — `Factor` (0.6), `Wage`, `HeatWeight`.
- **Heat:** `RugHeat` — `HeatLoadForMax`, `HeatLoadDecayPerDay` (0.72), `CoolPerDay` (25), `MaxDailyAuditChance`, `MaxAuditRate`.
- **Per-rug:** `RugCatalog` — base price, `HeatWeight`, common/rare.
- **Release switch:** `RugsConfig.Dev`.
