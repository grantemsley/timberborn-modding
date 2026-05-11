using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Timberborn.BaseComponentSystem;
using Timberborn.BehaviorSystem;
using Timberborn.Carrying;
using Timberborn.CoreUI;
using Timberborn.EntityNaming;
using Timberborn.EntityPanelSystem;
using Timberborn.GameFactionSystem;
using Timberborn.Localization;
using Timberborn.SelectionSystem;
using Timberborn.ReservableSystem;
using Timberborn.WalkingSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace grantemsley.BeaverTaskDisplay {

  internal class BeaverTaskFragment : IEntityPanelFragment {

    private const string SubPanelClass = "entity-sub-panel";
    private const string SubBoxClass = "bg-sub-box--green";

    private const string IdleLocKey      = "grantemsley.BeaverTaskDisplay.Idle";
    private const string TaskPrefixLocKey = "grantemsley.BeaverTaskDisplay.TaskPrefix";
    private const string HaulFromLocKey  = "grantemsley.BeaverTaskDisplay.Walk.HaulFrom";
    private const string HaulToLocKey    = "grantemsley.BeaverTaskDisplay.Walk.HaulTo";

    // Maps executor class name → loc key for non-walking executors.
    private static readonly Dictionary<string, string> ExecutorLocKeys = new() {
      { "WaitExecutor",              "grantemsley.BeaverTaskDisplay.Executor.Waiting" },
      { "BuildExecutor",             "grantemsley.BeaverTaskDisplay.Executor.Building" },
      { "DemolishExecutor",          "grantemsley.BeaverTaskDisplay.Executor.Demolishing" },
      { "PlantExecutor",             "grantemsley.BeaverTaskDisplay.Executor.Planting" },
      { "WalkToAccessibleExecutor",  "grantemsley.BeaverTaskDisplay.Executor.WalkingTo" },
      { "WalkToReservableExecutor",  "grantemsley.BeaverTaskDisplay.Executor.WalkingTo" },
      { "WorkAtReservableExecutor",  "grantemsley.BeaverTaskDisplay.Executor.Working" },
      { "WalkInsideExecutor",        "grantemsley.BeaverTaskDisplay.Executor.Entering" },
      { "WalkToPositionExecutor",    "grantemsley.BeaverTaskDisplay.Executor.Walking" },
      { "ProduceExecutor",           "grantemsley.BeaverTaskDisplay.Executor.Producing" },
      { "WorkExecutor",              "grantemsley.BeaverTaskDisplay.Executor.Working" },
      { "RemoveYieldExecutor",       "grantemsley.BeaverTaskDisplay.Executor.Harvesting" },
    };

    // Keys are the runtime values of ApplyEffectExecutor._animationName (a private field).
    // These were confirmed by inspecting the game DLLs. If a key doesn't match, the
    // NeedId fallback in GetApplyEffectTaskText is used instead.
    private static readonly Dictionary<string, string> AnimationLocKeys = new() {
      { "Eating",    "grantemsley.BeaverTaskDisplay.Animation.Eating" },
      { "Drinking",  "grantemsley.BeaverTaskDisplay.Animation.Drinking" },
      { "Sleeping",  "grantemsley.BeaverTaskDisplay.Animation.Sleeping" },
      { "Bathing",   "grantemsley.BeaverTaskDisplay.Animation.Bathing" },
      { "HavingFun", "grantemsley.BeaverTaskDisplay.Animation.HavingFun" },
      { "Healing",   "grantemsley.BeaverTaskDisplay.Animation.Healing" },
      { "Resting",   "grantemsley.BeaverTaskDisplay.Animation.Resting" },
    };

    // Behavior class names are Timberborn internals discovered by inspecting game DLLs
    // and confirmed via runtime logging (BehaviorManager.RunningBehavior.Name).
    // To discover new behavior names: uncomment the BeaverTaskScanner class and the
    // scanner initialization in InitializeFragment, build the mod, and play. The scanner
    // logs each new executor+behavior combination once to Player.log. Re-comment when done.
    //
    // These are prefix loc keys: the building name is appended via _destinationLabel.
    // Translators write only the prefix; the building name always follows in the UI.
    private static readonly Dictionary<string, string> WalkBehaviorPrefixKeys = new() {
      { "LaborWorkplaceBehavior",          "grantemsley.BeaverTaskDisplay.Walk.WorkAt" },
      { "PlanterWorkplaceBehavior",        "grantemsley.BeaverTaskDisplay.Walk.WorkAt" },
      { "BuildBehavior",                   "grantemsley.BeaverTaskDisplay.Walk.BuildAt" },
      { "DemolishBehavior",               "grantemsley.BeaverTaskDisplay.Walk.DemolishAt" },
      { "GatherWorkplaceBehavior",         "grantemsley.BeaverTaskDisplay.Walk.HarvestAt" },
      { "LumberjackFlagWorkplaceBehavior", "grantemsley.BeaverTaskDisplay.Walk.HarvestAt" },
      { "YieldRemoverBehavior",            "grantemsley.BeaverTaskDisplay.Walk.HarvestAt" },
      { "InventoryNeedBehavior",           "grantemsley.BeaverTaskDisplay.Walk.EatAt" },
      { "SleepNeedBehavior",              "grantemsley.BeaverTaskDisplay.Walk.SleepAt" },
      { "AttractionNeedBehavior",         "grantemsley.BeaverTaskDisplay.Walk.VisitAt" },
      { "ProduceWorkplaceBehavior",       "grantemsley.BeaverTaskDisplay.Walk.WorkAt" },
      // CarryRootBehavior and HaulWorkplaceBehavior handled inline — need GoodCarrier.IsCarrying check
    };

    // Complete text for WalkToPositionExecutor when behavior provides context.
    // No building name is appended since position walks have no entity destination.
    private static readonly Dictionary<string, string> PositionWalkBehaviorKeys = new() {
      { "PlanterWorkplaceBehavior", "grantemsley.BeaverTaskDisplay.Walk.Plant" },
      { "SleepNeedBehavior",        "grantemsley.BeaverTaskDisplay.Walk.Sleep" },
    };

    // Text shown when there is no running executor but a behavior is active (between executors).
    private static readonly Dictionary<string, string> BehaviorOnlyLocKeys = new() {
      { "EmptyOutputWorkplaceBehavior", "grantemsley.BeaverTaskDisplay.Behavior.EmptyingOutput" },
      { "FillInputWorkplaceBehavior",   "grantemsley.BeaverTaskDisplay.Behavior.FillingInput" },
    };

    // Reuses Animation.* loc keys since the display text is the same whether the beaver
    // is actively eating (ApplyEffectExecutor with animation name) or was directed to eat
    // via a slot-based building that sets no animation name (e.g. medical bed).
    // Buildings with unmapped NeedIds (e.g. "Lido") fall back to the game's own display name.
    private static readonly Dictionary<string, string> NeedIdLocKeys = new() {
      { "Hunger",  "grantemsley.BeaverTaskDisplay.Animation.Eating" },
      { "Thirst",  "grantemsley.BeaverTaskDisplay.Animation.Drinking" },
      { "Sleep",   "grantemsley.BeaverTaskDisplay.Animation.Sleeping" },
      { "Injury",  "grantemsley.BeaverTaskDisplay.Animation.Healing" },
      { "WetFur",  "grantemsley.BeaverTaskDisplay.Animation.Bathing" },
    };

    // The game DLLs are not publicized, so private fields on game types must be accessed
    // via reflection. These FieldInfo handles are cached statically so the lookup cost
    // (GetField call) is paid once per type, not per frame.
    private static readonly FieldInfo BehaviorManagerRunningExecutorField =
        typeof(BehaviorManager).GetField(
            "_runningExecutor", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo WalkToAccessibleAccessibleField =
        typeof(WalkToAccessibleExecutor).GetField(
            "_accessible", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo WalkInsideBuildingAccessibleField =
        typeof(WalkInsideExecutor).GetField(
            "_buildingAccessible", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo WalkToReservableReservableField =
        typeof(WalkToReservableExecutor).GetField(
            "_reservable", BindingFlags.NonPublic | BindingFlags.Instance);

    // Cached lazily on first ApplyEffectExecutor encounter (type lives in a separate assembly).
    private static FieldInfo _applyEffectAnimNameField;
    private static FieldInfo _applyEffectEffectsField;

    private static readonly Color DestinationHighlightColor = new(0.5f, 0.15f, 0f, 0.5f); // dark orange, semi-transparent

    private readonly EntitySelectionService _entitySelectionService;
    private readonly SelectableObjectRetriever _selectableObjectRetriever;
    private readonly FactionNeedService _factionNeedService;
    private readonly Highlighter _highlighter;
    private readonly ILoc _loc;

    private VisualElement _root;
    private Label _taskLabel;
    private Label _destinationLabel;

    private BehaviorManager _behaviorManager;
    private BaseComponent _currentDestEntity;

    public BeaverTaskFragment(EntitySelectionService entitySelectionService,
                              SelectableObjectRetriever selectableObjectRetriever,
                              FactionNeedService factionNeedService,
                              Highlighter highlighter,
                              ILoc loc) {
      _entitySelectionService = entitySelectionService;
      _selectableObjectRetriever = selectableObjectRetriever;
      _factionNeedService = factionNeedService;
      _highlighter = highlighter;
      _loc = loc;
    }

    public VisualElement InitializeFragment() {
      _root = new NineSliceVisualElement();
      _root.AddToClassList(SubPanelClass);
      _root.AddToClassList(SubBoxClass);
      _root.ToggleDisplayStyle(false);

      // Horizontal row so task and destination sit on the same line and wrap together.
      var row = new VisualElement();
      row.style.flexDirection = FlexDirection.Row;
      row.style.flexWrap = Wrap.Wrap;

      _taskLabel = new Label { text = string.Empty };
      _taskLabel.AddToClassList("entity-panel__text");
      row.Add(_taskLabel);

      _destinationLabel = new Label { text = string.Empty };
      _destinationLabel.AddToClassList("entity-panel__text");
      _destinationLabel.style.color = new Color(0.70f, 0.85f, 1f, 1f);
      _destinationLabel.RegisterCallback<ClickEvent>(_ => OnDestinationClicked());
      _destinationLabel.style.display = DisplayStyle.None;
      row.Add(_destinationLabel);

      _root.Add(row);

      // To re-enable the behavior scanner (for discovering new executor+behavior names):
      // uncomment the block below and the BeaverTaskScanner class at the bottom of this file,
      // build the mod, then play and check Player.log for [BTD scan] lines.
      //
      // if (!_scannerStarted) {
      //   _scannerStarted = true;
      //   new GameObject("[BTD_Scanner]").AddComponent<BeaverTaskScanner>();
      // }

      return _root;
    }

    public void ShowFragment(BaseComponent entity) {
      var bm = entity.GetComponent<BehaviorManager>();
      var walker = entity.GetComponent<Walker>();
      if (bm != null && walker != null) {
        _behaviorManager = bm;
        _root.ToggleDisplayStyle(true);
        Refresh();
      }
    }

    public void ClearFragment() {
      _behaviorManager = null;
      if (_currentDestEntity != null) {
        _highlighter.UnhighlightAllSecondary();
        _currentDestEntity = null;
      }
      _destinationLabel.style.display = DisplayStyle.None;
      _root.ToggleDisplayStyle(false);
    }

    public void UpdateFragment() {
      if (_behaviorManager != null) {
        Refresh();
      }
    }

    private void Refresh() {
      var actualExecutor = BehaviorManagerRunningExecutorField?.GetValue(_behaviorManager) as IExecutor;
      _taskLabel.text = GetTaskText(actualExecutor);

      var destEntity = TryGetDestinationEntity(actualExecutor);
      if (destEntity != _currentDestEntity) {
        if (_currentDestEntity != null) {
          _highlighter.UnhighlightAllSecondary();
        }
        _currentDestEntity = destEntity;
        if (destEntity != null) {
          _highlighter.HighlightSecondary(destEntity, DestinationHighlightColor);
        }
      }
      if (destEntity != null) {
        var named = destEntity.GetComponent<NamedEntity>();
        var displayName = named != null ? named.EntityName : destEntity.GameObject.name;
        _destinationLabel.text = " " + displayName;
        _destinationLabel.style.display = DisplayStyle.Flex;
      } else {
        _destinationLabel.style.display = DisplayStyle.None;
      }
    }

    private string GetTaskText(IExecutor executor) {
      if (executor == null || string.IsNullOrEmpty(_behaviorManager.RunningExecutor.Name)) {
        var behaviorOnly = _behaviorManager.RunningBehavior.Name;
        if (!string.IsNullOrEmpty(behaviorOnly) &&
            BehaviorOnlyLocKeys.TryGetValue(behaviorOnly, out var bOnlyKey)) {
          return _loc.T(TaskPrefixLocKey, _loc.T(bOnlyKey));
        }
        return _loc.T(IdleLocKey);
      }

      var typeName = executor.GetType().Name;
      var behaviorName = _behaviorManager.RunningBehavior.Name;

      // WalkToPositionExecutor: no entity destination, but behavior may provide context (e.g. planting).
      if (typeName == "WalkToPositionExecutor") {
        if (!string.IsNullOrEmpty(behaviorName) &&
            PositionWalkBehaviorKeys.TryGetValue(behaviorName, out var posKey)) {
          return _loc.T(TaskPrefixLocKey, _loc.T(posKey));
        }
        // fall through → ExecutorLocKeys["WalkToPositionExecutor"] = "Walking"
      }

      // Entity-walking executors: use behavior name to show intent rather than generic "Walking to".
      if (typeName is "WalkToAccessibleExecutor" or "WalkToReservableExecutor" or "WalkInsideExecutor") {
        if (!string.IsNullOrEmpty(behaviorName)) {
          string prefixKey = null;
          if (behaviorName == "HaulWorkplaceBehavior" || behaviorName == "CarryRootBehavior") {
            var carrier = _behaviorManager.GetComponent<GoodCarrier>();
            prefixKey = (carrier != null && carrier.IsCarrying) ? HaulToLocKey : HaulFromLocKey;
          } else {
            WalkBehaviorPrefixKeys.TryGetValue(behaviorName, out prefixKey);
          }
          if (prefixKey != null) {
            return _loc.T(TaskPrefixLocKey, _loc.T(prefixKey));
          }
        }
        // Unknown behavior → fall through to ExecutorLocKeys ("Walking to" / "Entering")
      }

      if (typeName == "ApplyEffectExecutor") {
        return GetApplyEffectTaskText(executor);
      }

      if (ExecutorLocKeys.TryGetValue(typeName, out var locKey)) {
        return _loc.T(TaskPrefixLocKey, _loc.T(locKey));
      }

      return _loc.T(TaskPrefixLocKey, typeName);
    }

    // Determines the display text for ApplyEffectExecutor, which covers all need-satisfaction
    // activities (eating, drinking, sleeping, bathing, healing, etc.).
    //
    // Fallback chain:
    //   1. _animationName field → AnimationLocKeys (most specific; set for standard need buildings)
    //   2. _effects[].NeedId → NeedIdLocKeys (slot-based buildings like medical bed have no animation)
    //   3. _effects[0].NeedId → FactionNeedService display name (game's own name for unmapped needs)
    //   4. Raw type name as last resort
    private string GetApplyEffectTaskText(IExecutor executor) {
      _applyEffectAnimNameField ??= executor.GetType().GetField(
          "_animationName", BindingFlags.NonPublic | BindingFlags.Instance);
      var animName = _applyEffectAnimNameField?.GetValue(executor) as string;
      if (animName != null && AnimationLocKeys.TryGetValue(animName, out var animKey)) {
        return _loc.T(TaskPrefixLocKey, _loc.T(animKey));
      }

      _applyEffectEffectsField ??= executor.GetType().GetField(
          "_effects", BindingFlags.NonPublic | BindingFlags.Instance);
      if (_applyEffectEffectsField?.GetValue(executor) is IEnumerable effects) {
        string firstNeedId = null;
        foreach (var effect in effects) {
          var needId = effect?.GetType().GetProperty("NeedId")?.GetValue(effect) as string;
          if (needId == null) continue;
          firstNeedId ??= needId;
          if (NeedIdLocKeys.TryGetValue(needId, out var needKey)) {
            return _loc.T(TaskPrefixLocKey, _loc.T(needKey));
          }
        }
        if (firstNeedId != null) {
          var needSpec = _factionNeedService.GetBeaverOrBotNeedById(firstNeedId);
          var needLocKey = needSpec?.DisplayNameLocKey;
          return _loc.T(TaskPrefixLocKey, string.IsNullOrEmpty(needLocKey) ? firstNeedId : _loc.T(needLocKey));
        }
      }

      return _loc.T(TaskPrefixLocKey, animName ?? executor.GetType().Name);
    }

    private static BaseComponent TryGetDestinationEntity(IExecutor executor) => executor switch {
      WalkToAccessibleExecutor e => WalkToAccessibleAccessibleField?.GetValue(e) as BaseComponent,
      WalkInsideExecutor e       => WalkInsideBuildingAccessibleField?.GetValue(e) as BaseComponent,
      WalkToReservableExecutor e => WalkToReservableReservableField?.GetValue(e) as BaseComponent,
      _                          => null,
    };

    private void OnDestinationClicked() {
      if (_currentDestEntity == null) return;
      if (_selectableObjectRetriever.TryGetSelectableObject(
              _currentDestEntity.GameObject, out var selectable)) {
        _entitySelectionService.SelectAndFocusOn(selectable);
      }
    }

    // -------------------------------------------------------------------------
    // Debug scanner — uncomment to discover new executor+behavior combinations.
    // See the comment in InitializeFragment for instructions.
    // -------------------------------------------------------------------------
    //
    // private static bool _scannerStarted = false;
    // private static readonly List<WeakReference<BehaviorManager>> _knownManagers = new();
    //
    // private class BeaverTaskScanner : MonoBehaviour {
    //   private static readonly HashSet<string> _seen = new();
    //   private static readonly FieldInfo RunExecField =
    //       typeof(BehaviorManager).GetField("_runningExecutor",
    //           BindingFlags.NonPublic | BindingFlags.Instance);
    //   private float _timer;
    //
    //   private void Update() {
    //     _timer += Time.deltaTime;
    //     if (_timer < 3f) return;
    //     _timer = 0f;
    //     _knownManagers.RemoveAll(r => !r.TryGetTarget(out _));
    //     foreach (var wr in _knownManagers.ToArray()) {
    //       if (!wr.TryGetTarget(out var bm)) continue;
    //       var exec = RunExecField?.GetValue(bm) as IExecutor;
    //       var execName = exec?.GetType().Name ?? "";
    //       if (execName is not ("WalkToAccessibleExecutor" or "WalkToReservableExecutor"
    //                         or "WalkInsideExecutor" or "WalkToPositionExecutor")) continue;
    //       var combo = $"{execName} | {bm.RunningBehavior.Name}";
    //       if (_seen.Add(combo)) Debug.Log($"[BTD scan] {combo}");
    //     }
    //   }
    // }
    //
    // To enable: uncomment above AND uncomment the scanner init block in InitializeFragment,
    // AND add to ShowFragment: _knownManagers.Add(new WeakReference<BehaviorManager>(_behaviorManager));
  }
}
