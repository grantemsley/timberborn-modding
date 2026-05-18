# EmergencyPriority mod

A Timberborn 1.0 mod that adds a 6th "Emergency" priority above Very High for construction sites. Beavers and bots employed at a Builder Hub work emergency-flagged jobs before anything else, ignoring their working hours and (Phase 2) scheduled sleep until needs become critical. Critical needs are still handled by the base game's `CriticalNeederRootBehavior` so beavers don't die.

## Status

**Phase 1 in-game tested and working.** Beavers finish their current task and immediately switch to the emergency job, work past their schedule, and revert when the emergency clears. UI looks right: red "!" toggle, click-sound plays, standard priority deselects visually when Emergency is on, and clicking a standard priority clears Emergency. Save/load preserves Emergency flags across reloads.

**Phase 2 in-game tested and working.** Patches on `BeaverNeedBehaviorPicker.ShouldPickEssentialAction` suppress scheduled sleep; `SleepNeedBehavior.ShouldSleepAtHome` + `SleepNeedBehavior.SleepOutside` make emergency builders sleep on the spot at the worksite instead of walking back near home.

**Phase 3 in-game tested and working.** Patch on `DistrictNeedBehaviorService.PickShortestAction` makes emergency builders in critical food/water state grab the globally-closest source (shortest `ActionDurationCalculator.DurationWithReturnInHours`) instead of the vanilla "highest-points group → shortest in that group" path. Non-emergency beavers and the non-critical path are unchanged.

**Stretch/optional** — extend the Emergency toggle to the top-bar BuilderPrioritiesButton area tool (paint Emergency over an area). Not in scope for Phases 1–3.

---

## File structure

```
Assets/Mods/EmergencyPriority/
├── CLAUDE.md                                     # this file
├── manifest.json                                 # Id: grantemsley.EmergencyPriority; requires Harmony >= 2.3.0
├── Data/
│   └── Localizations/
│       └── enUS_EmergencyPriority.csv            # Emergency label + toggle tooltip
└── Scripts/
    ├── grantemsley.EmergencyPriority.asmdef     # autoReferenced: false, allowUnsafeCode: true
    ├── EmergencyBuilderCheck.cs                 # shared helper: is this beaver an employed builder while emergency jobs exist?
    ├── EmergencyConstructable.cs                # BaseComponent on construction sites
    ├── EmergencyConstructionRegistry.cs         # singleton; HashSet of emergency-flagged ConstructionJobs + JobRegistered event
    ├── EmergencyInterruptionService.cs          # ILoadableSingleton; on JobRegistered, wakes builders mid-sleep/eat in the same district
    ├── EmergencyPatchBootstrap.cs               # ILoadableSingleton that hands registry to static patch fields
    ├── EmergencyPriorityConfigurator.cs         # Bindito DI + ConstructionSite→EmergencyConstructable decorator
    ├── EmergencyPriorityModStarter.cs           # IModStarter; calls Harmony.PatchAll
    ├── EmergencyToggleController.cs             # lifecycle/state for the 6th toggle (Enable/Disable/UpdateState)
    └── Patches/
        ├── BeaverNeedBehaviorPickerPatch.cs     # prefix ShouldPickEssentialAction → suppress scheduled sleep for emergency builders
        ├── BuilderHubWorkplaceBehaviorPatch.cs  # prefix Decide → try emergency jobs first
        ├── BuilderPriorityToggleGroupFactoryPatch.cs  # postfix Create → inject 6th toggle
        ├── CarryRootBehaviorPatch.cs            # prefix Decide → skip non-emergency hauls (backstop for new haul reservations during emergency)
        ├── DistrictNeedBehaviorServicePatch.cs  # prefix PickShortestAction → closest food/water for emergency builders in critical state
        ├── PriorityToggleGroupPatch.cs          # postfix Enable/Disable/UpdateGroup → dispatch to controller
        ├── PriorityToggleSelectionPatch.cs      # postfix OnValueChanged → auto-clear Emergency on standard click
        ├── SleepNeedBehaviorPatch.cs            # prefix Decide + ShouldSleepAtHome + SleepOutside → skip scheduled sleep, sleep on the spot when truly needed
        └── WorkerRootBehaviorPatch.cs           # prefix Decide → bypass AreWorkingHours for builders
```

`Data/Localizations/enUS_*.csv` is copied verbatim to `Localizations/` in the built mod (Mod-Builder convention).

Built mod lands in `Documents/Timberborn/Mods/grantemsley.EmergencyPriority/`.

---

## Build

Unity project at `D:\claude\timberborn-modding`, Unity **6000.0.16f1**, `-langversion:9.0`.

1. Open the project in Unity.
2. Wait for Package Manager to fetch `com.emka.timberborn-harmony` (declared in `Packages/manifest.json`). If it fails: Window → Package Manager → + → Add package from git URL → `https://github.com/eMkaQQ/timberborn-harmony.git`.
3. If the Mod Builder doesn't list the mod, **focus the Project window and press Ctrl+R** (or right-click Assets → Refresh) so Unity generates `.meta` files and indexes the new mod folder. The Mod Builder only sees mods Unity knows about.
4. Verify no compile errors in the Console.
5. **Timberborn → Show Mod Builder** → tick "Emergency Priority" → **Build all**.

