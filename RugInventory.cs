using System;
using System.Collections.Generic;
using BigAmbitions.Items;   // ItemInstance, CargoInstance
using Helpers;              // PlayerHelper
using UnityEngine;          // Mathf

namespace Rugs
{
    /// <summary>
    /// Reads/writes the rug cargo the player is carrying. Rugs are ordinary boxed cargo: ONE rug type
    /// per box (like a boxed, un-deployable good), spawned with pricePerUnit=0 so it's worth $0 to
    /// every vanilla sell path — only a rug dealer pays for it. The player holds one box at a time;
    /// carry more types by stashing boxes on a hand truck or in house storage.
    ///
    /// Selling reads what's in the player's HANDS (the held box). Rugs stashed on a hand truck or in
    /// house storage aren't auto-sold; carry the box in hand to sell it.
    /// </summary>
    internal static class RugInventory
    {
        // A rug stack pulled from a neutralized legacy "rugstash" ghost box, awaiting the HUD before we
        // hand it back as a proper cardboard box. Cleared on city unload via ResetMigration().
        private static CargoInstance _pendingSalvage;

        /// <summary>Clear cross-save migration state (called on city unload).</summary>
        internal static void ResetMigration() { _pendingSalvage = null; }

        /// <summary>The held box if it contains any rug cargo, else null. (Rugs ride in a plain
        /// cardboard box now, so we identify a "rug box" by its CONTENTS, not the container name.)</summary>
        internal static ItemInstance HeldStash()
        {
            ItemInstance box = PlayerHelper.ItemInstanceInHands;
            if (box == null || box.cargoInstances == null) return null;
            foreach (CargoInstance c in box.cargoInstances)
                if (c != null && RugCatalog.ByKey(c.itemName) != null) return box;
            return null;
        }

