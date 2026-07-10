using System.Windows;

namespace CFDeployer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Services.StorageService.Initialize();
        }
    }
}
