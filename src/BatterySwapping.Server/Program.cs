using System.Net;
using BatterySwapping.Contracts;
using BatterySwapping.Contracts.Models;
using BatterySwapping.Core;
using BatterySwapping.Core.Helpers;
using BatterySwapping.Core.Services;
using BatterySwapping.Server;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder=WebApplication.CreateBuilder(new WebApplicationOptions { Args=args, ContentRootPath=AppContext.BaseDirectory, WebRootPath=Path.Combine(AppContext.BaseDirectory,"wwwroot") });
var projectRoot=new DirectoryInfo(AppContext.BaseDirectory);
while(projectRoot!=null && !File.Exists(Path.Combine(projectRoot.FullName,"BatterySwappingSystem.sln"))) projectRoot=projectRoot.Parent;
var settingsPath=Environment.GetEnvironmentVariable("BSS_SETTINGS")??Path.Combine(projectRoot?.FullName??AppContext.BaseDirectory,".runtime","server.json");
if(!File.Exists(settingsPath)) throw new InvalidOperationException("Chưa chuẩn bị môi trường. Chạy scripts/Prepare-Local.ps1 trước khi F5.");
builder.Configuration.AddJsonFile(settingsPath,optional:false);
builder.WebHost.UseUrls(builder.Configuration["Urls"]??"http://127.0.0.1:5080");
string cs=builder.Configuration.GetConnectionString("Bss")??@"Server=.\SQLEXPRESS;Database=BatterySwappingDB;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5;";
DatabaseHelper.ConnectionString=cs;
builder.Services.AddSingleton(new SqlStore(cs));builder.Services.AddSingleton<BssEngine>();builder.Services.AddSingleton<AuthSessions>();
builder.Services.AddSingleton<MqttWorker>();builder.Services.AddHostedService(p=>p.GetRequiredService<MqttWorker>());
builder.Services.AddRateLimiter(o=>o.AddFixedWindowLimiter("login",x=>{x.PermitLimit=20;x.Window=TimeSpan.FromMinutes(1);x.QueueLimit=0;}));
var app=builder.Build();
var auditStore=app.Services.GetRequiredService<SqlStore>();
var existingIssues=new BusinessOperations(auditStore).Consistency();
if(existingIssues.Count>0){
    string report=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!,"inventory-consistency.json");
    File.WriteAllText(report,System.Text.Json.JsonSerializer.Serialize(existingIssues));
    app.Logger.LogWarning("Inventory consistency requires review: {Count} issues. Report {ReportPath}",existingIssues.Count,report);
}
foreach(var script in new[]{"06_Runtime.sql","07_Business.sql","08_Maintenance.sql","09_Cooldowns.sql","10_AutoThermal.sql"})
    app.Services.GetRequiredService<SqlStore>().Migrate(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,script)));
app.Use(async(ctx,next)=>{
    if(ctx.Connection.RemoteIpAddress is not null && !IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress)){ctx.Response.StatusCode=403;return;}
    ctx.Response.Headers["X-Content-Type-Options"]="nosniff";
    ctx.Response.Headers["Cache-Control"]="no-cache, no-store, must-revalidate";
    ctx.Response.Headers["Pragma"]="no-cache";
    ctx.Response.Headers["Expires"]="0";
    try {await next();}
    catch(DomainException e){ctx.Response.StatusCode=e.Status;await ctx.Response.WriteAsJsonAsync(new ErrorResponse(e.Message,"BUSINESS_RULE"));}
    catch(Exception e){app.Logger.LogError(e,"Request failed {Path}",ctx.Request.Path);ctx.Response.StatusCode=500;await ctx.Response.WriteAsJsonAsync(new ErrorResponse("Không thể xử lý yêu cầu. Xem log server.","SERVER_ERROR"));}
});
app.UseRouting();app.UseRateLimiter();
app.UseDefaultFiles();app.UseStaticFiles();
app.MapGet("/health",(MqttWorker mqtt,SqlStore db)=>{db.Read(x=>x.Scalar("SELECT 1"));return Results.Ok(new{server="online",database="online",mqtt=mqtt.Connected});});
app.MapPost("/api/v1/auth/login",(LoginRequest r,BssEngine e,AuthSessions auth)=>{var u=e.Login(r);return new LoginResponse(auth.Create(u),u);}).RequireRateLimiting("login");
// Simulator bootstrap is loopback-only; these credentials grant device topics, never admin APIs.
app.MapGet("/api/v1/device/config",(IConfiguration c)=>new{brokerUrl=c["Broker:WebSocketUrl"]??"ws://127.0.0.1:19001/mqtt",port=c.GetValue<int?>("Broker:Port")??18883,username="simulator",password=c["Broker:SimulatorPassword"]});
app.MapPost("/api/v1/device/lease",(DeviceLease r,BssEngine e)=>e.Lease(r));
app.MapGet("/api/v1/device/{station}/snapshot",(string station,BssEngine e)=>new{stationId=station,status=e.Status(station),slots=new StationService().GetCabinetSlots(station),cooldowns=e.Cooldowns(station),operatingState=new StationService().GetAllStations().Find(s=>s.MaTram==station)?.TrangThaiHoatDong,batteries=new BatteryService().GetAllBatteries(),sessions=e.Sessions().Where(s=>s.StationId==station&&s.State is not("Completed" or "Cancelled" or "Expired")),commands=e.Commands(station).Where(c=>c.Status is "Sent" or "Unconfirmed" or "Completed" or "Failed").Take(100)});

