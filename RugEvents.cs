using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BAModAPI;            // IModLogger
using BigAmbitions.Items;  // ItemInstance, CargoInstance
using Helpers;             // PlayerHelper, ItemHelper
using UnityEngine;

namespace Rugs
{
    /// <summary>
    /// Market events — the Drug-Wars soul. Each in-game day there's a chance a rug's price SPIKES
    /// or CRASHES in ONE district for a few days. The multiplier is applied to that district's
    /// dealer quotes (buy AND sell, so the spread/ordering is preserved), and the swing is surfaced
    /// on the Plug's wire so the player chases it — buy where it's crashed, sell where it's hot.
    ///
    /// State lives in modData and is processed once per in-game day (gated on a persisted last-day),
    /// so reloading can't re-roll the day's event and active swings survive save/load.
    ///
    /// Consequence events (shakedown → dirty money, stash-robbed → stored product, hospital → you)
    /// layer on next; this is the economic backbone.
    /// </summary>
    internal static class RugEvents
    {
        // ---- tuning ----
        private const float DailyEventChance = 0.55f; // chance a market event fires on a new day
        private const float SpikeMin = 2.0f, SpikeMax = 4.0f; // a spike multiplies price ×2–4
        private const float CrashMin = 0.35f, CrashMax = 0.60f; // a crash multiplies price ×0.35–0.6
        private const int   DaysMin = 1, DaysMax = 3;  // how long a swing lasts
        private const int   MaxActive = 3;             // cap simultaneous swings (avoid clutter)

        private const string KEvents  = "rugs:events";
        private const string KLastDay = "rugs:eventLastDay";

        private struct Ev { public bool spike; public string rug; public string district; public float mult; public int start; public int days; }

        private static bool _loaded;
        private static int _lastDay = -1;
        private static List<Ev> _active = new List<Ev>();

        internal static void Reset() { _loaded = false; _lastDay = -1; _active = new List<Ev>(); }

        private static void Load()
        {
            if (_loaded) return;
            if (!RugBooks.SaveReady()) return;
            _active = Decode(RugBooks.GetRaw(KEvents));
            _lastDay = (int.TryParse(RugBooks.GetRaw(KLastDay), NumberStyles.Integer, CultureInfo.InvariantCulture, out int d)) ? d : -1;
            _loaded = true;
        }

