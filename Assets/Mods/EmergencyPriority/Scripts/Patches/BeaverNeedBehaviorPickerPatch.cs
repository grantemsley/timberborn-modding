using System.Reflection;
using HarmonyLib;
using Timberborn.BeaverBehavior;
using Timberborn.NeedBehaviorSystem;
using Timberborn.NeedSystem;
using UnityEngine;

namespace grantemsley.EmergencyPriority.Patches {

  // Prefix on BeaverNeedBehaviorPicker.ShouldPickEssentialAction. The vanilla
  // logic triggers scheduled sleep when dawn is within ~120% of the essential
  // action's duration (ItIsTimeForEssentialAction). For emergency-employed
  // builders we skip that scheduled-sleep trigger entirely; sleep only wins
  // when need points actually bottom out, mirroring
  // EssentialActionIsAtMinimumPoints. CriticalNeederRootBehavior runs above
  // NeederRootBehavior in the tree, so true critical-state needs still preempt.
  [HarmonyPatch(typeof(BeaverNeedBehaviorPicker),
                nameof(BeaverNeedBehaviorPicker.ShouldPickEssentialAction))]
  public static class BeaverNeedBehaviorPickerPatch {

    private const BindingFlags AnyInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly FieldInfo AppraiserField =
        typeof(BeaverNeedBehaviorPicker).GetField("_appraiser", AnyInstance);
    private static readonly FieldInfo NeedManagerField =
        typeof(BeaverNeedBehaviorPicker).GetField("_needManager", AnyInstance);
    private static readonly MethodInfo AppraiseEffectMethod =
        typeof(Appraiser).GetMethod("AppraiseEffect", AnyInstance);
    private static readonly MethodInfo NeedIsAtMinimumPointsMethod =
        typeof(NeedManager).GetMethod("NeedIsAtMinimumPoints", AnyInstance,
                                      null, new[] { typeof(string) }, null);

    public static EmergencyConstructionRegistry Registry { get; set; }

    static BeaverNeedBehaviorPickerPatch() {
      if (AppraiserField == null) {
        Debug.LogError("[EmergencyPriority] Could not find BeaverNeedBehaviorPicker._appraiser; sleep schedule override disabled.");
      }
      if (NeedManagerField == null) {
        Debug.LogError("[EmergencyPriority] Could not find BeaverNeedBehaviorPicker._needManager; sleep schedule override disabled.");
      }
      if (AppraiseEffectMethod == null) {
        Debug.LogError("[EmergencyPriority] Could not find Appraiser.AppraiseEffect; sleep schedule override disabled.");
      }
      if (NeedIsAtMinimumPointsMethod == null) {
        Debug.LogError("[EmergencyPriority] Could not find NeedManager.NeedIsAtMinimumPoints; sleep schedule override disabled.");
      }
    }

    public static bool Prefix(BeaverNeedBehaviorPicker __instance,
                              EssentialAction essentialAction,
                              AppraisedAction nonEssentialAction,
                              ref bool __result) {
      if (!EmergencyBuilderCheck.IsEmergencyBuilder(__instance, Registry)) {
        return true;
      }
      if (AppraiserField == null || NeedManagerField == null
          || AppraiseEffectMethod == null || NeedIsAtMinimumPointsMethod == null) {
        return true;
      }
      var appraiser = AppraiserField.GetValue(__instance);
      var needManager = NeedManagerField.GetValue(__instance);
      if (appraiser == null || needManager == null) {
        return true;
      }
      // _appraiser.AppraiseEffect(in essentialAction) — boxed via object[] for ref-by-in.
      var appraiseArgs = new object[] { essentialAction };
      float essentialActionPoints = (float) AppraiseEffectMethod.Invoke(appraiser, appraiseArgs);
      if (essentialActionPoints > nonEssentialAction.Points) {
        __result = (bool) NeedIsAtMinimumPointsMethod.Invoke(
            needManager, new object[] { essentialAction.Effect.NeedId });
      } else {
        __result = false;
      }
      return false;
    }

  }

}
