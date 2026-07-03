using System.Collections.Generic;
using BAModAPI;
using BigAmbitions.Characters;              // Gender, SkinColor
using BigAmbitions.Characters.Appearance;   // AppearanceTag
using Character;                            // SkinColorHelper
using Helpers;          // PrefabHelper
using UnityEngine;

namespace Rugs
{
    /// <summary>
    /// A street rug dealer: a stationary, clickable NPC (subclass of the game's EntityController) that opens
    /// the deal panel. A dealer only decides WHICH rugs its corner trades (all commons + a few rares,
    /// deterministic per spot); PRICES are the district's daily rolled rates (<see cref="RugMarket"/>),
    /// identical to buy and sell — no per-dealer margin. Profit is spatial: work the districts, not the dealer.
    /// </summary>
    public sealed class RugDealerController : EntityController
    {
        private ThirdPersonCharacter _seller;
        private string _dealerName = "Rug Dealer";
        private RugDef[] _sells = new RugDef[0];   // rugs the player can BUY here (this dealer's subset)
        private RugDef[] _buys = new RugDef[0];    // rugs the player can SELL here (this dealer's subset)

        public string Name => _dealerName;
        public string Neighborhood { get; private set; } = "";
        public RugDef[] Sells => _sells;
        public RugDef[] Buys => _buys;

        // The "anchor": the dealer right outside the player's home. Its one privilege: it BUYS every rug,
        // so home is the guaranteed offload spot (it sells commons only; prices are the district's normal rates).
        public bool IsAnchor { get; private set; }

        private static readonly string[] DealerNames =
            { "Reggie", "Slim", "Tony Two-Times", "Mickey", "Vinnie", "Dutch", "Smalls", "Caine", "Frankie", "Ace" };

        // Every dealer wears ONE fixed look so the player recognises them on sight: a street/hood
        // outfit (Male, Casual streetwear). The game builds the character from a System.Random(seed),
        // so a fixed seed makes all 22 dealers identical. Dev builds cycle this live (F5) to dial the
        // look in; the chosen seed is baked here for release.
        internal static int AppearanceSeed = 49; // locked in-game via the \ cycle — the street/hood look

        public static RugDealerController Spawn(Vector3 position, Quaternion rotation, string neighborhood, IModLogger log)
        {
            var go = new GameObject("RugDealer");
            go.transform.SetPositionAndRotation(position, rotation);

            var col = go.AddComponent<CapsuleCollider>();
            col.height = 1.9f; col.radius = 0.45f; col.center = new Vector3(0f, 0.95f, 0f);

            var ctrl = go.AddComponent<RugDealerController>(); // runs Awake()
            int seed = SeedFromPosition(position);
            ctrl.Neighborhood = neighborhood ?? "";
            ctrl._dealerName = DealerNames[((seed % DealerNames.Length) + DealerNames.Length) % DealerNames.Length];
            ctrl.RollInventory(seed);          // deterministic per location → same mix every session
            ctrl.SpawnCharacter(log);
            log?.Info($"RUGS! dealer '{ctrl._dealerName}' @ '{ctrl.Neighborhood}' {position}; {ctrl.SellsSummary()}.");
            return ctrl;
        }

        // Stable seed from the spot (rounded to 0.1m so float jitter doesn't change the mix).
        private static int SeedFromPosition(Vector3 p)
        {
            unchecked
            {
                int h = Mathf.RoundToInt(p.x * 10f);
                h = h * 397 ^ Mathf.RoundToInt(p.z * 10f);
                h = h * 397 ^ Mathf.RoundToInt(p.y * 10f);
                return h;
            }
        }

        private void RollInventory(int seed)
        {
            // Deterministic per dealer (seeded by location) so the SUBSET each corner trades is stable across
            // sessions. PRICE is no longer per-dealer — it's the DISTRICT's rolled rate (see RugMarket/RugTrading),
            // identical to buy and sell. This only picks WHICH rugs this corner deals.
            //
            // SELL (what you can BUY here): ALL commons + a random 1–3 rares (rares take hunting to FIND).
            // BUY  (what the dealer purchases from you): ALL commons + a random 2–4 rares = a 7–9-of-9 floor, so
            // you can almost always offload — and rares pay big because their district PRICE BAND runs high.
            UnityEngine.Random.State prev = Random.state;
            Random.InitState(seed);
            try
            {
                var sells = new List<RugDef>();
                var buys  = new List<RugDef>();
                foreach (RugDef r in RugCatalog.All)
                    if (r.Common) { sells.Add(r); buys.Add(r); }      // commons trade everywhere, both ways
                foreach (RugDef r in PickRares(Random.Range(1, 4)))    // sell 1–3 rares — scarce to FIND
                    if (!sells.Contains(r)) sells.Add(r);
                foreach (RugDef r in PickRares(Random.Range(2, 5)))    // buy 2–4 rares
                    if (!buys.Contains(r)) buys.Add(r);
                _sells = sells.ToArray();
                _buys  = buys.ToArray();
            }
            finally { Random.state = prev; } // don't disturb the game's global RNG
        }

