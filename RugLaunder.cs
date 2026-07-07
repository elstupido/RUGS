using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BAModAPI;        // IModLogger
using Entities;        // Order lives in the global namespace; OrderEntry / OrderHistoryEntry are here
using BigAmbitions.DayNightCycle;   // Timestamp
using UnityEngine;

namespace Rugs
{
    /// <summary>
    /// Money laundering — wash hidden DIRTY cash into clean, spendable money by running it through a
    /// legit business you OWN, using that business's REAL books. We fabricate a believable sale on the
    /// business and let Big Ambitions' own nightly bookkeeping (BusinessHelper.RunDaily ->
    /// ProcessDailyOrders) book it as that business's revenue: clean, on the financial statements, AND
    /// taxed. The tax IS the cost of laundering — there is no separate fee.
    ///
    /// PLAUSIBILITY-BOUND: a business can only absorb so much before the books look cooked. The safe
    /// daily capacity scales with the business's believable ORGANIC daily revenue (its reported revenue
    /// minus our own past washes, so laundering can't bootstrap its own capacity). It's a HARD cap: once a
    /// business is tapped out for the day you can't wash more through it — take the instant quick-wash (a
    /// flat vig) to move the rest. So your wash capacity scales with the legit empire you build.
    ///
    /// Timing: a wash leaves the dirty stash immediately, but the clean money is booked at the next
    /// in-game midnight — it "clears overnight."
    /// </summary>
    internal static class RugLaunder
    {
        // ---- tuning knobs ----
        internal const float PlausibleInflation = 0.35f; // safe daily wash = this × the business's organic daily revenue
        internal const int   WindowDays         = 7;     // trailing window used to gauge "believable" revenue
        internal const float MinWash            = 100f;  // don't bother below this

        // Quick wash (the convenience tier): instant, ANY amount, but skims a flat vig. Lives in the laundry
        // menu beside the cheaper per-business washing. The vig is the only cost — no cap, no extra heat (the
        // cleaned money lands in the wallet, which the IRS audit already exposes).
        internal const float DealerVig = 0.30f;   // the cut on an instant quick-wash

        private const string KWashLog = "rugs:washLog"; // per-business per-day laundered amounts: "addr|day|amt;..."

        /// <summary>Every business the player owns (rented + named) — the candidate pool for BOTH laundering
        /// and dealer sidecars. WAREHOUSES ARE EXCLUDED: a factory/warehouse is INFRASTRUCTURE in RUGS! — it
        /// multiplies the stores it supplies (<see cref="RugFactoryBoost"/>) and never itself washes (no walk-in
        /// sales to hide dirty cash in) or earns (minting off factory-export revenue was explicitly ruled out).</summary>
        internal static List<BuildingRegistration> AllOwned()
        {
            var list = new List<BuildingRegistration>();
            try
            {
                var gi = SaveGameManager.Current;
                if (gi == null || gi.BuildingRegistrations == null) return list;
                foreach (BuildingRegistration reg in gi.BuildingRegistrations)
                    if (reg != null && reg.RentedByPlayer && !string.IsNullOrWhiteSpace(reg.BusinessName) && !IsWarehouse(reg))
                        list.Add(reg);
            }
            catch { }
            return list;
        }

