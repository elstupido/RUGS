using System.Collections.Generic;
using BAModAPI;     // IModLogger
using Entities;     // OrderHistoryEntry
using UnityEngine;  // JsonUtility, Mathf

namespace Rugs
{
    /// <summary>
    /// T2 — dealer sidecars. The player bolts a dealer onto a business they ALREADY own; each in-game day we
    /// READ that business's clean booked revenue and MINT dirty cash on top (a multiple of it), never touching
    /// BA's money — Harmony-free. A sidecar'd business is an "earner" and is locked out of laundering (it's
    /// excluded from <see cref="RugLaunder.Fronts"/>); washing happens at your OTHER businesses. State rides in
    /// the save via modData ("rugs:dealers"), mirroring <see cref="RugMarket"/>'s JSON pattern.
    /// </summary>
    internal static class RugSidecars
    {
        // ---- tuning ----
        internal const float Factor     = 0.6f;  // dirty cash minted per $1 of the front's organic daily revenue
        internal const float Wage       = 0f;    // flat daily dealer wage netted from the take (Phase A: 0; tune later)
        internal const float HeatWeight = 1.0f;  // IRS heat per $ when the held dirty cash is COLLECTED into the stash

        private const string KState = "rugs:dealers";

        [System.Serializable] private class Sidecar
        {
            public string addr;
            public float  dirtyHeld;
            public int    lastCreditedDay = -1;
        }

        [System.Serializable] private class State { public List<Sidecar> all = new List<Sidecar>(); }

        private static State _state;

        /// <summary>Drop the in-memory cache so the next access re-reads the current save.</summary>
        internal static void Reset() { _state = null; }

        private static void EnsureLoaded()
        {
            if (_state != null) return;
            if (!RugBooks.SaveReady()) return;
            string raw = RugBooks.GetRaw(KState);
            if (!string.IsNullOrEmpty(raw))
            {
                try { _state = JsonUtility.FromJson<State>(raw); } catch { _state = null; }
            }
            if (_state == null) _state = new State();
            if (_state.all == null) _state.all = new List<Sidecar>();
        }

        private static void Save()
        {
            if (_state == null) return;
            try { RugBooks.SetRaw(KState, JsonUtility.ToJson(_state)); } catch { }
        }

        private static Sidecar Find(string addr)
        {
            EnsureLoaded();
            if (_state == null) return null;
            foreach (Sidecar s in _state.all) if (s.addr == addr) return s;
            return null;
        }

        // ---- public API ----

        /// <summary>True if the business at this address key runs a dealer sidecar (→ an earner, not a wash venue).</summary>
        internal static bool HasSidecar(string addrKey) => Find(addrKey) != null;

        /// <summary>Attach a dealer to a business the player owns (idempotent). Starts earning the NEXT day.</summary>
        internal static void Attach(BuildingRegistration reg)
        {
            if (reg == null) return;
            string addr = RugLaunder.Key(reg);
            if (Find(addr) != null || _state == null) return; // Find ensures the state is loaded
            _state.all.Add(new Sidecar { addr = addr, lastCreditedDay = CurrentDay() }); // don't back-credit old days
            Save();
        }

        /// <summary>Pull the dealer off a business; sweep any un-collected dirty cash into the stash first.</summary>
        internal static void Remove(BuildingRegistration reg)
        {
            if (reg == null) return;
            Sidecar s = Find(RugLaunder.Key(reg));
            if (s == null) return;
            if (s.dirtyHeld >= 1f) RugBooks.AddDirty(Mathf.Floor(s.dirtyHeld), reg.Neighborhood, HeatWeight);
            _state.all.Remove(s);
            Save();
        }

        /// <summary>Dirty cash a sidecar is currently holding (uncollected).</summary>
        internal static float Held(BuildingRegistration reg)
        {
            if (reg == null) return 0f;
            Sidecar s = Find(RugLaunder.Key(reg));
            return s != null ? s.dirtyHeld : 0f;
        }

