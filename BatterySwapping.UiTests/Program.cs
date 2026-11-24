using System.Windows;
using BatterySwapping.App.Views;
using BatterySwapping.App.Views.Admin;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Check(!BatterySwapping.App.Services.NavigationPolicy.For("Admin").Kiosk,"admin does not use customer kiosk");
        Check(!BatterySwapping.App.Services.NavigationPolicy.For("KyThuatVien").WalletAdministration,"technician cannot manage wallets");
        Check(BatterySwapping.App.Services.NavigationPolicy.For("KhachHang").Kiosk,"customer has kiosk");
        Check(!BatterySwapping.App.Services.NavigationPolicy.For(null).Staff,"guest has no operating controls");
        Application app;
        if(args.Contains("--simulator")){app=new BatterySwapping.Simulator.App();}
        else {var desktop=new BatterySwapping.App.App();desktop.InitializeComponent();app=desktop;}
        var login=new LoginWindow();
        Check(login.FindName("txtUsername")!=null,"original XAML login loads");
        var dashboard=new StationDashboardView();
        Check(dashboard.FindName("icSlots")!=null,"original cabinet dashboard loads");
        Check(new BatteryManagementView().FindName("dgBatteries")!=null,"original battery management loads");
        Check(new CustomerManagementView().FindName("dgCustomers")!=null,"original customer management loads");
        Check(new SafetyAlertsView().FindName("dgAlerts")!=null,"original safety alerts load");
        Check(new SwapSessionsView().FindName("sessions")!=null,"swap session workspace loads");
        Check(new OverviewView().FindName("stations")!=null,"overview loads");
        Check(new OverviewView(true).FindName("summary")!=null,"customer wallet loads");
        Check(new BatterySwapping.App.Views.Kiosk.CustomerDashboardView().FindName("topupHistory")!=null,"driver profile wallet and personal history load");
        Check(SessionRow.Label("NeedsAssistance")=="Cần hỗ trợ","support states translated");
        Check(!new SessionRow(new BatterySwapping.Contracts.SwapSession{State="Completed"}).Active,"completed session has no cancellation");
        foreach(var role in new[]{"Admin","KyThuatVien","KhachHang"})
        {
            typeof(BatterySwapping.App.Services.AuthService).GetProperty("CurrentUser")!.GetSetMethod(true)!.Invoke(null,new object[]{new BatterySwapping.App.Models.TaiKhoan{VaiTro=role}});
            var window=new BatterySwapping.App.MainWindow();
            Check(((System.Windows.Controls.Button)window.FindName("btnNavKiosk")).Visibility==(role=="KhachHang"?Visibility.Visible:Visibility.Collapsed),role+" kiosk menu");
            Check(((System.Windows.Controls.Button)window.FindName("btnNavOverview")).Visibility==(role=="Admin"?Visibility.Visible:Visibility.Collapsed),role+" financial overview menu");
            Check(((System.Windows.Controls.Button)window.FindName("btnNavCustomers")).Visibility==(role=="Admin"?Visibility.Visible:Visibility.Collapsed),role+" customer administration menu");
            Check(((System.Windows.Controls.Button)window.FindName("btnEmergencyCutoff")).Visibility==Visibility.Collapsed,"global cutoff removed for "+role);
            window.Close();
        }
        typeof(BatterySwapping.App.Services.AuthService).GetProperty("CurrentUser")!.GetSetMethod(true)!.Invoke(null,new object?[]{null});
        var main=new BatterySwapping.App.MainWindow();
        Check(main.FindName("mainContentArea")!=null,"original main navigation loads");
        main.Close();

        // Verify Admin clicking btnNavKiosk opens Kiosk mode
        typeof(BatterySwapping.App.Services.AuthService).GetProperty("CurrentUser")!.GetSetMethod(true)!.Invoke(null,new object[]{new BatterySwapping.App.Models.TaiKhoan{VaiTro="Admin"}});
        var adminWin = new BatterySwapping.App.MainWindow();
        var kioskBtn = (System.Windows.Controls.Button)adminWin.FindName("btnNavKiosk");
        kioskBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var content = (System.Windows.Controls.ContentControl)adminWin.FindName("mainContentArea");
        Check(content.Content is BatterySwapping.App.Views.Kiosk.KioskWelcomeView, "Admin clicking btnNavKiosk opens KioskWelcomeView");
        adminWin.Close();

        // Verify KioskSwappingView simulation buttons exist
        var dummyCust = new BatterySwapping.App.Models.KhachHang { MaKH = 1, HoTen = "Test", SoDienThoai = "0987654321", SoDuVi = 100000 };
        var swappingView = new BatterySwapping.App.Views.Kiosk.KioskSwappingView(dummyCust);
        Check(swappingView.FindName("btnSimulateOld") != null, "in-app simulation button OLD_INSERTED exists");
        Check(swappingView.FindName("btnSimulateNew") != null, "in-app simulation button NEW_TAKEN exists");

        login.Close();app.Shutdown();
    }
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);}
}

