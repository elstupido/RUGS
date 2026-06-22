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
            _ready = false;
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
                RugMarket.EnsureLoaded();
                _log.Info("RUGS! ready.  F7 = capture dealer spot here  |  F6 = clear spots  |  F8 = test dealer in front of you.");
            }

            // Drive the rug market off the in-game day.
            try { RugMarket.SyncDay(SaveGameManager.Current.Day); } catch { }

            if (Input.GetKeyDown(KeyCode.F7)) RugSpotCapture.Capture(_log);   // record where I'm standing
            if (Input.GetKeyDown(KeyCode.F6)) RugSpotCapture.Clear(_log);     // wipe the spots file
            if (Input.GetKeyDown(KeyCode.F8)) SpawnDealerAtPlayer();          // quick test dealer
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
                string name = DealerNames[UnityEngine.Random.Range(0, DealerNames.Length)];
                RugDealerController.Spawn(pos, rot, name, _log);
            }
            catch (Exception e)
            {
                _log.Error("RUGS! F8 dealer spawn failed.");
                _log.Error(e);
            }
        }

        private static readonly string[] DealerNames =
            { "Reggie", "Slim", "Tony Two-Times", "Mickey", "Vinnie", "Dutch", "Smalls" };
    }
}
