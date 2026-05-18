# EmergencyPriority mod

A Timberborn 1.0 mod that adds a 6th "Emergency" priority above Very High for construction sites. Beavers and bots employed at a Builder Hub work emergency-flagged jobs before anything else, ignoring their working hours and (Phase 2) scheduled sleep until needs become critical. Critical needs are still handled by the base game's `CriticalNeederRootBehavior` so beavers don't die.

## Status

**Phase 1 complete (untested)** — scaffold, Emergency component, registry, UI toggle, construction-job override, schedule override. Mod has been written but never compiled or run.

**Phase 2 pending** — patch `BeaverNeedBehaviorPicker.ShouldPickEssentialAction` (suppress scheduled sleep) and `SleepNeedBehavior.ShouldSleepAtHome` (force the sleep-outside path so builders sleep near worksite instead of walking home).

**Phase 3 pending** — patch `DistrictNeedBehaviorService.PickShortestAction` so beavers in critical food/water state grab the closest source regardless of preference.

**Stretch/optional** — extend the Emergency toggle to the top-bar BuilderPrioritiesButton area tool (paint Emergency over an area). Not in scope for Phases 1–3.

---

## File structure

```
Assets/Mods/EmergencyPriority/
├── CLAUDE.md                                     # this file
├── manifest.json                                 # Id: grantemsley.EmergencyPriority; requires Harmony >= 2.3.0
├── Data/
│   └── Localizations/
│       └── enUS_EmergencyPriority.csv
└── Scripts/
    ├── grantemsley.EmergencyPriority.asmdef     # autoReferenced: false, allowUnsafeCode: true
    ├── EmergencyConstructable.cs                # BaseComponent on construction sites
    ├── EmergencyConstructionRegistry.cs         # singleton; HashSet of emergency-flagged ConstructionJobs
    ├── EmergencyPatchBootstrap.cs               # ILoadableSingleton that hands registry to static patch fields
    ├── EmergencyPriorityConfigurator.cs         # Bindito DI + ConstructionSite→EmergencyConstructable decorator
    ├── EmergencyPriorityModStarter.cs           # IModStarter; calls Harmony.PatchAll
    ├── EmergencyToggleController.cs             # lifecycle/state for the 6th toggle (Enable/Disable/UpdateState)
    └── Patches/
        ├── BuilderHubWorkplaceBehaviorPatch.cs  # prefix Decide → try emergency jobs first
        ├── BuilderPriorityToggleGroupFactoryPatch.cs  # postfix Create → inject 6th toggle
        ├── PriorityToggleGroupPatch.cs          # postfix Enable/Disable/UpdateGroup → dispatch to controller
        └── WorkerRootBehaviorPatch.cs           # prefix Decide → bypass AreWorkingHours for builders
```

`Data/Localizations/enUS_*.csv` is copied verbatim to `Localizations/` in the built mod (Mod-Builder convention).

Built mod lands in `Documents/Timberborn/Mods/grantemsley.EmergencyPriority/`.

---

## Build

Unity project at `D:\claude\timberborn-modding`, Unity **6000.0.16f1**, `-langversion:9.0`.

1. Open the project in Unity.
2. Wait for Package Manager to fetch `com.emka.timberborn-harmony` (declared in `Packages/manifest.json`). If it fails: Window → Package Manager → + → Add package from git URL → `https://github.com/eMkaQQ/timberborn-harmony.git`.
3. Verify no compile errors in the Console.
4. **Timberborn → Show Mod Builder** → tick "Emergency Priority" → **Build all**.

At runtime, the player also needs the `Harmony` mod from mod.io or Steam Workshop installed (matches `manifest.json` `RequiredMods`).

---

## Architecture

### Harmony dependency model

Timberborn mods do **not** bundle their own `0Harmony.dll`. The community convention is to declare `{ "Id": "Harmony", "MinimumVersion": "2.3.0" }` in `manifest.json` `RequiredMods`. Players install the shared `Harmony` mod separately; mod developers install the Unity package `com.emka.timberborn-harmony` for compile-time references. This avoids version conflicts when multiple mods use Harmony.

If we ever need to vendor Harmony (e.g. for a standalone distribution), drop `0Harmony.dll` into `Scripts/` and remove the RequiredMod entry — but don't do this if other Harmony mods will be active.

### Two parallel priority systems

