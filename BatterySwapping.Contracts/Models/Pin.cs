namespace BatterySwapping.Contracts.Models;
public class Pin {
        public string MaPin { get; set; } = string.Empty;
        public string LoaiPin { get; set; } = "Lithium LFP 72V-30Ah";
        public int DungLuongThietKe { get; set; } = 30; // Ah
        public decimal SoC { get; set; } = 100.0m; // State of Charge (0 - 100%)
        public decimal SoH { get; set; } = 100.0m; // State of Health (0 - 100%)
        public decimal NhietDo { get; set; } = 25.0m; // Độ C
        public decimal DienAp { get; set; } = 72.0m; // Volts
        public decimal DongDien { get; set; } = 0.0m; // Amperes
        public int SoLanSac { get; set; } = 0;
        public string TrangThai { get; set; } = "DaDay"; // DaDay, DangSac, KhaDung, DangSuDung, Loi, LoiQuaNhiet
        public DateTime NgayCapNhatCuoi { get; set; } = DateTime.Now;
        public string SoCFormatted => $"{SoC:F1}%";
        public string SoHFormatted => $"{SoH:F1}%";
        public string NhietDoFormatted => $"{NhietDo:F1} °C";
        public string DienApFormatted => $"{DienAp:F1} V";
        public string DongDienFormatted => $"{DongDien:F1} A";
}
