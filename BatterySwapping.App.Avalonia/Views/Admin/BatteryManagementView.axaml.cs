using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using BatterySwapping.App.Avalonia.Models;
using BatterySwapping.App.Avalonia.Services;

namespace BatterySwapping.App.Avalonia.Views.Admin;

public partial class BatteryManagementView : UserControl
{
    private readonly BatteryService _batteryService = new();
    private readonly StationService _stationService = new();
    private List<Pin> _allBatteries = new();
    private Pin? _selectedPin;

    public BatteryManagementView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) => LoadBatteries();
    }

    public async void LoadBatteries()
    {
        try
        {
            _allBatteries = await _batteryService.GetAllBatteries() ?? new();
            FilterData();
        }
        catch (Exception ex)
        {
            BssApi.ReportError(ex);
        }
    }

    private void FilterData()
    {
        if (dgBatteries == null) return;

        string keyword = txtSearch?.Text?.Trim().ToLower() ?? "";
        var query = (_allBatteries ?? new()).AsEnumerable();

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(b => (b.MaPin != null && b.MaPin.ToLower().Contains(keyword)) ||
                                     (b.LoaiPin != null && b.LoaiPin.ToLower().Contains(keyword)));
        }

        if (cboStatusFilter != null && cboStatusFilter.SelectedIndex > 0 && cboStatusFilter.SelectedItem is ComboBoxItem item)
        {
            string status = item.Content?.ToString() ?? "";
            if (status.Contains("Đã đầy")) query = query.Where(b => b.SoC >= 90.0m);
            else if (status.Contains("Đang sạc")) query = query.Where(b => b.TrangThai == "DangSac");
            else if (status.Contains("Khả dụng")) query = query.Where(b => b.TrangThai == "KhaDung");
            else if (status.Contains("Đang sử dụng")) query = query.Where(b => b.TrangThai == "DangSuDung");
            else if (status.Contains("Lỗi")) query = query.Where(b => b.TrangThai != null && b.TrangThai.Contains("Loi"));
        }

        dgBatteries.ItemsSource = query.ToList();
    }

    private void TxtSearch_KeyUp(object? sender, KeyEventArgs e)
    {
        FilterData();
    }

    private void CboStatusFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        FilterData();
    }

    private void BtnRefresh_Click(object? sender, RoutedEventArgs e)
    {
        LoadBatteries();
    }

    private void DgBatteries_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (dgBatteries.SelectedItem is Pin selected)
        {
            ShowInspection(selected);
        }
    }

    private void BtnRowMaintain_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Pin pin)
        {
            ShowInspection(pin);
        }
    }

    private async void ShowInspection(Pin pin)
    {
        _selectedPin = pin;
        if (pnlInspection == null || txtInspectPinId == null) return;

        pnlInspection.IsVisible = true;
        txtInspectPinId.Text = pin.MaPin;
        if (txtNewPinCode != null)
        {
            txtNewPinCode.Text = $"PIN-0{DateTime.Now:ss}";
        }

        var result = await _batteryService.InspectBattery(pin.MaPin);
        if (result != null && bdrDiagnosticResult != null && txtDiagnosticTitle != null && txtDiagnosticDesc != null)
        {
            txtDiagnosticDesc.Text = $"{result.HealthStatusDescription}\nKhuyến nghị: {result.RecommendedAction}";

            if (result.IsOverheated)
            {
                bdrDiagnosticResult.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF2, 0xF2));
                txtDiagnosticTitle.Text = "🚨 BÁO ĐỘNG SỰ CỐ: PIN QUÁ NHIỆT KHẨN CẤP (> 55°C)";
                txtDiagnosticTitle.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            }
            else if (result.IsDegraded)
            {
                bdrDiagnosticResult.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7));
                txtDiagnosticTitle.Text = "⚠️ CẢNH BÁO KỸ THUẬT: PIN CHAI (SoH < 80%)";
                txtDiagnosticTitle.Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09));
            }
            else
            {
                bdrDiagnosticResult.Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xFD, 0xF4));
                txtDiagnosticTitle.Text = "✅ ĐÁNH GIÁ BMS: PIN HOẠT ĐỘNG BÌNH THƯỜNG & AN TOÀN";
                txtDiagnosticTitle.Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D));
            }
        }
    }

    private void BtnCloseInspect_Click(object? sender, RoutedEventArgs e)
    {
        if (pnlInspection != null) pnlInspection.IsVisible = false;
        _selectedPin = null;
    }

    private async void BtnCoolAndReset_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedPin == null) return;
        var btn = sender as Button;
        if (btn != null) btn.IsEnabled = false;

        try
        {
            string stationId = "TRAM-BK01";
            int slotNumber = 1;

            var stations = await _stationService.GetAllStations();
            foreach (var st in stations)
            {
                var slots = await _stationService.GetCabinetSlots(st.MaTram);
                var found = slots.FirstOrDefault(s => s.MaPinHienTai == _selectedPin.MaPin);
                if (found != null)
                {
                    stationId = st.MaTram;
                    slotNumber = found.SoThuTu;
                    break;
                }
            }

            string tech = AuthService.CurrentUser?.HoTen ?? "Kỹ thuật viên Trạm";
            var outcome = await _batteryService.MaintainBattery(stationId, slotNumber, _selectedPin.MaPin, tech, "Bảo dưỡng nhiệt độ BMS về 25°C");

            if (txtDiagnosticDesc != null)
            {
                txtDiagnosticDesc.Text = outcome.Success
                    ? $"✔ Đã hoàn tất bảo dưỡng pin {_selectedPin.MaPin} tại {stationId} (Ngăn #{slotNumber}). Nhiệt độ an toàn, đã mở khóa khay tủ."
                    : $"❌ Lỗi bảo dưỡng: {outcome.Error}";
            }
            LoadBatteries();
        }
        catch (Exception ex)
        {
            BssApi.Error(ex);
        }
        finally
        {
            if (btn != null) btn.IsEnabled = true;
        }
    }

    private async void BtnReplaceBattery_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedPin == null) return;
        string newPin = txtNewPinCode?.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(newPin))
        {
            if (txtDiagnosticDesc != null) txtDiagnosticDesc.Text = "Vui lòng nhập mã pin mới thay thế!";
            return;
        }

        var btn = sender as Button;
        if (btn != null) btn.IsEnabled = false;

        try
        {
            string stationId = "TRAM-BK01";
            int slotNumber = 1;

            var stations = await _stationService.GetAllStations();
            foreach (var st in stations)
            {
                var slots = await _stationService.GetCabinetSlots(st.MaTram);
                var found = slots.FirstOrDefault(s => s.MaPinHienTai == _selectedPin.MaPin);
                if (found != null)
                {
                    stationId = st.MaTram;
                    slotNumber = found.SoThuTu;
                    break;
                }
            }

            string tech = AuthService.CurrentUser?.HoTen ?? "Kỹ thuật viên Trạm";
            var outcome = await _batteryService.ReplaceBattery(stationId, slotNumber, _selectedPin.MaPin, newPin, tech, "Thay thế pin chai/hỏng");

            if (txtDiagnosticDesc != null)
            {
                txtDiagnosticDesc.Text = outcome.Success
                    ? $"✔ Đã thay thế thành công pin mới {newPin} vào khay #{slotNumber} trạm {stationId}."
                    : $"❌ Lỗi thay thế: {outcome.Error}";
            }
            LoadBatteries();
        }
        catch (Exception ex)
        {
            BssApi.Error(ex);
        }
        finally
        {
            if (btn != null) btn.IsEnabled = true;
        }
    }

    private async void BtnAddBattery_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var nextId = $"PIN-{_allBatteries.Count + 1:D3}";
            var newPin = new Pin
            {
                MaPin = nextId,
                LoaiPin = "Lithium LFP 72V-30Ah",
                DungLuongThietKe = 30,
                SoC = 100m,
                SoH = 100m,
                NhietDo = 25m,
                DienAp = 72m,
                DongDien = 0m,
                SoLanSac = 0,
                TrangThai = "KhaDung"
            };

            var op = await _batteryService.AddBattery(newPin);
            if (op.Success)
            {
                LoadBatteries();
            }
            else
            {
                BssApi.Error(op.Error);
            }
        }
        catch (Exception ex)
        {
            BssApi.Error(ex);
        }
    }
}
