using HarmonyLib;
using Timberborn.BehaviorSystem;
using Timberborn.Carrying;
using Timberborn.ConstructionSites;
using Timberborn.InventorySystem;
using Timberborn.NeedSystem;

namespace grantemsley.EmergencyPriority.Patches {

  // Prefix on CarryRootBehavior.Decide. For emergency-employed builders, skip
  // picking up *new* non-emergency hauls when emergency jobs exist. Acts as a
  // backstop for LaborWorkplaceBehavior decorators that might reserve a haul
  // while emergency is active — we want builders idle / emergency-building,
  // not hauling side-quests.
  //
  // Two exceptions that let vanilla run:
  //
  // 1. The beaver is already carrying cargo (`GoodCarrier.IsCarrying`). Let
  //    vanilla finish the delivery to wherever they were headed — aborting
  //    mid-flight produces the "carry materials through to the new emergency"
  //    visual bug and wastes the in-flight goods.
  //
  // 2. The beaver's capacity reservation is destined for an emergency
  //    construction site itself. This is the "fetching materials FOR the
  //    emergency" path (StartConstructionJob → StartNeededMaterialsDelivery
  //    reserves goods → next tick CarryRootBehavior is supposed to walk to
  //    the source). Without this exception we'd block the pickup and the
  //    emergency would never get its materials — bots showed this clearly,
  //    sitting idle next to a missing-materials emergency.
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
      // Reservation is destined for an emergency site → let vanilla run so
      // the beaver picks up the materials and delivers them to the emergency.
      // Without this, bots (and beavers, for that matter) get stuck idle
      // next to a missing-materials emergency.
      if (IsHaulToEmergencySite(__instance, registry)) {
        return true;
      }
      __result = Decision.ReleaseNow();
      return false;
    }

    private static bool IsHaulToEmergencySite(CarryRootBehavior carry,
                                              EmergencyConstructionRegistry registry) {
      var reserver = carry.GetComponent<GoodReserver>();
      if (reserver == null || !reserver.HasReservedCapacity) {
        return false;
      }
      var destInventory = reserver.CapacityReservation.Inventory;
      if (destInventory == null) {
        return false;
      }
      var destJob = destInventory.GetComponent<ConstructionJob>();
      if (destJob == null) {
        return false;
      }
      foreach (var emergencyJob in registry.EmergencyJobs) {
        if (emergencyJob == destJob) {
          return true;
        }
      }
      return false;
    }

  }

}
