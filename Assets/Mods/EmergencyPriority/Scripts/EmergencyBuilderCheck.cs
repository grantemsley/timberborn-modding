using Timberborn.BaseComponentSystem;
using Timberborn.BuilderHubSystem;
using Timberborn.Common;
using Timberborn.WorkSystem;

namespace grantemsley.EmergencyPriority {

  internal static class EmergencyBuilderCheck {

    public static bool IsEmergencyBuilder(BaseComponent entity, EmergencyConstructionRegistry registry) {
      if (registry == null || !registry.HasAny || entity == null) {
        return false;
      }
      var worker = entity.GetComponent<Worker>();
      if (worker == null || !worker.Employed) {
        return false;
      }
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
