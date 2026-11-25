using BatterySwapping.Contracts;
using BatterySwapping.Core;

internal static class CooldownRegression
{
    public static void Run()
    {
        // This suite never reads the running application's connection string.
        var store=new SqlStore(@"Server=.\SQLEXPRESS;Database=BssArchitectureTests_20261121;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5;");
        foreach(var script in new[]{"06_Runtime.sql","07_Business.sql","08_Maintenance.sql","09_Cooldowns.sql","10_AutoThermal.sql","11_ThirdThermal.sql","12_BatteryThermalIncidents.sql"})
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
        MaintenanceOrder Reset(bool admin=false)=>engine.CreateMaintenance(new MaintenanceOrderRequest(station,1,"RESET_ALARM",null,"Cooldown regression",Guid.NewGuid().ToString()),1,admin);
        SlotCooldown Cooldown()=>engine.Cooldowns(station).Single();
        DeviceCommand[] Automatic()=>engine.Commands(station).Where(c=>c.Command=="AUTO_RESET_ALARM").ToArray();
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
            engine.Tick();
            Check(Automatic().Length==0 && Locked(),"4 minutes 59 seconds cannot queue automatic recovery");
            clock.Set(first.UnlockAfter);
            slots[0]=slots[0] with{Temperature=60};Send();engine.Tick();
            Check(Locked()&&Cooldown().ClearedAt==null&&Automatic().Length==0,"five minutes expiring while hot never unlocks or queues recovery");
            for(int incident=1;incident<=3;incident++)
            {
                if(incident>1)
                {
                    clock.Advance(TimeSpan.FromSeconds(1));
                    slots[0]=slots[0] with{Temperature=65,IsLocked=true};Send();
                    var started=Cooldown();
                    Check(started.ClearedAt==null && started.UnlockAfter==clock.GetUtcNow().AddMinutes(5),$"episode {incident} starts a fresh five-minute deadline");
                    if(incident==3)
                    {
                        // Simulate a still-active third episode recorded by the previous policy.
                        store.Write(db=>db.Exec("UPDATE dbo.BssSlotCooldowns SET RequiresAdmin=0 WHERE StationId=@s",("@s",station)));
                        var migration=File.ReadAllText(Path.Combine("Database","11_ThirdThermal.sql"));
                        store.Migrate(migration);store.Migrate(migration);
                        Check(Cooldown()==started,"third-episode migration is repeatable and preserves timer, battery, count and active history");
                    }
                    Send();Send();
                    Check(Cooldown().IncidentCount==incident,$"episode {incident} counts once despite repeated hot telemetry");
                    slots[0]=slots[0] with{Temperature=30};Send();
                    clock.Set(started.UnlockAfter.AddSeconds(-1));engine.Tick();
                    Check(Automatic().Length==incident-1 && Locked(),$"episode {incident} remains locked before minimum deadline");
                    Denied(()=>Reset(true),$"episode {incident} even Admin cannot reset before deadline");
                    clock.Set(started.UnlockAfter);
                }
                Check(Cooldown().IncidentCount==incident && Cooldown().RequiresAdmin==(incident>=3),$"episode {incident} persists exact count and Admin requirement");
                engine=new BssEngine(store,clock);
                Check(Cooldown().IncidentCount==incident,$"episode {incident} count survives engine recreation");
                slots[0]=slots[0] with{Temperature=35,IsLocked=true};Send();engine.Tick();
                Check(Automatic().Length==incident-1 && Locked(),$"episode {incident} exactly 35 degrees is insufficient for recovery");
                slots[0]=slots[0] with{Temperature=30,IsLocked=true};Send();
                if(incident<=2)
                {
                    Denied(()=>Reset(),$"episode {incident} rejects technician manual reset and waits for automation");
                    Denied(()=>Reset(true),$"episode {incident} rejects unnecessary Admin manual reset");
                    engine.Tick();engine.Tick();
                    var automatic=Automatic();
                    Check(automatic.Length==incident,$"episode {incident} queues exactly one automatic recovery command");
                    var command=automatic.Single(c=>c.ThermalStartedAt==Cooldown().StartedAt);
                    Check(command.SlotNumber==1 && command.BatteryId==pin && !command.AdminThermalReset,"automatic command targets the same battery and episode without Admin authorization");
                    Claim(command.CommandId);
                    Check(Locked()&&Cooldown().ClearedAt==null,"queue and dispatch do not unlock before device acknowledgement");
                    if(incident==1)
                    {
                        slots[0]=slots[0] with{Temperature=60,IsLocked=command.PreserveStationLock};
                        Denied(()=>Send(command.CommandId),"hot automatic success acknowledgement cannot finish recovery");
                        Check(Locked()&&Cooldown().ClearedAt==null,"rejected hot acknowledgement preserves isolation");
                    }
                    slots[0]=slots[0] with{Temperature=30,IsLocked=command.PreserveStationLock};Send(command.CommandId);
                    Check(!Locked() && Cooldown().ClearedAt==clock.GetUtcNow() && Cooldown().IncidentCount==incident,$"episode {incident} safe automatic ACK clears lock and preserves incident history");
                    engine.Tick();
                    Check(Automatic().Length==incident,"completed recovery is not queued again");
                }
                else
                {
                    engine.Tick();engine.Tick();
                    Check(Automatic().Length==2 && Locked() && Cooldown().ClearedAt==null,"third episode stays locked and never queues automatic reset");
                    Denied(()=>Reset(),"third episode technician cannot approve recovery");
                    Denied(()=>engine.RequestCommand(station,new CommandRequest("UNLOCK_SLOT",1)),"third episode cannot bypass Admin reset through door command");
                    var reset=Reset(true);var command=Claim(reset.CommandId);
                    Check(command.AdminThermalReset && command.ThermalStartedAt==Cooldown().StartedAt,"third reset records Admin approval for the current episode");
                    Check(Locked()&&Cooldown().ClearedAt==null,"Admin approval alone still requires device acknowledgement");
                    slots[0]=slots[0] with{Temperature=30,IsLocked=command.PreserveStationLock};Send(reset.CommandId);
                    Check(!Locked() && Cooldown().ClearedAt==clock.GetUtcNow() && Cooldown().IncidentCount==0 && !Cooldown().RequiresAdmin,"safe Admin recovery clears the third lock and resets incident counter");
                    Check(engine.MaintenanceOrders().Single(x=>x.Id==reset.Id).Status=="Completed","Admin maintenance completes only after safe acknowledgement");
                    var thermalAfterFirstReset=engine.ThermalStates().Single(x=>x.BatteryId==pin);
                    Check(thermalAfterFirstReset.IncidentCount==0 && thermalAfterFirstReset.TotalIncidents==3 && !thermalAfterFirstReset.RequiresAdmin && thermalAfterFirstReset.LastAdminResetAt==clock.GetUtcNow(),"ThermalStates exposes TotalIncidents >= 3 and LastAdminResetAt after first Admin reset");
                }
            }
            var ordinary=Reset();var ordinaryCommand=Claim(ordinary.CommandId);
            Check(!ordinaryCommand.AdminThermalReset && ordinaryCommand.ThermalStartedAt==null,"ordinary nonthermal technician maintenance remains available");
            slots[0]=slots[0] with{Temperature=30,IsLocked=ordinaryCommand.PreserveStationLock};Send(ordinary.CommandId);
            Check(engine.MaintenanceOrders().Single(x=>x.Id==ordinary.Id).Status=="Completed" && Cooldown().IncidentCount==0,"ordinary maintenance completes without changing cleared thermal history");
            clock.Advance(TimeSpan.FromSeconds(1));slots[0]=slots[0] with{Temperature=65,IsLocked=true};Send();
            Check(Cooldown().IncidentCount==1 && !Cooldown().RequiresAdmin && Cooldown().ClearedAt==null,"first incident after Admin reset begins a new automatic-recovery cycle");
            Denied(()=>engine.RequestCommand(station,new CommandRequest("UNLOCK_MAINTENANCE",1)),"manual unlock cannot bypass active thermal cooldown");
            int hotAlertId=store.Write(db=>Convert.ToInt32(db.Scalar("INSERT dbo.CanhBaoIoT(MaTram,MaNgan,MaPin,LoaiCanhBao,GiaTriDo,MucDoNghiemTrong) VALUES(@s,1,@p,N'QuaNhiet',65,N'KhanCap'); SELECT CAST(SCOPE_IDENTITY() AS int)",("@s",station),("@p",pin))));
            Denied(()=>engine.ResolveAlert(hotAlertId,"Admin (Admin)","Attempt bypass"),"direct alert resolve rejected during active thermal cooldown");

            // Empty slot manual lock/unlock via UNLOCK_MAINTENANCE without requiring a battery.
            slots[1]=slots[1] with{IsDoorOpen=false,IsLocked=true};
            var lockEmpty=engine.RequestCommand(station,new CommandRequest("LOCK_SLOT",2));Claim(lockEmpty.CommandId);Send(lockEmpty.CommandId);
            Check(store.Read(db=>Convert.ToBoolean(db.Scalar("SELECT KhoaBaoTri FROM dbo.NganTu WHERE MaTram=@s AND SoThuTu=2",("@s",station)))),"empty slot can be locked for maintenance");
            var unlockEmpty=engine.RequestCommand(station,new CommandRequest("UNLOCK_MAINTENANCE",2));Claim(unlockEmpty.CommandId);
            slots[1]=slots[1] with{IsLocked=false,IsDoorOpen=false};Send(unlockEmpty.CommandId);
            Check(!store.Read(db=>Convert.ToBoolean(db.Scalar("SELECT KhoaBaoTri FROM dbo.NganTu WHERE MaTram=@s AND SoThuTu=2",("@s",station)))),"empty slot unlocks cleanly via UNLOCK_MAINTENANCE");

            // Complete episode 1 on slot 1, then move battery to slot 2 to verify cross-slot cumulative IncidentCount.
            clock.Set(Cooldown().UnlockAfter);slots[0]=slots[0] with{Temperature=30,IsLocked=true};Send();
            engine.Tick();var autoSlot1=Automatic().OrderByDescending(c=>c.Timestamp).First();Claim(autoSlot1.CommandId);
            slots[0]=slots[0] with{Temperature=30,IsLocked=false};Send(autoSlot1.CommandId);
            store.Write(db=>db.Exec("UPDATE dbo.NganTu SET MaPinHienTai=NULL,TrangThaiNgan=N'Trong' WHERE MaTram=@s AND SoThuTu=1; UPDATE dbo.NganTu SET MaPinHienTai=@p,TrangThaiNgan=N'CoPin' WHERE MaTram=@s AND SoThuTu=2",("@s",station),("@p",pin)));
            slots[0]=new SlotTelemetry(1,null,0,0,28,0,0,false,false,false);
            slots[1]=new SlotTelemetry(2,pin,95,96,65,73,0,false,false,true);
            clock.Advance(TimeSpan.FromSeconds(1));Send();
            var slot2Ep2=engine.Cooldowns(station).Single(c=>c.SlotNumber==2);
            Check(slot2Ep2.IncidentCount==2 && !slot2Ep2.RequiresAdmin,"moving battery to another slot preserves cumulative IncidentCount (2)");
            clock.Set(slot2Ep2.UnlockAfter);slots[1]=slots[1] with{Temperature=30,IsLocked=true};Send();
            engine.Tick();var autoSlot2=Automatic().OrderByDescending(c=>c.Timestamp).First();Claim(autoSlot2.CommandId);
            slots[1]=slots[1] with{Temperature=30,IsLocked=false};Send(autoSlot2.CommandId);
            clock.Advance(TimeSpan.FromSeconds(1));slots[1]=slots[1] with{Temperature=65,IsLocked=true};Send();
            var slot2Ep3=engine.Cooldowns(station).Single(c=>c.SlotNumber==2);
            Check(slot2Ep3.IncidentCount==3 && slot2Ep3.RequiresAdmin,"third cumulative thermal episode across slots requires Admin");
            clock.Set(slot2Ep3.UnlockAfter);slots[1]=slots[1] with{Temperature=30,IsLocked=true};Send();
            var adminReset2=engine.CreateMaintenance(new MaintenanceOrderRequest(station,2,"RESET_ALARM",null,"Admin reset cross-slot",Guid.NewGuid().ToString()),1,true);
            Claim(adminReset2.CommandId);slots[1]=slots[1] with{Temperature=30,IsLocked=false};Send(adminReset2.CommandId);
            Check(engine.Cooldowns(station).Single(c=>c.SlotNumber==2).IncidentCount==0,"Admin reset clears cross-slot streak");
            Check(store.Read(db=>Convert.ToInt32(db.Scalar("SELECT IncidentCount FROM dbo.BssBatteryThermalState WHERE BatteryId=@p",("@p",pin))))==0 && store.Read(db=>Convert.ToInt32(db.Scalar("SELECT TotalIncidents FROM dbo.BssBatteryThermalState WHERE BatteryId=@p",("@p",pin))))==6,"battery thermal state resets streak while preserving total incident audit");
            var thermalAfterSecondReset=engine.ThermalStates().Single(x=>x.BatteryId==pin);
            Check(thermalAfterSecondReset.IncidentCount==0 && thermalAfterSecondReset.TotalIncidents>=3 && thermalAfterSecondReset.TotalIncidents==6 && !thermalAfterSecondReset.RequiresAdmin && thermalAfterSecondReset.LastAdminResetAt==clock.GetUtcNow(),"ThermalStates exposes cumulative TotalIncidents >= 3 after cross-slot Admin reset");
            Check(store.Read(db=>Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.BssBatteryThermalEvents WHERE BatteryId=@p AND EventType='AdminReset'",("@p",pin))))==2,"thermal audit log records Admin resets");

            // UNLOCK_MAINTENANCE works on a battery at 40C (>= 35C, < 55C) without thermal cooldown.
            slots[1]=slots[1] with{Temperature=40,IsLocked=true};Send();
            var lockWarm=engine.RequestCommand(station,new CommandRequest("LOCK_SLOT",2));Claim(lockWarm.CommandId);Send(lockWarm.CommandId);
            var unlockWarm=engine.RequestCommand(station,new CommandRequest("UNLOCK_MAINTENANCE",2));Claim(unlockWarm.CommandId);
            slots[1]=slots[1] with{IsLocked=false,IsDoorOpen=false};Send(unlockWarm.CommandId);
            Check(!store.Read(db=>Convert.ToBoolean(db.Scalar("SELECT KhoaBaoTri FROM dbo.NganTu WHERE MaTram=@s AND SoThuTu=2",("@s",station)))),"UNLOCK_MAINTENANCE unlocks 40C non-overheated slot without <35C requirement");

            // Safe alert resolution for mild alert without locking slot.
            int mildAlertId=store.Write(db=>Convert.ToInt32(db.Scalar("INSERT dbo.CanhBaoIoT(MaTram,MaNgan,MaPin,LoaiCanhBao,GiaTriDo,MucDoNghiemTrong) VALUES(@s,2,@p,N'NhietDoTang',40,N'CanhBao'); SELECT CAST(SCOPE_IDENTITY() AS int)",("@s",station),("@p",pin))));
            var resolvedAlert=engine.ResolveAlert(mildAlertId,"Tech (KyThuatVien)","Mild warning checked");
            Check(Convert.ToBoolean(resolvedAlert["DaXuLy"]) && !store.Read(db=>Convert.ToBoolean(db.Scalar("SELECT KhoaBaoTri FROM dbo.NganTu WHERE MaTram=@s AND SoThuTu=2",("@s",station)))),"safe alert resolve marks DaXuLy=1 without locking healthy slot");
            Console.WriteLine("RESULT cooldown regression passed without waiting or accelerating the production timer");
        }
        finally
        {
            store.Write(db=>db.Exec("DELETE dbo.BssBatteryThermalEvents WHERE BatteryId=@p OR StationId=@s; DELETE dbo.BssBatteryThermalState WHERE BatteryId=@p; DELETE l FROM dbo.BssMaintenanceLocks l JOIN dbo.BssMaintenance m ON m.Id=l.MaintenanceId WHERE m.StationId=@s; DELETE dbo.BssMaintenance WHERE StationId=@s; DELETE dbo.BssCommands WHERE StationId=@s; DELETE dbo.BssSlotCooldowns WHERE StationId=@s; DELETE dbo.BssDevices WHERE StationId=@s; DELETE dbo.CanhBaoIoT WHERE MaTram=@s; DELETE dbo.NganTu WHERE MaTram=@s; DELETE dbo.Pin WHERE MaPin=@p; DELETE dbo.TramPin WHERE MaTram=@s",("@s",station),("@p",pin)));
        }
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception("FAIL "+message);Console.WriteLine("PASS "+message);}
    static void Denied(Action action,string message){try{action();}catch(DomainException e) when(e.Status is 403 or 409){Check(true,message);return;}throw new Exception("FAIL "+message);}
    sealed class ManualClock(DateTimeOffset now):TimeProvider
    {
        DateTimeOffset value=now;
        public override DateTimeOffset GetUtcNow()=>value;
        public void Advance(TimeSpan delta)=>value+=delta;
        public void Set(DateTimeOffset next)=>value=next;
    }
}
