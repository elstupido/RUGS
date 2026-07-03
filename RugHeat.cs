using System;
using System.Collections.Generic;
using System.Globalization;
using BAModAPI;   // IModLogger
using Entities;   // Contact
using UnityEngine;

namespace Rugs
{
    /// <summary>
    /// Ground-floor "police" via the IRS. Big Ambitions has no enforcement system, so this is our own:
    /// a HEAT meter (0–100) driven by recent WEIGHTED DEALING you haven't cleaned up yet (heavier product
    /// heats you faster). It climbs as you deal, and comes back DOWN two ways: LAUNDER your dirty cash
    /// (washing it out sheds the matching exposure) or LAY LOW (the load decays a chunk each day). The
    /// higher heat, the higher the daily chance of an AUDIT that fines your real (wallet) money. So: wash
    /// what you make, and don't sit hot.
    ///
    /// Audit rolls are deterministic per in-game day and gated on a persisted "last processed day",
    /// so reloading can't re-roll a bad audit. A persisted per-save salt keeps the roll unpredictable
    /// across saves (so no universal "safe day" table). Multi-day gaps are caught up day by day.
    /// </summary>
    internal static class RugHeat
    {
        // ---- tuning knobs ----
        private const float HeatLoadForMax      = 40000f; // un-laundered weighted-dealing load that drives heat toward 100
        private const float HeatLoadDecayPerDay = 0.72f;  // recent-dealing load cools ~28%/day when you lay low
        private const float RisePerDay          = 14f;    // heat climbs toward its target by up to this/day
        private const float CoolPerDay          = 25f;    // ...and falls toward target fast once you launder / lay low
        private const float AuditMinHeat        = 25f;    // below this, no audits (small-timers are safe)
        private const float MaxDailyAuditChance = 0.20f;  // daily audit probability at heat 100
        private const float MaxAuditRate        = 0.20f;  // penalty = wallet × (heat/100) × this — IRS hits REAL money
        private const int   MaxCatchUpDays      = 60;     // bound the per-day loop on big time skips

        private const string AuditMsgKey  = "ba:messagetype_rugaudit"; // locale value is "{body}"
        private const string IrsContactId = "internal_revenue_service";
        private const string SaltKey      = "rugs:auditSalt";

        /// <summary>Advance heat per elapsed in-game day and maybe trigger audits.</summary>
        internal static void OnDayChanged(int day, IModLogger log)
        {
            RugBooks.Load();
            if (!RugBooks.SaveReady()) return;

            int last = RugBooks.HeatLastDay;
            if (last < 0) { RugBooks.SetHeat(RugBooks.Heat, RugBooks.HeatLoad, day); return; } // first run: stamp, no retroactive audits
            if (day <= last) return;

            int salt  = AuditSalt();
            int steps = Mathf.Min(day - last, MaxCatchUpDays);
            float heat = RugBooks.Heat;
            float load = RugBooks.HeatLoad;
            bool audited = false;

            for (int i = 1; i <= steps; i++)
            {
                int d = last + i;
                load *= HeatLoadDecayPerDay;            // recent dealing cools off over time
                if (load < 1f) load = 0f;
                float target = Mathf.Clamp(load / HeatLoadForMax * 100f, 0f, 100f);
                heat = target > heat ? Mathf.Min(target, heat + RisePerDay)
                                     : Mathf.Max(target, heat - CoolPerDay);

                if (heat >= AuditMinHeat)
                {
                    float chance = heat / 100f * MaxDailyAuditChance;
                    if (DeterministicRoll(d, salt) < chance) { Audit(heat, log); heat = 0f; load = 0f; audited = true; }
                }
            }

            RugBooks.SetHeat(heat, load, day);
            if (RugsConfig.Dev && !audited) log?.Info($"RUGS! heat {heat:0} (load {load:0}, dirty ${RugBooks.Dirty:N0}, day {day}).");
        }

        // One-time per-save salt so the deterministic roll is reload-stable yet unpredictable across saves.
        private static int AuditSalt()
        {
            string raw = RugBooks.GetRaw(SaltKey);
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) && s != 0) return s;
            int salt = unchecked((int)(UnityEngine.Random.value * 2147483646f) + 1); // 1..~int.max, never 0
            RugBooks.SetRaw(SaltKey, salt.ToString(CultureInfo.InvariantCulture));
            return salt;
        }

        // Same (day, salt) always rolls the same value → reloading can't dodge a pending audit.
        private static float DeterministicRoll(int day, int salt)
        {
            UnityEngine.Random.State prev = UnityEngine.Random.state;
            UnityEngine.Random.InitState(unchecked(day * 73856093) ^ salt);
            float v = UnityEngine.Random.value;
            UnityEngine.Random.state = prev; // don't disturb the game's global RNG
            return v;
        }

        private static void Audit(float heat, IModLogger log)
        {
            // The IRS only reaches REAL money (the wallet). Dirty cash is hidden from them — that's the
            // point. So a player hiding everything in the dirty stash dodges the fine (but it's then
            // street-risky and unspendable until laundered, which re-exposes it). Risk triangle.
            float money;
            try { money = SaveGameManager.Current.Money; } catch { money = 0f; }
            float fine = Mathf.Round(Mathf.Max(0f, money) * Mathf.Clamp01(heat / 100f) * MaxAuditRate);

            try
            {
                if (fine > 0f)
                    GameManager.ChangeMoneySafe(-fine,
                        new TransactionInfo("rugs:transaction_fine", "rugs:transactioncategory_fine"),
                        null, null, force: true, showNotification: true);
            }
            catch (Exception e) { log?.Error("RUGS! audit fine failed."); log?.Error(e); }

            string body =
                "AUDIT NOTICE.\n\nYour finances have been flagged for review.\n" +
                "Assessed penalty: $" + fine.ToString("N0") + " on unexplained wealth.\nKeep your affairs in order.";
            try { SendNotice(body); } catch (Exception e) { log?.Error(e); }
            log?.Info($"RUGS! IRS audit — heat {heat:0}, fine ${fine:N0} (wallet).");
        }

        private static void SendNotice(string body)
        {
            var gi = SaveGameManager.Current;
            if (gi == null || gi.Contacts == null) return;
            Contact c = gi.Contacts.Find(x => x != null && x.id == IrsContactId)
                     ?? gi.Contacts.Find(x => x != null && x.id == "The Plug");
            if (c == null) return; // money-change notification still fired; just no thread entry
            GameManager.SendTextMessage(c, AuditMsgKey, new Dictionary<string, string> { { "body", body } });
        }

        /// <summary>Human-readable heat band for the Plug wire / deal panel.</summary>
        internal static string Band(float heat)
            => heat >= 80f ? "CRITICAL" : heat >= 55f ? "High" : heat >= 25f ? "Medium" : heat > 0f ? "Low" : "None";
    }
}
