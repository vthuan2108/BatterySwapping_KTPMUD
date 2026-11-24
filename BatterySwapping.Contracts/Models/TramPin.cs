namespace BatterySwapping.Contracts.Models;
public class TramPin {
        public string MaTram { get; set; } = string.Empty;
        public string TenTram { get; set; } = string.Empty;
        public string DiaChi { get; set; } = string.Empty;
        public string? ToaDoGPS { get; set; }
        public int SoLuongNgan { get; set; } = 8;
        public string TrangThaiHoatDong { get; set; } = "HoatDong"; // HoatDong, BaoTri, TamDung
        public DateTime NgayLapDat { get; set; } = DateTime.Now;
}
