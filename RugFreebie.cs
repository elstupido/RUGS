using System;
using UnityEngine;

namespace Rugs
{
    /// <summary>
    /// "First ones free" (T0.1) — the onboarding hook. The very first time the player ever opens a dealer,
    /// that dealer fronts them a small starter stash so a broke player can get into the loop with zero
    /// capital. Fires exactly once, ever (persisted in modData), and reuses the same arrival-announce panel
    /// the events use ([RugEvents.Arrival] / RugDealUI.BuildArrival). The grant + the once-ever latch happen
    /// on acknowledge, so the offer waits around until it's actually taken.
    /// </summary>
    internal static class RugFreebie
    {
        private const string KClaimed      = "rugs:firstFreeClaimed";
        private const int    StarterAmount = 20;   // units fronted — a ~$1.5-2k starter (tunable balance knob)

        // The starter rug: a common one, so it's sellable at every dealer and teaches the cross-town spread.
        private static RugDef StarterRug => RugCatalog.Reed;

        /// <summary>
        /// Returns a one-time "here's a starter stash" arrival to announce on the player's FIRST dealer open,
        /// or null if it's already been claimed. Slots in ahead of the normal event roll in RugDealUI.Open.
        /// </summary>
        internal static RugEvents.Arrival TryFirstFree(RugDealerController dealer)
        {
            if (Claimed) return null;
            return new RugEvents.Arrival
            {
                message = $"New face? First taste's on me. Here's {StarterAmount} {StarterRug.Display} to get " +
                          "you started — go turn it into money. After this, you pay like everybody else.",
                ends = false,            // flip to the normal buy/sell panel after the player acknowledges
                onContinue = () => GrantAndClaim(dealer),
            };
        }

        private static bool Claimed => RugBooks.GetRaw(KClaimed) == "1";

        // Hand over the starter stash and latch the once-ever flag + engage the mod — the freebie IS the
        // player's first deal. RugInventory.GiveRugs routes the rugs to their hands or the cart they're
        // pushing, and if there's no room it sells them for dirty cash AT THIS CORNER'S street price,
        // booked to this district — so the gift always lands somehow, priced like everything else here.
        private static void GrantAndClaim(RugDealerController dealer)
        {
            try
            {
                string hood = dealer != null ? dealer.Neighborhood : null;
                RugInventory.GiveRugs(StarterRug, StarterAmount, RugMarket.StreetPrice(StarterRug, hood), hood);
                RugBooks.SetRaw(KClaimed, "1"); // once, ever
                RugBooks.MarkEngaged();         // the world wakes up — Plug, market, events, heat
            }
            catch (Exception e) { Debug.LogError("[RUGS!] first-free grant failed: " + e); }
        }
    }
}
