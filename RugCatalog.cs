namespace Rugs
{
    /// <summary>One rug product: its catalog key, display name, the vanilla item we
    /// clone for visuals/tags, and a base market price the rug market revolves around.</summary>
    public sealed class RugDef
    {
        public readonly string Key;        // Item.itemName AND localization key, e.g. "ba:itemname_reed"
        public readonly string Display;    // "REED"
        public readonly string Donor;      // vanilla item to clone (icon, world model, tags)
        public readonly float BasePrice;   // anchor market price

        public RugDef(string key, string display, string donor, float basePrice)
        {
            Key = key;
            Display = display;
            Donor = donor;
            BasePrice = basePrice;
        }
    }

    /// <summary>The five RUGS! products.</summary>
    public static class RugCatalog
    {
        public static readonly RugDef Reed    = new RugDef("ba:itemname_reed",    "REED",    "ba:itemname_lettuce",     90f);
        public static readonly RugDef Rocaine = new RugDef("ba:itemname_rocaine", "ROCAINE", "ba:itemname_sugar",       650f);
        public static readonly RugDef Reth    = new RugDef("ba:itemname_reth",    "RETH",    "ba:itemname_energydrink", 340f);
        public static readonly RugDef Rope    = new RugDef("ba:itemname_rope",    "ROPE",    "ba:itemname_beer",        460f);
        public static readonly RugDef Ranex   = new RugDef("ba:itemname_ranex",   "RANEX",   "ba:itemname_water",       150f);

        public static readonly RugDef[] All = { Reed, Rocaine, Reth, Rope, Ranex };

        public static RugDef ByKey(string key)
        {
            foreach (RugDef r in All)
                if (r.Key == key) return r;
            return null;
        }
    }
}
