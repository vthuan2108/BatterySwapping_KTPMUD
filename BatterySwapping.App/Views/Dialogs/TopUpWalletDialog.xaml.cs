using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using BatterySwapping.App.Models;
using BatterySwapping.App.Services;

namespace BatterySwapping.App.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for TopUpWalletDialog.xaml
    /// </summary>
    public partial class TopUpWalletDialog : Window
    {
        private readonly ReportService _reportService;
        private readonly List<KhachHang> _customers;

        public TopUpWalletDialog(List<KhachHang> customers, KhachHang? preselected = null)
        {
            InitializeComponent();
            _reportService = new ReportService();
            _customers = customers ?? new List<KhachHang>();

            cboCustomers.ItemsSource = _customers;
            cboCustomers.DisplayMemberPath = "HoTen";
            cboCustomers.SelectedValuePath = "MaKH";

            if (preselected != null)
            {
                cboCustomers.SelectedItem = preselected;
            }
            else if (_customers.Count > 0)
            {
                cboCustomers.SelectedIndex = 0;
            }
        }

        private void BtnQuickAmount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string amount)
            {
                if (txtAmount != null) txtAmount.Text = amount;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (cboCustomers == null || cboCustomers.SelectedItem is not KhachHang customer)
            {
                MessageBox.Show("Vui lòng chọn khách hàng!", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string amountText = txtAmount?.Text?.Trim() ?? string.Empty;
            if (!decimal.TryParse(amountText, out decimal amount) || amount <= 0)
            {
                MessageBox.Show("Số tiền nạp không hợp lệ (phải lớn hơn 0 đ)!", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string method = (cboMethod?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ChuyenKhoan";

            if (_reportService.TopUpWallet(customer.MaKH, amount, method, out string error))
            {
                MessageBox.Show($"Nạp thành công {amount:N0} đ vào tài khoản khách hàng {customer.HoTen}!", 
                                "Giao dịch thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show($"Lỗi nạp tiền: {error}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
