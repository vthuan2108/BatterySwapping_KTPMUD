namespace BatterySwapping.Contracts.Models;
public class KhachHang {
        public int MaKH { get; set; }
        public int? MaTK { get; set; }
        public string HoTen { get; set; } = string.Empty;
        public string SoDienThoai { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string BienSoXe { get; set; } = string.Empty;
        public decimal SoDuVi { get; set; } = 0.0m;
        public string? MaPinDangMuon { get; set; }
        public DateTime NgayDangKy { get; set; } = DateTime.Now;
        public string SoDuFormatted => string.Format("{0:N0} đ", SoDuVi);
        public string PinDangMuonDisplay => !string.IsNullOrEmpty(MaPinDangMuon) ? MaPinDangMuon : "Chưa mượn pin";
}
