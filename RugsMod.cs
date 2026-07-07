using System;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;   // UnityLifecycleProvider
using UnityEngine;

[assembly: RegisterModClass(typeof(Rugs.RugsMod))]

namespace Rugs
{
    /// <summary>
    /// RUGS! entry point. On city load: registers the rug items, drives the daily rug
    /// market, and (for testing) spawns a dealer with F8.
    /// </summary>
    [ModEntryOnCityLoad]
    public sealed class RugsMod : ModBigAmbitionsBase
    {
        private IModLogger _log;
        private bool _ready;
        private bool _rosterSpawned;
        private bool _migrated; // legacy "rugstash" ghost-box cleanup done (or nothing to do)

        public override Task OnLoadAsync(ModContext context)
        {
            _log = context.Logger;
            _log.Info("RUGS! city load — initialising.");
            UnityLifecycleProvider.OnUpdate += Tick;
            return Task.CompletedTask;
        }

        public override Task OnUnloadAsync()
        {
            UnityLifecycleProvider.OnUpdate -= Tick;
            RugItems.UnregisterAll();
            RugBooks.Reset();
            RugMarket.Reset();
            RugEvents.Reset();
            RugPlug.Reset();
            RugInventory.ResetMigration();
            RugDealers.Spawned.Clear();
            RugMachine.Reset();
            RugSidecars.Reset();
            RugFactoryBoost.Reset();
            _ready = false;
            _rosterSpawned = false;
            _migrated = false;
            _log?.Info("RUGS! unloaded.");
            return Task.CompletedTask;
        }

