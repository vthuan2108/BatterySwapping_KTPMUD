using System;
using Avalonia.Media;

namespace BatterySwapping.App.Avalonia.Models;

/// <summary>
/// Thực thể quản lý Pin và các thông số telemetry BMS (SoC, SoH, Nhiệt độ, Điện áp, Dòng điện).
/// Tương thích Avalonia UI.
/// </summary>
public class Pin
{
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

    private static readonly IBrush BrushRed = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly IBrush BrushAmber = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly IBrush BrushGreen = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));

    // Trạng thái hiển thị cảnh báo
    public bool IsQuaNhiet => NhietDo >= 55.0m;
    public bool IsCanhBaoNhiet => NhietDo >= 50.0m && NhietDo < 55.0m;
    public bool IsDatChuanDoiPin => SoC >= 90.0m && SoH >= 80.0m && NhietDo < 45.0m && TrangThai != "Loi" && TrangThai != "LoiQuaNhiet";
    public IBrush MauNhietDo => IsQuaNhiet ? BrushRed : (IsCanhBaoNhiet ? BrushAmber : BrushGreen);
    public string SoCFormatted => $"{SoC:F1}%";
    public string SoHFormatted => $"{SoH:F1}%";
    public string NhietDoFormatted => $"{NhietDo:F1} °C";
    public string DienApFormatted => $"{DienAp:F1} V";
    public string DongDienFormatted => $"{DongDien:F1} A";
}
