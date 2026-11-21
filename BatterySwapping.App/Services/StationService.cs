using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using BatterySwapping.App.Helpers;
using BatterySwapping.App.Models;

namespace BatterySwapping.App.Services
{
    /// <summary>
    /// Dịch vụ quản trị hệ thống trạm đổi pin và điều khiển các ngăn tủ thông minh.
    /// </summary>
    public class StationService
    {
        /// <summary>
        /// Lấy toàn bộ danh sách các trạm pin trong mạng lưới kèm xử lý an toàn giá trị NULL.
        /// </summary>
        public List<TramPin> GetAllStations()
        {
            var list = new List<TramPin>();
            string sql = "SELECT MaTram, TenTram, DiaChi, ToaDoGPS, SoLuongNgan, TrangThaiHoatDong, NgayLapDat FROM dbo.TramPin ORDER BY MaTram";
            DataTable dt = DatabaseHelper.ExecuteDataTable(sql);

            foreach (DataRow row in dt.Rows)
            {
                list.Add(new TramPin
                {
                    MaTram = row["MaTram"] != DBNull.Value ? row["MaTram"].ToString()! : string.Empty,
                    TenTram = row["TenTram"] != DBNull.Value ? row["TenTram"].ToString()! : string.Empty,
                    DiaChi = row["DiaChi"] != DBNull.Value ? row["DiaChi"].ToString()! : string.Empty,
                    ToaDoGPS = row["ToaDoGPS"] != DBNull.Value ? row["ToaDoGPS"].ToString() : null,
                    SoLuongNgan = row["SoLuongNgan"] != DBNull.Value ? Convert.ToInt32(row["SoLuongNgan"]) : 8,
                    TrangThaiHoatDong = row["TrangThaiHoatDong"] != DBNull.Value ? row["TrangThaiHoatDong"].ToString()! : "HoatDong",
                    NgayLapDat = row["NgayLapDat"] != DBNull.Value ? Convert.ToDateTime(row["NgayLapDat"]) : DateTime.Now
                });
            }
            return list;
        }

        /// <summary>
        /// Lấy chi tiết trạng thái 8 ngăn tủ sạc của một trạm chỉ định kèm thông tin pin.
        /// </summary>
        public List<NganTu> GetCabinetSlots(string maTram)
        {
            var list = new List<NganTu>();
            string sql = @"
                SELECT 
                    nt.MaNgan, nt.MaTram, nt.SoThuTu, nt.CuaDangMo, nt.KhoaBaoTri, nt.MaPinHienTai, nt.TrangThaiNgan, nt.NgayCapNhat,
                    p.LoaiPin, 
                    p.SoC, 
                    p.SoH, 
                    p.NhietDo, 
                    p.DienAp, 
                    p.DongDien, 
                    p.TrangThai AS TrangThaiPin
                FROM dbo.NganTu nt
                LEFT JOIN dbo.Pin p ON nt.MaPinHienTai = p.MaPin
                WHERE nt.MaTram = @MaTram
                ORDER BY nt.SoThuTu ASC";

            var param = new SqlParameter("@MaTram", SqlDbType.VarChar, 20) { Value = maTram };
            DataTable dt = DatabaseHelper.ExecuteDataTable(sql, param);

            foreach (DataRow row in dt.Rows)
            {
                list.Add(new NganTu
                {
                    MaNgan = Convert.ToInt32(row["MaNgan"]),
                    MaTram = row["MaTram"] != DBNull.Value ? row["MaTram"].ToString()! : string.Empty,
                    SoThuTu = Convert.ToInt32(row["SoThuTu"]),
                    CuaDangMo = row["CuaDangMo"] != DBNull.Value && Convert.ToBoolean(row["CuaDangMo"]),
                    KhoaBaoTri = row["KhoaBaoTri"] != DBNull.Value && Convert.ToBoolean(row["KhoaBaoTri"]),
                    MaPinHienTai = row["MaPinHienTai"] != DBNull.Value ? row["MaPinHienTai"].ToString() : null,
                    TrangThaiNgan = row["TrangThaiNgan"] != DBNull.Value ? row["TrangThaiNgan"].ToString()! : "Trong",
                    NgayCapNhat = row["NgayCapNhat"] != DBNull.Value ? Convert.ToDateTime(row["NgayCapNhat"]) : DateTime.Now,
                    LoaiPin = row["LoaiPin"] != DBNull.Value ? row["LoaiPin"].ToString() : null,
                    SoC = row["SoC"] != DBNull.Value ? Convert.ToDecimal(row["SoC"]) : 0.0m,
                    SoH = row["SoH"] != DBNull.Value ? Convert.ToDecimal(row["SoH"]) : 0.0m,
                    NhietDo = row["NhietDo"] != DBNull.Value ? Convert.ToDecimal(row["NhietDo"]) : 25.0m,
                    DienAp = row["DienAp"] != DBNull.Value ? Convert.ToDecimal(row["DienAp"]) : 0.0m,
                    DongDien = row["DongDien"] != DBNull.Value ? Convert.ToDecimal(row["DongDien"]) : 0.0m,
                    TrangThaiPin = row["TrangThaiPin"] != DBNull.Value ? row["TrangThaiPin"].ToString() : null
                });
            }
            return list;
        }

