using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using BatterySwapping.Client;
using BatterySwapping.Contracts;
using BatterySwapping.Contracts.Models;
using BatterySwapping.Core;
using MQTTnet;
using MQTTnet.Protocol;

if(!args.Contains("--integration")){Console.WriteLine("Run with --integration against prepared BssArchitectureTests_* fixture. Unit tests: BatterySwapping.ArchitectureTests.");return;}
string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
string settingsFile=Environment.GetEnvironmentVariable("BSS_TEST_SETTINGS")??Path.Combine(root,".runtime/server.json");
var settings=JsonDocument.Parse(File.ReadAllText(settingsFile)).RootElement;
string cs=settings.GetProperty("ConnectionStrings").GetProperty("Bss").GetString()!;
if(!cs.Contains("Database=BssArchitectureTests_",StringComparison.OrdinalIgnoreCase))throw new Exception("Integration tests refuse to use the application database.");
var store=new SqlStore(cs);var engine=new BssEngine(store);
using var admin=new ApiClient();using var driver=new ApiClient();using var other=new ApiClient();using var tech=new ApiClient();
await admin.Login("admin","123456");await driver.Login("driver01","123456");await other.Login("driver02","123456");await tech.Login("tech01","123456");
int count=0;
void Check(bool condition,string name){if(!condition)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);count++;}
using var raw=new HttpClient{BaseAddress=new Uri(Environment.GetEnvironmentVariable("BSS_API_URL")??"http://127.0.0.1:5080")};
Check((await raw.GetAsync("/api/v1/stations")).StatusCode==HttpStatusCode.Unauthorized,"anonymous cannot access admin data");
try{await driver.Post<DeviceCommand>("stations/TRAM-BK01/commands",new CommandRequest("CUT_OFF"));throw new Exception("Role bypass");}catch(InvalidOperationException){Check(true,"customer cannot control station");}
Check(store.Read(db=>(string)db.Scalar("SELECT MatKhau FROM dbo.TaiKhoan WHERE TenDangNhap='admin'")!).StartsWith("pbkdf2$"),"legacy password migrated to salted PBKDF2");
var list=await driver.Get<List<NganTu>>("stations/TRAM-BK01/slots");
var device=new TestDevice(raw,list);await device.Connect();
try{
    await Wait(async()=>(await driver.Get<StationStatus>("stations/TRAM-BK01/status")).Online);
    Check(true,"real MQTT telemetry reaches backend");
    try {engine.Receive(new DeviceMessage(1,"TRAM-BK01",device.Lease,Guid.NewGuid().ToString("N"),long.MaxValue,DateTimeOffset.UtcNow,null,null,null,null,null,true,Array.Empty<SlotTelemetry>()));throw new Exception("Partial telemetry accepted");}
    catch(DomainException){Check(true,"partial telemetry cannot refresh station safety");}
    var snapshotText=await raw.GetStringAsync("/api/v1/device/TRAM-BK01/snapshot");
    Check(!snapshotText.Contains(device.Lease),"public snapshot does not leak device lease secret");
    var duplicate=await raw.PostAsJsonAsync("/api/v1/device/lease",new DeviceLease("TRAM-BK01","duplicate"));
    Check(duplicate.StatusCode==HttpStatusCode.Conflict,"second active simulator rejected");
    string key=Guid.NewGuid().ToString("N");
    var s=await driver.Post<SwapSession>("swaps",new StartSwap("TRAM-BK01",key));
    var again=await driver.Post<SwapSession>("swaps",new StartSwap("TRAM-BK01",key));Check(s.Id==again.Id,"idempotent session creation");
    var s2=await other.Post<SwapSession>("swaps",new StartSwap("TRAM-BK01",Guid.NewGuid().ToString("N")));
    Check(s.ReturnSlot!=s2.ReturnSlot&&s.NewBatteryId!=s2.NewBatteryId,"simultaneous customers reserve distinct resources");
    var closing=await other.Post<SwapSession>($"swaps/{s2.Id}/cancel",new{});
    Check(closing.State=="ClosingCancelled" && store.Read(db=>Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.BssReservations WHERE SessionId=@id",("@id",s2.Id))))>0,"cancel retains resources until physical close ACK");
    await device.ExecuteUntil("UNLOCK_SLOT",s.Id);
    Check(store.Read(db=>Convert.ToBoolean(db.Scalar("SELECT CuaDangMo FROM dbo.NganTu WHERE MaTram='TRAM-BK01' AND SoThuTu=@n",("@n",s.ReturnSlot)))) ,"door opens only after device acknowledgement");
    await RestartServer();
    Check((await driver.Get<SwapSession>($"swaps/{s.Id}")).State=="AwaitingOldBattery","in-flight reservation survives real server restart before receipt");
    await device.Event(s,"OLD_INSERTED");
    await Wait(async()=>(await driver.Get<SwapSession>($"swaps/{s.Id}")).Charged);
    await device.ExecuteUntil("EJECT_BATTERY",s.Id);
    await RestartServer();
    Check((await driver.Get<SwapSession>($"swaps/{s.Id}")).Charged,"charged transaction survives real server restart before collection");
    await device.Event(s,"NEW_TAKEN");
    await Wait(async()=>(await driver.Get<SwapSession>($"swaps/{s.Id}")).State=="Completed");
    Check(store.Read(db=>Convert.ToDecimal(db.Scalar("SELECT SoDuVi FROM dbo.KhachHang WHERE MaKH=@c",("@c",s.CustomerId))))==125000,"wallet charged exactly once");
    await device.Event(s,"OLD_INSERTED",false);
    await Task.Delay(300);
    Check(store.Read(db=>Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.LichSuDoiPin WHERE GhiChu=@id",("@id",s.Id))))==1,"duplicate physical event cannot duplicate payment");
    Check(new BssEngine(new SqlStore(cs)).GetSession(s.Id)?.State=="Completed","sessions survive engine recreation");

    var beforeAccounts=store.Read(db=>Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.TaiKhoan")));
    Check((await raw.PostAsJsonAsync("/api/v1/auth/kiosk",new KioskAuthRequest("0987654321"))).StatusCode==HttpStatusCode.NotFound,"identifier cannot issue login token");
    Check((await raw.PostAsJsonAsync($"/api/v1/swaps/{s.Id}/events",new SwapEventRequest("OLD_INSERTED"))).StatusCode==HttpStatusCode.NotFound,"HTTP cannot forge physical swap events");
    Check(store.Read(db=>Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.TaiKhoan")))==beforeAccounts,"identifier cannot provision customer");
    var payment=await driver.Post<PaymentRequest>("payments",new CreatePaymentRequest(10000,Guid.NewGuid().ToString()));
    Check(payment.Status=="Pending","topup request starts pending");
    try{await tech.Post<PaymentRequest>($"payments/{payment.Id}/result",new PaymentResultRequest("Succeeded"));throw new Exception("technician payment bypass");}catch(InvalidOperationException){Check(true,"technician cannot confirm payment");}
    await admin.Post<PaymentRequest>($"payments/{payment.Id}/result",new PaymentResultRequest("Succeeded"));
    await admin.Post<PaymentRequest>($"payments/{payment.Id}/result",new PaymentResultRequest("Succeeded"));
    Check(store.Read(db=>Convert.ToDecimal(db.Scalar("SELECT SoDuVi FROM dbo.KhachHang WHERE MaKH=1")))==135000,"payment callback credits once");
    try{await other.Post<PaymentRequest>("payments",new CreatePaymentRequest(10000,Guid.NewGuid().ToString(),1));throw new Exception("otherwallet bypass");}catch(InvalidOperationException){Check(true,"customer cannot create payment for someone else");}
    var recovery=await driver.Post<SwapSession>("swaps",new StartSwap("TRAM-BK01",Guid.NewGuid().ToString("N")));
    await device.ExecuteUntil("UNLOCK_SLOT",recovery.Id);
    device.Change(recovery.ReturnSlot,t=>t with{BatteryId=recovery.OldBatteryId,Temperature=60,IsDoorOpen=false,IsLocked=true,Current=0});
    await device.Event(recovery,"OLD_INSERTED",false);
    await Wait(async()=>(await driver.Get<SwapSession>($"swaps/{recovery.Id}")).State=="NeedsAssistance");
    Check(store.Read(db=>db.Scalar("SELECT MaPinDangMuon FROM dbo.KhachHang WHERE MaKH=1"))==null,"received old battery leaves customer custody before charge");
    try{await tech.Post<MaintenanceOrder>("maintenance",new MaintenanceOrderRequest("TRAM-BK01",recovery.ReturnSlot,"RESET_ALARM",null,"Chặn reset sớm",Guid.NewGuid().ToString()));throw new Exception("early cooldown bypass");}catch(InvalidOperationException){Check(true,"hot receipt cannot be reset before five minutes");}
    // Dedicated fixture time travel only: deterministic endpoint regression avoids sleeping five minutes.
    store.Write(db=>db.Exec("UPDATE dbo.BssSlotCooldowns SET StartedAt=DATEADD(minute,-6,SYSDATETIMEOFFSET()),UnlockAfter=DATEADD(minute,-1,SYSDATETIMEOFFSET()) WHERE StationId='TRAM-BK01' AND SlotNumber=@n",("@n",recovery.ReturnSlot)));
    device.Change(recovery.ReturnSlot,t=>t with{Temperature=30});await device.Publish("telemetry");
    await device.ExecuteUntil("AUTO_RESET_ALARM",null);
    await Wait(async()=>(await tech.Get<List<SlotCooldown>>("stations/TRAM-BK01/cooldowns")).Single(c=>c.SlotNumber==recovery.ReturnSlot).ClearedAt!=null);
    Check(true,"first thermal episode recovers automatically without technician confirmation");
    await tech.Post<SwapSession>($"swaps/{recovery.Id}/return-old",new{});
    await device.ExecuteUntil("RETURN_OLD_BATTERY",recovery.Id);
    await RestartServer();
    await device.Event(recovery,"OLD_RETURNED");
    await Wait(async()=>(await driver.Get<SwapSession>($"swaps/{recovery.Id}")).State=="Cancelled");
    Check(store.Read(db=>Convert.ToDecimal(db.Scalar("SELECT SoDuVi FROM dbo.KhachHang WHERE MaKH=1")))==135000,"return old battery after restart does not charge wallet");
    Check((string?)store.Read(db=>db.Scalar("SELECT MaPinDangMuon FROM dbo.KhachHang WHERE MaKH=1"))==recovery.OldBatteryId,"return old battery restores custody only after device event");
    int hotSlot=4;device.Change(hotSlot,t=>t with {Temperature=65,Current=0,IsLocked=true});await device.Publish("alerts");
    await Wait(()=>Task.FromResult(store.Read(db=>Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.NganTu n JOIN dbo.Pin p ON p.MaPin=n.MaPinHienTai WHERE n.MaTram='TRAM-BK01' AND n.SoThuTu=4 AND n.KhoaBaoTri=1 AND p.DongDien=0")))==1));
    Check(true,"overheat telemetry triggers SQL cutoff and lock");
    device.Change(hotSlot,t=>t with{Temperature=30,Current=0,IsLocked=true});await device.Publish("telemetry");
    await Wait(()=>Task.FromResult(store.Read(db=>Convert.ToDecimal(db.Scalar("SELECT NhietDo FROM dbo.Pin WHERE MaPin=(SELECT MaPinHienTai FROM dbo.NganTu WHERE MaTram='TRAM-BK01' AND SoThuTu=4)")))<35));
    try{await admin.Post<DeviceCommand>("stations/TRAM-BK01/commands",new CommandRequest("RESTORE_STATION"));throw new Exception("unresolvedalert bypass");}catch(InvalidOperationException){Check(true,"cooled pin still requires closing alert before station restore");}
    var cooldown=(await admin.Get<List<SlotCooldown>>("stations/TRAM-BK01/cooldowns")).Single(c=>c.SlotNumber==hotSlot);
    Check(cooldown.ClearedAt==null && cooldown.UnlockAfter-cooldown.StartedAt>=TimeSpan.FromMinutes(5),"cooldown persisted for minimum five real minutes");
    try{await tech.Post<MaintenanceOrder>("maintenance",new MaintenanceOrderRequest("TRAM-BK01",hotSlot,"RESET_ALARM",null,"Nguội sớm chưa hết hạn",Guid.NewGuid().ToString()));throw new Exception("cold early reset bypass");}catch(InvalidOperationException){Check(true,"cooled slot remains locked before five minutes");}
    await RestartServer();
    Check((await admin.Get<List<SlotCooldown>>("stations/TRAM-BK01/cooldowns")).Single(c=>c.SlotNumber==hotSlot).UnlockAfter==cooldown.UnlockAfter,"server restart preserves cooldown deadline");
    store.Write(db=>db.Exec("UPDATE dbo.BssSlotCooldowns SET StartedAt=DATEADD(minute,-6,SYSDATETIMEOFFSET()),UnlockAfter=DATEADD(minute,-1,SYSDATETIMEOFFSET()) WHERE StationId='TRAM-BK01' AND SlotNumber=@n",("@n",hotSlot)));
    Check((await tech.Get<List<SlotCooldown>>("stations/TRAM-BK01/cooldowns")).Single(c=>c.SlotNumber==hotSlot).ClearedAt==null,"automatic recovery waits for device acknowledgement");
    await device.ExecuteUntil("AUTO_RESET_ALARM",null);
    await Wait(async()=>(await tech.Get<List<SlotCooldown>>("stations/TRAM-BK01/cooldowns")).Single(c=>c.SlotNumber==hotSlot).ClearedAt!=null);
    Check(store.Read(db=>Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.CanhBaoIoT WHERE MaTram='TRAM-BK01' AND MaNgan=4 AND DaXuLy=0")))==0,"safe acknowledgement resolves alert");
    string replacement="TEST-REPLACE";
    store.Write(db=>db.Exec("INSERT dbo.Pin(MaPin,LoaiPin,SoC,SoH,NhietDo,TrangThai) VALUES(@p,N'Lithium LFP 72V-30Ah',94,91,28,N'DaDay')",("@p",replacement)));
    string oldPin=device.Slots.Single(t=>t.SlotNumber==hotSlot).BatteryId!;
    var replace=await tech.Post<MaintenanceOrder>("maintenance",new MaintenanceOrderRequest("TRAM-BK01",hotSlot,"REPLACE_BATTERY",replacement,"Thay pin kho có sẵn",Guid.NewGuid().ToString()));
    try{await admin.Post<DeviceCommand>("stations/TRAM-BK01/commands",new CommandRequest("UNLOCK_SLOT",hotSlot));throw new Exception("maintenance lock bypass");}catch(InvalidOperationException){Check(true,"raw slot control cannot bypass maintenance reservation");}
    await device.ExecuteUntil("REPLACE_BATTERY",null);
    Check(store.Read(db=>(string?)db.Scalar("SELECT Location FROM dbo.BssBatteryLocations WHERE BatteryId=@p",("@p",oldPin)))=="Quarantine","replaced battery moves to quarantine");
    Check(store.Read(db=>Convert.ToDecimal(db.Scalar("SELECT SoH FROM dbo.Pin WHERE MaPin=@p",("@p",replacement))))==91,"replacement preserves registered BMS instead of inventing 100 percent");
    var cutoff=await admin.Post<DeviceCommand>("stations/TRAM-BK01/commands",new CommandRequest("CUT_OFF"));await device.ExecuteUntil("CUT_OFF",null);
    await Wait(async()=>(await admin.Get<DeviceCommand>($"commands/{cutoff.CommandId}")).Status=="Completed");
    Check(device.Slots.All(t=>t.Current==0&&t.IsLocked),"station cutoff protects all slots");
    await device.DisposeAsync();await Task.Delay(5500);
    Check(!(await admin.Get<StationStatus>("stations/TRAM-BK01/status")).Online,"device offline after five seconds");
    try{await driver.Post<SwapSession>("swaps",new StartSwap("TRAM-BK01",Guid.NewGuid().ToString("N")));throw new Exception("Offline swap accepted");}catch(InvalidOperationException){Check(true,"offline device cannot start a swap");}
    Console.WriteLine($"RESULT {count} PASSED");
}finally{await device.DisposeAsync();}

async Task RestartServer()
{
    string pidFile=Environment.GetEnvironmentVariable("BSS_TEST_PIDS")??Path.Combine(root,".runtime/processes.json");
    var processes=JsonNode.Parse(File.ReadAllText(pidFile))!.AsArray();
    string exe=Path.GetFullPath(Path.Combine(root,"src/BatterySwapping.Server/bin/"+(Environment.GetEnvironmentVariable("BSS_TEST_CONFIGURATION")??"Release")+"/net8.0/BatterySwapping.Server.exe"));
    var entry=processes.First(n=>string.Equals(n?["Path"]?.GetValue<string>(),exe,StringComparison.OrdinalIgnoreCase))!;
    using(var running=Process.GetProcessById(entry["Id"]!.GetValue<int>()))
    {
        if(!string.Equals(running.MainModule?.FileName,exe,StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected server process");
        running.Kill();await running.WaitForExitAsync();
    }
    var info=new ProcessStartInfo(exe){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
    info.Environment["BSS_SETTINGS"]=settingsFile;
    var restarted=Process.Start(info)!;
    restarted.OutputDataReceived+=(_,e)=>{if(e.Data?.Contains("Rejected")==true)Console.WriteLine(e.Data);};restarted.ErrorDataReceived+=(_,e)=>{if(e.Data!=null)Console.WriteLine(e.Data);};restarted.BeginOutputReadLine();restarted.BeginErrorReadLine();
    entry["Id"]=restarted.Id;entry["Start"]=restarted.StartTime.ToUniversalTime().ToString("O");File.WriteAllText(pidFile,processes.ToJsonString());
    await Wait(async()=>{try{return (await raw.GetFromJsonAsync<JsonElement>("/health")).GetProperty("mqtt").GetBoolean();}catch{return false;}},15000);
    await admin.Login("admin","123456");await driver.Login("driver01","123456");await other.Login("driver02","123456");await tech.Login("tech01","123456");
    await Wait(async()=>(await driver.Get<StationStatus>("stations/TRAM-BK01/status")).Online);
}

static async Task Wait(Func<Task<bool>> condition,int timeout=10000){var until=DateTime.UtcNow.AddMilliseconds(timeout);while(DateTime.UtcNow<until){if(await condition())return;await Task.Delay(150);}throw new Exception("Timeout waiting for integration state");}

sealed class TestDevice(HttpClient http,List<NganTu> slots):IAsyncDisposable
{
    readonly List<SlotTelemetry> data=slots.Select(n=>new SlotTelemetry(n.SoThuTu,n.MaPinHienTai,n.SoC,n.SoH,n.NhietDo,n.DienAp,n.DongDien,n.DongDien>0,n.CuaDangMo,n.KhoaBaoTri)).ToList();
    readonly IMqttClient mqtt=new MqttClientFactory().CreateMqttClient();
    readonly Channel<DeviceCommand> commands=Channel.CreateUnbounded<DeviceCommand>();
    readonly CancellationTokenSource stopping=new();readonly SemaphoreSlim send=new(1);string lease="";long sequence;
    Task? heartbeat;bool disposed;
    public string Lease=>lease;
    public SlotTelemetry[] Slots=>data.ToArray();
    public void Change(int n,Func<SlotTelemetry,SlotTelemetry> update){int i=data.FindIndex(x=>x.SlotNumber==n);data[i]=update(data[i]);}
    public async Task Connect(){
        var r=await http.PostAsJsonAsync("/api/v1/device/lease",new DeviceLease("TRAM-BK01",Guid.NewGuid().ToString("N")));r.EnsureSuccessStatusCode();lease=(await r.Content.ReadFromJsonAsync<LeaseResponse>())!.LeaseId;
        var config=await http.GetFromJsonAsync<JsonElement>("/api/v1/device/config");
        mqtt.ApplicationMessageReceivedAsync+=e=>{var c=JsonSerializer.Deserialize<DeviceCommand>(e.ApplicationMessage.ConvertPayloadToString(),SqlStore.Json)!;commands.Writer.TryWrite(c);return Task.CompletedTask;};
        await mqtt.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1",config.TryGetProperty("port",out var port)?port.GetInt32():18883).WithCredentials("simulator",config.GetProperty("password").GetString()).Build());
        await mqtt.SubscribeAsync(new MqttClientFactory().CreateSubscribeOptionsBuilder().WithTopicFilter(t=>t.WithTopic("bss/station/TRAM-BK01/commands").WithAtLeastOnceQoS()).Build());
        heartbeat=Task.Run(async()=>{try{while(!stopping.IsCancellationRequested){await Publish("telemetry");await Task.Delay(700,stopping.Token);}}catch(OperationCanceledException){}});
    }
    public async Task Publish(string kind,string? ev=null,string? command=null,SwapSession? s=null,int? slot=null,string? battery=null){
        await send.WaitAsync();try{
            var m=new DeviceMessage(1,"TRAM-BK01",lease,Guid.NewGuid().ToString("N"),++sequence,DateTimeOffset.UtcNow,ev,command,s?.Id,slot,battery,true,Slots);
            await mqtt.PublishAsync(new MqttApplicationMessageBuilder().WithTopic("bss/station/TRAM-BK01/"+kind).WithPayload(JsonSerializer.Serialize(m,SqlStore.Json)).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build());
        }finally{send.Release();}
    }
    public async Task ExecuteUntil(string name,string? session){
        using var timeout=new CancellationTokenSource(10000);
        while(true){var c=await commands.Reader.ReadAsync(timeout.Token);switch(c.Command){
            case "UNLOCK_SLOT":Change(c.SlotNumber!.Value,t=>t with{IsDoorOpen=true,Current=0});break;
            case "CLOSE_SLOT":Change(c.SlotNumber!.Value,t=>t with{IsDoorOpen=false});break;
            case "REPLACE_BATTERY":
                var snapshot=await http.GetFromJsonAsync<JsonElement>("/api/v1/device/TRAM-BK01/snapshot");
                var p=snapshot.GetProperty("batteries").EnumerateArray().Single(p=>p.GetProperty("maPin").GetString()==c.BatteryId);
                Change(c.SlotNumber!.Value,t=>t with{BatteryId=c.BatteryId,Soc=p.GetProperty("soC").GetDecimal(),Soh=p.GetProperty("soH").GetDecimal(),Temperature=p.GetProperty("nhietDo").GetDecimal(),Voltage=p.GetProperty("dienAp").GetDecimal(),Current=0,IsDoorOpen=false,IsLocked=false});break;
            case "RETURN_OLD_BATTERY":Change(c.SlotNumber!.Value,t=>t with{IsDoorOpen=true,IsLocked=false,Current=0});break;
            case "EJECT_BATTERY":Change(c.SlotNumber!.Value,t=>t with{IsDoorOpen=true,Current=0});break;
            case "AUTO_RESET_ALARM":
            case "RESET_ALARM":Change(c.SlotNumber!.Value,t=>t with{Temperature=30,IsLocked=c.PreserveStationLock,IsDoorOpen=false,Current=0});break;
            case "CUT_OFF":for(int i=0;i<data.Count;i++)data[i]=data[i] with{Current=0,IsLocked=true,IsDoorOpen=false};break;
        }
        await Publish("acks","ACK",c.CommandId,null,c.SlotNumber,c.BatteryId);
        if(c.Command==name&&(session==null||c.SessionId==session)){
            for(int attempt=0;attempt<60;attempt++){
                var snap=await http.GetFromJsonAsync<JsonElement>("/api/v1/device/TRAM-BK01/snapshot");
                if(snap.GetProperty("commands").EnumerateArray().Any(x=>x.GetProperty("commandId").GetString()==c.CommandId && x.GetProperty("status").GetString()=="Completed"))return;
                await Task.Delay(150);
            }
            throw new Exception("ACK was not persisted: "+c.Command);
        }}
    }
    public Task Event(SwapSession s,string name,bool change=true){
        int slot=name is "OLD_INSERTED" or "OLD_RETURNED"?s.ReturnSlot:s.ReceiveSlot;string battery=name is "OLD_INSERTED" or "OLD_RETURNED"?s.OldBatteryId:s.NewBatteryId;
        if(change)Change(slot,t=>name=="OLD_INSERTED"?t with{BatteryId=battery,Soc=18,Soh=95,Temperature=30,IsDoorOpen=false,Current=0}:t with{BatteryId=null,Soc=0,IsDoorOpen=false,Current=0});
        return Publish("events",name,null,s,slot,battery);
    }
    public async ValueTask DisposeAsync(){if(disposed)return;disposed=true;stopping.Cancel();if(heartbeat!=null)await heartbeat;mqtt.Dispose();stopping.Dispose();}
}
