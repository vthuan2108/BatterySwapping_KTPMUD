using System;

namespace BatterySwapping.App.Models
{
    /// <summary>
    /// Thực thể quản lý thông tin khách hàng, phương tiện và số dư ví điện tử.
    /// </summary>
    public class KhachHang
    {
        public int MaKH { get; set; }
        public int? MaTK { get; set; }
        public string HoTen { get; set; } = string.Empty;
        public string SoDienThoai { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string BienSoXe { get; set; } = string.Empty;
        public decimal SoDuVi { get; set; } = 0.0m;
        public string? MaPinDangMuon { get; set; }
        public DateTime NgayDangKy { get; set; } = DateTime.Now;

        // Thuộc tính tiện ích hiển thị
        public string SoDuFormatted => string.Format("{0:N0} đ", SoDuVi);
        public string PinDangMuonDisplay => !string.IsNullOrEmpty(MaPinDangMuon) ? MaPinDangMuon : "Chưa mượn pin";
    }
}
