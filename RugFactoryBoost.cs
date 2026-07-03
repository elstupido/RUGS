using System.Collections.Generic;
using UnityEngine;  // Mathf

namespace Rugs
{
    /// <summary>
    /// Factories as a FORCE MULTIPLIER (no rug production — that would need Harmony + AssetBundles, which this
    /// mod refuses). A factory you own doesn't itself wash or earn; it SUPERCHARGES the retail stores it
    /// SUPPLIES — a store fed by your factory washes more (a bigger <see cref="RugLaunder"/> daily cap) and/or
    /// mints more dirty cash (a bigger <see cref="RugSidecars"/> take), scaled by how much of the factory's
    /// output flows to it. This reskins BA's real vertical-integration bond (a factory makes the stores it feeds
    /// more profitable) into "...better at the dirty game." SINGLE-PURPOSE HOLDS: the factory is infrastructure;
    /// the served store keeps its one role (washer OR generator) and the factory just turns its dial up.
    ///
    /// Code-only, Harmony-free. We walk BA's logistics graph:
    ///   SaveGameManager.Current.logisticsManagerPlans (BA.cs:29601)
    ///     LogisticsManagerPlan.isFactory / .targetAddress (the warehouse) / .destinations (BA.cs:145812)
    ///       LogisticsManagerPlanDestination.deliveryTargetAddress (served store) / .stockTargets (BA.cs:206328)
    /// and read the feeding warehouse's produced value via its trailing ORDER-HISTORY revenue — BA rolls the
    /// factory's export ledger into the warehouse's books each night (BA.cs:195126), so that's a stable, persisted
    /// figure (the raw factoryExports list, BA.cs:6435, is cleared nightly and reads ~$0 at the mint moment).
    /// Address matching uses '==' exactly as the engine does (BA.cs:88670). The logistics types are held as 'var'
    /// so we don't bind their namespaces — only the global BuildingRegistration is named.
    ///
    /// PROXY / TUNING NOTES (validate in-engine): per-store shipped *value* isn't persisted, so we split a
    /// warehouse's believable daily produced value (its trailing order-history revenue — STABLE and available at
    /// mint time) across its destinations, weighted by each store's stockTargets COUNT (the per-item amount field
    /// lives in an external assembly we don't bind). Snapshotted once per in-game day. BoostScale/MaxBoost first-pass.
    /// </summary>
    internal static class RugFactoryBoost
    {
        // ---- tuning ----
        // Boosts read in the HUNDREDS of percent ("fed +370%", not +37%) — vertical integration is the
        // endgame lever, so a factory-fed store should feel supercharged, not politely improved.
        internal const float BoostScale = 1000f; // every $1k/day of factory value to a store = +100% pre-cap
        internal const float MaxBoost   = 10f;   // ceiling: a max-fed store runs ×11 wash/earn

        private static int _day = int.MinValue;
        private static Dictionary<string, float> _map; // store key (RugLaunder.Key) -> boost, rebuilt once/in-game day

        /// <summary>Drop the cache (called on city unload).</summary>
        internal static void Reset() { _day = int.MinValue; _map = null; }

        /// <summary>Boost in [0, MaxBoost] a factory network confers on this store; callers multiply output by (1 + this).</summary>
        internal static float For(BuildingRegistration store)
        {
            if (store == null) return 0f;
            int today = CurrentDay();
            if (_map == null || _day != today) { _day = today; _map = Build(); }
            return (_map != null && _map.TryGetValue(RugLaunder.Key(store), out float b)) ? b : 0f;
        }

