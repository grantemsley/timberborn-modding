using System.Reflection;
using HarmonyLib;
using Timberborn.BehaviorSystem;
using Timberborn.BuildingsNavigation;
using Timberborn.BuilderHubSystem;
using Timberborn.ConstructionSites;
using Timberborn.Navigation;
using UnityEngine;

namespace grantemsley.EmergencyPriority.Patches {

  // Prefix on BuilderHubWorkplaceBehavior.Decide. When the registry has any
  // emergency jobs, we try them first via the same StartConstructionJob path
  // the game uses. If one accepts, short-circuit and skip the regular
  // Priorities.Descending loop. If all emergency jobs reachable from this hub
  // release-now (build slot full, no materials, etc.), return
  // Decision.ReleaseNow — that keeps builders from picking up normal-priority
  // construction while emergencies are unfinished. If NO emergency is
  // reachable from this hub at all (the only emergencies are in different
  // districts), fall through to vanilla so this hub's builders keep doing
  // their normal work.
  //
  // We detect "reachable" via accessible.FindRoadToTerrainPath against each
  // emergency site's accessible — the same road-based check StartConstruction
  // Job uses internally. We can't use DistrictBuilding.GetDistrictOrConstruction
  // District here because ConstructionDistrict is assigned by a navmesh-update
  // listener that may not have settled at the moment beavers tick after a
  // save-load.
  [HarmonyPatch(typeof(BuilderHubWorkplaceBehavior), nameof(BuilderHubWorkplaceBehavior.Decide))]
  public static class BuilderHubWorkplaceBehaviorPatch {

    private const BindingFlags AnyInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly FieldInfo AccessibleField =
        typeof(BuilderHubWorkplaceBehavior).GetField("_accessible", AnyInstance);
    private static readonly FieldInfo SiteAccessibleField =
        typeof(ConstructionJob).GetField("_constructionSiteAccessible", AnyInstance);

    public static EmergencyConstructionRegistry Registry { get; set; }

    static BuilderHubWorkplaceBehaviorPatch() {
      if (AccessibleField == null) {
        Debug.LogError("[EmergencyPriority] Could not find BuilderHubWorkplaceBehavior._accessible; emergency job override disabled.");
      }
      if (SiteAccessibleField == null) {
        Debug.LogError("[EmergencyPriority] Could not find ConstructionJob._constructionSiteAccessible; cross-district fall-through disabled.");
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
      bool anyReachableEmergency = false;
      foreach (var job in registry.EmergencyJobs) {
        if (!job) {
          continue;
        }
        if (!IsReachableFromHub(job, accessible)) {
          continue;
        }
        anyReachableEmergency = true;
        var (behavior, decision) = job.StartConstructionJob(agent, accessible);
        if (!decision.ShouldReleaseNow) {
          __result = Decision.TransferNow(behavior, in decision);
          return false;
        }
      }
      if (anyReachableEmergency) {
        __result = Decision.ReleaseNow();
        return false;
      }
      return true;
    }

    // Returns true if there's a road-based path from the hub's accessible to
    // any of the construction site's access points. This is the same check
    // StartConstructionJob does internally — duplicating it here so we can
    // tell "all emergencies are out of district" (fall through) from "an
    // emergency is reachable but currently busy" (idle).
    private static bool IsReachableFromHub(ConstructionJob job, Accessible hubAccessible) {
      if (SiteAccessibleField == null) {
        // Reflection failed at static-init — assume reachable so behavior
        // matches the legacy "try every emergency" fallback.
        return true;
      }
      var siteAccessibleHolder = SiteAccessibleField.GetValue(job) as ConstructionSiteAccessible;
      var siteAccessible = siteAccessibleHolder?.Accessible;
      if (siteAccessible == null) {
        return false;
      }
      return hubAccessible.FindRoadToTerrainPath(siteAccessible, out _, out _);
    }

  }

}
