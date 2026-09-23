using Poches.Services;
using Poches.Views;

namespace Poches;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(Routes.Pocket, typeof(PocketDetailPage));
        Routing.RegisterRoute(Routes.EditPocket, typeof(EditPocketPage));
        Routing.RegisterRoute(Routes.Movement, typeof(MovementPage));
    }
}
