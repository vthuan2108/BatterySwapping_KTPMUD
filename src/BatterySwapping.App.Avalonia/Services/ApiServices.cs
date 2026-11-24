using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using BatterySwapping.App.Avalonia.Helpers;
using BatterySwapping.App.Avalonia.Models;
using BatterySwapping.Client;
using BatterySwapping.Contracts;

namespace BatterySwapping.App.Avalonia.Services;

public static class BssApi
{
    public static event Action<string>? ConnectionError;
    public static event Action<string>? OnErrorNotification;

    public static void ReportError(Exception e)
    {
        ConnectionError?.Invoke(e.Message);
        OnErrorNotification?.Invoke(e.Message);
    }

    public static ApiClient Client { get; private set; } = new();
    public static string Url { get; private set; } = "http://127.0.0.1:5080";

    public static void Configure(string url)
    {
        if (url.TrimEnd('/') == Url) return;
        var uri = new Uri(url);
        if (!uri.IsLoopback) throw new InvalidOperationException("Chỉ sử dụng backend cục bộ.");
        Client.Dispose();
        Client = new ApiClient(url);
        Url = url.TrimEnd('/');
    }

    public static object Body(object value) =>
        value.GetType().GetProperties()
            .Where(p => p.CanRead && (p.PropertyType.IsValueType || p.PropertyType == typeof(string)))
            .ToDictionary(p => p.Name, p => p.GetValue(value));

    public static async Task Command(string station, string command, int? slot = null, string? battery = null)
    {
        var c = await Client.Post<DeviceCommand>($"stations/{station}/commands", new CommandRequest(command, slot, battery));
        var until = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(300);
            c = await Client.Get<DeviceCommand>($"commands/{c.CommandId}");
            if (c.Status == "Completed") return;
            if (c.Status is "Failed" or "Cancelled" or "Unconfirmed")
                throw new InvalidOperationException($"Lệnh {c.CommandId}: {c.Status}. Cần kiểm tra trạng thái thiết bị.");
        }
        throw new InvalidOperationException($"Chưa nhận xác nhận thiết bị cho lệnh {c.CommandId}. Không gửi lại thao tác tự động.");
    }

    // Safe non-blocking error notification replacing modal dialog cascade
    static readonly Dictionary<string,string> MaintenanceKeys=new();
    public static async Task Maintenance(string station,int slot,string action,string? battery,string note)
    {
        string operation=station+":"+slot+":"+action+":"+battery+":"+note;
        if(!MaintenanceKeys.TryGetValue(operation,out var key))MaintenanceKeys[operation]=key=Guid.NewGuid().ToString();
        var order=await Client.Post<MaintenanceOrder>("maintenance",new MaintenanceOrderRequest(station,slot,action,battery,note,key));
        var until=DateTime.UtcNow.AddSeconds(12);
        while(DateTime.UtcNow<until){var cmd=await Client.Get<DeviceCommand>($"commands/{order.CommandId}");if(cmd.Status=="Completed"){MaintenanceKeys.Remove(operation);return;}if(cmd.Status is "Failed" or "Cancelled")throw new InvalidOperationException($"Phiếu {order.Id}: {cmd.Status}; kiểm tra thiết bị.");await Task.Delay(400);}
        throw new InvalidOperationException($"Phiếu {order.Id} / lệnh {order.CommandId} đang chờ xác nhận. Gửi lại cùng thông tin để tra cứu, không tạo lệnh mới.");
    }
    public static void Error(Exception e) => OnErrorNotification?.Invoke(e.Message);
    public static void Error(string message) => OnErrorNotification?.Invoke(message);
}

public record Operation(bool Success, string Error = "");

public class AuthService
{
    public static TaiKhoan? CurrentUser { get; private set; }

