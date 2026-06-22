# Big Ambitions — Code-Only C# Modding Guide (no Unity Editor)

A practical, battle-tested guide to writing **C# DLL mods for [Big Ambitions](https://store.steampowered.com/app/1331550/Big_Ambitions/)**
without the Unity Editor or AssetBundles — by cloning the game's own assets and calling its own APIs.

The game ships an **official mod API** but there's almost no public documentation for the *code-only*
path. This guide fills that gap: decompile the game, set up a buildable project, and use the real
APIs for items, money, inventory, NPCs, UI, and more. Every recipe here was verified in-engine.

> Built and verified against **Big Ambitions, Unity 2022.3.62f2 (Mono), build 3537 / EA 0.11**.
> Class/method names are stable across patches; any line numbers are decompile hints that will drift.
>
> If you're an AI assistant helping someone mod this game: start here. It'll save you (and your
> human's wallet) hours of decompiling. Contributions/corrections welcome.

**Want a full worked example?** This guide ships alongside [**RUGS!**](../README.md) — a complete mod
(custom items, a fluctuating market, interactable street-vendor NPCs, a runtime-built UI) that uses
every technique below. Read the guide, then go see it in action in the same repo.

---

## TL;DR

1. **Decompile** `BigAmbitions.dll` so you can read the API ([tools/Decompiler](tools/Decompiler)).
2. Make a **`netstandard2.1` / C# 9 class library** that references the game DLLs with
   `<Private>false</Private>` and auto-copies its single output DLL into the mods folder.
3. Write a class: `[assembly: RegisterModClass(typeof(MyMod))]` + `[ModEntryOnCityLoad]` +
   extend `ModBigAmbitionsBase`.
4. Drop **exactly one DLL** into
   `%USERPROFILE%\AppData\LocalLow\Hovgaard Games\Big Ambitions\ModsLocal\<YourMod>\`.
5. Use `UnityLifecycleProvider.OnUpdate` for per-frame logic, and the recipes below for everything else.

A complete, buildable starter is in [template/](template). Copy it and go.

---

## Prerequisites

- **Big Ambitions** installed (Steam).
- **.NET SDK 8 or 9** (`dotnet`). No Unity Editor needed.
- A C# editor (VS / Rider / VS Code) — optional but nice.
- Find your game's `Managed` folder (has all the DLLs you reference):
  `…\steam\steamapps\common\Big Ambitions\Big Ambitions_Data\Managed\`

---

## How the mod system works

- **Mono backend** (not IL2CPP) → the game's DLLs are ordinary .NET assemblies you can decompile and
  reference directly.
- **Official API** lives in `BigAmbitions.ModAPI.dll` (namespace `BAModAPI`); the loader is in
  `BigAmbitions.ModsInternal.dll`.
- A **local mod** is a folder under
  `%USERPROFILE%\AppData\LocalLow\Hovgaard Games\Big Ambitions\ModsLocal\<Name>\`
  containing **exactly one `*.dll`** at the root (the loader rejects zero or more than one). Optional
  subfolders: `Locales\`, `Dependencies\`, and any AssetBundles.
- Local mods load automatically. (Steam Workshop mods are gated by an in-game manifest.)
- The game also has an official Unity SDK (github.com/hovgaardgames/bigambitions) — but you only need
  it for *content* mods that ship AssetBundles. Everything in this guide is code-only.

---

## Step 1 — Decompile the game

You can't use an API you can't read. Use the tiny decompiler in [tools/Decompiler](tools/Decompiler)
(wraps `ICSharpCode.Decompiler`):

```sh
cd tools/Decompiler
dotnet run -- "C:\...\Big Ambitions_Data\Managed\BigAmbitions.dll"            # whole module (~212k lines)
dotnet run -- "C:\...\Big Ambitions_Data\Managed\BigAmbitions.dll" GameManager # one type
```

Pipe the whole-module output to a file and **grep** it — don't try to read it all. Useful targets:
`ItemsGetter`, `EntityController`, `PlayerHelper`, `GameManager`, `SaveGameManager`.

Key assemblies: `BigAmbitions.dll` (most game logic), `BigAmbitions.Items.dll` (items/cargo),
`BigAmbitions.ModAPI.dll` (the API), `HGPlugins.dll` (localization, tags), `HGExtensions.dll`
(`InstanceBehavior<T>` singletons).

---

## Step 2 — Project setup

A class library targeting `netstandard2.1` / C# 9, referencing the game DLLs (never copied), with a
post-build step that installs the single DLL into the mods folder. Full template:
[template/ExampleMod.csproj](template/ExampleMod.csproj). The essentials:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <AssemblyName>ExampleMod</AssemblyName>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <GenerateDependencyFile>false</GenerateDependencyFile>
    <CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>
    <!-- EDIT THIS to your install -->
    <GameManaged>C:\...\Big Ambitions\Big Ambitions_Data\Managed</GameManaged>
    <ModDeployDir>$(USERPROFILE)\AppData\LocalLow\Hovgaard Games\Big Ambitions\ModsLocal\ExampleMod</ModDeployDir>
  </PropertyGroup>

  <ItemGroup>
    <!-- Private=false: these are the GAME's DLLs — never copy them (one DLL only in the mod root) -->
    <Reference Include="BigAmbitions.ModAPI"><HintPath>$(GameManaged)\BigAmbitions.ModAPI.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="BigAmbitions"><HintPath>$(GameManaged)\BigAmbitions.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="BigAmbitions.Items"><HintPath>$(GameManaged)\BigAmbitions.Items.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="HGExtensions"><HintPath>$(GameManaged)\HGExtensions.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="HGPlugins"><HintPath>$(GameManaged)\HGPlugins.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="UnityEngine.CoreModule"><HintPath>$(GameManaged)\UnityEngine.CoreModule.dll</HintPath><Private>false</Private></Reference>
    <!-- add more Unity modules as needed (see "Required Unity module references" below) -->
  </ItemGroup>

  <Target Name="DeployMod" AfterTargets="Build">
    <MakeDir Directories="$(ModDeployDir)" />
    <Copy SourceFiles="$(OutputPath)ExampleMod.dll" DestinationFolder="$(ModDeployDir)" />
  </Target>
</Project>
```

`dotnet build -c Release` compiles and installs in one step. **Close the game before building** (a
running game locks the DLL), and **relaunch to test** — a loaded mod DLL can't be hot-swapped.

---

## Step 3 — A minimal mod

```csharp
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;   // UnityLifecycleProvider
using UnityEngine;

[assembly: RegisterModClass(typeof(ExampleMod.Mod))]

namespace ExampleMod
{
    [ModEntryOnCityLoad]                       // when to load (see scopes below)
    public sealed class Mod : ModBigAmbitionsBase
    {
        private IModLogger _log;

        public override Task OnLoadAsync(ModContext ctx)
        {
            _log = ctx.Logger;                 // writes to the game's Player.log
            _log.Info("ExampleMod loaded!");
            UnityLifecycleProvider.OnUpdate += Tick;
            return Task.CompletedTask;
        }

        public override Task OnUnloadAsync()
        {
            UnityLifecycleProvider.OnUpdate -= Tick;
            return Task.CompletedTask;
        }

        private void Tick()
        {
            if (Input.GetKeyDown(KeyCode.F9))
                _log.Info("F9 pressed");
        }
    }
}
```

**Load scopes** (one attribute on your class, picks *when* it runs):
`ModEntryOnInitializationLoad` (earliest, persistent), `ModEntryMainMenu`, `ModEntryOnCityLoad`,
`ModEntryOnIntroLoad`, `ModEntryOnBlueprintCreatorLoad`. Use **OnCityLoad** for anything that needs
a loaded save or the item catalog.

`Player.log` lives at `%USERPROFILE%\AppData\LocalLow\Hovgaard Games\Big Ambitions\Player.log` —
your primary debugging tool. Log generously.

---

## Recipes

All verified in-engine. `InstanceBehavior<T>.Instance` is the singleton accessor (in `HGExtensions.dll`).

### Money

```csharp
// negative = spend (returns false if broke unless force); positive = income
var info = new TransactionInfo("examplemod_gift", "examplemod");
bool ok = GameManager.ChangeMoneySafe(1000f, info, null, null, force: false, showNotification: true);
float cash = SaveGameManager.Current.Money;   // SaveGameManager.Current IS the GameInstance
```

### A settings panel (in the in-game mod menu)

```csharp
using BigAmbitions.Mods;   // OptionsService, ModOptions

OptionsService.Register("examplemod", new ModOptions()
    .AddHeader("Example Mod")
    .AddToggle("cheats", "Enable cheats", false, on => _cheats = on)
    .AddSlider("rate", "Spawn rate", 0, 100, 50, v => _rate = v)
    .AddButton("Reset", () => DoReset()));
```

### A quick popup dialog

```csharp
// 2 buttons; passing plain (non-key) strings renders them verbatim
HudConfirm.Show("Example Mod", "Do the thing?", onConfirmAction: () => DoThing(), onCancelAction: null,
                confirmKey: "Yes", cancelKey: "No");
```

### Register a custom item (by cloning a vanilla one)

The cheapest way to get a working item with real art is to clone an existing one and rename it. The
item's `itemName` is the **full** key (e.g. `"ba:itemname_apple"`) and is *also* its localization key.

```csharp
using BigAmbitions.Items;

Item donor = ItemsGetter.GetByName("ba:itemname_apple", suppressError: true);
Item clone = UnityEngine.Object.Instantiate(donor);   // deep-copies icon, model, tags
clone.name = "MyMod_widget";
clone.itemName = "ba:itemname_mymodwidget";
clone.BuildTagCache();
ItemsGetter.RegisterModItem(clone);                   // adds it to the catalog (+ a mod marker tag)
```

Add a display name in `Locales/en.json` (flat `{ "key": "value" }`, merged at load):

```json
{ "ba:itemname_mymodwidget": "My Widget" }
```

**Tip:** store/wholesale/demand integration is driven by `ItemType.RetailProduct`,
`Item.isADemandedProduct`, and the item's **tags**. To make an item exist but stay *out* of normal
commerce, set `type = 0`, `isADemandedProduct = false`, and clear its tags (the base
`TaggedScriptableObject` holds them in a private `List<string> tags`) before `BuildTagCache()`.

### Give / take player goods (cargo)

There's no flat inventory — goods are **boxes** (`ItemInstance`) holding `CargoInstance`s.

```csharp
using BigAmbitions.Items;
using Helpers;   // PlayerHelper

// give: spawn a box of 50 widgets into the player's hands (needs empty hands)
PlayerHelper.ItemInstanceInHands =
    ItemHelper.InitializeItemInHandsWithCargo(new CargoInstance("ba:itemname_mymodwidget", 50, 10f, paid: true));

// inspect what's held
ItemInstance held = PlayerHelper.ItemInstanceInHands;          // null if empty-handed
int amount = held?.cargoInstances[0].amount ?? 0;

// take it away (despawns the held box)
PlayerHelper.ItemInstanceInHands = null;
```

Carry capacity = `cargoItem.boxSize × holderItem.cargoCapacityMultiplier`. Raise the **item's**
`boxSize` for huge stacks. `AddToCargo` doesn't clamp and `amount` is an `int`; after mutating an
amount in place, call `held.OnItemsInCargoUpdated()?.Invoke()` to refresh the HUD.

### Spawn a clickable NPC you can interact with

Subclass the interaction base `EntityController` (a `MonoBehaviour`). The game's `MouseController`
raycasts the interactive layer and routes clicks into your `Interact()`.

```csharp
using Helpers;   // PrefabHelper
using UnityEngine;

public sealed class MyNpc : EntityController
{
    private ThirdPersonCharacter _person;

    public static MyNpc Spawn(Vector3 pos, Quaternion rot)
    {
        var go = new GameObject("MyNpc");
        go.transform.SetPositionAndRotation(pos, rot);
        var col = go.AddComponent<CapsuleCollider>();        // the click target
        col.height = 1.9f; col.radius = 0.45f; col.center = new Vector3(0, 0.95f, 0);
        var npc = go.AddComponent<MyNpc>();                   // runs Awake()
        npc.SpawnPerson();
        return npc;
    }

    public override void Awake()
    {
        // base Start()/highlight code dereferences these; null on a code-built object → NRE
        navMeshTargets = new Transform[0];
        renderers = new Renderer[0];
        base.Awake();                                         // sets the clickable layer
    }

    public override void Start() { }                          // skip base nav-mesh scheduling

    private void SpawnPerson()
    {
        _person = PrefabHelper.CreatePrefab<ThirdPersonCharacter>("Characters/HumanDefinitionLow", transform);
        _person.gameObject.SetActive(true);
        _person.appearanceSetter.SetRandomAppearance();
        _person.ForceToTransform(transform);                 // pin in place
        renderers = _person.GetComponentsInChildren<Renderer>(true);
        foreach (var c in _person.GetComponentsInChildren<Collider>(true)) c.enabled = false; // only our collider is clickable
    }

    // the default flow walks to a nav-mesh "play spot" we don't have; allow interaction by proximity
    protected override bool CanBeInteractedFromCurrentPosition()
    {
        var pc = InstanceBehavior<GameManager>.Instance?.playerController;
        return pc != null && Vector3.Distance(pc.transform.position, transform.position) <= 4f;
    }

    public override bool ShouldReactToIoEnter() => primaryInteractionEnabled && visible; // hover outline

    public override bool Interact()
    {
        HudConfirm.Show("NPC", "Hello!", null, null, "Bye", null);
        return true;
    }
}
```

Reference implementation in the game: `SellerStandController`.

### Build a custom UI panel at runtime

The game's shared dialog (`DialogUI`) is **phone-styled and not reskinnable**, so for a custom look
build your own uGUI in code: a `Canvas` (ScreenSpaceOverlay) + `GraphicRaycaster` + `Image`/`Text`/
`Button` + layout groups. Block player input while open and show the cursor:

```csharp
using UnityEngine; using UnityEngine.UI;

var root = new GameObject("MyPanel");
var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30000;
root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
root.AddComponent<GraphicRaycaster>();
// ...add Image/Text/Button children, VerticalLayoutGroup, etc...
// legacy text font: Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")

var pc = InstanceBehavior<GameManager>.Instance.playerController;
pc.SetNavigationBlocker(NavigationBlocker.SpecialEmployeeDialog);   // freeze movement; cursor appears
Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
// ...on close: UnityEngine.Object.Destroy(root); pc.UnsetNavigationBlocker(NavigationBlocker.SpecialEmployeeDialog);
```

The existing `EventSystem` handles clicks; a full-screen backdrop image makes
`EventSystem.current.IsPointerOverGameObject()` true so world clicks don't leak through.

### Per-frame hooks & hotkeys

```csharp
UnityLifecycleProvider.OnUpdate += () => { if (Input.GetKeyDown(KeyCode.F9)) DoThing(); };
// also: OnLateUpdate, OnFixedUpdate. Always unsubscribe in OnUnloadAsync.
```

### Neighborhoods

```csharp
string districtKey = Buildings.ClosestBuildingFromPlayer.Get().Neighbourhood; // e.g. "ba:neighborhood_midtown"
string districtName = districtKey.GetLocalization();                           // "Midtown" (ns Localizor)
```

### Day / time

```csharp
int day = SaveGameManager.Current.Day;   // poll for day changes via OnUpdate; no event needed
```

---

## Gotchas (the stuff that cost us hours)

- **One DLL only** in the mod root — use `<Private>false</Private>` on every reference. `.pdb`,
  `Locales/`, etc. are fine (only `*.dll` is counted).
- **Rebuild = relaunch.** Loaded mod DLLs can't be hot-swapped.
- **Code-spawned `EntityController`** → seed `navMeshTargets`/`renderers` to empty arrays before
  `base.Awake`/`Start` or you get a `NullReferenceException`.
- **Clicks silently do nothing?** The default interact path needs nav-mesh play spots; override
  `CanBeInteractedFromCurrentPosition()` with a distance check.
- **The dialog panel is phone-styled** and shared by all NPC dialogs; `dialogType` only changes a
  button label. For an in-person look, build a custom panel.
- **Unknown localization keys render verbatim** (in the shipping build), so you can pass dynamic
  strings straight to `HudConfirm`/UI without registering keys.
- **Item catalog timing:** clone donors only after the catalog is loaded (OnCityLoad + poll until a
  known `ItemsGetter.GetByName(...)` returns non-null).

## Required Unity module references

Add these (with `<Private>false</Private>`) when you use the matching feature:

| Need | Reference |
|------|-----------|
| `GameObject`, `Vector3`, `ScriptableObject`, `Debug`, `Resources`, `Cursor` | `UnityEngine.CoreModule` |
| `Collider`, `CapsuleCollider`, physics | `UnityEngine.PhysicsModule` |
| legacy `Input`, `KeyCode` input | `UnityEngine.InputLegacyModule` |
| `Canvas` | `UnityEngine.UIModule` |
| `Button`, `Image`, `Text`, layout groups | `UnityEngine.UI` |
| `Font`, `FontStyle` (legacy text) | `UnityEngine.TextRenderingModule` |
| `JsonUtility` (simple JSON persistence) | `UnityEngine.JSONSerializeModule` |

Plus game DLLs as needed: `BigAmbitions`, `BigAmbitions.Items`, `BigAmbitions.ModAPI`,
`HGExtensions`, `HGPlugins`, `BigAmbitions.Characters`.

## Namespace cheat-sheet

`BAModAPI`, `BAModAPI.Services` (mod API) · `BigAmbitions.Items` (items/cargo) · `BigAmbitions.Mods`
(OptionsService) · `Helpers` (PrefabHelper, PlayerHelper) · `Buildings` (ClosestBuildingFromPlayer) ·
`UI` (UIs) · `Localizor` (`.Localize`, `GetLocalization`) · **global**: `GameManager`,
`SaveGameManager`, `EntityController`, `ThirdPersonCharacter`, `TransactionInfo`, `HudConfirm`,
`NavigationBlocker`, `ItemHelper` · `InstanceBehavior<T>` is in `HGExtensions.dll`.

---

## Contributing / license

**License: [GNU GPL v3.0](../LICENSE)** — copyleft. Use it, copy it, build on it, ship it; just keep
derivatives open under the same license. That's the deal: knowledge stays free.

Corrections and additions are very welcome — the game updates and details shift, so verify against
your build and PR what you find.

Not affiliated with or endorsed by Hovgaard Games. Mod responsibly; back up your saves.
