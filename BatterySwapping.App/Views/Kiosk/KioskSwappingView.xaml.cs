using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BatterySwapping.App.Helpers;
using BatterySwapping.App.Models;
using BatterySwapping.App.Services;

namespace BatterySwapping.App.Views.Kiosk
{
    /// <summary>
    /// Interaction logic for KioskSwappingView.xaml
    /// Điều phối trực quan chu trình đổi pin 6 bước kết hợp hiển thị mô hình Tủ Pin Phần Cứng thực tế.
    /// </summary>
    public partial class KioskSwappingView : UserControl
    {
        private readonly SwapService _swapService;
        private readonly StationService _stationService;
        private readonly KhachHang _customer;
        private readonly string _stationId;

        private int _currentStep = 2; // Bắt đầu từ bước 2 vì bước 1 xác thực ví đã hoàn tất ở màn hình chào
        private NganTu? _emptySlot;
        private NganTu? _optimalSlot;
        private string _oldBatteryId = string.Empty;
        private decimal _oldBatterySoC = 18.0m;
        private List<NganTu> _cabinetSlots = new();

        public event Action<SwapResult>? OnSwapCompleted;
        public event Action? OnCancelled;

        public KioskSwappingView(KhachHang customer, string stationId = "TRAM-BK01")
        {
            InitializeComponent();
            _swapService = new SwapService();
            _stationService = new StationService();
            _customer = customer;
            _stationId = stationId;

            lblCustomerName.Text = _customer.HoTen;
            lblPlate.Text = _customer.BienSoXe;
            lblBalance.Text = _customer.SoDuFormatted;

            if (!string.IsNullOrEmpty(_customer.MaPinDangMuon))
            {
                _oldBatteryId = _customer.MaPinDangMuon;
            }

            Loaded += (s, e) =>
            {
                LoadInitialCabinetData();
                StartStep2_OpenEmptySlot();
            };

            Unloaded += (s, e) =>
            {
                try
                {
                    if (_emptySlot != null && _currentStep < 3) _stationService.SetDoorState(_stationId, _emptySlot.SoThuTu, false);
                    if (_optimalSlot != null && _currentStep == 5) _stationService.SetDoorState(_stationId, _optimalSlot.SoThuTu, false);
                }
                catch { }
            };
        }

        private void LoadInitialCabinetData()
        {
            try
            {
                _cabinetSlots = _stationService.GetCabinetSlots(_stationId) ?? new();
                RefreshCabinetView();
            }
            catch { }
        }

        private void RefreshCabinetView()
        {
            if (icKioskCabinetSlots == null) return;
            icKioskCabinetSlots.ItemsSource = null;
            icKioskCabinetSlots.ItemsSource = _cabinetSlots;
        }

