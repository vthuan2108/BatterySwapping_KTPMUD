using System.Text.Json;
using BatterySwapping.Contracts;

namespace BatterySwapping.Core;

public sealed partial class BssEngine(SqlStore store,TimeProvider? clock=null)
{
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SqlStore.Json);
    static T Parse<T>(object? value) => JsonSerializer.Deserialize<T>((string)value!, SqlStore.Json)!;
    static bool Active(SwapSession s) => s.State is not ("Completed" or "Cancelled" or "Expired");
    static void Require(bool condition, string error) { if (!condition) throw new DomainException(error); }
    public UserInfo Login(LoginRequest request)
    {
        Require(request.Username.Length <= 50 && request.Password.Length <= 256, "Thông tin đăng nhập không hợp lệ.");
        return store.Write(db => {
            var r=db.Rows("SELECT t.*,k.MaKH FROM dbo.TaiKhoan t LEFT JOIN dbo.KhachHang k ON k.MaTK=t.MaTK WHERE TenDangNhap=@u",("@u",request.Username.Trim())).SingleOrDefault();
            if(r is null || (string)r["TrangThai"]! != "HoatDong" || !Passwords.Verify(request.Password,(string)r["MatKhau"]!))
                throw new DomainException("Tên đăng nhập hoặc mật khẩu không đúng.",401);
            if(!((string)r["MatKhau"]!).StartsWith("pbkdf2$"))
                db.Exec("UPDATE dbo.TaiKhoan SET MatKhau=@p WHERE MaTK=@id",("@p",Passwords.Hash(request.Password)),("@id",r["MaTK"]));
            return new UserInfo((int)r["MaTK"]!, (string)r["TenDangNhap"]!, (string)r["HoTen"]!, (string)r["VaiTro"]!, r["MaKH"] as int?);
        });
    }
    public StationStatus Status(string station) => store.Read(db => Status(db,station));
    static StationStatus Status(Db db,string station)
    {
        var r=db.Rows("SELECT * FROM dbo.BssDevices WHERE StationId=@s",("@s",station)).SingleOrDefault();
        var seen=r?["LastSeen"] as DateTimeOffset?;
        return new(station,r!=null && (long)r["Sequence"]!>=0 && seen>DateTimeOffset.UtcNow.AddSeconds(-5),seen,r?["ClientId"] as string);
    }
    public LeaseResponse Lease(DeviceLease request) => store.Write(db => {
        Require(!string.IsNullOrWhiteSpace(request.ClientId) && request.ClientId.Length<=64,"Client ID không hợp lệ.");
        Require(db.Scalar("SELECT MaTram FROM dbo.TramPin WHERE MaTram=@s",("@s",request.StationId))!=null,"Không tìm thấy trạm.");
        var r=db.Rows("SELECT * FROM dbo.BssDevices WHERE StationId=@s",("@s",request.StationId)).SingleOrDefault();
        Require(r==null || (DateTimeOffset)r["LastSeen"]! < DateTimeOffset.UtcNow.AddSeconds(-5),"Trạm đã có simulator đang hoạt động.");
        string lease=Guid.NewGuid().ToString("N");
        db.Exec("DELETE dbo.BssDevices WHERE StationId=@s; INSERT dbo.BssDevices(StationId,ClientId,LeaseId,LastSeen,Sequence) VALUES(@s,@c,@l,SYSDATETIMEOFFSET(),-1)",("@s",request.StationId),("@c",request.ClientId),("@l",lease));
        return new LeaseResponse(lease,request.StationId,DateTimeOffset.UtcNow.AddSeconds(5));
    });
    public SwapSession? GetSession(string id) => store.Read(db=>db.Payload<SwapSession>("BssSessions",id));
    public List<SwapSession> Sessions(int? customer=null) => store.Read(db=>db.Rows("SELECT Payload FROM dbo.BssSessions WHERE (@c IS NULL OR CustomerId=@c)",("@c",customer)).Select(r=>Parse<SwapSession>(r["Payload"])).OrderByDescending(s=>s.CreatedAt).ToList());
    public List<DeviceCommand> Commands(string? station=null) => store.Read(db=>db.Rows("SELECT Payload FROM dbo.BssCommands WHERE (@s IS NULL OR StationId=@s)",("@s",station)).Select(r=>Parse<DeviceCommand>(r["Payload"])).OrderByDescending(c=>c.Timestamp).ToList());
    public SwapSession Start(int customer, StartSwap request) => store.Write(db => {
        Require(!string.IsNullOrWhiteSpace(request.IdempotencyKey) && request.IdempotencyKey.Length<=80,"Thiếu mã chống lặp.");
        var previous=db.Scalar("SELECT Payload FROM dbo.BssSessions WHERE CustomerId=@c AND RequestKey=@k",("@c",customer),("@k",request.IdempotencyKey));
        if(previous!=null) { var p=Parse<SwapSession>(previous); Require(p.StationId==request.StationId,"Mã yêu cầu đã dùng cho trạm khác."); return p; }
        Require(Status(db,request.StationId).Online,"Simulator mất kết nối.");
        Require(db.Scalar("SELECT MaTram FROM dbo.TramPin WHERE MaTram=@s AND TrangThaiHoatDong=N'HoatDong'",("@s",request.StationId))!=null,"Trạm đang dừng hoặc bảo trì.");
        Require(db.Scalar("SELECT Id FROM dbo.BssSessions WHERE CustomerId=@c AND Active=1",("@c",customer))==null,"Khách hàng đang có phiên chưa kết thúc.");
        var cust=db.Rows("SELECT * FROM dbo.KhachHang WHERE MaKH=@c",("@c",customer)).SingleOrDefault() ?? throw new DomainException("Không tìm thấy khách hàng.");
        Require(cust["MaPinDangMuon"] is string,"Khách hàng chưa có pin để trả.");
        decimal held=Convert.ToDecimal(db.Scalar("SELECT COALESCE(SUM(Hold),0) FROM dbo.BssSessions WHERE CustomerId=@c AND Active=1",("@c",customer)));
        decimal fee=Convert.ToDecimal(db.Scalar("SELECT Fee FROM dbo.BssPolicy WHERE Id=1"));
        Require((decimal)cust["SoDuVi"]!-held>=fee,$"Số dư khả dụng không đủ {fee:N0} đ.");
        var oldBattery=db.Rows("SELECT * FROM dbo.Pin WHERE MaPin=@p",("@p",cust["MaPinDangMuon"])).SingleOrDefault();
        Require(oldBattery!=null && (string)oldBattery["LoaiPin"]! == (string)db.Scalar("SELECT SupportedBatteryType FROM dbo.BssPolicy WHERE Id=1")!,"Loại pin đang giữ không tương thích hệ thống.");
        Require(Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.KhachHang WHERE MaPinDangMuon=@p",("@p",cust["MaPinDangMuon"])))==1 && db.Scalar("SELECT TOP 1 MaNgan FROM dbo.NganTu WHERE MaPinHienTai=@p",("@p",cust["MaPinDangMuon"]))==null,"Vị trí pin đang giữ chưa được đối soát.");
        var empty=db.Rows("SELECT TOP 1 n.* FROM dbo.NganTu n WHERE n.MaTram=@s AND n.MaPinHienTai IS NULL AND n.KhoaBaoTri=0 AND n.CuaDangMo=0 AND NOT EXISTS(SELECT 1 FROM dbo.BssSlotCooldowns c WHERE c.StationId=n.MaTram AND c.SlotNumber=n.SoThuTu AND c.ClearedAt IS NULL) AND NOT EXISTS(SELECT 1 FROM dbo.BssMaintenanceLocks WHERE Resource='slot:'+n.MaTram+':'+CAST(n.SoThuTu AS varchar)) AND n.TrangThaiNgan<>N'Loi' AND NOT EXISTS(SELECT 1 FROM dbo.BssReservations WHERE Resource='slot:'+n.MaTram+':'+CAST(n.SoThuTu AS varchar)) ORDER BY n.SoThuTu",("@s",request.StationId)).SingleOrDefault();
        var full=db.Rows("SELECT TOP 1 n.*,p.SoC FROM dbo.NganTu n JOIN dbo.Pin p ON p.MaPin=n.MaPinHienTai WHERE n.MaTram=@s AND n.KhoaBaoTri=0 AND n.CuaDangMo=0 AND NOT EXISTS(SELECT 1 FROM dbo.BssSlotCooldowns c WHERE c.StationId=n.MaTram AND c.SlotNumber=n.SoThuTu AND c.ClearedAt IS NULL) AND p.LoaiPin=(SELECT SupportedBatteryType FROM dbo.BssPolicy WHERE Id=1) AND NOT EXISTS(SELECT 1 FROM dbo.KhachHang WHERE MaPinDangMuon=p.MaPin) AND NOT EXISTS(SELECT 1 FROM dbo.BssMaintenanceLocks WHERE Resource='slot:'+n.MaTram+':'+CAST(n.SoThuTu AS varchar) OR Resource='pin:'+p.MaPin) AND p.SoC>=90 AND p.SoH>=80 AND p.NhietDo<=45 AND p.TrangThai IN(N'DaDay',N'DangSac',N'KhaDung') AND NOT EXISTS(SELECT 1 FROM dbo.BssReservations WHERE Resource='slot:'+n.MaTram+':'+CAST(n.SoThuTu AS varchar) OR Resource='pin:'+p.MaPin) ORDER BY p.SoC DESC,p.NhietDo,p.SoLanSac",("@s",request.StationId)).SingleOrDefault();
        Require(empty!=null,"Không còn ngăn trống."); Require(full!=null,"Không có pin đạt chuẩn.");
        var s=new SwapSession { Fee=fee,CustomerId=customer,Key=request.IdempotencyKey,StationId=request.StationId,OldBatteryId=(string)cust["MaPinDangMuon"]!,NewBatteryId=(string)full!["MaPinHienTai"]!,ReturnSlot=(int)empty!["SoThuTu"]!,ReceiveSlot=(int)full["SoThuTu"]! };
        Save(db,s,true);
        foreach(string resource in new[]{ $"slot:{s.StationId}:{s.ReturnSlot}",$"slot:{s.StationId}:{s.ReceiveSlot}",$"pin:{s.NewBatteryId}",$"pin:{s.OldBatteryId}" })
            db.Exec("INSERT dbo.BssReservations VALUES(@r,@id)",("@r",resource),("@id",s.Id));
        Queue(db,s.StationId,"UNLOCK_SLOT",s.ReturnSlot,s.OldBatteryId,s.Id);
        return s;
    });
    public SwapSession Cancel(string id) => store.Write(db=>{
        var s=db.Payload<SwapSession>("BssSessions",id) ?? throw new DomainException("Không tìm thấy phiên.",404);
        if(!Active(s)) return s;
        if(s.OldInserted || s.Charged) { s.State="NeedsAssistance";s.Note="Khách yêu cầu hỗ trợ sau khi giao pin."; }
        else {s.State="ClosingCancelled"; CancelActuation(db,s.Id); Queue(db,s.StationId,"CLOSE_SLOT",s.ReturnSlot,null,s.Id);}
        Save(db,s);return s;
    });
    static void CancelActuation(Db db,string session)
    {
        foreach(var row in db.Rows("SELECT Payload FROM dbo.BssCommands WHERE JSON_VALUE(Payload,'$.sessionId')=@id AND Status IN ('Pending','Sent','Unconfirmed')",("@id",session)))
        {var c=Parse<DeviceCommand>(row["Payload"]); if(c.Command is "UNLOCK_SLOT" or "EJECT_BATTERY"){c.Status="Cancelled";Save(db,c);}}
    }
    static void Save(Db db, SwapSession s,bool insert=false)
    {
        var args=new (string,object?)[]{("@id",s.Id),("@c",s.CustomerId),("@k",s.Key),("@station",s.StationId),("@a",Active(s)),("@h",Active(s)&&!s.Charged&&!s.State.StartsWith("Closing")?s.Fee:0m),("@p",Serialize(s))};
        db.Exec(insert?"INSERT dbo.BssSessions VALUES(@id,@c,@k,@station,@a,@h,@p)":"UPDATE dbo.BssSessions SET Active=@a,Hold=@h,Payload=@p WHERE Id=@id",args);
        if(!Active(s)) db.Exec("DELETE dbo.BssReservations WHERE SessionId=@id",("@id",s.Id));
    }
    static void Save(Db db,DeviceCommand c) => db.Exec("UPDATE dbo.BssCommands SET Status=@s,Payload=@p WHERE Id=@id",("@s",c.Status),("@p",Serialize(c)),("@id",c.CommandId));
    static DeviceCommand Queue(Db db,string station,string name,int? slot=null,string? battery=null,string? session=null)
    {
        var c=new DeviceCommand {StationId=station,Command=name,SlotNumber=slot,BatteryId=battery,SessionId=session};
        db.Exec("INSERT dbo.BssCommands VALUES(@id,@s,@state,@p)",("@id",c.CommandId),("@s",station),("@state",c.Status),("@p",Serialize(c)));return c;
    }
    public DeviceCommand RequestCommand(string station,CommandRequest request) => store.Write(db=>{
        Require(request.Command!="RESTORE_STATION" || !CoolingLocked(db,station),"Trạm còn ngăn đang khóa làm mát.");
        if(request.Command is "UNLOCK_SLOT" or "REPLACE_BATTERY")Require(!CoolingLocked(db,station,request.SlotNumber),"Ngăn đang khóa làm mát; cần xác nhận an toàn sau tối thiểu 5 phút.");
        Require(request.Command!="RESTORE_STATION" || Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.CanhBaoIoT WHERE MaTram=@s AND DaXuLy=0",("@s",station)))==0,"Xử lý các cảnh báo đang mở trước khi khôi phục trạm.");
        Require(request.Command != "RESTORE_STATION" || Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.BssMaintenance m JOIN dbo.BssMaintenanceLocks l ON l.MaintenanceId=m.Id WHERE m.StationId=@s",("@s",station)))==0,"Trạm còn phiếu bảo trì chưa đối soát.");
        Require(request.Command is not ("RESET_ALARM" or "REPLACE_BATTERY"),"Tạo phiếu bảo trì để xử lý hoặc thay pin.");
        Require(Status(db,station).Online || request.Command=="CUT_OFF","Simulator mất kết nối.");
        Require(request.Command is "CUT_OFF" or "RESTORE_STATION" or "UNLOCK_SLOT" or "CLOSE_SLOT" or "LOCK_SLOT" or "RESET_ALARM" or "TRIGGER_OVERHEAT" or "REPLACE_BATTERY","Lệnh không được hỗ trợ.");
        if(request.Command=="CUT_OFF")
            db.Exec("UPDATE dbo.TramPin SET TrangThaiHoatDong=N'TamDung' WHERE MaTram=@s; UPDATE n SET KhoaBaoTri=1 FROM dbo.NganTu n WHERE MaTram=@s; UPDATE p SET DongDien=0 FROM dbo.Pin p JOIN dbo.NganTu n ON n.MaPinHienTai=p.MaPin WHERE n.MaTram=@s",("@s",station));
        else if(request.Command=="RESTORE_STATION")
            Require(Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.Pin p JOIN dbo.NganTu n ON n.MaPinHienTai=p.MaPin WHERE n.MaTram=@s AND p.NhietDo>=35",("@s",station)))==0,"Trạm chưa đủ mát để khôi phục.");
        else
        {
            var slot=db.Rows("SELECT n.*,p.NhietDo FROM dbo.NganTu n LEFT JOIN dbo.Pin p ON p.MaPin=n.MaPinHienTai WHERE MaTram=@s AND SoThuTu=@n",("@s",station),("@n",request.SlotNumber)).SingleOrDefault();
            Require(slot!=null,"Ngăn không tồn tại.");
            Require(db.Scalar("SELECT MaintenanceId FROM dbo.BssMaintenanceLocks WHERE Resource=@r",("@r",$"slot:{station}:{request.SlotNumber}"))==null,"Ngăn thuộc phiếu bảo trì chưa kết thúc.");
            var reserved=db.Scalar("SELECT SessionId FROM dbo.BssReservations WHERE Resource=@r",("@r",$"slot:{station}:{request.SlotNumber}")) as string;
            Require(reserved==null || request.Command=="RESET_ALARM" && db.Payload<SwapSession>("BssSessions",reserved)?.State=="NeedsAssistance","Ngăn đang được giữ cho giao dịch.");
            if(request.Command=="UNLOCK_SLOT") Require(!(bool)slot!["KhoaBaoTri"]! && Convert.ToDecimal(slot["NhietDo"]??25m)<55,"Ngăn đang bị khóa an toàn.");
            if(request.Command=="REPLACE_BATTERY")
            {
                Require(!string.IsNullOrWhiteSpace(request.BatteryId) && request.BatteryId.Length<=20,"Mã pin mới không hợp lệ.");
                Require(db.Scalar("SELECT MaPin FROM dbo.Pin WHERE MaPin=@p",("@p",request.BatteryId))==null,"Chỉ lắp pin mới chưa tồn tại trong hệ thống.");
            }
        }
        return Queue(db,station,request.Command,request.SlotNumber,request.BatteryId);
    });
    public SwapSession Resume(string id) => store.Write(db=>{
        var s=db.Payload<SwapSession>("BssSessions",id)??throw new DomainException("Không tìm thấy phiên.",404);
        Require(s.State=="NeedsAssistance" && s.OldInserted,"Phiên không ở trạng thái cần hỗ trợ sau nhận pin.");
        Require(Status(db,s.StationId).Online,"Thiết bị mất kết nối.");
        Require(!CoolingLocked(db,s.StationId,s.ReceiveSlot),"Ngăn cấp pin đang khóa làm mát.");
        Require(s.Charged,"Phiên chưa chốt phí: cần đối soát pin trả tại trạm; không tự trừ tiền.");
        var last=db.Rows("SELECT Payload FROM dbo.BssCommands WHERE JSON_VALUE(Payload,'$.sessionId')=@id AND JSON_VALUE(Payload,'$.command')='EJECT_BATTERY'",("@id",id)).Select(r=>Parse<DeviceCommand>(r["Payload"])).OrderByDescending(c=>c.Timestamp).FirstOrDefault();
        Require(last!=null,"Chưa có lệnh cấp pin để đối soát.");
        if(last!.Status=="Completed")s.State="Dispensing";
        else if(last.Status is "Sent" or "Unconfirmed")Queue(db,s.StationId,"QUERY_STATUS",s.ReceiveSlot,last.CommandId,s.Id);
        else if(last.Status=="Failed")
        {
            Require(Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.NganTu n JOIN dbo.Pin p ON p.MaPin=n.MaPinHienTai WHERE n.MaTram=@s AND n.SoThuTu=@n AND p.MaPin=@p AND n.KhoaBaoTri=0 AND p.NhietDo<=45",("@s",s.StationId),("@n",s.ReceiveSlot),("@p",s.NewBatteryId)))==1,"Pin chưa an toàn để cấp lại.");
            Queue(db,s.StationId,"EJECT_BATTERY",s.ReceiveSlot,s.NewBatteryId,s.Id);s.State="Dispensing";
        }
        s.Note="Tiếp tục đối soát lệnh thiết bị, không thu phí thêm.";Save(db,s);return s;
    });
    public void Receive(DeviceMessage m)
    {
        bool accepted=store.Write(db=>{
        Require(m.SchemaVersion==1 && m.MessageId.Length is >0 and <=64 && m.Slots.Length<=64,"Thông điệp không hợp lệ.");
        Require(m.Timestamp>DateTimeOffset.UtcNow.AddSeconds(-15) && m.Timestamp<DateTimeOffset.UtcNow.AddSeconds(5),"Thông điệp đã cũ.");
        var d=db.Rows("SELECT * FROM dbo.BssDevices WHERE StationId=@s",("@s",m.StationId)).SingleOrDefault();
        Require(d!=null && (string)d["LeaseId"]! == m.LeaseId,"Simulator không còn quyền điều khiển.");
        if(db.Scalar("SELECT Id FROM dbo.BssMessages WHERE Id=@id",("@id",m.MessageId))!=null || m.Sequence<=(long)d!["Sequence"]!) return false;
        var expected=db.Rows("SELECT SoThuTu FROM dbo.NganTu WHERE MaTram=@s",("@s",m.StationId)).Select(r=>(int)r["SoThuTu"]!).Order().ToArray();
        Require(expected.SequenceEqual(m.Slots.Select(s=>s.SlotNumber).Order()),"Telemetry phải chứa đúng toàn bộ ngăn của trạm.");
        foreach(var t in m.Slots)
            Require(t.Soc is >=0 and <=100 && t.Soh is >=0 and <=100 && t.Temperature is >=-30 and <=150 && t.Voltage is >=0 and <=120 && t.Current is >=0 and <=100,"Thông số BMS ngoài giới hạn.");
        db.Exec("INSERT dbo.BssMessages(Id) VALUES(@id); UPDATE dbo.BssDevices SET LastSeen=SYSDATETIMEOFFSET(),Sequence=@q WHERE StationId=@s",("@id",m.MessageId),("@q",m.Sequence),("@s",m.StationId));
        foreach(var t in m.Slots)
        {
            ObserveCooldown(db,m.StationId,t);
            // Device measurements never silently reassign inventory ownership.
            if(t.BatteryId!=null)
                db.Exec("UPDATE p SET SoC=@soc,SoH=@soh,NhietDo=@temp,DienAp=@v,DongDien=CASE WHEN n.KhoaBaoTri=1 OR @temp>=55 THEN 0 ELSE @i END,NgayCapNhatCuoi=GETDATE() FROM dbo.Pin p JOIN dbo.NganTu n ON n.MaPinHienTai=p.MaPin WHERE n.MaTram=@s AND n.SoThuTu=@n AND p.MaPin=@b",("@soc",t.Soc),("@soh",t.Soh),("@temp",t.Temperature),("@v",t.Voltage),("@i",t.Current),("@s",m.StationId),("@n",t.SlotNumber),("@b",t.BatteryId));
        }
        return true;
        });
        if(!accepted)return;
        // Persist safety first: a malformed event or early ACK must never undo a hot observation.
        if(m.Event is "OLD_INSERTED" or "NEW_TAKEN" or "OLD_RETURNED" || m.CommandId!=null && m.Event=="ACK")store.Write(db=>{
            if(m.Event is "OLD_INSERTED" or "NEW_TAKEN" or "OLD_RETURNED")HandleSwapEvent(db,m);
            if(m.CommandId!=null && m.Event=="ACK")HandleAck(db,m);
            return true;
        });
    }
    void HandleSwapEvent(Db db,DeviceMessage m)
    {
        var s=m.SessionId==null?null:db.Payload<SwapSession>("BssSessions",m.SessionId);
        Require(s!=null && s.StationId==m.StationId,"Phiên thiết bị không khớp.");
        if(m.Event is "OLD_RETURNED" or "NEW_TAKEN")Require(!CoolingLocked(db,s!.StationId,m.Event=="OLD_RETURNED"?s.ReturnSlot:s.ReceiveSlot),"Ngăn đang khóa làm mát, chưa cho lấy pin.");
        if(m.Event=="OLD_RETURNED")
        {
            if(s!.State=="Cancelled")return;
            Require(s.State is "ReturningOldBattery" or "NeedsAssistance" && !s.Charged,"Phiên không chờ trả lại pin cũ.");
            var t=m.Slots.Single(x=>x.SlotNumber==s.ReturnSlot);
            Require(m.BatteryId==s.OldBatteryId && m.SlotNumber==s.ReturnSlot && t.BatteryId==null && !t.IsDoorOpen && t.Current==0,"Chưa xác nhận lấy pin cũ và đóng cửa.");
            Require(db.Scalar("SELECT Id FROM dbo.BssCommands WHERE JSON_VALUE(Payload,'$.sessionId')=@id AND JSON_VALUE(Payload,'$.command')='RETURN_OLD_BATTERY' AND Status='Completed'",("@id",s.Id))!=null,"Chưa xác nhận lệnh trả pin.");
            db.Exec("UPDATE dbo.NganTu SET MaPinHienTai=NULL,CuaDangMo=0,TrangThaiNgan=N'Trong' WHERE MaTram=@s AND SoThuTu=@n; UPDATE dbo.Pin SET TrangThai=N'DangSuDung',DongDien=0 WHERE MaPin=@p; UPDATE dbo.KhachHang SET MaPinDangMuon=@p WHERE MaKH=@c",("@s",s.StationId),("@n",s.ReturnSlot),("@p",s.OldBatteryId),("@c",s.CustomerId));
            s.State="Cancelled";s.Note="Đã trả lại pin cũ, không thu phí.";Save(db,s);return;
        }
        if(m.Event=="OLD_INSERTED")
        {
            if(s!.OldInserted) return;
            Require(s.State is "AwaitingOldBattery" or "ClosingCancelled" or "ClosingExpired","Phiên không nhận pin ở bước này.");
            bool cancelled=s.State!="AwaitingOldBattery" || s.ExpiresAt<=DateTimeOffset.UtcNow;
            var t=m.Slots.SingleOrDefault(t=>t.SlotNumber==s.ReturnSlot);
            Require(t!=null && t.BatteryId==s.OldBatteryId && !t.IsDoorOpen,"Pin trả hoặc cửa không khớp.");
            // A hot pin just received must remain in custody; an already isolated empty slot cannot accept another pin.
            var beforeCooldown=db.Rows("SELECT BatteryId FROM dbo.BssSlotCooldowns WHERE StationId=@s AND SlotNumber=@n AND ClearedAt IS NULL",("@s",s.StationId),("@n",s.ReturnSlot)).SingleOrDefault();
            Require(beforeCooldown==null || (string?)beforeCooldown["BatteryId"]==s.OldBatteryId,"Ngăn đang khóa làm mát, không nhận pin khác.");
            s.OldInserted=true;
            db.Exec("UPDATE dbo.KhachHang SET MaPinDangMuon=NULL WHERE MaKH=@c AND MaPinDangMuon=@p",("@c",s.CustomerId),("@p",s.OldBatteryId));
            db.Exec("UPDATE dbo.NganTu SET MaPinHienTai=@p,CuaDangMo=0,TrangThaiNgan=N'DangSac' WHERE MaTram=@s AND SoThuTu=@n",("@p",s.OldBatteryId),("@s",s.StationId),("@n",s.ReturnSlot));
            db.Exec("UPDATE dbo.Pin SET SoC=@soc,NhietDo=@t,TrangThai=N'DangSac',DongDien=0 WHERE MaPin=@p",("@soc",t!.Soc),("@t",t.Temperature),("@p",s.OldBatteryId));
            bool ready=Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.NganTu n JOIN dbo.Pin p ON p.MaPin=n.MaPinHienTai WHERE n.MaTram=@s AND n.SoThuTu=@n AND p.MaPin=@p AND n.KhoaBaoTri=0 AND p.NhietDo<=45 AND p.SoC>=90 AND p.SoH>=80",("@s",s.StationId),("@n",s.ReceiveSlot),("@p",s.NewBatteryId)))==1;
            if(cancelled || t.Temperature>=55 || CoolingLocked(db,s.StationId,s.ReturnSlot) || !ready) {s.State="NeedsAssistance";s.Note="Đã nhận pin cũ; cần kỹ thuật viên kiểm tra.";Save(db,s);return;}
            Require(db.Exec("UPDATE dbo.KhachHang SET SoDuVi=SoDuVi-@fee,MaPinDangMuon=NULL WHERE MaKH=@c AND MaPinDangMuon IS NULL AND SoDuVi>=@fee",("@fee",s.Fee),("@c",s.CustomerId),("@old",s.OldBatteryId))==1,"Không thể chốt ví.");
            s.Charged=true;s.State="Dispensing";
            s.TransactionId=Convert.ToInt32(db.Scalar("INSERT dbo.LichSuDoiPin(MaKH,MaTram,MaPinTra,SoC_PinTra,NganTra,MaPinNhan,SoC_PinNhan,NganNhan,PhiDoiPin,TrangThai,ThoiGianBatDau,GhiChu) SELECT @c,@s,@old,@soc,@r,@new,SoC,@n,@fee,N'DangXuLy',@start,@note FROM dbo.Pin WHERE MaPin=@new; SELECT CAST(SCOPE_IDENTITY() AS int)",("@c",s.CustomerId),("@s",s.StationId),("@old",s.OldBatteryId),("@soc",t.Soc),("@r",s.ReturnSlot),("@new",s.NewBatteryId),("@n",s.ReceiveSlot),("@fee",s.Fee),("@start",s.CreatedAt.UtcDateTime),("@note",s.Id)));
            db.Exec("UPDATE dbo.Pin SET TrangThai=N'DangCap',DongDien=0 WHERE MaPin=@p",("@p",s.NewBatteryId));
            Queue(db,s.StationId,"EJECT_BATTERY",s.ReceiveSlot,s.NewBatteryId,s.Id);Save(db,s);
        }
        else
        {
            if(s!.State=="Completed") return;
            Require(s.Charged && s.State is "Dispensing" or "NeedsAssistance","Chưa có giao dịch cấp pin.");
            Require(m.BatteryId==s.NewBatteryId && m.SlotNumber==s.ReceiveSlot,"Pin nhận không khớp.");
            var received=m.Slots.Single(t=>t.SlotNumber==s.ReceiveSlot);
            Require(received.BatteryId==null && !received.IsDoorOpen && received.Current==0,"Chưa xác nhận pin đã được lấy và cửa đã đóng.");
            Require(db.Scalar("SELECT Id FROM dbo.BssCommands WHERE JSON_VALUE(Payload,'$.sessionId')=@id AND JSON_VALUE(Payload,'$.command')='EJECT_BATTERY' AND Status='Completed'",("@id",s.Id))!=null,"Chưa xác nhận lệnh cấp pin.");
            db.Exec("UPDATE dbo.NganTu SET MaPinHienTai=NULL,CuaDangMo=0,TrangThaiNgan=N'Trong' WHERE MaTram=@s AND SoThuTu=@n; UPDATE dbo.Pin SET TrangThai=N'DangSuDung',DongDien=0 WHERE MaPin=@p; UPDATE dbo.KhachHang SET MaPinDangMuon=@p WHERE MaKH=@c; UPDATE dbo.LichSuDoiPin SET TrangThai=N'HoanThanh',ThoiGianKetThuc=GETDATE() WHERE MaGD=@gd",("@s",s.StationId),("@n",s.ReceiveSlot),("@p",s.NewBatteryId),("@c",s.CustomerId),("@gd",s.TransactionId));
            s.State="Completed";Save(db,s);
        }
    }
    void HandleAck(Db db,DeviceMessage m)
    {
        var c=db.Payload<DeviceCommand>("BssCommands",m.CommandId!);
        Require(c!=null && c.StationId==m.StationId,"Lệnh phản hồi không tồn tại.");
        if(c!.Status=="Completed") return;
        Require(c.Status is "Sent" or "Unconfirmed", "Lệnh chưa gửi hoặc đã bị hủy.");
        if(!m.Success) {c.Status="Failed"; c.Note="Thiết bị từ chối thao tác.";Save(db,c);
            db.Exec("UPDATE dbo.BssMaintenance SET Status='Failed',Result=@note WHERE CommandId=@id",("@note",c.Note),("@id",c.CommandId));
            if(c.SessionId!=null){var session=db.Payload<SwapSession>("BssSessions",c.SessionId);if(session!=null && Active(session)){session.State="NeedsAssistance";session.Note=c.Note;Save(db,session);}}return;}
        var t=m.Slots.SingleOrDefault(t=>t.SlotNumber==c.SlotNumber);
        if(c.Command is "UNLOCK_SLOT" or "EJECT_BATTERY" or "RETURN_OLD_BATTERY" or "REPLACE_BATTERY")Require(!CoolingLocked(db,c.StationId,c.SlotNumber),"Ngăn chưa kết thúc cách ly làm mát.");
        if(c.Command=="RESTORE_STATION")Require(!CoolingLocked(db,c.StationId),"Trạm còn ngăn khóa làm mát.");
        if(c.Command=="RESET_ALARM")RequireCooldownElapsed(db,c.StationId,c.SlotNumber!.Value);
        if(c.Command is "RESET_ALARM" or "REPLACE_BATTERY")
        {
            Require(t!=null && t.Temperature<35 && !t.IsDoorOpen && !t.IsLocked,"Chưa có telemetry an toàn để mở khóa.");
            if(c.Command=="RESET_ALARM")
                Require((string?)db.Scalar("SELECT MaPinHienTai FROM dbo.NganTu WHERE MaTram=@s AND SoThuTu=@n",("@s",c.StationId),("@n",c.SlotNumber))==t!.BatteryId,"Pin tại ngăn đã thay đổi.");
            if(c.Command=="REPLACE_BATTERY")
            {
                Require(t!.BatteryId==c.BatteryId,"Pin thay thế không khớp.");
                var old=(string?)db.Scalar("SELECT MaPinHienTai FROM dbo.NganTu WHERE MaTram=@s AND SoThuTu=@n",("@s",c.StationId),("@n",c.SlotNumber));
                Require(old!=null,"Không có pin cũ để tháo.");
                db.Exec("UPDATE dbo.Pin SET TrangThai=N'DaThuHoi',DongDien=0 WHERE MaPin=@old; DELETE dbo.BssBatteryLocations WHERE BatteryId IN(@old,@new); INSERT dbo.BssBatteryLocations(BatteryId,Location) VALUES(@old,'Quarantine'); UPDATE dbo.NganTu SET MaPinHienTai=@new WHERE MaTram=@s AND SoThuTu=@n; UPDATE dbo.Pin SET SoC=@soc,SoH=@soh,DienAp=@v,DongDien=0 WHERE MaPin=@new",("@old",old),("@new",c.BatteryId),("@s",c.StationId),("@n",c.SlotNumber),("@soc",t.Soc),("@soh",t.Soh),("@v",t.Voltage));
            }
            db.Exec("UPDATE p SET NhietDo=@t,TrangThai=CASE WHEN SoC>=90 THEN N'DaDay' ELSE N'DangSac' END FROM dbo.Pin p JOIN dbo.NganTu n ON n.MaPinHienTai=p.MaPin WHERE n.MaTram=@s AND n.SoThuTu=@n; UPDATE dbo.NganTu SET KhoaBaoTri=0,TrangThaiNgan=CASE WHEN MaPinHienTai IS NULL THEN N'Trong' ELSE N'CoPin' END WHERE MaTram=@s AND SoThuTu=@n; UPDATE dbo.CanhBaoIoT SET DaXuLy=1,NguoiXuLy=N'Backend: device ACK',ThoiGianXuLy=GETDATE() WHERE MaTram=@s AND MaNgan=@n AND DaXuLy=0",("@s",c.StationId),("@n",c.SlotNumber),("@t",t!.Temperature));
        }
        else if(c.Command is "CUT_OFF" or "RESTORE_STATION")
        {
            int count=Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.NganTu WHERE MaTram=@s",("@s",c.StationId)));
            Require(m.Slots.Length==count && m.Slots.All(t=>!t.IsDoorOpen && (c.Command=="CUT_OFF" ? t.Current==0 && t.IsLocked : t.Temperature<35)),"Chưa xác nhận an toàn toàn trạm.");
            Require(c.Command!="RESTORE_STATION" || Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.BssMaintenance m JOIN dbo.BssMaintenanceLocks l ON l.MaintenanceId=m.Id WHERE m.StationId=@s",("@s",c.StationId)))==0,"Trạm còn phiếu bảo trì.");
            Require(c.Command!="RESTORE_STATION" || Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.CanhBaoIoT WHERE MaTram=@s AND DaXuLy=0",("@s",c.StationId)))==0,"Trạm còn cảnh báo chưa đóng.");
            bool stop=c.Command=="CUT_OFF";
            db.Exec("UPDATE dbo.TramPin SET TrangThaiHoatDong=@state WHERE MaTram=@s; UPDATE dbo.NganTu SET KhoaBaoTri=@lock,CuaDangMo=0 WHERE MaTram=@s",("@s",c.StationId),("@state",stop?"TamDung":"HoatDong"),("@lock",stop));
        }
        else if(c.Command is "UNLOCK_SLOT" or "CLOSE_SLOT" or "LOCK_SLOT")
        {
            Require(t!=null && (c.Command=="UNLOCK_SLOT" ? t.IsDoorOpen && !t.IsLocked : !t.IsDoorOpen),"Trạng thái cửa chưa xác nhận.");
            db.Exec("UPDATE dbo.NganTu SET CuaDangMo=@o, KhoaBaoTri=CASE WHEN @lock=1 THEN 1 ELSE KhoaBaoTri END WHERE MaTram=@s AND SoThuTu=@n",("@o",t!.IsDoorOpen),("@lock",c.Command=="LOCK_SLOT"),("@s",c.StationId),("@n",c.SlotNumber));
        }
        else if(c.Command=="RETURN_OLD_BATTERY") Require(t!=null && t.BatteryId==c.BatteryId && t.IsDoorOpen && !t.IsLocked && t.Temperature<=45,"Pin cũ chưa an toàn để trả.");
        else if(c.Command=="EJECT_BATTERY") Require(t!=null && t.BatteryId==c.BatteryId && t.IsDoorOpen && !t.IsLocked && t.Temperature<=45 && t.Soc>=90 && t.Soh>=80,"Pin không thể cấp.");
        if(c.Command=="RESET_ALARM")db.Exec("UPDATE dbo.BssSlotCooldowns SET ClearedAt=@now WHERE StationId=@s AND SlotNumber=@n AND ClearedAt IS NULL",("@now",CooldownNow),("@s",c.StationId),("@n",c.SlotNumber));
        c.Status="Completed";Save(db,c);
        CompleteMaintenance(db,c);
        if(c.Command=="CLOSE_SLOT" && c.SessionId!=null)
        {
            var session=db.Payload<SwapSession>("BssSessions",c.SessionId);
            if(session!=null && session.State.StartsWith("Closing"))
            {
                if(t?.BatteryId!=null){
                    session.State="NeedsAssistance";session.Note="Có pin trong ngăn sau khi yêu cầu hủy; cần kiểm tra tại trạm.";
                    if(t.BatteryId==session.OldBatteryId){
                        session.OldInserted=true;
                        db.Exec("UPDATE dbo.NganTu SET MaPinHienTai=@p,CuaDangMo=0,TrangThaiNgan=N'CoPin' WHERE MaTram=@s AND SoThuTu=@n; UPDATE dbo.KhachHang SET MaPinDangMuon=NULL WHERE MaKH=@c AND MaPinDangMuon=@p; UPDATE dbo.Pin SET SoC=@soc,NhietDo=@t,DongDien=0 WHERE MaPin=@p",("@p",session.OldBatteryId),("@s",session.StationId),("@n",session.ReturnSlot),("@c",session.CustomerId),("@soc",t.Soc),("@t",t.Temperature));
                    }else session.Note+=" Pin không đúng: "+t.BatteryId+". Giữ ngăn và phiên để đối soát thủ công.";
                }
                else session.State=session.State=="ClosingExpired"?"Expired":"Cancelled";
                Save(db,session);
            }
        }
    }
    public DeviceCommand? ClaimCommand() => store.Write(db=>{
        var row=db.Rows("SELECT TOP 1 c.Payload FROM dbo.BssCommands c JOIN dbo.BssDevices d ON d.StationId=c.StationId WHERE c.Status='Pending' AND d.Sequence>=0 AND d.LastSeen>DATEADD(second,-5,SYSDATETIMEOFFSET()) ORDER BY JSON_VALUE(c.Payload,'$.timestamp')").SingleOrDefault();
        if(row==null) return null;var c=Parse<DeviceCommand>(row["Payload"]);
        if(!Status(db,c.StationId).Online) return null;
        if(c.SessionId!=null)
        {
            var s=db.Payload<SwapSession>("BssSessions",c.SessionId);
            if(s==null || (!Active(s) && c.Command is "UNLOCK_SLOT" or "EJECT_BATTERY")) { c.Status="Cancelled";Save(db,c);return null; }
        }
        if(c.Command=="RESTORE_STATION" && (CoolingLocked(db,c.StationId) || Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.CanhBaoIoT WHERE MaTram=@s AND DaXuLy=0",("@s",c.StationId)))>0 || Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.BssMaintenance m JOIN dbo.BssMaintenanceLocks l ON l.MaintenanceId=m.Id WHERE m.StationId=@s",("@s",c.StationId)))>0)){c.Status="Failed";c.Note="Trạm chưa hoàn tất xử lý cảnh báo/bảo trì.";Save(db,c);return null;}
        if(c.Command is "UNLOCK_SLOT" or "EJECT_BATTERY" or "RETURN_OLD_BATTERY" or "REPLACE_BATTERY" && CoolingLocked(db,c.StationId,c.SlotNumber)){
            c.Status="Failed";c.Note="Ngăn đang khóa làm mát.";Save(db,c);
            db.Exec("UPDATE dbo.BssMaintenance SET Status='Cancelled',Result=@note WHERE CommandId=@id; DELETE l FROM dbo.BssMaintenanceLocks l JOIN dbo.BssMaintenance m ON m.Id=l.MaintenanceId WHERE m.CommandId=@id",("@note",c.Note),("@id",c.CommandId));
            if(c.SessionId!=null){var interrupted=db.Payload<SwapSession>("BssSessions",c.SessionId);if(interrupted!=null&&Active(interrupted)){interrupted.State="NeedsAssistance";interrupted.Note=c.Note;Save(db,interrupted);}}
            return null;
        }
        c.Status="Sent";c.SentAt=DateTimeOffset.UtcNow;Save(db,c);return c;
    });
    public void Tick() => store.Write(db=>{
        foreach(var row in db.Rows("SELECT Payload FROM dbo.BssCommands WHERE Status='Sent'"))
        {var c=Parse<DeviceCommand>(row["Payload"]);if(c.SentAt<DateTimeOffset.UtcNow.AddSeconds(-10)){c.Status="Unconfirmed";Save(db,c);if(c.Command!="QUERY_STATUS") Queue(db,c.StationId,"QUERY_STATUS",c.SlotNumber,c.CommandId,c.SessionId);}}
        foreach(var row in db.Rows("SELECT Payload FROM dbo.BssSessions WHERE Active=1"))
        {
            var s=Parse<SwapSession>(row["Payload"]);
            if(s.State=="AwaitingOldBattery" && s.ExpiresAt<DateTimeOffset.UtcNow) {s.State="ClosingExpired";CancelActuation(db,s.Id);Queue(db,s.StationId,"CLOSE_SLOT",s.ReturnSlot,null,s.Id);Save(db,s);}
            else if(s.OldInserted && !Status(db,s.StationId).Online && s.State!="NeedsAssistance") {s.State="NeedsAssistance";s.Note="Mất kết nối sau khi nhận pin.";Save(db,s);}
        }
        db.Exec("DELETE dbo.BssMessages WHERE ReceivedAt<DATEADD(day,-1,SYSDATETIMEOFFSET())");return true;
    });
}