        /// <summary>
        /// Thêm mới một trạm sạc và tự động sinh các ngăn tủ mặc định trong cùng một Transaction an toàn.
        /// </summary>
        public bool AddStation(TramPin tram, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (tram == null || string.IsNullOrWhiteSpace(tram.MaTram) || string.IsNullOrWhiteSpace(tram.TenTram))
            {
                errorMessage = "Mã trạm và tên trạm không được để trống!";
                return false;
            }

            SqlConnection? conn = null;
            SqlTransaction? trans = null;

            try
            {
                conn = DatabaseHelper.GetOpenConnection();
                trans = conn.BeginTransaction();

                // Kiểm tra mã trạm đã tồn tại chưa
                string sqlCheck = "SELECT COUNT(*) FROM dbo.TramPin WHERE MaTram = @MaTram";
                using (var cmdCheck = new SqlCommand(sqlCheck, conn, trans))
                {
                    cmdCheck.Parameters.Add(new SqlParameter("@MaTram", SqlDbType.VarChar, 20) { Value = tram.MaTram.Trim() });
                    int exists = Convert.ToInt32(cmdCheck.ExecuteScalar());
                    if (exists > 0)
                    {
                        errorMessage = $"Mã trạm '{tram.MaTram}' đã tồn tại trong hệ thống!";
                        trans.Rollback();
                        return false;
                    }
                }

                string sqlTram = @"INSERT INTO dbo.TramPin (MaTram, TenTram, DiaChi, ToaDoGPS, SoLuongNgan, TrangThaiHoatDong, NgayLapDat)
                                  VALUES (@MaTram, @TenTram, @DiaChi, @ToaDoGPS, @SoLuongNgan, @TrangThai, GETDATE())";

                using (var cmd = new SqlCommand(sqlTram, conn, trans))
                {
                    cmd.Parameters.Add(new SqlParameter("@MaTram", SqlDbType.VarChar, 20) { Value = tram.MaTram.Trim() });
                    cmd.Parameters.Add(new SqlParameter("@TenTram", SqlDbType.NVarChar, 100) { Value = tram.TenTram.Trim() });
                    cmd.Parameters.Add(new SqlParameter("@DiaChi", SqlDbType.NVarChar, 255) { Value = tram.DiaChi.Trim() });
                    cmd.Parameters.Add(new SqlParameter("@ToaDoGPS", SqlDbType.VarChar, 50) { Value = (object?)tram.ToaDoGPS ?? DBNull.Value });
                    cmd.Parameters.Add(new SqlParameter("@SoLuongNgan", SqlDbType.Int) { Value = tram.SoLuongNgan > 0 ? tram.SoLuongNgan : 8 });
                    cmd.Parameters.Add(new SqlParameter("@TrangThai", SqlDbType.NVarChar, 30) { Value = string.IsNullOrWhiteSpace(tram.TrangThaiHoatDong) ? "HoatDong" : tram.TrangThaiHoatDong });
                    cmd.ExecuteNonQuery();
                }

                // Tự động tạo các ngăn tủ trống cho trạm mới
                int slotCount = tram.SoLuongNgan > 0 ? tram.SoLuongNgan : 8;
                for (int i = 1; i <= slotCount; i++)
                {
                    string sqlNgan = @"INSERT INTO dbo.NganTu (MaTram, SoThuTu, CuaDangMo, KhoaBaoTri, MaPinHienTai, TrangThaiNgan, NgayCapNhat)
                                      VALUES (@MaTram, @SoThuTu, 0, 0, NULL, N'Trong', GETDATE())";
                    using var cmdNgan = new SqlCommand(sqlNgan, conn, trans);
                    cmdNgan.Parameters.Add(new SqlParameter("@MaTram", SqlDbType.VarChar, 20) { Value = tram.MaTram.Trim() });
                    cmdNgan.Parameters.Add(new SqlParameter("@SoThuTu", SqlDbType.Int) { Value = i });
                    cmdNgan.ExecuteNonQuery();
                }

                trans.Commit();
                return true;
            }
            catch (Exception ex)
            {
                try { trans?.Rollback(); } catch { }
                errorMessage = ex.Message;
                return false;
            }
            finally
            {
                trans?.Dispose();
                conn?.Dispose();
            }
        }

        /// <summary>
        /// Phát lệnh mở hoặc đóng cửa ngăn tủ.
        /// </summary>
        public bool SetDoorState(string maTram, int soThuTu, bool openDoor)
        {
            string sql = "UPDATE dbo.NganTu SET CuaDangMo = @CuaDangMo, NgayCapNhat = GETDATE() WHERE MaTram = @MaTram AND SoThuTu = @SoThuTu AND (@CuaDangMo = 0 OR KhoaBaoTri = 0)";
            var p1 = new SqlParameter("@CuaDangMo", SqlDbType.Bit) { Value = openDoor };
            var p2 = new SqlParameter("@MaTram", SqlDbType.VarChar, 20) { Value = maTram };
            var p3 = new SqlParameter("@SoThuTu", SqlDbType.Int) { Value = soThuTu };

            return DatabaseHelper.ExecuteNonQuery(sql, p1, p2, p3) > 0;
        }

        /// <summary>
        /// Khóa hoặc mở khóa bảo trì ngăn tủ khi có sự cố kỹ thuật.
        /// </summary>
        public bool SetMaintenanceLock(string maTram, int soThuTu, bool lockState)
        {
            string sql = "UPDATE dbo.NganTu SET KhoaBaoTri = @KhoaBaoTri, NgayCapNhat = GETDATE() WHERE MaTram = @MaTram AND SoThuTu = @SoThuTu";
            var p1 = new SqlParameter("@KhoaBaoTri", SqlDbType.Bit) { Value = lockState };
            var p2 = new SqlParameter("@MaTram", SqlDbType.VarChar, 20) { Value = maTram };
            var p3 = new SqlParameter("@SoThuTu", SqlDbType.Int) { Value = soThuTu };

            return DatabaseHelper.ExecuteNonQuery(sql, p1, p2, p3) > 0;
        }
    }
}
