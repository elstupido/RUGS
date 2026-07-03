namespace Rugs
{
    /// <summary>One rug product: catalog key, display name, the vanilla item we clone for
    /// visuals, a base market price, how much IRS HEAT a dollar of it generates, and whether
    /// it's a Common rug (sold everywhere) or a rarer one (scarce, and never traded in the
    /// player's home district).</summary>
    public sealed class RugDef
    {
        public readonly string Key;        // Item.itemName AND localization key, e.g. "ba:itemname_reed"
        public readonly string Display;    // "REED"
        public readonly string Donor;      // vanilla item to clone (icon, world model)
        public readonly float BasePrice;   // anchor market price
        public readonly float HeatWeight;  // IRS heat per $ dealt (cheap weed ≈ 0.5, fentanyl ≈ 3.5)
        public readonly bool Common;       // true = everywhere; false = rare + banned in the home district

        public RugDef(string key, string display, string donor, float basePrice, float heatWeight, bool common)
        {
            Key = key;
            Display = display;
            Donor = donor;
            BasePrice = basePrice;
            HeatWeight = heatWeight;
            Common = common;
        }
    }

    /// <summary>The RUGS! product line.</summary>
    public static class RugCatalog
    {
        // Common — stocked across the city, including the home district.
        public static readonly RugDef Reed    = new RugDef("ba:itemname_reed",    "REED",    "ba:itemname_lettuce",     90f,  0.5f, true);
        public static readonly RugDef Rocaine = new RugDef("ba:itemname_rocaine", "ROCAINE", "ba:itemname_sugar",       650f, 1.2f, true);
        public static readonly RugDef Reth    = new RugDef("ba:itemname_reth",    "RETH",    "ba:itemname_energydrink", 340f, 1.0f, true);
        public static readonly RugDef Rope    = new RugDef("ba:itemname_rope",    "ROPE",    "ba:itemname_beer",        460f, 1.0f, true);
        public static readonly RugDef Ranex   = new RugDef("ba:itemname_ranex",   "RANEX",   "ba:itemname_water",       150f, 0.6f, true);

        // Rare — only turn up at a fraction of dealers, and NEVER in the home district. Heavier heat.
        public static readonly RugDef Rushrooms = new RugDef("ba:itemname_rushrooms", "RUSHROOMS", "ba:itemname_russetpotatoes",    280f, 0.8f, false);
        public static readonly RugDef Radderall = new RugDef("ba:itemname_radderall", "RADDERALL", "ba:itemname_caffeineextract",   170f, 0.4f, false);
        public static readonly RugDef Reroin    = new RugDef("ba:itemname_reroin",    "REROIN",    "ba:itemname_groundcoffeebeans", 580f, 2.0f, false);
        public static readonly RugDef Rentanyl  = new RugDef("ba:itemname_rentanyl",  "RENTANYL",  "ba:itemname_bakingmix",         950f, 3.5f, false);

        public static readonly RugDef[] All = { Reed, Rocaine, Reth, Rope, Ranex, Rushrooms, Radderall, Reroin, Rentanyl };

        public static RugDef ByKey(string key)
        {
            foreach (RugDef r in All)
                if (r.Key == key) return r;
            return null;
        }
    }
}
