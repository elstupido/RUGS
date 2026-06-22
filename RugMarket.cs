using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Rugs
{
    /// <summary>
    /// The private rug market: one global, fluctuating price per rug that drifts each
    /// in-game day (a mean-reverting random walk around each rug's base price). Dealers
    /// quote prices as market × their own constant. Persisted to a JSON file so the
    /// market state survives across sessions.
    /// </summary>
    internal static class RugMarket
    {
        [System.Serializable] public class Entry { public string key; public float price; }
        [System.Serializable] public class State { public List<Entry> prices = new List<Entry>(); public int lastDay = -1; }

        private static State _state;
        private static string FilePath => Path.Combine(Application.persistentDataPath, "RUGS_market.json");

        internal static void EnsureLoaded()
        {
            if (_state != null) return;
            try { if (File.Exists(FilePath)) _state = JsonUtility.FromJson<State>(File.ReadAllText(FilePath)); }
            catch { _state = null; }
            if (_state == null) _state = new State();
            foreach (RugDef r in RugCatalog.All)
                if (Find(r.Key) == null) _state.prices.Add(new Entry { key = r.Key, price = r.BasePrice });
        }

        private static Entry Find(string key)
        {
            foreach (Entry e in _state.prices) if (e.key == key) return e;
            return null;
        }

        internal static float Price(RugDef r)
        {
            EnsureLoaded();
            Entry e = Find(r.Key);
            return e != null ? e.price : r.BasePrice;
        }

        /// <summary>Advance the market to the given day, drifting once per elapsed day.</summary>
        internal static void SyncDay(int day)
        {
            EnsureLoaded();
            if (_state.lastDay < 0) { _state.lastDay = day; Save(); return; }
            if (day <= _state.lastDay) return;
            int steps = Mathf.Min(day - _state.lastDay, 60);
            _state.lastDay = day;
            for (int i = 0; i < steps; i++) Fluctuate();
            Save();
        }

        private static void Fluctuate()
        {
            foreach (RugDef r in RugCatalog.All)
            {
                Entry e = Find(r.Key);
                if (e == null) continue;
                float drift = Random.Range(-0.08f, 0.08f);                      // ±8% daily noise
                float reversion = (r.BasePrice - e.price) / r.BasePrice * 0.05f; // gentle pull to base
                e.price = Mathf.Clamp(e.price * (1f + drift + reversion), r.BasePrice * 0.4f, r.BasePrice * 2.5f);
            }
        }

        private static void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(_state)); } catch { }
        }
    }
}
