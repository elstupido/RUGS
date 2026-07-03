using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using BigAmbitions.Items;
using UnityEngine;

namespace Rugs
{
    /// <summary>
    /// Registers the five RUGS! products into the game's item catalog at runtime by
    /// cloning a vanilla retail product (so each inherits a real icon, world model and
    /// tags) and overriding the name + price. No Unity / AssetBundles needed.
    /// </summary>
    internal static class RugItems
    {
        // Rugs are carried in the VANILLA closed cardboard box: it has a real world prefab, so the
        // box renders in the player's hands and stores/hauls like normal goods. A custom-named
        // container has NO prefab in the asset bundles and throws on pickup (empty hands + no HUD).
        // Contraband is enforced by pricePerUnit=0 (every vanilla sell path pays $0) + type=0 (out of
        // shops/demand), NOT by a special box.
        internal const string BagItem = "ba:itemname_closedcardboardbox";

        internal static readonly List<string> Registered = new List<string>();

        /// <summary>True once the vanilla catalog (our clone donors) is populated.</summary>
        internal static bool CatalogReady()
        {
            foreach (RugDef r in RugCatalog.All)
                if (ItemsGetter.GetByName(r.Donor, suppressError: true) != null)
                    return true;
            return false;
        }

        internal static void RegisterAll(IModLogger log)
        {
            foreach (RugDef r in RugCatalog.All)
            {
                try { RegisterOne(r, log); }
                catch (Exception e) { log.Error("RUGS! failed to register " + r.Key); log.Error(e); }
            }
            ScrubDemandMarket(log);
            log.Info($"RUGS! items: {Registered.Count} registered.");
        }

        internal static void UnregisterAll()
        {
            foreach (string key in Registered)
                ItemsGetter.UnregisterModItem(key);
            Registered.Clear();
        }

        private static void RegisterOne(RugDef r, IModLogger log)
        {
            if (ItemsGetter.IsModItem(r.Key) || ItemsGetter.GetByName(r.Key, suppressError: true) != null)
                return;

            Item donor = ItemsGetter.GetByName(r.Donor, suppressError: true) ?? FindAnyRetailDonor();
            if (donor == null)
            {
                log.Warn("RUGS! no donor for " + r.Key + "; skipped.");
                return;
            }

            Item clone = UnityEngine.Object.Instantiate(donor);
            clone.name = "RUGS_" + r.Key;
            clone.itemName = r.Key;

            // B1 — rugs are contraband, NOT normal commerce goods. They can only be traded
            // through rug dealers. Strip the donor's store/wholesale tags plus the
            // retail/demand flags so rugs never show up in shops, wholesaler/importer
            // menus, or the neighborhood demand market. (Visuals come from serialized
            // fields, not tags, so the icon/model survive.)
            clone.type = (ItemType)0;
            clone.isADemandedProduct = false;
            ClearTags(clone);

            clone.boxSize = 100; // ONE box holds 100 units → scarcity: ~100 by hand, ~8×100 on a cart
            SetPrivateFloat(clone, "defaultMarketPrice", r.BasePrice); // vestigial; harmless
            clone.BuildTagCache();

            ItemsGetter.RegisterModItem(clone); // re-adds only the "ba:itemtag_mod" marker tag
            Registered.Add(r.Key);
            log.Info($"RUGS! + {r.Key}  (contraband, dealer-only; cloned visuals from {r.Donor})");
        }

        // Remove any leftover rug entries from the neighborhood demand market. Earlier builds
        // registered rugs as demanded products, so old saves may hold rug ProductMarketEntries;
        // the game's own cleanup does GetByName(x).isADemandedProduct with no null guard, so we
        // purge them here to keep rugs fully out of normal commerce.
        private static void ScrubDemandMarket(IModLogger log)
        {
            try
            {
                var entries = SaveGameManager.Current?.productMarketEntries;
                if (entries == null) return;
                int removed = entries.RemoveAll(x => x != null && RugCatalog.ByKey(x.itemName) != null);
                if (removed > 0) log.Info($"RUGS! purged {removed} stale rug entries from the demand market.");
            }
            catch (Exception e) { log.Error("RUGS! demand-market scrub failed."); log.Error(e); }
        }

        private static Item FindAnyRetailDonor()
        {
            if (ItemsGetter.AllItems == null) return null;
            foreach (Item it in ItemsGetter.AllItems)
                if (it != null && (it.type & ItemType.RetailProduct) != 0 && it.isADemandedProduct)
                    return it;
            return null;
        }

        private static void SetPrivateFloat(Item target, string fieldName, float value)
        {
            FieldInfo f = typeof(Item).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f != null && f.FieldType == typeof(float))
                f.SetValue(target, value);
        }

        // Wipe the authoring tag list (inherited from the clone donor) so the rug belongs to
        // no store category / wholesale list. The base TaggedScriptableObject holds tags in a
        // private List<string>; RegisterModItem re-adds only the mod marker afterwards.
        private static void ClearTags(Item target)
        {
            FieldInfo f = typeof(Item).BaseType.GetField("tags", BindingFlags.Instance | BindingFlags.NonPublic);
            if (f != null) f.SetValue(target, new List<string>());
        }
    }
}
