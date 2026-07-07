using System;
using System.Collections.Generic;
using System.Text;
using BAModAPI;                      // IModLogger
using Entities;                      // Contact, TextMessage
using UI.Smartphone.Apps.Contacts;   // ContactCategoryName
using UnityEngine;

namespace Rugs
{
    /// <summary>
    /// "The Plug" — an info-broker phone contact. Re-skins the game's native contacts +
    /// messaging system (the same pipe the IRS uses): the player gets a daily text — the
    /// "wire" — with the going rates for every rug and a read on their own book. This is the
    /// one place the game's phone-styled dialog is exactly right: it's literally a phone call.
    ///
    /// Two gotchas the implementation works around:
    ///  * A Contact's <c>description</c>/<c>category</c> are [IgnoreDataMember] (not saved), so
    ///    we re-apply them every load and dedup by <c>id</c> ourselves rather than trusting
    ///    Contact.GetContact's (id+description) match, which would spawn duplicates after a reload.
    ///  * A preset-less contact shows its <c>id</c> as the name (localized only if id is a known
    ///    key), so we use the human string "The Plug" directly as the id.
    /// </summary>
    internal static class RugPlug
    {
        private const string Id      = "The Plug";              // serialized id AND display name
        private const string DescKey = "ba:rugs_plug_desc";     // localized one-liner under the name
        private const string MsgKey  = "ba:messagetype_rugwire"; // locale value is "{body}"

        private static Contact _contact;
        private static int _lastWireDay = -1;

        internal static void Reset() { _contact = null; _lastWireDay = -1; }

        /// <summary>
        /// Find-or-create the contact and (re)apply its transient fields. Idempotent and cheap once
        /// cached, so it's safe to call every tick until the save's contact list is ready. Reset()
        /// clears the cache on city load so the transient fields get re-applied for the new save.
        /// </summary>
        internal static void Ensure(IModLogger log)
        {
            if (_contact != null) return;
            try
            {
                var gi = SaveGameManager.Current;
                if (gi == null || gi.Contacts == null) return;

                Contact c = gi.Contacts.Find(x => x != null && x.id == Id);
                if (c == null)
                {
                    c = Contact.GetContact(Id, ContactCategoryName.Finance, DescKey, null, false, false);
                    log?.Info("RUGS! added contact 'The Plug'.");
                }
                if (c != null)
                {
                    c.description = DescKey;                 // transient — re-apply so the cell renders right
                    c.category = ContactCategoryName.Finance;
                    _contact = c;
                }
            }
            catch (Exception e) { log?.Error("RUGS! Plug ensure failed."); log?.Error(e); }
        }

        /// <summary>Push one wire when the in-game day rolls over (also fires once on load).</summary>
        internal static void MaybePushWire(int day, IModLogger log)
        {
            if (_contact == null || day == _lastWireDay) return;
            // Wait for the phone UI to exist. Sending before it does enqueues the text but skips
            // the badge + notification (Contact.SendMessage early-returns on a null UIs.Instance),
            // so the player never "receives" it. Retry next tick once the UI is up.
            if (InstanceBehavior<UI.UIs>.Instance == null) return;
            _lastWireDay = day;
            try
            {
                var data = new Dictionary<string, string> { { "body", BuildWire(day) } };
                GameManager.SendTextMessage(_contact, MsgKey, data);
                log?.Info("RUGS! Plug wire sent (day " + day + ").");
            }
            catch (Exception e) { log?.Error("RUGS! Plug wire failed."); log?.Error(e); }
        }

        /// <summary>Push a one-off event notice through the Plug (used by RugEvents for instant events).</summary>
        internal static void Notify(string body)
        {
            if (_contact == null || string.IsNullOrEmpty(body)) return;
            try { GameManager.SendTextMessage(_contact, "ba:messagetype_rugevent", new Dictionary<string, string> { { "body", body } }); }
            catch { /* UI not ready — skip; non-critical */ }
        }

        private static string BuildWire(int day)
        {
            var sb = new StringBuilder();
            sb.Append("The wire — day ").Append(day).Append(".\n\n");
            sb.Append("Going rates — same price to buy or sell, so it's all about WHERE:\n");
            foreach (RugDef r in RugCatalog.All)
            {
                bool lo = RugMarket.BestBuy(r, out string loD, out float loP);
                bool hi = RugMarket.BestSell(r, out string hiD, out float hiP);
                if (lo && hi && hiP > loP + 0.5f)
                    sb.Append("• ").Append(r.Display).Append(": cheap in ").Append(Hood(loD)).Append(" ($").Append(loP.ToString("N0"))
                      .Append("), hot in ").Append(Hood(hiD)).Append(" ($").Append(hiP.ToString("N0")).Append(")\n");
                else if (lo)
                    sb.Append("• ").Append(r.Display).Append(": ~$").Append(loP.ToString("N0")).Append(" around town\n");
            }
            List<string> moves = RugEvents.ActiveHeadlines();
            if (moves.Count > 0)
            {
                sb.Append("\nWord on the street:\n");
                foreach (string h in moves) sb.Append(h).Append('\n');
            }
            else sb.Append("\nStreets are quiet — no big moves today.\n");

            float dirty = RugBooks.Dirty;
            sb.Append("\nYour book: $").Append(dirty.ToString("N0"));
            if (dirty > 0f)
            {
                sb.Append(" in cash that ain't on the books.");
                float safe = RugLaunder.TotalSafeCapacity();
                if (safe >= 1f)
                    sb.Append(" Your shops can quietly soak up about $")
                      .Append(safe.ToString("N0"))
                      .Append(" of it today before the books start to smell.");
                sb.Append(" Set up a computer at your place — click it to wash it clean.");
            }
            else sb.Append(" — clean, for now.");

            // Vertical integration, the dirty way: if a factory of theirs supplies any of their shops, say so.
            float topBoost = 0f;
            foreach (BuildingRegistration reg in RugLaunder.AllOwned())
            {
                float b = RugFactoryBoost.For(reg);
                if (b > topBoost) topBoost = b;
            }
            if (topBoost >= 0.005f)
                sb.Append("\nYour factory's feeding the network — supplied shops wash and earn up to +")
                  .Append((topBoost * 100f).ToString("0")).Append("% bigger.");

            string band = RugHeat.Band(RugBooks.Heat);
            sb.Append("\nHeat: ").Append(band).Append(band == "None" ? "." : " — the taxman's sniffing. Don't sit on a big pile.");
            sb.Append("\n\n— RUGS! v").Append(RugsConfig.Version); // support: every player can read their build off the daily wire
            return sb.ToString();
        }

        // Localized neighborhood name for the wire (falls back to the raw key).
        private static string Hood(string key)
        {
            try { string n = Localizor.LocalizorManager.GetLocalization(key); return string.IsNullOrEmpty(n) ? key : n; }
            catch { return key; }
        }
    }
}