        /// <summary>True for warehouse buildings (the factory hosts) — infrastructure, never a wash venue or earner.</summary>
        internal static bool IsWarehouse(BuildingRegistration reg)
        {
            if (reg == null) return false;
            // Subclass check first: the game deserializes warehouse registrations as the Warehouse subclass,
            // so this can't fail open on a cold BuildingCached the way the type-string read below can.
            if (reg.GetType().Name == "Warehouse") return true;
            try
            {
                string t = reg.GetBuildingType();
                return !string.IsNullOrEmpty(t) && t.IndexOf("warehouse", System.StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        /// <summary>Businesses the player can launder through: owned, MINUS any running a dealer sidecar —
        /// a business either SELLS (earner) or LAUNDERS (washer), never both.</summary>
        internal static List<BuildingRegistration> Fronts()
        {
            List<BuildingRegistration> list = AllOwned();
            list.RemoveAll(reg => RugSidecars.HasSidecar(Key(reg)));
            return list;
        }

        internal static int FrontCount() => Fronts().Count;

        /// <summary>True when the player owns a venue and is holding dirty cash.</summary>
        internal static bool CanLaunder() => FrontCount() > 0 && RugBooks.Dirty > 0f;

        /// <summary>
        /// A business's believable ORGANIC daily revenue: its reported revenue over the trailing window,
        /// minus the cash WE ran through it in that window (so a wash can't inflate its own ceiling).
        /// </summary>
        internal static float BelievableRevenue(BuildingRegistration reg)
        {
            if (reg == null) return 0f;
            int today = CurrentDay();
            float revSum = 0f;
            try
            {
                if (reg.orderHistory != null)
                    foreach (OrderHistoryEntry e in reg.orderHistory)
                        if (e != null && e.dayNumber >= today - WindowDays && e.dayNumber < today)
                            revSum += e.totalRevenue;
            }
            catch { }
            float organic = (revSum - LaunderedInWindow(reg, today)) / WindowDays;
            return Mathf.Max(0f, organic);
        }

        /// <summary>Safe daily wash capacity for a business (plausibility-bound).</summary>
        internal static float PlausibleCap(BuildingRegistration reg) => BelievableRevenue(reg) * PlausibleInflation;

        /// <summary>Safe capacity still available at this business today. A factory you own that SUPPLIES this
        /// business raises its plausible ceiling (<see cref="RugFactoryBoost"/>) — vertical integration buys
        /// more wash room.</summary>
        internal static float CapRemaining(BuildingRegistration reg)
            => Mathf.Max(0f, PlausibleCap(reg) * (1f + RugFactoryBoost.For(reg)) - WashedToday(reg));

        /// <summary>Total safe wash capacity remaining across all the player's venues today.</summary>
        internal static float TotalSafeCapacity()
        {
            float t = 0f;
            foreach (BuildingRegistration reg in Fronts()) t += CapRemaining(reg);
            return t;
        }

        /// <summary>Wash <paramref name="amount"/> through whichever venue has the most safe room left.</summary>
        internal static string QuickWash(float amount)
        {
            List<BuildingRegistration> fronts = Fronts();
            if (fronts.Count == 0) return "You need a legit business to wash through — rent one and give it a name first.";
            BuildingRegistration best = null;
            float bestRoom = float.NegativeInfinity;
            foreach (BuildingRegistration reg in fronts)
            {
                float room = CapRemaining(reg);
                if (room > bestRoom) { bestRoom = room; best = reg; }
            }
            return Wash(best, amount);
        }

        /// <summary>
        /// Wash up to <paramref name="amount"/> of dirty cash through business <paramref name="reg"/> by
        /// fabricating a believable sale on its books; BA's nightly bookkeeping clears it clean + taxed.
        /// Over-plausible volume feeds IRS heat. Returns a player-facing line.
        /// </summary>
        internal static string Wash(BuildingRegistration reg, float amount)
        {
            if (reg == null || !reg.RentedByPlayer) return "That business isn't yours to run money through.";

            float dirty = RugBooks.Dirty;
            if (dirty < MinWash) return "Not enough dirty cash to bother washing.";

            // HARD cap: a business can only absorb its plausible daily room. Tap it out and you're done here
            // for the day — take the instant quick-wash (the vig) to move the rest. No over-washing.
            float capRoom = Mathf.Floor(CapRemaining(reg));
            if (capRoom < MinWash)
                return $"{reg.BusinessName} is tapped out for today — any more would cook the books. Use the quick wash (a {DealerVig * 100f:0}% cut) to move the rest now.";

            string product = PlausibleProduct(reg);
            if (product == null)
                return $"{reg.BusinessName} has no real sales to hide behind yet — it needs a track record before you can run money through it.";

            float gross = Mathf.Floor(Mathf.Min(Mathf.Min(amount, dirty), capRoom)); // clamped to the plausible room
            if (gross < MinWash) return "Not enough dirty cash to bother washing.";

            if (!Fabricate(reg, product, gross)) return "Couldn't run it through the books — try again.";
            RugBooks.SendToWash(gross);   // dirty leaves now (this also cools heat); clean is booked at midnight
            RecordWash(reg, gross);

            float left = capRoom - gross;
            string tail = left < MinWash
                ? $"  That taps {reg.BusinessName} out for today."
                : $"  Room for ${left:N0} more here today.";
            return $"Running ${gross:N0} through {reg.BusinessName} — clears clean overnight, on the books and taxed.{tail}";
        }

        // ---- auto-wash (the night crew) ----

        private const string KAutoWash    = "rugs:autoWash";     // "1" = the night crew is on
        private const string KAutoWashDay = "rugs:autoWashDay";  // last in-game day the crew ran (reload-safe)
        internal const int AutoWashMinBusinesses = 5;            // fleet size before automation unlocks

        /// <summary>Auto-wash unlocks once the player runs a real fleet — below this, washing stays hands-on.</summary>
        internal static bool AutoWashUnlocked() => AllOwned().Count >= AutoWashMinBusinesses;

        /// <summary>Player's persisted night-crew switch (see the Grand Ledger).</summary>
        internal static bool AutoWashEnabled => RugBooks.GetRaw(KAutoWash) == "1";

        internal static void SetAutoWash(bool on)
        {
            RugBooks.SetRaw(KAutoWash, on ? "1" : "0");
            // Stamp today so the crew's FIRST run is the coming night, not a retroactive same-day wash.
            if (on) RugBooks.SetRaw(KAutoWashDay, CurrentDay().ToString(CultureInfo.InvariantCulture));
        }

        private const string KAutoWashReport = "rugs:autoWashReport"; // "day|washed|fronts" — the GL's receipt

        /// <summary>
        /// The night crew: once per in-game day, wash the dirty stash through every front with safe room —
        /// biggest room first — until the stash runs dry or every front is tapped. Exactly the same rules as
        /// pressing the button yourself (plausibility caps, real products, clears overnight, taxed): this is
        /// automation for a 30-business fleet, not a better rate. Unlocked at 5+ businesses. Every run leaves
        /// a RECEIPT the GL shows (even a "nothing needed washing" run), so the automation is verifiable.
        /// </summary>
        internal static void OnDayChanged(int day, IModLogger log)
        {
            if (!AutoWashEnabled || !AutoWashUnlocked()) return;
            string raw = RugBooks.GetRaw(KAutoWashDay);
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int last) && day <= last) return;
            RugBooks.SetRaw(KAutoWashDay, day.ToString(CultureInfo.InvariantCulture));
            if (raw == null) return; // first sighting on an old save: stamp only, wash from tomorrow

            (float washed, int through) = RunNightCrew(log);
            WriteReport(day, washed, through);
            if (washed >= 1f)
            {
                log?.Info($"RUGS! night crew washed ${washed:N0} through {through} front(s).");
                RugPlug.Notify($"Night crew ran ${washed:N0} through {through} front{(through == 1 ? "" : "s")} — clean by morning.");
            }
        }

        // The sweep itself (shared by the nightly run and the Dev F11 force): biggest room first, same rules
        // as manual washing. Returns what moved.
        private static (float washed, int through) RunNightCrew(IModLogger log)
        {
            if (RugBooks.Dirty < MinWash) return (0f, 0);

            var rooms = new List<(BuildingRegistration reg, float room)>();
            foreach (BuildingRegistration reg in Fronts())
            {
                float room = Mathf.Floor(CapRemaining(reg));
                if (room >= MinWash) rooms.Add((reg, room));
            }
            rooms.Sort((a, b) => b.room.CompareTo(a.room));

            float washed = 0f;
            int through = 0;
            foreach ((BuildingRegistration reg, float room) in rooms)
            {
                if (RugBooks.Dirty < MinWash) break;
                string product = PlausibleProduct(reg);
                if (product == null) continue; // no sales history to hide behind — the crew skips it
                float gross = Mathf.Floor(Mathf.Min(RugBooks.Dirty, room));
                if (gross < MinWash) continue;
                if (!Fabricate(reg, product, gross)) continue;
                RugBooks.SendToWash(gross); // dirty leaves now (cools heat); BA books it clean at midnight
                RecordWash(reg, gross);
                washed += gross;
                through++;
            }
            return (washed, through);
        }

        private static void WriteReport(int day, float washed, int through)
            => RugBooks.SetRaw(KAutoWashReport,
                day.ToString(CultureInfo.InvariantCulture) + "|" +
                washed.ToString("R", CultureInfo.InvariantCulture) + "|" +
                through.ToString(CultureInfo.InvariantCulture));

        /// <summary>The GL's receipt line for the last night-crew run ("" if it has never run).</summary>
        internal static string NightCrewReport()
        {
            string raw = RugBooks.GetRaw(KAutoWashReport);
            if (string.IsNullOrEmpty(raw)) return "";
            string[] f = raw.Split('|');
            if (f.Length != 3) return "";
            if (!float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float washed)) return "";
            if (!int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int through)) return "";
            return washed >= 1f
                ? $"last run: washed ${washed:N0} through {through} front{(through == 1 ? "" : "s")} (day {f[0]})"
                : $"last run: nothing needed washing (day {f[0]})";
        }

