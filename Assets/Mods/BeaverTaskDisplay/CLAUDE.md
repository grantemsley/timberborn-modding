# BeaverTaskDisplay mod

A Timberborn 1.0 mod that adds a task description row to the entity info panel, positioned directly below the existing "Carrying" section. Shows what the entity is currently doing and (when walking to a destination) a clickable destination name. Click the destination to jump the camera to it.

Works for any entity with both `BehaviorManager` and `Walker` components — beavers, bots, golems.

---

## File structure

```
Assets/Mods/BeaverTaskDisplay/
├── manifest.json                              # mod metadata; Id is "grantemsley.BeaverTaskDisplay"
├── Data/                                      # copied verbatim to built mod root
│   └── Localizations/
│       └── enUS_BeaverTaskDisplay.csv
└── Scripts/
    ├── grantemsley.BeaverTaskDisplay.asmdef   # assembly definition
    ├── BeaverTaskConfigurator.cs              # Bindito DI registration
    └── BeaverTaskFragment.cs                  # main logic + reflection helpers
```

The `Data/` folder convention is from the Mod-Builder docs: contents of `Data/` are copied as-is to the built mod's root folder. So `Data/Localizations/enUS_*.csv` ends up at `Localizations/enUS_*.csv` in the built mod, which is where Timberborn's loc service expects to find them.

Built mods land in `Documents/Timberborn/Mods/<ModId>/`.

---

## Build

In Unity (project at `d:\claude\timberborn-modding`):

1. **Timberborn → Show Mod Builder** menu
2. Tick "Beaver Task Display"
3. Click **Build all**

Unity version is **6000.0.16f1** per the modding repo. The `.csproj` rsp shows `-langversion:9.0` and the IL2CPP `UNITY_6000_3_6` define — modern C# features (`is not null`, target-typed `new`, switch expressions) are fine.

---

## Architecture

### `BeaverTaskConfigurator`

Standard Timberborn DI pattern (Bindito):

```csharp
[Context("Game")]
public class BeaverTaskConfigurator : Configurator {
  protected override void Configure() {
    Bind<BeaverTaskFragment>().AsSingleton();
    MultiBind<EntityPanelModule>().ToProvider<EntityPanelModuleProvider>().AsSingleton();
  }
  private class EntityPanelModuleProvider : IProvider<EntityPanelModule> {
    // ...constructs EntityPanelModule.Builder, calls AddBottomFragment(_fragment, 100), returns Build()
  }
}
```

`AddBottomFragment(_, 100)` puts our row just below CarryingUI's `GoodCarrierFragment`, which uses order **0** in the same Bottom region (verified by reading the IL of `Timberborn.CarryingUI.CarryingUIConfigurator.EntityPanelModuleProvider.Get`).

### `BeaverTaskFragment`

Implements `IEntityPanelFragment`. The interface contract:

| Method | When called | Our implementation |
|---|---|---|
| `InitializeFragment()` | Once at startup; returns the root `VisualElement` | Builds the NineSlice container with a task `Label` and destination `Label` |
| `ShowFragment(BaseComponent entity)` | When the user selects an entity | Caches `BehaviorManager` + checks for `Walker`; shows panel only if both exist |
| `UpdateFragment()` | Every frame the panel is shown | Re-reads task and destination, refreshes labels and highlight |
| `ClearFragment()` | When the panel is closed/another entity is selected | Clears state, hides the panel, removes destination highlight |

### Task text resolution

Task text is determined by a two-level lookup: **executor type** (the leaf action) and **behavior type** (the high-level intent).

**Walking executors** (`WalkToAccessibleExecutor`, `WalkToReservableExecutor`, `WalkInsideExecutor`) check `RunningBehavior.Name` first to show intent rather than a generic "Walking to":
- `CarryRootBehavior` / `HaulWorkplaceBehavior` → "Hauling from" or "Hauling to" (based on `GoodCarrier.IsCarrying`)
- `SleepNeedBehavior` → "Walking to sleep at"
- `InventoryNeedBehavior` → "Walking to eat at"
- `AttractionNeedBehavior` → "Walking to visit"
- `ProduceWorkplaceBehavior` / `LaborWorkplaceBehavior` / etc. → "Walking to work at"
- `BuildBehavior` → "Walking to build at"
- Unknown behavior → falls back to "Walking to" / "Entering" from `ExecutorLocKeys`

**Position-walk executors** (`WalkToPositionExecutor`) also check behavior:
- `PlantBehavior` / `PlanterWorkplaceBehavior` → "Walking to plant" (no entity destination)
- `SleepNeedBehavior` → "Walking to sleep"
- Unknown → "Walking"