        /// <summary>The single rug type in the held box, or null if none (one box = one type).</summary>
        internal static RugDef HeldType()
        {
            ItemInstance box = HeldStash();
            if (box == null || box.cargoInstances == null) return null;
            foreach (CargoInstance c in box.cargoInstances)
            {
                if (c == null || c.amount <= 0) continue;
                RugDef r = RugCatalog.ByKey(c.itemName);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>Total units of rug <paramref name="r"/> carried in the held stash.</summary>
        internal static int TotalOf(RugDef r)
        {
            ItemInstance box = HeldStash();
            if (box == null || box.cargoInstances == null) return 0;
            int sum = 0;
            foreach (CargoInstance c in box.cargoInstances)
                if (c != null && c.itemName == r.Key) sum += c.amount;
            return sum;
        }

        /// <summary>Every rug type currently carried, with amounts (for the deal panel).</summary>
        internal static List<(RugDef rug, int amount)> Carried()
        {
            var list = new List<(RugDef, int)>();
            ItemInstance box = HeldStash();
            if (box == null || box.cargoInstances == null) return list;
            foreach (CargoInstance c in box.cargoInstances)
            {
                if (c == null || c.amount <= 0) continue;
                RugDef r = RugCatalog.ByKey(c.itemName);
                if (r != null) list.Add((r, c.amount));
            }
            return list;
        }

        /// <summary>Remove up to <paramref name="qty"/> of rug <paramref name="r"/>; returns the amount actually removed.</summary>
        internal static int Remove(RugDef r, int qty)
        {
            ItemInstance box = HeldStash();
            if (box == null || box.cargoInstances == null || qty <= 0) return 0;

            int removed = 0;
            // Snapshot the slot list — RemoveFromCargo mutates box.cargoInstances mid-loop.
            var slots = new List<CargoInstance>(box.cargoInstances);
            foreach (CargoInstance c in slots)
            {
                if (removed >= qty) break;
                if (c == null || c.itemName != r.Key) continue;
                int take = Mathf.Min(c.amount, qty - removed);
                if (take >= c.amount) box.RemoveFromCargo(c);
                else box.ReduceFromCargo(c, take);
                removed += take;
            }
            if (removed > 0) DropEmptyHeldBox(); // selling the last rug out leaves an empty container — drop it
            return removed; // Remove/Reduce fire the HUD refresh internally
        }

        /// <summary>Outcome of a <see cref="GiveRugs"/> gift: how much was carried vs sold for dirty cash.</summary>
        internal struct GiveResult
        {
            public int   carried;    // units placed into the hand box / cart
            public int   cashed;     // units that wouldn't fit and were sold on the spot
            public float cashValue;  // dirty cash credited for the cashed units
            public bool  Any => carried > 0 || cashed > 0;
        }

        /// <summary>
        /// Give the player <paramref name="amount"/> units of rug <paramref name="r"/> (an event gift / freebie).
        /// Routes them to wherever there's room — a CART they're pushing, the matching rug box in hand, or a fresh
        /// box in empty hands — mirroring BA's own cargo-target logic (<c>PlayerHelper.IsHoldingItem</c> → the
        /// hand item, else <c>VehicleHelper.GetCurrentVehicle()</c> → the pushed cart; the two are mutually
        /// exclusive). It NEVER overwrites a box that's holding something else. Anything that won't fit is sold on
        /// the spot for dirty cash so a gift is never lost. FREE gifts cash out at the rug's base price;
        /// PURCHASED lots must pass <paramref name="cashUnitPrice"/> (what was paid per unit) so the un-carriable
        /// remainder refunds AT COST — otherwise a discounted street lot cashed at full price is a money pump.
        /// Returns what happened.
        /// </summary>
        internal static GiveResult GiveRugs(RugDef r, int amount, float cashUnitPrice = -1f)
        {
            var res = new GiveResult();
            if (r == null || amount <= 0) return res;

            // Not holding an item? Then either a CART is the target (hands are nulled while pushing one), or
            // hands are genuinely empty and we start a fresh box.
            if (!PlayerHelper.IsHoldingItem)
            {
                VehicleInstance cart = VehicleHelper.GetCurrentVehicle(); // null unless pushing a cart/hand truck
                if (cart != null)
                {
                    if (cart.TryToAddToCargo(new CargoInstance(r.Key, amount, 0f, paid: true))) res.carried = amount;
                    else Cash(r, amount, ref res, cashUnitPrice); // cart full → sell on the spot
                    return res;
                }
                int take = Mathf.Min(amount, RugTrading.StackCeiling);
                PlayerHelper.ItemInstanceInHands = ItemHelper.InitializeItemInHandsWithCargo(new CargoInstance(r.Key, take, 0f, paid: true), RugItems.BagItem);
                res.carried = take;
                Cash(r, amount - take, ref res, cashUnitPrice); // any overflow past one box → cash
                return res;
            }

            // Holding a box of THIS rug → top it up (never overwrite). Overflow cashed.
            ItemInstance held = PlayerHelper.ItemInstanceInHands;
            if (held != null && HeldType() == r)
            {
                int take = Mathf.Min(amount, RugTrading.StackCeiling - TotalOf(r));
                if (take > 0 && held.TryToAddToCargo(new CargoInstance(r.Key, take, 0f, paid: true)))
                {
                    res.carried = take;
                    Cash(r, amount - take, ref res, cashUnitPrice);
                }
                else Cash(r, amount, ref res, cashUnitPrice); // box already full
                return res;
            }

            // Hands hold a different rug, a non-rug box, or any other item → leave it alone; cash the gift.
            Cash(r, amount, ref res, cashUnitPrice);
            return res;
        }

        // Sell un-carriable gifted rugs on the spot → dirty cash (with the rug's heat). Never lose a gift.
        // unitPrice < 0 → base market price (free gifts); otherwise the caller's price (purchased lots → at cost).
        private static void Cash(RugDef r, int units, ref GiveResult res, float unitPrice = -1f)
        {
            if (units <= 0) return;
            float value = Mathf.Round((unitPrice >= 0f ? unitPrice : RugMarket.Price(r)) * units);
            if (value > 0f) RugBooks.AddDirty(value, null, r.HeatWeight);
            res.cashed    += units;
            res.cashValue += value;
        }

        /// <summary>
        /// Drop the held box if it's OUR cardboard box and now holds no rugs (e.g. everything just sold).
        /// An empty box left in hand is invisible to the "Carrying" readout yet still makes the game treat
        /// hands as FULL — it blocks picking up shopping baskets and any other object (the "phantom hands"
        /// bug v1 players hit after selling out). Safe to clear a VANILLA cardboard box: its ItemCached
        /// resolves, so the game's RemoveItemsFromHands path won't NRE (unlike the unregistered legacy
        /// "rugstash" container). Only touches an EMPTY box of ours — never a box with goods in it.
        /// </summary>
        internal static void DropEmptyHeldBox()
        {
            ItemInstance box = PlayerHelper.ItemInstanceInHands;
            if (box == null || box.itemName != RugItems.BagItem) return;
            if (box.cargoInstances != null)
                foreach (CargoInstance c in box.cargoInstances)
                    if (c != null && c.amount > 0) return; // still holds something — leave it alone
            try { PlayerHelper.ItemInstanceInHands = null; }
            catch (Exception e) { Debug.LogError("[RUGS!] failed to drop empty rug box: " + e); }
        }

        /// <summary>
        /// Back-compat cleanup for pre-container-fix saves where the player holds a box from the OLD
        /// custom "rugstash" container. That item is no longer registered, so its ItemCached is null —
        /// and the GAME calls <c>PlayerHelper.IsHoldingAMop</c> EVERY FRAME (MouseController.Run), which
        /// dereferences that null and throws. Result: the whole session spews NREs and mouse/interaction
        /// is broken. (This is the "old saves badly broken" report.)
        ///
        /// We NEUTRALIZE the ghost immediately by clearing the hand FIELD directly
        /// (<c>CharacterData.itemInHands = null</c>): that bypasses the game's RemoveItemsFromHands path
        /// (which itself NREs on the null ItemCached) and needs no HUD, so the per-frame crash stops on the
        /// first tick we run — no waiting, no window. Any rug the ghost carried is salvaged and handed back
        /// as a real cardboard box once the HUD is alive (that step routes through AddItemToHands → playerHUD).
        ///
        /// Returns true once SETTLED; false to retry next tick (player not loaded, or HUD pending for the
        /// salvage hand-back). No-op for any normal save — only a literal "rugstash" item triggers it.
        /// </summary>
        internal static bool TryMigrateOldStash()
        {
            ItemInstance box;
            try { box = PlayerHelper.ItemInstanceInHands; } // getter chain (CharacterData) can NRE mid-load
            catch { return false; }                          // player not loaded — retry

            if (box != null && box.itemName == "ba:itemname_rugstash")
            {
                // Salvage a rug stack (if any) before dropping the ghost.
                if (box.cargoInstances != null)
                    foreach (CargoInstance c in box.cargoInstances)
                        if (c != null && c.amount > 0 && RugCatalog.ByKey(c.itemName) != null) { c.pricePerUnit = 0f; _pendingSalvage = c; break; }

                // Kill the ghost NOW — directly, without the setter or the HUD — so the game's per-frame
                // IsHoldingAMop NRE stops immediately. Retry next tick if we can't reach CharacterData yet.
                if (!ClearHeldFieldDirect()) return false;
                if (RugsConfig.Dev) Debug.Log("[RUGS!] neutralized legacy rugstash ghost box (per-frame NRE stopped).");
            }

            // Hand the salvaged rug back as a proper cardboard box, once the HUD exists.
            if (_pendingSalvage != null)
            {
                if (InstanceBehavior<UI.UIs>.Instance == null) return false; // wait for the HUD
                ItemInstance held;
                try { held = PlayerHelper.ItemInstanceInHands; } catch { return false; }
                if (held == null) // don't clobber anything the player grabbed in the meantime
                    PlayerHelper.ItemInstanceInHands = ItemHelper.InitializeItemInHandsWithCargo(_pendingSalvage, RugItems.BagItem);
                if (RugsConfig.Dev) Debug.Log("[RUGS!] restored salvaged rug from old stash -> cardboard box.");
                _pendingSalvage = null;
            }
            return true;
        }

        /// <summary>
        /// Clear the held-item FIELD directly (<c>CharacterData.itemInHands = null</c>), bypassing the
        /// PlayerHelper setter / RemoveItemsFromHands (which NREs on an unregistered held item) and the HUD.
        /// Returns false if CharacterData isn't reachable yet (player still loading) so the caller retries.
        /// </summary>
        private static bool ClearHeldFieldDirect()
        {
            try
            {
                var cd = PlayerHelper.CharacterData; // var → no need to name the type's namespace
                if (cd == null) return false;
                cd.itemInHands = null;
                return true;
            }
            catch (Exception e) { if (RugsConfig.Dev) Debug.LogError("[RUGS!] direct hand-clear retry: " + e); return false; }
        }

        /// <summary>
        /// Force pricePerUnit=0 on any rug cargo in the held stash. Migration for saves made under
        /// the old model (rugs stored at their buy price would otherwise be vanilla-sellable for real
        /// money). Cheap; safe to call every tick.
        /// </summary>
        internal static void NormalizePrices()
        {
            ItemInstance box = HeldStash();
            if (box == null || box.cargoInstances == null) return;
            foreach (CargoInstance c in box.cargoInstances)
                if (c != null && c.pricePerUnit != 0f && RugCatalog.ByKey(c.itemName) != null)
                    c.pricePerUnit = 0f;
        }
    }
}