        /// <summary>DEV (F11): run the night-crew sweep RIGHT NOW, ignoring the unlock/toggle/day gates, and
        /// write the same receipt the nightly run does — instant verification without sleeping to midnight.</summary>
        internal static void DevForceNightCrew(IModLogger log)
        {
            (float washed, int through) = RunNightCrew(log);
            WriteReport(CurrentDay(), washed, through);
            log?.Info($"RUGS! [dev] night crew forced: washed ${washed:N0} through {through} front(s); dirty now ${RugBooks.Dirty:N0}.");
        }

        // ---- quick wash (instant, vig, uncapped) ----

        /// <summary>
        /// The convenience wash: turn dirty cash into clean wallet money instantly, any amount, for a flat
        /// <see cref="DealerVig"/> cut. No business needed, no cap — the vig is the price. Returns a line.
        /// </summary>
        internal static string WashViaDealer(float amount)
        {
            float dirty = RugBooks.Dirty;
            if (dirty < MinWash) return "Not enough dirty cash to bother washing.";

            float gross = Mathf.Floor(Mathf.Min(amount, dirty)); // "Max" passes float.MaxValue -> all dirty
            if (gross < MinWash) gross = Mathf.Floor(dirty);
            if (gross < MinWash) return "Not enough dirty cash to bother washing.";

            float net = Mathf.Floor(gross * (1f - DealerVig));
            RugBooks.LaunderInstant(gross, net);
            return $"Washed ${net:N0} clean, instant — the guy took {DealerVig * 100f:0}% (${gross - net:N0}).";
        }

