using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using BatterySwapping.App.Helpers;
using BatterySwapping.App.Models;

namespace BatterySwapping.App.Services
{
    /// <summary>
    /// Mô hình tổng hợp số liệu thống kê doanh thu và vận hành.
    /// </summary>
    public class SystemSummaryStats
    {
        public decimal TongDoanhThu { get; set; }
        public decimal DoanhThuHomNay { get; set; }
        public int TongLuotDoiPin { get; set; }
        public int TongSoPin { get; set; }
        public int SoPinKhaDung { get; set; }
        public int SoCanhBaoChuaXuLy { get; set; }
    }

    /// <summary>
    /// Dịch vụ lập báo cáo thống kê, quản lý khách hàng và lịch sử giao dịch ví.
    /// </summary>
    public class ReportService
    {
        /// <summary>
        /// Lấy toàn bộ số liệu thống kê tổng quan của hệ thống kèm xử lý an toàn giá trị NULL.
        /// </summary>
        public SystemSummaryStats GetSummaryStats()
        {
            var stats = new SystemSummaryStats();
            try
            {
                // Tổng doanh thu & lượt đổi pin
                string sqlGD = @"
                    SELECT 
                        ISNULL(SUM(PhiDoiPin), 0) AS TongDoanhThu,
                        ISNULL(SUM(CASE WHEN CAST(ThoiGianKetThuc AS DATE) = CAST(GETDATE() AS DATE) THEN PhiDoiPin ELSE 0 END), 0) AS DoanhThuHomNay,
                        COUNT(*) AS TongLuotDoi
                    FROM dbo.LichSuDoiPin WHERE TrangThai = N'HoanThanh'";
                DataTable dtGD = DatabaseHelper.ExecuteDataTable(sqlGD);
                if (dtGD.Rows.Count > 0)
                {
                    stats.TongDoanhThu = dtGD.Rows[0]["TongDoanhThu"] != DBNull.Value ? Convert.ToDecimal(dtGD.Rows[0]["TongDoanhThu"]) : 0.0m;
                    stats.DoanhThuHomNay = dtGD.Rows[0]["DoanhThuHomNay"] != DBNull.Value ? Convert.ToDecimal(dtGD.Rows[0]["DoanhThuHomNay"]) : 0.0m;
                    stats.TongLuotDoiPin = dtGD.Rows[0]["TongLuotDoi"] != DBNull.Value ? Convert.ToInt32(dtGD.Rows[0]["TongLuotDoi"]) : 0;
                }

                // Thống kê pin
                string sqlPin = @"
                    SELECT 
                        COUNT(*) AS TongPin,
                        SUM(CASE WHEN SoC >= 90.0 AND NhietDo < 45.0 AND TrangThai NOT IN (N'Loi', N'LoiQuaNhiet', N'DangSuDung') THEN 1 ELSE 0 END) AS PinKhaDung
                    FROM dbo.Pin";
                DataTable dtPin = DatabaseHelper.ExecuteDataTable(sqlPin);
                if (dtPin.Rows.Count > 0)
                {
                    stats.TongSoPin = dtPin.Rows[0]["TongPin"] != DBNull.Value ? Convert.ToInt32(dtPin.Rows[0]["TongPin"]) : 0;
                    stats.SoPinKhaDung = dtPin.Rows[0]["PinKhaDung"] != DBNull.Value ? Convert.ToInt32(dtPin.Rows[0]["PinKhaDung"]) : 0;
                }

                // Cảnh báo chưa xử lý
                string sqlCB = "SELECT COUNT(*) FROM dbo.CanhBaoIoT WHERE DaXuLy = 0";
                object? resCB = DatabaseHelper.ExecuteScalar(sqlCB);
                stats.SoCanhBaoChuaXuLy = (resCB != null && resCB != DBNull.Value) ? Convert.ToInt32(resCB) : 0;
            }
            catch
            {
                // Return default zeros
            }
            return stats;
        }

