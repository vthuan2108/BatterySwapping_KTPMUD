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
    private List<SlotCooldown> _cooldowns = new();
    private bool _cooldownReadValid;
    private bool _cooldownDeviceOnline;
    private string _cooldownStation = "";

    private void ShowCooldowns(List<NganTu> slots, bool online)
    {
        _cooldownDeviceOnline = online;
        var active = _cooldowns.Where(c => c.ClearedAt == null).OrderBy(c => c.SlotNumber).ToList();
        if (active.Count == 0)
        {
            txtCooldownStatus.Text = online ? "Bảo vệ nhiệt: không có ngăn đang trong đợt khóa quá nhiệt." : "Thiết bị offline — chưa thể xác nhận tình trạng an toàn hoặc mở ngăn.";
            return;
        }
        var lines = active.Select(c => {
            var seconds = Math.Max(0, (int)Math.Ceiling((c.UnlockAfter - DateTimeOffset.UtcNow).TotalSeconds));
            var slot = slots.Find(n => n.SoThuTu == c.SlotNumber);
            string temperature = slot == null ? "chưa có số liệu" : $"{slot.NhietDo:F1}°C";
            bool requiresAdmin = c.RequiresAdmin || c.IncidentCount >= 3;
            string state = seconds > 0
                ? $"đang làm mát tự động; khóa tối thiểu còn {seconds / 60:00}:{seconds % 60:00}"
                : slot == null || slot.NhietDo >= 35
                    ? "đã hết 5 phút, tiếp tục làm mát và giữ khóa; cần dưới 35°C"
                    : requiresAdmin
                        ? "đã đủ thời gian và dưới 35°C; chỉ Admin được xác nhận reset, tiếp tục giữ khóa"
                        : "đã đủ thời gian và dưới 35°C; chờ hệ thống và thiết bị tự động khôi phục, không cần xác nhận";
            if (requiresAdmin && (seconds > 0 || slot == null || slot.NhietDo >= 35))
                state += "; sau khi an toàn vẫn cần Admin xác nhận";
            return $"Ngăn #{c.SlotNumber} • quá nhiệt lần {c.IncidentCount} • {c.BatteryId ?? "chưa rõ pin"} • {temperature}: {state}.";
        });
        txtCooldownStatus.Text = string.Join("\n", lines) + "\nKhông nhận/cấp/rút pin hoặc mở cửa khi còn khóa bảo vệ. Thời gian thực, không tăng tốc theo mô phỏng." + (online ? "" : "\nThiết bị offline — số liệu có thể cũ, tiếp tục giữ khóa.");
    }

    private void CheckCooldownBeforeOpening(int slotNumber)
    {
        if (!_cooldownReadValid || _cooldownStation != _currentStationId)
            throw new InvalidOperationException("Chưa xác nhận được trạng thái khóa nhiệt từ backend. Làm mới dữ liệu trước khi mở ngăn.");
        if (!_cooldownDeviceOnline) throw new InvalidOperationException("Thiết bị đang offline; chưa cho mở ngăn hoặc thao tác pin. Chờ kết nối và số liệu an toàn mới nhất.");
        var active = _cooldowns.Find(c => c.SlotNumber == slotNumber && c.ClearedAt == null);
        if (active == null) return;
        var seconds = Math.Max(0, (int)Math.Ceiling((active.UnlockAfter - DateTimeOffset.UtcNow).TotalSeconds));
        throw new InvalidOperationException(seconds > 0
            ? $"Ngăn #{slotNumber} đang làm mát tự động. Còn tối thiểu {seconds / 60:00}:{seconds % 60:00} khóa theo thời gian thực; không được cắm/rút pin hoặc mở cửa."
            : active.RequiresAdmin || active.IncidentCount >= 3
                ? $"Ngăn #{slotNumber} quá nhiệt lần {active.IncidentCount}; tiếp tục giữ khóa đến khi dưới 35°C và Admin xác nhận reset."
                : $"Ngăn #{slotNumber} chờ hệ thống và thiết bị tự động khôi phục khi dưới 35°C; không cần xác nhận thủ công. Tiếp tục giữ khóa.");
    }

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

            _cooldownReadValid = false;
            try {
                var cooldowns = await BssApi.Client.Get<List<SlotCooldown>>($"stations/{targetStation}/cooldowns");
                if(targetStation != _currentStationId) return;
                _cooldowns = cooldowns; _cooldownStation = targetStation; _cooldownReadValid = true;
                var connection = await BssApi.Client.Get<StationStatus>($"stations/{targetStation}/status");
                if(targetStation != _currentStationId) return;
                foreach(var slot in slots)slot.ApplyCooldown(cooldowns.Find(c=>c.SlotNumber==slot.SoThuTu));
                    ShowCooldowns(slots, connection.Online);
            } catch(Exception ex) { txtCooldownStatus.Text = "Không đọc được khóa nhiệt từ backend; chưa cho mở ngăn. " + ex.Message; }
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
            _cooldownReadValid=false;txtCooldownStatus.Text="Đang kiểm tra khóa nhiệt của trạm được chọn…";
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
                if(nextDoorState) CheckCooldownBeforeOpening(slotNumber);

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
