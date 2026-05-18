using System.Reflection;
using HarmonyLib;
using Timberborn.BehaviorSystem;
using Timberborn.NeedSystem;
using Timberborn.SleepSystem;
using UnityEngine;

namespace grantemsley.EmergencyPriority.Patches {

  // Prefix on SleepNeedBehavior.ShouldSleepAtHome. For emergency-employed
  // builders, return false so the Decide path takes SleepOutside —
  // GetEssentialAction also routes through this, so the EssentialAction's
  // position becomes the beaver's current position (the worksite) rather
  // than home.
  [HarmonyPatch(typeof(SleepNeedBehavior), nameof(SleepNeedBehavior.ShouldSleepAtHome))]
  public static class SleepNeedBehaviorShouldSleepAtHomePatch {

    public static EmergencyConstructionRegistry Registry { get; set; }

    public static bool Prefix(SleepNeedBehavior __instance, ref bool __result) {
      if (!EmergencyBuilderCheck.IsEmergencyBuilder(__instance, Registry)) {
        return true;
      }
      __result = false;
      return false;
    }

  }

  // Prefix on SleepNeedBehavior.Decide. If the beaver picked sleep earlier
  // (scheduled bedtime), Emergency might be flagged mid-walk-home or mid-sleep.
  // The behavior tree's _returnToBehavior path re-invokes SleepNeedBehavior.
  // Decide once the running executor releases, and vanilla Decide would walk
  // home again or restart Sleep. For emergency-employed builders we instead
  // return ReleaseNow so the tree falls back through to WorkerRootBehavior and
  // picks up the emergency.
  //
  // We still let sleep through if the beaver's sleep need is at the minimum —
  // that's the "they'd actually pass out" case, and Phase 2's sleep-on-spot
  // patches will route them through SleepOutside at the current position. The
  // emergency can wait the few hours it takes to recover.
  [HarmonyPatch(typeof(SleepNeedBehavior), nameof(SleepNeedBehavior.Decide))]
  public static class SleepNeedBehaviorDecidePatch {

    public static EmergencyConstructionRegistry Registry { get; set; }

    public static bool Prefix(SleepNeedBehavior __instance, ref Decision __result) {
      if (!EmergencyBuilderCheck.IsEmergencyBuilder(__instance, Registry)) {
        return true;
      }
      var needManager = __instance.GetComponent<NeedManager>();
      if (needManager != null && needManager.NeedIsAtMinimumPoints(Sleeper.SleepNeedId)) {
        return true;
      }
      __result = Decision.ReleaseNow();
      return false;
    }

  }

  // Prefix on SleepNeedBehavior.SleepOutside. Vanilla flow:
  //   tick 1: _walkedToSleepingPosition == false → WalkToRandomSleepingPosition
  //   tick 2: _walkedToSleepingPosition == true  → Sleep at that random spot
  // `RandomDestinationPicker.GetCoordinates` anchors the random destination
  // on the beaver's HOME when one exists, so a builder who was on-site gets
  // dragged back near home before sleeping. For emergency builders we
  // pre-set the flag so the original method skips the walk and falls
  // straight through to Sleep() at the current position (the worksite).
  // Sleep() resets the flag to false when it launches the sleep executor,
  // so normal beavers and post-emergency sleeps are unaffected.
  [HarmonyPatch(typeof(SleepNeedBehavior), nameof(SleepNeedBehavior.SleepOutside))]
  public static class SleepNeedBehaviorSleepOutsidePatch {

    private const BindingFlags AnyInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly FieldInfo WalkedField =
        typeof(SleepNeedBehavior).GetField("_walkedToSleepingPosition", AnyInstance);

    public static EmergencyConstructionRegistry Registry { get; set; }

    static SleepNeedBehaviorSleepOutsidePatch() {
      if (WalkedField == null) {
        Debug.LogError("[EmergencyPriority] Could not find SleepNeedBehavior._walkedToSleepingPosition; sleep-on-spot override disabled.");
      }
    }

    public static void Prefix(SleepNeedBehavior __instance) {
      if (WalkedField == null) {
        return;
      }
      if (!EmergencyBuilderCheck.IsEmergencyBuilder(__instance, Registry)) {
        return;
      }
      WalkedField.SetValue(__instance, true);
    }

  }

}