        // A random `count` distinct RARE (non-Common) rugs.
        private static List<RugDef> PickRares(int count)
        {
            var pool = new List<RugDef>();
            foreach (RugDef r in RugCatalog.All) if (!r.Common) pool.Add(r);
            Shuffle(pool);
            count = Mathf.Clamp(count, 0, pool.Count);
            return pool.GetRange(0, count);
        }

        // Promote this dealer to the home anchor: your reliable spot right outside the house. Its ONE privilege:
        // it BUYS every rug (commons + rares), so home is the guaranteed place to offload a whole haul — the
        // player never has to go hunting for a dealer who'll take what they're carrying. It still only SELLS
        // commons (rares stay scarce on the home block). Prices are the district's normal rolled rates — no
        // special home discount.
        internal void MakeAnchor()
        {
            IsAnchor = true;
            var sells = new List<RugDef>();
            var buys  = new List<RugDef>();
            foreach (RugDef r in RugCatalog.All)
            {
                buys.Add(r);                 // the anchor BUYS every rug — your guaranteed cash-out near home
                if (r.Common) sells.Add(r);  // ...but only SELLS commons (rares stay scarce on the home block)
            }
            _sells = sells.ToArray();
            _buys  = buys.ToArray();
        }

        private static void Shuffle(List<RugDef> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private string SellsSummary()
        {
            var parts = new List<string>();
            foreach (RugDef r in _sells) parts.Add("sell " + r.Display);
            foreach (RugDef r in _buys)  parts.Add("buy " + r.Display);
            return string.Join(", ", parts);
        }

        public override void Awake()
        {
            // EntityController.Start()/highlight code reads these; null on a code-built object.
            navMeshTargets = new Transform[0];
            renderers = new Renderer[0];
            base.Awake(); // sets the clickable layer
        }

        public override void Start() { } // skip base nav-mesh scheduling

        private void SpawnCharacter(IModLogger log)
        {
            try
            {
                _seller = PrefabHelper.CreatePrefab<ThirdPersonCharacter>("Characters/HumanDefinitionLow", transform);
                _seller.gameObject.SetActive(true);
                ApplyDealerLook(_seller);                 // fixed street/hood look so every dealer matches
                _seller.ForceToTransform(transform);
                renderers = _seller.GetComponentsInChildren<Renderer>(true);
                foreach (Collider c in _seller.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                log?.Info("RUGS! dealer character ready.");
            }
            catch (System.Exception e)
            {
                log?.Error("RUGS! dealer character spawn failed.");
                log?.Error(e);
            }
        }

        // Apply the shared street/hood dealer look, deterministically from AppearanceSeed (so all
        // dealers are identical). Falls back to a plain random appearance if the styled call throws
        // (e.g. skin-color addressables not ready yet).
        private static void ApplyDealerLook(ThirdPersonCharacter c)
        {
            if (c == null || c.appearanceSetter == null) return;
            var aps = c.appearanceSetter;
            try
            {
                SkinColor skin = SkinColorHelper.GetRandom(AppearanceSeed);
                aps.SetRandomAppearance(Gender.Male, skin, new[] { AppearanceTag.Casual }, AppearanceSeed);
                if (aps.data != null) { aps.data.ageInDays = 34 * 365; aps.UpdateVisualAge(); } // grown men, fixed age
            }
            catch (System.Exception) { try { aps.SetRandomAppearance(); } catch { } }
        }

        /// <summary>Re-apply the shared look to this already-spawned dealer (used by dev seed-cycling).</summary>
        internal void Reskin() => ApplyDealerLook(_seller);

        public override bool ShouldReactToIoEnter() => primaryInteractionEnabled && visible;

        protected override bool CanBeInteractedFromCurrentPosition()
        {
            var pc = InstanceBehavior<GameManager>.Instance?.playerController;
            return pc != null && Vector3.Distance(pc.transform.position, transform.position) <= 4f;
        }

        public override bool Interact()
        {
            RugDealUI.Open(this);
            return true;
        }
    }
}
