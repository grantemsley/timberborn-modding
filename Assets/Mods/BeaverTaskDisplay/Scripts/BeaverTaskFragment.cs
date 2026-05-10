using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Timberborn.BaseComponentSystem;
using Timberborn.BehaviorSystem;
using Timberborn.CoreUI;
using Timberborn.EntityNaming;
using Timberborn.EntityPanelSystem;
using Timberborn.Localization;
using Timberborn.SelectionSystem;
using Timberborn.WalkingSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace Grant.BeaverTaskDisplay {

  // Adds a small panel below the carrying section showing the beaver's current
  // task and (when applicable) a clickable destination. Click the destination
  // to focus the camera on it.
  internal class BeaverTaskFragment : IEntityPanelFragment {

    private const string SubPanelClass = "entity-sub-panel";
    private const string SubBoxClass = "bg-sub-box--green";

    private const string IdleLocKey = "Grant.BeaverTaskDisplay.Idle";
    private const string TaskPrefixLocKey = "Grant.BeaverTaskDisplay.TaskPrefix";
    private const string WalkingToLocKey = "Grant.BeaverTaskDisplay.WalkingTo";
    private const string ExecutorLocKeyPrefix = "Grant.BeaverTaskDisplay.Executor.";

    // Map of executor class name -> loc key suffix. Used to translate raw
    // class names (which is all ExecutorInfo.Name carries) into human-readable
    // task descriptions. Unmapped executors fall back to a humanized split of
    // the class name (e.g. "MyCustomExecutor" -> "My Custom").
    private static readonly Dictionary<string, string> ExecutorLocSuffixes = new() {
      { "WaitExecutor",             "Waiting" },
      { "BuildExecutor",            "Building" },
      { "DemolishExecutor",         "Demolishing" },
      { "ApplyEffectExecutor",      "SatisfyingNeed" },
      { "PlantExecutor",            "Planting" },
      { "WalkToReservableExecutor", "WalkingTo" },
      { "WorkAtReservableExecutor", "Working" },
      { "WalkInsideExecutor",       "Entering" },
      { "WalkToAccessibleExecutor", "WalkingTo" },
      { "WalkToPositionExecutor",   "Walking" },
      { "ProduceExecutor",          "Producing" },
      { "WorkExecutor",             "Working" },
      { "RemoveYieldExecutor",      "Harvesting" },
    };

    // Cached reflection handles. The public API surfaces ExecutorInfo (a struct
    // with just a name + elapsed time); to inspect the actual IExecutor for
    // walking-target details, we read the private _runningExecutor field.
    private static readonly FieldInfo BehaviorManagerRunningExecutorField =
        typeof(BehaviorManager).GetField(
            "_runningExecutor", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo WalkToAccessibleAccessibleField =
        typeof(WalkToAccessibleExecutor).GetField(
            "_accessible", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo WalkInsideBuildingAccessibleField =
        typeof(WalkInsideExecutor).GetField(
            "_buildingAccessible", BindingFlags.NonPublic | BindingFlags.Instance);

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
      _root.style.alignItems = Align.FlexStart;
      _root.style.paddingLeft = 8;
      _root.style.paddingRight = 8;
      _root.style.paddingTop = 4;
      _root.style.paddingBottom = 4;
      _root.ToggleDisplayStyle(false);

      _taskLabel = new Label { text = string.Empty };
      _taskLabel.style.color = Color.white;
      _root.Add(_taskLabel);

      _destinationLabel = new Label { text = string.Empty };
      // A pale, slightly blue tone to suggest "clickable" without going full Unity-link blue.
      _destinationLabel.style.color = new Color(0.70f, 0.85f, 1f, 1f);
      _destinationLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
      _destinationLabel.RegisterCallback<ClickEvent>(_ => OnDestinationClicked());
      _destinationLabel.style.display = DisplayStyle.None;
      _root.Add(_destinationLabel);

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
      // Task description — RunningExecutor returns an ExecutorInfo struct
      // (Name + ElapsedTime). Use RunningExecutor.Name to determine if an executor is running.
      var executorClassName = _behaviorManager.RunningExecutor.Name;
      if (!string.IsNullOrEmpty(executorClassName)) {
        var friendly = GetFriendlyExecutorName(executorClassName);
        _taskLabel.text = _loc.T(TaskPrefixLocKey, friendly);
      } else {
        _taskLabel.text = _loc.T(IdleLocKey);
      }

      // Destination — we need the real IExecutor instance to inspect walking
      // targets, which is only available via the private field.
      var actualExecutor = BehaviorManagerRunningExecutorField?.GetValue(_behaviorManager) as IExecutor;
      var destEntity = TryGetDestinationEntity(actualExecutor);
      if (destEntity != null) {
        var named = destEntity.GetComponent<NamedEntity>();
        var displayName = named != null
            ? named.EntityName
            : destEntity.GameObject.name;
        _destinationLabel.text = _loc.T(WalkingToLocKey, displayName);
        _destinationLabel.style.display = DisplayStyle.Flex;
        _currentDestEntity = destEntity;
      } else {
        _destinationLabel.style.display = DisplayStyle.None;
        _currentDestEntity = null;
      }
    }

    private string GetFriendlyExecutorName(string executorClassName) {
      if (ExecutorLocSuffixes.TryGetValue(executorClassName, out var suffix)) {
        return _loc.T(ExecutorLocKeyPrefix + suffix);
      }
      return Humanize(executorClassName);
    }

    // "WalkToReservableExecutor" -> "Walk To Reservable"
    private static string Humanize(string className) {
      if (className.EndsWith("Executor")) {
        className = className.Substring(0, className.Length - "Executor".Length);
      }
      var sb = new StringBuilder(className.Length + 4);
      for (var i = 0; i < className.Length; i++) {
        if (i > 0 && char.IsUpper(className[i]) && !char.IsUpper(className[i - 1])) {
          sb.Append(' ');
        }
        sb.Append(className[i]);
      }
      return sb.ToString();
    }

    private static BaseComponent TryGetDestinationEntity(IExecutor executor) {
      if (executor == null) {
        return null;
      }
      switch (executor) {
        case WalkToAccessibleExecutor walkAcc:
          return WalkToAccessibleAccessibleField?.GetValue(walkAcc) as BaseComponent;
        case WalkInsideExecutor walkIn:
          return WalkInsideBuildingAccessibleField?.GetValue(walkIn) as BaseComponent;
        default:
          return null;
      }
    }

    private void OnDestinationClicked() {
      if (_currentDestEntity == null) {
        return;
      }
      // TryGetSelectableObject takes a GameObject, not a BaseComponent.
      if (_selectableObjectRetriever.TryGetSelectableObject(
              _currentDestEntity.GameObject, out var selectable)) {
        _entitySelectionService.SelectAndFocusOn(selectable);
      }
    }

  }
}
