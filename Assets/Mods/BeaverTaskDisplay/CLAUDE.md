# BeaverTaskDisplay mod

A Timberborn 1.0 mod that adds a task description and clickable walking-destination row to the beaver info panel, positioned directly below the existing "Carrying" section. Click the destination text and the camera jumps to that entity.

The same panel works for any entity with both `BehaviorManager` and `Walker` components — beavers, bots, golems.

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

`AddBottomFragment(_, 100)` puts our row just below CarryingUI's `GoodCarrierFragment`, which uses order **0** in the same Bottom region (verified by reading the IL of `Timberborn.CarryingUI.CarryingUIConfigurator.EntityPanelModuleProvider.Get` — it loads `ldc.i4.0` immediately before `callvirt AddBottomFragment`).

### `BeaverTaskFragment`

Implements `IEntityPanelFragment`. The interface contract:

| Method | When called | Our implementation |
|---|---|---|
| `InitializeFragment()` | Once at startup; returns the root `VisualElement` | Builds the NineSlice container with a task `Label` and destination `Label` |
| `ShowFragment(BaseComponent entity)` | When the user selects an entity | Caches `BehaviorManager` + checks for `Walker`; shows the panel only if both exist |
| `UpdateFragment()` | Every frame the panel is shown | Re-reads task and destination, refreshes labels |
| `ClearFragment()` | When the panel is closed/another entity is selected | Clears state, hides the panel |

---

## Game internals discovered (the important reference table)

These are all derived from decompiling DLLs in `Assets/Plugins/Timberborn/`. Keeping this here so future iterations don't have to re-derive everything.

### BehaviorManager — the source of "what task"

`Timberborn.BehaviorSystem.BehaviorManager` is a `TickableComponent` on every beaver/bot.

**Public API:**
- `RunningExecutor` — returns `ExecutorInfo` **struct** with two fields: `Name` (string) and `ElapsedTime`. The `Name` is just `executor.GetType().Name` — a raw class name like `"WalkToAccessibleExecutor"`.
- `RunningBehavior` — returns `BehaviorInfo` struct with a `Name` field. We don't currently use this; behaviors are higher-level than executors and would say things like `"WorkAtWorkplaceBehavior"` rather than the leaf action.
- `IsRunningBehavior<TBehavior>()` and `IsRunningExecutor<TExecutor>()` — **both are generic** with no non-generic overload. (This bit me once — the compiler error was `cannot be inferred from the usage`.) We don't use these; we check `string.IsNullOrEmpty(RunningExecutor.Name)` instead.

**Private (read via reflection):**
- `_runningExecutor` — the actual `IExecutor` instance. We must read this to inspect type-specific details on walking executors and `ApplyEffectExecutor`.

`IExecutor` itself only has `Tick(float)`, `Save(...)`, `Load(...)` — no name, no destination. All useful state lives on the concrete subclasses.

### Walker — the source of "where they're going"

`Timberborn.WalkingSystem.Walker` is also a `TickableComponent`.

**Public API:**
- `GoTo(IDestination)` — sets a new destination
- `PathFollower`, `PathCorners`, `CurrentPathBounds`, `CurrentDestinationReachable` — path state
- `StartedNewPath` event with `StartedNewPathEventArgs` (only carries `Distance`, no destination ref)
- `Stopped()`, `StopMoving()`, `RefreshPath()`

**Private:**
- `_currentDestination` — the current `IDestination`. We don't currently read this; we read the executor's destination instead.

### Walking executors — where the destination actually lives

Three concrete walking executors, all in `Timberborn.WalkingSystem`:

| Executor | Private field we read | Field type |
|---|---|---|
| `WalkToAccessibleExecutor` | `_accessible` | An `Accessible` component (BaseComponent) — the entity being walked toward |
| `WalkInsideExecutor` | `_buildingAccessible` | Similar — the building the beaver is entering |
| `WalkToPositionExecutor` | (no entity, just a position) | We currently show no destination row in this case |

