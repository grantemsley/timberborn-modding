using Bindito.Core;
using Timberborn.EntityPanelSystem;

namespace grantemsley.BeaverTaskDisplay {
  [Context("Game")]
  public class BeaverTaskConfigurator : Configurator {

    protected override void Configure() {
      Bind<BeaverTaskFragment>().AsSingleton();
      MultiBind<EntityPanelModule>().ToProvider<EntityPanelModuleProvider>().AsSingleton();
    }

    private class EntityPanelModuleProvider : IProvider<EntityPanelModule> {

      private readonly BeaverTaskFragment _fragment;

      public EntityPanelModuleProvider(BeaverTaskFragment fragment) {
        _fragment = fragment;
      }

      public EntityPanelModule Get() {
        var builder = new EntityPanelModule.Builder();
        // CarryingUI's GoodCarrierFragment uses AddBottomFragment(_, 0).
        // We use 100 to position our fragment after it.
        builder.AddBottomFragment(_fragment, 100);
        return builder.Build();
      }

    }

  }
}
