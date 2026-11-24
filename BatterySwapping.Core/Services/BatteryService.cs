using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using BatterySwapping.Core.Helpers;
using BatterySwapping.Contracts.Models;

namespace BatterySwapping.Core.Services
{
    /// <summary>
    /// Kết quả đánh giá sức khỏe và thông số kỹ thuật BMS của viên pin.
    /// </summary>
    public class BatteryInspectionResult
    {
        public Pin Battery { get; set; } = new();
        public bool IsEligibleForSwap { get; set; }
        public bool IsOverheated { get; set; }
        public bool IsDegraded { get; set; } // SoH < 80% (chai pin)
        public string HealthStatusDescription { get; set; } = string.Empty;
        public string RecommendedAction { get; set; } = string.Empty;
    }

    /// <summary>
    /// Dịch vụ quản lý danh mục Pin, kiểm tra các thông số kỹ thuật BMS, và cung cấp các nghiệp vụ bảo trì, thay thế pin dành cho Kỹ thuật viên.
    /// </summary>
    public class BatteryService
    {
        /// <summary>
        /// Lấy toàn bộ danh sách pin trong hệ thống kèm xử lý an toàn giá trị NULL.
        /// </summary>
        /// <returns>Danh sách các đối tượng Pin trong CSDL.</returns>
        public List<Pin> GetAllBatteries()
        {
            var list = new List<Pin>();
            string sql = "SELECT MaPin, LoaiPin, DungLuongThietKe, SoC, SoH, NhietDo, DienAp, DongDien, SoLanSac, TrangThai, NgayCapNhatCuoi FROM dbo.Pin ORDER BY MaPin";
            
            DataTable dt = DatabaseHelper.ExecuteDataTable(sql);
            foreach (DataRow row in dt.Rows)
            {
                list.Add(new Pin
                {
                    MaPin = row["MaPin"] != DBNull.Value ? row["MaPin"].ToString()! : string.Empty,
                    LoaiPin = row["LoaiPin"] != DBNull.Value ? row["LoaiPin"].ToString()! : "Lithium LFP 72V-30Ah",
                    DungLuongThietKe = row["DungLuongThietKe"] != DBNull.Value ? Convert.ToInt32(row["DungLuongThietKe"]) : 30,
                    SoC = row["SoC"] != DBNull.Value ? Convert.ToDecimal(row["SoC"]) : 0.0m,
                    SoH = row["SoH"] != DBNull.Value ? Convert.ToDecimal(row["SoH"]) : 0.0m,
                    NhietDo = row["NhietDo"] != DBNull.Value ? Convert.ToDecimal(row["NhietDo"]) : 25.0m,
                    DienAp = row["DienAp"] != DBNull.Value ? Convert.ToDecimal(row["DienAp"]) : 72.0m,
                    DongDien = row["DongDien"] != DBNull.Value ? Convert.ToDecimal(row["DongDien"]) : 0.0m,
                    SoLanSac = row["SoLanSac"] != DBNull.Value ? Convert.ToInt32(row["SoLanSac"]) : 0,
                    TrangThai = row["TrangThai"] != DBNull.Value ? row["TrangThai"].ToString()! : "KhaDung",
                    NgayCapNhatCuoi = row["NgayCapNhatCuoi"] != DBNull.Value ? Convert.ToDateTime(row["NgayCapNhatCuoi"]) : DateTime.Now
                });
            }
            return list;
        }