`IDestination` has two main implementations: `PositionDestination` (just a Vector3, no entity) and `AccessibleDestination` (has an `Accessible` entity reference). The walking executors store the relevant entity directly in their private fields, which is what we read.

### NeedBehaviorSystem.ApplyEffectExecutor — the special case

This single executor handles eating, drinking, sleeping, bathing, healing, recreation — anything that "applies an effect to satisfy a need." All of them show up as `ApplyEffectExecutor` in `RunningExecutor.Name`.

To distinguish the actual activity, we read the private `_animationName` string field. **The exact values aren't yet confirmed** — the loc dictionary `AnimationLocSuffixes` in the fragment lists guessed names (`"Eating"`, `"Sleeping"`, `"Bathing"`, etc.) which need verification by selecting a beaver doing each activity in-game.

If the real animation names use a different convention (`"eat"`, `"sleep_idle"`, `"BunkhouseBed"`), update the dictionary keys to match.

Other ApplyEffectExecutor private fields, in case `_animationName` proves insufficient:
- `_effects` — generic collection of effect objects (need to decompile further to know the element type and what it exposes)
- `_enterer` — the building the beaver entered to apply the effect
- `_finishTimestamp` — float, when the effect completes
- `_characterAnimator` — likely the animator used for the activity

### EntitySelectionService — the click-to-focus call

`Timberborn.SelectionSystem.EntitySelectionService.SelectAndFocusOn(SelectableObject)` does what we want for the destination click.

To convert from an entity to a `SelectableObject`:
- `SelectableObjectRetriever.TryGetSelectableObject(GameObject, out SelectableObject)` — **takes a GameObject, not a BaseComponent** (this also bit me once)

So the call pattern is:
```csharp
if (_selectableObjectRetriever.TryGetSelectableObject(
        _currentDestEntity.GameObject, out var sel)) {
  _entitySelectionService.SelectAndFocusOn(sel);
}
```

### NamedEntity — the source of human-readable building names

`Timberborn.EntityNaming.NamedEntity` is a component on most entities. Its `EntityName` property gives the localized display name (e.g. "Hauling Post", "Lumberjack Flag #2"). This is what we show as the destination label text.

Fall back to `entity.GameObject.name` (Unity GameObject name) if `NamedEntity` isn't present — that's the prefab clone name, which is ugly but better than nothing.

### BaseComponent — Timberborn's component base class

Lives in `Timberborn.BaseComponentSystem`. **Not** Unity's `MonoBehaviour`-derived classes directly — it's its own thing for Timberborn entity components.

Useful members:
- `GameObject` (property, capital G — note this is the Unity GameObject, returned by a property getter, not a field)
- `Name` (the GameObject's name)
- `GetComponent<T>()`, `HasComponent<T>()`, `TryGetComponent<T>()`, `GetComponents<T>()`
- `EnableComponent`, `DisableComponent`

The `GetComponent<T>` here is Timberborn's own implementation, not Unity's. It works the same way at the call site though.

### Localization (ILoc)

`Timberborn.Localization.ILoc` is the DI-injected localization service:

```csharp
_loc.T("loc.key")                   // no params
_loc.T("loc.key", arg1)             // one {0} substitution
_loc.T("loc.key", arg1, arg2)       // two
_loc.T("loc.key", arg1, arg2, arg3) // three
```

When the key isn't found, it returns the raw key (which is how I noticed the loc CSV wasn't loading — UI showed `grantemsley.BeaverTaskDisplay.Idle` literally instead of "Idle").

Loc CSV files are in `Data/Localizations/<lang>_<suffix>.csv` with three columns:

```csv
ID,Text,Comment
my.key,The text shown,Optional translator note
my.key.with.params,Hello {0}!,Use double quotes around values containing commas
```

### EntityPanelModule.Builder — fragment positioning

The builder has these methods (each takes `(IEntityPanelFragment, int order)`):

