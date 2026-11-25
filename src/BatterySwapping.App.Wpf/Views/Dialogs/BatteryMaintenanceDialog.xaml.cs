using BatterySwapping.Contracts;
﻿using System;

using System.Windows;
using System.Windows.Media;

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


            Loaded += BatteryMaintenanceDialog_Loaded;
        }

        private async Task DetectSlotForBattery()
        {
            foreach(var station in await new StationService().GetAllStations())
            {
                var slot=(await new StationService().GetCabinetSlots(station.MaTram)).Find(n=>n.MaPinHienTai==_pin.MaPin);
                if(slot!=null){_stationId=station.MaTram;_slotNumber=slot.SoThuTu;return;}
            }
            throw new InvalidOperationException("Pin chưa nằm trong ngăn tủ; không thể bảo trì tại trạm.");
        }

        private async void BatteryMaintenanceDialog_Loaded(object sender, RoutedEventArgs e)
        {
            var sourceButton=sender as System.Windows.Controls.Button;
            if(sourceButton!=null)sourceButton.IsEnabled=false;
            try {

            await DetectSlotForBattery();
            lblMaPin.Text = $"{_pin.MaPin} (Khay #{_slotNumber} - {_stationId})";
            lblLoaiPin.Text = _pin.LoaiPin;
            lblSoC.Text = $"{_pin.SoC:F1}%";
            lblSoH.Text = $"{_pin.SoH:F1}%";
            lblNhietDo.Text = $"{_pin.NhietDo:F1} °C";
            lblSoLanSac.Text = $"{_pin.SoLanSac} chu kỳ";
            lblDienApDongDien.Text = $"{_pin.DienAp:F1} V • {_pin.DongDien:F1} A";
            lblTrangThai.Text = _pin.TrangThai;

            // Đánh giá chẩn đoán chi tiết từ BatteryService và lịch sử quá nhiệt tích lũy
            var inspection = await _batteryService.InspectBattery(_pin.MaPin);
            var thermalState = await _batteryService.GetThermalStateSafe(_pin.MaPin);
            bool hasChronicOverheat = thermalState != null && thermalState.TotalIncidents >= 3;

            if (hasChronicOverheat)
            {
                borderChronicOverheat.Visibility = Visibility.Visible;
                lblChronicOverheatTitle.Text = $"⚠️ CẢNH BÁO TÁI DIỄN QUÁ NHIỆT ({thermalState!.TotalIncidents} LẦN TRONG LỊCH SỬ)";
                string resetInfo = thermalState.LastAdminResetAt.HasValue
                    ? $", Admin reset gần nhất: {thermalState.LastAdminResetAt.Value.ToLocalTime():dd/MM/yyyy HH:mm}"
                    : "";
                lblChronicOverheatDesc.Text =
                    $"Pin {_pin.MaPin} đã ghi nhận tổng cộng {thermalState.TotalIncidents} lần quá nhiệt (đợt hiện tại: {thermalState.IncidentCount} lần{resetInfo}). " +
                    "Ngay cả khi đã được Admin reset, khuyến nghị kiểm định chuyên sâu hoặc thay thế pin khỏi trạm bằng nghiệp vụ REPLACE_BATTERY (nút \"Thay bằng pin kho\").";
            }
            else
            {
                borderChronicOverheat.Visibility = Visibility.Collapsed;
            }

            if (inspection != null)
            {
                lblDiagnosticDesc.Text = $"{inspection.HealthStatusDescription}\nKhuyến nghị: {inspection.RecommendedAction}";

                if (inspection.IsOverheated)
                {
                    borderDiagnostic.Background = new SolidColorBrush(Color.FromRgb(254, 242, 242));
                    lblDiagnosticTitle.Text = "🚨 BÁO ĐỘNG SỰ CỐ: PIN QUÁ NHIỆT KHẨN CẤP (> 55°C)";
                    lblDiagnosticTitle.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                    lblNhietDo.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                    if (hasChronicOverheat)
                    {
                        lblDiagnosticDesc.Text += $"\n⚠️ Đã tích lũy {thermalState!.TotalIncidents} lần quá nhiệt — khuyến nghị kiểm định/thay thế bằng REPLACE_BATTERY.";
                    }
                }
                else if (inspection.IsDegraded)
                {
                    borderDiagnostic.Background = new SolidColorBrush(Color.FromRgb(254, 243, 199));
                    lblDiagnosticTitle.Text = "⚠️ CẢNH BÁO KỸ THUẬT: PIN CHAI (SoH < 80%)";
                    lblDiagnosticTitle.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
                    lblSoH.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
                    if (hasChronicOverheat)
                    {
                        lblDiagnosticDesc.Text += $"\n⚠️ Đồng thời pin có {thermalState!.TotalIncidents} lần quá nhiệt — ưu tiên thay thế qua REPLACE_BATTERY.";
                    }
                }
                else if (hasChronicOverheat)
                {
                    borderDiagnostic.Background = new SolidColorBrush(Color.FromRgb(254, 243, 199));
                    lblDiagnosticTitle.Text = $"⚠️ CẢNH BÁO KỸ THUẬT: PIN TÁI DIỄN QUÁ NHIỆT ({thermalState!.TotalIncidents} LẦN)";
                    lblDiagnosticTitle.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
                    lblDiagnosticDesc.Text = $"{inspection.HealthStatusDescription}\nLịch sử tích lũy {thermalState.TotalIncidents} lần quá nhiệt (kể cả sau khi Admin reset đưa bộ đếm đợt về 0). Khuyến nghị kiểm định kỹ thuật hoặc thay pin qua REPLACE_BATTERY.";
                }
                else
                {
                    borderDiagnostic.Background = new SolidColorBrush(Color.FromRgb(240, 253, 244));
                    lblDiagnosticTitle.Text = "✅ ĐÁNH GIÁ BMS: PIN HOẠT ĐỘNG BÌNH THƯỜNG VÀ AN TOÀN";
                    lblDiagnosticTitle.Foreground = new SolidColorBrush(Color.FromRgb(21, 128, 61));
                }
            }
            else if (hasChronicOverheat)
            {
                borderDiagnostic.Background = new SolidColorBrush(Color.FromRgb(254, 243, 199));
                lblDiagnosticTitle.Text = $"⚠️ CẢNH BÁO KỸ THUẬT: PIN TÁI DIỄN QUÁ NHIỆT ({thermalState!.TotalIncidents} LẦN)";
                lblDiagnosticTitle.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
                lblDiagnosticDesc.Text = $"Lịch sử tích lũy {thermalState.TotalIncidents} lần quá nhiệt. Khuyến nghị kiểm định kỹ thuật hoặc thay pin qua REPLACE_BATTERY.";
            }

            // Gợi ý mã pin mới
            txtNewPinId.ItemsSource=(await BssApi.Client.Get<List<BatteryInventoryItem>>("inventory")).Where(p=>p.Location=="Warehouse"&&!p.Reserved).ToList();

            } catch(Exception ex){BssApi.Error(ex);}
            finally {if(sourceButton!=null)sourceButton.IsEnabled=true;}
        }

        private async void BtnMaintain_Click(object sender, RoutedEventArgs e)
        {
            var sourceButton=sender as System.Windows.Controls.Button;
            if(sourceButton!=null)sourceButton.IsEnabled=false;
            try {

            string technicianName = AuthService.CurrentUser?.HoTen ?? "Kỹ thuật viên Trạm";
            string note = txtNote?.Text?.Trim() ?? "Bảo dưỡng kỹ thuật BMS hoàn tất";

            var outcome = await _batteryService.MaintainBattery(_stationId, _slotNumber, _pin.MaPin, technicianName, note);
            if (outcome.Success)
            {
                MessageBox.Show($"Đã hoàn tất bảo dưỡng pin {_pin.MaPin} tại trạm {_stationId} (Ngăn #{_slotNumber})!\n" +
                                $"• Thiết bị đã xác nhận xử lý; xem telemetry mới nhất\n" +
                                $"• Ngăn tủ đã được mở khóa bảo trì\n" +
                                $"• Cảnh báo an toàn đã được giải quyết",
                                "Bảo dưỡng thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show($"Bảo dưỡng thất bại:\n{outcome.Error}", "Lỗi bảo dưỡng", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            } catch(Exception ex){BssApi.Error(ex);}
            finally {if(sourceButton!=null)sourceButton.IsEnabled=true;}
        }

        private async void BtnReplace_Click(object sender, RoutedEventArgs e)
        {
            var sourceButton=sender as System.Windows.Controls.Button;
            if(sourceButton!=null)sourceButton.IsEnabled=false;
            try {

            string newPinId = (txtNewPinId.SelectedItem as BatteryInventoryItem)?.BatteryId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(newPinId))
            {
                MessageBox.Show("Vui lòng nhập mã pin mới thay thế!", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string technicianName = AuthService.CurrentUser?.HoTen ?? "Kỹ thuật viên Trạm";
            string note = txtNote?.Text?.Trim() ?? "Thay thế bằng pin kho đã đăng ký";

            if (MessageBox.Show($"XÁC NHẬN THAY THẾ PIN:\n\n" +
                                $"• Thu hồi viên pin cũ: {_pin.MaPin}\n" +
                                $"• Lắp viên pin mới: {newPinId} (thông số lấy từ pin đã đăng ký)\n" +
                                $"• Vị trí: Trạm {_stationId} - Ngăn #{_slotNumber}\n\n" +
                                $"Bạn có chắc chắn muốn thực hiện nghiệp vụ này?",
                                "Xác nhận thay thế pin", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                var outcome = await _batteryService.ReplaceBattery(_stationId, _slotNumber, _pin.MaPin, newPinId, technicianName, note);
            if (outcome.Success)
                {
                    MessageBox.Show($"Đã thay thế thành công viên pin {newPinId} vào khay #{_slotNumber} trạm {_stationId}!",
                                    "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show($"Thay thế pin thất bại:\n{outcome.Error}", "Lỗi thay thế", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            } catch(Exception ex){BssApi.Error(ex);}
            finally {if(sourceButton!=null)sourceButton.IsEnabled=true;}
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