var api=app.MapGroup("/api/v1");
api.AddEndpointFilter(async(ctx,next)=>{
    var http=ctx.HttpContext;string token=http.Request.Headers.Authorization.ToString().Replace("Bearer ","",StringComparison.Ordinal);
    var user=http.RequestServices.GetRequiredService<AuthSessions>().Get(token);
    if(user==null) return Results.Json(new ErrorResponse("Vui lòng đăng nhập.","UNAUTHENTICATED"),statusCode:401);
    var roles=http.GetEndpoint()?.Metadata.GetMetadata<Roles>();
    if(roles!=null && !roles.Names.Contains(user.Role)) return Results.StatusCode(403);
    http.Items["user"]=user;return await next(ctx);
});
UserInfo Who(HttpContext c)=>(UserInfo)c.Items["user"]!;
void Owner(HttpContext c,SwapSession s){var u=Who(c);if(u.Role=="KhachHang"&&u.CustomerId!=s.CustomerId)throw new DomainException("Không có quyền truy cập phiên.",403);}
var staff=new Roles("Admin","KyThuatVien");var admin=new Roles("Admin");
api.MapPost("/auth/logout",(HttpContext c,AuthSessions a)=>{a.Remove(c.Request.Headers.Authorization.ToString().Replace("Bearer ",""));return Results.NoContent();});
api.MapGet("/auth/me",(HttpContext c)=>Who(c));
api.MapBusinessEndpoints();
api.MapGet("/pricing",(SqlStore db)=>new BusinessOperations(db).GetPolicy());
api.MapMaintenanceEndpoints();
api.MapSimulationEndpoints();
api.MapGet("/stations",()=>new StationService().GetAllStations());
api.MapGet("/stations/{station}/slots",(string station)=>new StationService().GetCabinetSlots(station));
api.MapGet("/stations/{station}/cooldowns",(string station,BssEngine e)=>e.Cooldowns(station));
api.MapGet("/stations/{station}/status",(string station,BssEngine e)=>e.Status(station));
api.MapPost("/stations",(TramPin r)=>{if(r.SoLuongNgan is <1 or >64)throw new DomainException("Số ngăn phải từ 1 đến 64.",400);if(!new StationService().AddStation(r,out var error))throw new DomainException(error);return Results.Ok(r);}).WithMetadata(admin);
api.MapGet("/batteries",()=>new BatteryService().GetAllBatteries()).WithMetadata(staff);
api.MapPost("/batteries",(Pin r)=>{if(!new BatteryService().AddBattery(r,out var error))throw new DomainException(error);return Results.Ok(r);}).WithMetadata(admin);
api.MapGet("/customers",()=>new ReportService().GetAllCustomers()).WithMetadata(admin);
api.MapGet("/wallet",(HttpContext c,SqlStore db)=>db.Read(x=>x.Rows("SELECT k.*, COALESCE((SELECT SUM(Hold) FROM dbo.BssSessions WHERE CustomerId=k.MaKH AND Active=1),0) AS ReservedBalance FROM dbo.KhachHang k WHERE MaTK=@id",("@id",Who(c).Id)).SingleOrDefault()));
api.MapGet("/me/history/swaps",(HttpContext c,SqlStore db)=>db.Read(x=>x.Rows("SELECT * FROM dbo.LichSuDoiPin WHERE MaKH=@c ORDER BY MaGD DESC",("@c",Who(c).CustomerId??throw new DomainException("Không có hồ sơ khách hàng.",403))))).WithMetadata(new Roles("KhachHang"));
api.MapGet("/me/history/topups",(HttpContext c,SqlStore db)=>db.Read(x=>x.Rows("SELECT * FROM dbo.LichSuNapTien WHERE MaKH=@c ORDER BY MaNap DESC",("@c",Who(c).CustomerId??throw new DomainException("Không có hồ sơ khách hàng.",403))))).WithMetadata(new Roles("KhachHang"));
api.MapGet("/reports/summary",()=>new ReportService().GetSummaryStats()).WithMetadata(admin);
api.MapGet("/history/swaps",()=>new ReportService().GetSwapHistory()).WithMetadata(admin);
api.MapGet("/history/topups",()=>new ReportService().GetTopUpHistory()).WithMetadata(admin);
api.MapGet("/alerts",(SqlStore db)=>db.Read(x=>x.Rows("SELECT * FROM dbo.CanhBaoIoT ORDER BY MaCB DESC"))).WithMetadata(staff);
api.MapPost("/stations/{station}/commands",(string station,CommandRequest r,BssEngine e)=>{var cmd=e.RequestCommand(station,r);return Results.Accepted($"/api/v1/commands/{cmd.CommandId}",cmd);}).WithMetadata(staff);
api.MapGet("/commands/{id}",(string id,BssEngine e)=>e.Commands().SingleOrDefault(c=>c.CommandId==id)??throw new DomainException("Không tìm thấy lệnh.",404)).WithMetadata(staff);
api.MapGet("/commands",(BssEngine e)=>e.Commands().Take(200)).WithMetadata(staff);
api.MapPost("/swaps",(HttpContext c,StartSwap r,BssEngine e)=>{
    int customer=Who(c).CustomerId??throw new DomainException("Đăng nhập tài khoản khách hàng để sử dụng ví.",403);
    var s=e.Start(customer,r);return Results.Accepted($"/api/v1/swaps/{s.Id}",s);
});
api.MapGet("/swaps",(HttpContext c,BssEngine e)=>e.Sessions(Who(c).Role=="KhachHang"?Who(c).CustomerId??throw new DomainException("Thiếu hồ sơ khách hàng.",403):null));
api.MapPost("/swaps/{id}/resume",(string id,BssEngine e)=>e.Resume(id)).WithMetadata(staff);
api.MapGet("/swaps/{id}",(string id,HttpContext c,BssEngine e)=>{var s=e.GetSession(id)??throw new DomainException("Không tìm thấy phiên.",404);Owner(c,s);return s;});
api.MapPost("/swaps/{id}/cancel",(string id,HttpContext c,BssEngine e)=>{var s=e.GetSession(id)??throw new DomainException("Không tìm thấy phiên.",404);Owner(c,s);return e.Cancel(id);});
app.MapGet("/api-docs",()=>Results.Content("""
<!doctype html><html lang="vi"><meta charset="utf-8"><title>BSS API</title><body style="font:18px system-ui;max-width:1000px;margin:40px auto"><h1>BSS Local API v1</h1><p>REST JSON, Authorization: Bearer token từ POST /api/v1/auth/login {username,password}. Phiên 8 giờ; logout thu hồi token. Chỉ loopback.</p><ul><li>GET /health</li><li>GET /api/v1/policy, inventory, maintenance, payments; POST /api/v1/customers/create, handovers; PUT /api/v1/policy (Admin)</li><li>GET /api/v1/stations, stations/{id}/slots, stations/{id}/status</li><li>GET /api/v1/batteries, customers, alerts, reports/summary, history/swaps, history/topups</li><li>POST /api/v1/stations; POST /api/v1/batteries (Admin)</li><li>POST /api/v1/payments {amount,requestId,customerId?}; POST payments/{id}/result {result} (Admin)</li><li>POST /api/v1/swaps {stationId,idempotencyKey} (Khách hàng)</li><li>GET /api/v1/swaps/{id}; POST /api/v1/swaps/{id}/cancel</li><li>POST /api/v1/stations/{id}/commands {command,slotNumber,batteryId} (Admin/Kỹ thuật viên)</li><li>GET /api/v1/commands/{id}; GET /api/v1/commands</li></ul><p>Lệnh: CUT_OFF, RESTORE_STATION, RESET_ALARM, REPLACE_BATTERY, UNLOCK_SLOT, CLOSE_SLOT, LOCK_SLOT, TRIGGER_OVERHEAT. POST lệnh trả 202 và commandId; chỉ Completed mới là xác nhận thực thi.</p><p>Lỗi: 400 dữ liệu, 401 đăng nhập, 403 quyền, 404 không tồn tại, 409 nghiệp vụ. JSON {error,code}. Ví nội bộ demo; không gọi ngân hàng.</p><a href="/">Mở Web3D</a></body></html>
""","text/html; charset=utf-8"));
// Visual Studio F5 also starts the prepared local broker if it is not running.
System.Diagnostics.Process? ownedBroker=null;
try
{
    using var probe=new System.Net.Sockets.TcpClient();
    await probe.ConnectAsync("127.0.0.1",builder.Configuration.GetValue<int?>("Broker:Port")??18883).WaitAsync(TimeSpan.FromSeconds(1));
}
catch
{
    var broker=Path.Combine(projectRoot?.FullName??"", ".tools", "mosquitto", "mosquitto.exe");
    var config=Path.Combine(Path.GetDirectoryName(settingsPath)!,"mosquitto.conf");
    if(!File.Exists(broker)||!File.Exists(config))throw new InvalidOperationException("Thiếu broker cục bộ. Chạy scripts/Prepare-Local.ps1.");
    ownedBroker=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(broker){Arguments="-c \""+config+"\"",UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden});
}
app.Lifetime.ApplicationStopped.Register(()=>{try{if(ownedBroker is {HasExited:false})ownedBroker.Kill();}catch{}ownedBroker?.Dispose();});
app.Run();
