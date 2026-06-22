# RUGS! — Architecture

How the mod is put together and which game systems it leans on. For the *general* (project-agnostic)
Big Ambitions modding reference (and a buildable starter template), see the bundled
[Code-Only Modding Guide](../guide/README.md).

## Guiding approach

- **Code-only.** No Unity Editor, no AssetBundles. Built as a single C# class library against the
  game's own DLLs and shipped as one `RugsMod.dll`.
- **Reuse the game's assets & systems.** Rugs are *clones* of vanilla items (for icons/models);
  money, cargo, NPCs, and input all go through existing game APIs.
- **Own the UI.** The trade UI is built at runtime with uGUI (the game's shared dialog panel is
  phone-styled and not reskinnable — see Gotchas).

## Source files & responsibilities

| File | Responsibility |
|------|----------------|
| `RugsMod.cs` | `[ModEntryOnCityLoad]` entry. Subscribes to `UnityLifecycleProvider.OnUpdate`; once the item catalog is ready, registers rugs + market; ticks the market off the in-game day; handles dev hotkeys. |
| `RugCatalog.cs` | Static list of `RugDef` (key `ba:itemname_<rug>`, display name, clone donor item, base price). Single source of truth for "what rugs exist." |
| `RugItems.cs` | Clones each donor item, strips it to carry-only contraband, registers via `ItemsGetter.RegisterModItem`, and scrubs stale rug demand-market entries. |
| `RugMarket.cs` | Global price per rug; mean-reverting daily random walk; persisted to `RUGS_market.json`. |
| `RugDealerController.cs` | `EntityController` subclass = the dealer NPC. Spawns a `ThirdPersonCharacter`, makes itself clickable, rolls a disjoint sell/buy mix with fluctuating constants, opens the panel on interact. |
| `RugTrading.cs` | Price math (`market × constant`), the single-bag carry model, and buy/sell execution (money + cargo). |
| `RugDealUI.cs` | The runtime uGUI panel: title, live Cash/Carrying lines, "HE'S SELLING"/"HE'S BUYING" rows, quantity buttons, status, Leave; input blocking. |
| `RugSpotCapture.cs` | Dev tool: F7 records a dealer spot to `RUGS_dealer_spots.txt`; F6 clears it. |

## Data flows

**Startup (per city load).** `RugsMod.OnLoadAsync` hooks `OnUpdate`. Each frame until ready it checks
`RugItems.CatalogReady()` (a known donor resolves via `ItemsGetter`); then it registers all rugs,
loads the market, and prints the hotkey help. Thereafter every frame it calls
`RugMarket.SyncDay(SaveGameManager.Current.Day)`.

**Item registration (`RugItems.RegisterOne`).** `Object.Instantiate(donor)` → override `itemName`
to the rug key → **B1**: `type = 0`, `isADemandedProduct = false`, wipe the inherited tag list
(`ClearTags`) → set `boxSize = 999999` (huge carry ceiling) → `BuildTagCache()` →
`ItemsGetter.RegisterModItem(clone)` (which re-adds only the `ba:itemtag_mod` marker).
`ScrubDemandMarket` then removes any leftover rug `ProductMarketEntry` from older saves.

**Market tick (`RugMarket`).** State = `{ key→price, lastDay }`. On a new day each price gets
`±8%` noise plus a `5%` pull toward its base, clamped to `[0.4×, 2.5×]` base. Saved as JSON to
`persistentDataPath`.

**Dealer spawn (`RugDealerController.Spawn`).** Build a GameObject with a `CapsuleCollider` (the
click target) + the controller; `RollInventory()` picks disjoint sell/buy subsets with constants
(sell `0.95–1.20×`, buy `0.80–1.10×` — overlapping so cross-dealer arbitrage is possible); spawn a
pinned `ThirdPersonCharacter` for visuals.

**Interaction.** The game's `MouseController` raycasts the interactive layer (the controller's
`Awake` sets it) and routes clicks into `EntityController` → our `Interact()` → `RugDealUI.Open`.

