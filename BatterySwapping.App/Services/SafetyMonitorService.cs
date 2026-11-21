using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using Microsoft.Data.SqlClient;
using BatterySwapping.App.Helpers;
using BatterySwapping.App.Models;

namespace BatterySwapping.App.Services
{
    /// <summary>
    /// Dịch vụ giám sát an toàn IoT thời gian thực.
    /// Định kỳ quét nhiệt độ cảm biến các ngăn pin, tự động kích hoạt ngắt sạc và ghi nhận cảnh báo CSDL khi quá nhiệt (> 55°C).
    /// </summary>
    public class SafetyMonitorService
    {
        private Timer? _timer;
        private int _isScanning = 0; // Chống chồng chéo luồng Timer (Re-entrancy Guard)
        public event Action<CanhBaoIoT>? OnEmergencyAlertTriggered;

        public const decimal CRITICAL_TEMPERATURE = 55.0m;
        public const decimal WARNING_TEMPERATURE = 50.0m;

        /// <summary>
        /// Bắt đầu tiến trình ngầm giám sát cảm biến định kỳ mỗi intervalMs.
        /// </summary>
        public void StartMonitoring(int intervalMs = 2000)
        {
            if (_timer == null)
            {
                _timer = new Timer(ScanTelemetrySafety, null, 1000, intervalMs);
            }
        }

        /// <summary>
        /// Dừng tiến trình giám sát ngầm an toàn.
        /// </summary>
        public void StopMonitoring()
        {
            _timer?.Dispose();
            _timer = null;
        }

        private void ScanTelemetrySafety(object? state)
        {
            // Kiểm tra re-entrancy: nếu chu kỳ trước chưa kết thúc thì bỏ qua chu kỳ này
            if (Interlocked.CompareExchange(ref _isScanning, 1, 0) != 0)
                return;

            try
            {
                // Kiểm tra các viên pin có nhiệt độ bất thường cần ngắt sạc khẩn cấp
                string sql = @"
                    SELECT TOP 5 
                        ISNULL(nt.MaTram, 'TRAM-BK01') AS MaTram, 
                        ISNULL(nt.SoThuTu, 1) AS MaNgan, 
                        p.MaPin, 
                        p.NhietDo, 
                        p.SoC, 
                        p.TrangThai,
                        p.DongDien
                    FROM dbo.Pin p
                    LEFT JOIN dbo.NganTu nt ON nt.MaPinHienTai = p.MaPin
                    WHERE p.NhietDo >= @CritTemp 
                      AND (p.TrangThai <> N'LoiQuaNhiet' OR p.DongDien > 0.0 OR ISNULL(nt.KhoaBaoTri, 0) = 0)";

                var param = new SqlParameter("@CritTemp", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = CRITICAL_TEMPERATURE };
                DataTable dt = DatabaseHelper.ExecuteDataTable(sql, param);

                foreach (DataRow row in dt.Rows)
                {
                    string maTram = row["MaTram"] != DBNull.Value ? row["MaTram"].ToString()! : "TRAM-BK01";
                    int soThuTu = row["MaNgan"] != DBNull.Value ? Convert.ToInt32(row["MaNgan"]) : 1;
                    string maPin = row["MaPin"] != DBNull.Value ? row["MaPin"].ToString()! : string.Empty;
                    decimal temp = row["NhietDo"] != DBNull.Value ? Convert.ToDecimal(row["NhietDo"]) : CRITICAL_TEMPERATURE;

                    if (!string.IsNullOrEmpty(maPin))
                    {
                        // Kích hoạt ngắt sạc khẩn cấp và cập nhật CSDL
                        EmergencyCutoff(maTram, soThuTu, maPin, temp);
                    }
                }
            }
            catch
            {
                // Bỏ qua lỗi kết nối tạm thời để không làm gián đoạn luồng nền
            }
            finally
            {
                _isScanning = 0;
            }
        }

