# Feature request: a tiny ModAPI hook to override action-panel button visibility (e.g. the Sell button) — so code-only mods don't need Harmony

**TL;DR:** Add one optional delegate to `BAModAPI.ModEvents` and route the action-panel's
button-visibility result through it. ~1 field + 3 lines. No new assembly references, no behaviour
change when unused. This lets code-only mods hide/show the **Sell** button (and ideally the other
action buttons) based on their own logic, which today is impossible without bundling Harmony.

---

## Who / what

I'm building a **code-only** C# mod against the official ModAPI (no Unity project, no AssetBundles —
just one DLL in `ModsLocal`, using `IModBigAmbitions` / `ItemsGetter.RegisterModItem` / `OptionsService`
/ `UnityLifecycleProvider`). The official API is great for this. The one wall I keep hitting is **game
UI**: I can read and even reach UI objects, but I can't make a visibility change *stick*.

## The specific problem (Sell button)

`UI.ItemPanel.ItemPanelUI` owns the action buttons. Visibility is decided in one place:

```csharp
private void UpdateButtonsVisibility()      // ItemPanelUI
{
    ...
    sellButton.gameObject.SetActive(
        (VehicleHelper.IsInsideVehicle() && vehicleInfo.isCommonVehicle && ...GetSellingPrice() > 0f)
        || (selectedItemInstance != null
            && !selectedItemInstance.ItemCached.HasTag(TagRef.Itemtag.cannotsell)
            && selectedItemInstance.cargoInstances.All(x => x.paid && !x.IsSealed)
            && !PlacementSystem.IsInPlacementMode
            && selectedItemInstance.ItemCached.canBeGrabbed)
        || (PlacementSystem.IsInPlacementMode && ...));
}
```

Two things make this impossible to override from a code-only mod:

1. **It re-asserts state constantly.** `UpdateButtonsVisibility()` runs on basically every panel
   refresh — `SetItemInstance(...)`, `SetVehicle(...)`, the `PlacementSystem.onPlacementModeEnd`
   subscription, and the Toggle/refresh paths. So even though `sellButton` is a public field and a mod
   *can* call `SetActive(false)`, the next refresh immediately overwrites it. A mod can't win that race
   without running `SetActive(false)` every frame, which flickers and clobbers the button for *all*
   items, not just ours.

2. **The decision point is private and non-virtual.** There's no event, no virtual method, and no
   `ModEvents` hook for "the action panel just recomputed its buttons." So the only way to influence
   the *result* of `UpdateButtonsVisibility()` is to intercept the method itself — i.e. **Harmony**.

### Yes, the `cannotsell` tag exists — and it's not enough

I know the supported lever: tag an item `cannotsell` and the expression above goes false for that item.
I already use this (my contraband lives in a `cannotsell` container), and it works *for the static,
per-item case*. But it only covers "this item is permanently unsellable." It can't express:

- **Dynamic / game-state rules** — e.g. "hide Sell while a mod event is active," or based on location,
  time, player state. The tag is baked item data; toggling it live means mutating tags on shared items
  globally, which is exactly the kind of hack the official API is supposed to spare us.
- **Context the tag doesn't see** — e.g. the vehicle-sell branch, or "show normally but hide in my
  custom flow."

So for anything beyond "make this item-type unsellable forever," there is currently **no path except
Harmony**. Harmony works, but pulling a full runtime IL-patcher into a one-line UI tweak is heavy,
fragile across game updates (transpilers especially), and pushes simple mods off the official rails.

## The ask (minimal)

Let mods participate in the decision the game already makes. Smallest possible version, matching the
existing `ModEvents` convention (it currently holds `onModsLoaded` / `onModsUnloaded`):

**1. In `BAModAPI.ModEvents` (BigAmbitions.ModAPI.dll), add one field:**

```csharp
// Optional. Lets a code-only mod veto/force the Sell button's visibility.
// Receives the value the game just computed; returns the value to actually apply.
// Called every time the item panel refreshes. Null => unchanged vanilla behaviour.
public static Func<bool, bool> filterSellButtonVisibility;
```

`bool`-only signature on purpose — **no new assembly references** for ModAPI.dll. The mod reads
whatever it needs (held item, etc.) from the game APIs it already references.

**2. In `ItemPanelUI.UpdateButtonsVisibility()`, route the result through it:**

```csharp
bool showSell = /* the existing expression, unchanged */;
if (BAModAPI.ModEvents.filterSellButtonVisibility != null)
    showSell = BAModAPI.ModEvents.filterSellButtonVisibility(showSell);
sellButton.gameObject.SetActive(showSell);
```

That's it. Because it sits inside the method that *already* re-runs on every refresh, the mod's decision
sticks automatically — same reason the `cannotsell` tag is robust today — with zero per-frame polling
on our side.

### How a mod would use it (consumer side)

```csharp
ModEvents.filterSellButtonVisibility = computed =>
{
    if (MyMod.CrackdownActive) return false;   // dynamic rule the tag can't express
    return computed;                            // otherwise leave vanilla behaviour alone
};
```

## Optional upgrades (your call, not required)

- **Pass the held item** for nicer ergonomics: `Func<ItemInstance, bool, bool>`. Slightly richer, but
  it would add a `BigAmbitions.Items` reference to ModAPI.dll — hence why the `bool`-only version above
  is the primary ask.
- **Generalise to all action buttons.** `UpdateButtonsVisibility()` drives `placeButton`, `grabButton`,
  `discardButton`, `sellButton`, etc. the same way. A single hook keyed by a small enum would cover all
  of them: `public static Func<ActionButton, bool, bool> filterActionButtonVisibility;`. Same pattern,
  one hook, broader payoff.

## Why this is safe

- No behaviour change unless a mod assigns the delegate.
- No new dependencies for the `bool` version.
- Evaluated where the game already evaluates — no new call sites, no lifecycle surprises.
- Keeps simple UI mods on the official API instead of forcing Harmony into the ecosystem.

Thanks for the moddable build — happy to test a hook build or adjust the proposed signature.
