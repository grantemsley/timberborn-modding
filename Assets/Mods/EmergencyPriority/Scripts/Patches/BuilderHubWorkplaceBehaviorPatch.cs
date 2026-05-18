using System.Reflection;
using HarmonyLib;
using Timberborn.BehaviorSystem;
using Timberborn.BuilderHubSystem;
using Timberborn.ConstructionSites;
using Timberborn.GameDistricts;
using Timberborn.Navigation;
using UnityEngine;

namespace grantemsley.EmergencyPriority.Patches {

  // Prefix on BuilderHubWorkplaceBehavior.Decide. When the registry holds any
  // emergency jobs reachable from this hub, we try those first via the same
  // StartConstructionJob path the game uses. If one accepts, we short-circuit
  // and skip the regular Priorities.Descending loop.
  //
  // If at least one emergency site is in this hub's district but none can
  // currently accept this beaver (build slot full, all materials already in
  // flight, etc.), we return Decision.ReleaseNow instead of falling through.
  // That keeps the beaver from picking up a normal-priority construction
  // while the emergency is unfinished — they release out of this workplace
  // behavior and back through the behavior tree, going idle / eating / sleeping
  // until the emergency site can accept them again.
  //
  // If no emergency site is in this hub's district at all, we fall through to
  // vanilla so beavers at unrelated hubs aren't artificially blocked from
  // their normal work.
  [HarmonyPatch(typeof(BuilderHubWorkplaceBehavior), nameof(BuilderHubWorkplaceBehavior.Decide))]
  public static class BuilderHubWorkplaceBehaviorPatch {

    private const BindingFlags AnyInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly FieldInfo AccessibleField =
        typeof(BuilderHubWorkplaceBehavior).GetField("_accessible", AnyInstance);

    public static EmergencyConstructionRegistry Registry { get; set; }

    static BuilderHubWorkplaceBehaviorPatch() {
      if (AccessibleField == null) {
        Debug.LogError("[EmergencyPriority] Could not find BuilderHubWorkplaceBehavior._accessible; emergency job override disabled.");
      }
    }

    public static bool Prefix(BuilderHubWorkplaceBehavior __instance,
                              BehaviorAgent agent,
                              ref Decision __result) {
      var registry = Registry;
      if (registry == null || !registry.HasAny) {
        return true;
      }
      var accessible = AccessibleField?.GetValue(__instance) as Accessible;
      if (accessible == null) {
        return true;
      }
      var hubDistrict = accessible.GetComponent<DistrictBuilding>()?.GetDistrictOrConstructionDistrict();
      bool sameDistrictEmergencyExists = false;
      foreach (var job in registry.EmergencyJobs) {
        if (!job) {
          continue;
        }
        // Construction sites have ConstructionDistrict (not District) set while
        // unfinished. GetDistrictOrConstructionDistrict prefers District but
        // falls back to ConstructionDistrict — correct for both a finished hub
        // and an unfinished construction site.
        var jobDistrict = job.GetComponent<DistrictBuilding>()?.GetDistrictOrConstructionDistrict();
        if (jobDistrict != hubDistrict) {
          continue;
        }
        sameDistrictEmergencyExists = true;
        var (behavior, decision) = job.StartConstructionJob(agent, accessible);
        if (!decision.ShouldReleaseNow) {
          __result = Decision.TransferNow(behavior, in decision);
          return false;
        }
      }
      if (sameDistrictEmergencyExists) {
        __result = Decision.ReleaseNow();
        return false;
      }
      return true;
    }

  }

}