        private static void Save()
        {
            RugBooks.SetRaw(KEvents, Encode(_active));
            RugBooks.SetRaw(KLastDay, _lastDay.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Active price multiplier for this rug in this district (1.0 = no event).</summary>
        internal static float PriceMultiplier(RugDef r, string district)
        {
            Load();
            if (!_loaded || r == null || _active.Count == 0 || string.IsNullOrEmpty(district)) return 1f;
            float m = 1f;
            foreach (Ev e in _active)
                if (e.rug == r.Key && e.district == district) m *= e.mult;
            return m;
        }

        /// <summary>Advance events once per new day: expire finished swings, maybe roll a new one.</summary>
        internal static void OnDayChanged(int day, IModLogger log)
        {
            Load();
            if (!_loaded || day <= _lastDay) return; // process each day once → reload-stable
            _lastDay = day;

            _active.RemoveAll(e => e.start + e.days <= day); // expire

            var districts = RugDealers.Districts;
            if (_active.Count < MaxActive && districts.Count > 0 && UnityEngine.Random.value < DailyEventChance)
            {
                bool spike = UnityEngine.Random.value < 0.5f;
                RugDef r = RugCatalog.All[UnityEngine.Random.Range(0, RugCatalog.All.Length)];
                string district = districts[UnityEngine.Random.Range(0, districts.Count)];
                float mult = spike ? UnityEngine.Random.Range(SpikeMin, SpikeMax) : UnityEngine.Random.Range(CrashMin, CrashMax);
                _active.Add(new Ev
                {
                    spike = spike, rug = r.Key, district = district,
                    mult = (float)Math.Round(mult, 2),
                    start = day, days = UnityEngine.Random.Range(DaysMin, DaysMax + 1),
                });
                log?.Info($"RUGS! event: {(spike ? "SPIKE" : "CRASH")} {r.Display} in {district} x{mult:0.0}.");
            }

            Save(); // consequence/upside events now fire on dealer-open (RollDealerArrival), not daily
        }

        // ---- consequence / upside events: rolled when you OPEN A DEALER (the Drug-Wars "arrival" beat) ----
        private enum Hit { Find, Robbed, Hospital, Shakedown, FindMoney, Flavor, Offer }

        /// <summary>An arrival event to announce: the line to show, plus (for events that whisk the player
        /// away, like the hospital) a deferred action to run on acknowledge and a flag to skip the buy/sell.</summary>
        internal sealed class Arrival
        {
            public string message;
            public bool   ends;        // true → after the player acknowledges, close (don't show buy/sell)
            public Action onContinue;  // deferred effect, run on acknowledge (e.g. the hospital teleport)
            public List<Choice> choices; // when non-empty, the panel offers these branches instead of one Continue button
            public static implicit operator Arrival(string s) => string.IsNullOrEmpty(s) ? null : new Arrival { message = s };
        }

        /// <summary>One branch of an interactive arrival: a button label, and the effect to run when picked —
        /// which returns the OUTCOME screen (another <see cref="Arrival"/>: its result line, and whether it ends
        /// or flows on to the deal). Lets an arrival fork (pay/run/stand, buy/pass) with no extra UI plumbing.</summary>
        internal sealed class Choice
        {
            public readonly string label;
            public readonly Func<Arrival> resolve;
            public Choice(string label, Func<Arrival> resolve) { this.label = label; this.resolve = resolve; }
        }

        private const string KArrivalDay = "rugs:arrivalDay:";  // + district key -> in-game day of THAT neighborhood's last arrival event
        private const float  ArrivalChance = 1.00f;             // 1.00 = an arrival event in every neighborhood each in-game day (capped to one per neighborhood/day). Lower for some eventless visits; climbs with heat.

        /// <summary>
        /// Rolled when the player opens a dealer ("arriving on the corner"). Returns an announcement to show
        /// before the buy/sell panel, or null for nothing. At most one arrival event PER NEIGHBORHOOD per
        /// in-game day, so dealer-hopping within a district doesn't spam them (but stepping into a new district
        /// can still pop one); the odds climb with heat.
        /// </summary>
        internal static Arrival RollDealerArrival(string district, IModLogger log = null)
        {
            try
            {
                if (!RugBooks.Engaged) return null;
                int nowDay = CurrentDay();
                string key = KArrivalDay + (district ?? ""); // per-neighborhood cooldown bucket
                // Cooldown: at most one arrival event per neighborhood per in-game day. (Guarded so a missing
                // last-day — which won't parse — leaves the player eligible, never falsely "on cooldown".)
                if (int.TryParse(RugBooks.GetRaw(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int last)
                    && nowDay <= last) return null;

                float chance = Mathf.Clamp01(ArrivalChance + RugBooks.Heat / 300f);
                if (UnityEngine.Random.value >= chance) return null;

                Arrival a = RollConsequence(log, district);
                if (a == null || string.IsNullOrEmpty(a.message)) return null;
                RugBooks.SetRaw(key, nowDay.ToString(CultureInfo.InvariantCulture));
                return a;
            }
            catch { return null; }
        }

        // Pick and resolve one consequence/upside, weighted by how riskily you're playing. Returns its line.
        // The district rides along so money events price and book at THIS corner's street rates.
        private static Arrival RollConsequence(IModLogger log, string district)
        {
            float heat = RugBooks.Heat;
            var pool = new List<(Hit hit, float w)>
            {
                (Hit.Flavor,    2.5f),  // mostly the corner's just being weird at you (no stakes)
                (Hit.Find,      1.0f),  // stumble on free product
                (Hit.FindMoney, 1.0f),  // ...or a dropped roll of cash
                (Hit.Offer,     1.2f),  // a street offer (a cheap lot going fast)
            };
            if (HasStoredRugs()) pool.Add((Hit.Robbed, 1.5f));                       // only if you have a stash to rob
            if (heat >= 40f || RugInventory.HeldType() != null)                      // jumped if hot or holding
                pool.Add((Hit.Hospital, Mathf.Clamp(heat / 50f, 0.4f, 2.0f)));
            if (RugBooks.Dirty >= 2000f)                                             // muscle shows up if you're sitting on a stash
                pool.Add((Hit.Shakedown, Mathf.Clamp(RugBooks.Dirty / 5000f, 0.5f, 3.0f)));

            float total = 0f; foreach (var c in pool) total += c.w;
            float roll = UnityEngine.Random.value * total, acc = 0f;
            Hit pick = Hit.Flavor;
            foreach (var c in pool) { acc += c.w; if (roll <= acc) { pick = c.hit; break; } }

            switch (pick)
            {
                case Hit.Robbed:    return ResolveStashRobbed(log);
                case Hit.Hospital:  return ResolveHospital(log);
                case Hit.Shakedown: return ResolveShakedown(log);
                case Hit.FindMoney: return ResolveFindMoney(log, district);
                case Hit.Offer:     return RollOffer(log, district);
                case Hit.Flavor:    return ResolveFlavor(log);
                default:            return ResolveFindStash(log, district);
            }
        }

        private static int CurrentDay()
        {
            try { var gi = SaveGameManager.Current; return gi != null ? gi.Day : 0; }
            catch { return 0; }
        }

        private static int ParseInt(string s, int fallback)
            => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

        // Shakedown → street muscle wants a cut of your DIRTY cash (the wallet is the IRS's turf, never theirs).
        // Interactive (the Drug-Wars "do you run?" beat): pay them off (sure thing, cheaper), run for it
        // (heat-weighted coin-flip), or stand your ground (risky). Each branch resolves to its own outcome line.
        private static Arrival ResolveShakedown(IModLogger log)
        {
            try
            {
                if (RugBooks.Dirty <= 0f) return null; // nothing on you worth muscling
                return new Arrival
                {
                    message = "Local muscle steps into your path. \"You're moving product on our block. That's gonna cost you.\"",
                    choices = new List<Choice>
                    {
                        new Choice("Pay them off",      () => MusclePay(log)),
                        new Choice("Run for it",        () => MuscleRun(log)),
                        new Choice("Stand your ground", () => MuscleStand(log)),
                    },
                };
            }
            catch (Exception e) { log?.Error("RUGS! muscle event failed."); log?.Error(e); return null; }
        }

        // Pay up — a smaller, CERTAIN cut of the dirty stash. Cost of doing business; on to the deal.
        private static Arrival MusclePay(IModLogger log)
        {
            float take = Mathf.Min(Mathf.Round(RugBooks.Dirty * UnityEngine.Random.Range(0.15f, 0.25f)), RugBooks.Dirty);
            if (take > 0f) RugBooks.LoseDirty(take);
            log?.Info($"RUGS! muscle: paid ${take:N0}.");
            return $"You peel off ${take:N0} and they wave you through. Cost of doing business.";
        }

        // Run — the hotter you are, the bigger a target, the likelier they run you down. Caught → a harder
        // skim or a beating; clean → nothing lost.
        private static Arrival MuscleRun(IModLogger log)
        {
            float caught = Mathf.Clamp(0.30f + RugBooks.Heat / 250f, 0.30f, 0.75f);
            if (UnityEngine.Random.value >= caught)
            {
                log?.Info("RUGS! muscle: ran clean.");
                return "You cut down an alley and lose them. Nothing gone but your pride.";
            }
            if (UnityEngine.Random.value < 0.55f)
            {
                float take = Mathf.Min(Mathf.Round(RugBooks.Dirty * UnityEngine.Random.Range(0.35f, 0.6f)), RugBooks.Dirty);
                if (take > 0f) RugBooks.LoseDirty(take);
                log?.Info($"RUGS! muscle: caught, -${take:N0}.");
                return $"They run you down at the corner and take it the hard way — ${take:N0} gone.";
            }
            return Jumped("They catch you and put you on the ground.", log);
        }

        // Stand — a coin-flip on nerve: they back off (nothing lost) or it turns into a beating.
        private static Arrival MuscleStand(IModLogger log)
        {
            if (UnityEngine.Random.value < 0.45f)
            {
                log?.Info("RUGS! muscle: faced down.");
                return "You square up and don't blink. They weigh it, decide you're more trouble than you're worth, and drift off. Nothing lost.";
            }
            return Jumped("You throw the first punch. It does not go your way.", log);
        }

        // Got jumped → BA's HospitalRespawn handles the faint, teleport, time skip, native ~$2k bill and a
        // happiness hit. Announce first, run the teleport on acknowledge, skip the buy/sell (ends=true). No death.
        private static Arrival Jumped(string lead, IModLogger log)
        {
            var gm = InstanceBehavior<GameManager>.Instance;
            if (gm == null) return lead + " You limp away, rattled."; // no teleport available — soft fallback
            log?.Info("RUGS! event: hospital (got jumped).");
            return new Arrival
            {
                message = lead + " Everything's going black…",
                ends = true,
                onContinue = () => { try { gm.StartCoroutine(gm.HospitalRespawn()); } catch { } },
            };
        }

        private static bool HasStoredRugs()
        {
            try
            {
                var gi = SaveGameManager.Current;
                if (gi == null || gi.BuildingRegistrations == null) return false;
                foreach (BuildingRegistration reg in gi.BuildingRegistrations)
                {
                    if (reg == null || !reg.RentedByPlayer || reg.itemInstances == null) continue;
                    foreach (ItemInstance inst in reg.itemInstances.Values)
                    {
                        if (inst?.cargoInstances == null) continue;
                        foreach (CargoInstance c in inst.cargoInstances)
                            if (c != null && c.amount > 0 && RugCatalog.ByKey(c.itemName) != null) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // Stash robbed → product vanishes from a business/warehouse you own (never your carried bag).
        private static Arrival ResolveStashRobbed(IModLogger log)
        {
            try
            {
                var gi = SaveGameManager.Current;
                if (gi == null || gi.BuildingRegistrations == null) return null;

                var hits = new List<(ItemInstance holder, CargoInstance cargo)>();
                foreach (BuildingRegistration reg in gi.BuildingRegistrations)
                {
                    if (reg == null || !reg.RentedByPlayer || reg.itemInstances == null) continue;
                    foreach (ItemInstance inst in reg.itemInstances.Values)
                    {
                        if (inst?.cargoInstances == null) continue;
                        foreach (CargoInstance c in inst.cargoInstances)
                            if (c != null && c.amount > 0 && RugCatalog.ByKey(c.itemName) != null) hits.Add((inst, c));
                    }
                }
                if (hits.Count == 0) return null;

                var target = hits[UnityEngine.Random.Range(0, hits.Count)];
                RugDef r = RugCatalog.ByKey(target.cargo.itemName);
                int take = Mathf.Clamp(Mathf.CeilToInt(target.cargo.amount * UnityEngine.Random.Range(0.3f, 0.6f)), 1, target.cargo.amount);
                if (take >= target.cargo.amount) target.holder.RemoveFromCargo(target.cargo);
                else target.holder.ReduceFromCargo(target.cargo, take);
                target.holder.OnItemsInCargoUpdated()?.Invoke();

                log?.Info($"RUGS! event: stash robbed ({take} {r.Display}).");
                return $"Your stash got hit — somebody walked off with {take:N0} {r.Display} while nobody was looking.";
            }
            catch (Exception e) { log?.Error("RUGS! stash-robbed failed."); log?.Error(e); return null; }
        }

        // Hospital → you got jumped working a deal (rolled at random when you're hot or holding). Shares the
        // Jumped() outcome with the muscle event's bad branches.
        private static Arrival ResolveHospital(IModLogger log)
        {
            try { return Jumped("Bad night — got jumped working a deal.", log); }
            catch (Exception e) { log?.Error("RUGS! hospital event failed."); log?.Error(e); return null; }
        }

        // Find a stash → free product, routed to hands or the cart you're pushing (or sold on the spot for
        // dirty cash when there's no room). "On the spot" means THIS corner: overflow cashes at the local
        // street price and books to this district — never the neutral base price.
        private static Arrival ResolveFindStash(IModLogger log, string district)
        {
            try
            {
                RugDef r = RugCatalog.All[UnityEngine.Random.Range(0, RugCatalog.All.Length)];
                int found = UnityEngine.Random.Range(5, 31);
                RugInventory.GiveResult g = RugInventory.GiveRugs(r, found, RugMarket.StreetPrice(r, district), district);

                log?.Info($"RUGS! event: found {found} {r.Display} (carried {g.carried}, cashed {g.cashed}).");
                if (g.cashed <= 0)
                    return $"Somebody's stash, just sitting there — {g.carried:N0} {r.Display}. Yours now.";
                if (g.carried > 0)
                    return $"Somebody's stash, just sitting there — {found:N0} {r.Display}. Grabbed what you could carry; offloaded the rest for ${g.cashValue:N0} dirty.";
                return $"Found a stash of {found:N0} {r.Display} — no room to carry it, so you offloaded it on the spot for ${g.cashValue:N0} dirty.";
            }
            catch (Exception e) { log?.Error("RUGS! find-stash failed."); log?.Error(e); return null; }
        }

        // Find money → a dropped roll of dirty cash. No dealing exposure (heatWeight 0) — it isn't product.
        private static Arrival ResolveFindMoney(IModLogger log, string district)
        {
            try
            {
                int amount = UnityEngine.Random.Range(40, 401);
                RugBooks.AddDirty(amount, district, 0f); // found HERE — book it to this district
                log?.Info($"RUGS! event: found ${amount} cash.");
                return $"A fat roll of bills on the sidewalk, nobody in sight. ${amount:N0}, finders keepers.";
            }
            catch (Exception e) { log?.Error("RUGS! find-money failed."); log?.Error(e); return null; }
        }

        // Flavor → pure texture, no stakes. A corner character says something absurd; then on to the deal.
        private static Arrival ResolveFlavor(IModLogger log)
        {
            string line = RugFlavor.Line();
            return string.IsNullOrEmpty(line) ? null : new Arrival { message = line, ends = false };
        }

        // A street offer (accept/decline). The Drug-Wars "cheap product going fast" beat → a discounted rug lot.
        // (The bigger-coat carry upgrade is deferred: BA's boxSize gates per-box capacity, so it fights the
        // carry-scarcity design rather than slotting in cleanly.)
        private static Arrival RollOffer(IModLogger log, string district)
        {
            try
            {
                RugDef r = RugCatalog.All[UnityEngine.Random.Range(0, RugCatalog.All.Length)];
                int units = UnityEngine.Random.Range(10, 41);
                float discount = UnityEngine.Random.Range(0.45f, 0.70f);              // ~30–55% under THIS corner
                // Discount off the LOCAL street quote, not the neutral base — "way under the street" must be
                // TRUE on the street the player is standing on (a crashed district discounts the crash price).
                float unit = Mathf.Max(1f, Mathf.Round(RugMarket.StreetPrice(r, district) * discount));
                float cost = unit * units;
                return new Arrival
                {
                    message = $"A twitchy guy leans in. \"Gotta move {units} {r.Display} right now — ${unit:N0} a piece, way under the street. You want it?\"",
                    choices = new List<Choice>
                    {
                        new Choice($"Buy · ${cost:N0}", () => BuyCheapLot(r, units, cost, district, log)),
                        new Choice("Pass",              () => (Arrival)"You shake your head. He's already melting back into the crowd."),
                    },
                };
            }
            catch (Exception e) { log?.Error("RUGS! offer failed."); log?.Error(e); return null; }
        }

        // Pay for the lot (dirty cash first, then the wallet — mirrors RugTrading.Buy) and route it through the
        // shared gift path (into the held box / pushed cart, overflow flipped for dirty). Never spends past funds.
        private static Arrival BuyCheapLot(RugDef r, int units, float cost, string district, IModLogger log)
        {
            try
            {
                float funds = RugBooks.Dirty + SaveGameManager.Current.Money;
                if (funds < cost) return "You count what's on you and come up short. He clicks his tongue and splits.";
                float fromDirty  = RugBooks.SpendDirty(cost);
                float fromWallet = cost - fromDirty;
                if (fromWallet > 0f)
                    GameManager.ChangeMoneySafe(-fromWallet, new TransactionInfo("ba:transaction_rugdeal", "rugs"), null, null, force: true, showNotification: true);
                RugBooks.MarkEngaged();
                // Overflow the player can't carry refunds AT the lot's unit price (cost), never at market —
                // cashing a discounted lot at full price was a guaranteed in-place money pump.
                RugInventory.GiveResult g = RugInventory.GiveRugs(r, units, cost / units, district);
                log?.Info($"RUGS! offer: bought {units} {r.Display} for ${cost:N0} (carried {g.carried}, cashed {g.cashed}).");
                if (g.carried > 0 && g.cashed > 0)
                    return $"Done. {g.carried:N0} {r.Display} in the bag; the rest wouldn't fit — he takes it back at cost (${g.cashValue:N0} to your dirty roll).";
                if (g.cashed > 0)
                    return $"No room to carry any of it — the lot goes straight back at what you paid. ${g.cashValue:N0} lands dirty; nobody made a dime.";
                return $"Done — {g.carried:N0} {r.Display} for ${cost:N0}. Steal of the day.";
            }
            catch (Exception e) { log?.Error("RUGS! cheap-lot buy failed."); log?.Error(e); return "The deal falls apart in your hands."; }
        }

        /// <summary>One headline per active swing, for the Plug's wire.</summary>
        internal static List<string> ActiveHeadlines()
        {
            Load();
            var lines = new List<string>();
            if (!_loaded) return lines;
            foreach (Ev e in _active)
            {
                RugDef r = RugCatalog.ByKey(e.rug);
                if (r == null) continue;
                string place = District(e.district);
                lines.Add(e.spike
                    ? $"• {r.Display} is HOT in {place} — sell high"
                    : $"• {r.Display} is flooding {place} — buy cheap");
            }
            return lines;
        }

        private static string District(string key)
        {
            try { string n = Localizor.LocalizorManager.GetLocalization(key); return string.IsNullOrEmpty(n) ? key : n; }
            catch { return key; }
        }

        // ---- modData encode/decode: "S|rug|district|mult|start|days;..." ----
        private static string Encode(List<Ev> evs)
        {
            var sb = new StringBuilder();
            foreach (Ev e in evs)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(e.spike ? 'S' : 'C').Append('|').Append(e.rug).Append('|').Append(e.district).Append('|')
                  .Append(e.mult.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                  .Append(e.start.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(e.days.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static List<Ev> Decode(string s)
        {
            var list = new List<Ev>();
            if (string.IsNullOrEmpty(s)) return list;
            foreach (string part in s.Split(';'))
            {
                if (part.Length == 0) continue;
                string[] f = part.Split('|');
                if (f.Length != 6) continue;
                if (!float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float mult)) continue;
                if (!int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int start)) continue;
                if (!int.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int days)) continue;
                list.Add(new Ev { spike = f[0] == "S", rug = f[1], district = f[2], mult = mult, start = start, days = days });
            }
            return list;
        }
    }
}
