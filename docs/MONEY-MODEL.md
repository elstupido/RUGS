# RUGS! — The Money Model (dirty vs. declared)

> How the mod tracks money earned from rugs, whether it's been declared, and how
> that becomes laundering (T5) and heat (T1/E2). Decided from an exhaustive survey
> of Big Ambitions' money systems, hardened by an adversarial stress-test that
> **killed one wrong assumption** (see "The tax trap" below). All citations are to
> the decompiled `BigAmbitions.dll` (`BA.cs:LINE`). **Zero Harmony patches.**

## The shape of it

Big Ambitions has **one money pool** — `GameInstance.Money` is a single float
(`BA.cs:29434`). There is no native cash/bank or declared/undeclared split, and
**no police / fraud / heat / audit system at all**. So the mod invents the split:

- **The vault — `GameInstance.modData`** (`BA.cs:29621`): a `public
  Dictionary<string,string>` that is the *only* occurrence in 212k lines. Vanilla
  declares it and never reads, writes, or trims it. It serializes into the save in
  **both** formats (Newtonsoft JSON *and* Odin binary), so our state rides inside
  the save atomically. This is the home for money state — **not** a side-car file
  (a side-car can desync on reload/branching saves). Keys are namespaced `rugs:`.
- **Dirty is a scalar, not a pool.** We store `rugs:dirtyMoney` alongside the
  single `Money` float; "clean" is the implicit remainder (`Money − dirty`).
  [RugBooks.cs](../RugBooks.cs) keeps the dirty tally honest with a per-tick
  `Reconcile()` that clamps `dirty ≤ Money` — which makes **"spend clean first"**
  automatic (dirty only falls once spending eats past the clean remainder).
- **The ledger entry is audit-only.** Each sale also leaves a tagged
  `Transaction`, but that queue is capped/trimmed at 1000 (`BA.cs:30982`), so it
  can never be re-summed for a lifetime total. The `modData` scalars are the
  single source of truth.

### What we store (`modData`, string-encoded, invariant culture)

| Key | Meaning |
|-----|---------|
| `rugs:dirtyMoney` | un-laundered cash on hand (clamped to wallet each tick) |
| `rugs:declaredLifetime` | cumulative amount ever laundered/declared |
| `rugs:earnedLifetime` | gross rug income ever (stat) |
| `rugs:dirtyByDistrict` | `key=amt;key=amt` per neighborhood (feeds district heat, D2) |

## Laundering (dirty → clean) — built (1.3)

Money is one pool, so "clean" is just `Money − dirty`. Laundering moves cash OUT of the
dirty scalar and books it as legitimate, **taxed** income — through Big Ambitions' own
systems, zero Harmony. Two paths, both in [RugLaunder.cs](../RugLaunder.cs):

**1. Through a business you own (cheap, capped).** We fabricate a completed, paid `Order`
on a legit business the player owns and add it to that business's public
`unprocessedCompletedOrders` (`BA.cs:6372`). BA's nightly `BusinessHelper.RunDaily` →
`ProcessDailyOrders` (`BA.cs:195017/195091`) then books it as that business's revenue via
`ChangeMoneySafe("ba:transaction_revenue", …)` — clean, on the financial statements, **and
taxed** at tax time. The wash leaves the dirty stash *now*; the clean money lands at the next
in-game midnight ("clears overnight"). `wholesalePrice=0` makes the whole sum profit, so the
**tax is the cost** — there's no separate fee.
   - **Plausibility-bound.** A business absorbs only so much before the books look cooked. Safe
     daily capacity = the business's *organic* trailing daily revenue × `PlausibleInflation`
     (1.5 since v1.6.0 — the balance master knob; rider intake is locked to it at ×12/7), where organic = reported revenue minus our own past washes (so a wash can't inflate
     its own ceiling). A dead shell — or a future rug front with $0 legit BA income — has ~$0
     capacity, so the model is self-consistent.
   - **Greed draws the IRS.** You can push past the safe line, but every over-plausible dollar
     feeds heat (`LaunderHeatWeight`), and the audit then bites the now-clean wallet.

