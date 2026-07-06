using System.Collections.Generic;
using UnityEngine;

namespace Rugs
{
    /// <summary>
    /// The rug market, Drug-Wars style. There is NO single global price: each DISTRICT rolls its OWN price for
    /// each rug, uniformly within that rug's wide [min,max] BAND, re-rolled every in-game day. So the same rug
    /// can be cheap on one side of town and several times dearer on the other, today — and the profit is purely
    /// SPATIAL: buy low in one district, haul it, sell high in another. Buying and selling happen at the SAME
    /// district price (no dealer margin — see <see cref="RugTrading"/>), exactly as Drug Wars works, so you can't
    /// flip in place — a same-spot buy+sell is a wash.
    ///
    /// Band width follows the dopewars shape: cheaper (common) rugs swing WIDER (~4×), pricey rares swing
    /// narrower (~2.5×) but in much bigger absolute dollars. Every district rolls the same way — the home
    /// district is NOT special-priced; its only privilege is that the anchor dealer there BUYS every rug, so the
    /// player always has a guaranteed place to offload a haul.
    ///
    /// Per-save state in modData: a flat list of (district,key,price) cells, lazily rolled on first access for a
    /// district and re-rolled daily. District price EVENTS (spikes/crashes) layer on top via
    /// <see cref="RugEvents.PriceMultiplier"/>.
    /// </summary>
    internal static class RugMarket
    {
        // Band factors (× base price). Commons swing wide; rares narrower-but-bigger. Every district (incl. home)
        // rolls the same way — the home district's only privilege is the anchor dealer buying all rug types.
        private const float CommonLow = 0.50f, CommonHigh = 2.20f; // ~4.4× ratio (cheap rugs roam the most)
        private const float RareLow   = 0.65f, RareHigh   = 1.60f; // ~2.5× ratio (big absolute dollars)

        [System.Serializable] public class Cell  { public string district; public string key; public float price; }
        [System.Serializable] public class State { public List<Cell> cells = new List<Cell>(); public int lastDay = -1; }

        private const string MarketKey = "rugs:market";
        private static State _state;

        /// <summary>Drop the in-memory cache so the next access re-reads the current save.</summary>
        internal static void Reset() { _state = null; }

        internal static void EnsureLoaded()
        {
            if (_state != null) return;
            if (!RugBooks.SaveReady()) return; // wait for the save before binding state
            string raw = RugBooks.GetRaw(MarketKey);
            if (!string.IsNullOrEmpty(raw)) { try { _state = JsonUtility.FromJson<State>(raw); } catch { _state = null; } }
            if (_state == null) _state = new State();
            if (_state.cells == null) _state.cells = new List<Cell>();
        }

        private static Cell Find(string district, string key)
        {
            if (_state == null) return null;
            foreach (Cell c in _state.cells) if (c.district == district && c.key == key) return c;
            return null;
        }

        /// <summary>This DISTRICT's current price for the rug (lazily rolled within its band on first access).</summary>
        internal static float Price(RugDef r, string district)
        {
            EnsureLoaded();
            if (r == null) return 0f;
            if (_state == null) return r.BasePrice; // save not ready — quote the anchor price
            if (district == null) district = "";
            Cell c = Find(district, r.Key);
            if (c == null)
            {
                c = new Cell { district = district, key = r.Key, price = Roll(r, district) };
                _state.cells.Add(c);
                Save();
            }
            return c.price;
        }

        /// <summary>Back-compat neutral quote (the rug's base price) for non-district contexts (gift cash-out, offer base).</summary>
        internal static float Price(RugDef r) => r != null ? r.BasePrice : 0f;

        /// <summary>Re-roll every district's prices once per new in-game day (the daily market shuffle).</summary>
        internal static void SyncDay(int day)
        {
            EnsureLoaded();
            if (_state == null) return;
            if (_state.lastDay < 0) { _state.lastDay = day; Save(); return; } // first sync: stamp, don't re-roll fresh rolls
            if (day <= _state.lastDay) return;
            _state.lastDay = day;
            foreach (Cell c in _state.cells) c.price = Roll(RugCatalog.ByKey(c.key), c.district);
            Save();
        }

        // Roll a fresh uniform price within the rug's band (commons swing wider than rares). Same for every
        // district — the home district gets no special pricing.
        private static float Roll(RugDef r, string district)
        {
            if (r == null) return 0f;
            float lo = r.Common ? CommonLow : RareLow;
            float hi = r.Common ? CommonHigh : RareHigh;
            return Mathf.Round(r.BasePrice * Random.Range(lo, hi));
        }

        /// <summary>Cheapest district to BUY this rug right now, at the EFFECTIVE street price — band × any
        /// active event swing, exactly what a dealer there quotes (RugTrading.Quote). Keeping the event in the
        /// math keeps the wire's map consistent with its own "word on the street" (a flooded district must
        /// SHOW as the cheap one). False if no districts.</summary>
        internal static bool BestBuy(RugDef r, out string district, out float price)
        {
            district = ""; price = 0f;
            List<string> ds = RugDealers.Districts;
            if (r == null || ds == null || ds.Count == 0) return false;
            float best = float.MaxValue;
            foreach (string d in ds) { float p = StreetPrice(r, d); if (p < best) { best = p; district = d; } }
            price = best;
            return best < float.MaxValue;
        }

        /// <summary>Dearest district to SELL this rug right now, at the EFFECTIVE street price (see BestBuy).</summary>
        internal static bool BestSell(RugDef r, out string district, out float price)
        {
            district = ""; price = 0f;
            List<string> ds = RugDealers.Districts;
            if (r == null || ds == null || ds.Count == 0) return false;
            float best = -1f;
            foreach (string d in ds) { float p = StreetPrice(r, d); if (p > best) { best = p; district = d; } }
            price = best;
            return best >= 0f;
        }

        /// <summary>THE canonical street quote — what a dealer in this district actually charges/pays: rolled
        /// band × active event multiplier, rounded (identical to RugTrading.Quote). EVERY player-facing price
        /// in a known district must come from here (the wire's map, event offers, on-the-spot cash-outs), so no
        /// two systems can ever disagree about what a corner is worth.</summary>
        internal static float StreetPrice(RugDef r, string district)
            => Mathf.Round(Price(r, district) * RugEvents.PriceMultiplier(r, district));

        private static void Save()
        {
            if (_state == null) return;
            try { RugBooks.SetRaw(MarketKey, JsonUtility.ToJson(_state)); } catch { }
        }
    }
}
