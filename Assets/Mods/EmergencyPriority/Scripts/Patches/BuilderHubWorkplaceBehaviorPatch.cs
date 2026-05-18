using System.Reflection;
using HarmonyLib;
using Timberborn.BehaviorSystem;
using Timberborn.BuilderHubSystem;
using Timberborn.ConstructionSites;
using Timberborn.Navigation;
using UnityEngine;

namespace grantemsley.EmergencyPriority.Patches {

  // Prefix on BuilderHubWorkplaceBehavior.Decide. When the registry holds any
  // emergency jobs, we try those first via the same StartConstructionJob path
  // the game uses. If one accepts, we short-circuit and skip the regular
  // Priorities.Descending loop. If none accept (out of district, blocked, etc.),
  // we fall through to the original method.
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
      foreach (var job in registry.EmergencyJobs) {
        if (!job) {
          continue;
        }
        var (behavior, decision) = job.StartConstructionJob(agent, accessible);
        if (!decision.ShouldReleaseNow) {
          __result = Decision.TransferNow(behavior, in decision);
          return false;
        }
      }
      return true;
    }

  }

}