        /// <summary>
        /// Lấy thông tin chi tiết một viên pin theo mã định danh.
        /// </summary>
        /// <param name="maPin">Mã định danh viên pin (ví dụ: PIN-001).</param>
        /// <returns>Đối tượng Pin hoặc null nếu không tìm thấy.</returns>
        public Pin? GetBatteryById(string maPin)
        {
            if (string.IsNullOrWhiteSpace(maPin)) return null;

            string sql = "SELECT MaPin, LoaiPin, DungLuongThietKe, SoC, SoH, NhietDo, DienAp, DongDien, SoLanSac, TrangThai, NgayCapNhatCuoi FROM dbo.Pin WHERE MaPin = @MaPin";
            var param = new SqlParameter("@MaPin", SqlDbType.VarChar, 20) { Value = maPin.Trim() };
            DataTable dt = DatabaseHelper.ExecuteDataTable(sql, param);

            if (dt.Rows.Count == 0) return null;

            var row = dt.Rows[0];
            return new Pin
            {
                MaPin = row["MaPin"] != DBNull.Value ? row["MaPin"].ToString()! : string.Empty,
                LoaiPin = row["LoaiPin"] != DBNull.Value ? row["LoaiPin"].ToString()! : "Lithium LFP 72V-30Ah",
                DungLuongThietKe = row["DungLuongThietKe"] != DBNull.Value ? Convert.ToInt32(row["DungLuongThietKe"]) : 30,
                SoC = row["SoC"] != DBNull.Value ? Convert.ToDecimal(row["SoC"]) : 0.0m,
                SoH = row["SoH"] != DBNull.Value ? Convert.ToDecimal(row["SoH"]) : 0.0m,
                NhietDo = row["NhietDo"] != DBNull.Value ? Convert.ToDecimal(row["NhietDo"]) : 25.0m,
                DienAp = row["DienAp"] != DBNull.Value ? Convert.ToDecimal(row["DienAp"]) : 72.0m,
                DongDien = row["DongDien"] != DBNull.Value ? Convert.ToDecimal(row["DongDien"]) : 0.0m,
                SoLanSac = row["SoLanSac"] != DBNull.Value ? Convert.ToInt32(row["SoLanSac"]) : 0,
                TrangThai = row["TrangThai"] != DBNull.Value ? row["TrangThai"].ToString()! : "KhaDung",
                NgayCapNhatCuoi = row["NgayCapNhatCuoi"] != DBNull.Value ? Convert.ToDateTime(row["NgayCapNhatCuoi"]) : DateTime.Now
            };
        }

        /// <summary>
        /// Đánh giá toàn diện sức khỏe pin BMS (SoC, SoH, nhiệt độ, chu kỳ sạc) phục vụ kỹ thuật viên kiểm định.
        /// </summary>
        /// <param name="maPin">Mã định danh viên pin.</param>
        /// <returns>Đối tượng kết quả đánh giá sức khỏe pin.</returns>
        public BatteryInspectionResult? InspectBattery(string maPin)
        {
            var pin = GetBatteryById(maPin);
            if (pin == null) return null;

            var result = new BatteryInspectionResult
            {
                Battery = pin,
                IsOverheated = pin.NhietDo >= 55.0m || pin.TrangThai == "LoiQuaNhiet",
                IsDegraded = pin.SoH < 80.0m,
                IsEligibleForSwap = pin.SoC >= 90.0m && pin.SoH >= 80.0m && pin.NhietDo <= 45.0m && pin.TrangThai != "Loi" && pin.TrangThai != "LoiQuaNhiet"
            };

            if (result.IsOverheated)
            {
                result.HealthStatusDescription = $"CẢNH BÁO NGUY HIỂM: Pin quá nhiệt ({pin.NhietDo:F1}°C >= 55°C)! Đã kích hoạt ngắt sạc an toàn.";
                result.RecommendedAction = "Kỹ thuật viên cần kiểm tra hệ thống quạt tản nhiệt, chờ làm mát về dưới 35°C và chạy kiểm tra trước khi khôi phục sạc.";
            }
            else if (result.IsDegraded)
            {
                result.HealthStatusDescription = $"CẢNH BÁO CHAI PIN: Sức khỏe pin suy giảm ({pin.SoH:F1}% < 80%) sau {pin.SoLanSac} chu kỳ sạc xả.";
                result.RecommendedAction = "Khuyến nghị thu hồi và thay thế viên pin mới để đảm bảo cự ly hành trình xe cho khách hàng.";
            }
            else if (result.IsEligibleForSwap)
            {
                result.HealthStatusDescription = $"TRẠNG THÁI TỐT: Pin đạt chuẩn đổi ({pin.SoC:F1}% SoC, {pin.SoH:F1}% SoH, {pin.NhietDo:F1}°C).";
                result.RecommendedAction = "Pin sẵn sàng nhả cho tài xế xe điện.";
            }
            else
            {
                result.HealthStatusDescription = $"ĐANG SẠC / CHỜ SẠC: Dung lượng hiện tại ({pin.SoC:F1}%), nhiệt độ ổn định ({pin.NhietDo:F1}°C).";
                result.RecommendedAction = "Tiếp tục duy trì dòng sạc trong khay tủ cho tới khi đạt ngưỡng >= 90%.";
            }

            return result;
        }

