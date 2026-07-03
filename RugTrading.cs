using BigAmbitions.Items;   // CargoInstance, ItemInstance
using Helpers;              // PlayerHelper
using UnityEngine;          // Mathf

namespace Rugs
{
    /// <summary>
    /// Trade execution + price math. Rugs are ordinary boxed cargo now: one held stash box carries
    /// all five rug types as separate stacks, each stack spawned with pricePerUnit=0 so it's worth
    /// $0 to every vanilla sell path. Buying merges into / appends a stack; selling offloads the
    /// whole carried amount of a rug to a dealer. (Hauled/stored rugs are sold by carrying them.)
    /// </summary>
    internal static class RugTrading
    {
        internal const int StackCeiling = 100; // per-box cap (matches RugItems boxSize) — carry scarcity

        // Drug-Wars pricing: ONE price per district per rug, IDENTICAL to buy and sell (no dealer margin) — the
        // profit is purely spatial (buy cheap in one district, haul it, sell dear in another). District price
        // events (spikes/crashes) layer on top. A same-spot buy+sell is a wash, so there's no in-place flip.
        internal static float BuyPrice(RugDealerController d, RugDef r)  => Quote(d, r);
        internal static float SellPrice(RugDealerController d, RugDef r) => Quote(d, r);
        private static float Quote(RugDealerController d, RugDef r)
            => Mathf.Round(RugMarket.Price(r, d.Neighborhood) * RugEvents.PriceMultiplier(r, d.Neighborhood));

        /// <summary>How many of this rug the player could buy (dirty stash + wallet, and stack room).</summary>
        internal static int MaxAffordable(RugDealerController d, RugDef r)
        {
            float unit = BuyPrice(d, r);
            int room = StackCeiling - RugInventory.TotalOf(r);
            float funds = RugBooks.Dirty + SaveGameManager.Current.Money; // spend dirty cash first, then wallet
            int byMoney = unit <= 0f ? room : Mathf.FloorToInt(funds / unit);
            return Mathf.Clamp(Mathf.Min(byMoney, room), 0, StackCeiling);
        }

        /// <summary>Buy qty of a rug, adding it to the held stash (a new stack or merged in).</summary>
        internal static string Buy(RugDealerController d, RugDef r, int qty)
        {
            ItemInstance held = PlayerHelper.ItemInstanceInHands;
            RugDef carrying = RugInventory.HeldType(); // the rug in hand, if any

            // One box, one type: if you're already carrying a DIFFERENT rug, stash or sell it first.
            if (carrying != null && carrying != r)
                return $"You're still holding {carrying.Display} — sell it or stash it first.";

            // Rugs can go into: empty hands (fresh box), the SAME rug's box (merge), or an empty
            // cardboard box still in hand — e.g. the one left over after selling out. Filling that
            // leftover box instead of refusing avoids a soft-lock where a sold-out player can't rebuy.
            bool canFillHeld = carrying == r || IsEmptyBox(held);
            if (held != null && !canFillHeld)
            {
                // Hands occupied by a non-rug item, or a box holding other goods — can't start a rug box.
                if (RugsConfig.Dev) Debug.Log("[RUGS!] buy blocked: hands hold '" + held.itemName + "'.");
                return "Hands are full — put that down first.";
            }

            qty = Mathf.Min(qty, StackCeiling - RugInventory.TotalOf(r));
            if (qty <= 0) return "You can't carry any more of that.";

            float unit = BuyPrice(d, r);
            float cost = unit * qty;
            float wallet = SaveGameManager.Current.Money;
            if (RugBooks.Dirty + wallet < cost) return "You're short. Come back with money.";

            // Pay from the DIRTY stash first, then dip into the real wallet for any shortfall.
            float fromDirty  = RugBooks.SpendDirty(cost);
            float fromWallet = cost - fromDirty;
            if (fromWallet > 0f)
                GameManager.ChangeMoneySafe(-fromWallet, new TransactionInfo("ba:transaction_rugdeal", "rugs"), null, null, force: true, showNotification: true);
            RugBooks.MarkEngaged(); // first deal switches the mod's systems on

            // Spawn as worthless cargo: pricePerUnit=0 -> $0 on every vanilla sell path; paid:true so it stacks/merges.
            var cargo = new CargoInstance(r.Key, qty, 0f, paid: true);
            if (held != null && canFillHeld) // merge into the carried rug box, or fill the empty box in hand
            {
                if (!held.TryToAddToCargo(cargo)) return "Your box is full.";
            }
            else // empty hands → start a fresh box
            {
                PlayerHelper.ItemInstanceInHands = ItemHelper.InitializeItemInHandsWithCargo(cargo, RugItems.BagItem);
            }
            return $"Bought {qty:N0} {r.Display} for ${cost:N0}.";
        }

        /// <summary>Sell up to qty of this rug (capped at what's carried) to a dealer who buys it.</summary>
        internal static string Sell(RugDealerController d, RugDef r, int qty)
        {
            int carried = RugInventory.TotalOf(r);
            qty = Mathf.Min(qty, carried);
            if (qty <= 0) return $"You're not carrying any {r.Display}.";

            int sold = RugInventory.Remove(r, qty);
            if (sold <= 0) return $"Couldn't offload {r.Display}.";
            RugBooks.MarkEngaged(); // first deal switches the mod's systems on

            float total = SellPrice(d, r) * sold;
            // Proceeds go to the DIRTY-CASH stash, NOT the wallet — launder it to make it spendable.
            RugBooks.AddDirty(total, d.Neighborhood, r.HeatWeight); // heavier rugs raise more heat
            return $"Sold {sold:N0} {r.Display} for ${total:N0} dirty cash.";
        }

        /// <summary>Sell everything of this rug the player is carrying.</summary>
        internal static string SellAll(RugDealerController d, RugDef r) => Sell(d, r, int.MaxValue);

        /// <summary>True if the player is holding our cardboard box with NOTHING in it (e.g. just
        /// sold out) — safe to fill with a fresh rug stack instead of refusing the buy.</summary>
        private static bool IsEmptyBox(ItemInstance held)
        {
            if (held == null || held.itemName != RugItems.BagItem) return false;
            if (held.cargoInstances == null) return true;
            foreach (CargoInstance c in held.cargoInstances)
                if (c != null && c.amount > 0) return false;
            return true;
        }
    }
}
