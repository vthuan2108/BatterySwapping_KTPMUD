using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BatterySwapping.App.Models;
using BatterySwapping.App.Services;
using BatterySwapping.Contracts;

namespace BatterySwapping.App.Views.Admin
{
    /// <summary>
    /// Interaction logic for StationDashboardView.xaml
    /// </summary>
    public partial class StationDashboardView : UserControl
    {
        private readonly StationService _stationService;
        private readonly ReportService _reportService;
        private DispatcherTimer? _refreshTimer;
        public void SelectStation(string station){_currentStationId=station;if(cboStations.ItemsSource!=null)cboStations.SelectedValue=station;}
        public string CurrentStationId => _currentStationId;
        private string _currentStationId = "TRAM-BK01";

        public StationDashboardView()
        {
            InitializeComponent();
            btnAddStation.Visibility=AuthService.CurrentUser?.VaiTro=="Admin"?Visibility.Visible:Visibility.Collapsed;
            if(AuthService.CurrentUser?.VaiTro=="KyThuatVien"){revenueCard.Visibility=Visibility.Collapsed;swapCard.Visibility=Visibility.Collapsed;}
            _stationService = new StationService();
            _reportService = new ReportService();

            Loaded += StationDashboardView_Loaded;
            Unloaded += StationDashboardView_Unloaded;
        }

        private void StationDashboardView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadStationList();
            RefreshData();

            // Tự động làm mới giao diện mỗi 3 giây để cập nhật trạng thái sạc từ Simulator
            if (_refreshTimer == null)
            {
                _refreshTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                _refreshTimer.Tick += (s, ev) => RefreshData();
            }
            if (!_refreshTimer.IsEnabled)
            {
                _refreshTimer.Start();
            }
        }

        private void StationDashboardView_Unloaded(object sender, RoutedEventArgs e)
        {
            _refreshTimer?.Stop();
        }