        /// <summary>
        /// Thao tác kỹ thuật viên: Thực hiện bảo dưỡng pin tại chỗ (làm mát, reset trạng thái quá nhiệt, mở khóa ngăn tủ).
        /// </summary>
        /// <param name="maTram">Mã trạm sạc.</param>
        /// <param name="soThuTu">Số thứ tự khay tủ (1..8).</param>
        /// <param name="maPin">Mã viên pin.</param>
        /// <param name="technicianName">Họ tên kỹ thuật viên thực hiện.</param>
        /// <param name="note">Ghi chú nhật ký kỹ thuật.</param>
        /// <param name="errorMessage">Thông điệp lỗi nếu có.</param>
        /// <returns>True nếu bảo dưỡng thành công; ngược lại False.</returns>
        public bool MaintainBattery(string maTram, int soThuTu, string maPin, string technicianName, string note, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                var p1 = new SqlParameter("@MaTram", SqlDbType.VarChar, 20) { Value = maTram };
                var p2 = new SqlParameter("@SoThuTu", SqlDbType.Int) { Value = soThuTu };
                var p3 = new SqlParameter("@MaPinCu", SqlDbType.VarChar, 20) { Value = maPin };
                var p4 = new SqlParameter("@MaPinMoi", SqlDbType.VarChar, 20) { Value = DBNull.Value };
                var p5 = new SqlParameter("@NguoiThucHien", SqlDbType.NVarChar, 100) { Value = technicianName };
                var p6 = new SqlParameter("@GhiChu", SqlDbType.NVarChar, 255) { Value = string.IsNullOrWhiteSpace(note) ? "Bảo dưỡng kỹ thuật hoàn tất" : note };

                DatabaseHelper.ExecuteStoredProcedureNonQuery("dbo.sp_BaoTriThayThePin", p1, p2, p3, p4, p5, p6);
                return true;
            }
            catch (Exception)
            {
                // Fallback nếu chưa đăng ký Procedure
                try
                {
                    string sqlUpdate = @"
                        UPDATE dbo.Pin 
                        SET NhietDo = 25.00,
                            TrangThai = CASE WHEN SoC >= 90.0 THEN N'DaDay' ELSE N'DangSac' END,
                            DongDien = CASE WHEN SoC >= 100.0 THEN 0.0 ELSE 15.0 END,
                            NgayCapNhatCuoi = GETDATE()
                        WHERE MaPin = @MaPin;

                        UPDATE dbo.NganTu 
                        SET KhoaBaoTri = 0,
                            TrangThaiNgan = CASE WHEN SoC >= 90.0 THEN N'CoPin' ELSE N'DangSac' END,
                            NgayCapNhat = GETDATE()
                        FROM dbo.NganTu nt
                        INNER JOIN dbo.Pin p ON nt.MaPinHienTai = p.MaPin
                        WHERE nt.MaTram = @MaTram AND nt.SoThuTu = @SoThuTu;

                        UPDATE dbo.CanhBaoIoT 
                        SET DaXuLy = 1, NguoiXuLy = @Tech, ThoiGianXuLy = GETDATE(), GhiChu = @Note
                        WHERE MaPin = @MaPin AND DaXuLy = 0;";

                    var pPin = new SqlParameter("@MaPin", SqlDbType.VarChar, 20) { Value = maPin };
                    var pTram = new SqlParameter("@MaTram", SqlDbType.VarChar, 20) { Value = maTram };
                    var pSlot = new SqlParameter("@SoThuTu", SqlDbType.Int) { Value = soThuTu };
                    var pTech = new SqlParameter("@Tech", SqlDbType.NVarChar, 100) { Value = technicianName };
                    var pNote = new SqlParameter("@Note", SqlDbType.NVarChar, 255) { Value = note };

                    DatabaseHelper.ExecuteNonQuery(sqlUpdate, pPin, pTram, pSlot, pTech, pNote);
                    return true;
                }
                catch (Exception fallbackEx)
                {
                    errorMessage = fallbackEx.Message;
                    return false;
                }
            }
        }