        /// <summary>
        /// THE BOOKS' [COLLECT ALL] button: sweep EVERY rider's held take into the stash in one click — the
        /// bulk twin of <see cref="Collect"/>, same per-district AddDirty math (and therefore identical heat)
        /// as collecting each by hand. Returns the status line.
        /// </summary>
        internal static string CollectAll()
        {
            EnsureLoaded();
            if (_state == null || _state.all.Count == 0) return "No riders on the payroll.";

            var owned = new Dictionary<string, BuildingRegistration>();
            try
            {
                var gi = SaveGameManager.Current;
                if (gi?.BuildingRegistrations != null)
                    foreach (BuildingRegistration reg in gi.BuildingRegistrations)
                        if (reg != null && reg.RentedByPlayer) owned[RugLaunder.Key(reg)] = reg;
            }
            catch { }

            float total = 0f;
            int riders = 0;
            foreach (Sidecar s in _state.all)
            {
                if (s.dirtyHeld < 1f) continue;
                float amt = Mathf.Floor(s.dirtyHeld);
                owned.TryGetValue(s.addr, out BuildingRegistration reg);
                RugBooks.AddDirty(amt, reg != null ? reg.Neighborhood : null, HeatWeight);
                s.dirtyHeld -= amt;
                total += amt;
                riders++;
            }
            if (total < 1f) return "Nothing to collect yet — the riders are still working.";
            Save();
            return $"Collected ${total:N0} off {riders} rider{(riders == 1 ? "" : "s")} — it's in the stash now. Wash it.";
        }

        /// <summary>Collect a sidecar's held dirty cash into the global dirty stash (this is what adds heat).</summary>
        internal static string Collect(BuildingRegistration reg)
        {
            if (reg == null) return "";
            Sidecar s = Find(RugLaunder.Key(reg));
            if (s == null || s.dirtyHeld < 1f) return "Nothing to collect yet.";
            float amt = Mathf.Floor(s.dirtyHeld);
            RugBooks.AddDirty(amt, reg.Neighborhood, HeatWeight);
            s.dirtyHeld -= amt;
            Save();
            return $"Collected ${amt:N0} off your dealer — it's in the stash now. Wash it somewhere else.";
        }

        /// <summary>
        /// Daily: per sidecar, mint dirty cash from the host business's just-closed clean revenue. We credit any
        /// orderHistory entry newer than what we've already booked (BA writes one entry per day, dayNumber=Day-1),
        /// so this is double-count-proof and survives a missed tick. BA's money is never touched — we only read.
        /// </summary>
        internal static void OnDayChanged(int day, IModLogger log = null)
        {
            EnsureLoaded();
            if (_state == null || _state.all.Count == 0) return;

            var gi = SaveGameManager.Current;
            if (gi == null || gi.BuildingRegistrations == null) return;

            // Index the player's currently-owned businesses by address key.
            var owned = new Dictionary<string, BuildingRegistration>();
            foreach (BuildingRegistration reg in gi.BuildingRegistrations)
                if (reg != null && reg.RentedByPlayer)
                    owned[RugLaunder.Key(reg)] = reg;

            bool changed = false;
            for (int i = _state.all.Count - 1; i >= 0; i--)
            {
                Sidecar s = _state.all[i];
                if (!owned.TryGetValue(s.addr, out BuildingRegistration reg) || reg == null)
                {
                    // Business gone (sold/unrented) — the dealer walks, but his held take reaches the stash
                    // first (mirrors Remove(); silently losing cash to a transient address hiccup is worse).
                    if (s.dirtyHeld >= 1f) RugBooks.AddDirty(Mathf.Floor(s.dirtyHeld), null, HeatWeight);
                    _state.all.RemoveAt(i); changed = true; continue;
                }
                if (RugLaunder.IsWarehouse(reg))
                {
                    // Warehouses are infrastructure (the factory force-multiplier), never earners. Retire any
                    // stale sidecar from a pre-exclusion save, sweeping its held take into the stash first.
                    if (s.dirtyHeld >= 1f) RugBooks.AddDirty(Mathf.Floor(s.dirtyHeld), reg.Neighborhood, HeatWeight);
                    _state.all.RemoveAt(i); changed = true; continue;
                }
                if (reg.orderHistory == null) continue;

                int threshold = s.lastCreditedDay, maxDay = s.lastCreditedDay;
                foreach (OrderHistoryEntry e in reg.orderHistory)
                {
                    if (e == null || e.dayNumber <= threshold) continue;
                    // A factory you own that SUPPLIES this business multiplies its dirty take (RugFactoryBoost).
                    float take = Mathf.Max(0f, e.totalRevenue * Factor * (1f + RugFactoryBoost.For(reg)) - Wage);
                    if (take > 0f) { s.dirtyHeld += take; changed = true; }
                    if (e.dayNumber > maxDay) maxDay = e.dayNumber;
                }
                if (maxDay != s.lastCreditedDay) { s.lastCreditedDay = maxDay; changed = true; }
            }
            if (changed) Save();
        }

        private static int CurrentDay()
        {
            try { return SaveGameManager.Current != null ? SaveGameManager.Current.Day : -1; }
            catch { return -1; }
        }
    }
}