        private async void LoadStationList()
        {
            if (cboStations == null) return;
            try
            {
                var stations = await _stationService.GetAllStations();
                cboStations.ItemsSource = stations;
                cboStations.DisplayMemberPath = "TenTram";
                cboStations.SelectedValuePath = "MaTram";
                cboStations.SelectedValue=_currentStationId;

                if (stations.Count > 0 && cboStations.SelectedIndex < 0)
                {
                    cboStations.SelectedIndex = 0;
                    _currentStationId = stations[0].MaTram;
                }
            }
            catch (Exception ex)
            {
                BssApi.ReportError(ex);
                // Safe ignore if DB not ready
            }
        }

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
            string state = seconds > 0
                ? $"đang làm mát tự động; khóa tối thiểu còn {seconds / 60:00}:{seconds % 60:00}"
                : slot == null || slot.NhietDo >= 35
                    ? "đã hết 5 phút, tiếp tục làm mát và giữ khóa; cần dưới 35°C"
                    : "đã đủ thời gian và dưới 35°C; chờ kỹ thuật viên xác nhận reset cùng thiết bị";
            return $"Ngăn #{c.SlotNumber} • {c.BatteryId ?? "chưa rõ pin"} • {temperature}: {state}.";
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
            : $"Ngăn #{slotNumber} đã hết thời gian tối thiểu nhưng chưa được xác nhận an toàn. Kiểm tra nhiệt độ dưới 35°C và xử lý phiếu bảo trì trước khi mở ngăn.");
    }

        private bool _refreshing;
        public async void RefreshData()
        {
            if(_refreshing)return;_refreshing=true;
            var targetStation=_currentStationId;
            try {

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(RefreshData);
                return;
            }

            if (lblTodayRevenue == null || lblTotalSwaps == null || lblReadyBatteries == null ||
                lblActiveAlerts == null || icSlots == null || cboStations == null) return;
            try
            {
                var connection=await BssApi.Client.Get<BatterySwapping.Contracts.StationStatus>($"stations/{targetStation}/status");
                txtTelemetryStatus.Text=connection.Online?"● TELEMETRY ONLINE":"● THIẾT BỊ OFFLINE";
                // Cập nhật thống kê KPI
                if(AuthService.CurrentUser?.VaiTro=="Admin"){
                var stats = await _reportService.GetSummaryStats();
                lblTodayRevenue.Text = string.Format("{0:N0} đ", stats.DoanhThuHomNay);
                lblTotalSwaps.Text = $"{stats.TongLuotDoiPin} lượt";
                lblReadyBatteries.Text = $"{stats.SoPinKhaDung} / {stats.TongSoPin} pin";
                lblActiveAlerts.Text = $"{stats.SoCanhBaoChuaXuLy} sự cố";

                }
                // Cập nhật danh sách ngăn tủ
                var slots = await _stationService.GetCabinetSlots(targetStation);
                if(targetStation!=_currentStationId)return;
                _cooldownReadValid = false;
                try {
                    var cooldowns = await BssApi.Client.Get<List<SlotCooldown>>($"stations/{targetStation}/cooldowns");
                    if(targetStation != _currentStationId) return;
                    _cooldowns = cooldowns; _cooldownStation = targetStation; _cooldownReadValid = true;
                    ShowCooldowns(slots, connection.Online);
                } catch(Exception ex) { txtCooldownStatus.Text = "Không đọc được khóa nhiệt từ backend; chưa cho mở ngăn. " + ex.Message; }
                icSlots.ItemsSource = slots;icSlots.IsEnabled=true;
                if(AuthService.CurrentUser?.VaiTro=="KyThuatVien"){lblReadyBatteries.Text=$"{slots.Count(s=>s.CoPin&&s.SoC>=90&&s.NhietDo<45&&!s.KhoaBaoTri)} / {slots.Count} pin";lblActiveAlerts.Text=$"{slots.Count(s=>s.KhoaBaoTri||s.NhietDo>=55)} ngăn cần kiểm tra";}
            }
            catch (Exception ex)
            {
                BssApi.ReportError(ex);
                // Safe ignore
            }

            } finally {_refreshing=false;}
        }

        private void CboStations_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboStations == null || lblStationAddress == null) return;
            if (cboStations.SelectedItem is TramPin selectedStation)
            {
                icSlots.IsEnabled=false;icSlots.ItemsSource=null;
                _cooldownReadValid=false;txtCooldownStatus.Text="Đang kiểm tra khóa nhiệt của trạm được chọn…";
                _currentStationId = selectedStation.MaTram;
                lblStationAddress.Text = $"{selectedStation.TenTram} • {selectedStation.DiaChi}";
                RefreshData();
            }
        }

        private void BtnAddStation_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Dialogs.AddStationDialog();
            if (dialog.ShowDialog() == true)
            {
                LoadStationList();
                RefreshData();
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshData();
        }

        private async void BtnOpenDoor_Click(object sender, RoutedEventArgs e)
        {
            if(!icSlots.IsEnabled)return;
            var targetStation=_currentStationId;
            var sourceButton=sender as System.Windows.Controls.Button;
            if(sourceButton!=null)sourceButton.IsEnabled=false;
            try {

            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int slotNumber))
            {
                var slots = icSlots.ItemsSource as List<NganTu>;
                var slot = slots?.Find(s => s.SoThuTu == slotNumber);
                bool nextDoorState = slot == null || !slot.CuaDangMo;
                if(nextDoorState) CheckCooldownBeforeOpening(slotNumber);

                await _stationService.SetDoorState(targetStation, slotNumber, nextDoorState);
                MessageBox.Show($"Đã phát lệnh {(nextDoorState ? "MỞ CỬA" : "ĐÓNG CỬA")} ngăn tủ #{slotNumber} tại trạm {_currentStationId}!",
                                "Điều khiển phần cứng", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshData();
            }

            } catch(Exception ex){BssApi.Error(ex);}
            finally {if(sourceButton!=null)sourceButton.IsEnabled=true;}
        }

        private async void BtnToggleLock_Click(object sender, RoutedEventArgs e)
        {
            if(!icSlots.IsEnabled)return;
            var targetStation=_currentStationId;
            var sourceButton=sender as System.Windows.Controls.Button;
            if(sourceButton!=null)sourceButton.IsEnabled=false;
            try {

            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int slotNumber))
            {
                var slots = icSlots.ItemsSource as List<NganTu>;
                var slot = slots?.Find(s => s.SoThuTu == slotNumber);
                bool nextLockState = slot == null || !slot.KhoaBaoTri;

                await _stationService.SetMaintenanceLock(targetStation, slotNumber, nextLockState);
                if (nextLockState)
                {
                    MessageBox.Show($"Đã kích hoạt chế độ KHÓA BẢO TRÌ cho ngăn tủ #{slotNumber}!",
                                    "An toàn hệ thống", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show($"Đã MỞ KHÓA BẢO TRÌ cho ngăn tủ #{slotNumber}!",
                                    "An toàn hệ thống", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                RefreshData();
            }

            } catch(Exception ex){BssApi.Error(ex);}
            finally {if(sourceButton!=null)sourceButton.IsEnabled=true;}
        }
        private async void CutoffClick(object sender,RoutedEventArgs e)
        {
            var station=_currentStationId;
            if(MessageBox.Show($"Ngắt sạc và khóa mọi ngăn tại {station}?","Xác nhận ngắt trạm",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
            var button=(Button)sender;button.IsEnabled=false;
            try{await BssApi.Command(station,"CUT_OFF");MessageBox.Show($"Thiết bị đã xác nhận ngắt trạm {station}.");RefreshData();}catch(Exception ex){BssApi.Error(ex);}finally{button.IsEnabled=true;}
        }
        private async void RestoreClick(object sender,RoutedEventArgs e)
        {
            var station=_currentStationId;
            if(MessageBox.Show($"Yêu cầu kiểm tra an toàn và khôi phục trạm {station}?","Khôi phục trạm",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            var button=(Button)sender;button.IsEnabled=false;
            try{await BssApi.Command(station,"RESTORE_STATION");RefreshData();}catch(Exception ex){BssApi.Error(ex);}finally{button.IsEnabled=true;}
        }
    }
}

