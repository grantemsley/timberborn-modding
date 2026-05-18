using System.Reflection;
using HarmonyLib;
using Timberborn.BehaviorSystem;
using Timberborn.BuilderHubSystem;
using Timberborn.Common;
using Timberborn.WorkSystem;
using UnityEngine;

namespace grantemsley.EmergencyPriority.Patches {

  // Prefix on WorkerRootBehavior.Decide. The vanilla logic gates work on
  // _workerWorkingHours.AreWorkingHours and falls through to community service /
  // sleep / wandering outside those hours. When this worker is employed at a
  // Builder Hub AND the registry has any emergency jobs, we skip the working-
  // hours gate and call DecideAsWorker() directly. WorkRefuser still applies.
  // Beavers in critical-need state are still handled earlier in the tree by
  // CriticalNeederRootBehavior, so we don't need to check for that here.
  [HarmonyPatch(typeof(WorkerRootBehavior), nameof(WorkerRootBehavior.Decide))]
  public static class WorkerRootBehaviorPatch {

    private const BindingFlags AnyInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly FieldInfo WorkerField =
        typeof(WorkerRootBehavior).GetField("_worker", AnyInstance);
    private static readonly FieldInfo WorkRefuserField =
        typeof(WorkerRootBehavior).GetField("_workRefuser", AnyInstance);
    private static readonly MethodInfo DecideAsWorkerMethod =
        typeof(WorkerRootBehavior).GetMethod("DecideAsWorker", AnyInstance);

    public static EmergencyConstructionRegistry Registry { get; set; }

    static WorkerRootBehaviorPatch() {
      if (WorkerField == null) {
        Debug.LogError("[EmergencyPriority] Could not find WorkerRootBehavior._worker; schedule override disabled.");
      }
      if (WorkRefuserField == null) {
        Debug.LogError("[EmergencyPriority] Could not find WorkerRootBehavior._workRefuser; schedule override disabled.");
      }
      if (DecideAsWorkerMethod == null) {
        Debug.LogError("[EmergencyPriority] Could not find WorkerRootBehavior.DecideAsWorker; schedule override disabled.");
      }
    }

    public static bool Prefix(WorkerRootBehavior __instance,
                              BehaviorAgent agent,
                              ref Decision __result) {
      var registry = Registry;
      if (registry == null || !registry.HasAny) {
        return true;
      }
      if (WorkerField == null || DecideAsWorkerMethod == null) {
        return true;
      }
      var worker = WorkerField.GetValue(__instance) as Worker;
      if (worker == null || !worker.Employed) {
        return true;
      }
      if (!IsEmployedAtBuilderHub(worker)) {
        return true;
      }
      var workRefuser = WorkRefuserField?.GetValue(__instance) as WorkRefuser;
      if (workRefuser != null && workRefuser.RefusesWork) {
        return true;
      }
      __result = (Decision) DecideAsWorkerMethod.Invoke(__instance, null);
      return false;
    }

    private static bool IsEmployedAtBuilderHub(Worker worker) {
      var workplace = worker.Workplace;
      if (!workplace) {
        return false;
      }
      ReadOnlyList<WorkplaceBehavior> behaviors = workplace.WorkplaceBehaviors;
      for (int i = 0; i < behaviors.Count; i++) {
        if (behaviors[i] is BuilderHubWorkplaceBehavior) {
          return true;
        }
      }
      return false;
    }

  }

}
