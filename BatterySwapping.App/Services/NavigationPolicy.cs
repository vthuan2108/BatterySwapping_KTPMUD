namespace BatterySwapping.App.Services;
public record NavigationPolicy(bool Staff,bool WalletAdministration,bool Kiosk)
{
    public static NavigationPolicy For(string? role)=>new(role is "Admin" or "KyThuatVien",role=="Admin",role=="KhachHang");
}