**Buy (`RugTrading.Buy`).** Clamp qty to bag room (`BagCeiling - held`), charge via
`GameManager.ChangeMoneySafe`, then either merge into the held bag (`HeldCargo().amount += qty` +
fire `OnItemsInCargoUpdated`) or create a new bag via `ItemHelper.InitializeItemInHandsWithCargo`.

**Sell (`RugTrading.SellAll`).** If the carried bag is this rug, credit `unit × amount` and clear
the hands (`PlayerHelper.ItemInstanceInHands = null`).

## Game integration points

Class/method names are stable; line numbers are decompile hints for **build 3537** and will drift.

- **Mod API** (`BigAmbitions.ModAPI.dll`, ns `BAModAPI`): `RegisterModClass`, `ModEntryOnCityLoad`,
  `ModBigAmbitionsBase`, `ModContext`, `IModLogger`, `BAModAPI.Services.UnityLifecycleProvider`.
- **Items/catalog** (`BigAmbitions.Items.dll`): `ItemsGetter.RegisterModItem/GetByName/AllItems`,
  `Item` (`itemName`, `type`, `boxSize`, `isADemandedProduct`), `ItemType`, `CargoInstance`,
  `ItemInstance` (`cargoInstances`, `OnItemsInCargoUpdated`), `GetMaxStockCapacity` (capacity =
  `rug.boxSize × box.cargoCapacityMultiplier`).
- **Carry/hands** (`BigAmbitions.dll`): `Helpers.PlayerHelper.ItemInstanceInHands` (setter
  spawns/despawns the held box), `ItemHelper.InitializeItemInHandsWithCargo` (wraps cargo in
  `ba:itemname_closedcardboardbox`).
- **NPC + interaction** (`BigAmbitions.dll`): `EntityController` (interaction base; `Awake` sets the
  clickable layer; we override `CanBeInteractedFromCurrentPosition` + `ShouldReactToIoEnter`),
  `Helpers.PrefabHelper.CreatePrefab<ThirdPersonCharacter>("Characters/HumanDefinitionLow")`,
  `ThirdPersonCharacter.ForceToTransform`. Reference impl: `SellerStandController`.
- **Money** (`BigAmbitions.dll`): `GameManager.ChangeMoneySafe(amount, TransactionInfo, …)`,
  `SaveGameManager.Current` (a `GameInstance`: `Money`, `Day`, `productMarketEntries`).
- **Neighborhoods** (`BigAmbitions.dll`): `Buildings.ClosestBuildingFromPlayer.Get().Neighbourhood`
  (district at the player's position; keys like `ba:neighborhood_midtown`).
- **Singletons**: `InstanceBehavior<T>.Instance` (in `HGExtensions.dll`).
- **Localization** (`HGPlugins.dll`, ns `Localizor`): item display key == `itemName`; mod `Locales\<lang>.json`
  is merged in. `.Localize()` / unknown keys render verbatim.

## Gotchas (learned the hard way)

- **Code-spawned `EntityController`s** have null `navMeshTargets`/`renderers` → `NullReferenceException`
  in the base `Start()`/highlight code. We seed them to empty arrays in `Awake` and skip `base.Start()`.
- **The base click flow** (`WalkOverAndInteract`) needs nav-mesh play spots we don't have, so clicks
  silently die. We override `CanBeInteractedFromCurrentPosition` with a plain distance check.
- **The game's dialog panel is phone-styled** and shared by all dialogs; `dialogType` only changes a
  button label, so it can't be reskinned. That's why the trade UI is a custom uGUI panel.
- **Exactly one DLL** may sit in the mod root. All game/Unity references use `<Private>false</Private>`
  so they aren't copied; docs/Locales don't count (only `*.dll`).
- **Rebuild = restart.** Loaded mod DLLs can't be hot-swapped.
- **Required Unity module refs** for the runtime UI: `UnityEngine.UIModule` (Canvas),
  `UnityEngine.UI` (Button/Image/Text/layout), `UnityEngine.TextRenderingModule` (Font),
  `UnityEngine.JSONSerializeModule` (JsonUtility), `UnityEngine.InputLegacyModule` (Input).