The game already has a `Priority` enum (5 values: VeryLow…VeryHigh) consumed throughout compiled code. We can't extend it. Instead, Emergency is a **separate orthogonal bool flag** stored on each construction site via `EmergencyConstructable`. The base game's priority sort still runs; our Harmony prefixes inject emergency jobs ahead of the sort.

When Emergency is cleared, the building reverts to its underlying `BuilderPrioritizable.Priority`. This means the UI shows two independent checked states: one from the regular priority radio group and one from the Emergency toggle. That's intentional — Emergency is a temporary boost on top of the normal priority.

### Component lifecycle (`EmergencyConstructable`)

Decorated onto every `ConstructionSite`. Implements:
- `IAwakableComponent` — `Awake()` caches `_constructionJob = GetComponent<ConstructionJob>()`.
- `IUnfinishedStateListener` — `OnEnterUnfinishedState` / `OnExitUnfinishedState` track whether the site is currently buildable, and register/unregister with the registry if `IsEmergency` is true at the transition.
- `IPersistentEntity` — saves only when `IsEmergency` is true (mirrors `BuilderPrioritizable.Save`).

`SetEmergency(bool)` is the only external mutation entry point. It updates `IsEmergency` and, if the site is currently unfinished, registers or unregisters with `EmergencyConstructionRegistry`.

Load order is `Awake → Load → OnEnterUnfinishedState`. By the time the state listener fires, `IsEmergency` is correctly loaded from the save and the registry gets populated. **Untested** on save-load; if `OnEnterUnfinishedState` doesn't fire on load (only on initial placement), saved emergencies won't register and we'd need to add an `IInitializableEntity.InitializeEntity` hook. The base game's `ConstructionRegistrar` uses the same pattern so this is expected to work.

### Registry (`EmergencyConstructionRegistry`)

Single `HashSet<ConstructionJob>`. Exposes `HasAny` (O(1)) and `EmergencyJobs` (the live set, for iteration). Patches read this from their hot paths; the `HasAny` short-circuit means non-emergency state is one bool check.

Bound as singleton in `EmergencyPriorityConfigurator`. `EmergencyPatchBootstrap` (`ILoadableSingleton`) injects the registry and assigns it to the static `Registry` properties on `BuilderHubWorkplaceBehaviorPatch` and `WorkerRootBehaviorPatch` at scene load. This is the bridge between DI-resolved state and static Harmony patches.

### The four Harmony patches

| # | Target | Type | Purpose |
|---|---|---|---|
| 1a | `BuilderPriorityToggleGroupFactory.Create` | Postfix | Inject a 6th red toggle into the construction-site priority widget. Stores an `EmergencyToggleController` in a `ConditionalWeakTable<PriorityToggleGroup, EmergencyToggleController>` so dead groups from prior scene loads get garbage-collected. |
| 1b | `PriorityToggleGroup.{Enable,Disable,UpdateGroup}` | Postfix ×3 | Look up the controller for `__instance` and dispatch the lifecycle call. Workplace priority panels miss the lookup (not in the table) and are unaffected. |
| 2 | `BuilderHubWorkplaceBehavior.Decide` | Prefix | If the registry has any jobs, iterate them and call `ConstructionJob.StartConstructionJob(agent, accessible)` directly. First accepting job short-circuits the original `Priorities.Descending` loop. Reads `_accessible` via reflection. |
| 3 | `WorkerRootBehavior.Decide` | Prefix | If registry has any jobs AND the beaver's workplace contains a `BuilderHubWorkplaceBehavior` AND `!WorkRefuser.RefusesWork`, invoke private `DecideAsWorker()` via reflection — skipping the `AreWorkingHours` gate. Bots use the same code path, so they're also affected. |

`CriticalNeederRootBehavior` runs above `WorkerRootBehavior` in the behavior tree, so beavers in critical-need state still preempt the emergency override. That's how we get "ignore needs but don't die": critical state always wins.

### Static-constructor reflection guards

Each patch's static constructor logs `Debug.LogError` (or `LogWarning` for cosmetic reflection like the sprite loader) if any private field/method lookup returned null. The patch then gracefully no-ops. This makes a game-version-induced rename visible in the Unity log instead of mysteriously broken behavior.

### UI integration (`EmergencyToggleController`)

Mirrors `PriorityToggle`'s lifecycle methods (`Enable(IPrioritizable)`, `Disable()`, `UpdateState()`) without subclassing or modifying `PriorityToggleGroup._toggles`. We create a vanilla `new Toggle()`, attach the standard `priority-toggle` and `content-centered` USS classes, hide its default Label (BaseField slot — class name varies by Unity version, so we hide any Label descendant), and tint both the toggle background and the checkmark image red.

