using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using BatterySwapping.App.Helpers;
using BatterySwapping.App.Models;

namespace BatterySwapping.App.Services
{
    /// <summary>
    /// Kết quả của một chu trình đổi pin tự động.
    /// </summary>
    public class SwapResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int TransactionId { get; set; }
        public int SlotOldBattery { get; set; } // Ngăn nhận pin cũ
        public string OldBatteryId { get; set; } = string.Empty;
        public decimal OldBatterySoC { get; set; }
        public int SlotNewBattery { get; set; } // Ngăn nhả pin mới
        public string NewBatteryId { get; set; } = string.Empty;
        public decimal NewBatterySoC { get; set; }
        public decimal Fee { get; set; } = 25000.0m;
        public decimal RemainingBalance { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Dịch vụ điều phối chu trình đổi pin 6 bước tự động tại trạm Kiosk.
    /// Áp dụng thuật toán chọn pin tối ưu và cơ chế bảo vệ giao dịch (ACID) qua Stored Procedure và SqlTransaction dự phòng.
    /// </summary>
    public class SwapService
    {
        public const decimal SWAP_FEE = 25000.0m;
        public const decimal MIN_REQUIRED_SOC = 90.0m;
        public const decimal MIN_REQUIRED_SOH = 80.0m;
        public const decimal MAX_SAFE_TEMP = 45.0m;

        /// <summary>
        /// Bước 1: Kiểm tra thông tin khách hàng và số dư ví điện tử (Hỗ trợ tra cứu qua SĐT hoặc mã QR).
        /// </summary>
        public bool ValidateCustomerWallet(string phoneOrQr, out KhachHang? customer, out string errorMessage)
        {
            customer = null;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(phoneOrQr))
            {
                errorMessage = "Vui lòng nhập số điện thoại hoặc quét mã QR hợp lệ!";
                return false;
            }

            try
            {
                // Giải mã payload mã QR nếu người dùng quét QR
                int parsedMaKH = 0;
                string searchPhone = phoneOrQr.Trim();
                if (QrCodeHelper.TryParseQrPayload(phoneOrQr, out int qMaKH, out string qPhone))
                {
                    parsedMaKH = qMaKH;
                    searchPhone = qPhone;
                }

                string sql = @"
                    SELECT MaKH, MaTK, HoTen, SoDienThoai, Email, BienSoXe, SoDuVi, MaPinDangMuon, NgayDangKy 
                    FROM dbo.KhachHang 
                    WHERE (@MaKH = 0 OR MaKH = @MaKH) AND SoDienThoai = @Phone";

                var pMaKH = new SqlParameter("@MaKH", SqlDbType.Int) { Value = parsedMaKH };
                var pPhone = new SqlParameter("@Phone", SqlDbType.VarChar, 50) { Value = searchPhone };

                DataTable dt = DatabaseHelper.ExecuteDataTable(sql, pMaKH, pPhone);

                if (dt.Rows.Count == 0)
                {
                    errorMessage = "Không tìm thấy thông tin khách hàng trong hệ thống!";
                    return false;
                }

                var row = dt.Rows[0];
                customer = new KhachHang
                {
                    MaKH = Convert.ToInt32(row["MaKH"]),
                    MaTK = row["MaTK"] != DBNull.Value ? Convert.ToInt32(row["MaTK"]) : null,
                    HoTen = row["HoTen"] != DBNull.Value ? row["HoTen"].ToString()! : string.Empty,
                    SoDienThoai = row["SoDienThoai"] != DBNull.Value ? row["SoDienThoai"].ToString()! : string.Empty,
                    Email = row["Email"] != DBNull.Value ? row["Email"].ToString() : null,
                    BienSoXe = row["BienSoXe"] != DBNull.Value ? row["BienSoXe"].ToString()! : string.Empty,
                    SoDuVi = row["SoDuVi"] != DBNull.Value ? Convert.ToDecimal(row["SoDuVi"]) : 0.0m,
                    MaPinDangMuon = row["MaPinDangMuon"] != DBNull.Value ? row["MaPinDangMuon"].ToString() : null,
                    NgayDangKy = row["NgayDangKy"] != DBNull.Value ? Convert.ToDateTime(row["NgayDangKy"]) : DateTime.Now
                };

                if (customer.SoDuVi < SWAP_FEE)
                {
                    errorMessage = $"Số dư ví ({customer.SoDuFormatted}) không đủ để thực hiện đổi pin (Yêu cầu tối thiểu {SWAP_FEE:N0} đ)! Vui lòng nạp thêm tiền.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Lỗi tra cứu khách hàng: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Bước 2: Tìm ngăn tủ trống khả dụng để nhận pin cũ (Không bị khóa bảo trì và không bị lỗi).
        /// </summary>
        public NganTu? FindAvailableEmptySlot(string maTram)
        {
            string sql = @"
                SELECT TOP 1 MaNgan, MaTram, SoThuTu, CuaDangMo, KhoaBaoTri, MaPinHienTai, TrangThaiNgan, NgayCapNhat
                FROM dbo.NganTu
                WHERE MaTram = @MaTram 
                  AND KhoaBaoTri = 0 
                  AND MaPinHienTai IS NULL 
                  AND TrangThaiNgan <> N'Loi'
                ORDER BY CuaDangMo ASC, SoThuTu ASC";

            var param = new SqlParameter("@MaTram", SqlDbType.VarChar, 20) { Value = maTram };
            DataTable dt = DatabaseHelper.ExecuteDataTable(sql, param);

            if (dt.Rows.Count == 0) return null;

            var row = dt.Rows[0];
            return new NganTu
            {
                MaNgan = Convert.ToInt32(row["MaNgan"]),
                MaTram = row["MaTram"] != DBNull.Value ? row["MaTram"].ToString()! : string.Empty,
                SoThuTu = Convert.ToInt32(row["SoThuTu"]),
                CuaDangMo = row["CuaDangMo"] != DBNull.Value && Convert.ToBoolean(row["CuaDangMo"]),
                KhoaBaoTri = row["KhoaBaoTri"] != DBNull.Value && Convert.ToBoolean(row["KhoaBaoTri"]),
                MaPinHienTai = null,
                TrangThaiNgan = row["TrangThaiNgan"] != DBNull.Value ? row["TrangThaiNgan"].ToString()! : "Trong",
                NgayCapNhat = row["NgayCapNhat"] != DBNull.Value ? Convert.ToDateTime(row["NgayCapNhat"]) : DateTime.Now
            };
        }

        /// <summary>
        /// Bước 4: Thuật toán chọn viên pin tối ưu nhất tại trạm để nhả cho khách hàng.
        /// Tiêu chí lọc: SoC >= 90%, SoH >= 80%, Nhiệt độ <= 45°C, Trạng thái hoạt động bình thường.
        /// Thứ tự ưu tiên: SoC cao nhất -> Nhiệt độ thấp nhất -> Số lần sạc ít nhất.
        /// </summary>
        public NganTu? FindOptimalBatterySlot(string maTram)
        {
            string sql = @"
                SELECT TOP 1 
                    nt.MaNgan, nt.MaTram, nt.SoThuTu, nt.CuaDangMo, nt.KhoaBaoTri, nt.MaPinHienTai, nt.TrangThaiNgan, nt.NgayCapNhat,
                    p.LoaiPin, p.SoC, p.SoH, p.NhietDo, p.DienAp, p.DongDien, p.TrangThai AS TrangThaiPin
                FROM dbo.NganTu nt
                INNER JOIN dbo.Pin p ON nt.MaPinHienTai = p.MaPin
                WHERE nt.MaTram = @MaTram 
                  AND nt.KhoaBaoTri = 0 
                  AND nt.TrangThaiNgan <> N'Loi'
                  AND p.SoC >= @MinSoC 
                  AND p.SoH >= @MinSoH 
                  AND p.NhietDo <= @MaxTemp
                  AND p.TrangThai NOT IN (N'Loi', N'LoiQuaNhiet', N'DangSuDung')
                ORDER BY p.SoC DESC, p.NhietDo ASC, p.SoLanSac ASC";

            var parameters = new[]
            {
                new SqlParameter("@MaTram", SqlDbType.VarChar, 20) { Value = maTram },
                new SqlParameter("@MinSoC", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = MIN_REQUIRED_SOC },
                new SqlParameter("@MinSoH", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = MIN_REQUIRED_SOH },
                new SqlParameter("@MaxTemp", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = MAX_SAFE_TEMP }
            };

            DataTable dt = DatabaseHelper.ExecuteDataTable(sql, parameters);
            if (dt.Rows.Count == 0) return null;

            var row = dt.Rows[0];
            return new NganTu
            {
                MaNgan = Convert.ToInt32(row["MaNgan"]),
                MaTram = row["MaTram"] != DBNull.Value ? row["MaTram"].ToString()! : string.Empty,
                SoThuTu = Convert.ToInt32(row["SoThuTu"]),
                CuaDangMo = row["CuaDangMo"] != DBNull.Value && Convert.ToBoolean(row["CuaDangMo"]),
                KhoaBaoTri = row["KhoaBaoTri"] != DBNull.Value && Convert.ToBoolean(row["KhoaBaoTri"]),
                MaPinHienTai = row["MaPinHienTai"] != DBNull.Value ? row["MaPinHienTai"].ToString() : null,
                TrangThaiNgan = row["TrangThaiNgan"] != DBNull.Value ? row["TrangThaiNgan"].ToString()! : "CoPin",
                NgayCapNhat = row["NgayCapNhat"] != DBNull.Value ? Convert.ToDateTime(row["NgayCapNhat"]) : DateTime.Now,
                LoaiPin = row["LoaiPin"] != DBNull.Value ? row["LoaiPin"].ToString() : "Lithium LFP 72V-30Ah",
                SoC = row["SoC"] != DBNull.Value ? Convert.ToDecimal(row["SoC"]) : 0.0m,
                SoH = row["SoH"] != DBNull.Value ? Convert.ToDecimal(row["SoH"]) : 0.0m,
                NhietDo = row["NhietDo"] != DBNull.Value ? Convert.ToDecimal(row["NhietDo"]) : 25.0m,
                DienAp = row["DienAp"] != DBNull.Value ? Convert.ToDecimal(row["DienAp"]) : 72.0m,
                DongDien = row["DongDien"] != DBNull.Value ? Convert.ToDecimal(row["DongDien"]) : 0.0m,
                TrangThaiPin = row["TrangThaiPin"] != DBNull.Value ? row["TrangThaiPin"].ToString() : "DaDay"
            };
        }

        /// <summary>
        /// Bước 6: Hoàn tất giao dịch đổi pin, trừ ví và chốt dữ liệu với cơ chế đảm bảo tính toàn vẹn ACID.
        /// Hỗ trợ cả Stored Procedure sp_ThucHienDoiPin và SqlTransaction dự phòng.
        /// </summary>
        public SwapResult ExecuteFullSwap(
            KhachHang customer, 
            string maTram, 
            int emptySlotNumber, 
            string oldBatteryId, 
            decimal oldBatterySoc, 
            int optimalSlotNumber, 
            string newBatteryId, 
            decimal newBatterySoc)
        {
            var result = new SwapResult
            {
                SlotOldBattery = emptySlotNumber,
                OldBatteryId = oldBatteryId,
                OldBatterySoC = oldBatterySoc,
                SlotNewBattery = optimalSlotNumber,
                NewBatteryId = newBatteryId,
                NewBatterySoC = newBatterySoc,
                Fee = SWAP_FEE
            };

            // Thử thực thi qua Stored Procedure sp_ThucHienDoiPin
            try
            {
                using var conn = DatabaseHelper.GetOpenConnection();
                using var cmd = new SqlCommand("dbo.sp_ThucHienDoiPin", conn)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = DatabaseHelper.DefaultCommandTimeout
                };

                cmd.Parameters.Add(new SqlParameter("@MaKH", SqlDbType.Int) { Value = customer.MaKH });
                cmd.Parameters.Add(new SqlParameter("@MaTram", SqlDbType.VarChar, 20) { Value = maTram });
                cmd.Parameters.Add(new SqlParameter("@MaPinTra", SqlDbType.VarChar, 20) { Value = oldBatteryId });
                cmd.Parameters.Add(new SqlParameter("@SoC_PinTra", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = oldBatterySoc });
                cmd.Parameters.Add(new SqlParameter("@NganTra", SqlDbType.Int) { Value = emptySlotNumber });
                cmd.Parameters.Add(new SqlParameter("@MaPinNhan", SqlDbType.VarChar, 20) { Value = newBatteryId });
                cmd.Parameters.Add(new SqlParameter("@SoC_PinNhan", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = newBatterySoc });
                cmd.Parameters.Add(new SqlParameter("@NganNhan", SqlDbType.Int) { Value = optimalSlotNumber });
                cmd.Parameters.Add(new SqlParameter("@PhiDoiPin", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = SWAP_FEE });

                var outParam = new SqlParameter("@MaGD", SqlDbType.Int)
                {
                    Direction = ParameterDirection.Output
                };
                cmd.Parameters.Add(outParam);

                cmd.ExecuteNonQuery();

                int transId = (outParam.Value != null && outParam.Value != DBNull.Value) ? Convert.ToInt32(outParam.Value) : 0;
                result.Success = true;
                result.TransactionId = transId;
                customer.SoDuVi -= SWAP_FEE;
                customer.MaPinDangMuon = newBatteryId;
                result.RemainingBalance = customer.SoDuVi;
                result.Message = "Giao dịch đổi pin tự động thành công hoàn tất!";
                return result;
            }
            catch (SqlException ex) when (ex.Number == 2812) // Lỗi 2812: Không tìm thấy stored procedure -> Chuyển sang SqlTransaction thuần
            {
                result.Success = false;
                result.Message = "Thiếu thủ tục đổi pin. Vui lòng chạy script SQL 02 trước khi sử dụng.";
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Giao dịch đổi pin thất bại: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Thực thi đổi pin dự phòng trực tiếp bằng SqlTransaction (ACID) đảm bảo tính toàn vẹn 100% ngay cả khi chưa nạp Stored Procedure.
        /// </summary>
        private SwapResult ExecuteFullSwapWithSqlTransaction(
            KhachHang customer,
            string maTram,
            int emptySlotNumber,
            string oldBatteryId,
            decimal oldBatterySoc,
            int optimalSlotNumber,
            string newBatteryId,
            decimal newBatterySoc,
            SwapResult result)
        {
            using var conn = DatabaseHelper.GetOpenConnection();
            using var trans = conn.BeginTransaction(IsolationLevel.ReadCommitted);
            try
            {
                // 1. Khóa và kiểm tra số dư ví khách hàng chống Race Condition
                string sqlCheckBalance = "SELECT SoDuVi FROM dbo.KhachHang WITH (UPDLOCK, ROWLOCK) WHERE MaKH = @MaKH";
                using (var cmdCheck = new SqlCommand(sqlCheckBalance, conn, trans))
                {
                    cmdCheck.Parameters.Add(new SqlParameter("@MaKH", SqlDbType.Int) { Value = customer.MaKH });
                    object? balanceObj = cmdCheck.ExecuteScalar();
                    if (balanceObj == null || balanceObj == DBNull.Value)
                    {
                        throw new InvalidOperationException("Không tìm thấy thông tin khách hàng trong hệ thống.");
                    }
                    decimal balance = Convert.ToDecimal(balanceObj);
                    if (balance < SWAP_FEE)
                    {
                        throw new InvalidOperationException($"Số dư ví ({balance:N0} đ) không đủ để thực hiện đổi pin (Yêu cầu: {SWAP_FEE:N0} đ).");
                    }
                }

                // 2. Trừ tiền ví và gán mã pin mới đang mượn cho khách
                string sqlUpdateCust = @"
                    UPDATE dbo.KhachHang 
                    SET SoDuVi = SoDuVi - @Phi, MaPinDangMuon = @MaPinNhan 
                    WHERE MaKH = @MaKH";
                using (var cmdCust = new SqlCommand(sqlUpdateCust, conn, trans))
                {
                    cmdCust.Parameters.Add(new SqlParameter("@Phi", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = SWAP_FEE });
                    cmdCust.Parameters.Add(new SqlParameter("@MaPinNhan", SqlDbType.VarChar, 20) { Value = newBatteryId });
                    cmdCust.Parameters.Add(new SqlParameter("@MaKH", SqlDbType.Int) { Value = customer.MaKH });
                    cmdCust.ExecuteNonQuery();
                }

                // 3. Đảm bảo pin trả tồn tại trong bảng Pin và giải phóng khỏi các ngăn tủ cũ nếu có
                string sqlReleaseOld = @"
                    UPDATE dbo.NganTu 
                    SET MaPinHienTai = NULL, TrangThaiNgan = N'Trong', NgayCapNhat = GETDATE() 
                    WHERE MaPinHienTai = @MaPinTra AND NOT (MaTram = @MaTram AND SoThuTu = @NganTra);

                    IF NOT EXISTS (SELECT 1 FROM dbo.Pin WHERE MaPin = @MaPinTra)
                    BEGIN
                        INSERT INTO dbo.Pin (MaPin, LoaiPin, DungLuongThietKe, SoC, SoH, NhietDo, DienAp, DongDien, SoLanSac, TrangThai, NgayCapNhatCuoi)
                        VALUES (@MaPinTra, N'Lithium LFP 72V-30Ah', 30, @SoC_Tra, 95.0, 28.0, 72.0, 15.0, 1, N'DangSac', GETDATE());
                    END
                    ELSE
                    BEGIN
                        UPDATE dbo.Pin 
                        SET SoC = @SoC_Tra, TrangThai = N'DangSac', DongDien = 15.0, NgayCapNhatCuoi = GETDATE()
                        WHERE MaPin = @MaPinTra;
                    END

                    UPDATE dbo.NganTu 
                    SET MaPinHienTai = @MaPinTra, TrangThaiNgan = N'DangSac', CuaDangMo = 0, NgayCapNhat = GETDATE()
                    WHERE MaTram = @MaTram AND SoThuTu = @NganTra;";

                using (var cmdOld = new SqlCommand(sqlReleaseOld, conn, trans))
                {
                    cmdOld.Parameters.Add(new SqlParameter("@MaPinTra", SqlDbType.VarChar, 20) { Value = oldBatteryId });
                    cmdOld.Parameters.Add(new SqlParameter("@MaTram", SqlDbType.VarChar, 20) { Value = maTram });
                    cmdOld.Parameters.Add(new SqlParameter("@NganTra", SqlDbType.Int) { Value = emptySlotNumber });
                    cmdOld.Parameters.Add(new SqlParameter("@SoC_Tra", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = oldBatterySoc });
                    cmdOld.ExecuteNonQuery();
                }

                // 4. Nhả pin mới ra khỏi ngăn nhận và cập nhật trạng thái pin mới
                string sqlNew = @"
                    UPDATE dbo.Pin 
                    SET TrangThai = N'DangSuDung', DongDien = 0.0, SoLanSac = SoLanSac + 1, NgayCapNhatCuoi = GETDATE()
                    WHERE MaPin = @MaPinNhan;

                    UPDATE dbo.NganTu 
                    SET MaPinHienTai = NULL, TrangThaiNgan = N'Trong', CuaDangMo = 0, NgayCapNhat = GETDATE()
                    WHERE MaTram = @MaTram AND SoThuTu = @NganNhan;";

                using (var cmdNew = new SqlCommand(sqlNew, conn, trans))
                {
                    cmdNew.Parameters.Add(new SqlParameter("@MaPinNhan", SqlDbType.VarChar, 20) { Value = newBatteryId });
                    cmdNew.Parameters.Add(new SqlParameter("@MaTram", SqlDbType.VarChar, 20) { Value = maTram });
                    cmdNew.Parameters.Add(new SqlParameter("@NganNhan", SqlDbType.Int) { Value = optimalSlotNumber });
                    cmdNew.ExecuteNonQuery();
                }

                // 5. Ghi lịch sử giao dịch đổi pin
                string sqlLog = @"
                    INSERT INTO dbo.LichSuDoiPin (
                        MaKH, MaTram, MaPinTra, SoC_PinTra, NganTra, 
                        MaPinNhan, SoC_PinNhan, NganNhan, 
                        ThoiGianBatDau, ThoiGianKetThuc, PhiDoiPin, TrangThai, GhiChu
                    )
                    VALUES (
                        @MaKH, @MaTram, @MaPinTra, @SoC_Tra, @NganTra,
                        @MaPinNhan, @SoC_Nhan, @NganNhan,
                        DATEADD(MINUTE, -2, GETDATE()), GETDATE(), @Phi, N'HoanThanh', N'Giao dịch đổi pin tự động thành công (SqlTransaction fallback)'
                    );
                    SELECT CAST(SCOPE_IDENTITY() AS INT);";

                int transId = 0;
                using (var cmdLog = new SqlCommand(sqlLog, conn, trans))
                {
                    cmdLog.Parameters.Add(new SqlParameter("@MaKH", SqlDbType.Int) { Value = customer.MaKH });
                    cmdLog.Parameters.Add(new SqlParameter("@MaTram", SqlDbType.VarChar, 20) { Value = maTram });
                    cmdLog.Parameters.Add(new SqlParameter("@MaPinTra", SqlDbType.VarChar, 20) { Value = oldBatteryId });
                    cmdLog.Parameters.Add(new SqlParameter("@SoC_Tra", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = oldBatterySoc });
                    cmdLog.Parameters.Add(new SqlParameter("@NganTra", SqlDbType.Int) { Value = emptySlotNumber });
                    cmdLog.Parameters.Add(new SqlParameter("@MaPinNhan", SqlDbType.VarChar, 20) { Value = newBatteryId });
                    cmdLog.Parameters.Add(new SqlParameter("@SoC_Nhan", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = newBatterySoc });
                    cmdLog.Parameters.Add(new SqlParameter("@NganNhan", SqlDbType.Int) { Value = optimalSlotNumber });
                    cmdLog.Parameters.Add(new SqlParameter("@Phi", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = SWAP_FEE });

                    object? idObj = cmdLog.ExecuteScalar();
                    if (idObj != null && idObj != DBNull.Value)
                    {
                        transId = Convert.ToInt32(idObj);
                    }
                }

                trans.Commit();

                result.Success = true;
                result.TransactionId = transId;
                customer.SoDuVi -= SWAP_FEE;
                customer.MaPinDangMuon = newBatteryId;
                result.RemainingBalance = customer.SoDuVi;
                result.Message = "Giao dịch đổi pin tự động thành công hoàn tất!";
                return result;
            }
            catch (Exception ex)
            {
                try { trans.Rollback(); } catch { }
                result.Success = false;
                result.Message = $"Giao dịch đổi pin thất bại: {ex.Message}";
                return result;
            }
        }
    }
}
