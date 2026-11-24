using System;

namespace BatterySwapping.App.Avalonia.Models;

/// <summary>
/// Thực thể quản lý chu trình và lịch sử giao dịch đổi pin giữa khách hàng và trạm.
/// </summary>
public class GiaoDichDoiPin
{
    public int MaGD { get; set; }
    public int MaKH { get; set; }
    public string? TenKhachHang { get; set; }
    public string? SoDienThoai { get; set; }
    public string MaTram { get; set; } = string.Empty;
    public string MaPinTra { get; set; } = string.Empty;
    public decimal SoC_PinTra { get; set; }
    public int? NganTra { get; set; }
    public string MaPinNhan { get; set; } = string.Empty;
    public decimal SoC_PinNhan { get; set; }
    public int? NganNhan { get; set; }
    public DateTime ThoiGianBatDau { get; set; } = DateTime.Now;
    public DateTime? ThoiGianKetThuc { get; set; }
    public decimal PhiDoiPin { get; set; } = 25000.0m;
    public string TrangThai { get; set; } = "HoanThanh";
    public string? GhiChu { get; set; }

    public string PhiFormatted => string.Format("{0:N0} đ", PhiDoiPin);
    public string ThoiGianBatDauFormatted => ThoiGianBatDau.ToString("dd/MM/yyyy HH:mm:ss");
    public string ThoiGianKetThucFormatted => ThoiGianKetThuc.HasValue ? ThoiGianKetThuc.Value.ToString("dd/MM/yyyy HH:mm:ss") : "--";
    public string NganTraDisplay => NganTra.HasValue ? $"Ngăn #{NganTra.Value:D2}" : "--";
    public string NganNhanDisplay => NganNhan.HasValue ? $"Ngăn #{NganNhan.Value:D2}" : "--";
}
