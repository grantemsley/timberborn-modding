using HarmonyLib;
using Timberborn.ModManagerScene;
using UnityEngine;

namespace grantemsley.EmergencyPriority {

  // Entry point invoked once at game startup by ModCodeStarter. Applies all
  // [HarmonyPatch]-annotated classes in this assembly.
  public class EmergencyPriorityModStarter : IModStarter {

    public const string HarmonyId = "grantemsley.EmergencyPriority";

    public void StartMod(IModEnvironment modEnvironment) {
      try {
        var harmony = new Harmony(HarmonyId);
        harmony.PatchAll(typeof(EmergencyPriorityModStarter).Assembly);
      } catch (System.Exception e) {
        Debug.LogError($"[EmergencyPriority] Failed to apply Harmony patches: {e}");
      }
    }

  }

}