At runtime, the player also needs the `Harmony` mod from mod.io or Steam Workshop installed (matches `manifest.json` `RequiredMods`).

---

## Architecture

### Harmony dependency model

Timberborn mods do **not** bundle their own `0Harmony.dll`. The community convention is to declare `{ "Id": "Harmony", "MinimumVersion": "2.3.0" }` in `manifest.json` `RequiredMods`. Players install the shared `Harmony` mod separately; mod developers install the Unity package `com.emka.timberborn-harmony` for compile-time references. This avoids version conflicts when multiple mods use Harmony.

If we ever need to vendor Harmony (e.g. for a standalone distribution), drop `0Harmony.dll` into `Scripts/` and remove the RequiredMod entry — but don't do this if other Harmony mods will be active.

When importing `HarmonyLib`, watch for `HarmonyLib.Priority` clashing with `Timberborn.PrioritySystem.Priority`. If both namespaces are in scope, add `using Priority = Timberborn.PrioritySystem.Priority;`.

### Two parallel priority systems

The game already has a `Priority` enum (5 values: VeryLow…VeryHigh) consumed throughout compiled code. We can't extend it. Instead, Emergency is a **separate orthogonal bool flag** stored on each construction site via `EmergencyConstructable`. The base game's priority sort still runs; our Harmony prefixes inject emergency jobs ahead of the sort.

When Emergency is cleared, the building reverts to its underlying `BuilderPrioritizable.Priority`. The UI presents this as a single 6-option radio group:
- Clicking a standard priority while Emergency is on auto-clears Emergency (patch on `PriorityToggle.OnValueChanged`).
- Clicking the active Emergency toggle off leaves `BuilderPrioritizable.Priority` untouched, so it falls back to whichever standard priority was previously set.
- While Emergency is on, the standard priority toggles' `priority-toggle--checked` class is stripped each frame so they look unselected.

### Component lifecycle (`EmergencyConstructable`)

Decorated onto every `ConstructionSite`. Implements:
- `IAwakableComponent` — `Awake()` caches `_constructionJob = GetComponent<ConstructionJob>()`.
- `IUnfinishedStateListener` — `OnEnterUnfinishedState` / `OnExitUnfinishedState` track whether the site is currently buildable, and register/unregister with the registry if `IsEmergency` is true at the transition.
- `IPersistentEntity` — saves only when `IsEmergency` is true (mirrors `BuilderPrioritizable.Save`).

`SetEmergency(bool)` is the only external mutation entry point. It updates `IsEmergency` and, if the site is currently unfinished, registers or unregisters with `EmergencyConstructionRegistry`.

Load order is `Awake → Load → OnEnterUnfinishedState`. **Untested** on save-load; if `OnEnterUnfinishedState` doesn't fire on load (only on initial placement), saved emergencies won't register and we'd need to add an `IInitializableEntity.InitializeEntity` hook. The base game's `ConstructionRegistrar` uses the same pattern so this is expected to work.

### Registry (`EmergencyConstructionRegistry`)

Single `HashSet<ConstructionJob>`. Exposes `HasAny` (O(1)) and `EmergencyJobs` (the live set, for iteration). Patches read this from their hot paths; the `HasAny` short-circuit means non-emergency state is one bool check.

Bound as singleton in `EmergencyPriorityConfigurator`. `EmergencyPatchBootstrap` (`ILoadableSingleton`) injects the registry and assigns it to the static `Registry` properties on `BuilderHubWorkplaceBehaviorPatch` and `WorkerRootBehaviorPatch` at scene load. This is the bridge between DI-resolved state and static Harmony patches.

Iteration order is undefined (HashSet); when multiple sites are flagged, the pick order is arbitrary. A `SortedSet<ConstructionJob>` keyed on `InstantiationOrder` would mirror the base game's tiebreaker if deterministic ordering becomes important.

`Register` fires a `JobRegistered` event when a new job is added (set transition only). `EmergencyInterruptionService` listens to this — see below.

### Interruption (`EmergencyInterruptionService`)

`BehaviorManager.Tick` only re-evaluates root behaviors when `_runningExecutor == null`. A beaver mid-sleep (or mid-eat) is therefore deaf to new emergencies until their executor finishes — which for scheduled sleep can be hours of game time. Without intervention, "set Emergency, builders stay napping" was the user-visible bug.

`EmergencyInterruptionService` is an `ILoadableSingleton` that subscribes to `EmergencyConstructionRegistry.JobRegistered`. When a job is added, it:

