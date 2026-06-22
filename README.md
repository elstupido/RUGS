# RUGS!

A parody black-market trading mod for **Big Ambitions**. It adds five fictional "rug" products
(**REED, ROCAINE, RETH, ROPE, RANEX**) and a network of street **rug dealers** you find around
the city, buy from, and sell to — riding a private, fluctuating rug market for profit.

> Everything here is fictional, satirical game content (drugs → "rugs"). No real-world anything.

**Status:** early development. The core loop works end-to-end. See [docs/ROADMAP.md](docs/ROADMAP.md).

---

## What it does today

- **5 contraband products**, created at runtime by cloning vanilla items for their art — no Unity,
  no AssetBundles.
- **Private rug market** — each rug has a global price that drifts every in-game day, persisted
  across sessions.
- **Street dealers** — clickable NPCs that open a custom "street deal" panel showing what they
  buy and sell at live, dealer-specific prices.
- **Bulk trading** — carry up to 9,999 of a rug in one bag; buy ×1/×10/×100/Max, Sell All.
- **Dealer-only economy** — rugs can't be sold in shops, ordered from wholesalers, or demanded by
  neighborhoods. The dealer network is the *only* market.

---

## Requirements

- **Big Ambitions** (Steam), Unity **2022.3.62f2**, Mono backend, build **3537 / EA 0.11**.
- **.NET SDK 9** (for `dotnet build`). No Unity Editor needed — this is a pure C# DLL mod.
- Windows (paths/build target assume the Steam install on this machine).

---

## Build & install

```sh
# from the project root:
dotnet build -c Release
```

The build **auto-deploys** the mod into the game's local mods folder (see the csproj `DeployMod`
target). **Close the game before building** — a running game locks the DLL.

- **Source project:** `D:\BigAmbitionsMods\RUGS\`
- **Installed mod:** `%USERPROFILE%\AppData\LocalLow\Hovgaard Games\Big Ambitions\ModsLocal\RUGS!\`
  - `RugsMod.dll` (the one and only DLL — the loader requires exactly one at the mod root)
  - `Locales\en.json` (display names)

The mod loads on **city load** (when you load/start a save). To pick up a new build you must fully
**quit and relaunch** — a loaded mod DLL can't be hot-swapped in a running process.

---

## Dev hotkeys (in-city)

| Key | Action |
|-----|--------|
| **F7** | Capture the current spot (position, facing, neighborhood) to `RUGS_dealer_spots.txt` and drop a preview dealer there. Used to author permanent dealer locations. |
| **F6** | Clear the captured-spots file. |
| **F8** | Spawn a one-off test dealer 2 m in front of you. |

These are temporary authoring/test tools and will be removed before any release build.

---

## Project layout

| File | Role |
|------|------|
| `RugsMod.cs` | Mod entry point; city-load init, daily market tick, dev hotkeys. |
| `RugCatalog.cs` | The five rug definitions (key, display name, clone donor, base price). |
| `RugItems.cs` | Registers rugs into the game item catalog as carry-only contraband. |
| `RugMarket.cs` | Global per-rug price with daily drift; JSON persistence. |
| `RugDealerController.cs` | The dealer NPC (spawn, click-to-interact, per-dealer buy/sell mix). |
| `RugTrading.cs` | Buy/sell execution, price math, the carried-bag model. |
| `RugDealUI.cs` | The custom runtime-built "street deal" uGUI panel. |
| `RugSpotCapture.cs` | F7/F6 dealer-location authoring tool. |
| `Locales/en.json` | Rug display-name localization. |
| `docs/` | [ARCHITECTURE.md](docs/ARCHITECTURE.md) (how it's built) · [ROADMAP.md](docs/ROADMAP.md) (status + decisions). |
| `guide/` | A standalone, reusable **[Code-Only Big Ambitions Modding Guide](guide/README.md)** + a buildable starter template + the decompiler tool. |

---

## Bundled modding guide

RUGS! is also the worked example for a general-purpose
**[Code-Only Big Ambitions Modding Guide](guide/README.md)** that lives in [`guide/`](guide). It
covers the decompile workflow, project setup, and copy-pasteable recipes (items, money, cargo, NPCs,
runtime UI…), plus a minimal buildable `ExampleMod` template and the decompiler tool. If you want to
make your *own* code-only mod, start there.

## License

[GNU GPL v3.0](LICENSE) — copyleft. The mod, the guide, and the template are all free to use, copy,
and build on; keep derivatives open under the same license.

Not affiliated with or endorsed by Hovgaard Games. Fictional, satirical content. Back up your saves.