```
AddLeftHeaderFragment    AddRightHeaderFragment    AddMiddleHeaderFragment
AddTopFragment           AddMiddleFragment         AddBottomFragment
AddFooterFragment        AddSideFragment
AddDiagnosticFragment    AddContentFragment
```

Order is ascending within a region. CarryingUI uses `AddBottomFragment(_, 0)`. We use `AddBottomFragment(_, 100)` to sit just below it with room for other mods to insert between.

---

## Decisions and rationale

1. **Mod ID prefix `grantemsley.`** — recommended by Mod-directory-structure.md to use a username or domain prefix for global uniqueness across all mods.

2. **Show panel for `BehaviorManager` + `Walker` (not "is beaver")** — bots and golems also benefit from the same info, and they all have these two components. No reason to gate on a beaver-specific component.

3. **Two separate labels rather than one combined line** — explicit user requirement from early in the project: "If we're showing the destination in a separate row, don't include it in the current task part. That would be redundant."

4. **Destination shown only for `WalkTo*Executor` with an entity destination** — `WalkToPositionExecutor` only has a Vector3, no entity to focus on. We could do a reverse lookup ("what's at this tile?") to upgrade this case but it adds complexity for marginal value.

5. **Reflection on private fields, not Harmony patches** — the official modding tools build against the game DLLs as-is (no publicizing); private fields aren't accessible directly. Reflection is the standard fallback. The cached `FieldInfo` lookups happen once at type-init and incur no per-frame cost beyond the GetValue call.

6. **`ApplyEffectExecutor` distinguished by `_animationName`** — `RunningExecutor.Name` is just the class name, which is the same string for eating/drinking/sleeping/etc. The animation name appears to be the only field on the executor that varies between these. Confirmed values pending in-game testing.

7. **Removed `alignItems = Align.FlexStart` from root** — it was sizing labels to their content's *minimum* width, causing internal text wrap. Default `Stretch` lets the labels fill the panel width and only wrap when actually too long.

8. **Removed inline padding overrides** — the `entity-sub-panel` USS class handles spacing consistently with the other sub-panels (carrying, etc.). My explicit `paddingLeft = 8` was misaligning our text vs the carrying section above.

9. **Pale blue color (no bold) for destination** — provides clickability hint without making the row visually heavy. Could add a hover effect later if the affordance feels too subtle.

10. **`AddBottomFragment(_, 100)`, not 1** — gives breathing room for other mods to insert between us and CarryingUI's order 0 if they want to.

---

## Known limitations and risks

- **Reflection fragility** — game patches could rename `_runningExecutor`, `_accessible`, `_buildingAccessible`, or `_animationName` without warning. The fragment is defensive (null checks throughout) so it'll silently degrade rather than crash, but the destination row or animation-name handling could stop working. Task name via the public `ExecutorInfo.Name` would still work since it doesn't depend on reflection.

- **`ApplyEffectExecutor` animation names are guessed** — `AnimationLocSuffixes` in the fragment is my best guess at what values appear. Verify in-game and update.

- **No `PositionDestination` handling** — beavers walking to a tile (rather than an entity) get no destination row. The task line still shows.

- **English only** — only `enUS_BeaverTaskDisplay.csv`. Add other languages by creating files like `frFR_BeaverTaskDisplay.csv`, `deDE_BeaverTaskDisplay.csv`, etc.

- **No persistence or settings** — the panel always appears for entities with `BehaviorManager` + `Walker`. No toggle, no in-game options.

- **Editor compile vs Mod Builder** — the Unity Editor compile must succeed before the Mod Builder runs; otherwise you'll see "Error building Player because scripts have compile errors in the editor". Always check the Visual Studio Error List or Unity Console first.

---

## Tooling and source references

### Where things live on disk

