using System.Collections.Generic;
using Timberborn.ConstructionSites;

namespace grantemsley.EmergencyPriority {

  // Singleton tracking every construction site currently flagged as Emergency.
  // Patches consult this from the hot path of Decide() — keep operations O(1).
  public class EmergencyConstructionRegistry {

    private readonly HashSet<ConstructionJob> _emergencyJobs = new HashSet<ConstructionJob>();

    public bool HasAny => _emergencyJobs.Count > 0;

    public IReadOnlyCollection<ConstructionJob> EmergencyJobs => _emergencyJobs;

    public void Register(ConstructionJob job) {
      if (job) {
        _emergencyJobs.Add(job);
      }
    }

    public void Unregister(ConstructionJob job) {
      if (job) {
        _emergencyJobs.Remove(job);
      }
    }

  }

}
