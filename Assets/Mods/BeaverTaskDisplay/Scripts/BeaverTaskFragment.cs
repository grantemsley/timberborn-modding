using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Timberborn.BaseComponentSystem;
using Timberborn.BehaviorSystem;
using Timberborn.CoreUI;
using Timberborn.EntityNaming;
using Timberborn.EntityPanelSystem;
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

    private const string IdleLocKey = "grantemsley.BeaverTaskDisplay.Idle";
    private const string WalkingToLocKey = "grantemsley.BeaverTaskDisplay.WalkingTo";
    private const string TaskPrefixLocKey = "grantemsley.BeaverTaskDisplay.TaskPrefix";

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

    private static readonly Dictionary<string, string> AnimationLocKeys = new() {
      { "Eating",    "grantemsley.BeaverTaskDisplay.Animation.Eating" },
      { "Drinking",  "grantemsley.BeaverTaskDisplay.Animation.Drinking" },
      { "Sleeping",  "grantemsley.BeaverTaskDisplay.Animation.Sleeping" },
      { "Bathing",   "grantemsley.BeaverTaskDisplay.Animation.Bathing" },
      { "HavingFun", "grantemsley.BeaverTaskDisplay.Animation.HavingFun" },
      { "Healing",   "grantemsley.BeaverTaskDisplay.Animation.Healing" },
      { "Resting",   "grantemsley.BeaverTaskDisplay.Animation.Resting" },
    };

    // Fallback when _animationName is null (e.g. slot-based animations like the medical bed).
    // Maps the NeedId of the first effect to a display string.
    private static readonly Dictionary<string, string> NeedIdLocKeys = new() {
      { "Hunger",  "grantemsley.BeaverTaskDisplay.Animation.Eating" },
      { "Thirst",  "grantemsley.BeaverTaskDisplay.Animation.Drinking" },
      { "Sleep",   "grantemsley.BeaverTaskDisplay.Animation.Sleeping" },
      { "Injury",  "grantemsley.BeaverTaskDisplay.Animation.Healing" },
    };

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

    private readonly EntitySelectionService _entitySelectionService;
    private readonly SelectableObjectRetriever _selectableObjectRetriever;
    private readonly ILoc _loc;

    private VisualElement _root;
    private Label _taskLabel;
    private Label _destinationLabel;

    private BehaviorManager _behaviorManager;
    private BaseComponent _currentDestEntity;

    public BeaverTaskFragment(EntitySelectionService entitySelectionService,
                              SelectableObjectRetriever selectableObjectRetriever,
                              ILoc loc) {
      _entitySelectionService = entitySelectionService;
      _selectableObjectRetriever = selectableObjectRetriever;
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
      _taskLabel.style.color = Color.white;
      row.Add(_taskLabel);

      _destinationLabel = new Label { text = string.Empty };
      _destinationLabel.style.color = new Color(0.70f, 0.85f, 1f, 1f);
      _destinationLabel.RegisterCallback<ClickEvent>(_ => OnDestinationClicked());
      _destinationLabel.style.display = DisplayStyle.None;
      row.Add(_destinationLabel);

      _root.Add(row);
      return _root;
    }

    public void ShowFragment(BaseComponent entity) {
      _behaviorManager = entity.GetComponent<BehaviorManager>();
      var walker = entity.GetComponent<Walker>();
      if (_behaviorManager != null && walker != null) {
        _root.ToggleDisplayStyle(true);
        Refresh();
      }
    }

    public void ClearFragment() {
      _behaviorManager = null;
      _currentDestEntity = null;
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
      if (destEntity != null) {
        var named = destEntity.GetComponent<NamedEntity>();
        var displayName = named != null ? named.EntityName : destEntity.GameObject.name;
        _destinationLabel.text = " " + _loc.T(WalkingToLocKey, displayName);
        _destinationLabel.style.display = DisplayStyle.Flex;
        _currentDestEntity = destEntity;
      } else {
        _destinationLabel.style.display = DisplayStyle.None;
        _currentDestEntity = null;
      }
    }

    private string GetTaskText(IExecutor executor) {
      if (executor == null || string.IsNullOrEmpty(_behaviorManager.RunningExecutor.Name)) {
        return _loc.T(IdleLocKey);
      }

      var typeName = executor.GetType().Name;

      if (typeName == "ApplyEffectExecutor") {
        _applyEffectAnimNameField ??= executor.GetType().GetField(
            "_animationName", BindingFlags.NonPublic | BindingFlags.Instance);
        var animName = _applyEffectAnimNameField?.GetValue(executor) as string;
        if (animName != null && AnimationLocKeys.TryGetValue(animName, out var animKey)) {
          return _loc.T(TaskPrefixLocKey, _loc.T(animKey));
        }

        // _animationName is null when the building uses a slot-based animation (e.g. medical bed).
        // Fall back to the NeedId of the first effect.
        _applyEffectEffectsField ??= executor.GetType().GetField(
            "_effects", BindingFlags.NonPublic | BindingFlags.Instance);
        if (_applyEffectEffectsField?.GetValue(executor) is IEnumerable effects) {
          foreach (var effect in effects) {
            var needId = effect?.GetType().GetProperty("NeedId")?.GetValue(effect) as string;
            if (needId != null && NeedIdLocKeys.TryGetValue(needId, out var needKey)) {
              return _loc.T(TaskPrefixLocKey, _loc.T(needKey));
            }
            break;
          }
        }

        return _loc.T(TaskPrefixLocKey, animName ?? typeName);
      }

      if (ExecutorLocKeys.TryGetValue(typeName, out var locKey)) {
        return _loc.T(TaskPrefixLocKey, _loc.T(locKey));
      }

      return _loc.T(TaskPrefixLocKey, typeName);
    }

    private static BaseComponent TryGetDestinationEntity(IExecutor executor) {
      if (executor == null) return null;
      switch (executor) {
        case WalkToAccessibleExecutor walkAcc:
          return WalkToAccessibleAccessibleField?.GetValue(walkAcc) as BaseComponent;
        case WalkInsideExecutor walkIn:
          return WalkInsideBuildingAccessibleField?.GetValue(walkIn) as BaseComponent;
        case WalkToReservableExecutor walkRes:
          return WalkToReservableReservableField?.GetValue(walkRes) as BaseComponent;
        default:
          return null;
      }
    }

    private void OnDestinationClicked() {
      if (_currentDestEntity == null) return;
      if (_selectableObjectRetriever.TryGetSelectableObject(
              _currentDestEntity.GameObject, out var selectable)) {
        _entitySelectionService.SelectAndFocusOn(selectable);
      }
    }

  }
}
