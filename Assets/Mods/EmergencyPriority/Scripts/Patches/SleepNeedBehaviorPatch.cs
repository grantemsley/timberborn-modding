using HarmonyLib;
using Timberborn.SleepSystem;

namespace grantemsley.EmergencyPriority.Patches {

  // Prefix on SleepNeedBehavior.ShouldSleepAtHome. For emergency-employed
  // builders, return false so the Decide path takes SleepOutside —
  // WalkToRandomSleepingPosition then picks a destination near the beaver's
  // current position (the worksite) rather than walking back home.
  // GetEssentialAction also calls ShouldSleepAtHome, so the EssentialAction's
  // position becomes the beaver's current position too, which is what we want
  // for duration/points calculations.
  [HarmonyPatch(typeof(SleepNeedBehavior), nameof(SleepNeedBehavior.ShouldSleepAtHome))]
  public static class SleepNeedBehaviorPatch {

    public static EmergencyConstructionRegistry Registry { get; set; }

    public static bool Prefix(SleepNeedBehavior __instance, ref bool __result) {
      if (!EmergencyBuilderCheck.IsEmergencyBuilder(__instance, Registry)) {
        return true;
      }
      __result = false;
      return false;
    }

  }

}
