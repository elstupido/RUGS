using BigAmbitions.Items;   // CargoInstance, ItemInstance
using Helpers;              // PlayerHelper
using UnityEngine;          // Mathf

namespace Rugs
{
    /// <summary>Trade execution + price math. You carry ONE bag holding up to BagCeiling
    /// units of a single rug; buying merges into it, selling offloads the whole bag.</summary>
    internal static class RugTrading
    {
        internal const int BagCeiling = 9999;

        internal static float BuyPrice(RugDealerController d, RugDef r)  => Mathf.Round(RugMarket.Price(r) * d.SellConstant(r));
        internal static float SellPrice(RugDealerController d, RugDef r) => Mathf.Round(RugMarket.Price(r) * d.BuyConstant(r));

        /// <summary>The rug the player is currently carrying, if any.</summary>
        internal static RugDef HeldRug()
        {
            CargoInstance c = HeldCargo();
            return c == null ? null : RugCatalog.ByKey(c.itemName);
        }

        internal static int HeldAmount()
        {
            CargoInstance c = HeldCargo();
            return c == null ? 0 : c.amount;
        }

        private static CargoInstance HeldCargo()
        {
            ItemInstance box = PlayerHelper.ItemInstanceInHands;
            if (box == null) return null;
            foreach (CargoInstance c in box.cargoInstances)
                if (!string.IsNullOrEmpty(c.itemName) && RugCatalog.ByKey(c.itemName) != null)
                    return c;
            return null;
        }

        /// <summary>How many of this rug the player could buy (money + bag room).</summary>
        internal static int MaxAffordable(RugDealerController d, RugDef r)
        {
            float unit = BuyPrice(d, r);
            int room = BagCeiling - (HeldRug() == r ? HeldAmount() : 0);
            int byMoney = unit <= 0f ? BagCeiling : Mathf.FloorToInt(SaveGameManager.Current.Money / unit);
            return Mathf.Clamp(Mathf.Min(byMoney, room), 0, BagCeiling);
        }

        /// <summary>Buy qty bags, merging into the carried bag. Returns a status string.</summary>
        internal static string Buy(RugDealerController d, RugDef r, int qty)
        {
            RugDef held = HeldRug();
            if (held != null && held != r)
                return $"You're carrying {held.Display} — offload it first.";

            int room = BagCeiling - (held == r ? HeldAmount() : 0);
            qty = Mathf.Min(qty, room);
            if (qty <= 0) return "Your bag's full.";

            float unit = BuyPrice(d, r);
            float cost = unit * qty;
            var info = new TransactionInfo("ba:transaction_rugdeal", "rugs");
            if (!GameManager.ChangeMoneySafe(-cost, info, null, null, force: false, showNotification: true))
                return "You can't afford that.";

            if (held == r)
            {
                HeldCargo().amount += qty;
                PlayerHelper.ItemInstanceInHands.OnItemsInCargoUpdated()?.Invoke(); // refresh the game's HUD count
            }
            else
            {
                PlayerHelper.ItemInstanceInHands =
                    ItemHelper.InitializeItemInHandsWithCargo(new CargoInstance(r.Key, qty, unit, paid: true));
            }

            return $"Bought {qty:N0} {r.Display} for ${cost:N0}.";
        }

        /// <summary>Sell the entire carried bag to a dealer who buys this rug.</summary>
        internal static string SellAll(RugDealerController d, RugDef r)
        {
            if (HeldRug() != r)
                return $"You're not carrying any {r.Display}.";

            int qty = HeldAmount();
            float total = SellPrice(d, r) * qty;
            var info = new TransactionInfo("ba:transaction_rugdeal", "rugs");
            GameManager.ChangeMoneySafe(total, info, null, null, force: false, showNotification: false);
            PlayerHelper.ItemInstanceInHands = null; // dealer takes the whole bag
            return $"Sold {qty:N0} {r.Display} for ${total:N0}.";
        }
    }
}
