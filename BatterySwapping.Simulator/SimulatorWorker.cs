using System;
using System.Data;
using System.Threading;
using Microsoft.Data.SqlClient;

namespace BatterySwapping.Simulator
{
    /// <summary>
    /// Tiến trình nền tự động giả lập chu trình sạc xả và sinh thông số Telemetry cho pin tại trạm.
    /// </summary>
    public class SimulatorWorker
    {
        private Timer? _chargingTimer;
        private int _isCharging;
        private readonly string _connectionString;
        public bool IsRunning { get; private set; }

        public event Action<string>? OnLogMessage;

        public SimulatorWorker(string connectionString)
        {
            _connectionString = connectionString;
        }

        public void Start()
        {
            if (IsRunning) return;
            IsRunning = true;
            // Kích hoạt chu kỳ sạc mỗi 3 giây
            _chargingTimer = new Timer(ChargingCycleTick, null, 1000, 3000);
            OnLogMessage?.Invoke("Tiến trình giả lập sạc tự động đã KHỞI ĐỘNG (chu kỳ 3s/lần).");
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            _chargingTimer?.Dispose();
            _chargingTimer = null;
            OnLogMessage?.Invoke("Tiến trình giả lập sạc tự động đã DỪNG.");
        }

        private void ChargingCycleTick(object? state)
        {
            if (!IsRunning || Interlocked.CompareExchange(ref _isCharging, 1, 0) != 0) return;
            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();

                // Tự động sạc pin trong các ngăn tủ đang hoạt động (không bị khóa bảo trì).
                // Tiếp tục sạc cho đến khi đạt 100.0%. Khi đạt >= 90.0%, pin được đánh dấu 'DaDay' (sẵn sàng cấp cho khách).
                // Khi đạt 100.0%, ngắt dòng sạc (DongDien = 0.0A) và nhiệt độ bắt đầu hạ dần về nhiệt độ phòng.
                string sqlUpdate = @"
                    UPDATE p
                    SET 
                        p.SoC = CASE WHEN p.SoC + 1.0 >= 100.0 THEN 100.0 ELSE p.SoC + 1.0 END,
                        p.NhietDo = CASE 
                            WHEN p.SoC + 1.0 >= 100.0 AND p.NhietDo > 26.0 THEN p.NhietDo - 0.2
                            WHEN p.NhietDo < 36.0 THEN p.NhietDo + 0.1 
                            ELSE p.NhietDo 
                        END,
                        p.DongDien = CASE WHEN p.SoC + 1.0 >= 100.0 THEN 0.0 ELSE 15.0 END,
                        p.TrangThai = CASE WHEN p.SoC + 1.0 >= 90.0 THEN N'DaDay' ELSE N'DangSac' END,
                        p.NgayCapNhatCuoi = GETDATE()
                    FROM dbo.Pin p
                    INNER JOIN dbo.NganTu nt ON nt.MaPinHienTai = p.MaPin
                    WHERE (p.TrangThai = N'DangSac' OR (p.TrangThai = N'DaDay' AND p.SoC < 100.0))
                      AND nt.KhoaBaoTri = 0 
                      AND p.NhietDo < 50.0;";

                using var cmd = new SqlCommand(sqlUpdate, conn);
                int rows = cmd.ExecuteNonQuery();

                if (rows > 0)
                {
                    OnLogMessage?.Invoke($"[AutoCharge] Đã nạp thêm +1% pin cho {rows} viên pin đang sạc trong tủ.");
                }
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"[Lỗi Sim]: {ex.Message}");
            }
            finally { Interlocked.Exchange(ref _isCharging, 0); }
        }
    }
}
