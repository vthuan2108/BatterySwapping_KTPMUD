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
    /// Interaction logic for CustomerManagementView.xaml
    /// </summary>
    public partial class CustomerManagementView : UserControl
    {
        private readonly ReportService _reportService;
        private List<KhachHang> _allCustomers = new();

        public CustomerManagementView()
        {
            InitializeComponent();
            _reportService = new ReportService();
            Loaded += (s, e) => LoadCustomers();
        }

        public async void LoadCustomers()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(LoadCustomers);
                return;
            }
            try
            {
                _allCustomers = await _reportService.GetAllCustomers() ?? new();
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
            if (dgCustomers == null || txtSearch == null) return;

            string keyword = txtSearch.Text?.Trim().ToLower() ?? "";
            var query = (_allCustomers ?? new()).AsEnumerable();

            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(c => (c != null && c.HoTen != null && c.HoTen.ToLower().Contains(keyword)) ||
                                         (c != null && c.SoDienThoai != null && c.SoDienThoai.Contains(keyword)) ||
                                         (c != null && c.BienSoXe != null && c.BienSoXe.ToLower().Contains(keyword)));
            }

            dgCustomers.ItemsSource = query.ToList();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterData();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadCustomers();
        }

        private void BtnTopUp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TopUpWalletDialog(_allCustomers);
            if (dialog.ShowDialog() == true)
            {
                LoadCustomers();
            }
        }

        private void BtnRowTopUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is KhachHang customer)
            {
                var dialog = new TopUpWalletDialog(_allCustomers, customer);
                if (dialog.ShowDialog() == true)
                {
                    LoadCustomers();
                }
            }
        }
    }
}
