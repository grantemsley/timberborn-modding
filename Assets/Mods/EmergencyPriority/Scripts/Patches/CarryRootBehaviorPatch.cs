using HarmonyLib;
using Timberborn.BehaviorSystem;
using Timberborn.Carrying;
using Timberborn.ConstructionSites;
using Timberborn.InventorySystem;
using Timberborn.NeedSystem;
using Timberborn.WorkSystem;
using UnityEngine;

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

    // Toggle to true to dump per-call diagnostic logs to the Unity console.
    // Keep false in normal play — verbose during emergency activity.
    private const bool VerboseLogging = true;

    public static bool Prefix(CarryRootBehavior __instance, ref Decision __result) {
      var registry = Registry;
      if (registry == null || !registry.HasAny) {
        return true;
      }
      var workerType = __instance.GetComponent<Worker>()?.WorkerType ?? "?";
      var name = __instance.Name;
      if (!EmergencyBuilderCheck.IsEmergencyBuilder(__instance, registry)) {
        if (VerboseLogging) Debug.Log($"[EmergencyPriority] CarryPatch {name} ({workerType}): not emergency builder → vanilla");
        return true;
      }
      var needManager = __instance.GetComponent<NeedManager>();
      if (needManager != null && needManager.AnyNeedIsInCriticalState()) {
        if (VerboseLogging) Debug.Log($"[EmergencyPriority] CarryPatch {name} ({workerType}): critical need → vanilla");
        return true;
      }
      var goodCarrier = __instance.GetComponent<GoodCarrier>();
      if (goodCarrier != null && goodCarrier.IsCarrying) {
        if (VerboseLogging) Debug.Log($"[EmergencyPriority] CarryPatch {name} ({workerType}): IsCarrying → vanilla");
        return true;
      }
      if (IsHaulToEmergencySite(__instance, registry)) {
        if (VerboseLogging) Debug.Log($"[EmergencyPriority] CarryPatch {name} ({workerType}): dest is emergency → vanilla");
        return true;
      }
      var reserver = __instance.GetComponent<GoodReserver>();
      var hasReserved = reserver != null && reserver.HasReservedCapacity;
      if (VerboseLogging) Debug.Log($"[EmergencyPriority] CarryPatch {name} ({workerType}): blocking, hasReservation={hasReserved}");
      __result = Decision.ReleaseNow();
      return false;
    }

    private static bool IsHaulToEmergencySite(CarryRootBehavior carry,
                                              EmergencyConstructionRegistry registry) {
      var reserver = carry.GetComponent<GoodReserver>();
      if (reserver == null) {
        if (VerboseLogging) Debug.Log($"[EmergencyPriority] IsHaulToEmergencySite {carry.Name}: no GoodReserver");
        return false;
      }
      if (!reserver.HasReservedCapacity) {
        if (VerboseLogging) Debug.Log($"[EmergencyPriority] IsHaulToEmergencySite {carry.Name}: no capacity reservation");
        return false;
      }
      var destInventory = reserver.CapacityReservation.Inventory;
      if (destInventory == null) {
        if (VerboseLogging) Debug.Log($"[EmergencyPriority] IsHaulToEmergencySite {carry.Name}: reservation has null Inventory");
        return false;
      }
      var destJob = destInventory.GetComponent<ConstructionJob>();
      if (destJob == null) {
        if (VerboseLogging) Debug.Log($"[EmergencyPriority] IsHaulToEmergencySite {carry.Name}: dest {destInventory.Name} has no ConstructionJob (not a construction site)");
        return false;
      }
      foreach (var emergencyJob in registry.EmergencyJobs) {
        if (emergencyJob == destJob) {
          return true;
        }
      }
      if (VerboseLogging) Debug.Log($"[EmergencyPriority] IsHaulToEmergencySite {carry.Name}: dest {destInventory.Name} has ConstructionJob but it's NOT in emergency registry");
      return false;
    }

  }

}
