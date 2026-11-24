using BatterySwapping.Contracts;
using BatterySwapping.Core;

internal static class CooldownRegression
{
    public static void Run()
    {
        // This suite never reads the running application's connection string.
        var store=new SqlStore(@"Server=.\SQLEXPRESS;Database=BssArchitectureTests_20261121;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5;");
        foreach(var script in new[]{"06_Runtime.sql","07_Business.sql","08_Maintenance.sql","09_Cooldowns.sql"})
            store.Migrate(File.ReadAllText(Path.Combine("Database",script)));
        string suffix=Guid.NewGuid().ToString("N")[..10];
        string station="COOL-"+suffix,pin="COOLPIN-"+suffix;
        var clock=new ManualClock(DateTimeOffset.UtcNow);
        var engine=new BssEngine(store,clock);
        store.Write(db=>db.Exec("INSERT dbo.TramPin(MaTram,TenTram,DiaChi,SoLuongNgan) VALUES(@s,N'Cooldown test',N'Isolated fixture',2); INSERT dbo.Pin(MaPin,LoaiPin,SoC,SoH,NhietDo,TrangThai) VALUES(@p,N'Lithium LFP 72V-30Ah',95,96,30,N'DaDay'); INSERT dbo.NganTu(MaTram,SoThuTu,MaPinHienTai,TrangThaiNgan) VALUES(@s,1,@p,N'CoPin'),(@s,2,NULL,N'Trong')",("@s",station),("@p",pin)));
        string lease=engine.Lease(new DeviceLease(station,"cooldown-regression")).LeaseId;
        long sequence=0;
        var slots=new[]{new SlotTelemetry(1,pin,95,96,65,73,0,false,false,true),new SlotTelemetry(2,null,0,0,28,0,0,false,false,false)};
        void Send(string? command=null)
        {
            engine.Receive(new DeviceMessage(1,station,lease,Guid.NewGuid().ToString("N"),++sequence,DateTimeOffset.UtcNow,command==null?null:"ACK",command,null,null,null,true,slots));
        }
        MaintenanceOrder Reset()=>engine.CreateMaintenance(new MaintenanceOrderRequest(station,1,"RESET_ALARM",null,"Cooldown regression",Guid.NewGuid().ToString()),1);
        bool Locked()=>store.Read(db=>Convert.ToBoolean(db.Scalar("SELECT KhoaBaoTri FROM dbo.NganTu WHERE MaTram=@s AND SoThuTu=1",("@s",station))));
        DeviceCommand Claim(string expected)
        {
            // Call only with the fixture server stopped: ClaimCommand owns the global outbox.
            var c=engine.ClaimCommand();
            Check(c?.CommandId==expected,"requested device command dispatched");
            return c!;
        }
        try
        {
            slots[0]=slots[0] with{Temperature=30,IsLocked=false};Send();
            var queuedRestore=engine.RequestCommand(station,new CommandRequest("RESTORE_STATION"));
            var queuedUnlock=engine.RequestCommand(station,new CommandRequest("UNLOCK_SLOT",1));
            slots[0]=slots[0] with{Temperature=65,IsLocked=true};
            Send();
            Check(engine.ClaimCommand()==null&&engine.ClaimCommand()==null,"commands queued before overheating are not dispatched after isolation");
            Check(engine.Commands(station).Single(c=>c.CommandId==queuedRestore.CommandId).Status=="Failed","queued station restore cannot bypass new cooldown");
            Check(engine.Commands(station).Single(c=>c.CommandId==queuedUnlock.CommandId).Status=="Failed","queued door unlock cannot bypass new cooldown");
            var first=engine.Cooldowns(station).Single();
            Check(first.UnlockAfter-first.StartedAt==TimeSpan.FromMinutes(5),"overheat creates exactly five real minutes of isolation");
            Check(Locked(),"overheat locks the affected cabinet slot");
            clock.Advance(TimeSpan.FromSeconds(20));Send();
            Check(engine.Cooldowns(station).Single().UnlockAfter==first.UnlockAfter,"repeated hot telemetry cannot extend the same episode");
            engine=new BssEngine(store,clock);
            Check(engine.Cooldowns(station).Single().UnlockAfter==first.UnlockAfter,"cooldown deadline survives engine recreation");
            slots[0]=slots[0] with{Temperature=30};Send();
            Denied(()=>engine.RequestCommand(station,new CommandRequest("UNLOCK_SLOT",1)),"cooled pin cannot open before five minutes");
            Denied(()=>Reset(),"technician cannot reset before five minutes");
            var other=engine.RequestCommand(station,new CommandRequest("UNLOCK_SLOT",2));Claim(other.CommandId);
            slots[1]=slots[1] with{IsDoorOpen=true};Send(other.CommandId);
            Check(store.Read(db=>Convert.ToBoolean(db.Scalar("SELECT CuaDangMo FROM dbo.NganTu WHERE MaTram=@s AND SoThuTu=2",("@s",station)))),"unaffected slot remains independently operable");
            clock.Set(first.UnlockAfter.AddSeconds(-1));
            Denied(()=>Reset(),"4 minutes 59 seconds is still locked");
            clock.Set(first.UnlockAfter);
            slots[0]=slots[0] with{Temperature=60};Send();
            Check(Locked()&&engine.Cooldowns(station).Single().ClearedAt==null,"five minutes expiring while hot never unlocks slot");
            var reset=Reset();Claim(reset.CommandId);
            slots[0]=slots[0] with{IsLocked=false};
            Denied(()=>Send(reset.CommandId),"hot success acknowledgement cannot finish maintenance");
            Check(Locked(),"rejected hot acknowledgement preserves isolation");
            slots[0]=slots[0] with{Temperature=30,IsLocked=true};Send();
            Check(Locked(),"cooled and expired still requires device-confirmed technician reset");
            slots[0]=slots[0] with{IsLocked=false};Send(reset.CommandId);
            Check(!Locked()&&engine.Cooldowns(station).Single().ClearedAt==clock.GetUtcNow(),"safe reset acknowledgement clears elapsed cooldown");
            Check(engine.MaintenanceOrders().Single(x=>x.Id==reset.Id).Status=="Completed","maintenance completes only after safe acknowledgement");
            clock.Advance(TimeSpan.FromSeconds(1));slots[0]=slots[0] with{Temperature=65,IsLocked=true};Send();
            var second=engine.Cooldowns(station).Single();
            Check(second.ClearedAt==null&&second.StartedAt>first.StartedAt&&second.UnlockAfter==clock.GetUtcNow().AddMinutes(5),"new overheating after reset starts a fresh five-minute episode");
            Console.WriteLine("RESULT cooldown regression passed without waiting or accelerating the production timer");
        }
        finally
        {
            store.Write(db=>db.Exec("DELETE l FROM dbo.BssMaintenanceLocks l JOIN dbo.BssMaintenance m ON m.Id=l.MaintenanceId WHERE m.StationId=@s; DELETE dbo.BssMaintenance WHERE StationId=@s; DELETE dbo.BssCommands WHERE StationId=@s; DELETE dbo.BssSlotCooldowns WHERE StationId=@s; DELETE dbo.BssDevices WHERE StationId=@s; DELETE dbo.CanhBaoIoT WHERE MaTram=@s; DELETE dbo.NganTu WHERE MaTram=@s; DELETE dbo.Pin WHERE MaPin=@p; DELETE dbo.TramPin WHERE MaTram=@s",("@s",station),("@p",pin)));
        }
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception("FAIL "+message);Console.WriteLine("PASS "+message);}
    static void Denied(Action action,string message){try{action();}catch(DomainException){Check(true,message);return;}throw new Exception("FAIL "+message);}
    sealed class ManualClock(DateTimeOffset now):TimeProvider
    {
        DateTimeOffset value=now;
        public override DateTimeOffset GetUtcNow()=>value;
        public void Advance(TimeSpan delta)=>value+=delta;
        public void Set(DateTimeOffset next)=>value=next;
    }
}