1. Reads the job's district via `DistrictBuilding.District`.
2. Pulls every `BuilderHubWorkplaceBehavior`-bearing building in that district from `DistrictBuildingRegistry.GetEnabledBuildings<BuilderHubWorkplaceBehavior>()`.
3. Iterates each hub's `Workplace.AssignedWorkers`.
4. For each worker, reads `BehaviorManager._runningExecutor` and `_runningBehavior` via reflection. We deliberately do *not* skip critical-state beavers here: when a beaver has just fallen asleep their Sleep need points are at minimum, which makes `Need.IsInCriticalState` true (because `IsCritical && !IsFavorable`), which made `AnyNeedIsInCriticalState` return true and silently skipped the interrupt — that was the exact "click Emergency on a sleeping beaver, nothing happens" bug. If a beaver really is in critical need (e.g., starving), `CriticalNeederRootBehavior` wins again on the next re-decide and they resume the critical-need behavior — one wasted tick is a fine cost for making sleep interruption actually work.

   Branches based on what's running:
   - If executor is `ApplyEffectExecutor` (sleep/eat/drink): set `_finishTimestamp = 0` so the executor's own `Tick` returns Success on the next tick and properly clears its `Sleeping` animation.
   - Else if running behavior is `SleepNeedBehavior` (the beaver is mid-walk to bed): null the executor so re-decide happens immediately. Patch 5a then returns `ReleaseNow` and the tree falls to `WorkerRootBehavior`.
   - Else if running behavior is `CarryRootBehavior`: drop cargo + release reservations + null executor (see Hauling Interrupt section below).
   - Otherwise: leave alone.
   - If the executor is an `ApplyEffectExecutor` (sleep, eat, drink, etc.), set `_finishTimestamp` to `0f`. Next tick the executor sees the expired timestamp, calls `TurnOffAnimation`, and returns `ExecutorStatus.Success` — so animations clean up properly.
   - If the running behavior is `CarryRootBehavior` (hauling), check whether the destination is an emergency site itself; if so, leave the beaver alone — that haul is feeding the emergency. Otherwise: spawn a `RecoveredGoodStack` at the beaver's current grid position via `RecoveredGoodStackSpawner.AddAwaitingGoods`, call `goodCarrier.EmptyHands()`, release any capacity/stock reservations via `GoodReserver`, and null `_runningExecutor`. The dropped cargo becomes a pickup-able stack on the ground (same mechanism as deconstructed buildings); the beaver re-decides next tick and `WorkerRootBehavior` picks up the emergency.

The cleanup path works because `Decision.ReleaseWhenFinished` sets `shouldReturnToBehavior: false` for sleep, so once the executor clears, `ProcessBehaviors` evaluates the tree from the top. For hauls (`Decision.ReturnWhenFinished`), `_returnToBehavior` is true and `CarryRootBehavior.Decide` is consulted again — but with cargo emptied and reservations released, the vanilla logic already returns `ReleaseNow`; patch #7 is a backstop for new haul reservations that might be initiated later.

Construction work and other walks aren't interrupted — they use different executors / behaviors and complete on their own short timescale.

On save-load, each persisted Emergency site re-registers and re-fires `JobRegistered`. Multiple registrations interrupt the same beavers multiple times, but the operation is idempotent (setting `_finishTimestamp` to 0 repeatedly is a no-op) so there's no compounding cost.

### The Harmony patches

