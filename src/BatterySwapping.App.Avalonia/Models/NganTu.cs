using System;
using Avalonia.Media;

namespace BatterySwapping.App.Avalonia.Models;

/// <summary>
/// Thực thể quản lý ngăn tủ sạc pin tại trạm.
/// Chuẩn hóa hiển thị màu sắc và trạng thái trực quan cho Avalonia UI.
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

    // Bảng màu Brush tĩnh chuẩn hóa cho giao diện Avalonia
    private static readonly IBrush BrushRed = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly IBrush BrushGreen = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
    private static readonly IBrush BrushAmber = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly IBrush BrushCyan = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
    private static readonly IBrush BrushGray = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
    private static readonly IBrush BrushBorderDark = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));
    private static readonly IBrush BrushBgDark = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
    private static readonly IBrush BrushBgOpen = new SolidColorBrush(Color.FromRgb(0x08, 0x2F, 0x49));
    private static readonly IBrush BrushBgDanger = new SolidColorBrush(Color.FromRgb(0x45, 0x0A, 0x0A));
    private static readonly IBrush BrushDoorClosed = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));

    // Trạng thái hiển thị giao diện phần cứng tủ pin thực tế
    public IBrush MauTrangThai => KhoaBaoTri || NhietDo >= 55.0m ? BrushRed
        : CuaDangMo ? BrushCyan
        : string.IsNullOrEmpty(MaPinHienTai) ? BrushGray
        : SoC >= 90.0m ? BrushGreen
        : BrushAmber;

    public string MoTaTrangThai => KhoaBaoTri || NhietDo >= 55.0m ? "NGUY HIỂM / KHÓA"
        : CuaDangMo ? "CỬA ĐANG MỞ"
        : string.IsNullOrEmpty(MaPinHienTai) ? "NGĂN TRỐNG"
        : SoC >= 90.0m ? $"ĐÃ ĐẦY ({SoC:N0}%)"
        : $"ĐANG SẠC ({SoC:N0}%)";

    public IBrush MauNhietDo => NhietDo >= 55.0m ? BrushRed : (NhietDo >= 50.0m ? BrushAmber : BrushGreen);

    public string TenHocTu => $"HỘC #{SoThuTu:D2}";
    public string TrangThaiCuaText => CuaDangMo ? "🔓 CỬA MỞ" : "🔒 ĐÓNG";
    public IBrush MauTrangThaiCua => CuaDangMo ? BrushCyan : BrushDoorClosed;
    public bool CoPin => !string.IsNullOrEmpty(MaPinHienTai);
    public string MoTaPin => string.IsNullOrEmpty(MaPinHienTai) ? "[KHOANG TRỐNG]" : $"{MaPinHienTai} ({SoC:N0}%)";
    public IBrush MauVienHoc => CuaDangMo ? BrushCyan : (KhoaBaoTri ? BrushRed : (SoC >= 90m && CoPin ? BrushGreen : BrushBorderDark));
    public IBrush NenKhoangTu => CuaDangMo ? BrushBgOpen : (KhoaBaoTri ? BrushBgDanger : BrushBgDark);
}
