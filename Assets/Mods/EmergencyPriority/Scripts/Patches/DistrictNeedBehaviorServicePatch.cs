using System.Collections;
using System.Reflection;
using HarmonyLib;
using Timberborn.NeedBehaviorSystem;
using Timberborn.NeedSystem;
using UnityEngine;

namespace grantemsley.EmergencyPriority.Patches {

  // Prefix on DistrictNeedBehaviorService.PickShortestAction. Vanilla iterates
  // appraised need-behavior groups in descending-points order; for each group
  // it picks the shortest-duration behavior, returning the first group with
  // any reachable option. A starving emergency builder may therefore walk
  // past closer food to reach their favorite, costing minutes of work time.
  //
  // For emergency-employed builders in actual critical-state (onlyNeedsInCritical
  // State = true — the CriticalNeederRootBehavior pathway), we instead pick the
  // globally shortest-duration action across all groups. Non-emergency beavers
  // and the non-critical path are unchanged.
  [HarmonyPatch(typeof(DistrictNeedBehaviorService),
                nameof(DistrictNeedBehaviorService.PickShortestAction))]
  public static class DistrictNeedBehaviorServicePatch {

    private const BindingFlags AnyInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly FieldInfo AppraisedField =
        typeof(DistrictNeedBehaviorService).GetField("_appraisedNeedBehaviors", AnyInstance);
    private static readonly PropertyInfo GroupProperty;
    private static readonly PropertyInfo PointsProperty;

    public static EmergencyConstructionRegistry Registry { get; set; }

    static DistrictNeedBehaviorServicePatch() {
      if (AppraisedField == null) {
        Debug.LogError("[EmergencyPriority] Could not find DistrictNeedBehaviorService._appraisedNeedBehaviors; closest-need override disabled.");
        return;
      }
      // _appraisedNeedBehaviors is SortedSet<AppraisedNeedBehaviorGroup>. The
      // element struct is a nested type, so resolve its members reflectively.
      var elementType = typeof(DistrictNeedBehaviorService).GetNestedType(
          "AppraisedNeedBehaviorGroup", BindingFlags.Public | BindingFlags.NonPublic);
      if (elementType == null) {
        Debug.LogError("[EmergencyPriority] Could not find DistrictNeedBehaviorService.AppraisedNeedBehaviorGroup; closest-need override disabled.");
        return;
      }
      GroupProperty = elementType.GetProperty("NeedBehaviorGroup", AnyInstance);
      PointsProperty = elementType.GetProperty("Points", AnyInstance);
      if (GroupProperty == null || PointsProperty == null) {
        Debug.LogError("[EmergencyPriority] Could not find AppraisedNeedBehaviorGroup members; closest-need override disabled.");
      }
    }

    public static bool Prefix(DistrictNeedBehaviorService __instance,
                              NeedManager needManager,
                              Vector3 essentialActionPosition,
                              float hoursLeftForNonEssentialActions,
                              bool onlyNeedsInCriticalState,
                              ref AppraisedAction? __result) {
      if (!onlyNeedsInCriticalState) {
        return true;
      }
      if (AppraisedField == null || GroupProperty == null || PointsProperty == null) {
        return true;
      }
      if (!EmergencyBuilderCheck.IsEmergencyBuilder(needManager, Registry)) {
        return true;
      }
      var appraised = AppraisedField.GetValue(__instance) as IEnumerable;
      if (appraised == null) {
        return true;
      }
      var calculator = needManager.GetComponent<ActionDurationCalculator>();
      if (calculator == null) {
        return true;
      }

      AppraisedAction? best = null;
      float bestDuration = float.MaxValue;

      foreach (var entry in appraised) {
        var group = (DistrictNeedBehaviorService.NeedBehaviorGroup) GroupProperty.GetValue(entry);
        if (group == null) {
          continue;
        }
        float points = (float) PointsProperty.GetValue(entry);
        var behaviors = group.NeedBehaviors;
        for (int i = 0; i < behaviors.Count; i++) {
          var behavior = behaviors[i];
          Vector3? actionPosition = behavior.ActionPosition(needManager);
          if (!actionPosition.HasValue) {
            continue;
          }
          float duration = calculator.DurationWithReturnInHours(
              actionPosition.Value, essentialActionPosition);
          if (duration < bestDuration) {
            bestDuration = duration;
            best = new AppraisedAction(behavior, group.Needs, points);
          }
        }
      }

      __result = best;
      return false;
    }

  }

}
