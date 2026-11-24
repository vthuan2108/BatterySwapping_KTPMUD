using System;

namespace BatterySwapping.App.Models
{
    /// <summary>
    /// Thực thể quản lý lịch sử nạp tiền và biến động số dư ví khách hàng.
    /// </summary>
    public class GiaoDichVi
    {
        public int MaNap { get; set; }
        public int MaKH { get; set; }
        public string? TenKhachHang { get; set; }
        public decimal SoTien { get; set; }
        public string PhuongThuc { get; set; } = "ChuyenKhoan"; // ChuyenKhoan, TienMat, TheNganHang
        public string? MaGiaoDichNganHang { get; set; }
        public DateTime ThoiGian { get; set; } = DateTime.Now;
        public string TrangThai { get; set; } = "ThanhCong";

        public string SoTienFormatted => string.Format("+{0:N0} đ", SoTien);
        public string ThoiGianFormatted => ThoiGian.ToString("dd/MM/yyyy HH:mm:ss");
    }
}
