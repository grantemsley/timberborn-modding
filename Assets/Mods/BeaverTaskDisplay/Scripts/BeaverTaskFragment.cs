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
      // InventoryNeedBehavior handled inline — need to distinguish eat vs drink via reflection
      { "SleepNeedBehavior",              "grantemsley.BeaverTaskDisplay.Walk.SleepAt" },
      { "AttractionNeedBehavior",         "grantemsley.BeaverTaskDisplay.Walk.VisitAt" },
      { "ProduceWorkplaceBehavior",       "grantemsley.BeaverTaskDisplay.Walk.WorkAt" },
      // CarryRootBehavior and HaulWorkplaceBehavior handled inline — need GoodCarrier.IsCarrying check
    };

    // Complete text for WalkToPositionExecutor when behavior provides context.
    // No building name is appended since position walks have no entity destination.
    private static readonly Dictionary<string, string> PositionWalkBehaviorKeys = new() {
      { "PlantBehavior",            "grantemsley.BeaverTaskDisplay.Walk.Plant" },
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

    // Cached lazily to read the need being targeted by InventoryNeedBehavior.
    private static FieldInfo _behaviorManagerRunningBehaviorField;
    private static FieldInfo _inventoryNeedBehaviorNeedIdField;

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
    private Walker _walker;
    private BaseComponent _currentDestEntity;
    private string _cachedTaskText;
    private string _lastExecutorName;
    private string _lastBehaviorName;
    private bool _lastIsCarrying;

    private Mesh _ribbonMesh;
    private MeshRenderer _pathMeshRenderer;
    private Mesh _arrowMesh;
    private MeshRenderer _arrowMeshRenderer;

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
      _destinationLabel.RegisterCallback<MouseOverEvent>(_ => _destinationLabel.style.color = new Color(0.90f, 0.97f, 1f, 1f));
      _destinationLabel.RegisterCallback<MouseOutEvent>(_ => _destinationLabel.style.color = new Color(0.70f, 0.85f, 1f, 1f));
      _destinationLabel.style.display = DisplayStyle.None;
      row.Add(_destinationLabel);

      _root.Add(row);

      var pathGO = new GameObject("[BTD_PathLine]");
      _ribbonMesh = new Mesh { name = "BTD_RibbonMesh" };
      pathGO.AddComponent<MeshFilter>().sharedMesh = _ribbonMesh;
      _pathMeshRenderer = pathGO.AddComponent<MeshRenderer>();
      _pathMeshRenderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
      _pathMeshRenderer.enabled = false;

      var arrowGO = new GameObject("[BTD_PathArrows]");
      _arrowMesh = new Mesh { name = "BTD_ArrowMesh" };
      arrowGO.AddComponent<MeshFilter>().sharedMesh = _arrowMesh;
      _arrowMeshRenderer = arrowGO.AddComponent<MeshRenderer>();
      _arrowMeshRenderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
      _arrowMeshRenderer.enabled = false;

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
        _walker = walker;
        _walker.StartedNewPath += OnStartedNewPath;
        CapturePathSnapshot();
        _root.ToggleDisplayStyle(true);
        Refresh();
      }
    }

    public void ClearFragment() {
      _behaviorManager = null;
      if (_walker != null) {
        _walker.StartedNewPath -= OnStartedNewPath;
        _walker = null;
      }
      _cachedPathPositions.Clear();
      _lastExecutorName = null;
      _lastBehaviorName = null;
      _lastIsCarrying = false;
      _pathMeshRenderer.enabled = false;
      _arrowMeshRenderer.enabled = false;
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
      var execName = _behaviorManager.RunningExecutor.Name;
      var behaviorName = _behaviorManager.RunningBehavior.Name;
      var carrier = _behaviorManager.GetComponent<GoodCarrier>();
      var isCarrying = carrier != null && carrier.IsCarrying;
      if (execName != _lastExecutorName || behaviorName != _lastBehaviorName || isCarrying != _lastIsCarrying) {
        _lastExecutorName = execName;
        _lastBehaviorName = behaviorName;
        _lastIsCarrying = isCarrying;
        _cachedTaskText = GetTaskText(actualExecutor);
      }
      _taskLabel.text = _cachedTaskText;

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

      RefreshPathLine();
    }

    // Snapshot of the full path captured when a new path starts.
    private readonly List<Vector3> _cachedPathPositions = new();

    // Reusable buffers to avoid per-frame allocation.
    private readonly List<Vector3> _pathPositions = new();

    private readonly List<Vector3> _ribbonVerts = new();
    private readonly List<int>     _ribbonTris  = new();
    private readonly List<Color>   _ribbonColors = new();

    private readonly List<Vector3> _arrowVerts = new();
    private readonly List<int> _arrowTris = new();
    private readonly List<Color> _arrowColors = new();
    private static readonly Color ArrowColor = new(1f, 0.65f, 0f, 0.9f);

    private void OnStartedNewPath(object sender, StartedNewPathEventArgs e) {
      CapturePathSnapshot();
    }

    private void CapturePathSnapshot() {
      _cachedPathPositions.Clear();
      if (_walker == null) return;
      foreach (var corner in _walker.PathCorners)
        _cachedPathPositions.Add(corner.Position);
    }

    private void RefreshPathLine() {
      if (_walker == null || Time.timeScale > 0f) {
        _pathMeshRenderer.enabled = false;
        _arrowMeshRenderer.enabled = false;
        return;
      }

      if (_cachedPathPositions.Count < 2) {
        _pathMeshRenderer.enabled = false;
        _arrowMeshRenderer.enabled = false;
        return;
      }

      _pathPositions.Clear();
      _pathPositions.AddRange(_cachedPathPositions);

      if (_pathPositions.Count < 2) {
        _pathMeshRenderer.enabled = false;
        _arrowMeshRenderer.enabled = false;
        return;
      }

      BuildRibbonMesh(_pathPositions);
      _pathMeshRenderer.enabled = true;

      BuildArrowMesh(_pathPositions);
      _arrowMeshRenderer.enabled = true;
    }

    // Unit-length right vector in the XZ plane, perpendicular to the horizontal
    // component of `fwd`. Falls back to world +X when fwd points straight up/down.
    private static Vector3 HorizontalRight(Vector3 fwd) {
      var r = new Vector3(-fwd.z, 0f, fwd.x);
      var sqMag = r.sqrMagnitude;
      return sqMag < 0.0001f ? Vector3.right : r / Mathf.Sqrt(sqMag);
    }

    private void BuildRibbonMesh(List<Vector3> positions) {
      // Square-tube extrusion. Each cross-section is a small square perpendicular
      // to the segment direction, so the tube's projected width is roughly
      // consistent (~1.0–1.4×) from any camera angle. Replaces an earlier flat
      // ribbon, which appeared thin on horizontal segments and wide on steep
      // (zipline/stair) segments due to camera-angle foreshortening.
      const float halfWidth = 0.06f;
      var color = new Color(1f, 0.65f, 0f, 0.9f);

      _ribbonVerts.Clear();
      _ribbonTris.Clear();
      _ribbonColors.Clear();

      // 1. Build one orthonormal frame per waypoint and emit 4 verts per frame.
      for (var i = 0; i < positions.Count; i++) {
        Vector3 fwd;
        if (i == 0) {
          fwd = (positions[1] - positions[0]).normalized;
        } else if (i == positions.Count - 1) {
          fwd = (positions[i] - positions[i - 1]).normalized;
        } else {
          var f1 = (positions[i]     - positions[i - 1]).normalized;
          var f2 = (positions[i + 1] - positions[i]).normalized;
          var sum = f1 + f2;
          // Averaging the in/out directions smooths the frame across corners,
          // reducing visible twist. Fall back to f1 on a 180° fold-back.
          fwd = sum.sqrMagnitude < 0.0001f ? f1 : sum.normalized;
        }

        var right = HorizontalRight(fwd);
        var up    = Vector3.Cross(fwd, right).normalized;

        var p = positions[i];
        var rOff = right * halfWidth;
        var uOff = up    * halfWidth;
        _ribbonVerts.Add(p + rOff + uOff); // 0: TR
        _ribbonVerts.Add(p - rOff + uOff); // 1: TL
        _ribbonVerts.Add(p - rOff - uOff); // 2: BL
        _ribbonVerts.Add(p + rOff - uOff); // 3: BR
        _ribbonColors.Add(color); _ribbonColors.Add(color);
        _ribbonColors.Add(color); _ribbonColors.Add(color);
      }

      // 2. Connect consecutive cross-sections with 8 triangles per segment
      //    (one quad per tube face). Winding is CCW from each face's outward
      //    normal — purely for RecalculateNormals; Sprites/Default doesn't cull.
      for (var i = 0; i < positions.Count - 1; i++) {
        int a = i * 4;
        int b = (i + 1) * 4;
        // Top face (+up): a0,b0,b1,a1
        _ribbonTris.Add(a + 0); _ribbonTris.Add(b + 0); _ribbonTris.Add(b + 1);
        _ribbonTris.Add(a + 0); _ribbonTris.Add(b + 1); _ribbonTris.Add(a + 1);
        // Left face (-right): a1,b1,b2,a2
        _ribbonTris.Add(a + 1); _ribbonTris.Add(b + 1); _ribbonTris.Add(b + 2);
        _ribbonTris.Add(a + 1); _ribbonTris.Add(b + 2); _ribbonTris.Add(a + 2);
        // Bottom face (-up): a2,b2,b3,a3
        _ribbonTris.Add(a + 2); _ribbonTris.Add(b + 2); _ribbonTris.Add(b + 3);
        _ribbonTris.Add(a + 2); _ribbonTris.Add(b + 3); _ribbonTris.Add(a + 3);
        // Right face (+right): a3,b3,b0,a0
        _ribbonTris.Add(a + 3); _ribbonTris.Add(b + 3); _ribbonTris.Add(b + 0);
        _ribbonTris.Add(a + 3); _ribbonTris.Add(b + 0); _ribbonTris.Add(a + 0);
      }

      _ribbonMesh.Clear();
      _ribbonMesh.SetVertices(_ribbonVerts);
      _ribbonMesh.SetColors(_ribbonColors);
      _ribbonMesh.SetTriangles(_ribbonTris, 0);
      _ribbonMesh.RecalculateNormals();
      _ribbonMesh.RecalculateBounds();
    }

    private void BuildArrowMesh(List<Vector3> positions) {
      const float spacing  = 2.5f;  // world units between arrows
      const float halfLen  = 0.20f; // half-length of arrow triangle
      const float halfWide = 0.23f; // half-width — with halfLen gives roughly equilateral proportions
      const float yOffset  = 0.03f; // small lift to avoid z-fighting with path surface

      _arrowVerts.Clear();
      _arrowColors.Clear();
      _arrowTris.Clear();
      float accumulated = spacing * 0.5f; // start halfway in so first arrow isn't right at origin

      for (var i = 0; i < positions.Count - 1; i++) {
        var a = positions[i];
        var b = positions[i + 1];
        var seg = b - a;
        var segLen = seg.magnitude;
        if (segLen < 0.001f) continue;

        var fwd  = seg / segLen;
        // Right vector in the XZ plane regardless of slope
        var right = new Vector3(-fwd.z, 0f, fwd.x).normalized;

        while (accumulated <= segLen) {
          var center = a + fwd * accumulated;
          center.y += yOffset;

          var tip      = center + fwd   * halfLen;
          var baseLeft = center - fwd   * halfLen + right * halfWide;
          var baseRight= center - fwd   * halfLen - right * halfWide;

          var idx = _arrowVerts.Count;
          _arrowVerts.Add(tip);
          _arrowVerts.Add(baseLeft);
          _arrowVerts.Add(baseRight);
          _arrowColors.Add(ArrowColor);
          _arrowColors.Add(ArrowColor);
          _arrowColors.Add(ArrowColor);
          _arrowTris.Add(idx); _arrowTris.Add(idx + 1); _arrowTris.Add(idx + 2);

          accumulated += spacing;
        }
        accumulated -= segLen;
      }

      _arrowMesh.Clear();
      _arrowMesh.SetVertices(_arrowVerts);
      _arrowMesh.SetColors(_arrowColors);
      _arrowMesh.SetTriangles(_arrowTris, 0);
      _arrowMesh.RecalculateNormals();
      _arrowMesh.RecalculateBounds();
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
          } else if (behaviorName == "InventoryNeedBehavior") {
            prefixKey = GetInventoryNeedPrefixKey();
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

    private string GetInventoryNeedPrefixKey() {
      _behaviorManagerRunningBehaviorField ??= typeof(BehaviorManager).GetField(
          "_runningBehavior", BindingFlags.NonPublic | BindingFlags.Instance);
      var behavior = _behaviorManagerRunningBehaviorField?.GetValue(_behaviorManager);
      if (behavior != null) {
        _inventoryNeedBehaviorNeedIdField ??= behavior.GetType().GetField(
            "_needId", BindingFlags.NonPublic | BindingFlags.Instance);
        var needId = _inventoryNeedBehaviorNeedIdField?.GetValue(behavior) as string;
        if (needId == "Thirst") return "grantemsley.BeaverTaskDisplay.Walk.DrinkAt";
      }
      return "grantemsley.BeaverTaskDisplay.Walk.EatAt";
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