**2. The instant quick-wash (convenience).** `RugBooks.LaunderInstant` debits the dirty scalar
and credits the wallet directly via `ChangeMoneySafe("rugs:transaction_launder", …)`, minus a
flat `DealerVig` (30%) cut. No business, no cap, no overnight wait — the vig is the only cost.

Both paths open from the **laundry menu**, reached by clicking a computer the player buys and
places at home ([RugMachine.cs](../RugMachine.cs)) — a native BA buy→place→interact item we only
hook, never a custom object. (The old gambling-winnings "declare" primitive is gone — see the
tax trap below for why that path was a dead end.)

## The tax trap (what the stress-test killed)

**Wrong assumption (mine):** "push rug income into `CurrentTaxPeriodGamblingWinnings`
and the IRS taxes it as unexplained income." **It does not hit a small-time
dealer**, for three verified reasons:
1. `GenerateTaxes` only fires when `PlayerShouldDoTaxes()` is true — `Day % 60 == 0`
   **AND** trailing-year registered-business profit ≥ **$150,000** (`BA.cs:196693`).
   Rug cash credited without a business address never enters `financialSummaries`,
   so it can't even help cross that gate.
2. That gambling pool is **zeroed every tax event** (`BA.cs:196659`), so anything
   stashed there for a sub-threshold player is silently discarded — never billed.
3. Vanilla has **no detection of undeclared cash** whatsoever.

**Consequence:** the gambling-pool path is valid only as the **laundering "declare"
target for players already over the $150k business threshold**. It is *not* a
ground-floor enforcement mechanism. **So 1.3 abandoned it entirely** — laundering now
books through a real business's order pipeline (taxed, plausibility-bound) or the instant
vig wash, and heat is our own greenfield meter (below).

## Ground-floor heat (E2) — must be our own code

Because vanilla won't penalize a small dealer, **heat/enforcement is greenfield mod
logic** — built ([RugHeat.cs](../RugHeat.cs), with the audit cadence in [RugBooks.cs](../RugBooks.cs)):
- A mod-owned **heat** meter (`rugs:heat`, 0–100) that rises with dirty-cash
  growth/concentration and decays over time (modeled on the Happiness pattern, not
  overloading it).
- A mod-owned **audit cadence** (weekly tick / probabilistic roll scaled by
  heat + dirtyMoney) that levies an "unexplained income" penalty via a **negative
  `ChangeMoneySafe`** fine (`rugs:transactioncategory_fine`), surfaced through the
  IRS contact's texts — the IRS-as-heat fantasy, off the gated vanilla tax cycle.
- Laundering lowers `dirtyMoney` before it can be penalized → the counter-play that
  unifies T1 (heat) and T5 (laundering).

## Key APIs

| API | `BA.cs` | Use |
|-----|---------|-----|
| `GameInstance.modData` | 29621 | the in-save vault (only occurrence; vanilla-untouched) |
| `GameInstance.Money` | 29434 | single money float |
| `GameInstance.CurrentTaxPeriodGamblingWinnings` | 29496 | launder/declare write-target (taxed at 196773, reset at 196659) |
| `GameManager.ChangeMoneySafe` | 30930 | credit sells / debit fines+fees with a tagged `TransactionInfo` |
| `ChangeMoney` casino hook | 30954-30957 | the exact precedent we clone for the declare path |
| `TaxHelper.GenerateTaxes` | 196727 | taxable base = regBiz + gambling + deductibles + realEstate; never reads `Transactions` |
| `PlayerShouldDoTaxes` | 196693 | the `Day%60==0 && profit≥150k` gate (why ground-floor heat can't use it) |
| `ReduceTransactionQueue` | 30982 | proves the ledger is audit-only (1000 cap) |