The current entity's `EmergencyConstructable` is looked up via `(prioritizable as BaseComponent)?.GetComponent<EmergencyConstructable>()`. `IPrioritizable` is implemented by `BuilderPrioritizable : BaseComponent`, so the cast succeeds for construction sites.

The 6th toggle uses the `VeryHigh` panel sprite (loaded via reflection on `BuilderPriorityToggleGroupFactory._builderPrioritySpriteLoader`) tinted red. If the sprite loader can't be found, the toggle still works but has no icon (logged as a warning).

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

Two factories wrap the generic `PriorityToggleGroupFactory`:
- `BuilderPriorityToggleGroupFactory` — used by [ConstructionSiteFragment](D:\claude\timberborn-decompiled\Timberborn.ConstructionSitesUI\Timberborn.ConstructionSitesUI\ConstructionSiteFragment.cs).
- `WorkplacePriorityToggleGroupFactory` — used by `WorkplaceFragment`.

Patching the generic factory affects both. Patching the builder-specific factory is the right scope for Emergency.

The widget UXML loads `Game/EntityPanel/PriorityToggleGroup` (a `NineSliceVisualElement` containing a `Label` and a `TogglesWrapper`). Each toggle is its own UXML (`Game/EntityPanel/PriorityToggle`) — a `<ui:Toggle name="PriorityToggle" class="priority-toggle content-centered" />` with inline-styled background-image (the priority icon) and tint color.

USS classes:
- `.priority-toggle` — 24×24, with `--click-sound: "UI.Click"`
- `.priority-toggle--checked` — adds background image `UI/Images/Game/priority-toggle-checked` (the "selected" highlight behind the icon)

