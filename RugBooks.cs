using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Rugs
{
    /// <summary>
    /// "The books" — the mod's dirty-money ledger. Money earned selling rugs is DIRTY
    /// (undeclared) by default: the game's tax engine never sees it. We track how much
    /// dirty cash the player is sitting on, per district, plus lifetime stats — and later
    /// let them LAUNDER it (move dirty -> declared, which makes it taxable = clean).
    ///
    /// State lives in <c>GameInstance.modData</c> (a public Dictionary&lt;string,string&gt;
    /// the game serializes into the save in BOTH formats and never touches itself), so the
    /// tally rides inside the save atomically — no side-car file for money state. All keys
    /// are namespaced "rugs:" to avoid colliding with other mods.
    ///
    /// Dirty cash is a SEPARATE hidden stash (a real balance in modData), NOT a slice of the
    /// wallet: selling credits it (never the wallet), buying spends it first, and laundering
    /// (<see cref="RugLaunder"/>, through a business you own) is the only bridge into the clean,
    /// spendable wallet. The street can rob the dirty stash (<see cref="LoseDirty"/>); the IRS can
    /// only ever touch the real wallet. That's the risk triangle.
    /// </summary>
    internal static class RugBooks
    {
        private const string KDirty       = "rugs:dirtyMoney";       // un-laundered cash on hand
        private const string KDeclared    = "rugs:declaredLifetime"; // cumulative laundered/declared
        private const string KEarned      = "rugs:earnedLifetime";   // gross rug income ever (stat)
        private const string KByDistrict  = "rugs:dirtyByDistrict";  // "key=amt;key=amt" per neighborhood
        private const string KHeat        = "rugs:heat";             // 0..100 IRS suspicion meter
        private const string KHeatLoad    = "rugs:heatLoad";         // decaying weighted-dealing exposure (drives heat)
        private const string KHeatLastDay = "rugs:heatLastDay";      // last day heat/audit was processed
        private const string KEngaged     = "rugs:engaged";          // has the player ever traded a rug?

        private static bool _loaded;
        private static bool _engaged;
        private static float _dirty, _declared, _earned, _heat, _heatLoad;
        private static int _heatLastDay = -1;
        private static Dictionary<string, float> _byDistrict = new Dictionary<string, float>();

        internal static float Dirty            => _dirty;
        internal static float DeclaredLifetime => _declared;
        internal static float EarnedLifetime   => _earned;
        internal static float Heat             => _heat;
        internal static float HeatLoad         => _heatLoad;
        internal static int   HeatLastDay      => _heatLastDay;

        /// <summary>True once the player has made their first rug deal — the mod is dormant until then.</summary>
        internal static bool Engaged { get { Load(); return _engaged; } }

        /// <summary>Latch engagement on the first buy/sell. From here the mod's systems switch on.</summary>
        internal static void MarkEngaged()
        {
            Load();
            if (!_loaded || _engaged) return;
            _engaged = true;
            Save();
        }

        internal static float DirtyInDistrict(string key)
            => (!string.IsNullOrEmpty(key) && _byDistrict.TryGetValue(key, out float v)) ? v : 0f;

        /// <summary>Drop the in-memory cache so the next access re-reads the current save.</summary>
        internal static void Reset()
        {
            _loaded = false;
            _engaged = false;
            _dirty = _declared = _earned = _heat = _heatLoad = 0f;
            _heatLastDay = -1;
            _byDistrict = new Dictionary<string, float>();
        }

        internal static void Load()
        {
            if (_loaded) return;
            Dictionary<string, string> md = ModData();
            if (md == null) return; // save not ready yet — try again next tick
            _dirty      = GetF(md, KDirty);
            _declared   = GetF(md, KDeclared);
            _earned     = GetF(md, KEarned);
            _heat       = GetF(md, KHeat);
            _heatLoad   = GetF(md, KHeatLoad);
            _heatLastDay = (md.TryGetValue(KHeatLastDay, out string hd) && int.TryParse(hd, NumberStyles.Integer, CultureInfo.InvariantCulture, out int d)) ? d : -1;
            _engaged = md.TryGetValue(KEngaged, out string en) && en == "1";
            _byDistrict = DecodeDistricts(md.TryGetValue(KByDistrict, out string s) ? s : null);
            _loaded = true;
        }

        /// <summary>Persist the heat meter, its decaying load, and the last day processed (set by RugHeat).</summary>
        internal static void SetHeat(float heat, float heatLoad, int lastDay)
        {
            Load();
            if (!_loaded) return;
            _heat = Mathf.Clamp(heat, 0f, 100f);
            _heatLoad = Mathf.Max(0f, heatLoad);
            _heatLastDay = lastDay;
            Save();
        }

        /// <summary>
        /// Spend from the dirty-cash stash (rug buys pull from here before dipping into the wallet).
        /// Returns how much of <paramref name="amount"/> the stash actually covered.
        /// </summary>
        internal static float SpendDirty(float amount)
        {
            Load();
            if (!_loaded || amount <= 0f) return 0f;
            float take = Mathf.Min(amount, _dirty);
            if (take <= 0f) return 0f;
            _dirty -= take;
            DrainDistricts(take);
            Save();
            return take;
        }

        /// <summary>Street loss: dirty cash stolen outright (shakedown). Gone — not laundered, not declared.</summary>
        internal static void LoseDirty(float amount)
        {
            Load();
            if (!_loaded) return;
            amount = Mathf.Min(Mathf.Max(0f, amount), _dirty);
            if (amount <= 0f) return;
            _dirty -= amount;
            DrainDistricts(amount);
            Save();
        }

        /// <summary>Record dirty income from a rug sale (amount, district, per-rug heat weight).</summary>
        internal static void AddDirty(float amount, string district, float heatWeight)
        {
            Load();
            if (!_loaded || amount <= 0f) return;
            _dirty    += amount;
            _earned   += amount;
            _heatLoad += amount * Mathf.Max(0f, heatWeight); // weighted dealing drives IRS heat
            if (!string.IsNullOrEmpty(district))
            {
                _byDistrict.TryGetValue(district, out float v);
                _byDistrict[district] = v + amount;
            }
            Save();
        }

        /// <summary>
        /// Move dirty cash out of the stash to be laundered through a business's books. Tracked as
        /// declared (a lifetime stat), and the per-district buckets drain — but this does NOT credit the
        /// wallet: the clean money is booked by Big Ambitions' own nightly bookkeeping when the
        /// fabricated sale clears (see <see cref="RugLaunder"/>). Returns the amount moved.
        /// </summary>
        internal static float SendToWash(float amount)
        {
            Load();
            if (!_loaded) return 0f;
            amount = Mathf.Clamp(amount, 0f, _dirty);
            if (amount <= 0f) return 0f;
            float before = _dirty;
            _dirty    -= amount;
            _declared += amount;
            CleanLoad(amount, before); // laundering cleans your trail — it cools heat
            DrainDistricts(amount);
            Save();
            return amount;
        }

        /// <summary>
        /// Laundering cleans your trail: when dirty cash is washed out of the stash, shed heat-load in
        /// proportion to the share of the pile that just left. Wash it all and your recent-dealing exposure
        /// goes with it; wash half and you cool by half. (Daily decay in RugHeat keeps cooling you on top.)
        /// </summary>
        private static void CleanLoad(float removed, float dirtyBefore)
        {
            if (dirtyBefore <= 0f || removed <= 0f) return;
            _heatLoad *= Mathf.Clamp01((dirtyBefore - removed) / dirtyBefore);
        }

        /// <summary>
        /// Instant "dealer" launder: pull <paramref name="gross"/> from the dirty stash and drop
        /// <paramref name="net"/> (gross minus the dealer's vig) straight into the clean wallet. Unlike the
        /// books-routed wash this is immediate and needs no business — the vig is the price. Returns net delivered.
        /// </summary>
        internal static float LaunderInstant(float gross, float net)
        {
            Load();
            if (!_loaded) return 0f;
            gross = Mathf.Clamp(gross, 0f, _dirty);
            if (gross <= 0f) return 0f;
            net = Mathf.Clamp(net, 0f, gross);
            float before = _dirty;
            _dirty    -= gross;
            _declared += net;
            CleanLoad(gross, before); // washing cools heat too
            DrainDistricts(gross);
            try
            {
                GameManager.ChangeMoneySafe(net,
                    new TransactionInfo("rugs:transaction_launder", "rugs:transactioncategory_launder"),
                    null, null, force: true, showNotification: false);
            }
            catch { }
            Save();
            return net;
        }

        // ---- persistence ----

        private static void Save()
        {
            Dictionary<string, string> md = ModData();
            if (md == null) return;
            md[KDirty]       = _dirty.ToString("R", CultureInfo.InvariantCulture);
            md[KDeclared]    = _declared.ToString("R", CultureInfo.InvariantCulture);
            md[KEarned]      = _earned.ToString("R", CultureInfo.InvariantCulture);
            md[KHeat]        = _heat.ToString("R", CultureInfo.InvariantCulture);
            md[KHeatLoad]    = _heatLoad.ToString("R", CultureInfo.InvariantCulture);
            md[KHeatLastDay] = _heatLastDay.ToString(CultureInfo.InvariantCulture);
            md[KEngaged]     = _engaged ? "1" : "0";
            md[KByDistrict]  = EncodeDistricts();
        }

        /// <summary>True once a save/GameInstance is live (so in-save state can be read/written).</summary>
        internal static bool SaveReady() { try { return SaveGameManager.Current != null; } catch { return false; } }

        /// <summary>Read a raw string from the in-save mod stash (null if absent / save not ready).</summary>
        internal static string GetRaw(string key)
        {
            Dictionary<string, string> md = ModData();
            return (md != null && md.TryGetValue(key, out string s)) ? s : null;
        }

        /// <summary>Write a raw string into the in-save mod stash (no-op if the save isn't ready).</summary>
        internal static void SetRaw(string key, string value)
        {
            Dictionary<string, string> md = ModData();
            if (md != null) md[key] = value;
        }

        private static Dictionary<string, string> ModData()
        {
            try
            {
                var gi = SaveGameManager.Current;
                if (gi == null) return null;
                if (gi.modData == null) gi.modData = new Dictionary<string, string>();
                return gi.modData;
            }
            catch { return null; }
        }

        private static float GetF(Dictionary<string, string> md, string key)
            => (md.TryGetValue(key, out string s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) ? v : 0f;

        // Proportionally reduce per-district buckets when dirty cash leaves the pool (launder/spend),
        // so the district map stays consistent with the total without tracking which district was spent.
        private static void DrainDistricts(float amount)
        {
            if (_byDistrict.Count == 0 || amount <= 0f) return;
            float total = 0f;
            foreach (float v in _byDistrict.Values) total += v;
            if (total <= 0f) { _byDistrict.Clear(); return; }
            float keep = Mathf.Max(0f, total - amount) / total;
            var keys = new List<string>(_byDistrict.Keys);
            foreach (string k in keys)
            {
                float nv = _byDistrict[k] * keep;
                if (nv < 0.5f) _byDistrict.Remove(k);
                else _byDistrict[k] = nv;
            }
        }

        private static string EncodeDistricts()
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, float> kv in _byDistrict)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key).Append('=').Append(kv.Value.ToString("R", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static Dictionary<string, float> DecodeDistricts(string s)
        {
            var d = new Dictionary<string, float>();
            if (string.IsNullOrEmpty(s)) return d;
            foreach (string part in s.Split(';'))
            {
                if (part.Length == 0) continue;
                int i = part.LastIndexOf('=');
                if (i <= 0) continue;
                string key = part.Substring(0, i);
                if (float.TryParse(part.Substring(i + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    d[key] = v;
            }
            return d;
        }
    }
}
