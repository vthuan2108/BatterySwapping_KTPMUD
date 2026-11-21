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
        public string CurrentStationId => _currentStationId;
        private string _currentStationId = "TRAM-BK01";

        public StationDashboardView()
        {
            InitializeComponent();
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
                    Interval = TimeSpan.FromSeconds(3)
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

        private void LoadStationList()
        {
            if (cboStations == null) return;
            try
            {
                var stations = _stationService.GetAllStations();
                cboStations.ItemsSource = stations;
                cboStations.DisplayMemberPath = "TenTram";
                cboStations.SelectedValuePath = "MaTram";

                if (stations.Count > 0 && cboStations.SelectedIndex < 0)
                {
                    cboStations.SelectedIndex = 0;
                    _currentStationId = stations[0].MaTram;
                }
            }
            catch
            {
                // Safe ignore if DB not ready
            }
        }

        public void RefreshData()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(RefreshData);
                return;
            }

            if (lblTodayRevenue == null || lblTotalSwaps == null || lblReadyBatteries == null || 
                lblActiveAlerts == null || icSlots == null || cboStations == null) return;
            try
            {
                // Cập nhật thống kê KPI
                var stats = _reportService.GetSummaryStats();
                lblTodayRevenue.Text = string.Format("{0:N0} đ", stats.DoanhThuHomNay);
                lblTotalSwaps.Text = $"{stats.TongLuotDoiPin} lượt";
                lblReadyBatteries.Text = $"{stats.SoPinKhaDung} / {stats.TongSoPin} pin";
                lblActiveAlerts.Text = $"{stats.SoCanhBaoChuaXuLy} sự cố";

                // Cập nhật danh sách 8 ngăn tủ
                var slots = _stationService.GetCabinetSlots(_currentStationId);
                icSlots.ItemsSource = slots;
            }
            catch
            {
                // Safe ignore
            }
        }

        private void CboStations_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboStations == null || lblStationAddress == null) return;
            if (cboStations.SelectedItem is TramPin selectedStation)
            {
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

        private void BtnOpenDoor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int slotNumber))
            {
                var slots = icSlots.ItemsSource as List<NganTu>;
                var slot = slots?.Find(s => s.SoThuTu == slotNumber);
                bool nextDoorState = slot == null || !slot.CuaDangMo;

                _stationService.SetDoorState(_currentStationId, slotNumber, nextDoorState);
                MessageBox.Show($"Đã phát lệnh {(nextDoorState ? "MỞ CỬA" : "ĐÓNG CỬA")} ngăn tủ #{slotNumber} tại trạm {_currentStationId}!", 
                                "Điều khiển phần cứng", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshData();
            }
        }

        private void BtnToggleLock_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int slotNumber))
            {
                var slots = icSlots.ItemsSource as List<NganTu>;
                var slot = slots?.Find(s => s.SoThuTu == slotNumber);
                bool nextLockState = slot == null || !slot.KhoaBaoTri;

                _stationService.SetMaintenanceLock(_currentStationId, slotNumber, nextLockState);
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
        }
    }
}
