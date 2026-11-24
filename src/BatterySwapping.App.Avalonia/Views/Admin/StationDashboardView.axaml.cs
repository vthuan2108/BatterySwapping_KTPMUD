using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using BatterySwapping.App.Avalonia.Models;
using BatterySwapping.App.Avalonia.Services;
using BatterySwapping.Contracts;

namespace BatterySwapping.App.Avalonia.Views.Admin;

public partial class StationDashboardView : UserControl
{
    private readonly StationService _stationService = new();
    private readonly ReportService _reportService = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string _currentStationId = "TRAM-BK01";
    private bool _refreshing;
    private List<TramPin> _stations = new();

    public string CurrentStationId => _currentStationId;

    public StationDashboardView()
    {
        InitializeComponent();

        _refreshTimer.Tick += (_, _) => RefreshData();

        AttachedToVisualTree += (_, _) =>
        {
            LoadStationList();
            RefreshData();
            _refreshTimer.Start();
        };

        DetachedFromVisualTree += (_, _) =>
        {
            _refreshTimer.Stop();
        };
    }

    public void SelectStation(string station)
    {
        _currentStationId = station;
        if (_stations.Count > 0)
        {
            var match = _stations.FirstOrDefault(s => s.MaTram == station);
            if (match != null && cboStations != null)
            {
                cboStations.SelectedItem = match;
            }
        }
        RefreshData();
    }

    private async void LoadStationList()
    {
        if (cboStations == null) return;
        try
        {
            _stations = await _stationService.GetAllStations();
            cboStations.ItemsSource = _stations;

            var match = _stations.FirstOrDefault(s => s.MaTram == _currentStationId) ?? _stations.FirstOrDefault();
            if (match != null)
            {
                _currentStationId = match.MaTram;
                cboStations.SelectedItem = match;
                if (lblStationAddress != null)
                {
                    lblStationAddress.Text = $"{match.TenTram} • {match.DiaChi}";
                }
            }
        }
        catch (Exception ex)
        {
            BssApi.ReportError(ex);
        }
    }

    public async void RefreshData()
    {
        if (_refreshing) return;
        _refreshing = true;
        var targetStation = _currentStationId;

        try
        {
            // Kiểm tra trạng thái kết nối trạm
            try
            {
                var connection = await BssApi.Client.Get<StationStatus>($"stations/{targetStation}/status");
                if (txtTelemetryStatus != null && pnlTelemetryBadge != null)
                {
                    txtTelemetryStatus.Text = connection.Online ? "● TELEMETRY ONLINE" : "● THIẾT BỊ OFFLINE";
                    txtTelemetryStatus.Foreground = connection.Online
                        ? new SolidColorBrush(Color.FromRgb(0x04, 0x78, 0x57))
                        : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                    pnlTelemetryBadge.Background = connection.Online
                        ? new SolidColorBrush(Color.FromRgb(0xDC, 0xFC, 0xE7))
                        : new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));
                }
            }
            catch { }

            // KPI Thống kê
            try
            {
                if(AuthService.CurrentUser?.VaiTro=="Admin"){
                var stats = await _reportService.GetSummaryStats();
                if (lblTodayRevenue != null) lblTodayRevenue.Text = $"{stats.DoanhThuHomNay:N0} đ";
                if (lblTotalSwaps != null) lblTotalSwaps.Text = $"{stats.TongLuotDoiPin} lượt";
                if (lblReadyBatteries != null) lblReadyBatteries.Text = $"{stats.SoPinKhaDung} / {stats.TongSoPin} pin";
                if (lblActiveAlerts != null) lblActiveAlerts.Text = $"{stats.SoCanhBaoChuaXuLy} sự cố";}
            }
            catch { }

            // Danh sách ngăn tủ
            var slots = await _stationService.GetCabinetSlots(targetStation);
            if (targetStation != _currentStationId) return;

            if (icSlots != null)
            {
                icSlots.ItemsSource = slots;if(AuthService.CurrentUser?.VaiTro=="KyThuatVien"){if(lblReadyBatteries!=null)lblReadyBatteries.Text=$"{slots.Count(s=>s.CoPin&&s.SoC>=90&&s.NhietDo<45&&!s.KhoaBaoTri)} / {slots.Count} pin";if(lblActiveAlerts!=null)lblActiveAlerts.Text=$"{slots.Count(s=>s.KhoaBaoTri||s.NhietDo>=55)} ngăn cần kiểm tra";}
            }
        }
        catch (Exception ex)
        {
            BssApi.ReportError(ex);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void CboStations_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (cboStations?.SelectedItem is TramPin selected)
        {
            _currentStationId = selected.MaTram;
            if (lblStationAddress != null)
            {
                lblStationAddress.Text = $"{selected.TenTram} • {selected.DiaChi}";
            }
            RefreshData();
        }
    }

    private void BtnRefresh_Click(object? sender, RoutedEventArgs e)
    {
        RefreshData();
    }

    private async void BtnOpenDoor_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int slotNumber))
        {
            btn.IsEnabled = false;
            try
            {
                var slots = icSlots?.ItemsSource as List<NganTu>;
                var slot = slots?.FirstOrDefault(s => s.SoThuTu == slotNumber);
                bool nextDoorState = slot == null || !slot.CuaDangMo;

                await _stationService.SetDoorState(_currentStationId, slotNumber, nextDoorState);
                if (txtActionFeedback != null)
                {
                    txtActionFeedback.Text = $"✔ Đã phát lệnh {(nextDoorState ? "MỞ CỬA" : "ĐÓNG CỬA")} ngăn #{slotNumber} trạm {_currentStationId}";
                }
                RefreshData();
            }
            catch (Exception ex)
            {
                BssApi.Error(ex);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
    }

    private async void BtnToggleLock_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int slotNumber))
        {
            btn.IsEnabled = false;
            try
            {
                var slots = icSlots?.ItemsSource as List<NganTu>;
                var slot = slots?.FirstOrDefault(s => s.SoThuTu == slotNumber);
                bool nextLockState = slot == null || !slot.KhoaBaoTri;

                await _stationService.SetMaintenanceLock(_currentStationId, slotNumber, nextLockState);
                if (txtActionFeedback != null)
                {
                    txtActionFeedback.Text = nextLockState
                        ? $"⚠ Đã KÍCH HOẠT KHÓA BẢO TRÌ ngăn #{slotNumber} trạm {_currentStationId}"
                        : $"✔ Đã MỞ KHÓA BẢO TRÌ ngăn #{slotNumber} trạm {_currentStationId}";
                }
                RefreshData();
            }
            catch (Exception ex)
            {
                BssApi.Error(ex);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
    }

    private async void CutoffClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            btn.IsEnabled = false;
            try
            {
                await BssApi.Command(_currentStationId, "CUT_OFF");
                if (txtActionFeedback != null)
                {
                    txtActionFeedback.Text = $"🚨 ĐÃ PHÁT LỆNH NGẮT SẠC TOÀN TRẠM {_currentStationId} THÀNH CÔNG!";
                }
                RefreshData();
            }
            catch (Exception ex)
            {
                BssApi.Error(ex);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
    }

    private async void RestoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            btn.IsEnabled = false;
            try
            {
                await BssApi.Command(_currentStationId, "RESTORE_STATION");
                if (txtActionFeedback != null)
                {
                    txtActionFeedback.Text = $"✔ ĐÃ KHÔI PHỤC HOẠT ĐỘNG TRẠM {_currentStationId}";
                }
                RefreshData();
            }
            catch (Exception ex)
            {
                BssApi.Error(ex);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
    }
}
