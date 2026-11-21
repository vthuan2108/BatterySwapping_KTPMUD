using System;
using System.Collections.Generic;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Data.SqlClient;

namespace BatterySwapping.Simulator
{
    public class SimulatorSlotModel
    {
        public int SoThuTu { get; set; }
        public string? MaPinHienTai { get; set; }
        public decimal SoC { get; set; }
        public decimal NhietDo { get; set; }
        public bool CuaDangMo { get; set; }
        public bool KhoaBaoTri { get; set; }

        public string CuaText => CuaDangMo ? "CỬA MỞ" : "CỬA KHÓA";
        public string CuaBackground => CuaDangMo ? "#3B82F6" : "#475569";
        public string KhoaText => KhoaBaoTri ? "BẢO TRÌ" : "HOẠT ĐỘNG";
        public string KhoaBackground => KhoaBaoTri ? "#DC2626" : "#059669";
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string _connString = AutoDetectConnectionString();
        private readonly string _stationId = "TRAM-BK01";
        private SimulatorWorker _worker;
        private DispatcherTimer? _pollTimer;

        public static string AutoDetectConnectionString()
        {
            string[] candidateServers = { @".\SQLEXPRESS", "localhost", @"(localdb)\mssqllocaldb" };
            foreach (var server in candidateServers)
            {
                string testCs = $"Server={server};Database=BatterySwappingDB;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=2;";
                try
                {
                    using var conn = new SqlConnection(testCs);
                    conn.Open();
                    return $"Server={server};Database=BatterySwappingDB;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15;";
                }
                catch { }
            }
            return @"Server=.\SQLEXPRESS;Database=BatterySwappingDB;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15;";
        }

        private int _selectedSlotNumber = 1;
        private string? _selectedPinId = "PIN-001";
        private List<SimulatorSlotModel> _slots = new();

        public MainWindow()
        {
            InitializeComponent();
            _worker = new SimulatorWorker(_connString);
            _worker.OnLogMessage += Worker_OnLogMessage;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Log("Đã mở Bảng điều khiển giả lập trạm IoT BSS.");
            RefreshSlots();

            // Quét định kỳ mỗi 2 giây để đồng bộ trạng thái cửa tủ nếu App bên kia mở cửa
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _pollTimer.Tick += (s, ev) => RefreshSlots(silent: true);
            _pollTimer.Start();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _worker.OnLogMessage -= Worker_OnLogMessage;
            _worker.Stop();
            _pollTimer?.Stop();
        }

        private void Worker_OnLogMessage(string msg)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            try
            {
                Dispatcher.Invoke(() => Log(msg));
            }
            catch { }
        }