        // ---- order fabrication ----

        // Build a completed+paid single-line "sale" and queue it on the business's real order list, so
        // BA's ProcessDailyOrders books it as revenue. wholesalePrice=0 => the whole sum is profit (taxed),
        // which is exactly the cost of going legit. A non-null timestamp keeps it from being pruned.
        private static bool Fabricate(BuildingRegistration reg, string itemName, float price)
        {
            try
            {
                var gi = SaveGameManager.Current;
                if (gi == null || reg.unprocessedCompletedOrders == null) return false;
                var order = new Order
                {
                    completed = true,
                    timestamp = new Timestamp(gi.Day, gi.Hour, 0f)
                };
                order.entries.Add(new OrderEntry
                {
                    itemName         = itemName,
                    price            = price,
                    wholesalePrice   = 0f,
                    available        = true,
                    priceAccceptable = true,   // (BA's own spelling)
                    paid             = true,
                    processed        = false
                });
                reg.unprocessedCompletedOrders.Add(order);
                return true;
            }
            catch { return false; }
        }

        // A product this business actually sells (pulled from its own recent sales) so the fabricated
        // sale is plausible and resolves cleanly in the financial reports. Null if it has no track record.
        private static string PlausibleProduct(BuildingRegistration reg)
        {
            try
            {
                if (reg.orderHistory != null)
                    for (int i = reg.orderHistory.Count - 1; i >= 0; i--)
                    {
                        OrderHistoryEntry e = reg.orderHistory[i];
                        if (e?.itemSales == null) continue;
                        foreach (OrderHistoryEntry.ItemReport r in e.itemSales)
                            if (r != null && !string.IsNullOrEmpty(r.itemName)) return r.itemName;
                    }
            }
            catch { }
            return null;
        }

