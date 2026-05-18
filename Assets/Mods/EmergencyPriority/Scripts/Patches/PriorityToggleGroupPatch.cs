using HarmonyLib;
using Timberborn.PrioritySystem;
using Timberborn.PrioritySystemUI;

namespace grantemsley.EmergencyPriority.Patches {

  // PriorityToggleGroup is shared by builder priority AND workplace priority
  // panels, but Controllers only has entries for groups built by
  // BuilderPriorityToggleGroupFactory. Lookups for workplace groups silently
  // miss, so these postfixes are safe to apply to all groups.

  [HarmonyPatch(typeof(PriorityToggleGroup), nameof(PriorityToggleGroup.Enable))]
  public static class PriorityToggleGroupEnablePatch {
    public static void Postfix(PriorityToggleGroup __instance, IPrioritizable prioritizable) {
      if (BuilderPriorityToggleGroupFactoryPatch.Controllers
          .TryGetValue(__instance, out var controller)) {
        controller.Enable(prioritizable);
      }
    }
  }

  [HarmonyPatch(typeof(PriorityToggleGroup), nameof(PriorityToggleGroup.Disable))]
  public static class PriorityToggleGroupDisablePatch {
    public static void Postfix(PriorityToggleGroup __instance) {
      if (BuilderPriorityToggleGroupFactoryPatch.Controllers
          .TryGetValue(__instance, out var controller)) {
        controller.Disable();
      }
    }
  }

  [HarmonyPatch(typeof(PriorityToggleGroup), nameof(PriorityToggleGroup.UpdateGroup))]
  public static class PriorityToggleGroupUpdatePatch {
    public static void Postfix(PriorityToggleGroup __instance) {
      if (BuilderPriorityToggleGroupFactoryPatch.Controllers
          .TryGetValue(__instance, out var controller)) {
        controller.UpdateState();
      }
    }
  }

}
