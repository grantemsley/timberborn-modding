using Timberborn.BaseComponentSystem;
using Timberborn.PrioritySystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace grantemsley.EmergencyPriority {

  // Owns the Emergency toggle attached to a single PriorityToggleGroup. Mirrors
  // the lifecycle methods of PriorityToggle (Enable / Disable / UpdateState) so
  // patches on PriorityToggleGroup can dispatch to it without us needing to
  // subclass or interact with the group's internal _toggles list.
  //
  // The toggle hides itself entirely when the currently-selected entity has no
  // EmergencyConstructable component — that's how we keep it off rubble piles
  // and demolition panels, which share BuilderPriorityToggleGroupFactory with
  // construction sites.
  public class EmergencyToggleController {

    private static readonly Color EmergencyRed = new Color(0.92f, 0.20f, 0.20f, 1f);
    private static readonly string CheckedClass = "priority-toggle--checked";

    private readonly Toggle _toggle;
    private readonly VisualElement _togglesWrapper;
    private EmergencyConstructable _current;

    public EmergencyToggleController(Toggle toggle, VisualElement togglesWrapper) {
      _toggle = toggle;
      _togglesWrapper = togglesWrapper;
      _toggle.style.unityBackgroundImageTintColor = new StyleColor(EmergencyRed);
      _toggle.RegisterValueChangedCallback(OnValueChanged);
    }

    public void Enable(IPrioritizable prioritizable) {
      _current = (prioritizable as BaseComponent)?.GetComponent<EmergencyConstructable>();
      _toggle.style.display = _current != null ? DisplayStyle.Flex : DisplayStyle.None;
      UpdateState();
    }

    public void Disable() {
      _current = null;
    }

    public void UpdateState() {
      if (_current == null) {
        return;
      }
      bool isEmergency = _current.IsEmergency;
      _toggle.SetValueWithoutNotify(isEmergency);
      _toggle.EnableInClassList(CheckedClass, isEmergency);
      if (isEmergency) {
        SuppressSiblingChecks();
      }
    }

    private void OnValueChanged(ChangeEvent<bool> evt) {
      _current?.SetEmergency(evt.newValue);
      _toggle.EnableInClassList(CheckedClass, evt.newValue);
    }

    // When Emergency is active, visually unselect the 5 standard priority
    // toggles so the row reads as "Emergency overrides them". PriorityToggle's
    // own UpdateState will re-add the checked class next frame; we run via the
    // PriorityToggleGroup.UpdateGroup postfix, which is after the per-toggle
    // updates, so removing the class every frame keeps it suppressed.
    private void SuppressSiblingChecks() {
      if (_togglesWrapper == null) {
        return;
      }
      int count = _togglesWrapper.childCount;
      for (int i = 0; i < count; i++) {
        var child = _togglesWrapper[i];
        if (child == _toggle) {
          continue;
        }
        child.RemoveFromClassList(CheckedClass);
      }
    }

  }

}
