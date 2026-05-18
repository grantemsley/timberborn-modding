using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.ConstructionSites;
using Timberborn.Persistence;
using Timberborn.WorldPersistence;

namespace grantemsley.EmergencyPriority {

  // Sits alongside BuilderPrioritizable + ConstructionJob on every construction
  // site. Holds the Emergency flag, persists it, and (un)registers the job with
  // the EmergencyConstructionRegistry whenever the flag flips or the site moves
  // in/out of the unfinished state.
  public class EmergencyConstructable : BaseComponent,
                                        IAwakableComponent,
                                        IUnfinishedStateListener,
                                        IPersistentEntity {

    public static readonly ComponentKey EmergencyConstructableKey =
        new ComponentKey("EmergencyConstructable");
    public static readonly PropertyKey<bool> IsEmergencyKey =
        new PropertyKey<bool>("IsEmergency");

    private readonly EmergencyConstructionRegistry _registry;
    private ConstructionJob _constructionJob;
    private bool _isUnfinished;

    public bool IsEmergency { get; private set; }

    public EmergencyConstructable(EmergencyConstructionRegistry registry) {
      _registry = registry;
    }

    public void Awake() {
      _constructionJob = GetComponent<ConstructionJob>();
    }

    public void SetEmergency(bool isEmergency) {
      if (IsEmergency == isEmergency) {
        return;
      }
      IsEmergency = isEmergency;
      if (_isUnfinished) {
        if (isEmergency) {
          _registry.Register(_constructionJob);
        } else {
          _registry.Unregister(_constructionJob);
        }
      }
    }

    public void OnEnterUnfinishedState() {
      _isUnfinished = true;
      if (IsEmergency) {
        _registry.Register(_constructionJob);
      }
    }

    public void OnExitUnfinishedState() {
      _isUnfinished = false;
      if (IsEmergency) {
        _registry.Unregister(_constructionJob);
      }
    }

    public void Save(IEntitySaver entitySaver) {
      if (IsEmergency) {
        entitySaver.GetComponent(EmergencyConstructableKey).Set(IsEmergencyKey, true);
      }
    }

    public void Load(IEntityLoader entityLoader) {
      if (entityLoader.TryGetComponent(EmergencyConstructableKey, out var objectLoader)) {
        IsEmergency = objectLoader.Get(IsEmergencyKey);
      }
    }

  }

}
