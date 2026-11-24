using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BatterySwapping.App.Models;
using BatterySwapping.App.Services;

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
                var connection=await BssApi.Client.Get<BatterySwapping.Contracts.StationStatus>($"stations/{_currentStationId}/status");
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

