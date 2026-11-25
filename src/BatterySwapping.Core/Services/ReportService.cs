using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using BatterySwapping.Core.Helpers;
using BatterySwapping.Contracts.Models;

namespace BatterySwapping.Core.Services
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
            string sql = "SELECT MaKH, MaTK, HoTen, SoDienThoai, Email, BienSoXe, SoDuVi, MaPinDangMuon, MaTheRFID, MaQR, NgayDangKy FROM dbo.KhachHang ORDER BY MaKH DESC";
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
                    MaTheRFID = dt.Columns.Contains("MaTheRFID") && row["MaTheRFID"] != DBNull.Value ? row["MaTheRFID"].ToString() : null,
                    MaQR = dt.Columns.Contains("MaQR") && row["MaQR"] != DBNull.Value ? row["MaQR"].ToString() : null,
                    NgayDangKy = row["NgayDangKy"] != DBNull.Value ? Convert.ToDateTime(row["NgayDangKy"]) : DateTime.Now
                });
            }
            return list;
        }

        /// <summary>
        /// Nạp tiền vào ví khách hàng và lưu lại lịch sử với bảo vệ toàn vẹn giao dịch ACID.
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
