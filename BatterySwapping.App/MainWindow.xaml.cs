using System.Windows;
using BatterySwapping.App.Models;
using BatterySwapping.App.Services;
using BatterySwapping.App.Views.Admin;
using BatterySwapping.App.Views.Kiosk;

namespace BatterySwapping.App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly SafetyMonitorService _safetyMonitorService;
        private readonly StationDashboardView _dashboardView;
        private readonly BatteryManagementView _batteryView;
        private readonly CustomerManagementView _customerView;
        private readonly TransactionHistoryView _transactionView;
        private readonly SafetyAlertsView _alertsView;

        public MainWindow(bool openInKioskMode = false)
        {
            InitializeComponent();
            bool staff = !openInKioskMode && AuthService.CurrentUser?.VaiTro is "Admin" or "KyThuatVien";
            foreach (var button in new[] { btnNavDashboard, btnNavBatteries, btnNavCustomers, btnNavTransactions, btnNavAlerts })
                button.Visibility = staff ? Visibility.Visible : Visibility.Collapsed;
            btnEmergencyCutoff.Visibility = staff ? Visibility.Visible : Visibility.Collapsed;
            if (!staff) openInKioskMode = true;

            _safetyMonitorService = new SafetyMonitorService();
            _dashboardView = new StationDashboardView();
            _batteryView = new BatteryManagementView();
            _customerView = new CustomerManagementView();
            _transactionView = new TransactionHistoryView();
            _alertsView = new SafetyAlertsView();

            // Hiển thị thông tin người dùng đăng nhập
            if (AuthService.CurrentUser != null)
            {
                if (lblCurrentUserName != null) lblCurrentUserName.Text = AuthService.CurrentUser.HoTen;
                if (lblCurrentUserRole != null) 
                {
                    lblCurrentUserRole.Text = $"Vai trò: {AuthService.CurrentUser.VaiTro}";
                    if (AuthService.CurrentUser.VaiTro == "KyThuatVien")
                    {
                        lblCurrentUserRole.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 189, 248)); // Sky blue
                        if (lblSystemStatus != null)
                        {
                            lblSystemStatus.Text = "Chế độ Kỹ thuật viên (Bảo trì trạm, kiểm định BMS, xử lý sự cố IoT) • Trực tuyến";
                        }
                    }
                    else if (AuthService.CurrentUser.VaiTro == "Admin")
                    {
                        lblCurrentUserRole.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129)); // Green
                        if (lblSystemStatus != null)
                        {
                            lblSystemStatus.Text = "Chế độ Quản trị viên Toàn hệ thống (Admin) • Trực tuyến";
                        }
                    }
                }
            }
            else
            {
                if (lblCurrentUserName != null) lblCurrentUserName.Text = "Chế độ Khách / Kiosk";
                if (lblCurrentUserRole != null) lblCurrentUserRole.Text = "Chế độ trải nghiệm";
            }

            // Đăng ký nhận cảnh báo an toàn thời gian thực
            _safetyMonitorService.OnEmergencyAlertTriggered += SafetyMonitorService_OnEmergencyAlertTriggered;
            _safetyMonitorService.StartMonitoring(2000);

            Loaded += (s, e) =>
            {
                if (openInKioskMode)
                {
                    OpenKioskMode();
                }
                else
                {
                    mainContentArea.Content = _dashboardView;
                }
            };

            Closing += (s, e) =>
            {
                _safetyMonitorService.OnEmergencyAlertTriggered -= SafetyMonitorService_OnEmergencyAlertTriggered;
                _safetyMonitorService.StopMonitoring();
            };
        }

        private void SafetyMonitorService_OnEmergencyAlertTriggered(CanhBaoIoT alert)
        {
            if (alert == null) return;
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

            try
            {
                Dispatcher.Invoke(() =>
                {
                    _dashboardView.RefreshData();
                    _alertsView.LoadAlerts();

                    MessageBox.Show(
                        $"🚨 CẢNH BÁO AN TOÀN IOT KHẨN CẤP!\n\n" +
                        $"• Trạm: {alert.MaTram} - Ngăn tủ #{alert.MaNgan}\n" +
                        $"• Pin: {alert.MaPin}\n" +
                        $"• Nhiệt độ đo được: {alert.GiaTriDo:F1} °C (> 55°C)\n" +
                        $"• Tình trạng: Hệ thống đã TỰ ĐỘNG NGẮT SẠC và KHÓA CHẶT TỦ để chống cháy nổ!",
                        "BÁO ĐỘNG CHÁY NỔ",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                });
            }
            catch { }
        }

        #region Kiosk Workflow Management

        public void OpenKioskMode()
        {
            var welcomeView = new KioskWelcomeView();
            welcomeView.OnCustomerValidated += WelcomeView_OnCustomerValidated;
            mainContentArea.Content = welcomeView;
        }

        private void WelcomeView_OnCustomerValidated(KhachHang customer)
        {
            var swappingView = new KioskSwappingView(customer);
            swappingView.OnSwapCompleted += SwappingView_OnSwapCompleted;
            swappingView.OnCancelled += () => OpenKioskMode();
            mainContentArea.Content = swappingView;
        }

        private void SwappingView_OnSwapCompleted(SwapResult result)
        {
            var successView = new KioskSuccessView(result);
            successView.OnFinished += () => OpenKioskMode();
            mainContentArea.Content = successView;
        }

        #endregion

        #region Navigation Sidebar

        private void BtnNavKiosk_Click(object sender, RoutedEventArgs e)
        {
            OpenKioskMode();
        }

        private void BtnNavDashboard_Click(object sender, RoutedEventArgs e)
        {
            if (AuthService.CurrentUser?.VaiTro is not ("Admin" or "KyThuatVien")) return;
            _dashboardView.RefreshData();
            mainContentArea.Content = _dashboardView;
        }

        private void BtnNavBatteries_Click(object sender, RoutedEventArgs e)
        {
            if (AuthService.CurrentUser?.VaiTro is not ("Admin" or "KyThuatVien")) return;
            _batteryView.LoadBatteries();
            mainContentArea.Content = _batteryView;
        }

        private void BtnNavCustomers_Click(object sender, RoutedEventArgs e)
        {
            if (AuthService.CurrentUser?.VaiTro is not ("Admin" or "KyThuatVien")) return;
            _customerView.LoadCustomers();
            mainContentArea.Content = _customerView;
        }

        private void BtnNavTransactions_Click(object sender, RoutedEventArgs e)
        {
            if (AuthService.CurrentUser?.VaiTro is not ("Admin" or "KyThuatVien")) return;
            _transactionView.LoadData();
            mainContentArea.Content = _transactionView;
        }

        private void BtnNavAlerts_Click(object sender, RoutedEventArgs e)
        {
            if (AuthService.CurrentUser?.VaiTro is not ("Admin" or "KyThuatVien")) return;
            _alertsView.LoadAlerts();
            mainContentArea.Content = _alertsView;
        }

        private void BtnEmergencyCutoff_Click(object sender, RoutedEventArgs e)
        {
            if (AuthService.CurrentUser?.VaiTro is not ("Admin" or "KyThuatVien")) return;
            if (MessageBox.Show("BẠN CÓ CHẮC CHẮN MUỐN KÍCH HOẠT LỆNH NGẮT SẠC TOÀN BỘ TRẠM?\n\nToàn bộ các khay sạc sẽ dừng dòng điện và khóa chốt bảo vệ khẩn cấp.", 
                                "CẢNH BÁO AN TOÀN", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try { _safetyMonitorService.EmergencyCutoffStation(_dashboardView.CurrentStationId); }
                catch (System.Exception ex) { MessageBox.Show(ex.Message, "Ngắt sạc thất bại"); return; }
                MessageBox.Show("Lệnh ngắt sạc toàn trạm đã được phát đi thành công!", "Hệ thống bảo vệ", MessageBoxButton.OK, MessageBoxImage.Information);
                _dashboardView.RefreshData();
            }
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Bạn có muốn đăng xuất khỏi hệ thống?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                new AuthService().Logout();
                var login = new Views.LoginWindow();
                login.Show();
                this.Close();
            }
        }

        #endregion
    }
}
