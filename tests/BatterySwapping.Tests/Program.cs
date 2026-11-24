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
var settings=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,".runtime/server.json"))).RootElement;
string cs=settings.GetProperty("ConnectionStrings").GetProperty("Bss").GetString()!;
if(!cs.Contains("Database=BssArchitectureTests_",StringComparison.OrdinalIgnoreCase))throw new Exception("Integration tests refuse to use the application database.");
var store=new SqlStore(cs);var engine=new BssEngine(store);
using var admin=new ApiClient();using var driver=new ApiClient();using var other=new ApiClient();using var tech=new ApiClient();
await admin.Login("admin","123456");await driver.Login("driver01","123456");await other.Login("driver02","123456");await tech.Login("tech01","123456");
int count=0;
void Check(bool condition,string name){if(!condition)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);count++;}
using var raw=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:5080")};
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

    // Test Kiosk authentication & in-app simulation events
    var kioskResp = await raw.PostAsJsonAsync("/api/v1/auth/kiosk", new KioskAuthRequest("0987654321"));
    Check(kioskResp.IsSuccessStatusCode, "kiosk login with existing driver phone succeeds");
    var kioskUser = (await kioskResp.Content.ReadFromJsonAsync<LoginResponse>())!.User;
    Check(kioskUser.Role == "KhachHang" && kioskUser.CustomerId == 1, "kiosk login returns correct customer");

    using var kioskClient = new ApiClient();
    await kioskClient.KioskLogin("0901234567");
    Check(kioskClient.User?.Role == "KhachHang" && kioskClient.User?.CustomerId > 0, "kiosk login provisions and authenticates 0901234567");

    var simSwapKey = Guid.NewGuid().ToString("N");
    var simSession = await kioskClient.Post<SwapSession>("swaps", new StartSwap("TRAM-BK01", simSwapKey));
    Check(simSession.State == "AwaitingOldBattery", "kiosk started swap in AwaitingOldBattery");

    var oldEvResp = await raw.PostAsJsonAsync($"/api/v1/swaps/{simSession.Id}/events", new SwapEventRequest("OLD_INSERTED"));
    var oldEvBody = await oldEvResp.Content.ReadAsStringAsync();
    Check(oldEvResp.IsSuccessStatusCode, "simulated OLD_INSERTED event accepted: " + oldEvBody);
    var simSessionDispensing = (await oldEvResp.Content.ReadFromJsonAsync<SwapSession>())!;
    Check(simSessionDispensing.State == "Dispensing" && simSessionDispensing.OldInserted, "OLD_INSERTED transitions session to Dispensing");

    var newEvResp = await raw.PostAsJsonAsync($"/api/v1/swaps/{simSession.Id}/events", new SwapEventRequest("NEW_TAKEN"));
    Check(newEvResp.IsSuccessStatusCode, "simulated NEW_TAKEN event accepted");
    var simSessionCompleted = (await newEvResp.Content.ReadFromJsonAsync<SwapSession>())!;
    Check(simSessionCompleted.State == "Completed", "NEW_TAKEN completes swap session");

    int hotSlot=4;device.Change(hotSlot,t=>t with {Temperature=65,Current=0,IsLocked=true});await device.Publish("alerts");
    await Wait(()=>Task.FromResult(store.Read(db=>Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.NganTu n JOIN dbo.Pin p ON p.MaPin=n.MaPinHienTai WHERE n.MaTram='TRAM-BK01' AND n.SoThuTu=4 AND n.KhoaBaoTri=1 AND p.DongDien=0")))==1));
    Check(true,"overheat telemetry triggers SQL cutoff and lock");
    var reset=await tech.Post<DeviceCommand>("stations/TRAM-BK01/commands",new CommandRequest("RESET_ALARM",hotSlot));
    Check(reset.Status=="Pending","reset stays pending before device acknowledgement");
    await device.ExecuteUntil("RESET_ALARM",null);
    await Wait(async()=>(await tech.Get<DeviceCommand>($"commands/{reset.CommandId}")).Status=="Completed");
    Check(store.Read(db=>Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.CanhBaoIoT WHERE MaTram='TRAM-BK01' AND MaNgan=4 AND DaXuLy=0")))==0,"safe acknowledgement resolves alert");
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
    string pidFile=Path.Combine(root,".runtime/processes.json");
    var processes=JsonNode.Parse(File.ReadAllText(pidFile))!.AsArray();
    string exe=Path.GetFullPath(Path.Combine(root,"src/BatterySwapping.Server/bin/Release/net8.0/BatterySwapping.Server.exe"));
    var entry=processes.First(n=>string.Equals(n?["Path"]?.GetValue<string>(),exe,StringComparison.OrdinalIgnoreCase))!;
    using(var running=Process.GetProcessById(entry["Id"]!.GetValue<int>()))
    {
        if(!string.Equals(running.MainModule?.FileName,exe,StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected server process");
        running.Kill();await running.WaitForExitAsync();
    }
    var info=new ProcessStartInfo(exe){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
    info.Environment["BSS_SETTINGS"]=Path.Combine(root,".runtime/server.json");
    var restarted=Process.Start(info)!;
    restarted.OutputDataReceived+=(_,_)=>{};restarted.ErrorDataReceived+=(_,_)=>{};restarted.BeginOutputReadLine();restarted.BeginErrorReadLine();
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
        await mqtt.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1",18883).WithCredentials("simulator",config.GetProperty("password").GetString()).Build());
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
            case "EJECT_BATTERY":Change(c.SlotNumber!.Value,t=>t with{IsDoorOpen=true,Current=0});break;
            case "RESET_ALARM":Change(c.SlotNumber!.Value,t=>t with{Temperature=30,IsLocked=false,IsDoorOpen=false,Current=0});break;
            case "CUT_OFF":for(int i=0;i<data.Count;i++)data[i]=data[i] with{Current=0,IsLocked=true,IsDoorOpen=false};break;
        }
        await Publish("acks","ACK",c.CommandId,null,c.SlotNumber,c.BatteryId);
        if(c.Command==name&&(session==null||c.SessionId==session)){await Task.Delay(200);return;}}
    }
    public Task Event(SwapSession s,string name,bool change=true){
        int slot=name=="OLD_INSERTED"?s.ReturnSlot:s.ReceiveSlot;string battery=name=="OLD_INSERTED"?s.OldBatteryId:s.NewBatteryId;
        if(change)Change(slot,t=>name=="OLD_INSERTED"?t with{BatteryId=battery,Soc=18,Soh=95,Temperature=30,IsDoorOpen=false,Current=0}:t with{BatteryId=null,Soc=0,IsDoorOpen=false,Current=0});
        return Publish("events",name,null,s,slot,battery);
    }
    public async ValueTask DisposeAsync(){if(disposed)return;disposed=true;stopping.Cancel();if(heartbeat!=null)await heartbeat;mqtt.Dispose();stopping.Dispose();}
}
