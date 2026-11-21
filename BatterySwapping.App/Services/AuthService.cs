using System;
using System.Data;
using Microsoft.Data.SqlClient;
using BatterySwapping.App.Helpers;
using BatterySwapping.App.Models;

namespace BatterySwapping.App.Services
{
    /// <summary>
    /// Dịch vụ xử lý đăng nhập, xác thực danh tính, quản lý phiên làm việc và mã hóa mật khẩu người dùng.
    /// Tích hợp cơ chế tự động di chuyển (Password Migration) nâng cấp dữ liệu hạt giống cũ sang chuẩn băm SHA-256.
    /// </summary>
    public class AuthService
    {
        /// <summary>
        /// Thông tin tài khoản người dùng hiện đang đăng nhập trong phiên làm việc.
        /// </summary>
        public static TaiKhoan? CurrentUser { get; private set; }

        /// <summary>
        /// Xác thực tài khoản người dùng dựa trên tên đăng nhập và mật khẩu.
        /// Tự động di chuyển (auto-migrate) mật khẩu dạng văn bản thuần sang mã băm SHA-256 khi đăng nhập thành công.
        /// </summary>
        /// <param name="username">Tên đăng nhập hệ thống.</param>
        /// <param name="password">Mật khẩu dạng văn bản thuần.</param>
        /// <param name="errorMessage">Thông điệp lỗi trả về nếu xác thực thất bại.</param>
        /// <returns>True nếu đăng nhập thành công; ngược lại False.</returns>
        public bool Login(string username, string password, out string errorMessage)
        {
            errorMessage = string.Empty;
            CurrentUser = null;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                errorMessage = "Vui lòng nhập đầy đủ tên đăng nhập và mật khẩu!";
                return false;
            }

            try
            {
                string sql = "SELECT MaTK, TenDangNhap, MatKhau, HoTen, VaiTro, TrangThai, NgayTao FROM dbo.TaiKhoan WHERE TenDangNhap = @Username";
                var param = new SqlParameter("@Username", SqlDbType.VarChar, 50) { Value = username.Trim() };
                DataTable dt = DatabaseHelper.ExecuteDataTable(sql, param);

                if (dt.Rows.Count == 0)
                {
                    errorMessage = "Tên đăng nhập không tồn tại trong hệ thống!";
                    return false;
                }

                var row = dt.Rows[0];
                int maTK = Convert.ToInt32(row["MaTK"]);
                string storedHash = row["MatKhau"] != DBNull.Value ? row["MatKhau"].ToString()! : string.Empty;
                string trangThai = row["TrangThai"] != DBNull.Value ? row["TrangThai"].ToString()! : "HoatDong";

                if (trangThai == "BiKhoa")
                {
                    errorMessage = "Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên!";
                    return false;
                }

                if (!PasswordHelper.VerifyPassword(password, storedHash))
                {
                    errorMessage = "Mật khẩu không chính xác!";
                    return false;
                }

                // CƠ CHẾ NÂNG CẤP MẬT KHẨU TỰ ĐỘNG (PASSWORD MIGRATION):
                // Nếu mật khẩu trong CSDL chưa phải là mã băm SHA-256 (64 ký tự hex), tự động băm và ghi đè CSDL
                if (!PasswordHelper.IsSha256(storedHash))
                {
                    MigratePasswordToHash(maTK, password);
                }

                // Đăng nhập thành công, thiết lập phiên làm việc toàn cục
                CurrentUser = new TaiKhoan
                {
                    MaTK = maTK,
                    TenDangNhap = row["TenDangNhap"] != DBNull.Value ? row["TenDangNhap"].ToString()! : string.Empty,
                    HoTen = row["HoTen"] != DBNull.Value ? row["HoTen"].ToString()! : string.Empty,
                    VaiTro = row["VaiTro"] != DBNull.Value ? row["VaiTro"].ToString()! : "KhachHang",
                    TrangThai = trangThai,
                    NgayTao = row["NgayTao"] != DBNull.Value ? Convert.ToDateTime(row["NgayTao"]) : DateTime.Now
                };

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Lỗi kết nối CSDL: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Tự động di chuyển và nâng cấp mật khẩu dạng văn bản thuần sang mã băm SHA-256 trong CSDL.
        /// </summary>
        /// <param name="maTK">Mã định danh tài khoản.</param>
        /// <param name="plainPassword">Mật khẩu văn bản thuần cần băm.</param>
        /// <returns>True nếu cập nhật CSDL thành công; ngược lại False.</returns>
        public static bool MigratePasswordToHash(int maTK, string plainPassword)
        {
            if (maTK <= 0 || string.IsNullOrEmpty(plainPassword)) return false;

            try
            {
                string hashed = PasswordHelper.HashPassword(plainPassword);
                string sql = "UPDATE dbo.TaiKhoan SET MatKhau = @Hashed WHERE MaTK = @MaTK";
                var pHash = new SqlParameter("@Hashed", SqlDbType.VarChar, 256) { Value = hashed };
                var pId = new SqlParameter("@MaTK", SqlDbType.Int) { Value = maTK };

                return DatabaseHelper.ExecuteNonQuery(sql, pHash, pId) > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Đổi mật khẩu người dùng với kiểm thực mật khẩu cũ và băm SHA-256 mật khẩu mới.
        /// </summary>
        /// <param name="maTK">Mã định danh tài khoản.</param>
        /// <param name="oldPassword">Mật khẩu hiện tại.</param>
        /// <param name="newPassword">Mật khẩu mới.</param>
        /// <param name="errorMessage">Thông điệp lỗi nếu đổi mật khẩu thất bại.</param>
        /// <returns>True nếu đổi mật khẩu thành công; ngược lại False.</returns>
        public bool ChangePassword(int maTK, string oldPassword, string newPassword, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            {
                errorMessage = "Mật khẩu mới phải có độ dài tối thiểu 6 ký tự!";
                return false;
            }

            try
            {
                string sqlQuery = "SELECT MatKhau FROM dbo.TaiKhoan WHERE MaTK = @MaTK";
                var pId = new SqlParameter("@MaTK", SqlDbType.Int) { Value = maTK };
                object? res = DatabaseHelper.ExecuteScalar(sqlQuery, pId);

                if (res == null || res == DBNull.Value)
                {
                    errorMessage = "Không tìm thấy thông tin tài khoản!";
                    return false;
                }

                string stored = res.ToString()!;
                if (!PasswordHelper.VerifyPassword(oldPassword, stored))
                {
                    errorMessage = "Mật khẩu hiện tại không chính xác!";
                    return false;
                }

                string hashedNew = PasswordHelper.HashPassword(newPassword);
                string sqlUpdate = "UPDATE dbo.TaiKhoan SET MatKhau = @NewHash WHERE MaTK = @MaTK";
                var pNewHash = new SqlParameter("@NewHash", SqlDbType.VarChar, 256) { Value = hashedNew };
                var pUpdateId = new SqlParameter("@MaTK", SqlDbType.Int) { Value = maTK };

                return DatabaseHelper.ExecuteNonQuery(sqlUpdate, pNewHash, pUpdateId) > 0;
            }
            catch (Exception ex)
            {
                errorMessage = $"Lỗi đổi mật khẩu: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Đăng xuất khỏi hệ thống và hủy phiên làm việc hiện tại.
        /// </summary>
        public void Logout()
        {
            CurrentUser = null;
        }
    }
}
