using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Timberborn.BuilderPrioritySystemUI;
using Timberborn.CoreUI;
using Timberborn.PrioritySystemUI;
using Timberborn.TooltipSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace grantemsley.EmergencyPriority.Patches {

  // Postfix on BuilderPriorityToggleGroupFactory.Create. After the standard 5
  // priority toggles are built, we append a 6th Emergency toggle into the same
  // TogglesWrapper and register an EmergencyToggleController against the
  // returned PriorityToggleGroup. Lifecycle dispatch happens via patches on
  // PriorityToggleGroup.Enable / Disable / UpdateGroup.
  //
  // The factory is also used by DemolishableFragment and RecoveredGoodStackFragment,
  // not just ConstructionSiteFragment — so the toggle gets added to all three.
  // The controller hides itself when the selected entity has no
  // EmergencyConstructable, keeping it functional only on construction sites.
  [HarmonyPatch(typeof(BuilderPriorityToggleGroupFactory), nameof(BuilderPriorityToggleGroupFactory.Create))]
  public static class BuilderPriorityToggleGroupFactoryPatch {

    private const BindingFlags AnyInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly FieldInfo InnerFactoryField =
        typeof(BuilderPriorityToggleGroupFactory).GetField(
            "_priorityToggleGroupFactory", AnyInstance);
    private static readonly FieldInfo VisualElementLoaderField =
        typeof(PriorityToggleGroupFactory).GetField(
            "_visualElementLoader", AnyInstance);

    private static readonly Color EmergencyRed = new Color(0.92f, 0.20f, 0.20f, 1f);
    private const string PriorityToggleUxmlPath = "Game/EntityPanel/PriorityToggle";
    private const string EmergencyTooltipLocKey = "grantemsley.EmergencyPriority.EmergencyTooltip";

    // Toggles awaiting tooltip registration. EntityPanel is also an
    // ILoadableSingleton and may initialize its fragments (calling our
    // Postfix) BEFORE our EmergencyPatchBootstrap.Load assigns the registrar.
    // We queue the toggle in that case and drain on assignment.
    private static readonly List<Toggle> PendingTooltipToggles = new List<Toggle>();
    private static ITooltipRegistrar _tooltipRegistrar;

    public static ITooltipRegistrar TooltipRegistrar {
      get => _tooltipRegistrar;
      set {
        _tooltipRegistrar = value;
        if (value != null) {
          for (int i = 0; i < PendingTooltipToggles.Count; i++) {
            value.RegisterLocalizable(PendingTooltipToggles[i], EmergencyTooltipLocKey);
          }
          PendingTooltipToggles.Clear();
        }
      }
    }

    // PriorityToggleGroup → its emergency controller. Weak so dead groups (from
    // previous scene loads) are collected and don't accumulate over a session.
    public static readonly ConditionalWeakTable<PriorityToggleGroup, EmergencyToggleController>
        Controllers = new ConditionalWeakTable<PriorityToggleGroup, EmergencyToggleController>();

    static BuilderPriorityToggleGroupFactoryPatch() {
      if (InnerFactoryField == null || VisualElementLoaderField == null) {
        Debug.LogError("[EmergencyPriority] Could not resolve VisualElementLoader via reflection; Emergency toggle will not be added.");
      }
    }

    public static void Postfix(BuilderPriorityToggleGroupFactory __instance,
                                VisualElement parent,
                                PriorityToggleGroup __result) {
      if (__result == null || parent == null || parent.childCount == 0) {
        return;
      }
      VisualElement groupRoot = parent[parent.childCount - 1];
      VisualElement togglesWrapper = groupRoot.Q<VisualElement>("TogglesWrapper");
      if (togglesWrapper == null) {
        return;
      }
      var loader = GetVisualElementLoader(__instance);
      if (loader == null) {
        return;
      }
      var toggle = BuildToggle(togglesWrapper, loader);
      if (toggle == null) {
        return;
      }
      Controllers.Remove(__result);
      Controllers.Add(__result, new EmergencyToggleController(toggle, togglesWrapper));
    }

    private static VisualElementLoader GetVisualElementLoader(
        BuilderPriorityToggleGroupFactory factory) {
      if (InnerFactoryField == null || VisualElementLoaderField == null) {
        return null;
      }
      var innerFactory = InnerFactoryField.GetValue(factory) as PriorityToggleGroupFactory;
      if (innerFactory == null) {
        return null;
      }
      return VisualElementLoaderField.GetValue(innerFactory) as VisualElementLoader;
    }

    // Load the same UXML the game uses for standard priority toggles. This
    // ensures the Toggle goes through VisualElementInitializer, which registers
    // the click-sound callback (UISoundInitializer) and any other element-level
    // setup the game expects.
    private static Toggle BuildToggle(VisualElement parent, VisualElementLoader loader) {
      var loaded = loader.LoadVisualElement(PriorityToggleUxmlPath);
      var toggle = loaded as Toggle ?? loaded.Q<Toggle>("PriorityToggle");
      if (toggle == null) {
        return null;
      }
      parent.Add(toggle);
      // Hide the default Unity checkmark glyph; the priority-toggle UXML doesn't
      // override it for us, and we want only the "!" overlay to be visible.
      var checkmark = toggle.Q<VisualElement>("unity-checkmark");
      if (checkmark != null) {
        checkmark.style.display = DisplayStyle.None;
      }
      // Hide the BaseField<bool> label slot too (class name varies by Unity
      // version, so hide any Label descendant).
      foreach (var label in toggle.Query<Label>().ToList()) {
        label.style.display = DisplayStyle.None;
      }
      // Tint the "checked" highlight (the priority-toggle--checked background)
      // red so the selection state matches the icon color.
      toggle.style.unityBackgroundImageTintColor = new StyleColor(EmergencyRed);
      AddIcon(toggle);
      RegisterTooltip(toggle);
      return toggle;
    }

    private static void RegisterTooltip(Toggle toggle) {
      var registrar = _tooltipRegistrar;
      if (registrar != null) {
        registrar.RegisterLocalizable(toggle, EmergencyTooltipLocKey);
      } else {
        PendingTooltipToggles.Add(toggle);
      }
    }

    // Render a red bold "!" centered inside the 24x24 toggle.
    private static void AddIcon(Toggle toggle) {
      var icon = new Label("!");
      icon.AddToClassList("emergency-priority-icon");
      icon.style.position = Position.Absolute;
      icon.style.left = 0;
      icon.style.right = 0;
      icon.style.top = 0;
      icon.style.bottom = 0;
      icon.style.unityTextAlign = TextAnchor.MiddleCenter;
      icon.style.fontSize = 20;
      icon.style.unityFontStyleAndWeight = FontStyle.Bold;
      icon.style.color = new StyleColor(EmergencyRed);
      icon.pickingMode = PickingMode.Ignore;
      toggle.Add(icon);
    }

  }

}
