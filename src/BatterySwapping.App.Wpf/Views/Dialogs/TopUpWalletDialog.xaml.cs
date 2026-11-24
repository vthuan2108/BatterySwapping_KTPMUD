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

    string? paymentKey;string paymentBody="";
        private bool _saving;
        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var sourceButton=sender as System.Windows.Controls.Button;
            if(sourceButton!=null)sourceButton.IsEnabled=false;
            try {

            if(_saving)return;_saving=true;
            try {

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

            string body=customer.MaKH+":"+amount;if(body!=paymentBody){paymentKey=null;paymentBody=body;}paymentKey??=Guid.NewGuid().ToString();
            var outcome = await _reportService.TopUpWallet(customer.MaKH, amount, method,paymentKey);
            if (outcome.Success)
            {paymentKey=null;
                MessageBox.Show($"Nạp thành công {amount:N0} đ vào ví của {customer.HoTen}!",
                                "Giao dịch thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show($"Lỗi nạp tiền: {outcome.Error}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            } finally {_saving=false;}

            } catch(Exception ex){BssApi.Error(ex);}
            finally {if(sourceButton!=null)sourceButton.IsEnabled=true;}
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
