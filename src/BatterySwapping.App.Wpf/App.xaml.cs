using System.Windows;

namespace BatterySwapping.App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App(){DispatcherUnhandledException += (_,e)=>{Services.BssApi.Error(e.Exception);e.Handled=true;};}
    }
}
