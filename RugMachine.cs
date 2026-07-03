using System;
using BAModAPI;   // IModLogger
using UnityEngine;

namespace Rugs
{
    /// <summary>
    /// "Laundry machine" — but it's just a REAL Big Ambitions item the player buys and places themselves.
    /// We don't spawn, sell, or place anything: the player orders a computer from the game's stores and sets
    /// it up in their apartment through BA's normal interior placement (real item, real model, real save —
    /// fully native). Our only job is to make that placed computer, while it's in the player's HOME, open the
    /// laundering window when clicked.
    ///
    /// The hook: for each matching placed item, parent a small interactable onto it (an
    /// <see cref="LaundererController"/>) whose enclosing collider catches the click — left-click opens our
    /// laundering UI; right-click is forwarded to the item's own pick-up/move so native placement still works.
    /// No custom item, no custom buy, no custom placement, no saved state — BA owns all of that; we just
    /// re-apply the hook each time the home interior loads.
    /// </summary>
    internal static class RugMachine
    {
        // The vanilla items that act as a "laundering rig" when placed in the player's home. Existing models,
        // so the game's own buy + placement handle them natively. Our enclosing click-catcher wins the click
        // before the host (even a desk the computer sits on), so it works wherever it's placed. Swap freely.
        private static readonly string[] Rigs =
            { "ba:itemname_desktopcomputer", "ba:itemname_computer", "ba:itemname_laptop" };
        private const string HookName = "RugLaunderHook";

        private static IModLogger _log;
        private static int _scan;

        internal static void Init(IModLogger log) { _log = log; }
        internal static void Reset() { } // nothing to persist — BA owns the item

        /// <summary>Per-frame (while engaged): hook any laundering-rig item the player has placed at home.</summary>
        internal static void Tick()
        {
            if (!InHome()) return;
            if ((++_scan % 30) != 0) return; // scan ~twice a second, not every frame (GetComponentsInChildren allocates)

            var bm = InstanceBehavior<BuildingManager>.Instance;
            Transform container = bm != null ? bm.indoorItemContainer : null;
            if (container == null) return;

            ItemController[] placed = container.GetComponentsInChildren<ItemController>(true);
            foreach (ItemController ic in placed)
            {
                if (ic == null || !IsRig(ic.itemName)) continue;
                if (ic.transform.Find(HookName) != null) continue; // already hooked this visit
                Hook(ic);
            }
        }

        private static bool IsRig(string itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return false;
            for (int i = 0; i < Rigs.Length; i++) if (Rigs[i] == itemName) return true;
            return false;
        }

        // Suppress the item's native click and attach our own interactable that opens the laundering window.
        private static void Hook(ItemController vanilla)
        {
            try
            {
                var go = new GameObject(HookName);
                go.transform.SetParent(vanilla.transform, false);
                BoxCollider col = go.AddComponent<BoxCollider>();
                col.size = new Vector3(1.0f, 1.3f, 1.0f); // enclose the host so our collider wins the click ray
                col.center = new Vector3(0f, 0.5f, 0f);
                LaundererController ctrl = go.AddComponent<LaundererController>(); // Awake() sets the clickable layer
                ctrl.Bind(vanilla); // left-click → launder; right-click → forwarded to the item's own pick-up/move
                if (RugsConfig.Dev) _log?.Info("RUGS! hooked a home '" + vanilla.itemName + "' as a laundering rig.");
            }
            catch (Exception e) { _log?.Error("RUGS! laundering hook failed."); _log?.Error(e); }
        }

        private static bool InHome()
        {
            try
            {
                if (!BuildingManager.IsInsideBuilding) return false;
                var bm = InstanceBehavior<BuildingManager>.Instance;
                BuildingRegistration reg = bm != null ? bm.buildingRegistration : null;
                if (reg == null || !reg.RentedByPlayer) return false;
                string t = reg.GetBuildingType();
                return !string.IsNullOrEmpty(t) && t.IndexOf("residential", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }
    }

    /// <summary>A small clickable we parent onto a placed item so interacting opens the laundering window.</summary>
    public sealed class LaundererController : EntityController
    {
        private ItemController _host; // the placed item we ride on — right-click is forwarded to it (pick up / move)

        internal void Bind(ItemController host)
        {
            _host = host;
            if (host != null) renderers = host.GetComponentsInChildren<Renderer>(true); // glow the host on hover
        }

        public override void Awake()
        {
            navMeshTargets = new Transform[0];
            renderers = new Renderer[0];
            base.Awake(); // sets the clickable layer
        }

        public override void Start() { } // skip base nav-mesh scheduling

        public override bool ShouldReactToIoEnter() => primaryInteractionEnabled && visible;

        protected override bool CanBeInteractedFromCurrentPosition()
        {
            var pc = InstanceBehavior<GameManager>.Instance?.playerController;
            return pc != null && Vector3.Distance(pc.transform.position, transform.position) <= 2.5f;
        }

        public override bool Interact() // left-click → launder
        {
            RugLaunderUI.Open();
            return true;
        }

        public override void OnIoRightClick() // right-click → hand off to the item's own pick-up / move
        {
            if (_host != null) _host.OnIoRightClick();
        }
    }
}
