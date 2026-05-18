using System.Reflection;
using Timberborn.BehaviorSystem;
using Timberborn.BuilderHubSystem;
using Timberborn.Carrying;
using Timberborn.Common;
using Timberborn.ConstructionSites;
using Timberborn.GameDistricts;
using Timberborn.Goods;
using Timberborn.InventorySystem;
using Timberborn.Navigation;
using Timberborn.NeedBehaviorSystem;
using Timberborn.NeedSystem;
using Timberborn.RecoveredGoodSystem;
using Timberborn.SingletonSystem;
using Timberborn.WorkSystem;
using UnityEngine;

namespace grantemsley.EmergencyPriority {

  // Listens to EmergencyConstructionRegistry.JobRegistered. When a new emergency
  // site appears, it interrupts the rest-style executor (sleep / eat / drink —
  // anything driven by ApplyEffectExecutor) of every builder employed at a hub
  // in the same district, so they re-evaluate immediately instead of finishing
  // their nap and only then noticing the emergency.
  //
  // Critical-state beavers are skipped — they'd just resume their critical-need
  // behavior on re-evaluation, and interrupting them is wasted work.
  //
  // Interruption is done by setting ApplyEffectExecutor._finishTimestamp to 0.
  // On the next BehaviorManager.Tick, the executor's own Tick() observes the
  // expired timestamp, calls TurnOffAnimation (so the beaver doesn't stay stuck
  // in the "Sleeping" pose), and returns Success — which clears the running
  // executor slot and lets ProcessBehaviors run the tree from the top.
  public class EmergencyInterruptionService : ILoadableSingleton {

    private const BindingFlags AnyInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly FieldInfo RunningExecutorField =
        typeof(BehaviorManager).GetField("_runningExecutor", AnyInstance);
    private static readonly FieldInfo RunningBehaviorField =
        typeof(BehaviorManager).GetField("_runningBehavior", AnyInstance);
    private static readonly FieldInfo FinishTimestampField =
        typeof(ApplyEffectExecutor).GetField("_finishTimestamp", AnyInstance);

    private readonly EmergencyConstructionRegistry _registry;
    private readonly RecoveredGoodStackSpawner _recoveredGoodStackSpawner;

    public EmergencyInterruptionService(EmergencyConstructionRegistry registry,
                                        RecoveredGoodStackSpawner recoveredGoodStackSpawner) {
      _registry = registry;
      _recoveredGoodStackSpawner = recoveredGoodStackSpawner;
    }

    public void Load() {
      if (RunningExecutorField == null) {
        Debug.LogError("[EmergencyPriority] Could not find BehaviorManager._runningExecutor; cannot interrupt resting builders on Emergency.");
      }
      if (RunningBehaviorField == null) {
        Debug.LogError("[EmergencyPriority] Could not find BehaviorManager._runningBehavior; cannot interrupt hauling builders on Emergency.");
      }
      if (FinishTimestampField == null) {
        Debug.LogError("[EmergencyPriority] Could not find ApplyEffectExecutor._finishTimestamp; cannot interrupt resting builders on Emergency.");
      }
      _registry.JobRegistered += OnJobRegistered;
    }

    private void OnJobRegistered(ConstructionJob job) {
      if (RunningExecutorField == null || FinishTimestampField == null) {
        return;
      }
      if (!job) {
        return;
      }
      var district = job.GetComponent<DistrictBuilding>()?.District;
      if (district == null) {
        return;
      }
      var districtBuildingRegistry = district.GetComponent<DistrictBuildingRegistry>();
      if (districtBuildingRegistry == null) {
        return;
      }
      foreach (var workplace in districtBuildingRegistry.GetEnabledBuildings<Workplace>()) {
        if (!HasBuilderHubBehavior(workplace)) {
          continue;
        }
        foreach (var worker in workplace.AssignedWorkers) {
          TryInterrupt(worker);
        }
      }
    }

    private static bool HasBuilderHubBehavior(Workplace workplace) {
      ReadOnlyList<WorkplaceBehavior> behaviors = workplace.WorkplaceBehaviors;
      for (int i = 0; i < behaviors.Count; i++) {
        if (behaviors[i] is BuilderHubWorkplaceBehavior) {
          return true;
        }
      }
      return false;
    }

    private void TryInterrupt(Worker worker) {
      if (worker == null) {
        return;
      }
      var behaviorManager = worker.GetComponent<BehaviorManager>();
      if (behaviorManager == null) {
        return;
      }
      var needManager = worker.GetComponent<NeedManager>();
      if (needManager != null && needManager.AnyNeedIsInCriticalState()) {
        return;
      }
      var executor = RunningExecutorField.GetValue(behaviorManager);
      if (executor == null) {
        return;
      }
      // Resting (sleep/eat/drink): force the executor's own finish path so
      // animations clean up properly.
      if (executor is ApplyEffectExecutor applyEffectExecutor) {
        FinishTimestampField.SetValue(applyEffectExecutor, 0f);
        return;
      }
      // Hauling: if the destination is an emergency site, leave it alone —
      // that haul is feeding the emergency. Otherwise drop the cargo on the
      // ground as a recovered-good stack, release reservations, and null
      // the executor so the beaver re-decides immediately into WorkerRootBehavior.
      var runningBehavior = RunningBehaviorField?.GetValue(behaviorManager);
      if (runningBehavior is CarryRootBehavior) {
        if (IsHaulingToEmergencySite(worker)) {
          return;
        }
        DropCargoAndReleaseReservations(worker);
        RunningExecutorField.SetValue(behaviorManager, null);
      }
    }

    private bool IsHaulingToEmergencySite(Worker worker) {
      var reserver = worker.GetComponent<GoodReserver>();
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
      foreach (var emergencyJob in _registry.EmergencyJobs) {
        if (emergencyJob == destJob) {
          return true;
        }
      }
      return false;
    }

    private void DropCargoAndReleaseReservations(Worker worker) {
      var goodCarrier = worker.GetComponent<GoodCarrier>();
      if (goodCarrier != null && goodCarrier.IsCarrying) {
        var carried = goodCarrier.CarriedGoods;
        if (carried.Amount > 0) {
          var gridPos = NavigationCoordinateSystem.WorldToGridInt(worker.Transform.position);
          _recoveredGoodStackSpawner.AddAwaitingGoods(gridPos, new[] { carried });
        }
        goodCarrier.EmptyHands();
      }
      var reserver = worker.GetComponent<GoodReserver>();
      if (reserver != null) {
        if (reserver.HasReservedCapacity) {
          reserver.UnreserveCapacity();
        }
        if (reserver.HasReservedStock) {
          reserver.UnreserveStock();
        }
      }
    }

  }

}
