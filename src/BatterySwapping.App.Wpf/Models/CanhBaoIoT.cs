using System;
using System.Windows.Media;

namespace BatterySwapping.App.Models
{
    /// <summary>
    /// Thực thể quản lý cảnh báo telemetry từ phần cứng trạm IoT (Quá nhiệt, cháy nổ, điện áp bất thường).
    /// Hỗ trợ an toàn đa luồng giữa Thread nền và UI Dispatcher qua Brush Freezing.
    /// </summary>
    public class CanhBaoIoT
    {
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

        private static readonly SolidColorBrush BrushRed = new(Color.FromRgb(0xEF, 0x44, 0x44));
        private static readonly SolidColorBrush BrushAmber = new(Color.FromRgb(0xF5, 0x9E, 0x0B));
        private static readonly SolidColorBrush BrushBlue = new(Color.FromRgb(0x3B, 0x82, 0xF6));

        static CanhBaoIoT()
        {
            // Đóng băng (Freeze) Brushes để an toàn truy cập chéo giữa Thread nền IoT và UI Dispatcher
            if (BrushRed.CanFreeze) BrushRed.Freeze();
            if (BrushAmber.CanFreeze) BrushAmber.Freeze();
            if (BrushBlue.CanFreeze) BrushBlue.Freeze();
        }

        public SolidColorBrush MauMucDo => MucDoNghiemTrong switch
        {
            "KhanCap" => BrushRed,
            "CanhBao" => BrushAmber,
            _ => BrushBlue
        };

        public string DaXuLyText => DaXuLy ? "Đã xử lý" : "Chưa xử lý";
        public string GiaTriDoFormatted => GiaTriDo.HasValue ? $"{GiaTriDo.Value:F1} °C" : "--";
        public string ViTriFormatted => MaNgan.HasValue ? $"Ngăn #{MaNgan.Value:D2}" : "Toàn trạm";
    }
}
