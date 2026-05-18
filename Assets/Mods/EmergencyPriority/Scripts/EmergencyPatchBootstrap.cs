using grantemsley.EmergencyPriority.Patches;
using Timberborn.SingletonSystem;
using Timberborn.TooltipSystem;

namespace grantemsley.EmergencyPriority {

  // Bridges DI-resolved singletons into the static fields the Harmony patches
  // reference. Runs once per scene load via ILoadableSingleton.Load.
  public class EmergencyPatchBootstrap : ILoadableSingleton {

    private readonly EmergencyConstructionRegistry _registry;
    private readonly ITooltipRegistrar _tooltipRegistrar;

    public EmergencyPatchBootstrap(EmergencyConstructionRegistry registry,
                                   ITooltipRegistrar tooltipRegistrar) {
      _registry = registry;
      _tooltipRegistrar = tooltipRegistrar;
    }

    public void Load() {
      BuilderHubWorkplaceBehaviorPatch.Registry = _registry;
      WorkerRootBehaviorPatch.Registry = _registry;
      BeaverNeedBehaviorPickerPatch.Registry = _registry;
      SleepNeedBehaviorDecidePatch.Registry = _registry;
      SleepNeedBehaviorShouldSleepAtHomePatch.Registry = _registry;
      SleepNeedBehaviorSleepOutsidePatch.Registry = _registry;
      DistrictNeedBehaviorServicePatch.Registry = _registry;
      CarryRootBehaviorPatch.Registry = _registry;
      BuilderPriorityToggleGroupFactoryPatch.TooltipRegistrar = _tooltipRegistrar;
    }

  }

}
