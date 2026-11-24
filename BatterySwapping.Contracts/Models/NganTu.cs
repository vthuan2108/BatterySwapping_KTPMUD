namespace BatterySwapping.Contracts.Models;
public class NganTu {
        public int MaNgan { get; set; }
        public string MaTram { get; set; } = string.Empty;
        public int SoThuTu { get; set; } // 1 .. 8
        public bool CuaDangMo { get; set; } = false;
        public bool KhoaBaoTri { get; set; } = false;
        public string? MaPinHienTai { get; set; }
        public string TrangThaiNgan { get; set; } = "Trong"; // Trong, CoPin, DangSac, Loi
        public DateTime NgayCapNhat { get; set; } = DateTime.Now;
        public string? LoaiPin { get; set; }
        public decimal SoC { get; set; } = 0.0m;
        public decimal SoH { get; set; } = 0.0m;
        public decimal NhietDo { get; set; } = 25.0m;
        public decimal DienAp { get; set; } = 0.0m;
        public decimal DongDien { get; set; } = 0.0m;
        public string? TrangThaiPin { get; set; }
        public string TenHocTu => $"HỘC #{SoThuTu:D2}";
        public string TrangThaiCuaText => CuaDangMo ? "🔓 CỬA MỞ" : "🔒 ĐÓNG";
        public string MoTaPin => string.IsNullOrEmpty(MaPinHienTai) ? "[KHOANG TRỐNG]" : $"{MaPinHienTai} ({SoC:N0}%)";
}