        private void Log(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Log(message));
                return;
            }
            if (txtLog.Text.Length > 30000)
            {
                txtLog.Text = txtLog.Text.Substring(txtLog.Text.Length - 15000);
            }
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            txtLog.AppendText(line + Environment.NewLine);
            txtLog.ScrollToEnd();
        }

        private void RefreshSlots(bool silent = false)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => RefreshSlots(silent));
                return;
            }
            try
            {
                using var conn = new SqlConnection(_connString);
                conn.Open();

                string sql = @"
                    SELECT nt.SoThuTu, nt.MaPinHienTai, nt.CuaDangMo, nt.KhoaBaoTri,
                           ISNULL(p.SoC, 0.0) AS SoC, ISNULL(p.NhietDo, 25.0) AS NhietDo
                    FROM dbo.NganTu nt
                    LEFT JOIN dbo.Pin p ON nt.MaPinHienTai = p.MaPin
                    WHERE nt.MaTram = @MaTram
                    ORDER BY nt.SoThuTu ASC";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@MaTram", _stationId);

                using var reader = cmd.ExecuteReader();
                var list = new List<SimulatorSlotModel>();
                while (reader.Read())
                {
                    list.Add(new SimulatorSlotModel
                    {
                        SoThuTu = reader.GetInt32(0),
                        MaPinHienTai = reader.IsDBNull(1) ? null : reader.GetString(1),
                        CuaDangMo = reader.GetBoolean(2),
                        KhoaBaoTri = reader.GetBoolean(3),
                        SoC = reader.GetDecimal(4),
                        NhietDo = reader.GetDecimal(5)
                    });
                }

                _slots = list;
                icSlots.ItemsSource = null;
                icSlots.ItemsSource = _slots;

                // Cập nhật slot đang chọn
                var current = _slots.Find(s => s.SoThuTu == _selectedSlotNumber);
                if (current != null)
                {
                    _selectedPinId = current.MaPinHienTai;
                    lblSelectedSlotTitle.Text = $"ĐIỀU KHIỂN CAN THIỆP: NGĂN TỦ #{current.SoThuTu} (Pin: {current.MaPinHienTai ?? "Trống"})";
                    btnToggleInsert.Content = string.IsNullOrEmpty(current.MaPinHienTai) ? "Cắm pin vào khay" : "Rút pin khỏi khay";
                    btnToggleDoor.Content = current.CuaDangMo ? "Đóng cửa khay" : "Mở cửa khay";
                }

                if (!silent) Log($"Đã đồng bộ thông số 8 ngăn tủ của trạm {_stationId}.");
            }
            catch (Exception ex)
            {
                if (!silent) Log($"[Lỗi kết nối CSDL]: {ex.Message}");
            }
        }

        private void SlotBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && int.TryParse(b.Tag?.ToString(), out int slotNum))
            {
                _selectedSlotNumber = slotNum;
                var current = _slots.Find(s => s.SoThuTu == slotNum);
                if (current != null)
                {
                    _selectedPinId = current.MaPinHienTai;
                    sliderTemp.Value = (double)current.NhietDo;
                    sliderSoc.Value = (double)current.SoC;
                    lblSelectedSlotTitle.Text = $"ĐIỀU KHIỂN CAN THIỆP: NGĂN TỦ #{slotNum} (Pin: {current.MaPinHienTai ?? "Trống"})";
                    btnToggleInsert.Content = string.IsNullOrEmpty(current.MaPinHienTai) ? "Cắm pin vào khay" : "Rút pin khỏi khay";
                    btnToggleDoor.Content = current.CuaDangMo ? "Đóng cửa khay" : "Mở cửa khay";
                    Log($"Đã chọn can thiệp ngăn tủ #{slotNum}.");
                }
            }
        }

        private void SliderTemp_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (lblTempSliderVal != null)
            {
                lblTempSliderVal.Text = $"{e.NewValue:F1} °C";
            }
        }

        private void SliderSoc_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (lblSocSliderVal != null)
            {
                lblSocSliderVal.Text = $"{e.NewValue:F0} %";
            }
        }

        private void BtnApplyParams_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedPinId))
            {
                MessageBox.Show("Ngăn tủ này đang TRỐNG (chưa cắm pin)! Vui lòng cắm pin trước khi chỉnh nhiệt độ/SoC.", 
                                "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var conn = new SqlConnection(_connString);
                conn.Open();

                string sql = "UPDATE dbo.Pin SET NhietDo = @Temp, SoC = @SoC, NgayCapNhatCuoi = GETDATE() WHERE MaPin = @MaPin";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Temp", (decimal)sliderTemp.Value);
                cmd.Parameters.AddWithValue("@SoC", (decimal)sliderSoc.Value);
                cmd.Parameters.AddWithValue("@MaPin", _selectedPinId);

                cmd.ExecuteNonQuery();
                Log($"[Ghi thông số]: Đã cập nhật pin {_selectedPinId} -> Nhiệt độ: {sliderTemp.Value:F1}°C, SoC: {sliderSoc.Value:F0}%.");
                RefreshSlots(silent: true);
            }
            catch (Exception ex)
            {
                Log($"[Lỗi áp dụng]: {ex.Message}");
            }
        }

        private void BtnToggleInsert_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var conn = new SqlConnection(_connString);
                conn.Open();

                var current = _slots.Find(s => s.SoThuTu == _selectedSlotNumber);
                if (current == null) return;

                if (string.IsNullOrEmpty(current.MaPinHienTai))
                {
                    // Tìm một viên pin khả dụng ngoài trạm (chưa cắm vào bất kỳ ngăn tủ nào)
                    string sqlFind = @"
                        SELECT TOP 1 p.MaPin 
                        FROM dbo.Pin p 
                        LEFT JOIN dbo.NganTu nt ON p.MaPin = nt.MaPinHienTai 
                        WHERE nt.MaPinHienTai IS NULL 
                          AND p.TrangThai NOT IN (N'DangSuDung', N'LoiQuaNhiet')
                        ORDER BY p.MaPin ASC";
                    using var cmdFind = new SqlCommand(sqlFind, conn);
                    var pinObj = cmdFind.ExecuteScalar();
                    string pinToInsert = pinObj?.ToString() ?? "PIN-011";

                    string sqlInsert = @"
                        UPDATE dbo.NganTu SET MaPinHienTai = @Pin, TrangThaiNgan = N'CoPin', NgayCapNhat = GETDATE()
                        WHERE MaTram = @MaTram AND SoThuTu = @Slot;
                        UPDATE dbo.Pin SET TrangThai = N'KhaDung' WHERE MaPin = @Pin;";
                    using var cmd = new SqlCommand(sqlInsert, conn);
                    cmd.Parameters.AddWithValue("@Pin", pinToInsert);
                    cmd.Parameters.AddWithValue("@MaTram", _stationId);
                    cmd.Parameters.AddWithValue("@Slot", _selectedSlotNumber);
                    cmd.ExecuteNonQuery();

                    Log($"[Hardware] Đã cắm pin {pinToInsert} vào khay #{_selectedSlotNumber}.");
                }
                else
                {
                    // Rút pin ra khỏi khay
                    string oldPin = current.MaPinHienTai;
                    string sqlRemove = @"
                        UPDATE dbo.NganTu SET MaPinHienTai = NULL, TrangThaiNgan = N'Trong', NgayCapNhat = GETDATE()
                        WHERE MaTram = @MaTram AND SoThuTu = @Slot;";
                    using var cmd = new SqlCommand(sqlRemove, conn);
                    cmd.Parameters.AddWithValue("@MaTram", _stationId);
                    cmd.Parameters.AddWithValue("@Slot", _selectedSlotNumber);
                    cmd.ExecuteNonQuery();

                    Log($"[Hardware] Đã rút pin {oldPin} ra khỏi khay #{_selectedSlotNumber}.");
                }

                RefreshSlots();
            }
            catch (Exception ex)
            {
                Log($"[Lỗi cắm/rút pin]: {ex.Message}");
            }
        }

        private void BtnToggleDoor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var conn = new SqlConnection(_connString);
                conn.Open();

                var current = _slots.Find(s => s.SoThuTu == _selectedSlotNumber);
                if (current == null) return;

                bool nextState = !current.CuaDangMo;
                string sql = "UPDATE dbo.NganTu SET CuaDangMo = @Cua, NgayCapNhat = GETDATE() WHERE MaTram = @MaTram AND SoThuTu = @Slot";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Cua", nextState);
                cmd.Parameters.AddWithValue("@MaTram", _stationId);
                cmd.Parameters.AddWithValue("@Slot", _selectedSlotNumber);
                cmd.ExecuteNonQuery();

                Log($"[Hardware] Cửa khay #{_selectedSlotNumber} đã chuyển sang: {(nextState ? "MỞ" : "ĐÓNG")}.");
                RefreshSlots();
            }
            catch (Exception ex)
            {
                Log($"[Lỗi mở/đóng cửa]: {ex.Message}");
            }
        }

        private void BtnEmergencyHeat_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedPinId))
            {
                MessageBox.Show("Khay này chưa có pin để giả lập quá nhiệt! Hãy chọn khay đã có pin (ví dụ khay #1 hoặc #4).", 
                                "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var conn = new SqlConnection(_connString);
                conn.Open();

                // Đẩy nhiệt độ lên 65°C -> Trigger SQL Server sẽ tự động kích hoạt, 
                // và SafetyMonitorService bên ứng dụng chính sẽ ngắt sạc và báo động đỏ ngay lập tức!
                string sql = @"
                    UPDATE dbo.Pin 
                    SET NhietDo = 65.0, NgayCapNhatCuoi = GETDATE() 
                    WHERE MaPin = @MaPin";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@MaPin", _selectedPinId);
                cmd.ExecuteNonQuery();

                sliderTemp.Value = 65.0;
                Log($"🚨 [SỰ CỐ KHẨN CẤP] Đã đẩy nhiệt độ pin {_selectedPinId} lên 65.0 °C! Kiểm tra màn hình ứng dụng chính để xem phản ứng ngắt sạc tự động.");
                RefreshSlots();
            }
            catch (Exception ex)
            {
                Log($"[Lỗi giả lập quá nhiệt]: {ex.Message}");
            }
        }

        private void ChkAutoCharge_Checked(object sender, RoutedEventArgs e)
        {
            _worker.Start();
        }

        private void ChkAutoCharge_Unchecked(object sender, RoutedEventArgs e)
        {
            _worker.Stop();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshSlots();
        }
    }
}
