using System;
using System.Linq;

namespace BatterySwapping.App.Avalonia.Helpers;

/// <summary>
/// Tiện ích hỗ trợ mã hóa định danh và giải mã mã QR giao dịch tại trạm Kiosk.
/// </summary>
public static class QrCodeHelper
{
    /// <summary>
    /// Tạo chuỗi Payload đại diện cho mã QR của khách hàng tại trạm.
    /// Cú pháp chuẩn: BSS:KH-{maKH}:{soDienThoai}:{timestamp}
    /// </summary>
    public static string GenerateCustomerQrPayload(int maKH, string soDienThoai)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"BSS:KH-{maKH}:{soDienThoai}:{timestamp}";
    }

    /// <summary>
    /// Giải mã chuỗi QR quét được từ ứng dụng điện thoại hoặc thẻ RFID, hỗ trợ số điện thoại trực tiếp.
    /// </summary>
    public static bool TryParseQrPayload(string qrText, out int maKH, out string soDienThoai)
    {
        maKH = 0;
        soDienThoai = string.Empty;

        if (string.IsNullOrWhiteSpace(qrText)) return false;

        string trimmed = qrText.Trim();

        // Xử lý chuỗi mã QR có định dạng BSS:...
        if (trimmed.StartsWith("BSS:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split(':');
            if (parts.Length >= 3)
            {
                // parts[1] dạng KH-1 hoặc 1
                string idPart = parts[1];
                if (idPart.StartsWith("KH-", StringComparison.OrdinalIgnoreCase))
                {
                    idPart = idPart.Substring(3);
                }
                if (!int.TryParse(idPart, out maKH) || maKH <= 0) return false;

                // parts[2] dạng PHONE-0987654321 hoặc 0987654321
                string phonePart = parts[2];
                if (phonePart.StartsWith("PHONE-", StringComparison.OrdinalIgnoreCase))
                {
                    phonePart = phonePart.Substring(6);
                }
                soDienThoai = phonePart.Trim();
                return soDienThoai.Length >= 9 && soDienThoai.Length <= 15 && soDienThoai.All(char.IsDigit);
            }
        }

        // Xử lý nếu người dùng nhập số điện thoại trực tiếp (hỗ trợ các tiền tố phổ biến +84, khoảng trắng, gạch nối)
        string cleanPhone = trimmed.Replace(" ", "").Replace("-", "").Replace(".", "");
        if (cleanPhone.StartsWith("+84"))
        {
            cleanPhone = "0" + cleanPhone.Substring(3);
        }
        if (cleanPhone.All(char.IsDigit) && cleanPhone.Length >= 9)
        {
            soDienThoai = cleanPhone;
            return true;
        }

        // Trường hợp nhập chuỗi bất kỳ khác, gán làm số điện thoại tra cứu nguyên trạng
        soDienThoai = trimmed;
        return false;
    }
}
