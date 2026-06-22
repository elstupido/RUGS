using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;   // UnityLifecycleProvider
using BigAmbitions.Mods;   // OptionsService, ModOptions
using UnityEngine;

// Registers this class as the mod's entry point with the game's loader.
[assembly: RegisterModClass(typeof(ExampleMod.Mod))]

namespace ExampleMod
{
    /// <summary>
    /// A minimal, buildable starter mod. Demonstrates the essentials:
    /// load scope, lifecycle, logging, a settings panel, per-frame hooks, a hotkey,
    /// the money API, and a popup dialog. Delete what you don't need and build your own.
    ///
    /// Build:  dotnet build -c Release   (close the game first; relaunch to test)
    /// Then:   load a save and press F9.
    /// </summary>
    [ModEntryOnCityLoad]
    public sealed class Mod : ModBigAmbitionsBase
    {
        private IModLogger _log;
        private bool _bigGift;

        public override Task OnLoadAsync(ModContext ctx)
        {
            _log = ctx.Logger;
            _log.Info("ExampleMod loaded. Press F9 for a cash gift.");

            // A settings panel appears in the in-game mod menu.
            OptionsService.Register("examplemod", new ModOptions()
                .AddHeader("Example Mod")
                .AddToggle("biggift", "Bigger gift ($10,000)", false, on => _bigGift = on));

            UnityLifecycleProvider.OnUpdate += Tick;
            return Task.CompletedTask;
        }

        public override Task OnUnloadAsync()
        {
            UnityLifecycleProvider.OnUpdate -= Tick;
            OptionsService.RemoveModOptions("examplemod");
            _log?.Info("ExampleMod unloaded.");
            return Task.CompletedTask;
        }

        private void Tick()
        {
            if (Input.GetKeyDown(KeyCode.F9))
                Gift();
        }

        private void Gift()
        {
            float amount = _bigGift ? 10000f : 1000f;
            HudConfirm.Show(
                "Example Mod",
                $"Accept a gift of ${amount:N0}?",
                onConfirmAction: () =>
                {
                    var info = new TransactionInfo("examplemod_gift", "examplemod");
                    GameManager.ChangeMoneySafe(amount, info, null, null, force: false, showNotification: true);
                    _log.Info($"Gifted ${amount:N0}. Balance: ${SaveGameManager.Current.Money:N0}");
                },
                onCancelAction: null,
                confirmKey: "Yes please",
                cancelKey: "No thanks");
        }
    }
}
