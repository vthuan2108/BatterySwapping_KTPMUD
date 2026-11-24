namespace BatterySwapping.Contracts.Models;
public class TaiKhoan {
        public int MaTK { get; set; }
        public string TenDangNhap { get; set; } = string.Empty;
        public string MatKhau { get; set; } = string.Empty;
        public string HoTen { get; set; } = string.Empty;
        public string VaiTro { get; set; } = "KhachHang"; // Admin, KyThuatVien, KhachHang
        public string TrangThai { get; set; } = "HoatDong"; // HoatDong, BiKhoa
        public DateTime NgayTao { get; set; } = DateTime.Now;
}
