using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BatterySwapping.App.Models;
using BatterySwapping.App.Services;
using BatterySwapping.App.Views.Dialogs;

namespace BatterySwapping.App.Views.Admin
{
    /// <summary>
    /// Interaction logic for BatteryManagementView.xaml
    /// </summary>
    public partial class BatteryManagementView : UserControl
    {
        private readonly BatteryService _batteryService;
        private List<Pin> _allBatteries = new();

        public BatteryManagementView()
        {
            InitializeComponent();
            _batteryService = new BatteryService();
            btnAddBattery.Visibility=NavigationPolicy.For(AuthService.CurrentUser?.VaiTro).WalletAdministration?Visibility.Visible:Visibility.Collapsed;
            Loaded += (s, e) => LoadBatteries();
        }

        public async void LoadBatteries()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(LoadBatteries);
                return;
            }
            try
            {
                _allBatteries = await _batteryService.GetAllBatteries() ?? new();
                FilterData();
            }
            catch (Exception ex)
            {
                BssApi.ReportError(ex);
                // Safe ignore
            }
        }

        private void FilterData()
        {
            if (dgBatteries == null || cboStatusFilter == null || txtSearch == null) return;

            string keyword = txtSearch.Text?.Trim().ToLower() ?? "";
            var query = (_allBatteries ?? new()).AsEnumerable();

            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(b => (b != null && b.MaPin != null && b.MaPin.ToLower().Contains(keyword)) ||
                                         (b != null && b.LoaiPin != null && b.LoaiPin.ToLower().Contains(keyword)));
            }

            if (cboStatusFilter.SelectedIndex > 0 && cboStatusFilter.SelectedItem is ComboBoxItem item)
            {
                string status = item.Content?.ToString() ?? "";
                if (status.Contains("Đã đầy")) query = query.Where(b => b != null && b.SoC >= 90.0m);
                else if (status.Contains("Đang sạc")) query = query.Where(b => b != null && b.TrangThai == "DangSac");
                else if (status.Contains("Khả dụng")) query = query.Where(b => b != null && b.TrangThai == "KhaDung");
                else if (status.Contains("Đang sử dụng")) query = query.Where(b => b != null && b.TrangThai == "DangSuDung");
                else if (status.Contains("Lỗi")) query = query.Where(b => b != null && b.TrangThai != null && b.TrangThai.Contains("Loi"));
            }

            dgBatteries.ItemsSource = query.ToList();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterData();
        }

        private void CboStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterData();
        }

        private void MaintenanceHistoryClick(object sender,RoutedEventArgs e)=>new MaintenanceHistoryWindow().Show();
        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadBatteries();
        }

        private void BtnAddBattery_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddBatteryDialog();
            if (dialog.ShowDialog() == true)
            {
                LoadBatteries();
            }
        }

        private void BtnMaintainSelected_Click(object sender, RoutedEventArgs e)
        {
            if (dgBatteries.SelectedItem is Pin selected)
            {
                OpenMaintenanceDialog(selected);
            }
            else
            {
                MessageBox.Show("Vui lòng chọn một viên pin từ danh sách để kiểm định / bảo trì!", "Chưa chọn pin", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnRowMaintain_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Pin pin)
            {
                OpenMaintenanceDialog(pin);
            }
        }

        private void DgBatteries_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (dgBatteries.SelectedItem is Pin selected)
            {
                OpenMaintenanceDialog(selected);
            }
        }

        private void OpenMaintenanceDialog(Pin pin)
        {
            var dialog = new BatteryMaintenanceDialog(pin);
            if (dialog.ShowDialog() == true)
            {
                LoadBatteries();
            }
        }
    }
}
