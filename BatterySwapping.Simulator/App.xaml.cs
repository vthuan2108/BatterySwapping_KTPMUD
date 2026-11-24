using System.Windows;
namespace BatterySwapping.Simulator;
public partial class App : Application
{
    public App()
    {
        // This executable hosts views from the desktop assembly; load their application resources first.
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/BatterySwapping.App;component/Resources/AppStyles.xaml", UriKind.Absolute)
        });
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_,err)=> { BatterySwapping.App.Services.BssApi.Error(err.Exception); err.Handled=true; };
        new BatterySwapping.App.Views.LoginWindow().Show();
    }
}
