using HarmonyLib;
using Timberborn.BehaviorSystem;
using Timberborn.Carrying;
using Timberborn.ConstructionSites;
using Timberborn.InventorySystem;
using Timberborn.NeedSystem;

namespace grantemsley.EmergencyPriority.Patches {

  // Prefix on CarryRootBehavior.Decide. For emergency-employed builders, skip
  // the carry/deliver logic when emergency jobs exist — the beaver keeps the
  // cargo in hand, falls through to WorkerRootBehavior, and works the
  // emergency. When the emergency clears (registry empty), vanilla
  // CarryRootBehavior runs again and they deliver the cargo to its original
  // destination (or fall back to the nearest accepting inventory).
  //
  // Exception: if the current haul is destined for an emergency site itself,
  // we let it finish — that haul is actively helping the emergency. Blocking
  // it would starve the site of materials.
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