**`ApplyEffectExecutor`** (eating, drinking, sleeping, etc.) uses a three-tier fallback:
1. `_animationName` private field → `AnimationLocKeys` (most specific)
2. `_effects[].NeedId` → `NeedIdLocKeys` (slot-based buildings like medical bed have no animation)
3. `_effects[0].NeedId` → `FactionNeedService.GetBeaverOrBotNeedById` (game's own display name for unmapped needs like "Lido", "WindTunnel")

**Null executor** (between executors): checks `BehaviorOnlyLocKeys` for behaviors like `EmptyOutputWorkplaceBehavior` and `FillInputWorkplaceBehavior`.

**All other executors** fall through to `ExecutorLocKeys` (Building, Planting, Harvesting, etc.).

### Destination highlight

When a destination entity is shown, `Highlighter.HighlightSecondary` applies a dark orange highlight to it. The highlight updates only when the destination changes, and is cleared on deselect via `UnhighlightAllSecondary`.

**Known limitation:** When the destination is the beaver's home or workplace, `RelationHighlighter` also applies a blue primary highlight to the same building, which visually overrides our secondary orange. The task text still correctly identifies the destination; only the highlight is affected. This is a fundamental limitation of the highlight system's layering — `RelationHighlighter` re-applies its highlight via primary tier and cannot be overridden from a secondary context.

### Behavior name discovery

Behavior class names (`CarryRootBehavior`, `PlantBehavior`, etc.) are Timberborn internals that can only be discovered by DLL inspection or runtime logging. The `BeaverTaskFragment.cs` file contains a commented-out `BeaverTaskScanner` class that can be re-enabled to log new combinations. See the comment in `InitializeFragment` for instructions.

---

## Game internals discovered (the important reference table)

These are all derived from decompiling DLLs in `Assets/Plugins/Timberborn/`. Keeping this here so future iterations don't have to re-derive everything.

### BehaviorManager

`Timberborn.BehaviorSystem.BehaviorManager` is a `TickableComponent` on every beaver/bot.

**Public API:**
- `RunningExecutor` — returns `ExecutorInfo` struct with `Name` (string) and `ElapsedTime`. `Name` is `executor.GetType().Name` — a raw class name.
- `RunningBehavior` — returns `BehaviorInfo` struct with `Name`. Behaviors are higher-level; e.g. `"CarryRootBehavior"` while the executor is `"WalkToAccessibleExecutor"`.
- `IsRunningBehavior<TBehavior>()` and `IsRunningExecutor<TExecutor>()` — **both are generic** with no non-generic overload. Don't use these; check `string.IsNullOrEmpty(RunningExecutor.Name)` instead.

**Private (read via reflection):**
- `_runningExecutor` — the actual `IExecutor` instance. Required to inspect walking executor destinations and `ApplyEffectExecutor` fields.

### Walking executors

| Executor | DLL | Private field | Field type |
|---|---|---|---|
| `WalkToAccessibleExecutor` | WalkingSystem | `_accessible` | `Accessible` (BaseComponent) |
| `WalkInsideExecutor` | WalkingSystem | `_buildingAccessible` | `Accessible` (BaseComponent) |
| `WalkToReservableExecutor` | ReservableSystem | `_reservable` | `Reservable` (BaseComponent) |
| `WalkToPositionExecutor` | WalkingSystem | (no entity) | Vector3 position only |

### Behavior names (confirmed via runtime logging)

| Behavior | Seen with executor | Meaning |
|---|---|---|
| `CarryRootBehavior` | WalkToAccessibleExecutor | Hauling goods |
| `PlantBehavior` | WalkToPositionExecutor, PlantExecutor | Planting |
| `PlanterWorkplaceBehavior` | WalkToReservableExecutor | Walking to planting zone |
| `YieldRemoverBehavior` | WalkToReservableExecutor, RemoveYieldExecutor | Harvesting/logging |
| `BuildBehavior` | WalkToAccessibleExecutor, BuildExecutor | Building |
| `SleepNeedBehavior` | WalkInsideExecutor, WalkToPositionExecutor, ApplyEffectExecutor | Sleeping |
| `InventoryNeedBehavior` | WalkInsideExecutor | Eating/drinking from building |
| `AttractionNeedBehavior` | WalkInsideExecutor, ApplyEffectExecutor | Using an attraction |
| `ProduceWorkplaceBehavior` | WalkInsideExecutor, ProduceExecutor | Workshop production |
| `LaborWorkplaceBehavior` | WalkToReservableExecutor | Walking to workplace |
| `WaitInsideIdlyWorkplaceBehavior` | WalkInsideExecutor, WaitExecutor | Idling inside |
| `WanderRootBehavior` | WalkToPositionExecutor, WaitExecutor | Wandering |
| `EmptyOutputWorkplaceBehavior` | (null executor) | Emptying workshop output |
| `FillInputWorkplaceBehavior` | (null executor) | Filling workshop input |

### ApplyEffectExecutor

Handles all need-satisfaction activities. Private fields read via reflection:
- `_animationName` — set by `TurnOnAnimation`; null for slot-based buildings (e.g. medical bed uses `TransformSlot` animation, not executor animation)
- `_effects` — `IEnumerable<ContinuousEffect>` where `ContinuousEffect.NeedId` (string) identifies the need. NeedId values: `"Hunger"`, `"Thirst"`, `"Sleep"`, `"Injury"`, `"WetFur"`, `"Lido"`, `"MudBath"`, etc.

### GoodCarrier

`Timberborn.Carrying.GoodCarrier` — `IsCarrying` (bool) tells us whether the entity is currently holding goods. Used to distinguish "Hauling from" (empty) vs "Hauling to" (loaded).

### FactionNeedService

`Timberborn.GameFactionSystem.FactionNeedService.GetBeaverOrBotNeedById(string needId)` — returns `NeedSpec` which has `DisplayNameLocKey`. Used to show the game's own localized name for unmapped needs (attractions, etc.).

### RelationHighlighter

`Timberborn.RelationSystemUI.RelationHighlighter` applies a blue **primary** highlight to home/workplace when an entity is selected. It re-applies via `HighlightPrimary` on selection and relation-change events. Our secondary highlight cannot override it — see Known Limitations.

### Highlighter

`Timberborn.SelectionSystem.Highlighter` — injected as a transient (each injection gets its own instance). Key methods:
- `HighlightSecondary(BaseComponent, Color)` — adds a secondary highlight keyed to this instance
- `UnhighlightAllSecondary()` — removes all secondary highlights added by this instance
- `HighlightPrimary(BaseComponent, Color)` — adds a primary highlight (used by RelationHighlighter for blue; adding our own primary doesn't reliably override it due to render-order issues)

### Entity panel UI

- Root: `NineSliceVisualElement` with `entity-sub-panel` and `bg-sub-box--green` CSS classes
- Labels use `entity-panel__text` CSS class for correct font size (13px) — without it, the Medium-weight SDF font renders at the wrong size and appears heavier/bolder
- `GoodCarrierFragment` uses `NineSliceLabel` (element IS the label); we use `NineSliceVisualElement` + two `Label` children in a horizontal flex row

---

## Decisions and rationale

1. **Mod ID prefix `grantemsley.`** — recommended by Mod-directory-structure.md for global uniqueness.

2. **Show panel for `BehaviorManager` + `Walker`** — bots and golems also benefit; no reason to gate on a beaver-specific component.

3. **Two separate labels on one line** — task prefix in `_taskLabel`, destination name in `_destinationLabel`. Both inside a `flexDirection: Row, flexWrap: Wrap` container. Destination label is pale blue and clickable; task label is standard grey.

4. **Behavior-based walking context** — `RunningBehavior.Name` gives the high-level intent when walking. This avoids showing a generic "Walking to Large Water Pump" with no indication of why.

5. **`GoodCarrier.IsCarrying` for haul direction** — no public API exposes whether a haul walk is pickup vs delivery; the carrying state is the reliable proxy.

6. **Reflection on private fields** — the official modding tools build against unmodified game DLLs; private fields aren't accessible directly. All `FieldInfo` handles are cached as `static readonly` fields (cost paid once at type-init, not per frame). Lazily-initialized fields (ApplyEffectExecutor) are `static` non-readonly and initialized on first encounter.

7. **`ApplyEffectExecutor` three-tier fallback** — animation name → NeedId map → FactionNeedService → raw type name. Covers both animation-based buildings (food tables, baths) and slot-based buildings (medical bed).

8. **`AddBottomFragment(_, 100)`** — gives room for other mods to insert between us and CarryingUI's order 0.

9. **`entity-panel__text` CSS class on labels** — discovered via runtime debug (resolvedStyle.unityFontStyleAndWeight = Normal, fontSize = 13) that the "bold" appearance was actually the Medium SDF font rendering at the wrong size. Adding the game's own text class fixes it.

---

## Known limitations and risks

- **Reflection fragility** — game patches could rename `_runningExecutor`, `_accessible`, `_buildingAccessible`, `_reservable`, `_animationName`, or `_effects`. The fragment null-checks throughout so it degrades silently rather than crashing.

- **Destination highlight overridden by blue** — when the walking destination is the entity's home or workplace, `RelationHighlighter`'s blue primary highlight overrides our secondary orange. The task text is still correct; only the highlight is affected.

- **Behavior names may change** — behavior class names (`CarryRootBehavior`, `PlantBehavior`, etc.) are internal strings discovered via runtime logging. A game update could rename them; unknown behaviors fall through to the generic "Walking to" fallback.

- **English only** — only `enUS_BeaverTaskDisplay.csv`. Add other languages by creating `frFR_BeaverTaskDisplay.csv`, `deDE_BeaverTaskDisplay.csv`, etc. All `Walk.*` loc keys are prefix strings (building name appended after); translator comments in the CSV explain this.

- **No persistence or settings** — the panel always appears for entities with `BehaviorManager` + `Walker`.

- **Editor compile vs Mod Builder** — the Unity Editor compile must succeed before the Mod Builder runs. Check the Visual Studio Error List or Unity Console first if the build fails.

---

## Tooling and source references

### Where things live on disk

- **Modding repo (Unity project)**: `D:\claude\timberborn-modding`
- **Game DLLs**: `D:\claude\timberborn-modding\Assets\Plugins\Timberborn\Timberborn.*.dll` (~250 DLLs)
- **Decompiled game source**: `D:\claude\timberborn-decompiled\` — one subfolder per DLL, one .cs per type; use Grep/Read here instead of inspecting DLLs directly
- **Game files (USS, UXML, blueprints)**: `D:\claude\Timberborn\` — full game installation copy
- **Wiki (cloned)**: `D:\claude\timberborn-modding.wiki\` — `Coding basics.md`, `User-interface.md`, `Mod-directory-structure.md`, `Translations.md`, `Mod-Builder.md` most useful
- **Example mods**: `D:\claude\timberborn-modding\Assets\Mods\` — `HelloWorld` is the closest reference for fragments + DI

### How to inspect game DLLs

The Python `dnfile` package is fast for finding type/field/method names:

```python
import dnfile
pe = dnfile.dnPE("Timberborn.SomeAssembly.dll")
md = pe.net.mdtables
for row in md.TypeDef.rows:
    name = str(row.TypeName)
    for field_ref in row.FieldList:
        f = field_ref.row
        if f: print(f.Name)
```

For method bodies and IL, use **dnSpyEx**: `https://github.com/dnSpyEx/dnSpy/releases`.

USS/UXML files live in `D:\claude\Timberborn\` as part of the game installation (asset bundles), but the modding project's `Assets/Tools/ImportedAssets/Editor/Resources/UI/` contains `.uss.txt` and `.uxml.txt` copies of the editor-visible stylesheets — useful for understanding CSS classes.

### DLLs inspected during this project

- `Timberborn.BehaviorSystem.dll` — `BehaviorManager`, `IExecutor`, `ExecutorInfo`, `BehaviorInfo`
- `Timberborn.WalkingSystem.dll` — `Walker`, `WalkToAccessibleExecutor`, `WalkToPositionExecutor`, `WalkInsideExecutor`
- `Timberborn.NeedBehaviorSystem.dll` — `ApplyEffectExecutor`
- `Timberborn.ReservableSystem.dll` — `WalkToReservableExecutor`, `Reservable`
- `Timberborn.EntityPanelSystem.dll` — `EntityPanelModule.Builder`, `IEntityPanelFragment`
- `Timberborn.SelectionSystem.dll` — `EntitySelectionService`, `SelectableObject`, `SelectableObjectRetriever`, `Highlighter`, `HighlightableObject`
- `Timberborn.RelationSystemUI.dll` — `RelationHighlighter` (uses HighlightPrimary for blue relation tint)
- `Timberborn.EntityNaming.dll` — `NamedEntity`
- `Timberborn.CarryingUI.dll` — `GoodCarrierFragment` (reference for fragment positioning and NineSliceLabel pattern)
- `Timberborn.Carrying.dll` — `GoodCarrier` (`IsCarrying` bool)
- `Timberborn.GameFactionSystem.dll` — `FactionNeedService` (`GetBeaverOrBotNeedById`)
- `Timberborn.NeedSpecs.dll` — `NeedSpec` (`DisplayNameLocKey`)
- `Timberborn.Localization.dll` — `ILoc`
- `Timberborn.BaseComponentSystem.dll` — `BaseComponent`
- `Timberborn.Healthcare.dll` — investigated for healing executor; uses `ApplyEffectExecutor` with slot animation
