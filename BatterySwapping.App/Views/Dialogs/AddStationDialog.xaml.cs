using System;
using System.Windows;
using BatterySwapping.App.Models;
using BatterySwapping.App.Services;

namespace BatterySwapping.App.Views.Dialogs
{
    /// <summary>
    /// Cửa sổ tương tác tạo mới trạm đổi pin vào mạng lưới hệ thống.
    /// </summary>
    public partial class AddStationDialog : Window
    {
        private readonly StationService _stationService;

        public AddStationDialog()
        {
            InitializeComponent();
            _stationService = new StationService();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string maTram = txtMaTram?.Text?.Trim() ?? string.Empty;
            string tenTram = txtTenTram?.Text?.Trim() ?? string.Empty;
            string diaChi = txtDiaChi?.Text?.Trim() ?? string.Empty;
            string? toaDoGPS = txtToaDoGPS?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(toaDoGPS)) toaDoGPS = null;

            if (string.IsNullOrWhiteSpace(maTram) || string.IsNullOrWhiteSpace(tenTram) || string.IsNullOrWhiteSpace(diaChi))
            {
                MessageBox.Show("Vui lòng điền đầy đủ Mã trạm, Tên trạm và Địa chỉ!", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtSoLuongNgan?.Text?.Trim(), out int soLuongNgan) || soLuongNgan < 1 || soLuongNgan > 32)
            {
                MessageBox.Show("Số lượng ngăn tủ phải là số nguyên từ 1 đến 32 (mặc định là 8)!", "Thông số không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string trangThai = (cboTrangThai?.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "HoatDong";

            var tram = new TramPin
            {
                MaTram = maTram,
                TenTram = tenTram,
                DiaChi = diaChi,
                ToaDoGPS = toaDoGPS,
                SoLuongNgan = soLuongNgan,
                TrangThaiHoatDong = trangThai,
                NgayLapDat = DateTime.Now
            };

            if (_stationService.AddStation(tram, out string errorMessage))
            {
                MessageBox.Show($"Đã tạo mới thành công trạm '{tenTram}' ({maTram}) kèm {soLuongNgan} ngăn tủ!", 
                                "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show($"Không thể tạo trạm mới:\n{errorMessage}", "Lỗi tạo trạm", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
