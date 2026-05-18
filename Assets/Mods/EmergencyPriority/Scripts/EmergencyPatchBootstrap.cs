using grantemsley.EmergencyPriority.Patches;
using Timberborn.SingletonSystem;

namespace grantemsley.EmergencyPriority {

  // Bridges the registry singleton into the static fields the Harmony patches
  // reference. Runs once per scene load via ILoadableSingleton.Load.
  public class EmergencyPatchBootstrap : ILoadableSingleton {

    private readonly EmergencyConstructionRegistry _registry;

    public EmergencyPatchBootstrap(EmergencyConstructionRegistry registry) {
      _registry = registry;
    }

    public void Load() {
      BuilderHubWorkplaceBehaviorPatch.Registry = _registry;
      WorkerRootBehaviorPatch.Registry = _registry;
      BeaverNeedBehaviorPickerPatch.Registry = _registry;
      SleepNeedBehaviorShouldSleepAtHomePatch.Registry = _registry;
      SleepNeedBehaviorSleepOutsidePatch.Registry = _registry;
    }

  }

}
