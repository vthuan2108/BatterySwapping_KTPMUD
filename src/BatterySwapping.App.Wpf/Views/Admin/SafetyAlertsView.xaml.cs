using System.Windows;
using System.Windows.Controls;
using BatterySwapping.App.Models;
using BatterySwapping.App.Services;

namespace BatterySwapping.App.Views.Admin
{
    /// <summary>
    /// Interaction logic for SafetyAlertsView.xaml
    /// </summary>
    public partial class SafetyAlertsView : UserControl
    {
        private readonly SafetyMonitorService _safetyService;

        public SafetyAlertsView()
        {
            InitializeComponent();
            _safetyService = new SafetyMonitorService();
            Loaded += (s, e) => LoadAlerts();
        }

        public async void LoadAlerts()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(LoadAlerts);
                return;
            }
            if (dgAlerts == null || chkOnlyUnresolved == null) return;
            try
            {
                bool onlyUnresolved = chkOnlyUnresolved.IsChecked == true;
                dgAlerts.ItemsSource = await _safetyService.GetAlerts(onlyUnresolved);
            }
            catch (Exception ex)
            {
                BssApi.ReportError(ex);
                // Safe ignore
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadAlerts();
        }

        private void ChkOnlyUnresolved_Changed(object sender, RoutedEventArgs e)
        {
            LoadAlerts();
        }

        private async void BtnResolve_Click(object sender, RoutedEventArgs e)
        {
            var sourceButton=sender as System.Windows.Controls.Button;
            if(sourceButton!=null)sourceButton.IsEnabled=false;
            try {

            if (sender is Button btn && btn.Tag is CanhBaoIoT alert)
            {
                if (alert.DaXuLy)
                {
                    MessageBox.Show("Sự cố này đã được xử lý trước đó!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                bool hasActiveCooldown = false;
                if (alert.MaNgan.HasValue)
                {
                    var cooldowns = await BssApi.Client.Get<List<BatterySwapping.Contracts.SlotCooldown>>($"stations/{alert.MaTram}/cooldowns");
                    var activeCooldown = cooldowns.Find(c => c.SlotNumber == alert.MaNgan.Value && c.ClearedAt == null);
                    if (activeCooldown != null)
                    {
                        hasActiveCooldown = true;
                        var reason = await BssApi.GetThermalResetBlockReason(alert.MaTram, alert.MaNgan.Value);
                        if (reason != null)
                        {
                            MessageBox.Show(reason, "Bảo vệ nhiệt", MessageBoxButton.OK, MessageBoxImage.Information);
                            LoadAlerts();
                            return;
                        }
                        var seconds = Math.Max(0, (int)Math.Ceiling((activeCooldown.UnlockAfter - DateTimeOffset.UtcNow).TotalSeconds));
                        if (seconds > 0)
                        {
                            MessageBox.Show($"Ngăn #{alert.MaNgan.Value} đang làm mát tự động. Còn tối thiểu {seconds / 60:00}:{seconds % 60:00} khóa theo thời gian thực.", "Bảo vệ nhiệt", MessageBoxButton.OK, MessageBoxImage.Information);
                            LoadAlerts();
                            return;
                        }
                    }
                }

                string technicianName = AuthService.CurrentUser != null
                    ? $"{AuthService.CurrentUser.HoTen} ({AuthService.CurrentUser.VaiTro})"
                    : "Kỹ thuật viên Trạm";

                string note = hasActiveCooldown
                    ? $"Đã kiểm tra an toàn tại ngăn #{alert.MaNgan} ({alert.MaTram}), pin {alert.MaPin ?? "--"}. Yêu cầu backend kiểm tra điều kiện an toàn và thiết bị xác nhận reset."
                    : alert.MaNgan.HasValue
                        ? $"Đã kiểm tra và đóng cảnh báo tại ngăn #{alert.MaNgan} ({alert.MaTram}), pin {alert.MaPin ?? "--"}."
                        : $"Đã kiểm tra và đóng cảnh báo tại trạm {alert.MaTram}.";

                if (await _safetyService.ResolveAlert(alert.MaCB, technicianName, note, moKhoaNganTu: hasActiveCooldown))
                {
                    string detail = hasActiveCooldown
                        ? $"• Ngăn #{alert.MaNgan} đã được xác nhận reset an toàn và mở khóa bảo trì."
                        : "• Trạng thái: Đã đóng cảnh báo (không tác động khóa phần cứng).";
                    MessageBox.Show($"Đã xác nhận khắc phục thành công sự cố #{alert.MaCB}!\n" +
                                    $"• Người xử lý: {technicianName}\n" +
                                    detail,
                                    "Xử lý thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadAlerts();
                }
                else
                {
                    MessageBox.Show($"Không thể cập nhật trạng thái sự cố #{alert.MaCB}. Vui lòng thử lại!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            } catch(Exception ex){BssApi.Error(ex);}
            finally {if(sourceButton!=null)sourceButton.IsEnabled=true;}
        }
    }
}
