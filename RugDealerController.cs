using System.Collections.Generic;
using BAModAPI;
using Helpers;          // PrefabHelper
using UnityEngine;

namespace Rugs
{
    /// <summary>
    /// A street rug dealer: a stationary, clickable NPC (subclass of the game's
    /// EntityController) that opens a buy dialog. Each dealer sells a small subset of
    /// rugs at market × its own (randomised) constant.
    /// </summary>
    public sealed class RugDealerController : EntityController
    {
        private ThirdPersonCharacter _seller;
        private string _dealerName = "Rug Dealer";
        private RugDef[] _sells = new RugDef[0];   // rugs the player can BUY here
        private RugDef[] _buys = new RugDef[0];    // rugs the player can SELL here
        private readonly Dictionary<RugDef, float> _sellConst = new Dictionary<RugDef, float>();
        private readonly Dictionary<RugDef, float> _buyConst = new Dictionary<RugDef, float>();

        public string Name => _dealerName;
        public RugDef[] Sells => _sells;
        public RugDef[] Buys => _buys;
        public float SellConstant(RugDef r) => _sellConst.TryGetValue(r, out float c) ? c : 1.15f;
        public float BuyConstant(RugDef r) => _buyConst.TryGetValue(r, out float c) ? c : 0.85f;

        public static RugDealerController Spawn(Vector3 position, Quaternion rotation, string dealerName, IModLogger log)
        {
            var go = new GameObject("RugDealer");
            go.transform.SetPositionAndRotation(position, rotation);

            var col = go.AddComponent<CapsuleCollider>();
            col.height = 1.9f; col.radius = 0.45f; col.center = new Vector3(0f, 0.95f, 0f);

            var ctrl = go.AddComponent<RugDealerController>(); // runs Awake()
            ctrl._dealerName = dealerName;
            ctrl.RollInventory();
            ctrl.SpawnCharacter(log);
            log?.Info($"RUGS! dealer '{dealerName}' spawned; sells {ctrl.SellsSummary()}.");
            return ctrl;
        }

        private void RollInventory()
        {
            // Disjoint subsets: sells 1–2 rugs (1.05–1.30× market), buys 1–2 OTHER rugs
            // (0.70–0.95× market). Disjoint sell/buy sets across dealers create arbitrage.
            var pool = new List<RugDef>(RugCatalog.All);
            Shuffle(pool);

            // Ranges OVERLAP so cross-dealer arbitrage is possible: a dealer selling a rug
            // cheap (low sell const) + another buying it dear (high buy const) = profit,
            // on top of market timing. Most pairs still lose (the dealers' cut), so you
            // have to shop around for a good route.
            int sellCount = Mathf.Min(Random.Range(1, 3), pool.Count);
            for (int i = 0; i < sellCount; i++) { RugDef r = pool[0]; pool.RemoveAt(0); _sellConst[r] = Round2(Random.Range(0.95f, 1.20f)); }

            int buyCount = Mathf.Min(Random.Range(1, 3), pool.Count);
            for (int i = 0; i < buyCount; i++) { RugDef r = pool[0]; pool.RemoveAt(0); _buyConst[r] = Round2(Random.Range(0.80f, 1.10f)); }

            _sells = new List<RugDef>(_sellConst.Keys).ToArray();
            _buys = new List<RugDef>(_buyConst.Keys).ToArray();
        }

        private static float Round2(float v) => Mathf.Round(v * 100f) / 100f;

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
            foreach (RugDef r in _sells) parts.Add($"sell {r.Display}x{_sellConst[r]:0.00}");
            foreach (RugDef r in _buys) parts.Add($"buy {r.Display}x{_buyConst[r]:0.00}");
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
                _seller.appearanceSetter.SetRandomAppearance();
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

        public override bool ShouldReactToIoEnter() => primaryInteractionEnabled && visible;

        private static bool _loggedHover;
        public override void OnIoEnter()
        {
            if (!_loggedHover) { _loggedHover = true; Debug.Log("[RUGS!] dealer hover detected — raycast hit OK."); }
            base.OnIoEnter();
        }

        protected override bool CanBeInteractedFromCurrentPosition()
        {
            var pc = InstanceBehavior<GameManager>.Instance?.playerController;
            return pc != null && Vector3.Distance(pc.transform.position, transform.position) <= 4f;
        }

        public override bool Interact()
        {
            Debug.Log("[RUGS!] dealer Interact() — opening deal panel.");
            RugDealUI.Open(this);
            return true;
        }
    }
}
