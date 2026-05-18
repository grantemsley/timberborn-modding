using System;
using System.Collections.Generic;
using Timberborn.ConstructionSites;

namespace grantemsley.EmergencyPriority {

  // Singleton tracking every construction site currently flagged as Emergency.
  // Patches consult this from the hot path of Decide() — keep operations O(1).
  public class EmergencyConstructionRegistry {

    private readonly HashSet<ConstructionJob> _emergencyJobs = new HashSet<ConstructionJob>();

    public bool HasAny => _emergencyJobs.Count > 0;

    public IReadOnlyCollection<ConstructionJob> EmergencyJobs => _emergencyJobs;

    // Fired when a job transitions from unregistered to registered. Listeners
    // (notably EmergencyInterruptionService) use this to wake builders whose
    // current executor would otherwise keep them busy for hours.
    public event Action<ConstructionJob> JobRegistered;

    public void Register(ConstructionJob job) {
      if (job && _emergencyJobs.Add(job)) {
        JobRegistered?.Invoke(job);
      }
    }

    public void Unregister(ConstructionJob job) {
      if (job) {
        _emergencyJobs.Remove(job);
      }
    }

  }

}