        /// <summary>
        /// Thao tác kỹ thuật viên: Thu hồi viên pin chai/hỏng và thay thế bằng viên pin mới đạt chuẩn 100% trong khay tủ.
        /// </summary>
        /// <param name="maTram">Mã trạm sạc.</param>
        /// <param name="soThuTu">Số thứ tự khay tủ.</param>
        /// <param name="oldPinId">Mã viên pin cũ cần thu hồi.</param>
        /// <param name="newPinId">Mã viên pin mới thay thế vào.</param>
        /// <param name="technicianName">Họ tên kỹ thuật viên thực hiện.</param>
        /// <param name="note">Ghi chú nhật ký kỹ thuật.</param>
        /// <param name="errorMessage">Thông điệp lỗi nếu có.</param>
        /// <returns>True nếu thay thế pin thành công; ngược lại False.</returns>
        public bool ReplaceBattery(string maTram, int soThuTu, string oldPinId, string newPinId, string technicianName, string note, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(oldPinId) || string.IsNullOrWhiteSpace(newPinId))
            {
                errorMessage = "Vui lòng nhập đầy đủ mã pin cũ và mã pin mới!";
                return false;
            }

            if (string.Equals(oldPinId.Trim(), newPinId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Mã pin mới không được trùng với mã pin cũ!";
                return false;
            }

            try
            {
                var p1 = new SqlParameter("@MaTram", SqlDbType.VarChar, 20) { Value = maTram };
                var p2 = new SqlParameter("@SoThuTu", SqlDbType.Int) { Value = soThuTu };
                var p3 = new SqlParameter("@MaPinCu", SqlDbType.VarChar, 20) { Value = oldPinId.Trim() };
                var p4 = new SqlParameter("@MaPinMoi", SqlDbType.VarChar, 20) { Value = newPinId.Trim() };
                var p5 = new SqlParameter("@NguoiThucHien", SqlDbType.NVarChar, 100) { Value = technicianName };
                var p6 = new SqlParameter("@GhiChu", SqlDbType.NVarChar, 255) { Value = string.IsNullOrWhiteSpace(note) ? "Thay thế pin chai/hỏng bằng pin mới" : note };

                DatabaseHelper.ExecuteStoredProcedureNonQuery("dbo.sp_BaoTriThayThePin", p1, p2, p3, p4, p5, p6);
                return true;
            }
            catch (Exception)
            {
                // Fallback nếu chưa đăng ký Procedure
                using var conn = DatabaseHelper.GetOpenConnection();
                using var trans = conn.BeginTransaction();
                try
                {
                    // 1. Đánh dấu pin cũ đã thu hồi
                    string sqlOld = "UPDATE dbo.Pin SET TrangThai = N'DaThuHoi', DongDien = 0.0, NgayCapNhatCuoi = GETDATE() WHERE MaPin = @OldPin";
                    using (var cmdOld = new SqlCommand(sqlOld, conn, trans))
                    {
                        cmdOld.Parameters.Add(new SqlParameter("@OldPin", SqlDbType.VarChar, 20) { Value = oldPinId.Trim() });
                        cmdOld.ExecuteNonQuery();
                    }

                    // 2. Thêm hoặc cập nhật pin mới
                    string sqlNewCheck = "SELECT COUNT(*) FROM dbo.Pin WHERE MaPin = @NewPin";
                    bool pinNewExists = false;
                    using (var cmdCheck = new SqlCommand(sqlNewCheck, conn, trans))
                    {
                        cmdCheck.Parameters.Add(new SqlParameter("@NewPin", SqlDbType.VarChar, 20) { Value = newPinId.Trim() });
                        pinNewExists = Convert.ToInt32(cmdCheck.ExecuteScalar()) > 0;
                    }

                    if (!pinNewExists)
                    {
                        string sqlInsertNew = @"INSERT INTO dbo.Pin (MaPin, LoaiPin, DungLuongThietKe, SoC, SoH, NhietDo, DienAp, DongDien, SoLanSac, TrangThai, NgayCapNhatCuoi)
                                                VALUES (@NewPin, N'Lithium LFP 72V-30Ah', 30, 100.00, 100.00, 25.00, 73.00, 0.00, 0, N'DaDay', GETDATE())";
                        using var cmdInsert = new SqlCommand(sqlInsertNew, conn, trans);
                        cmdInsert.Parameters.Add(new SqlParameter("@NewPin", SqlDbType.VarChar, 20) { Value = newPinId.Trim() });
                        cmdInsert.ExecuteNonQuery();
                    }
                    else
                    {
                        string sqlUpdateNew = "UPDATE dbo.Pin SET TrangThai = N'DaDay', SoC = 100.00, SoH = 100.00, NhietDo = 25.00, DongDien = 0.0, NgayCapNhatCuoi = GETDATE() WHERE MaPin = @NewPin";
                        using var cmdUpdate = new SqlCommand(sqlUpdateNew, conn, trans);
                        cmdUpdate.Parameters.Add(new SqlParameter("@NewPin", SqlDbType.VarChar, 20) { Value = newPinId.Trim() });
                        cmdUpdate.ExecuteNonQuery();
                    }

                    // 3. Giải phóng pin mới khỏi ngăn cũ nếu có
                    string sqlClearNew = "UPDATE dbo.NganTu SET MaPinHienTai = NULL, TrangThaiNgan = N'Trong' WHERE MaPinHienTai = @NewPin AND NOT (MaTram = @MaTram AND SoThuTu = @SoThuTu)";
                    using (var cmdClear = new SqlCommand(sqlClearNew, conn, trans))
                    {
                        cmdClear.Parameters.Add(new SqlParameter("@NewPin", SqlDbType.VarChar, 20) { Value = newPinId.Trim() });
                        cmdClear.Parameters.Add(new SqlParameter("@MaTram", SqlDbType.VarChar, 20) { Value = maTram });
                        cmdClear.Parameters.Add(new SqlParameter("@SoThuTu", SqlDbType.Int) { Value = soThuTu });
                        cmdClear.ExecuteNonQuery();
                    }

                    // 4. Gán pin mới vào ngăn tủ
                    string sqlAssign = @"UPDATE dbo.NganTu 
                                         SET MaPinHienTai = @NewPin, TrangThaiNgan = N'CoPin', KhoaBaoTri = 0, CuaDangMo = 0, NgayCapNhat = GETDATE() 
                                         WHERE MaTram = @MaTram AND SoThuTu = @SoThuTu";
                    using (var cmdAssign = new SqlCommand(sqlAssign, conn, trans))
                    {
                        cmdAssign.Parameters.Add(new SqlParameter("@NewPin", SqlDbType.VarChar, 20) { Value = newPinId.Trim() });
                        cmdAssign.Parameters.Add(new SqlParameter("@MaTram", SqlDbType.VarChar, 20) { Value = maTram });
                        cmdAssign.Parameters.Add(new SqlParameter("@SoThuTu", SqlDbType.Int) { Value = soThuTu });
                        cmdAssign.ExecuteNonQuery();
                    }

                    // 5. Đóng cảnh báo an toàn tồn đọng của pin cũ
                    string sqlAlert = "UPDATE dbo.CanhBaoIoT SET DaXuLy = 1, NguoiXuLy = @Tech, ThoiGianXuLy = GETDATE(), GhiChu = @Note WHERE MaPin = @OldPin AND DaXuLy = 0";
                    using (var cmdAlert = new SqlCommand(sqlAlert, conn, trans))
                    {
                        cmdAlert.Parameters.Add(new SqlParameter("@Tech", SqlDbType.NVarChar, 100) { Value = technicianName });
                        cmdAlert.Parameters.Add(new SqlParameter("@Note", SqlDbType.NVarChar, 255) { Value = note });
                        cmdAlert.Parameters.Add(new SqlParameter("@OldPin", SqlDbType.VarChar, 20) { Value = oldPinId.Trim() });
                        cmdAlert.ExecuteNonQuery();
                    }

                    trans.Commit();
                    return true;
                }
                catch (Exception rollbackEx)
                {
                    try { trans?.Rollback(); } catch { }
                    errorMessage = rollbackEx.Message;
                    return false;
                }
            }
        }