        private void HighlightStep(int stepNumber)
        {
            Border[] steps = { step1, step2, step3, step4, step5, step6 };
            var activeBrush = TryFindResource("PrimaryBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
            var inactiveBorderBrush = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51));
            var inactiveTextBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));

            for (int i = 0; i < steps.Length; i++)
            {
                if (steps[i] == null) continue;
                if (i + 1 <= stepNumber)
                {
                    steps[i].BorderBrush = activeBrush;
                    if (steps[i].Child is TextBlock tb)
                    {
                        tb.Foreground = activeBrush;
                        tb.FontWeight = FontWeights.Bold;
                    }
                }
                else
                {
                    steps[i].BorderBrush = inactiveBorderBrush;
                    if (steps[i].Child is TextBlock tb)
                    {
                        tb.Foreground = inactiveTextBrush;
                        tb.FontWeight = FontWeights.Normal;
                    }
                }
            }
        }

        /// <summary>
        /// BƯỚC 2: Tìm khay tủ trống và phát lệnh mở cửa khay
        /// </summary>
        private void StartStep2_OpenEmptySlot()
        {
            _currentStep = 2;
            HighlightStep(2);

            if (string.IsNullOrWhiteSpace(_oldBatteryId))
            { MessageBox.Show("Khách hàng chưa có pin đang mượn để đổi."); OnCancelled?.Invoke(); return; }
            _emptySlot = _swapService.FindAvailableEmptySlot(_stationId);
            if (_emptySlot == null)
            {
                MessageBox.Show("Rất tiếc! Hiện tại trạm sạc không còn khay tủ trống để nhận pin cạn. Vui lòng thử lại sau!", 
                                "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                OnCancelled?.Invoke();
                return;
            }

            // Mở cửa khay trống trong CSDL
            _stationService.SetDoorState(_stationId, _emptySlot.SoThuTu, true);

            // Cập nhật trạng thái hiển thị trên mô hình tủ pin vật lý bên trái
            var slotModel = _cabinetSlots.Find(s => s.SoThuTu == _emptySlot.SoThuTu);
            if (slotModel != null)
            {
                slotModel.CuaDangMo = true;
            }
            RefreshCabinetView();

            txtStepIcon.Text = "🔓";
            txtStepTitle.Text = $"CỬA HỘC TỦ #{_emptySlot.SoThuTu:D2} ĐÃ BẬT MỞ";
            txtStepDescription.Text = $"Khay sạc #{_emptySlot.SoThuTu:D2} đang mở cửa (Đèn báo màu xanh Cyan trên tủ bên trái).\nXin mời Quý khách cắm viên pin cũ ({_oldBatteryId}) vào khay và ĐÓNG CHẶT CỬA TỦ.";
            boxDetails.Visibility = Visibility.Collapsed;
            btnAction.Content = "ĐÃ CẮM PIN VÀ ĐÓNG CỬA KHAY ➔";
        }

        /// <summary>
        /// BƯỚC 3: Đọc dữ liệu Telemetry BMS của pin cũ
        /// </summary>
        private void StartStep3_ReadOldBatteryBms()
        {
            _currentStep = 3;
            HighlightStep(3);

            // Đóng cửa khay
            if (_emptySlot != null)
            {
                _stationService.SetDoorState(_stationId, _emptySlot.SoThuTu, false);
                var slotModel = _cabinetSlots.Find(s => s.SoThuTu == _emptySlot.SoThuTu);
                if (slotModel != null)
                {
                    slotModel.CuaDangMo = false;
                    slotModel.MaPinHienTai = _oldBatteryId;
                    slotModel.SoC = _oldBatterySoC;
                    slotModel.TrangThaiNgan = "DangSac";
                }
            }
            RefreshCabinetView();

            txtStepIcon.Text = "🔍";
            txtStepTitle.Text = "ĐANG ĐỌC DỮ LIỆU BMS PIN CŨ...";
            txtStepDescription.Text = "Hệ thống đang quét kiểm tra thông số an toàn, điện áp và dung lượng còn lại của viên pin bạn vừa cắm.";

            boxDetails.Visibility = Visibility.Visible;
            lblDetailHeader.Text = $"🔍 KẾT QUẢ ĐỌC CẢM BIẾN BMS: {_oldBatteryId}";
            lblDetail1.Text = $"Mã Pin: {_oldBatteryId}";

            try
            {
                var dtPin = DatabaseHelper.ExecuteDataTable(
                    "SELECT SoC, SoH, NhietDo FROM dbo.Pin WHERE MaPin = @MaPin",
                    new Microsoft.Data.SqlClient.SqlParameter("@MaPin", _oldBatteryId));
                if (dtPin.Rows.Count > 0)
                {
                    _oldBatterySoC = Convert.ToDecimal(dtPin.Rows[0]["SoC"]);
                    decimal soh = Convert.ToDecimal(dtPin.Rows[0]["SoH"]);
                    decimal temp = Convert.ToDecimal(dtPin.Rows[0]["NhietDo"]);
                    if (temp >= SafetyMonitorService.CRITICAL_TEMPERATURE)
                    { MessageBox.Show("Pin trả quá nhiệt. Vui lòng liên hệ kỹ thuật viên."); OnCancelled?.Invoke(); return; }
                    lblDetail2.Text = $"Dung lượng SoC: {_oldBatterySoC:F1}%";
                    lblDetail3.Text = $"Sức khỏe SoH: {soh:F1}%";
                    lblDetail4.Text = $"Nhiệt độ: {temp:F1} °C (An toàn)";
                }
                else
                { MessageBox.Show("Không tìm thấy pin trả. Giao dịch đã dừng."); OnCancelled?.Invoke(); return; }
            }
            catch (Exception ex)
            { MessageBox.Show($"Không đọc được BMS: {ex.Message}"); OnCancelled?.Invoke(); return; }

            btnAction.Content = "XÁC NHẬN THÔNG SỐ & CHỌN PIN MỚI ➔";
        }

        /// <summary>
        /// BƯỚC 4: Thuật toán chọn pin đầy tối ưu
        /// </summary>
        private void StartStep4_FindOptimalBattery()
        {
            _currentStep = 4;
            HighlightStep(4);

            _optimalSlot = _swapService.FindOptimalBatterySlot(_stationId);
            if (_optimalSlot == null)
            {
                MessageBox.Show("Hiện tại trạm chưa có viên pin nào sạc đầy đạt chuẩn (SoC ≥ 90%, Nhiệt độ < 45°C) để nhả! Vui lòng liên hệ nhân viên.",
                                "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                OnCancelled?.Invoke();
                return;
            }

            txtStepIcon.Text = "✨";
            txtStepTitle.Text = $"ĐÃ TÌM THẤY PIN TỐI ƯU TẠI HỘC #{_optimalSlot.SoThuTu:D2}";
            txtStepDescription.Text = $"Thuật toán đã chọn viên pin có hiệu năng tốt nhất sẵn sàng cho xe của bạn tại Hộc #{_optimalSlot.SoThuTu:D2}.";

            boxDetails.Visibility = Visibility.Visible;
            lblDetailHeader.Text = $"🔋 THÔNG SỐ PIN MỚI ĐƯỢC CHỌN: {_optimalSlot.MaPinHienTai}";
            lblDetail1.Text = $"Mã Pin: {_optimalSlot.MaPinHienTai}";
            lblDetail2.Text = $"Dung lượng SoC: {_optimalSlot.SoC:F1}% (ĐÃ ĐẦY)";
            lblDetail3.Text = $"Sức khỏe SoH: {_optimalSlot.SoH:F1}%";
            lblDetail4.Text = $"Nhiệt độ BMS: {_optimalSlot.NhietDo:F1} °C";

            btnAction.Content = $"MỞ KHAY #{_optimalSlot.SoThuTu:D2} ĐỂ LẤY PIN ➔";
        }

        /// <summary>
        /// BƯỚC 5: Mở khay pin mới cho khách lấy
        /// </summary>
        private void StartStep5_DispenseNewBattery()
        {
            _currentStep = 5;
            HighlightStep(5);

            if (_optimalSlot != null)
            {
                _stationService.SetDoorState(_stationId, _optimalSlot.SoThuTu, true);
                var slotModel = _cabinetSlots.Find(s => s.SoThuTu == _optimalSlot.SoThuTu);
                if (slotModel != null)
                {
                    slotModel.CuaDangMo = true;
                }
            }
            RefreshCabinetView();

            txtStepIcon.Text = "🎁";
            txtStepTitle.Text = $"CỬA HỘC TỦ #{_optimalSlot?.SoThuTu:D2} ĐÃ BẬT MỞ";
            txtStepDescription.Text = $"Khay #{_optimalSlot?.SoThuTu:D2} đang mở cửa (Sáng đèn xanh ngọc lục bảo bên tủ trái).\nXin mời Quý khách rút pin ({_optimalSlot?.MaPinHienTai}) lắp vào xe và ĐÓNG CỬA TỦ để kết thúc.";

            boxDetails.Visibility = Visibility.Collapsed;
            btnAction.Content = "ĐÃ LẤY PIN VÀ ĐÓNG CỬA TỦ ➔";
        }

        /// <summary>
        /// BƯỚC 6: Khóa chốt an toàn, quyết toán tiền và hoàn tất
        /// </summary>
        private void StartStep6_FinalizeSwap()
        {
            _currentStep = 6;
            HighlightStep(6);

            if (_emptySlot == null || _optimalSlot == null || string.IsNullOrEmpty(_optimalSlot.MaPinHienTai))
            {
                MessageBox.Show("Dữ liệu ngăn tủ không hợp lệ để hoàn tất giao dịch. Giao dịch đã bị hủy.", "Lỗi chu trình", MessageBoxButton.OK, MessageBoxImage.Error);
                OnCancelled?.Invoke();
                return;
            }

            _stationService.SetDoorState(_stationId, _optimalSlot.SoThuTu, false);
            var slotModel = _cabinetSlots.Find(s => s.SoThuTu == _optimalSlot.SoThuTu);
            if (slotModel != null)
            {
                slotModel.CuaDangMo = false;
                slotModel.MaPinHienTai = null;
                slotModel.SoC = 0;
                slotModel.TrangThaiNgan = "Trong";
            }
            RefreshCabinetView();

            txtStepIcon.Text = "💳";
            txtStepTitle.Text = "ĐANG QUYẾT TOÁN VÍ VÀ IN BIÊN LAI...";
            txtStepDescription.Text = "Hệ thống đang trừ tiền dịch vụ và ghi nhận lịch sử giao dịch vào cơ sở dữ liệu an toàn.";
            btnAction.IsEnabled = false;

            // Thực thi giao dịch CSDL an toàn qua SwapService
            var result = _swapService.ExecuteFullSwap(
                _customer,
                _stationId,
                _emptySlot.SoThuTu,
                _oldBatteryId,
                _oldBatterySoC,
                _optimalSlot.SoThuTu,
                _optimalSlot.MaPinHienTai,
                _optimalSlot.SoC
            );

            if (result.Success)
            {
                OnSwapCompleted?.Invoke(result);
            }
            else
            {
                btnAction.IsEnabled = true;
                MessageBox.Show(result.Message, "Lỗi giao dịch", MessageBoxButton.OK, MessageBoxImage.Error);
                OnCancelled?.Invoke();
            }
        }

        private void BtnAction_Click(object sender, RoutedEventArgs e)
        {
            switch (_currentStep)
            {
                case 2:
                    StartStep3_ReadOldBatteryBms();
                    break;
                case 3:
                    StartStep4_FindOptimalBattery();
                    break;
                case 4:
                    StartStep5_DispenseNewBattery();
                    break;
                case 5:
                    StartStep6_FinalizeSwap();
                    break;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Bạn có chắc chắn muốn hủy giao dịch đổi pin?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                // Đảm bảo đóng mọi cửa ngăn tủ nếu đang mở
                if (_emptySlot != null) _stationService.SetDoorState(_stationId, _emptySlot.SoThuTu, false);
                if (_optimalSlot != null) _stationService.SetDoorState(_stationId, _optimalSlot.SoThuTu, false);
                OnCancelled?.Invoke();
            }
        }
    }
}