        /// <summary>
        /// Xử lý ngắt sạc khẩn cấp, khóa ngăn tủ, lưu bản ghi CSDL và phát thông báo sự cố.
        /// </summary>
        public void EmergencyCutoffStation(string maTram)
        {
            using var conn = DatabaseHelper.GetOpenConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                using var cmd = new SqlCommand(@"
                    UPDATE dbo.TramPin SET TrangThaiHoatDong = N'TamDung' WHERE MaTram = @Tram;
                    IF @@ROWCOUNT = 0 THROW 50010, N'Tram khong ton tai.', 1;
                    UPDATE p SET DongDien = 0, NgayCapNhatCuoi = GETDATE()
                    FROM dbo.Pin p JOIN dbo.NganTu n ON n.MaPinHienTai = p.MaPin WHERE n.MaTram = @Tram;
                    UPDATE dbo.NganTu SET KhoaBaoTri = 1, CuaDangMo = 0, NgayCapNhat = GETDATE() WHERE MaTram = @Tram;
                    INSERT dbo.CanhBaoIoT (MaTram, LoaiCanhBao, MucDoNghiemTrong, GhiChu)
                    VALUES (@Tram, N'NgatSacToanTram', N'KhanCap', N'Dung tram va khoa toan bo ngan theo lenh nguoi van hanh');", conn, tx);
                cmd.Parameters.AddWithValue("@Tram", maTram);
                cmd.ExecuteNonQuery();
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
        }

        public void EmergencyCutoff(string maTram, int soThuTu, string maPin, decimal currentTemp)
        {
            try
            {
                int maCB = 0;
                string moTaCanhBao = $"QUÁ NHIỆT PIN ({currentTemp:F1}°C) - ĐÃ TỰ ĐỘNG NGẮT SẠC KHẨN CẤP";
                string ghiChu = "Cảm biến BMS phát hiện nhiệt độ vượt ngưỡng an toàn (55°C). Hệ thống đã tự động ngắt dòng nạp về 0A và khóa khay tủ.";

                // 1. Khóa ngăn tủ, ngắt dòng nạp điện về 0A và lưu cảnh báo vào bảng CanhBaoIoT
                string sqlCutoff = @"
                    -- Ngắt dòng nạp và đổi trạng thái pin sang LoiQuaNhiet
                    UPDATE dbo.Pin 
                    SET TrangThai = N'LoiQuaNhiet', DongDien = 0.0, NgayCapNhatCuoi = GETDATE()
                    WHERE MaPin = @MaPin;

                    -- Khóa ngăn tủ bảo trì an toàn
                    UPDATE dbo.NganTu 
                    SET KhoaBaoTri = 1, TrangThaiNgan = N'Loi', NgayCapNhat = GETDATE()
                    WHERE MaTram = @MaTram AND SoThuTu = @SoThuTu;

                    -- Lưu cảnh báo vào bảng CanhBaoIoT nếu chưa có cảnh báo chưa xử lý
                    IF NOT EXISTS (
                        SELECT 1 FROM dbo.CanhBaoIoT 
                        WHERE MaPin = @MaPin AND DaXuLy = 0
                    )
                    BEGIN
                        INSERT INTO dbo.CanhBaoIoT (MaTram, MaNgan, MaPin, LoaiCanhBao, GiaTriDo, MucDoNghiemTrong, DaXuLy, GhiChu)
                        VALUES (@MaTram, @SoThuTu, @MaPin, @LoaiCanhBao, @GiaTriDo, N'KhanCap', 0, @GhiChu);
                        SELECT CAST(SCOPE_IDENTITY() AS INT);
                    END
                    ELSE
                    BEGIN
                        SELECT TOP 1 MaCB FROM dbo.CanhBaoIoT WHERE MaPin = @MaPin AND DaXuLy = 0 ORDER BY MaCB DESC;
                    END";

                var p1 = new SqlParameter("@MaTram", SqlDbType.VarChar, 20) { Value = maTram };
                var p2 = new SqlParameter("@SoThuTu", SqlDbType.Int) { Value = soThuTu };
                var p3 = new SqlParameter("@MaPin", SqlDbType.VarChar, 20) { Value = maPin };
                var p4 = new SqlParameter("@LoaiCanhBao", SqlDbType.NVarChar, 100) { Value = moTaCanhBao };
                var p5 = new SqlParameter("@GiaTriDo", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = currentTemp };
                var p6 = new SqlParameter("@GhiChu", SqlDbType.NVarChar, 255) { Value = ghiChu };

                object? resId = DatabaseHelper.ExecuteScalar(sqlCutoff, p1, p2, p3, p4, p5, p6);
                if (resId != null && resId != DBNull.Value)
                {
                    maCB = Convert.ToInt32(resId);
                }

                // 2. Tạo đối tượng cảnh báo đầy đủ và gửi sự kiện tới UI
                var alert = new CanhBaoIoT
                {
                    MaCB = maCB,
                    MaTram = maTram,
                    MaNgan = soThuTu,
                    MaPin = maPin,
                    LoaiCanhBao = moTaCanhBao,
                    GiaTriDo = currentTemp,
                    ThoiGianPhatHien = DateTime.Now,
                    MucDoNghiemTrong = "KhanCap",
                    DaXuLy = false,
                    GhiChu = ghiChu
                };

                OnEmergencyAlertTriggered?.Invoke(alert);
            }
            catch
            {
                // Safe ignore lỗi thứ cấp để bảo vệ chu trình chính
            }
        }

        /// <summary>
        /// Lấy toàn bộ danh sách cảnh báo IoT kèm xử lý an toàn giá trị NULL.
        /// </summary>
        public List<CanhBaoIoT> GetAlerts(bool onlyUnresolved = false)
        {
            var list = new List<CanhBaoIoT>();
            string sql = @"
                SELECT MaCB, MaTram, MaNgan, MaPin, LoaiCanhBao, GiaTriDo, ThoiGianPhatHien, MucDoNghiemTrong, DaXuLy, NguoiXuLy, ThoiGianXuLy, GhiChu
                FROM dbo.CanhBaoIoT " +
                (onlyUnresolved ? "WHERE DaXuLy = 0 " : "") +
                "ORDER BY ThoiGianPhatHien DESC";

            DataTable dt = DatabaseHelper.ExecuteDataTable(sql);
            foreach (DataRow row in dt.Rows)
            {
                list.Add(new CanhBaoIoT
                {
                    MaCB = Convert.ToInt32(row["MaCB"]),
                    MaTram = row["MaTram"] != DBNull.Value ? row["MaTram"].ToString()! : string.Empty,
                    MaNgan = row["MaNgan"] != DBNull.Value ? Convert.ToInt32(row["MaNgan"]) : null,
                    MaPin = row["MaPin"] != DBNull.Value ? row["MaPin"].ToString() : null,
                    LoaiCanhBao = row["LoaiCanhBao"] != DBNull.Value ? row["LoaiCanhBao"].ToString()! : string.Empty,
                    GiaTriDo = row["GiaTriDo"] != DBNull.Value ? Convert.ToDecimal(row["GiaTriDo"]) : null,
                    ThoiGianPhatHien = row["ThoiGianPhatHien"] != DBNull.Value ? Convert.ToDateTime(row["ThoiGianPhatHien"]) : DateTime.Now,
                    MucDoNghiemTrong = row["MucDoNghiemTrong"] != DBNull.Value ? row["MucDoNghiemTrong"].ToString()! : "CanhBao",
                    DaXuLy = row["DaXuLy"] != DBNull.Value && Convert.ToBoolean(row["DaXuLy"]),
                    NguoiXuLy = row["NguoiXuLy"] != DBNull.Value ? row["NguoiXuLy"].ToString() : null,
                    ThoiGianXuLy = row["ThoiGianXuLy"] != DBNull.Value ? Convert.ToDateTime(row["ThoiGianXuLy"]) : null,
                    GhiChu = row["GhiChu"] != DBNull.Value ? row["GhiChu"].ToString() : null
                });
            }
            return list;
        }

        /// <summary>
        /// Xác nhận đã khắc phục sự cố an toàn IoT, mở khóa khay tủ và khôi phục pin về trạng thái sẵn sàng.
        /// </summary>
        /// <param name="maCB">Mã định danh cảnh báo.</param>
        /// <param name="nguoiXuLy">Tên kỹ thuật viên hoặc quản trị viên xử lý.</param>
        /// <param name="ghiChu">Ghi chú khắc phục sự cố.</param>
        /// <param name="moKhoaNganTu">Tùy chọn mở khóa ngăn tủ (mặc định true).</param>
        /// <returns>True nếu cập nhật thành công; ngược lại False.</returns>
        public bool ResolveAlert(int maCB, string nguoiXuLy, string? ghiChu, bool moKhoaNganTu = true)
        {
            if (maCB <= 0) return false;

            try
            {
                var p1 = new SqlParameter("@MaCB", SqlDbType.Int) { Value = maCB };
                var p2 = new SqlParameter("@NguoiXuLy", SqlDbType.NVarChar, 100) { Value = string.IsNullOrWhiteSpace(nguoiXuLy) ? "Kỹ thuật viên" : nguoiXuLy };
                var p3 = new SqlParameter("@GhiChu", SqlDbType.NVarChar, 255) { Value = (object?)ghiChu ?? "Đã kiểm tra an toàn và xử lý hoàn tất" };
                var p4 = new SqlParameter("@MoKhoaNganTu", SqlDbType.Bit) { Value = moKhoaNganTu };

                DatabaseHelper.ExecuteStoredProcedureNonQuery("dbo.sp_GiaiQuyetCanhBaoAnToan", p1, p2, p3, p4);
                return true;
            }
            catch
            {
                // Fallback nếu chưa đăng ký Procedure
                try
                {
                    string sql = @"
                        UPDATE dbo.CanhBaoIoT 
                        SET DaXuLy = 1, NguoiXuLy = @NguoiXuLy, ThoiGianXuLy = GETDATE(), GhiChu = @GhiChu
                        WHERE MaCB = @MaCB;

                        IF @MoKhoa = 1
                        BEGIN
                            UPDATE nt
                            SET nt.KhoaBaoTri = 0,
                                nt.TrangThaiNgan = CASE WHEN nt.MaPinHienTai IS NULL THEN N'Trong' ELSE N'CoPin' END,
                                nt.NgayCapNhat = GETDATE()
                            FROM dbo.NganTu nt
                            INNER JOIN dbo.CanhBaoIoT cb ON cb.MaTram = nt.MaTram AND cb.MaNgan = nt.SoThuTu
                            WHERE cb.MaCB = @MaCB;

                            UPDATE p
                            SET p.NhietDo = 26.0,
                                p.TrangThai = CASE WHEN p.SoC >= 90.0 THEN N'DaDay' ELSE N'DangSac' END,
                                p.DongDien = CASE WHEN p.SoC >= 100.0 THEN 0.0 ELSE 15.0 END,
                                p.NgayCapNhatCuoi = GETDATE()
                            FROM dbo.Pin p
                            INNER JOIN dbo.CanhBaoIoT cb ON cb.MaPin = p.MaPin
                            WHERE cb.MaCB = @MaCB AND p.TrangThai = N'LoiQuaNhiet';
                        END";

                    var p1 = new SqlParameter("@MaCB", SqlDbType.Int) { Value = maCB };
                    var p2 = new SqlParameter("@NguoiXuLy", SqlDbType.NVarChar, 100) { Value = string.IsNullOrWhiteSpace(nguoiXuLy) ? "Kỹ thuật viên" : nguoiXuLy };
                    var p3 = new SqlParameter("@GhiChu", SqlDbType.NVarChar, 255) { Value = (object?)ghiChu ?? DBNull.Value };
                    var p4 = new SqlParameter("@MoKhoa", SqlDbType.Bit) { Value = moKhoaNganTu };

                    return DatabaseHelper.ExecuteNonQuery(sql, p1, p2, p3, p4) > 0;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
