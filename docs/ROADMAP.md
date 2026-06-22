# RUGS! — Roadmap & Design Decisions

## Milestones

### Done
- **M0 — Clickable street dealer NPC.** Proved a code-spawned `EntityController` can be spawned,
  pinned, hovered, and clicked, opening a confirmation. *(validated in-engine)*
- **M1 — Game-dialog buy (superseded).** Bought a rug through the game's NPC dialog system.
  Replaced by the custom panel because that dialog is phone-styled and can't be reskinned.
- **M2 — Custom "street deal" panel.** Runtime-built uGUI panel with live prices and Buy. *(validated)*
- **M3 — Full buy + sell arbitrage.** Dealers buy and sell disjoint rug subsets at `market × constant`;
  Buy/Sell across dealers for profit. *(validated)*
- **A — Bulk carry & quantity.** Bags hold up to 9,999; ×1/×10/×100/Max buy, Sell All; live
  Cash/Carrying readouts; HUD sync on merge.
- **B1 — Sell only to rug dealers.** Rugs stripped of retail type, demand flag, and store/wholesale
  tags → contraband that exists only in the dealer economy. Stale demand-market entries scrubbed.

### Next
- **C1 — Discoverable, persistent dealers.** Bake hand-captured spots (`RUGS_dealer_spots.txt`, via
  the F7 tool) into a fixed roster placed on city load; persist each dealer's mix per save. Replaces
  F8 as the real source of dealers. *(needs: validated sidewalk spots from the player.)*
- **C2 — (optional) "Dealers found" log** — track who/where/what-they-trade.
- **D1 — Neighborhood-aware spreads.** `price = market × dealerConstant × regionMultiplier`;
  per-dealer constants drift daily.
- **D2 — Regional events.** Random events (e.g. "Police crackdown — Manhattan") that spike/crash a
  district's rug prices for N days, announced via notification. The big spread-mover.

### Later / phase 2
- Tier progression: street dealer → … → **dealership** (more volume, better margins, more rug types).
- Deploying your *own* higher-tier dealers (the "hybrid" loop).
- Multi-bag / hand-truck hauling for larger volume.
- Steam Workshop polish (thumbnail, in-game Mod Creator upload), strip dev hotkeys.

## Design decisions (and why)

| Decision | Choice | Why |
|----------|--------|-----|
| Mod type | Custom content, **code-only** | User wanted to avoid the Unity Editor / AssetBundles; clone vanilla assets instead. |
| Products | 5 parody rugs (REED/ROCAINE/RETH/ROPE/RANEX) | The mod's premise. |
| Dealer ownership | **Hybrid** | Buy from street NPCs, run product through your own higher-tier dealers (phase 2). |
| Tiers | **Dealer rank ladder** | Street dealer → dealership; higher tiers = more volume/margin/variety. |
| Price engine | **Global fluctuating market × per-dealer constants**, plus **regional events** | Market gives daily ups/downs; per-dealer constants create cross-dealer arbitrage; events create big regional swings. |
| Inventory | **Physical game cargo** | Most immersive — buy spawns a real bag you carry; sell consumes it. |
| Carry ceiling | ~**9,999** per bag | Drugs ignore normal small carry limits. |
| Trade UI | **Custom runtime uGUI panel** | The game's shared dialog is phone-styled and not reskinnable. |
| Selling | **Only to rug dealers** | Core to the fantasy; rugs are contraband, out of normal commerce. |
| Dealer placement | **Hand-placed by the player**, captured via F7 | Player authors exact sidewalk spots; mod bakes them into a roster. |

## Tuning knobs (where to change balance)

- Rug base prices: `RugCatalog.cs`.
- Market volatility / mean-reversion / clamps: `RugMarket.Fluctuate`.
- Dealer sell/buy constant ranges & subset sizes: `RugDealerController.RollInventory`.
- Carry ceiling: `RugTrading.BagCeiling` (+ `boxSize` in `RugItems`).