        private void Tick()
        {
            if (!_ready)
            {
                if (!RugItems.CatalogReady()) return;
                _ready = true;
                RugItems.RegisterAll(_log);
                RugBooks.Reset(); RugBooks.Load();   // re-bind the ledger to this save (market state lives here too)
                RugMarket.Reset(); RugMarket.EnsureLoaded();
                RugEvents.Reset();                   // re-bind district price swings to this save
                RugSidecars.Reset();                 // re-bind dealer sidecars to this save
                RugFactoryBoost.Reset();             // drop the boost day-cache (same reason the others reset here)
                RugPlug.Reset();                     // re-establish the contact for this save
                RugMachine.Init(_log);               // home laundry-machine appliance (spawns when you're home)
                _log.Info(RugsConfig.Dev
                    ? "RUGS! ready (DEV) v" + RugsConfig.Version + ".  \\ = cycle dealer look | F6 = clear spots | F7 = capture spot | F8 = test dealer | F9 = [debug] launder all | F10 = stress rows | F11 = force night crew."
                    : "RUGS! ready. v" + RugsConfig.Version);
            }

            // Legacy save cleanup: convert a pre-fix "rugstash" ghost box held in hand into a vanilla
            // box (or clear it). Retried each tick until it settles, because it must run AFTER the
            // player + HUD are alive — earlier and the hand-swap NREs (which is what stranded the
            // ghost box in old saves and made buying say "free up your hands"). Guarded so it never
            // touches a vanilla player's hands: it only acts on a literal "ba:itemname_rugstash" item.
            if (_ready && !_migrated)
            {
                try { _migrated = RugInventory.TryMigrateOldStash(); }
                catch (Exception e) { _migrated = true; _log?.Error("RUGS! old-stash migration failed."); _log?.Error(e); }
            }

            // Dealers always exist — they're the opt-in door. EVERYTHING else stays dormant until the
            // player makes their first rug deal: no Plug, no events, no heat, no interference with
            // normal Big Ambitions play. Install-and-ignore == vanilla BA.
            if (!_rosterSpawned)
            {
                _rosterSpawned = true;
                RugDealers.SpawnFromRoster(_log);
            }

            if (RugBooks.Engaged)
            {
                RugPlug.Ensure(_log);           // the Plug reaches out once you're in the game
                RugInventory.NormalizePrices(); // keep carried rugs worth $0 to vanilla (migrates old saves)
                try
                {
                    int day = SaveGameManager.Current.Day;
                    RugMarket.SyncDay(day);            // advance prices
                    RugDealUI.OnDayTick(day);          // re-render an open deal panel if midnight re-rolled prices
                    RugEvents.OnDayChanged(day, _log); // expire/roll district price swings + consequences
                    RugHeat.OnDayChanged(day, _log);   // update IRS heat; maybe trigger an audit
                    RugSidecars.OnDayChanged(day, _log); // dealer sidecars mint dirty cash off their fronts' takings
                    RugLaunder.OnDayChanged(day, _log);  // the night crew (auto-wash), if switched on in the GL
                    RugPlug.MaybePushWire(day, _log);  // text the daily wire on day rollover (incl. events)
                }
                catch { }
                RugMachine.Tick(); // keep the home laundry machine present while the player's in their residence
            }

            // Safety: Escape closes the deal panel — via RequestClose, so a pending street moment (muscle,
            // hospital) can't be dodged by mashing Esc; its own buttons are the only way through.
            if (RugDealUI.IsOpen && Input.GetKeyDown(KeyCode.Escape)) RugDealUI.RequestClose();
            if (RugLaunderUI.IsOpen && Input.GetKeyDown(KeyCode.Escape)) RugLaunderUI.Close();
            if (RugLedgerUI.IsOpen && Input.GetKeyDown(KeyCode.Escape)) RugLedgerUI.Close();

            // Authoring/debug hotkeys — DEV builds only (RugsConfig.Dev=false strips them for release).
            if (RugsConfig.Dev)
            {
                if (Input.GetKeyDown(KeyCode.Backslash)) CycleDealerLook();        // cycle dealer look (F5 = vanilla quicksave)
                if (Input.GetKeyDown(KeyCode.F7)) RugSpotCapture.Capture(_log);   // record where I'm standing
                if (Input.GetKeyDown(KeyCode.F6)) RugSpotCapture.Clear(_log);     // wipe the spots file
                if (Input.GetKeyDown(KeyCode.F8)) SpawnDealerAtPlayer();          // quick test dealer
                if (Input.GetKeyDown(KeyCode.F9)) DebugLaunder();                 // launder all dirty cash
                if (Input.GetKeyDown(KeyCode.F10)) // scroll stress test: fake rows in the list panels
                {
                    RugsConfig.UiStressRows = (RugsConfig.UiStressRows + 30) % 90;
                    _log.Info($"RUGS! UI stress rows = {RugsConfig.UiStressRows} — open the laundry / GL panel.");
                }
                if (Input.GetKeyDown(KeyCode.F11)) // automation test: force the night crew + dump rider holdings
                {
                    RugLaunder.DevForceNightCrew(_log);
                    foreach (BuildingRegistration reg in RugLaunder.AllOwned())
                        if (RugSidecars.HasSidecar(RugLaunder.Key(reg)))
                            _log.Info($"RUGS! [dev] rider @ {reg.BusinessName}: holding ${RugSidecars.Held(reg):N0}.");
                }
            }
        }

        // Dev hook: wash all dirty cash through whichever owned business has the most plausible room,
        // so you can watch it clear (dirty drops now; BA books the clean, taxed revenue next midnight).
        private void DebugLaunder()
        {
            string msg = RugLaunder.QuickWash(float.MaxValue);
            _log.Info("RUGS! [debug] " + msg + $" (dirty now ${RugBooks.Dirty:N0}).");
        }

        // Dev hook: bump the shared dealer-appearance seed and re-skin every live dealer, so I can flip
        // through looks in-game and lock the most gangster one (then bake the seed into AppearanceSeed).
        private void CycleDealerLook()
        {
            RugDealerController.AppearanceSeed++;
            RugDealers.ReskinAll();
            _log.Info($"RUGS! dealer look seed = {RugDealerController.AppearanceSeed} (press \\ to cycle).");
        }

        private void SpawnDealerAtPlayer()
        {
            try
            {
                var pc = InstanceBehavior<GameManager>.Instance?.playerController;
                if (pc == null) { _log.Warn("RUGS! no player controller yet."); return; }

                Transform t = pc.transform;
                Vector3 pos = t.position + t.forward * 2f;
                Quaternion rot = Quaternion.LookRotation(-t.forward);
                RugDealerController.Spawn(pos, rot, RugDealers.CurrentNeighborhood(), _log);
            }
            catch (Exception e)
            {
                _log.Error("RUGS! F8 dealer spawn failed.");
                _log.Error(e);
            }
        }
    }
}