We construct toggles by hand with `new Toggle()` (no UXML loader available from a Harmony postfix without reflecting into the factory's `_visualElementLoader`).

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

6. **Static-init reflection guards.** Game patches may rename `_accessible`, `_worker`, `_workRefuser`, `DecideAsWorker`, etc. Silent no-op is the worst failure mode for the user. A logged error tells them what to file a bug about.

7. **Don't patch `CriticalNeederRootBehavior`.** "Ignore needs as long as possible without dying" — the base game's critical-state detection is exactly the threshold we want. By keeping critical above our schedule override in the behavior tree, beavers always preempt for actual death-level needs.

8. **`Lib.Harmony 2.4.2` via the shared `com.emka.timberborn-harmony` package.** Avoids the type-conflict footgun from each mod bundling its own DLL. `manifest.json` `RequiredMods` enforces the runtime dependency.

9. **Bots get patch 2 & 3 implicitly.** Bots use `WorkerRootBehavior` and `BuilderHubWorkplaceBehavior` too. The user wanted bots to work emergencies nonstop — which is what they do, since bots have no need-based behaviors to interfere.

10. **Phased rollout.** Phase 1 (emergency priority + schedule override) is the headline feature. Sleep-on-spot and closest-food are refinements. Splitting them limits risk per ship.

---

## Known limitations and risks

- **Phase 1 has never been compiled or run.** First Unity open will catch compile errors. The most fragile areas to check: `using HarmonyLib;` resolution, Toggle internals (checkmark/label query), and whether the Mod Builder includes everything correctly.

- **Save-load behavior is unverified.** If `OnEnterUnfinishedState` doesn't fire on save load, persistent emergencies don't register. Mitigation: add an `IInitializableEntity.InitializeEntity` hook that re-registers based on `IsEmergency`. Pattern matches base game's `ConstructionRegistrar` so probably works.

- **Cross-mod Harmony patch ordering.** Any other mod patching the same methods may interact unpredictably with our prefixes, especially patch 3 returning `false` to skip the original — that short-circuits other prefixes too. Use Harmony priority annotations if conflicts arise.

- **Reflection fragility.** All private-field access is reflection-based, cached in `static readonly` `FieldInfo` / `MethodInfo`. Static-init guards log when these are null.

- **UI**: Toggle visual styling assumes Unity's default `Toggle` visual tree has a `unity-checkmark` child VisualElement. If a Unity version changes that, the icon won't appear (toggle still functional). Hidden Label slot may also reserve layout space in some Unity versions — visual check needed.

- **No top-bar area tool.** `BuilderPrioritiesButton` (paint priority over an area) doesn't have an Emergency option. Skipped intentionally for Phase 1.

- **English only.** Add `frFR_*.csv`, `deDE_*.csv`, etc. in `Data/Localizations/` for translations.

- **No tooltip on the Emergency toggle specifically.** Inherits the generic "Priorities" tooltip from the toggle wrapper. Cosmetic.

---

## Phase 2 plan (sleep override)

Two more patches:

- **`BeaverNeedBehaviorPicker.ShouldPickEssentialAction`** (prefix) — if `__instance._needManager`'s owning beaver has Worker→Workplace with a BuilderHubWorkplaceBehavior AND registry has emergency jobs, bypass `ItIsTimeForEssentialAction` (the close-to-dawn scheduled-sleep trigger) and set `__result = __instance.EssentialActionIsAtMinimumPoints(...)`. Sleep still wins when sleep need bottoms out.

- **`SleepNeedBehavior.ShouldSleepAtHome`** (prefix) — same builder-emergency check; return `__result = false` to force the `SleepOutside` path. `WalkToRandomSleepingPosition` then picks a spot near the beaver's current position (which will be the worksite, since they were working).

Caching the "is this beaver an emergency builder" check on a `ConditionalWeakTable<BeaverNeedBehaviorPicker, bool>` keyed on instance is probably worth it for perf — the patch fires per-beaver per-tick.

## Phase 3 plan (closest food)

- **`DistrictNeedBehaviorService.PickShortestAction`** (prefix) — if the calling beaver (via `needManager.Owner`) is a builder in emergency mode AND `onlyNeedsInCriticalState` is true, replace the points-sorted iteration with a globally-shortest-duration scan across all `_appraisedNeedBehaviors`. Set `__result` and return false. Use `ActionDurationCalculator.DurationWithReturnInHours` for consistency with game's existing logic.

The non-critical path is untouched — only "critically hungry/thirsty" beavers ignore preference.

---

## Tooling and source references

### On disk

- **Modding repo (Unity project):** `D:\claude\timberborn-modding`
- **Decompiled game source:** `D:\claude\timberborn-decompiled\` — one subfolder per DLL. Use `Grep`/`Read` here (not Python/dnfile scripts).
- **Game files (USS, UXML, blueprints):** `D:\claude\Timberborn\` and `Assets/Tools/ImportedAssets/Editor/Resources/UI/` for `.uss.txt` / `.uxml.txt` editor copies.
- **Wiki (cloned):** `D:\claude\timberborn-modding.wiki\` — `Coding basics.md`, `Timberborn-architecture.md`, `Mod-directory-structure.md` most relevant.
- **Example mods:** `Assets/Mods/HelloWorld` and `Assets/Mods/BeaverTaskDisplay`. BeaverTaskDisplay's `CLAUDE.md` is the closest reference for fragment + DI patterns and reflection-on-private-fields tactics.

### DLLs inspected during Phase 1

- `Timberborn.PrioritySystem` / `PrioritySystemUI` — `Priority`, `IPrioritizable`, `PriorityToggleGroup`, `PriorityToggleGroupFactory`
- `Timberborn.BuilderPrioritySystem` / `BuilderPrioritySystemUI` — `BuilderPrioritizable`, `BuilderPriorityToggleGroupFactory`, `BuilderPrioritySpriteLoader`
- `Timberborn.ConstructionSites` / `ConstructionSitesUI` — `ConstructionSite`, `ConstructionJob`, `ConstructionRegistrar`, `ConstructionRegistry`, `ConstructionSiteFragment`
- `Timberborn.BuilderHubSystem` — `BuilderHubWorkplaceBehavior`, `BuildingJobProvider`, `IBuilderJobProvider`
- `Timberborn.WorkSystem` / `WorkSystemUI` — `WorkerRootBehavior`, `Worker`, `Workplace`, `WorkRefuser`, `WorkerWorkingHours`, `WorkplacePriorityToggleGroupFactory`
- `Timberborn.BehaviorSystem` — `BehaviorAgent`, `Behavior`, `Decision`, `BehaviorManager`, `RootBehavior`
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
- Self-review (`/anthropic-skills:code-review`) catches things like the `is not Worker worker` pattern footgun before the first compile.
- When verifying claims about Unity / Harmony / external libs against the web, prefer authoritative sources (mod's own README, NuGet for Lib.Harmony, Microsoft docs for C# spec).
