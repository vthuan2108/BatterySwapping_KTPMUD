namespace BatterySwapping.Contracts.Models;
public class CanhBaoIoT {
        public int MaCB { get; set; }
        public string MaTram { get; set; } = string.Empty;
        public int? MaNgan { get; set; }
        public string? MaPin { get; set; }
        public string LoaiCanhBao { get; set; } = string.Empty;
        public decimal? GiaTriDo { get; set; }
        public DateTime ThoiGianPhatHien { get; set; } = DateTime.Now;
        public string MucDoNghiemTrong { get; set; } = "CanhBao"; // ThongTin, CanhBao, KhanCap
        public bool DaXuLy { get; set; } = false;
        public string? NguoiXuLy { get; set; }
        public DateTime? ThoiGianXuLy { get; set; }
        public string? GhiChu { get; set; }
        public string DaXuLyText => DaXuLy ? "Đã xử lý" : "Chưa xử lý";
        public string GiaTriDoFormatted => GiaTriDo.HasValue ? $"{GiaTriDo.Value:F1} °C" : "--";
        public string ViTriFormatted => MaNgan.HasValue ? $"Ngăn #{MaNgan.Value:D2}" : "Toàn trạm";
}
