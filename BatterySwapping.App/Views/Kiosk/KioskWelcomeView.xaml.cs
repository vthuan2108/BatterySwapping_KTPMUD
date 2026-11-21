using System;
using System.Windows;
using System.Windows.Controls;
using BatterySwapping.App.Models;
using BatterySwapping.App.Services;

namespace BatterySwapping.App.Views.Kiosk
{
    /// <summary>
    /// Interaction logic for KioskWelcomeView.xaml
    /// </summary>
    public partial class KioskWelcomeView : UserControl
    {
        private readonly SwapService _swapService;
        public event Action<KhachHang>? OnCustomerValidated;

        public KioskWelcomeView()
        {
            InitializeComponent();
            _swapService = new SwapService();
        }

        private void BtnStartSwap_Click(object sender, RoutedEventArgs e)
        {
            if (lblKioskError != null) lblKioskError.Visibility = Visibility.Collapsed;
            string input = txtPhoneOrQr?.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                if (lblKioskError != null)
                {
                    lblKioskError.Text = "Vui lòng nhập số điện thoại hoặc chọn tài xế demo!";
                    lblKioskError.Visibility = Visibility.Visible;
                }
                return;
            }

            if (_swapService.ValidateCustomerWallet(input, out var customer, out string error))
            {
                if (customer != null)
                {
                    OnCustomerValidated?.Invoke(customer);
                }
            }
            else
            {
                if (lblKioskError != null)
                {
                    lblKioskError.Text = error;
                    lblKioskError.Visibility = Visibility.Visible;
                }
            }
        }

        private void BtnQuickDriver_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string phone)
            {
                if (txtPhoneOrQr != null) txtPhoneOrQr.Text = phone;
                BtnStartSwap_Click(sender, e);
            }
        }
    }
}
