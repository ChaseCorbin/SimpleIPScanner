using System.Windows;
using Velopack;

namespace SimpleIPScanner
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Velopack must run before any application logic. Handles install/uninstall
            // lifecycle events and exits the process early when appropriate.
            VelopackApp.Build().Run();
            base.OnStartup(e);
        }
    }
}