        // Walk the logistics graph once per day. A warehouse's produced value is ONE pool no matter how many
        // plans run out of it (counting it per plan would let N plans hand out N× the real output), and only
        // MANNED plans move product — we mirror the engine's own delivery gates (GetPlannedDeliveries,
        // BA.cs:145841: no employee / MaxDestinations == 0 → nothing ships; only the first MaxDestinations
        // destinations deliver). Pool per warehouse, collect every served OWNED store's share across all its
        // plans, then split the pool by share and convert to a clamped boost.
        private static Dictionary<string, float> Build()
        {
            var map = new Dictionary<string, float>();
            try
            {
                var gi = SaveGameManager.Current;
                if (gi == null || gi.logisticsManagerPlans == null || gi.BuildingRegistrations == null) return map;

                // Snapshot the player's owned businesses once (warehouse + served stores are matched against these).
                var owned = new List<BuildingRegistration>();
                foreach (BuildingRegistration r in gi.BuildingRegistrations)
                    if (r != null && r.RentedByPlayer) owned.Add(r);

                var pooled = new Dictionary<string, float>();                        // warehouse key -> produced value (once)
                var shares = new Dictionary<string, List<(string store, float w)>>(); // warehouse key -> served-store weights

                foreach (var plan in gi.logisticsManagerPlans)
                {
                    if (plan == null || !plan.isFactory || plan.destinations == null) continue;
                    if (string.IsNullOrEmpty(plan.assignedEmployeeId)) continue; // unmanned plan ships nothing
                    int maxDest;
                    try { maxDest = plan.MaxDestinations; } catch { maxDest = 0; }
                    if (maxDest <= 0) continue;

                    // The feeding warehouse must be one you own; its produced value is the fuel for the boost.
                    BuildingRegistration warehouse = null;
                    foreach (BuildingRegistration r in owned)
                        if (r.Address == plan.targetAddress) { warehouse = r; break; }
                    if (warehouse == null) continue;

                    string whKey = RugLaunder.Key(warehouse);
                    if (!pooled.ContainsKey(whKey))
                    {
                        pooled[whKey] = ProducedValue(warehouse); // once per warehouse, not per plan
                        shares[whKey] = new List<(string, float)>();
                    }
                    if (pooled[whKey] <= 0f) continue; // an idle factory confers nothing

                    int slot = 0;
                    foreach (var dest in plan.destinations)
                    {
                        if (slot++ >= maxDest) break; // the engine only delivers the first MaxDestinations
                        if (dest == null) continue;
                        BuildingRegistration store = null;
                        foreach (BuildingRegistration r in owned)
                            if (r.Address == dest.deliveryTargetAddress) { store = r; break; }
                        if (store == null) continue; // only your OWN stores carry RUGS roles to boost

                        float weight = (dest.stockTargets != null && dest.stockTargets.Count > 0) ? dest.stockTargets.Count : 1f;
                        shares[whKey].Add((RugLaunder.Key(store), weight));
                    }
                }

                // Split each warehouse's single pool across every store it supplies, by weight share.
                foreach (KeyValuePair<string, float> pool in pooled)
                {
                    List<(string store, float w)> list = shares[pool.Key];
                    if (pool.Value <= 0f || list.Count == 0) continue;
                    float totalWeight = 0f;
                    foreach ((string _, float w) in list) totalWeight += w;
                    if (totalWeight <= 0f) continue;
                    foreach ((string store, float w) in list)
                    {
                        map.TryGetValue(store, out float acc);
                        map[store] = acc + pool.Value * (w / totalWeight);
                    }
                }

                // Accumulated value -> clamped boost.
                var keys = new List<string>(map.Keys);
                foreach (string k in keys)
                    map[k] = Mathf.Clamp(map[k] / BoostScale, 0f, MaxBoost);
            }
            catch { }
            return map;
        }

        // The factory's believable produced value: its warehouse's trailing daily order-history revenue. BA rolls
        // the factory's export ledger into the warehouse's books each night (BA.cs:195126), so this is STABLE and
        // available at the day-rollover mint moment — unlike the nightly-cleared factoryExports list. Reuses
        // RugLaunder's organic-revenue read (a warehouse is never laundered through, so its "laundered-in-window" is 0).
        private static float ProducedValue(BuildingRegistration wh)
            => wh == null ? 0f : Mathf.Max(0f, RugLaunder.BelievableRevenue(wh));

        private static int CurrentDay()
        {
            try { return SaveGameManager.Current != null ? SaveGameManager.Current.Day : -1; }
            catch { return -1; }
        }
    }
}
