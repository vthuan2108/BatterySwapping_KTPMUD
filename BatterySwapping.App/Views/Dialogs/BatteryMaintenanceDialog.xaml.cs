using System;
using System.Data;
using System.Windows;
using System.Windows.Media;
using Microsoft.Data.SqlClient;
using BatterySwapping.App.Helpers;
using BatterySwapping.App.Models;
using BatterySwapping.App.Services;

namespace BatterySwapping.App.Views.Dialogs
{
    /// <summary>
    /// Cửa sổ nghiệp vụ dành cho Kỹ thuật viên (KyThuatVien) và Admin:
    /// Kiểm định thông số BMS, bảo dưỡng làm mát, reset sự cố quá nhiệt hoặc thay thế pin chai/hỏng.
    /// </summary>
    public partial class BatteryMaintenanceDialog : Window
    {
        private readonly BatteryService _batteryService;
        private readonly Pin _pin;
        private string _stationId = "TRAM-BK01";
        private int _slotNumber = 1;

        public BatteryMaintenanceDialog(Pin pin, string? stationId = null, int? slotNumber = null)
        {
            InitializeComponent();
            _batteryService = new BatteryService();
            _pin = pin ?? throw new ArgumentNullException(nameof(pin));

            if (!string.IsNullOrEmpty(stationId)) _stationId = stationId;
            if (slotNumber.HasValue && slotNumber.Value > 0) _slotNumber = slotNumber.Value;
            else DetectSlotForBattery();

            Loaded += BatteryMaintenanceDialog_Loaded;
        }

        private void DetectSlotForBattery()
        {
            try
            {
                string sql = "SELECT TOP 1 MaTram, SoThuTu FROM dbo.NganTu WHERE MaPinHienTai = @MaPin";
                var param = new SqlParameter("@MaPin", SqlDbType.VarChar, 20) { Value = _pin.MaPin };
                DataTable dt = DatabaseHelper.ExecuteDataTable(sql, param);
                if (dt.Rows.Count > 0)
                {
                    _stationId = dt.Rows[0]["MaTram"].ToString() ?? "TRAM-BK01";
                    _slotNumber = Convert.ToInt32(dt.Rows[0]["SoThuTu"]);
                }
            }
            catch { }
        }

        private void BatteryMaintenanceDialog_Loaded(object sender, RoutedEventArgs e)
        {
            lblMaPin.Text = $"{_pin.MaPin} (Khay #{_slotNumber} - {_stationId})";
            lblLoaiPin.Text = _pin.LoaiPin;
            lblSoC.Text = $"{_pin.SoC:F1}%";
            lblSoH.Text = $"{_pin.SoH:F1}%";
            lblNhietDo.Text = $"{_pin.NhietDo:F1} °C";
            lblSoLanSac.Text = $"{_pin.SoLanSac} chu kỳ";
            lblDienApDongDien.Text = $"{_pin.DienAp:F1} V • {_pin.DongDien:F1} A";
            lblTrangThai.Text = _pin.TrangThai;

            // Đánh giá chẩn đoán chi tiết từ BatteryService
            var inspection = _batteryService.InspectBattery(_pin.MaPin);
            if (inspection != null)
            {
                lblDiagnosticDesc.Text = $"{inspection.HealthStatusDescription}\nKhuyến nghị: {inspection.RecommendedAction}";

                if (inspection.IsOverheated)
                {
                    borderDiagnostic.Background = new SolidColorBrush(Color.FromRgb(254, 242, 242));
                    lblDiagnosticTitle.Text = "🚨 BÁO ĐỘNG SỰ CỐ: PIN QUÁ NHIỆT KHẨN CẤP (> 55°C)";
                    lblDiagnosticTitle.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                    lblNhietDo.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                }
                else if (inspection.IsDegraded)
                {
                    borderDiagnostic.Background = new SolidColorBrush(Color.FromRgb(254, 243, 199));
                    lblDiagnosticTitle.Text = "⚠️ CẢNH BÁO KỸ THUẬT: PIN CHAI (SoH < 80%)";
                    lblDiagnosticTitle.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
                    lblSoH.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
                }
                else
                {
                    borderDiagnostic.Background = new SolidColorBrush(Color.FromRgb(240, 253, 244));
                    lblDiagnosticTitle.Text = "✅ ĐÁNH GIÁ BMS: PIN HOẠT ĐỘNG BÌNH THƯỜNG VÀ AN TOÀN";
                    lblDiagnosticTitle.Foreground = new SolidColorBrush(Color.FromRgb(21, 128, 61));
                }
            }

            // Gợi ý mã pin mới
            txtNewPinId.Text = $"PIN-0{DateTime.Now:ss}";
        }

        private void BtnMaintain_Click(object sender, RoutedEventArgs e)
        {
            string technicianName = AuthService.CurrentUser?.HoTen ?? "Kỹ thuật viên Trạm";
            string note = txtNote?.Text?.Trim() ?? "Bảo dưỡng kỹ thuật BMS hoàn tất";

            if (_batteryService.MaintainBattery(_stationId, _slotNumber, _pin.MaPin, technicianName, note, out string errorMessage))
            {
                MessageBox.Show($"Đã hoàn tất bảo dưỡng pin {_pin.MaPin} tại trạm {_stationId} (Ngăn #{_slotNumber})!\n" +
                                $"• Nhiệt độ đã hạ về 25°C an toàn\n" +
                                $"• Ngăn tủ đã được mở khóa bảo trì\n" +
                                $"• Cảnh báo an toàn đã được giải quyết",
                                "Bảo dưỡng thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show($"Bảo dưỡng thất bại:\n{errorMessage}", "Lỗi bảo dưỡng", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnReplace_Click(object sender, RoutedEventArgs e)
        {
            string newPinId = txtNewPinId?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(newPinId))
            {
                MessageBox.Show("Vui lòng nhập mã pin mới thay thế!", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string technicianName = AuthService.CurrentUser?.HoTen ?? "Kỹ thuật viên Trạm";
            string note = txtNote?.Text?.Trim() ?? "Thay thế pin chai/hỏng bằng pin mới đạt chuẩn 100%";

            if (MessageBox.Show($"XÁC NHẬN THAY THẾ PIN:\n\n" +
                                $"• Thu hồi viên pin cũ: {_pin.MaPin}\n" +
                                $"• Lắp viên pin mới: {newPinId} (SoC: 100%, SoH: 100%)\n" +
                                $"• Vị trí: Trạm {_stationId} - Ngăn #{_slotNumber}\n\n" +
                                $"Bạn có chắc chắn muốn thực hiện nghiệp vụ này?",
                                "Xác nhận thay thế pin", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                if (_batteryService.ReplaceBattery(_stationId, _slotNumber, _pin.MaPin, newPinId, technicianName, note, out string errorMessage))
                {
                    MessageBox.Show($"Đã thay thế thành công viên pin {newPinId} vào khay #{_slotNumber} trạm {_stationId}!",
                                    "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show($"Thay thế pin thất bại:\n{errorMessage}", "Lỗi thay thế", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
