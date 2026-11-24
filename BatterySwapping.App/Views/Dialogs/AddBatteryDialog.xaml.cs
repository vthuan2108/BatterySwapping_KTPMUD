using System;
using System.Windows;
using BatterySwapping.App.Models;
using BatterySwapping.App.Services;

namespace BatterySwapping.App.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for AddBatteryDialog.xaml
    /// </summary>
    public partial class AddBatteryDialog : Window
    {
        private readonly BatteryService _batteryService;

        public AddBatteryDialog()
        {
            InitializeComponent();
            _batteryService = new BatteryService();
        }

        private bool _saving;
        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var sourceButton=sender as System.Windows.Controls.Button;
            if(sourceButton!=null)sourceButton.IsEnabled=false;
            try {

            if(_saving)return;_saving=true;
            try {

            string maPin = txtMaPin?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(maPin))
            {
                MessageBox.Show("Vui lòng nhập mã pin!", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!decimal.TryParse(txtSoC?.Text, out var soc) || soc < 0 || soc > 100)
            {
                MessageBox.Show("Dung lượng SoC phải từ 0 đến 100%!", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!decimal.TryParse(txtSoH?.Text, out var soh) || soh < 0 || soh > 100)
            {
                MessageBox.Show("Sức khỏe SoH phải từ 0 đến 100%!", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var pin = new Pin
            {
                MaPin = maPin,
                LoaiPin = txtLoaiPin?.Text?.Trim() ?? "Lithium LFP 72V-30Ah",
                DungLuongThietKe = 30,
                SoC = soc,
                SoH = soh,
                NhietDo = decimal.TryParse(txtNhietDo?.Text, out var temp) ? temp : 25m,
                DienAp = decimal.TryParse(txtDienAp?.Text, out var v) ? v : 72m,
                DongDien = 0,
                SoLanSac = 0,
                TrangThai = "DaDay"
            };

            var outcome = await _batteryService.AddBattery(pin);
            if (outcome.Success)
            {
                MessageBox.Show("Thêm pin mới thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show($"Lỗi thêm pin: {outcome.Error}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
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
