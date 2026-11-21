using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BatterySwapping.App.Helpers
{
    /// <summary>
    /// Lớp tiện ích xử lý băm và kiểm thực mật khẩu người dùng theo chuẩn thuật toán mật mã học SHA-256.
    /// Cung cấp cơ chế tự động nhận diện chuỗi băm (Hash Detection) và hỗ trợ nâng cấp (Migration) dữ liệu cũ an toàn.
    /// </summary>
    public static class PasswordHelper
    {
        private static readonly Regex Sha256HexRegex = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

        /// <summary>
        /// Băm mật khẩu văn bản thuần (plain text) bằng thuật toán an toàn SHA-256.
        /// </summary>
        /// <param name="password">Chuỗi mật khẩu dạng văn bản thuần nhập từ người dùng.</param>
        /// <returns>Chuỗi ký tự Hexadecimal viết thường gồm 64 ký tự đại diện cho mã băm SHA-256.</returns>
        public static string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return string.Empty;

            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            var builder = new StringBuilder(64);
            foreach (var b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }
            return builder.ToString();
        }

        /// <summary>
        /// Kiểm tra một chuỗi lưu trữ có phải là mã băm SHA-256 tiêu chuẩn (64 ký tự hex) hay không.
        /// </summary>
        /// <param name="hashString">Chuỗi kiểm tra từ cơ sở dữ liệu.</param>
        /// <returns>True nếu là định dạng mã băm SHA-256 64 ký tự; ngược lại False.</returns>
        public static bool IsSha256(string? hashString)
        {
            if (string.IsNullOrWhiteSpace(hashString) || hashString.Length != 64)
                return false;

            return Sha256HexRegex.IsMatch(hashString);
        }

        /// <summary>
        /// Kiểm thực mật khẩu đầu vào có khớp với mã lưu trữ trong CSDL.
        /// Hỗ trợ kiểm tra song song: mã băm SHA-256 chuẩn hóa và văn bản thuần (fallback cho dữ liệu hạt giống ban đầu).
        /// </summary>
        /// <param name="inputPassword">Mật khẩu người dùng nhập vào trên giao diện.</param>
        /// <param name="storedHash">Mật khẩu hoặc mã băm đang lưu trong bảng TaiKhoan.</param>
        /// <returns>True nếu mật khẩu hợp lệ; ngược lại False.</returns>
        public static bool VerifyPassword(string inputPassword, string storedHash)
        {
            if (string.IsNullOrEmpty(inputPassword) || string.IsNullOrEmpty(storedHash))
                return false;

            // 1. Kiểm tra khớp trực tiếp văn bản thuần (hỗ trợ cơ chế tương thích ngược / seed data)
            if (!IsSha256(storedHash) && inputPassword == storedHash)
                return true;

            // 2. Kiểm tra khớp mã băm SHA-256
            string hashedInput = HashPassword(inputPassword);
            return string.Equals(hashedInput, storedHash, StringComparison.OrdinalIgnoreCase);
        }
    }
}