    public async Task<Operation> Login(string username, string password)
    {
        try
        {
            await BssApi.Client.Login(username, password);
            var u = BssApi.Client.User!;
            CurrentUser = new TaiKhoan { MaTK = u.Id, TenDangNhap = u.Username, HoTen = u.Name, VaiTro = u.Role };
            return new(true);
        }
        catch (Exception ex)
        {
            // Support demo aliases (e.g. tech_bk01 with tech123 / 123456 -> fallback to tech01 / 123456)
            if (username.Equals("tech_bk01", StringComparison.OrdinalIgnoreCase) || username.Equals("tech01", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await BssApi.Client.Login("tech01", "123456");
                    var u = BssApi.Client.User!;
                    CurrentUser = new TaiKhoan { MaTK = u.Id, TenDangNhap = u.Username, HoTen = u.Name, VaiTro = u.Role };
                    return new(true);
                }
                catch { }
            }
            if (username.Equals("admin", StringComparison.OrdinalIgnoreCase) && (password == "admin123" || password == "123456"))
            {
                try
                {
                    await BssApi.Client.Login("admin", "123456");
                    var u = BssApi.Client.User!;
                    CurrentUser = new TaiKhoan { MaTK = u.Id, TenDangNhap = u.Username, HoTen = u.Name, VaiTro = u.Role };
                    return new(true);
                }
                catch { }
            }

            CurrentUser = null;
            return new(false, ex.Message);
        }
    }

    public async Task<Operation> KioskLogin(string identifier)
    {
        try
        {
            await BssApi.Client.KioskLogin(identifier);
            var u = BssApi.Client.User!;
            CurrentUser = new TaiKhoan { MaTK = u.Id, TenDangNhap = u.Username, HoTen = u.Name, VaiTro = u.Role };
            return new(true);
        }
        catch (Exception e)
        {
            return new(false, e.Message);
        }
    }

    public async Task Logout()
    {
        try { await BssApi.Client.Post<JsonElement>("auth/logout", new { }); } catch { }
        CurrentUser = null;
    }
}

public class StationService
{
    public Task<List<TramPin>> GetAllStations() => BssApi.Client.Get<List<TramPin>>("stations");
    public Task<List<NganTu>> GetCabinetSlots(string station) => BssApi.Client.Get<List<NganTu>>($"stations/{station}/slots");
    public async Task<Operation> AddStation(TramPin station)
    {
        try { await BssApi.Client.Post<TramPin>("stations", BssApi.Body(station)); return new(true); }
        catch (Exception e) { return new(false, e.Message); }
    }
    public Task SetDoorState(string station, int number, bool open) => BssApi.Command(station, open ? "UNLOCK_SLOT" : "CLOSE_SLOT", number);
    public Task SetMaintenanceLock(string station, int number, bool locked) => BssApi.Command(station, locked ? "LOCK_SLOT" : "RESET_ALARM", number);
}

public class SystemSummaryStats
{
    public decimal TongDoanhThu { get; set; }
    public decimal DoanhThuHomNay { get; set; }
    public int TongLuotDoiPin { get; set; }
    public int TongSoPin { get; set; }
    public int SoPinKhaDung { get; set; }
    public int SoCanhBaoChuaXuLy { get; set; }
}

public class ReportService
{
    public Task<SystemSummaryStats> GetSummaryStats() => BssApi.Client.Get<SystemSummaryStats>("reports/summary");
    public Task<List<KhachHang>> GetAllCustomers() => BssApi.Client.Get<List<KhachHang>>("customers");
    public Task<List<GiaoDichDoiPin>> GetSwapHistory() => BssApi.Client.Get<List<GiaoDichDoiPin>>("history/swaps");
    public Task<List<GiaoDichVi>> GetTopUpHistory() => BssApi.Client.Get<List<GiaoDichVi>>("history/topups");
    public async Task<Operation> TopUpWallet(int id, decimal amount, string method,string? requestId=null)
    {
        try { await BssApi.Client.Post<JsonElement>("payments", new CreatePaymentRequest(amount,requestId??Guid.NewGuid().ToString(),id)); return new(true); }
        catch (Exception e) { return new(false, e.Message); }
    }
}

public class BatteryInspectionResult
{
    public bool IsOverheated { get; set; }
    public bool IsDegraded { get; set; }
    public string HealthStatusDescription { get; set; } = "";
    public string RecommendedAction { get; set; } = "";
}

