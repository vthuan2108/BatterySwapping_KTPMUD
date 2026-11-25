using System.Text.Json;
using BatterySwapping.Contracts;
using BatterySwapping.Core;

internal static class SimulationRegression
{
    public static void Run()
    {
        // Never target the running application's database or its assets.
        var store = new SqlStore(@"Server=.\SQLEXPRESS;Database=BssArchitectureTests_20261121;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5;");
        foreach (string script in new[] { "06_Runtime.sql", "07_Business.sql", "08_Maintenance.sql", "09_Cooldowns.sql", "10_AutoThermal.sql", "11_ThirdThermal.sql", "12_BatteryThermalIncidents.sql" })
            store.Migrate(File.ReadAllText(Path.Combine("Database", script)));
        string suffix = Guid.NewGuid().ToString("N")[..10], station = "SIM-" + suffix;
        string[] pins = Enumerable.Range(1, 5).Select(n => "SP" + n + "-" + suffix).ToArray();
        string client = "simulation-regression-" + suffix, lease = Guid.NewGuid().ToString("N");
        var reset = new SimulationOperations(store);
        int customer = store.Write(db =>
        {
            db.Exec("INSERT dbo.TramPin(MaTram,TenTram,DiaChi,SoLuongNgan) VALUES(@s,N'Simulation test',N'Isolated fixture',4)", ("@s", station));
            foreach (string pin in pins)
                db.Exec("INSERT dbo.Pin(MaPin,LoaiPin,DungLuongThietKe,SoC,SoH,NhietDo) VALUES(@p,N'Lithium LFP 72V-30Ah',37,50,81,42)", ("@p", pin));
            db.Exec("INSERT dbo.NganTu(MaTram,SoThuTu,MaPinHienTai,CuaDangMo,KhoaBaoTri) VALUES(@s,1,@p1,1,1),(@s,2,@p2,1,1),(@s,3,@p3,1,1),(@s,4,NULL,1,1); INSERT dbo.BssBatteryLocations VALUES(@p5,'Warehouse'); INSERT dbo.BssDevices VALUES(@s,@c,@l,SYSDATETIMEOFFSET(),10)", ("@s", station), ("@p1", pins[0]), ("@p2", pins[1]), ("@p3", pins[2]), ("@p5", pins[4]), ("@c", client), ("@l", lease));
            return Convert.ToInt32(db.Scalar("INSERT dbo.KhachHang(HoTen,SoDienThoai,BienSoXe,SoDuVi,MaPinDangMuon) VALUES(N'Simulation owner',@phone,'SIM',123456,@p); SELECT CAST(SCOPE_IDENTITY() AS int)", ("@phone", "SIM" + suffix), ("@p", pins[3])));
        });
        var keys = new List<string>();
        ResetSimulationRequest Request(string? overrideLease = null)
        {
            string key = Guid.NewGuid().ToString(); keys.Add(key);
            return new(key, client, overrideLease ?? lease);
        }
        void Renew() => store.Write(db => db.Exec("UPDATE dbo.BssDevices SET LastSeen=SYSDATETIMEOFFSET() WHERE StationId=@s", ("@s", station)));
        string Stable() => store.Read(db => JsonSerializer.Serialize(new
        {
            Customer = db.Rows("SELECT * FROM dbo.KhachHang WHERE MaKH=@c", ("@c", customer)),
            External = db.Rows("SELECT * FROM dbo.Pin WHERE MaPin IN(@p4,@p5) ORDER BY MaPin", ("@p4", pins[3]), ("@p5", pins[4])),
            Slots = db.Rows("SELECT MaNgan,MaPinHienTai FROM dbo.NganTu WHERE MaTram=@s ORDER BY SoThuTu", ("@s", station)),
            Capacity = db.Rows("SELECT MaPin,DungLuongThietKe FROM dbo.Pin WHERE MaPin IN(@p1,@p2,@p3) ORDER BY MaPin", ("@p1", pins[0]), ("@p2", pins[1]), ("@p3", pins[2])),
            Payments = db.Rows("SELECT * FROM dbo.LichSuNapTien WHERE MaKH=@c", ("@c", customer)),
            Swaps = db.Rows("SELECT * FROM dbo.LichSuDoiPin WHERE MaKH=@c", ("@c", customer)),
            Location = db.Rows("SELECT * FROM dbo.BssBatteryLocations WHERE BatteryId=@p", ("@p", pins[4]))
        }, SqlStore.Json));
        string Bms() => store.Read(db => JsonSerializer.Serialize(db.Rows("SELECT p.SoC,p.SoH,p.NhietDo,p.SoLanSac FROM dbo.Pin p JOIN dbo.NganTu n ON n.MaPinHienTai=p.MaPin WHERE n.MaTram=@s ORDER BY n.SoThuTu", ("@s", station)), SqlStore.Json));
        DeviceCommand Command(string type)
        {
            var command = new DeviceCommand { StationId = station, Command = type };
            store.Write(db => db.Exec("INSERT dbo.BssCommands VALUES(@id,@s,@status,@p)", ("@id", command.CommandId), ("@s", station), ("@status", command.Status), ("@p", BssEngine.Serialize(command))));
            return command;
        }
        try
        {
            store.Write(db => db.Exec("INSERT dbo.LichSuNapTien(MaKH,SoTien,PhuongThuc,TrangThai) VALUES(@c,123456,N'Test',N'ThanhCong'); INSERT dbo.BssSlotCooldowns(StationId,SlotNumber,BatteryId,StartedAt,UnlockAfter) VALUES(@s,1,@p,SYSDATETIMEOFFSET(),DATEADD(minute,5,SYSDATETIMEOFFSET()))", ("@c", customer), ("@s", station), ("@p", pins[0])));
            string stable = Stable();
            store.Write(db => db.Exec("INSERT dbo.CanhBaoIoT(MaTram,MaNgan,MaPin,LoaiCanhBao,GhiChu) VALUES(@s,1,@p,N'QuaNhiet',N'Test retained alert')", ("@s", station), ("@p", pins[0])));
            var query = Command("QUERY_STATUS");
            var request = Request(); Renew();
            var result = reset.Reset(station, request); lease = result.LeaseId;
            Check(Stable() == stable, "reset preserves ownership, wallets, history, IDs, capacity and external batteries");
            Check(store.Read(db => Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.CanhBaoIoT WHERE MaTram=@s AND DaXuLy=1 AND GhiChu LIKE N'%Test retained alert%'", ("@s", station)))) == 1, "reset acknowledges scenario alerts and preserves their history");
            Check(result.ResetId == request.RequestId && result.StationId == station && lease != request.LeaseId, "reset rotates simulator lease");
            string bms = Bms();
            Check(reset.Reset(station, request) == result && Bms() == bms, "lost-response retry returns identical reset without randomizing again");
            Check(store.Read(db => Convert.ToInt64(db.Scalar("SELECT Sequence FROM dbo.BssDevices WHERE StationId=@s", ("@s", station)))) == -1, "new simulation starts sequence at minus one");
            Check(store.Read(db => db.Rows("SELECT Status,Payload FROM dbo.BssCommands WHERE Id=@id", ("@id", query.CommandId))).All(r => (string)r["Status"]! == "Cancelled" && JsonSerializer.Deserialize<DeviceCommand>((string)r["Payload"]!, SqlStore.Json)!.Status == "Cancelled"), "query status and payload are both cancelled");
            var rows = store.Read(db => db.Rows("SELECT p.* FROM dbo.NganTu n JOIN dbo.Pin p ON p.MaPin=n.MaPinHienTai WHERE n.MaTram=@s ORDER BY n.SoThuTu", ("@s", station)));
            Check(rows.Select((r, index) => (decimal)r["SoC"]! >= (index < 2 ? 90 : 10) && (decimal)r["SoC"]! <= (index < 2 ? 99 : 89) && (decimal)r["SoH"]! is >= 85 and <= 100 && (decimal)r["NhietDo"]! is >= 25 and <= 34 && (int)r["SoLanSac"]! is >= 0 and <= 350).All(x => x), "random BMS stays bounded with first two occupied slots ready");
            Renew(); Denied(() => reset.Reset(station, Request("wrong-lease")), "wrong lease cannot reset simulation");
            for (int index = 0; index < rows.Count; index++)
            {
                var row = rows[index]; decimal soc = (decimal)row["SoC"]!;
                Check(index < 2 ? soc is >= 90 and <= 99 : soc is >= 10 and <= 89, "reset SoC range for cabinet slot " + (index + 1));
                Check((decimal)row["SoH"]! is >= 85 and <= 100 && (decimal)row["NhietDo"]! is >= 25 and <= 34 && (int)row["SoLanSac"]! is >= 0 and <= 350, "random BMS remains in normal initial ranges");
                decimal current = soc < 80 ? 15 : soc < 98 ? 8 : 2;
                Check((decimal)row["DongDien"]! == current && Math.Abs((decimal)row["DienAp"]! - (67.2m + 12.8m * soc / 100m + current * .04m)) <= .005m, "current and voltage use simulator charging model");
            }
            // An attempt that never reached the server must remain recoverable with
            // its original request key after the controller pauses telemetry.
            var pendingRequest = Request();
            Check(store.Read(db => db.Scalar("SELECT Id FROM dbo.BssBusinessRequests WHERE Id=@id", ("@id", pendingRequest.RequestId))) == null, "unapplied pending reset has no stored outcome");
            store.Write(db => db.Exec("UPDATE dbo.BssDevices SET LastSeen=DATEADD(second,-10,SYSDATETIMEOFFSET()) WHERE StationId=@s", ("@s", station)));
            var idleResult = reset.Reset(station, pendingRequest); lease = idleResult.LeaseId;
            Check(idleResult.ResetId == pendingRequest.RequestId && lease != pendingRequest.LeaseId, "unapplied pending reset recovers after idle with its current lease");
            bms = Bms();
            store.Write(db => db.Exec("UPDATE dbo.BssDevices SET Sequence=42,LastSeen=DATEADD(second,-10,SYSDATETIMEOFFSET()) WHERE StationId=@s", ("@s", station)));
            Check(reset.Reset(station, pendingRequest) == idleResult && Bms() == bms && store.Read(db => Convert.ToInt64(db.Scalar("SELECT Sequence FROM dbo.BssDevices WHERE StationId=@s", ("@s", station)))) == 42, "completed idle retry returns its outcome without resetting continued state");
            Check(store.Read(db => Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.NganTu WHERE MaTram=@s AND (CuaDangMo=1 OR KhoaBaoTri=1)", ("@s", station)))) == 0, "normal reset closes and unlocks every slot");
            Check(store.Read(db => db.Scalar("SELECT StationId FROM dbo.BssSlotCooldowns WHERE StationId=@s", ("@s", station))) == null, "reset starts a new incident streak");
            Check(new BssEngine(store).Commands(station).Single(c => c.CommandId == query.CommandId).Status == "Cancelled", "reset cancels harmless QUERY in stored payload");
            Denied(() => reset.Reset(station, Request("wrong-lease")), "wrong lease cannot reset");
            var active = Command("UNLOCK_SLOT"); Renew();
            Denied(() => reset.Reset(station, Request()), "pending physical command blocks reset");
            foreach (string status in new[] { "Sent", "Unconfirmed" })
            {
                store.Write(db => db.Exec("UPDATE dbo.BssCommands SET Status=@status WHERE Id=@id", ("@status", status), ("@id", active.CommandId))); Renew();
                Denied(() => reset.Reset(station, Request()), status + " physical command blocks reset");
            }
            store.Write(db => db.Exec("DELETE dbo.BssCommands WHERE Id=@id; INSERT dbo.BssSessions(Id,CustomerId,RequestKey,StationId,Active,Hold,Payload) VALUES(@session,@c,'simulation-test',@s,1,0,'{}')", ("@id", active.CommandId), ("@session", suffix), ("@c", customer), ("@s", station))); Renew();
            Denied(() => reset.Reset(station, Request()), "active swap blocks reset");
            store.Write(db => db.Exec("DELETE dbo.BssSessions WHERE Id=@session; INSERT dbo.BssMaintenance(Id,RequestId,StationId,SlotNumber,Action,Note,ActorId,CommandId,Status) VALUES(@id,@id,@s,1,'RESET_ALARM',N'Test',1,@id,'Pending'); INSERT dbo.BssMaintenanceLocks VALUES('slot:'+@s+':1',@id)", ("@session", suffix), ("@id", suffix), ("@s", station))); Renew();
            Denied(() => reset.Reset(station, Request()), "maintenance resource lock blocks reset");
            store.Write(db => db.Exec("DELETE dbo.BssMaintenanceLocks WHERE MaintenanceId=@id; DELETE dbo.BssMaintenance WHERE Id=@id", ("@id", suffix))); Renew();
            result = reset.Reset(station, Request()); lease = result.LeaseId;
            Check(Bms() != bms, "independent reset generates a varied initial BMS sample");
            store.Write(db => db.Exec("UPDATE dbo.TramPin SET TrangThaiHoatDong=N'TamDung' WHERE MaTram=@s", ("@s", station))); Renew();
            result = reset.Reset(station, Request()); lease = result.LeaseId;
            Check(store.Read(db => Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.NganTu WHERE MaTram=@s AND KhoaBaoTri=0", ("@s", station)))) == 0 && store.Read(db => Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.Pin p JOIN dbo.NganTu n ON n.MaPinHienTai=p.MaPin WHERE n.MaTram=@s AND p.DongDien<>0", ("@s", station)))) == 0, "reset preserves station isolation and stops charging");
            store.Write(db => db.Exec("UPDATE dbo.BssDevices SET ClientId='replacement-simulator',LeaseId=@l WHERE StationId=@s", ("@s", station), ("@l", Guid.NewGuid().ToString("N"))));
            Denied(() => reset.Reset(station, request), "old client cannot replay reset after simulator takeover");
            Denied(() => reset.Reset(station, request), "previous simulator cannot replay reset after ownership changes");
            Check(Stable() == stable, "all reset and rejection paths preserve financial and ownership records");
            Console.WriteLine("RESULT simulation reset regression passed");
        }
        finally
        {
            store.Write(db =>
            {
                db.Exec("DELETE dbo.BssMaintenanceLocks WHERE MaintenanceId=@id; DELETE dbo.BssMaintenance WHERE StationId=@s; DELETE dbo.BssReservations WHERE SessionId=@id; DELETE dbo.BssSessions WHERE StationId=@s; DELETE dbo.BssCommands WHERE StationId=@s; DELETE dbo.BssSlotCooldowns WHERE StationId=@s; DELETE dbo.BssDevices WHERE StationId=@s; DELETE dbo.LichSuNapTien WHERE MaKH=@c; DELETE dbo.KhachHang WHERE MaKH=@c; DELETE dbo.CanhBaoIoT WHERE MaTram=@s; DELETE dbo.NganTu WHERE MaTram=@s; DELETE dbo.BssBatteryLocations WHERE BatteryId=@p; DELETE dbo.TramPin WHERE MaTram=@s", ("@id", suffix), ("@s", station), ("@c", customer), ("@p", pins[4]));
                foreach (string key in keys) db.Exec("DELETE dbo.BssBusinessRequests WHERE Id=@id", ("@id", key));
                foreach (string pin in pins) { db.Exec("DELETE dbo.BssBatteryThermalEvents WHERE BatteryId=@p; DELETE dbo.BssBatteryThermalState WHERE BatteryId=@p; DELETE dbo.Pin WHERE MaPin=@p", ("@p", pin)); }
                return true;
            });
        }
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL " + message); Console.WriteLine("PASS " + message); }
    static void Denied(Action action, string message) { try { action(); } catch (DomainException) { Check(true, message); return; } throw new Exception("FAIL " + message); }
}
