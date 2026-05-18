using HarmonyLib;
using Timberborn.BehaviorSystem;
using Timberborn.Carrying;
using Timberborn.NeedSystem;

namespace grantemsley.EmergencyPriority.Patches {

  // Prefix on CarryRootBehavior.Decide. For emergency-employed builders, skip
  // picking up *new* hauls when emergency jobs exist. Acts as a backstop for
  // LaborWorkplaceBehavior decorators that might reserve a haul while the
  // emergency is active — we want those builders to idle / emergency-build,
  // not haul side-quests.
  //
  // Exception: if the beaver is already carrying cargo (`GoodCarrier.
  // IsCarrying`), let vanilla finish the delivery to wherever they were
  // headed. Reason: aborting an in-flight delivery looks weird — the user
  // saw beavers carry materials all the way to a (formerly-emergency) site,
  // refuse to drop them, then carry them onward to the new emergency. Letting
  // vanilla complete the existing delivery means the cargo lands in a useful
  // inventory (the original construction site, or a fallback), the beaver
  // empties hands cleanly, and re-decide picks up the new emergency.
  //
  // CriticalNeederRootBehavior runs above us in the tree, so critically-needy
  // beavers won't even reach this prefix. We still guard against critical state
  // explicitly to handle edge cases.
  [HarmonyPatch(typeof(CarryRootBehavior), nameof(CarryRootBehavior.Decide))]
  public static class CarryRootBehaviorPatch {

    public static EmergencyConstructionRegistry Registry { get; set; }

    public static bool Prefix(CarryRootBehavior __instance, ref Decision __result) {
      var registry = Registry;
      if (registry == null || !registry.HasAny) {
        return true;
      }
      if (!EmergencyBuilderCheck.IsEmergencyBuilder(__instance, registry)) {
        return true;
      }
      var needManager = __instance.GetComponent<NeedManager>();
      if (needManager != null && needManager.AnyNeedIsInCriticalState()) {
        return true;
      }
      // Already mid-haul → let vanilla finish the delivery. Avoids the
      // "carry through to the new emergency site" visual bug.
      var goodCarrier = __instance.GetComponent<GoodCarrier>();
      if (goodCarrier != null && goodCarrier.IsCarrying) {
        return true;
      }
      __result = Decision.ReleaseNow();
      return false;
    }

  }

}