- **Modding repo (Unity project)**: `D:\claude\timberborn-modding`
- **Game DLLs (decompilation targets)**: `D:\claude\timberborn-modding\Assets\Plugins\Timberborn\Timberborn.*.dll` — these are the actual game DLLs, imported by the modding tools. There are ~250 of them; the mod doesn't need most of them.
- **Wiki (cloned)**: `D:\claude\timberborn-modding.wiki` — contains all the modding docs as markdown. Most useful: `Coding basics.md`, `Timberborn-architecture.md`, `User-interface.md`, `Mod-directory-structure.md`, `Translations.md`, `Mod-Builder.md`.
- **Example mods**: `D:\claude\timberborn-modding\Assets\Mods\` — `HelloWorld` is the closest reference for fragments + DI patterns.

### How to inspect game DLLs

For finding type/method/field names quickly, the Python `dnfile` package is fast:

```python
import dnfile
pe = dnfile.dnPE("Timberborn.SomeAssembly.dll")
md = pe.net.mdtables
for row in md.TypeDef.rows:
    name = str(row.TypeName)
    # ...iterate methods via row.MethodList (list of MDTableIndex into MethodDef)
    # ...iterate fields via row.FieldList
```

For deeper inspection (method bodies, IL, decompiled C#), use **dnSpy** or **dnSpyEx** from `https://github.com/dnSpyEx/dnSpy/releases`. Open the DLL, navigate types, and dnSpy will decompile to readable C#.

### DLLs already inspected during this project

(For Claude Code: if you find yourself wondering about types in these, they've been touched before — likely safe to dig in.)

- `Timberborn.BehaviorSystem.dll` — `BehaviorManager`, `IExecutor`, `ExecutorInfo`, `BehaviorInfo`, `WaitExecutor`, `ExecutorExtensions`
- `Timberborn.WalkingSystem.dll` — `Walker`, `WalkToAccessibleExecutor`, `WalkToPositionExecutor`, `WalkInsideExecutor`, `IDestination`, `PositionDestination`, `AccessibleDestination`
- `Timberborn.NeedBehaviorSystem.dll` — `ApplyEffectExecutor`
- `Timberborn.EntityPanelSystem.dll` — `EntityPanelModule.Builder`, `IEntityPanelFragment`, `EntityDescription`, `EntityDescriptionService`
- `Timberborn.SelectionSystem.dll` — `EntitySelectionService`, `SelectableObject`, `SelectableObjectRetriever`
- `Timberborn.EntityNaming.dll` — `NamedEntity`
- `Timberborn.CarryingUI.dll` — `GoodCarrierFragment`, `CarryingUIConfigurator` (anchor for our positioning)
- `Timberborn.Carrying.dll` — `GoodCarrier` (the underlying component, in case we want to read what's being carried)
- `Timberborn.Localization.dll` — `ILoc`, `Loc`
- `Timberborn.WorldPersistence.dll` — `INamedComponent` (where `ComponentName` comes from)
- `Timberborn.BaseComponentSystem.dll` — `BaseComponent`

Other executors that exist (found during enumeration) and could be added to the loc map if needed:

| Executor | DLL | Likely meaning |
|---|---|---|
| `BuildExecutor` | ConstructionSites | Building a construction site |
| `DemolishExecutor` | Demolishing | Tearing down |
| `PlantExecutor` | Planting | Planting a sapling |
| `WalkToReservableExecutor` | ReservableSystem | Walking to a workplace/reservable |
| `WorkAtReservableExecutor` | ReservableSystem | Working at a reservable spot |
| `ProduceExecutor` | Workshops | Producing goods |
| `WorkExecutor` | Workshops | Generic work |
| `RemoveYieldExecutor` | Yielding | Harvesting trees/crops |

---

## Open follow-ups

- Verify `_animationName` values for `ApplyEffectExecutor` and tune `AnimationLocSuffixes` accordingly.
- Consider showing destination for `WalkToPositionExecutor` via a tile-to-entity reverse lookup (e.g. nearest building footprint).
- Hover effect on the destination label (color shift or underline) for a clearer click affordance.
- Mod settings system if/when the user wants a toggle.
- Translations to other supported languages.
