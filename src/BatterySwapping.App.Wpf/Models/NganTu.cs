using System;
using System.Windows.Media;

namespace BatterySwapping.App.Models
{
    /// <summary>
    /// Thực thể quản lý ngăn tủ sạc pin tại trạm.
    /// Chuẩn hóa hiển thị màu sắc và đảm bảo an toàn truy cập chéo luồng trong WPF.
    /// </summary>
    public class NganTu
    {
        public int MaNgan { get; set; }
        public string MaTram { get; set; } = string.Empty;
        public int SoThuTu { get; set; } // 1 .. 8
        public bool CuaDangMo { get; set; } = false;
        public bool KhoaBaoTri { get; set; } = false;
        public string? MaPinHienTai { get; set; }
        public string TrangThaiNgan { get; set; } = "Trong"; // Trong, CoPin, DangSac, Loi
        public DateTime NgayCapNhat { get; set; } = DateTime.Now;

        // Dữ liệu mở rộng khi JOIN với bảng Pin
        public string? LoaiPin { get; set; }
        public decimal SoC { get; set; } = 0.0m;
        public decimal SoH { get; set; } = 0.0m;
        public decimal NhietDo { get; set; } = 25.0m;
        public decimal DienAp { get; set; } = 0.0m;
        public decimal DongDien { get; set; } = 0.0m;
        public string? TrangThaiPin { get; set; }

        public bool KhoaNhietTamThoi { get; private set; }
        public bool KhoaLoiNhiet { get; private set; }
        public void ApplyCooldown(BatterySwapping.Contracts.SlotCooldown? cooldown)
        {
            bool active=cooldown?.ClearedAt==null && cooldown!=null;
            KhoaLoiNhiet=active&&(cooldown!.RequiresAdmin||cooldown.IncidentCount>=3);
            KhoaNhietTamThoi=active&&!KhoaLoiNhiet;
        }

        // Bảng màu Brush tĩnh chuẩn hóa cho giao diện WPF (Freeze để an toàn đa luồng)
        private static readonly SolidColorBrush BrushRed = new(Color.FromRgb(0xEF, 0x44, 0x44));
        private static readonly SolidColorBrush BrushGreen = new(Color.FromRgb(0x10, 0xB9, 0x81));
        private static readonly SolidColorBrush BrushAmber = new(Color.FromRgb(0xF5, 0x9E, 0x0B));
        private static readonly SolidColorBrush BrushCyan = new(Color.FromRgb(0x38, 0xBD, 0xF8));
        private static readonly SolidColorBrush BrushGray = new(Color.FromRgb(0x64, 0x74, 0x8B));
        private static readonly SolidColorBrush BrushBorderDark = new(Color.FromRgb(0x33, 0x41, 0x55));
        private static readonly SolidColorBrush BrushBgDark = new(Color.FromRgb(0x0F, 0x17, 0x2A));
        private static readonly SolidColorBrush BrushBgOpen = new(Color.FromRgb(0x08, 0x2F, 0x49));
        private static readonly SolidColorBrush BrushBgCooling = new(Color.FromRgb(0x3F, 0x2D, 0x0C));
        private static readonly SolidColorBrush BrushBgDanger = new(Color.FromRgb(0x45, 0x0A, 0x0A));
        private static readonly SolidColorBrush BrushDoorClosed = new(Color.FromRgb(0x47, 0x55, 0x69));

        static NganTu()
        {
            if (BrushRed.CanFreeze) BrushRed.Freeze();
            if (BrushGreen.CanFreeze) BrushGreen.Freeze();
            if (BrushAmber.CanFreeze) BrushAmber.Freeze();
            if (BrushCyan.CanFreeze) BrushCyan.Freeze();
            if (BrushGray.CanFreeze) BrushGray.Freeze();
            if (BrushBorderDark.CanFreeze) BrushBorderDark.Freeze();
            if (BrushBgDark.CanFreeze) BrushBgDark.Freeze();
            if (BrushBgOpen.CanFreeze) BrushBgOpen.Freeze();
            if (BrushBgCooling.CanFreeze) BrushBgCooling.Freeze();
            if (BrushBgDanger.CanFreeze) BrushBgDanger.Freeze();
            if (BrushDoorClosed.CanFreeze) BrushDoorClosed.Freeze();
        }

        // Trạng thái hiển thị giao diện phần cứng tủ pin thực tế
        public SolidColorBrush MauTrangThai => KhoaLoiNhiet ? BrushRed
            : KhoaNhietTamThoi || KhoaBaoTri || NhietDo >= 55.0m ? BrushAmber
            : CuaDangMo ? BrushCyan
            : string.IsNullOrEmpty(MaPinHienTai) ? BrushGray
            : SoC >= 90.0m ? BrushGreen
            : BrushAmber;

        public string MoTaTrangThai => KhoaLoiNhiet ? "LỖI NHIỆT / CHỜ ADMIN"
            : KhoaNhietTamThoi ? "LÀM MÁT / KHÓA TẠM"
            : KhoaBaoTri ? "KHÓA BẢO TRÌ"
            : NhietDo >= 55.0m ? "NHIỆT CAO / CÁCH LY"
            : CuaDangMo ? "CỬA ĐANG MỞ"
            : string.IsNullOrEmpty(MaPinHienTai) ? "NGĂN TRỐNG"
            : SoC >= 90.0m ? $"ĐÃ ĐẦY ({SoC:N0}%)"
            : $"ĐANG SẠC ({SoC:N0}%)";

        public SolidColorBrush MauNhietDo => NhietDo >= 55.0m ? BrushRed : (NhietDo >= 50.0m ? BrushAmber : BrushGreen);

        public string TenHocTu => $"HỘC #{SoThuTu:D2}";
        public string TrangThaiCuaText => CuaDangMo ? "🔓 CỬA MỞ" : "🔒 ĐÓNG";
        public SolidColorBrush MauTrangThaiCua => CuaDangMo ? BrushCyan : BrushDoorClosed;
        public bool CoPin => !string.IsNullOrEmpty(MaPinHienTai);
        public string MoTaPin => string.IsNullOrEmpty(MaPinHienTai) ? "[KHOANG TRỐNG]" : $"{MaPinHienTai} ({SoC:N0}%)";
        public SolidColorBrush MauVienHoc => KhoaLoiNhiet ? BrushRed : KhoaNhietTamThoi || KhoaBaoTri || NhietDo >= 55m ? BrushAmber : CuaDangMo ? BrushCyan : SoC >= 90m && CoPin ? BrushGreen : BrushBorderDark;
        public SolidColorBrush NenKhoangTu => KhoaLoiNhiet ? BrushBgDanger : KhoaNhietTamThoi || KhoaBaoTri || NhietDo >= 55m ? BrushBgCooling : CuaDangMo ? BrushBgOpen : BrushBgDark;
    }
}
