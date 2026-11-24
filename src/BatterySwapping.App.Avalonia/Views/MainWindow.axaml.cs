using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using BatterySwapping.App.Avalonia.Models;
using BatterySwapping.App.Avalonia.Services;
using BatterySwapping.App.Avalonia.Views.Admin;
using BatterySwapping.App.Avalonia.Views.Kiosk;

namespace BatterySwapping.App.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly OverviewView _overviewView = new();
    private readonly StationDashboardView _dashboardView = new();
    private readonly BatteryManagementView _batteryView = new();
    private readonly CustomerManagementView _customerView = new();
    private readonly TransactionHistoryView _transactionView = new();
    private readonly SafetyAlertsView _alertsView = new();
    private readonly CustomerDashboardView _walletView = new();

    private readonly SafetyMonitorService _safetyMonitor = new();
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow() : this(openInKioskMode: false)
    {
    }

    public MainWindow(bool openInKioskMode = false)
    {
        InitializeComponent();

        // Cấu hình đồng hồ thời gian thực
        _clockTimer.Tick += (_, _) =>
        {
            if (lblClock != null) lblClock.Text = DateTime.Now.ToString("HH:mm:ss");
        };
        _clockTimer.Start();

        // Kết nối điều hướng khi nhấp chọn trạm từ Tổng quan
        _overviewView.StationSelected += (stationId) =>
        {
            mainContentArea.Content = _dashboardView;
            _dashboardView.SelectStation(stationId);
        };

        // Kết nối sự kiện đổi pin từ Kiosk
        _walletView.StartSwap += () => mainContentArea.Content=new SwapWorkflowView();

        // Lắng nghe sự cố an toàn khẩn cấp thời gian thực (Non-blocking In-App Alert Banner)
        _safetyMonitor.OnEmergencyAlertTriggered += OnEmergencySafetyAlertTriggered;

        // Phân quyền giao diện theo Role
        var role = AuthService.CurrentUser?.VaiTro;
        var policy = NavigationPolicy.For(role);

        SetupUserBadge(role);
        ApplyNavigationPolicy(policy, openInKioskMode);

        Closed += (_, _) =>
        {
            _clockTimer.Stop();
            _safetyMonitor.StopMonitoring();
            _safetyMonitor.OnEmergencyAlertTriggered -= OnEmergencySafetyAlertTriggered;
        };
    }

    private void SetupUserBadge(string? role)
    {
        if (AuthService.CurrentUser != null)
        {
            if (lblCurrentUserName != null) lblCurrentUserName.Text = AuthService.CurrentUser.HoTen;
            if (lblCurrentUserRole != null) lblCurrentUserRole.Text = $"Vai trò: {role}";

            if (role == "KyThuatVien")
            {
                if (pnlRolePill != null) pnlRolePill.Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x4A, 0x6E));
                if (lblCurrentUserRole != null) lblCurrentUserRole.Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
                if (lblRoleInfo != null) lblRoleInfo.Text = "Chế độ Kỹ thuật viên (Bảo trì & BMS)";
            }
            else if (role == "Admin")
            {
                if (pnlRolePill != null) pnlRolePill.Background = new SolidColorBrush(Color.FromRgb(0x06, 0x4E, 0x3B));
                if (lblCurrentUserRole != null) lblCurrentUserRole.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
                if (lblRoleInfo != null) lblRoleInfo.Text = "Chế độ Quản trị viên (Admin)";
            }
            else
            {
                if (pnlRolePill != null) pnlRolePill.Background = new SolidColorBrush(Color.FromRgb(0x31, 0x2E, 0x81));
                if (lblCurrentUserRole != null) lblCurrentUserRole.Foreground = new SolidColorBrush(Color.FromRgb(0x81, 0x8C, 0xF8));
                if (lblRoleInfo != null) lblRoleInfo.Text = "Chế độ Khách hàng / Kiosk";
            }
        }
        else
        {
            if (lblCurrentUserName != null) lblCurrentUserName.Text = "Chế độ Trải nghiệm";
            if (lblCurrentUserRole != null) lblCurrentUserRole.Text = "Khách vãng lai";
        }
    }

    private void ApplyNavigationPolicy(NavigationPolicy policy, bool openInKioskMode)
    {
        bool staff = policy.Staff && !openInKioskMode;

        btnNavOverview.IsVisible = staff && policy.WalletAdministration;
        btnNavDashboard.IsVisible = staff;
        btnNavBatteries.IsVisible = staff;
        btnNavCustomers.IsVisible = staff && policy.WalletAdministration;btnNavBusiness.IsVisible=staff&&policy.WalletAdministration;
        btnNavTransactions.IsVisible = staff || policy.Kiosk;
        btnNavAlerts.IsVisible = staff;
        btnNavWallet.IsVisible = policy.Kiosk || !staff;

        if (staff)
        {
            btnNavDashboard.Content="🏭  Vận hành trạm và BMS";btnNavBatteries.Content="🔋  Pin và bảo trì";btnNavTransactions.Content="📜  Phiên đổi pin và hỗ trợ";
            _safetyMonitor.StartMonitoring(2000);
            mainContentArea.Content = _dashboardView;
        }
        else
        {
            mainContentArea.Content = _walletView;
        }
    }

    private void OnEmergencySafetyAlertTriggered(CanhBaoIoT alert)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (pnlEmergencyBanner != null && txtEmergencyBannerMessage != null)
            {
                txtEmergencyBannerMessage.Text = $"🚨 PHÁT HIỆN SỰ CỐ: Trạm {alert.MaTram} - {alert.ViTriFormatted} - Pin {alert.MaPin ?? "N/A"}, giá trị {alert.GiaTriDoFormatted}. Kiểm tra loại cảnh báo và số liệu mới nhất.";
                pnlEmergencyBanner.IsVisible = true;
            }

            _dashboardView.RefreshData();
            _alertsView.LoadAlerts();
        });
    }

    private void BtnDismissAlert_Click(object? sender, RoutedEventArgs e)
    {
        if (pnlEmergencyBanner != null) pnlEmergencyBanner.IsVisible = false;
    }

    private void BtnViewEmergencyAlert_Click(object? sender, RoutedEventArgs e)
    {
        if (pnlEmergencyBanner != null) pnlEmergencyBanner.IsVisible = false;
        mainContentArea.Content = _alertsView;
        _alertsView.LoadAlerts();
    }

    private void BtnNavOverview_Click(object? sender, RoutedEventArgs e)
    {
        mainContentArea.Content = _overviewView;
        _ = _overviewView.ReloadData();
    }

    private void BtnNavDashboard_Click(object? sender, RoutedEventArgs e)
    {
        mainContentArea.Content = _dashboardView;
        _dashboardView.RefreshData();
    }

    private void BtnNavBatteries_Click(object? sender, RoutedEventArgs e)
    {
        mainContentArea.Content = _batteryView;
        _batteryView.LoadBatteries();
    }

    private void BtnNavBusiness_Click(object? sender,RoutedEventArgs e){if(AuthService.CurrentUser?.VaiTro=="Admin")mainContentArea.Content=new BusinessOperationsView();}
    private void BtnNavCustomers_Click(object? sender, RoutedEventArgs e)
    {
        mainContentArea.Content = _customerView;
        _customerView.LoadCustomers();
    }

    private void BtnNavTransactions_Click(object? sender, RoutedEventArgs e)
    {
        mainContentArea.Content = AuthService.CurrentUser?.VaiTro=="Admin"?_transactionView:new SwapWorkflowView();
        if(AuthService.CurrentUser?.VaiTro=="Admin")_transactionView.LoadData();
    }

    private void BtnNavAlerts_Click(object? sender, RoutedEventArgs e)
    {
        mainContentArea.Content = _alertsView;
        _alertsView.LoadAlerts();
    }

    private void BtnNavWallet_Click(object? sender, RoutedEventArgs e)
    {
        mainContentArea.Content = _walletView;
        _ = _walletView.Reload();
    }

    private async void BtnLogout_Click(object? sender, RoutedEventArgs e)
    {
        await new AuthService().Logout();
        var login = new LoginWindow();
        login.Show();
        Close();
    }
}