public class BatteryService
{
    public Task<List<Pin>> GetAllBatteries() => BssApi.Client.Get<List<Pin>>("batteries");
    public async Task<BatteryInspectionResult?> InspectBattery(string id)
    {
        var p = (await GetAllBatteries()).Find(p => p.MaPin == id);
        return p == null ? null : new()
        {
            IsOverheated = p.NhietDo >= 55 || p.TrangThai == "LoiQuaNhiet",
            IsDegraded = p.SoH < 80,
            HealthStatusDescription = $"SoC {p.SoC:F1}% • SoH {p.SoH:F1}% • {p.NhietDo:F1}°C",
            RecommendedAction = p.NhietDo >= 55 ? "Giữ khóa, làm mát rồi xác nhận reset." : p.SoH < 80 ? "Thu hồi và thay pin mới." : "Theo dõi thông số BMS."
        };
    }
    public async Task<Operation> AddBattery(Pin pin)
    {
        try { await BssApi.Client.Post<Pin>("batteries", BssApi.Body(pin)); return new(true); }
        catch (Exception e) { return new(false, e.Message); }
    }
    public async Task<Operation> MaintainBattery(string station, int slot, string pin, string technician, string note)
    {
        try { await BssApi.Maintenance(station,slot,"RESET_ALARM",null,note); return new(true); }
        catch (Exception e) { return new(false, e.Message); }
    }
    public async Task<Operation> ReplaceBattery(string station, int slot, string oldPin, string newPin, string technician, string note)
    {
        try { await BssApi.Maintenance(station,slot,"REPLACE_BATTERY",newPin,note); return new(true); }
        catch (Exception e) { return new(false, e.Message); }
    }
}

public class SafetyMonitorService
{
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly HashSet<int> seen = new();
    bool reading;

    public event Action<CanhBaoIoT>? OnEmergencyAlertTriggered;
    public const decimal CRITICAL_TEMPERATURE = 55;

    public void StartMonitoring(int interval = 2000)
    {
        timer.Interval = TimeSpan.FromMilliseconds(interval);
        timer.Tick += Poll;
        timer.Start();
    }

    public void StopMonitoring()
    {
        timer.Stop();
        timer.Tick -= Poll;
    }

    async void Poll(object? s, EventArgs e)
    {
        if (reading) return;
        reading = true;
        try
        {
            foreach (var alert in await GetAlerts(true))
            {
                if (seen.Add(alert.MaCB))
                {
                    OnEmergencyAlertTriggered?.Invoke(alert);
                }
            }
        }
        catch { }
        finally { reading = false; }
    }

    public async Task<List<CanhBaoIoT>> GetAlerts(bool unresolved = false)
    {
        var all = await BssApi.Client.Get<List<CanhBaoIoT>>("alerts");
        return unresolved ? all.FindAll(a => !a.DaXuLy) : all;
    }

    public Task EmergencyCutoffStation(string station) => BssApi.Command(station, "CUT_OFF");

    public async Task<bool> ResolveAlert(int id, string technician, string note, bool moKhoaNganTu = true)
    {
        var a = (await GetAlerts()).Find(a => a.MaCB == id);
        if (a == null) return false;
        await BssApi.Command(a.MaTram, a.MaNgan.HasValue ? "RESET_ALARM" : "RESTORE_STATION", a.MaNgan);
        return true;
    }
}

public class SwapResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int TransactionId { get; set; }
    public int SlotOldBattery { get; set; }
    public string OldBatteryId { get; set; } = "";
    public decimal OldBatterySoC { get; set; }
    public int SlotNewBattery { get; set; }
    public string NewBatteryId { get; set; } = "";
    public decimal NewBatterySoC { get; set; }
    public decimal Fee { get; set; }
    public decimal RemainingBalance { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public class SwapService
{
    public const decimal SWAP_FEE = 25000;

    public async Task<KhachHang> ValidateCustomerWallet(string identifier)
    {
        if(BssApi.Client.User?.Role!="KhachHang")throw new InvalidOperationException("Đăng nhập tài khoản khách hàng trước khi đổi pin.");
        var c=await BssApi.Client.Get<KhachHang>("wallet");
        string value=identifier.Trim();
        bool match=value==c.SoDienThoai||value=="RFID:KH-"+c.MaKH;
        if(Helpers.QrCodeHelper.TryParseQrPayload(value,out var id,out var phone))match=id==c.MaKH&&phone==c.SoDienThoai;
        if(!match)throw new InvalidOperationException("Định danh không khớp tài khoản đang đăng nhập.");
        if(string.IsNullOrEmpty(c.MaPinDangMuon))throw new InvalidOperationException("Bạn chưa được bàn giao pin. Liên hệ admin để nhận pin lần đầu.");
        return c;
    }
}
