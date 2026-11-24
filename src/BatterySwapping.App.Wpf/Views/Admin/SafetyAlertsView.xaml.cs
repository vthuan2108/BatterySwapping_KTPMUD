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

                string technicianName = AuthService.CurrentUser != null
                    ? $"{AuthService.CurrentUser.HoTen} ({AuthService.CurrentUser.VaiTro})"
                    : "Kỹ thuật viên Trạm";

                string note = $"Đã kiểm tra an toàn tại ngăn #{alert.MaNgan} ({alert.MaTram}), pin {alert.MaPin}. Nhiệt độ an toàn, đã mở khóa khay tủ.";

                if (await _safetyService.ResolveAlert(alert.MaCB, technicianName, note, moKhoaNganTu: true))
                {
                    MessageBox.Show($"Đã xác nhận khắc phục thành công sự cố #{alert.MaCB}!\n" +
                                    $"• Người xử lý: {technicianName}\n" +
                                    $"• Ngăn #{alert.MaNgan} đã được mở khóa bảo trì tự động.",
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
