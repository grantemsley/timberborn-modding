using Bindito.Core;
using Timberborn.ConstructionSites;
using Timberborn.SingletonSystem;
using Timberborn.TemplateInstantiation;

namespace grantemsley.EmergencyPriority {

  [Context("Game")]
  public class EmergencyPriorityConfigurator : Configurator {

    protected override void Configure() {
      Bind<EmergencyConstructionRegistry>().AsSingleton();
      Bind<EmergencyConstructable>().AsTransient();
      MultiBind<ILoadableSingleton>().To<EmergencyPatchBootstrap>().AsSingleton();
      MultiBind<TemplateModule>().ToProvider(ProvideTemplateModule).AsSingleton();
    }

    private static TemplateModule ProvideTemplateModule() {
      var builder = new TemplateModule.Builder();
      builder.AddDecorator<ConstructionSite, EmergencyConstructable>();
      return builder.Build();
    }

  }

}
