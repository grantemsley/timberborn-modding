using System.Reflection;
using HarmonyLib;
using Timberborn.BaseComponentSystem;
using Timberborn.PrioritySystemUI;
using UnityEngine;

namespace grantemsley.EmergencyPriority.Patches {

  // Postfix on PriorityToggle.OnValueChanged. When the user clicks any standard
  // priority toggle on an entity that currently has Emergency set, clear the
  // Emergency flag. Combined with the visual deselection of sibling toggles in
  // EmergencyToggleController, this makes the row behave like a single 6-option
  // radio group: picking any priority becomes the active one.
  //
  // We clear regardless of newValue's direction. Reason: when Emergency is on,
  // the priority toggles' Toggle.value still tracks the underlying
  // BuilderPrioritizable.Priority (we only strip the visual "checked" CSS
  // class, not the value). So clicking the underlying-active priority toggles
  // its value from true to false, firing OnValueChanged with newValue=false.
  // A naive `if (!newValue) return` skips this case, which is why clicking the
  // currently-set priority while Emergency was on did nothing. Vanilla
  // OnValueChanged still gates `SetPriority(_priority)` on newValue=true, so
  // the underlying priority stays correct either way; the next UpdateGroup
  // tick fixes the visual.
  //
  // Clicking the Emergency toggle off goes through
  // EmergencyToggleController.OnValueChanged instead, which leaves the
  // underlying BuilderPrioritizable.Priority alone — so it naturally "falls
  // back" to the previously selected priority.
  [HarmonyPatch(typeof(PriorityToggle), nameof(PriorityToggle.OnValueChanged))]
  public static class PriorityToggleSelectionPatch {

    private const BindingFlags AnyInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly FieldInfo PrioritizableField =
        typeof(PriorityToggle).GetField("_prioritizable", AnyInstance);

    static PriorityToggleSelectionPatch() {
      if (PrioritizableField == null) {
        Debug.LogError("[EmergencyPriority] Could not find PriorityToggle._prioritizable; Emergency will not auto-clear when a regular priority is picked.");
      }
    }

    public static void Postfix(PriorityToggle __instance) {
      var prioritizable = PrioritizableField?.GetValue(__instance) as BaseComponent;
      if (prioritizable == null) {
        return;
      }
      var emergency = prioritizable.GetComponent<EmergencyConstructable>();
      if (emergency != null && emergency.IsEmergency) {
        emergency.SetEmergency(false);
      }
    }

  }

}
