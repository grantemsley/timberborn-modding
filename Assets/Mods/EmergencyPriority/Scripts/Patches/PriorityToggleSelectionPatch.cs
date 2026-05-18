using System.Reflection;
using HarmonyLib;
using Timberborn.BaseComponentSystem;
using Timberborn.PrioritySystemUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace grantemsley.EmergencyPriority.Patches {

  // Postfix on PriorityToggle.OnValueChanged. When the user clicks a standard
  // priority (newValue = true) on an entity that currently has Emergency set,
  // clear the Emergency flag. Combined with the visual deselection of sibling
  // toggles in EmergencyToggleController, this makes the row behave like a
  // single 6-option radio group: picking any priority becomes the active one.
  //
  // Clicking the active Emergency toggle off goes through
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

    public static void Postfix(PriorityToggle __instance, ChangeEvent<bool> changeEvent) {
      if (!changeEvent.newValue) {
        return;
      }
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