        /// <summary>
        /// Thêm mới một viên pin vào kho hệ thống với kiểm tra hợp lệ dữ liệu.
        /// </summary>
        /// <param name="pin">Đối tượng pin chứa các thông số khởi tạo.</param>
        /// <param name="errorMessage">Thông điệp lỗi trả về nếu thêm thất bại.</param>
        /// <returns>True nếu thêm thành công; ngược lại False.</returns>
        public bool AddBattery(Pin pin, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (pin == null || string.IsNullOrWhiteSpace(pin.MaPin))
            {
                errorMessage = "Mã pin không được để trống!";
                return false;
            }

            try
            {
                // Kiểm tra mã pin đã tồn tại chưa
                string sqlCheck = "SELECT COUNT(*) FROM dbo.Pin WHERE MaPin = @MaPin";
                var pCheck = new SqlParameter("@MaPin", SqlDbType.VarChar, 20) { Value = pin.MaPin.Trim() };
                object? count = DatabaseHelper.ExecuteScalar(sqlCheck, pCheck);
                if (count != null && Convert.ToInt32(count) > 0)
                {
                    errorMessage = $"Mã pin '{pin.MaPin}' đã tồn tại trong hệ thống!";
                    return false;
                }

                string sql = @"INSERT INTO dbo.Pin (MaPin, LoaiPin, DungLuongThietKe, SoC, SoH, NhietDo, DienAp, DongDien, SoLanSac, TrangThai, NgayCapNhatCuoi)
                               VALUES (@MaPin, @LoaiPin, @DungLuong, @SoC, @SoH, @NhietDo, @DienAp, @DongDien, @SoLanSac, @TrangThai, GETDATE())";

                var parameters = new[]
                {
                    new SqlParameter("@MaPin", SqlDbType.VarChar, 20) { Value = pin.MaPin.Trim() },
                    new SqlParameter("@LoaiPin", SqlDbType.NVarChar, 50) { Value = string.IsNullOrWhiteSpace(pin.LoaiPin) ? "Lithium LFP 72V-30Ah" : pin.LoaiPin.Trim() },
                    new SqlParameter("@DungLuong", SqlDbType.Int) { Value = pin.DungLuongThietKe > 0 ? pin.DungLuongThietKe : 30 },
                    new SqlParameter("@SoC", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = Math.Clamp(pin.SoC, 0m, 100m) },
                    new SqlParameter("@SoH", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = Math.Clamp(pin.SoH, 0m, 100m) },
                    new SqlParameter("@NhietDo", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = pin.NhietDo },
                    new SqlParameter("@DienAp", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = pin.DienAp },
                    new SqlParameter("@DongDien", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = pin.DongDien },
                    new SqlParameter("@SoLanSac", SqlDbType.Int) { Value = Math.Max(0, pin.SoLanSac) },
                    new SqlParameter("@TrangThai", SqlDbType.NVarChar, 30) { Value = string.IsNullOrWhiteSpace(pin.TrangThai) ? "KhaDung" : pin.TrangThai }
                };

                return DatabaseHelper.ExecuteNonQuery(sql, parameters) > 0;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Cập nhật thông số Telemetry gửi từ phần cứng cảm biến IoT BMS.
        /// </summary>
        /// <param name="maPin">Mã định danh viên pin.</param>
        /// <param name="soc">Dung lượng phần trăm sạc (0 - 100%).</param>
        /// <param name="soh">Tình trạng sức khỏe pin (0 - 100%).</param>
        /// <param name="nhietDo">Nhiệt độ hiện tại đo từ cảm biến BMS (°C).</param>
        /// <param name="dienAp">Điện áp đo được (Volts).</param>
        /// <param name="dongDien">Dòng điện sạc/xả (Amperes).</param>
        /// <returns>True nếu cập nhật thành công; ngược lại False.</returns>
        public bool UpdateBatteryTelemetry(string maPin, decimal soc, decimal soh, decimal nhietDo, decimal dienAp, decimal dongDien)
        {
            if (string.IsNullOrWhiteSpace(maPin)) return false;

            try
            {
                string sql = @"UPDATE dbo.Pin 
                               SET SoC = @SoC, SoH = @SoH, NhietDo = @NhietDo, DienAp = @DienAp, DongDien = @DongDien, NgayCapNhatCuoi = GETDATE()
                               WHERE MaPin = @MaPin";

                var parameters = new[]
                {
                    new SqlParameter("@MaPin", SqlDbType.VarChar, 20) { Value = maPin.Trim() },
                    new SqlParameter("@SoC", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = Math.Clamp(soc, 0m, 100m) },
                    new SqlParameter("@SoH", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = Math.Clamp(soh, 0m, 100m) },
                    new SqlParameter("@NhietDo", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = nhietDo },
                    new SqlParameter("@DienAp", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = dienAp },
                    new SqlParameter("@DongDien", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = dongDien }
                };

                return DatabaseHelper.ExecuteNonQuery(sql, parameters) > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