| # | Target | Type | Purpose |
|---|---|---|---|
| 1a | `BuilderPriorityToggleGroupFactory.Create` | Postfix | Inject a 6th red toggle into the construction-site priority widget. Loads `Game/EntityPanel/PriorityToggle` UXML via the reflected-out `VisualElementLoader` so the Toggle goes through `VisualElementInitializer` (registers `UISoundInitializer`'s click-sound callback). Stores an `EmergencyToggleController` in a `ConditionalWeakTable<PriorityToggleGroup, EmergencyToggleController>` so dead groups from prior scene loads get garbage-collected. |
| 1b | `PriorityToggleGroup.{Enable,Disable,UpdateGroup}` | Postfix ×3 | Look up the controller for `__instance` and dispatch the lifecycle call. Workplace priority panels miss the lookup (not in the table) and are unaffected. |
| 1c | `PriorityToggle.OnValueChanged` | Postfix | When the user clicks any standard priority toggle on an entity with Emergency set, clear the Emergency flag. We clear regardless of `newValue`'s direction — when Emergency is on, the toggles' `Toggle.value` still tracks the underlying `BuilderPrioritizable.Priority` (we only strip the visual CSS class), so clicking the currently-set priority toggles its value true → false and fires with `newValue=false`. The vanilla `OnValueChanged` gates its `SetPriority` call on `newValue=true`, so the underlying priority stays correct either way. Reads `_prioritizable` via reflection. |
| 2 | `BuilderHubWorkplaceBehavior.Decide` | Prefix | For each emergency job, first call `hubAccessible.FindRoadToTerrainPath(siteAccessible, ...)` to check whether the site is reachable from this hub. Skip non-reachable jobs. For each reachable job, call `ConstructionJob.StartConstructionJob(agent, accessible)`; the first one that accepts short-circuits the original `Priorities.Descending` loop. If at least one emergency is reachable from this hub but none can currently accept the beaver (build slot full, materials in flight, etc.), return `Decision.ReleaseNow()` — builders idle rather than picking up normal-priority construction. If NO emergency is reachable from this hub (every emergency is in a different district), fall through to vanilla so this hub's builders keep doing their normal work. The road check duplicates the one `StartConstructionJob` does internally so we can distinguish "wrong district" from "busy". An earlier version compared `DistrictBuilding.GetDistrictOrConstructionDistrict()` but `ConstructionDistrict` is timing-dependent on the navmesh listener after save-load; using the road check directly avoids that pitfall. Reads `_accessible` (on hub) and `_constructionSiteAccessible` (on construction job) via reflection. |
| 3 | `WorkerRootBehavior.Decide` | Prefix | If registry has any jobs AND the beaver's workplace contains a `BuilderHubWorkplaceBehavior` AND `!WorkRefuser.RefusesWork`, invoke private `DecideAsWorker()` via reflection — skipping the `AreWorkingHours` gate. Bots use the same code path, so they're also affected. |
| 4 | `BeaverNeedBehaviorPicker.ShouldPickEssentialAction` | Prefix | For emergency-employed builders, skip the `ItIsTimeForEssentialAction` (scheduled-sleep-near-dawn) trigger. Sleep only wins when `EssentialActionIsAtMinimumPoints` says the need has actually bottomed out. Reflection on `_appraiser`, `_needManager`, `Appraiser.AppraiseEffect`, `NeedManager.NeedIsAtMinimumPoints`. |
| 5a | `SleepNeedBehavior.Decide` | Prefix | For emergency-employed builders, return `Decision.ReleaseNow()` so the tree re-evaluates and `WorkerRootBehavior` picks up the emergency. Exception: if sleep is actually at minimum (`NeedManager.NeedIsAtMinimumPoints("Sleep")`), let vanilla run — that's the "they'd otherwise pass out" case, and patches 5b/5c route them through SleepOutside at the current position. Handles the case where Emergency is flagged after the beaver has already picked sleep (e.g., mid-walk-to-bed); without it, vanilla Decide would re-walk-home or restart Sleep via the `_returnToBehavior` path. |
| 5b | `SleepNeedBehavior.ShouldSleepAtHome` | Prefix | For emergency-employed builders, return `false` to force the SleepOutside path. `GetEssentialAction` also routes through this, so the essential action's position becomes "here, now" rather than home. |
| 5c | `SleepNeedBehavior.SleepOutside` | Prefix | For emergency-employed builders, pre-set `_walkedToSleepingPosition = true` so the original method skips `WalkToRandomSleepingPosition` and goes straight to `Sleep()` at the current position. Without this, `RandomDestinationPicker.GetCoordinates` anchors the random destination on the beaver's *home* (not the worksite), dragging emergency builders back near home before sleeping. `Sleep()` resets the flag, so normal beavers are unaffected. |
| 6 | `DistrictNeedBehaviorService.PickShortestAction` | Prefix | When `onlyNeedsInCriticalState == true` AND the calling beaver is an emergency builder, scan all groups in `_appraisedNeedBehaviors` and return the globally shortest-duration action (by `ActionDurationCalculator.DurationWithReturnInHours`). Vanilla iterates groups in descending-points order and returns the highest-points group's shortest behavior, which can drag a starving builder past closer food to their favorite. Reflection on the private nested struct `AppraisedNeedBehaviorGroup` (`NeedBehaviorGroup`/`Points` properties) and the private `_appraisedNeedBehaviors` field. |
| 7 | `CarryRootBehavior.Decide` | Prefix | For emergency-employed builders, return `Decision.ReleaseNow()` to skip carry/delivery logic — the beaver falls through the behavior tree to `WorkerRootBehavior` and picks up the emergency immediately. Acts as a backstop for any *new* haul reservations that get initiated while emergency is active (e.g., from `LaborBehavior` decorators on the hub). For *in-progress* hauls, `EmergencyInterruptionService` has already dropped the cargo and released reservations, so vanilla `CarryRootBehavior.Decide` would already release naturally — the patch just defends against new reservations. Exception: hauls destined for an emergency site (detected by `GoodReserver.CapacityReservation.Inventory.GetComponent<ConstructionJob>()` being in the registry) proceed normally so we don't starve the emergency of materials. |

`CriticalNeederRootBehavior` runs above `WorkerRootBehavior` in the behavior tree, so beavers in critical-need state still preempt the emergency override. That's how we get "ignore needs but don't die": critical state always wins.

Patches 4, 5, 6, and 7 share `EmergencyBuilderCheck.IsEmergencyBuilder(entity, registry)` — checks that the registry has any jobs and the entity has a `Worker` employed at a workplace containing a `BuilderHubWorkplaceBehavior`. `BeaverNeedBehaviorPicker`, `SleepNeedBehavior`, `NeedManager`, and `CarryRootBehavior` are all `BaseComponent`s on the beaver entity, so `GetComponent<Worker>()` works for each.

### Static-constructor reflection guards

Each patch's static constructor logs `Debug.LogError` if any private field/method lookup returned null. The patch then gracefully no-ops. This makes a game-version-induced rename visible in the Unity log instead of mysteriously broken behavior.

### UI integration (`EmergencyToggleController`)

Mirrors `PriorityToggle`'s lifecycle methods (`Enable(IPrioritizable)`, `Disable()`, `UpdateState()`) without subclassing or modifying `PriorityToggleGroup._toggles`.

Critical insight discovered during testing: we MUST load the toggle via `VisualElementLoader.LoadVisualElement("Game/EntityPanel/PriorityToggle")` rather than `new Toggle()`. The loader runs `VisualElementInitializer.InitializeVisualElement(element)` recursively, which registers `UISoundInitializer`'s `ClickEvent` callback. Without that, the toggle has no click sound. We reflect into the factory chain to reach the loader:

```
BuilderPriorityToggleGroupFactory
  → _priorityToggleGroupFactory : PriorityToggleGroupFactory
    → _visualElementLoader : VisualElementLoader
```

The loaded Toggle gets the right styles (`priority-toggle` and `content-centered` USS classes) and is registered with all `IVisualElementInitializer`s. We then:
1. Hide its `unity-checkmark` child (`style.display = None`) so Unity's default checkmark glyph doesn't show through.
2. Hide any Label descendant (BaseField slot — class name varies by Unity version, so we hide every Label).
3. Tint the toggle's background-image color red (affects the `priority-toggle--checked` highlight).
4. Add a centered, absolute, bold, red `Label("!")` as the icon. `PickingMode.Ignore` so clicks fall through to the underlying Toggle.
5. Register a localizable tooltip via `ITooltipRegistrar.RegisterLocalizable(toggle, "grantemsley.EmergencyPriority.EmergencyTooltip")`. The registrar is wired into the static `BuilderPriorityToggleGroupFactoryPatch.TooltipRegistrar` by `EmergencyPatchBootstrap`. Important ordering note: `EntityPanel` is itself an `ILoadableSingleton` and calls `InitializeFragment` on every fragment during `Load()`, which fires our Postfix and builds the toggle. Mod singletons load *after* core singletons, so the registrar is null at that point. We queue toggles into `PendingTooltipToggles` when the registrar is null and drain the queue when `EmergencyPatchBootstrap.Load` later assigns the registrar via the static setter.

The current entity's `EmergencyConstructable` is looked up via `(prioritizable as BaseComponent)?.GetComponent<EmergencyConstructable>()`. `IPrioritizable` is implemented by `BuilderPrioritizable : BaseComponent`, so the cast succeeds for construction sites.

The 6th toggle hides itself (`style.display = None`) when the selected entity has no `EmergencyConstructable`. This is how we keep the button off `RecoveredGoodStackFragment` (rubble) and `DemolishableFragment` (demolition), which share `BuilderPriorityToggleGroupFactory` with `ConstructionSiteFragment`.

### Sibling visual suppression

When Emergency is on, `EmergencyToggleController.UpdateState` walks the `TogglesWrapper` children and removes the `priority-toggle--checked` class from every non-Emergency toggle. Since the standard `PriorityToggle.UpdateState` re-adds the class every frame, our controller has to do this every frame too — and we run via the `PriorityToggleGroup.UpdateGroup` postfix, which is after the per-toggle updates, giving us the last word.

When Emergency is cleared, the standard toggles' `UpdateState` re-adds the class on the next frame automatically.

---

## Game internals discovered

### Priority enum is sealed at 5 values

`Timberborn.PrioritySystem.Priority` is `enum { VeryLow, Low, Normal, High, VeryHigh }`. `Priorities.Ascending` and `Priorities.Descending` are derived via `Enum.GetValues`. Used as dictionary keys in `ConstructionRegistry._jobs` and as sort keys in `PriorityOrderedWorkplaces.WorkplacePriorityKey`. Extending the enum is not feasible — we have to layer Emergency as an orthogonal flag.

### Construction job flow

```
BuilderHubWorkplaceBehavior.Decide
  → loop Priorities.Descending
    → loop _providers (sorted by ProviderPriority ascending)
      → IBuilderJobProvider.GetJob(accessible, agent, priority)
        → BuildingJobProvider.GetJob: foreach ConstructionRegistry.GetJobs(priority)
          → ConstructionJob.StartConstructionJob(agent, workplaceAccessible)
            → returns (Behavior, Decision); release-now if site off / no district / unreachable
```

Each `ConstructionJob` is registered with `ConstructionRegistry` by `ConstructionRegistrar.OnEnterUnfinishedState` keyed on its current `BuilderPrioritizable.Priority`. Priority changes re-key the entry.

Our patch 2 bypasses the priority loop entirely when emergency jobs exist, calling `StartConstructionJob` directly. The same `(behavior, decision)` tuple comes back and we wrap it with `Decision.TransferNow`.

### Beaver behavior tree (from `BeaverBehaviorInitializer.InitializeBehaviors`)

Top-to-bottom; first non-`ReleaseNow` wins:
1. `CharacterControlRootBehavior`
2. `DeadRootBehavior`
3. `CarryRootBehavior` (adults only)
4. `DieRootBehavior`
5. `ContaminateRootBehavior`
6. **`CriticalNeederRootBehavior`** — picks `BestNeedBehaviorAffectingNeedsInCriticalState`. We deliberately don't touch this so beavers always preempt emergency work for genuinely critical needs.
7. `StrandedRootBehavior`
8. **`WorkerRootBehavior`** — gates on `!WorkRefuser.RefusesWork && _worker.Employed && _workerWorkingHours.AreWorkingHours`. Calls `DecideAsWorker → WorkAtWorkplace` which iterates `_worker.Workplace.WorkplaceBehaviors`. **Patched by us (#3)** to skip the `AreWorkingHours` gate.
9. **`NeederRootBehavior`** — picks `BestNeedBehavior`. Phase 2 will patch `BeaverNeedBehaviorPicker.ShouldPickEssentialAction` to suppress scheduled sleep here.
10. `WanderRootBehavior`

### Worker / Workplace

- `Worker.Workplace` — public property, `Workplace` is also a `BaseComponent`.
- `Workplace.WorkplaceBehaviors` — public, returns `ReadOnlyList<WorkplaceBehavior>` from `Timberborn.Common`.
- A Builder Hub workplace decorates `BuilderHubSpec → BuilderHubWorkplaceBehavior` (both Folktail and Iron Teeth use `BuilderHubSpec`).
- `WorkerRootBehavior._worker`, `_workRefuser`, `_workerWorkingHours` and `WorkerRootBehavior.DecideAsWorker()` are all private/internal in IL despite decompiled output showing `public` — must be accessed via reflection.

### UI: priority toggle widget

Three panels use `BuilderPriorityToggleGroupFactory`:
- `ConstructionSiteFragment` — construction sites (where we want Emergency)
- `RecoveredGoodStackFragment` — rubble piles
- `DemolishableFragment` — buildings flagged for demolition

The Emergency toggle gets injected into all three but hides itself on the latter two (entity lacks `EmergencyConstructable`).

The widget UXML loads `Game/EntityPanel/PriorityToggleGroup` (a `NineSliceVisualElement` containing a `Label` and a `TogglesWrapper`). Each toggle is its own UXML (`Game/EntityPanel/PriorityToggle`) — a `<ui:Toggle name="PriorityToggle" class="priority-toggle content-centered" />` with inline-styled background-image (the priority icon) and tint color.

USS classes:
- `.priority-toggle` — 24×24, with `--click-sound: "UI.Click"` (consumed by `UISoundInitializer`)
- `.priority-toggle--checked` — adds background image `UI/Images/Game/priority-toggle-checked` (the "selected" highlight behind the icon)

### `UISoundInitializer` and `VisualElementInitializer`

`Timberborn.CoreUI.UISoundInitializer` is an `IVisualElementInitializer` that registers a `ClickEvent` callback on every visual element. The callback reads the `--click-sound` custom style property from the clicked element and plays it via `UISoundController`. Critically, this initializer only runs on elements that go through `VisualElementLoader.LoadVisualElement(...)`. Manually-instantiated `new Toggle()` skip the initializer pipeline entirely and have no click sound.

The click-sound check requires `clickEvent.currentTarget == clickEvent.target` (click landed directly on the element with the registered handler). So putting an absolutely-positioned interactive Label over a Toggle would steal the sound — use `PickingMode.Ignore` on overlay elements.

### Persistence

`Timberborn.WorldPersistence.IPersistentEntity` provides `Save(IEntitySaver)` and `Load(IEntityLoader)`. Both `IEntitySaver` and `IEntityLoader` live in `Timberborn.Persistence`. `ComponentKey` and `PropertyKey<T>` are also in `Timberborn.Persistence`. `BuilderPrioritizable.Save` is the reference pattern — only writes the property when it differs from the default, to keep saves clean and forward-compatible.

### IPrioritizable

`Timberborn.PrioritySystem.IPrioritizable` only requires `Priority` and `SetPriority(Priority)`. `BuilderPrioritizable` (the construction-site implementation) inherits `BaseComponent`, so we can downcast and `GetComponent<EmergencyConstructable>()` from inside the toggle's `Enable` callback.

---

## Decisions and rationale

1. **Emergency as orthogonal flag, not a 6th enum value.** The `Priority` enum is sealed and used throughout compiled game code (sort keys, switch statements, dictionary keys). Extending it would require recompiling the game.

2. **Harmony for behavior override, DI for state.** Behavior-tree decisions are private game logic — Harmony is the only practical tool. State (the registry, the per-entity flag) plays nicely with Bindito.

3. **Bridge pattern: `EmergencyPatchBootstrap` to wire DI-resolved registry into static patch fields.** Harmony patches are static; they can't be constructor-injected. The bridge runs once at scene load.

4. **`ConditionalWeakTable<PriorityToggleGroup, EmergencyToggleController>` instead of a Dictionary.** Scene reloads create new `PriorityToggleGroup` instances; the old ones would otherwise pile up in a Dictionary. Weak table allows GC.

5. **`ConstructionSite → EmergencyConstructable` template decoration.** Mirrors how `BuilderPrioritizable` is added via `ConstructionSitePrioritizableEnabler → BuilderPrioritizable`. The decorator ensures the component is present on every construction site at instantiation time.

6. **Static-init reflection guards.** Game patches may rename `_accessible`, `_worker`, `_workRefuser`, `DecideAsWorker`, `_prioritizable`, etc. Silent no-op is the worst failure mode for the user. A logged error tells them what to file a bug about.

7. **Don't patch `CriticalNeederRootBehavior`.** "Ignore needs as long as possible without dying" — the base game's critical-state detection is exactly the threshold we want. By keeping critical above our schedule override in the behavior tree, beavers always preempt for actual death-level needs.

8. **`Lib.Harmony 2.4.2` via the shared `com.emka.timberborn-harmony` package.** Avoids the type-conflict footgun from each mod bundling its own DLL. `manifest.json` `RequiredMods` enforces the runtime dependency.

9. **Bots get patch 2 & 3 implicitly.** Bots use `WorkerRootBehavior` and `BuilderHubWorkplaceBehavior` too. The user wanted bots to work emergencies nonstop — which is what they do, since bots have no need-based behaviors to interfere.

10. **Phased rollout.** Phase 1 (emergency priority + schedule override) is the headline feature. Sleep-on-spot and closest-food are refinements. Splitting them limits risk per ship.

11. **Load the toggle UXML, don't `new Toggle()`.** Discovered during testing: `new Toggle()` bypasses `VisualElementInitializer`, so it has no click sound. Reflect through the factory chain to get `VisualElementLoader` and load `Game/EntityPanel/PriorityToggle`.

12. **Hide `unity-checkmark` and any Label descendants explicitly.** Unity's default `Toggle` renders a checkmark glyph and a label-slot child; both need to be hidden for the toggle to look like a clean square button.

13. **Emergency-clears-on-standard-click is a separate Harmony patch.** `PriorityToggleSelectionPatch` postfixes `PriorityToggle.OnValueChanged` to auto-clear Emergency. This is more reliable than trying to react via `BuilderPrioritizable.PriorityChanged` (which would fire even when the priority is set programmatically, not just by user click).

14. **Falling back from Emergency keeps `BuilderPrioritizable.Priority` untouched.** Toggling Emergency off only flips `EmergencyConstructable.IsEmergency`; the underlying priority is preserved, so the standard toggle that matches it re-asserts its checked visual next frame via `PriorityToggle.UpdateState`.

---

## Known limitations and risks

- **Save-load behavior is unverified.** If `OnEnterUnfinishedState` doesn't fire on save load, persistent emergencies don't register. Mitigation: add an `IInitializableEntity.InitializeEntity` hook that re-registers based on `IsEmergency`. Pattern matches base game's `ConstructionRegistrar` so probably works. **First in-game scenario to verify.**

- **Cross-mod Harmony patch ordering.** Any other mod patching the same methods may interact unpredictably with our prefixes, especially patch 3 returning `false` to skip the original — that short-circuits other prefixes too. Use Harmony priority annotations if conflicts arise.

- **Reflection fragility.** All private-field access is reflection-based, cached in `static readonly` `FieldInfo` / `MethodInfo`. Static-init guards log when these are null.

- **No top-bar area tool.** `BuilderPrioritiesButton` (paint priority over an area) doesn't have an Emergency option. Skipped intentionally for Phase 1.

- **English only.** Add `frFR_*.csv`, `deDE_*.csv`, etc. in `Data/Localizations/` for translations.

- **HashSet iteration order is undefined.** When multiple sites are flagged, the pick order is arbitrary. Acceptable since "all emergency tasks are equally urgent" is consistent with intent.

- **No `[HarmonyPriority]` annotations.** Our patches don't declare priority relative to other mods. Add `[HarmonyPriority]` if conflicts surface.

---

## Tooling and source references

### On disk

- **Modding repo (Unity project):** `D:\claude\timberborn-modding`
- **Decompiled game source:** `D:\claude\timberborn-decompiled\` — one subfolder per DLL. Use `Grep`/`Read` here (not Python/dnfile scripts).
- **Game files (USS, UXML, blueprints):** `D:\claude\Timberborn\` and `Assets/Tools/ImportedAssets/Editor/Resources/UI/` for `.uss.txt` / `.uxml.txt` editor copies.
- **Wiki (cloned):** `D:\claude\timberborn-modding.wiki\` — `Coding basics.md`, `Timberborn-architecture.md`, `Mod-directory-structure.md` most relevant.
- **Example mods:** `Assets/Mods/HelloWorld` and `Assets/Mods/BeaverTaskDisplay`. BeaverTaskDisplay's `CLAUDE.md` is the closest reference for fragment + DI patterns and reflection-on-private-fields tactics.

### DLLs inspected during Phase 1

- `Timberborn.PrioritySystem` / `PrioritySystemUI` — `Priority`, `IPrioritizable`, `PriorityToggle`, `PriorityToggleGroup`, `PriorityToggleGroupFactory`
- `Timberborn.BuilderPrioritySystem` / `BuilderPrioritySystemUI` — `BuilderPrioritizable`, `BuilderPriorityToggleGroupFactory`, `BuilderPrioritySpriteLoader`
- `Timberborn.ConstructionSites` / `ConstructionSitesUI` — `ConstructionSite`, `ConstructionJob`, `ConstructionRegistrar`, `ConstructionRegistry`, `ConstructionSiteFragment`
- `Timberborn.BuilderHubSystem` — `BuilderHubWorkplaceBehavior`, `BuildingJobProvider`, `IBuilderJobProvider`
- `Timberborn.WorkSystem` / `WorkSystemUI` — `WorkerRootBehavior`, `Worker`, `Workplace`, `WorkRefuser`, `WorkerWorkingHours`, `WorkplacePriorityToggleGroupFactory`
- `Timberborn.BehaviorSystem` — `BehaviorAgent`, `Behavior`, `Decision`, `BehaviorManager`, `RootBehavior`
- `Timberborn.CoreUI` — `VisualElementLoader`, `VisualElementInitializer`, `UISoundInitializer`, `IVisualElementInitializer`
- `Timberborn.UISound` — `UISoundController`
- `Timberborn.RecoveredGoodSystemUI`, `Timberborn.DemolishingUI` — other consumers of `BuilderPriorityToggleGroupFactory`
- `Timberborn.BeaverBehavior` — `BeaverBehaviorInitializer`, `BeaverNeedBehaviorPicker` (for Phase 2)
- `Timberborn.SleepSystem` — `SleepNeedBehavior`, `Sleeper`, `SleeperSpec` (for Phase 2)
- `Timberborn.NeedBehaviorSystem` — `CriticalNeederRootBehavior`, `NeederRootBehavior`, `DistrictNeedBehaviorService`, `Appraiser`, `NeedFilter` (for Phase 3)
- `Timberborn.BlockSystem` — `IUnfinishedStateListener`
- `Timberborn.WorldPersistence` / `Persistence` — `IPersistentEntity`, `ComponentKey`, `PropertyKey<T>`
- `Timberborn.ModManagerScene` — `IModStarter`, `ModCodeStarter`
- `Timberborn.SingletonSystem` — `ILoadableSingleton`
- `Timberborn.TemplateInstantiation` — `TemplateModule.Builder`
- `Timberborn.BaseComponentSystem` — `BaseComponent`, `IAwakableComponent`

### Harmony / Lib.Harmony

- Package: `com.emka.timberborn-harmony` v2.4.2 (wraps Lib.Harmony)
- Repo: https://github.com/eMkaQQ/timberborn-harmony
- Player-facing mod (mod.io / Steam Workshop): `Id: "Harmony"` — required at runtime.

---

## Workflow notes (for future Claude sessions)

- Memory files document persistent project conventions: see `~/.claude/projects/D--claude-timberborn-modding/memory/` for the no-Co-Authored-By rule, decompiled-source-only investigation, prefer-bash-over-PowerShell, and the Harmony-as-shared-dependency convention.
- Plan major behavior changes with the user before writing patches. Phased rollouts (Phase 1 ship → test → Phase 2) reduce blast radius when patches go wrong.
- Self-review (`/anthropic-skills:code-review`) catches things like the `is not Worker worker` pattern footgun before the first compile. Use `as` casts instead.
- When verifying claims about Unity / Harmony / external libs against the web, prefer authoritative sources (mod's own README, NuGet for Lib.Harmony, Microsoft docs for C# spec).
- After creating new files in `Assets/`, the user usually needs to refresh Unity (Ctrl+R in the Project window) before the Mod Builder lists the new mod. Don't manually create `.meta` files; let Unity generate them.

### Git workflow

The user works in the main repo at `D:\claude\timberborn-modding\`, NOT inside `.claude/worktrees/`. The worktree is session-side scaffolding; files there are separate from what the user opens in Unity. Always run `git status` from the main repo path.

When committing, exclude:
- Unrelated changes (other mods' edits that aren't part of the current task)
- `.claude/worktrees/...` (session infrastructure)

Include:
- `Packages/manifest.json` and `Packages/packages-lock.json` if Unity package dependencies changed
- `timberborn-modding.slnx` if a new project was added to the solution
- All mod files and `.meta` files under `Assets/Mods/<mod-name>/`