        // ---- per-business wash ledger (modData) ----

        /// <summary>Dirty cash already run through this business today (resets when the day rolls over).</summary>
        internal static float WashedToday(BuildingRegistration reg)
        {
            int today = CurrentDay();
            string key = Key(reg);
            float sum = 0f;
            foreach (var e in ParseLog())
                if (e.day == today && e.addr == key) sum += e.amt;
            return sum;
        }

        private static float LaunderedInWindow(BuildingRegistration reg, int today)
        {
            string key = Key(reg);
            float sum = 0f;
            foreach (var e in ParseLog())
                if (e.addr == key && e.day >= today - WindowDays && e.day < today) sum += e.amt;
            return sum;
        }

        private static void RecordWash(BuildingRegistration reg, float amount)
        {
            int today = CurrentDay();
            string key = Key(reg);
            List<(string addr, int day, float amt)> log = ParseLog();
            bool merged = false;
            for (int i = 0; i < log.Count; i++)
                if (log[i].day == today && log[i].addr == key) { log[i] = (key, today, log[i].amt + amount); merged = true; break; }
            if (!merged) log.Add((key, today, amount));
            log.RemoveAll(e => e.day < today - WindowDays - 1); // keep just enough history for the window
            RugBooks.SetRaw(KWashLog, Encode(log));
        }

        private static List<(string addr, int day, float amt)> ParseLog()
        {
            var list = new List<(string, int, float)>();
            string raw = RugBooks.GetRaw(KWashLog);
            if (string.IsNullOrEmpty(raw)) return list;
            foreach (string rec in raw.Split(';'))
            {
                if (rec.Length == 0) continue;
                string[] f = rec.Split('|');
                if (f.Length != 3) continue;
                if (int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int day)
                 && float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float amt))
                    list.Add((f[0], day, amt));
            }
            return list;
        }

        private static string Encode(List<(string addr, int day, float amt)> log)
        {
            var sb = new StringBuilder();
            foreach ((string addr, int day, float amt) in log)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(addr).Append('|')
                  .Append(day.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(amt.ToString("R", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        // Stable per-business key (street name + number); strip our delimiters out just in case.
        // Internal so RugSidecars keys per-business state the same way (and the Fronts() exclusion matches).
        internal static string Key(BuildingRegistration reg)
        {
            try
            {
                var a = reg.Address;
                if (a == null) return "?";
                string s = (a.streetName ?? "") + "#" + a.streetNumber.ToString(CultureInfo.InvariantCulture);
                return s.Replace('|', '/').Replace(';', ',');
            }
            catch { return "?"; }
        }

        private static int CurrentDay()
        {
            try { return SaveGameManager.Current != null ? SaveGameManager.Current.Day : -1; }
            catch { return -1; }
        }
    }
}
