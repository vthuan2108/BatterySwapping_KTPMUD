using System;
using System.Windows;
using System.Windows.Controls;
using BatterySwapping.App.Services;

namespace BatterySwapping.App.Views.Kiosk
{
    /// <summary>
    /// Interaction logic for KioskSuccessView.xaml
    /// </summary>
    public partial class KioskSuccessView : UserControl
    {
        public event Action? OnFinished;

        public KioskSuccessView(SwapResult? result)
        {
            InitializeComponent();

            if (result == null) return;

            if (lblTxId != null) lblTxId.Text = $"#BSS-GD-{result.TransactionId:D5}";
            if (lblTime != null) lblTime.Text = result.Timestamp.ToString("dd/MM/yyyy HH:mm:ss");
            if (lblOldBattery != null) lblOldBattery.Text = $"{result.OldBatteryId} ({result.OldBatterySoC:F1}%) • Khay #{result.SlotOldBattery}";
            if (lblNewBattery != null) lblNewBattery.Text = $"{result.NewBatteryId} ({result.NewBatterySoC:F1}%) • Khay #{result.SlotNewBattery}";
            if (lblFee != null) lblFee.Text = string.Format("{0:N0} đ", result.Fee);
            if (lblRemaining != null) lblRemaining.Text = string.Format("{0:N0} đ", result.RemainingBalance);
        }

        private void BtnFinish_Click(object sender, RoutedEventArgs e)
        {
            OnFinished?.Invoke();
        }

        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Lệnh in hóa đơn nhiệt đã được gửi tới máy in tại trạm!", "In biên lai", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