        /// <summary>
        /// Lấy danh sách toàn bộ khách hàng trong hệ thống.
        /// </summary>
        public List<KhachHang> GetAllCustomers()
        {
            var list = new List<KhachHang>();
            string sql = "SELECT MaKH, MaTK, HoTen, SoDienThoai, Email, BienSoXe, SoDuVi, MaPinDangMuon, NgayDangKy FROM dbo.KhachHang ORDER BY MaKH DESC";
            DataTable dt = DatabaseHelper.ExecuteDataTable(sql);

            foreach (DataRow row in dt.Rows)
            {
                list.Add(new KhachHang
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
                });
            }
            return list;
        }

        /// <summary>
        /// Nạp tiền vào ví khách hàng và lưu lại lịch sử với bảo vệ toàn vẹn giao dịch ACID.
        /// </summary>
        public bool TopUpWallet(int maKH, decimal amount, string method, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (maKH <= 0)
            {
                errorMessage = "Mã khách hàng không hợp lệ!";
                return false;
            }

            if (amount <= 0)
            {
                errorMessage = "Số tiền nạp phải lớn hơn 0 VNĐ!";
                return false;
            }

            try
            {
                var p1 = new SqlParameter("@MaKH", SqlDbType.Int) { Value = maKH };
                var p2 = new SqlParameter("@SoTien", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = amount };
                var p3 = new SqlParameter("@PhuongThuc", SqlDbType.NVarChar, 50) { Value = string.IsNullOrWhiteSpace(method) ? "ChuyenKhoan" : method };

                DatabaseHelper.ExecuteStoredProcedureNonQuery("dbo.sp_NapTienVi", p1, p2, p3);
                return true;
            }
            catch (SqlException ex) when (ex.Number == 2812) // Fallback nếu chưa nạp procedure
            {
                return TopUpWalletFallback(maKH, amount, method, out errorMessage);
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private bool TopUpWalletFallback(int maKH, decimal amount, string method, out string errorMessage)
        {
            errorMessage = string.Empty;
            using var conn = DatabaseHelper.GetOpenConnection();
            using var trans = conn.BeginTransaction();
            try
            {
                string sqlCust = "UPDATE dbo.KhachHang SET SoDuVi = SoDuVi + @Amount WHERE MaKH = @MaKH";
                using (var cmd1 = new SqlCommand(sqlCust, conn, trans))
                {
                    cmd1.Parameters.Add(new SqlParameter("@Amount", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = amount });
                    cmd1.Parameters.Add(new SqlParameter("@MaKH", SqlDbType.Int) { Value = maKH });
                    int affected = cmd1.ExecuteNonQuery();
                    if (affected == 0)
                    {
                        throw new InvalidOperationException("Không tìm thấy khách hàng trong hệ thống!");
                    }
                }

                string sqlLog = @"INSERT INTO dbo.LichSuNapTien (MaKH, SoTien, PhuongThuc, ThoiGian, TrangThai)
                                  VALUES (@MaKH, @Amount, @Method, GETDATE(), N'ThanhCong')";
                using (var cmd2 = new SqlCommand(sqlLog, conn, trans))
                {
                    cmd2.Parameters.Add(new SqlParameter("@MaKH", SqlDbType.Int) { Value = maKH });
                    cmd2.Parameters.Add(new SqlParameter("@Amount", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = amount });
                    cmd2.Parameters.Add(new SqlParameter("@Method", SqlDbType.NVarChar, 50) { Value = method });
                    cmd2.ExecuteNonQuery();
                }

                trans.Commit();
                return true;
            }
            catch (Exception ex)
            {
                try { trans.Rollback(); } catch { }
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Lấy danh sách lịch sử đổi pin của toàn hệ thống.
        /// </summary>
        public List<GiaoDichDoiPin> GetSwapHistory()
        {
            var list = new List<GiaoDichDoiPin>();
            string sql = @"
                SELECT 
                    gd.MaGD, gd.MaKH, kh.HoTen AS TenKhachHang, kh.SoDienThoai, gd.MaTram,
                    gd.MaPinTra, gd.SoC_PinTra, gd.NganTra,
                    gd.MaPinNhan, gd.SoC_PinNhan, gd.NganNhan,
                    gd.ThoiGianBatDau, gd.ThoiGianKetThuc, gd.PhiDoiPin, gd.TrangThai, gd.GhiChu
                FROM dbo.LichSuDoiPin gd
                INNER JOIN dbo.KhachHang kh ON gd.MaKH = kh.MaKH
                ORDER BY gd.MaGD DESC";

            DataTable dt = DatabaseHelper.ExecuteDataTable(sql);
            foreach (DataRow row in dt.Rows)
            {
                list.Add(new GiaoDichDoiPin
                {
                    MaGD = Convert.ToInt32(row["MaGD"]),
                    MaKH = Convert.ToInt32(row["MaKH"]),
                    TenKhachHang = row["TenKhachHang"] != DBNull.Value ? row["TenKhachHang"].ToString() : string.Empty,
                    SoDienThoai = row["SoDienThoai"] != DBNull.Value ? row["SoDienThoai"].ToString() : string.Empty,
                    MaTram = row["MaTram"] != DBNull.Value ? row["MaTram"].ToString()! : string.Empty,
                    MaPinTra = row["MaPinTra"] != DBNull.Value ? row["MaPinTra"].ToString()! : string.Empty,
                    SoC_PinTra = row["SoC_PinTra"] != DBNull.Value ? Convert.ToDecimal(row["SoC_PinTra"]) : 0.0m,
                    NganTra = row["NganTra"] != DBNull.Value ? Convert.ToInt32(row["NganTra"]) : null,
                    MaPinNhan = row["MaPinNhan"] != DBNull.Value ? row["MaPinNhan"].ToString()! : string.Empty,
                    SoC_PinNhan = row["SoC_PinNhan"] != DBNull.Value ? Convert.ToDecimal(row["SoC_PinNhan"]) : 0.0m,
                    NganNhan = row["NganNhan"] != DBNull.Value ? Convert.ToInt32(row["NganNhan"]) : null,
                    ThoiGianBatDau = row["ThoiGianBatDau"] != DBNull.Value ? Convert.ToDateTime(row["ThoiGianBatDau"]) : DateTime.Now,
                    ThoiGianKetThuc = row["ThoiGianKetThuc"] != DBNull.Value ? Convert.ToDateTime(row["ThoiGianKetThuc"]) : null,
                    PhiDoiPin = row["PhiDoiPin"] != DBNull.Value ? Convert.ToDecimal(row["PhiDoiPin"]) : 25000.0m,
                    TrangThai = row["TrangThai"] != DBNull.Value ? row["TrangThai"].ToString()! : "HoanThanh",
                    GhiChu = row["GhiChu"] != DBNull.Value ? row["GhiChu"].ToString() : null
                });
            }
            return list;
        }

        /// <summary>
        /// Lấy danh sách lịch sử nạp tiền vào ví.
        /// </summary>
        public List<GiaoDichVi> GetTopUpHistory()
        {
            var list = new List<GiaoDichVi>();
            string sql = @"
                SELECT ls.MaNap, ls.MaKH, kh.HoTen AS TenKhachHang, ls.SoTien, ls.PhuongThuc, ls.MaGiaoDichNganHang, ls.ThoiGian, ls.TrangThai
                FROM dbo.LichSuNapTien ls
                INNER JOIN dbo.KhachHang kh ON ls.MaKH = kh.MaKH
                ORDER BY ls.MaNap DESC";

            DataTable dt = DatabaseHelper.ExecuteDataTable(sql);
            foreach (DataRow row in dt.Rows)
            {
                list.Add(new GiaoDichVi
                {
                    MaNap = Convert.ToInt32(row["MaNap"]),
                    MaKH = Convert.ToInt32(row["MaKH"]),
                    TenKhachHang = row["TenKhachHang"] != DBNull.Value ? row["TenKhachHang"].ToString() : null,
                    SoTien = row["SoTien"] != DBNull.Value ? Convert.ToDecimal(row["SoTien"]) : 0.0m,
                    PhuongThuc = row["PhuongThuc"] != DBNull.Value ? row["PhuongThuc"].ToString()! : "ChuyenKhoan",
                    MaGiaoDichNganHang = row["MaGiaoDichNganHang"] != DBNull.Value ? row["MaGiaoDichNganHang"].ToString() : null,
                    ThoiGian = row["ThoiGian"] != DBNull.Value ? Convert.ToDateTime(row["ThoiGian"]) : DateTime.Now,
                    TrangThai = row["TrangThai"] != DBNull.Value ? row["TrangThai"].ToString()! : "ThanhCong"
                });
            }
            return list;
        }
    }
}
