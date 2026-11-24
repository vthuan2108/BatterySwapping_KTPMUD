using BatterySwapping.Contracts;
namespace BatterySwapping.Core;
public sealed partial class BssEngine
{
    public List<MaintenanceOrder> MaintenanceOrders()=>store.Read(db=>db.Rows("SELECT * FROM dbo.BssMaintenance ORDER BY CreatedAt DESC").Select(MaintenanceRow).ToList());
    static MaintenanceOrder MaintenanceRow(Dictionary<string,object?> r)=>new((string)r["Id"]!,(string)r["CommandId"]!,(string)r["Status"]!,(string)r["StationId"]!,(int)r["SlotNumber"]!,(string)r["Action"]!,r["OldBatteryId"] as string,r["NewBatteryId"] as string,(string)r["Note"]!,(int)r["ActorId"]!,(DateTimeOffset)r["CreatedAt"]!,r["Result"] as string);
    public MaintenanceOrder CreateMaintenance(MaintenanceOrderRequest r,int actor)=>store.Write(db=>{
        Require(r.RequestId.Length is >0 and <=80 && r.Note.Length is >0 and <=1000,"Nhập mã yêu cầu và ghi chú bảo trì.");
        var previous=db.Rows("SELECT * FROM dbo.BssMaintenance WHERE RequestId=@id",("@id",r.RequestId)).SingleOrDefault();
        if(previous!=null){var p=MaintenanceRow(previous);Require(p.ActorId==actor&&p.StationId==r.StationId&&p.SlotNumber==r.SlotNumber&&p.Action==r.Action&&p.NewBatteryId==r.NewBatteryId&&p.Note==r.Note,"Mã yêu cầu bảo trì đã được sử dụng.");return p;}
        Require(r.Action is "RESET_ALARM" or "REPLACE_BATTERY","Thao tác bảo trì không hợp lệ.");
        Require(Status(db,r.StationId).Online,"Thiết bị mất kết nối.");
        Require(db.Scalar("SELECT TOP 1 Id FROM dbo.BssCommands WHERE StationId=@s AND JSON_VALUE(Payload,'$.command')='RESTORE_STATION' AND Status IN('Pending','Sent','Unconfirmed')",("@s",r.StationId))==null,"Lệnh khôi phục trạm chưa kết thúc; cần đối soát trước khi bảo trì.");
        var slot=db.Rows("SELECT n.*,p.LoaiPin FROM dbo.NganTu n LEFT JOIN dbo.Pin p ON p.MaPin=n.MaPinHienTai WHERE MaTram=@s AND SoThuTu=@n",("@s",r.StationId),("@n",r.SlotNumber)).SingleOrDefault();
        Require(slot!=null && slot["MaPinHienTai"] is string,"Ngăn chưa có pin để bảo trì.");
        string resource=$"slot:{r.StationId}:{r.SlotNumber}";
        var reserved=db.Scalar("SELECT SessionId FROM dbo.BssReservations WHERE Resource=@r",("@r",resource)) as string;
        Require(reserved==null || r.Action=="RESET_ALARM" && db.Payload<SwapSession>("BssSessions",reserved)?.State=="NeedsAssistance","Ngăn đang thuộc phiên đổi pin.");
        Require(db.Scalar("SELECT MaintenanceId FROM dbo.BssMaintenanceLocks WHERE Resource=@r",("@r",resource))==null,"Ngăn đang có phiếu bảo trì chưa kết thúc.");
        if(r.Action=="REPLACE_BATTERY"){
            Require(db.Scalar("SELECT TOP 1 MaKH FROM dbo.KhachHang WHERE MaPinDangMuon=@p",("@p",slot!["MaPinHienTai"]))==null && db.Scalar("SELECT SessionId FROM dbo.BssReservations WHERE Resource=@r",("@r","pin:"+slot["MaPinHienTai"]))==null,"Pin cũ đang thuộc khách hoặc phiên; cần đối soát.");
            Require(r.NewBatteryId!=null,"Chọn pin trong kho.");BusinessOperations.EnsureWarehouse(db,r.NewBatteryId!);
            Require((string?)db.Scalar("SELECT LoaiPin FROM dbo.Pin WHERE MaPin=@p",("@p",r.NewBatteryId))==(string?)slot!["LoaiPin"],"Loại pin thay thế không tương thích.");
            Require(Convert.ToDecimal(db.Scalar("SELECT NhietDo FROM dbo.Pin WHERE MaPin=@p",("@p",r.NewBatteryId)))<35,"Chờ pin trong kho nguội dưới 35°C trước khi thay.");
        }
        if(r.Action=="RESET_ALARM")RequireCooldownElapsed(db,r.StationId,r.SlotNumber);else Require(!CoolingLocked(db,r.StationId,r.SlotNumber),"Ngăn còn cách ly làm mát; xác nhận reset an toàn trước khi thay pin.");
        var c=Queue(db,r.StationId,r.Action,r.SlotNumber,r.NewBatteryId);
        string id=Guid.NewGuid().ToString("N");
        db.Exec("INSERT dbo.BssMaintenance(Id,RequestId,StationId,SlotNumber,Action,OldBatteryId,NewBatteryId,Note,ActorId,CommandId,Status) VALUES(@id,@key,@s,@n,@a,@old,@new,@note,@actor,@cmd,'Pending'); UPDATE dbo.NganTu SET KhoaBaoTri=1 WHERE MaTram=@s AND SoThuTu=@n",("@id",id),("@key",r.RequestId),("@s",r.StationId),("@n",r.SlotNumber),("@a",r.Action),("@old",slot!["MaPinHienTai"]),("@new",r.NewBatteryId),("@note",r.Note),("@actor",actor),("@cmd",c.CommandId));
        db.Exec("INSERT dbo.BssMaintenanceLocks VALUES(@r,@id)",("@r",resource),("@id",id));
        if(r.Action=="REPLACE_BATTERY")db.Exec("INSERT dbo.BssMaintenanceLocks VALUES(@r,@id)",("@r","pin:"+r.NewBatteryId),("@id",id));
        return MaintenanceRow(db.Rows("SELECT * FROM dbo.BssMaintenance WHERE Id=@id",("@id",id)).Single());
    });
    public MaintenanceOrder RetryMaintenance(string id)=>store.Write(db=>{
        var row=db.Rows("SELECT * FROM dbo.BssMaintenance WHERE Id=@id",("@id",id)).SingleOrDefault()??throw new DomainException("Không tìm thấy phiếu.",404);
        var order=MaintenanceRow(row);var old=db.Payload<DeviceCommand>("BssCommands",order.CommandId)!;
        Require(order.Status=="Failed" && old.Status=="Failed","Chỉ gửi lại sau phản hồi thất bại rõ ràng; lệnh chưa xác nhận phải đối soát.");
        Require(Status(db,order.StationId).Online,"Thiết bị mất kết nối.");
        Require((string?)db.Scalar("SELECT MaPinHienTai FROM dbo.NganTu WHERE MaTram=@s AND SoThuTu=@n",("@s",order.StationId),("@n",order.SlotNumber))==order.OldBatteryId,"Pin tại ngăn đã thay đổi, cần đối soát.");
        if(order.Action=="RESET_ALARM")RequireCooldownElapsed(db,order.StationId,order.SlotNumber);else Require(!CoolingLocked(db,order.StationId,order.SlotNumber),"Ngăn đang khóa làm mát.");
        var c=Queue(db,order.StationId,order.Action,order.SlotNumber,order.NewBatteryId);
        db.Exec("UPDATE dbo.BssMaintenance SET CommandId=@cmd,Status='Pending',Result=NULL WHERE Id=@id",("@cmd",c.CommandId),("@id",id));
        return MaintenanceRow(db.Rows("SELECT * FROM dbo.BssMaintenance WHERE Id=@id",("@id",id)).Single());
    });
    static void CompleteMaintenance(Db db,DeviceCommand c){
        if(c.Command is not("RESET_ALARM" or "REPLACE_BATTERY"))return;
        db.Exec("UPDATE dbo.BssMaintenance SET Status='Completed',Result=N'Thiết bị xác nhận cùng telemetry an toàn.' WHERE CommandId=@c; DELETE l FROM dbo.BssMaintenanceLocks l JOIN dbo.BssMaintenance m ON m.Id=l.MaintenanceId WHERE m.CommandId=@c",("@c",c.CommandId));
    }
    public SwapSession ReturnOldBattery(string id)=>store.Write(db=>{
        var s=db.Payload<SwapSession>("BssSessions",id)??throw new DomainException("Không tìm thấy phiên.",404);
        Require(!CoolingLocked(db,s.StationId,s.ReturnSlot),"Ngăn pin trả đang khóa làm mát.");
        if(s.State=="ReturningOldBattery")return s;
        Require(s.State=="NeedsAssistance"&&s.OldInserted&&!s.Charged,"Chỉ trả pin cũ cho phiên đã nhận pin nhưng chưa thu phí.");
        Require(Status(db,s.StationId).Online,"Thiết bị mất kết nối.");
        Require(db.Scalar("SELECT MaintenanceId FROM dbo.BssMaintenanceLocks WHERE Resource=@r",("@r",$"slot:{s.StationId}:{s.ReturnSlot}"))==null,"Cần hoàn tất bảo trì ngăn trước khi trả pin.");
        Require(Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.NganTu n JOIN dbo.Pin p ON p.MaPin=n.MaPinHienTai WHERE n.MaTram=@s AND n.SoThuTu=@n AND p.MaPin=@p AND n.KhoaBaoTri=0 AND p.NhietDo<=45",("@s",s.StationId),("@n",s.ReturnSlot),("@p",s.OldBatteryId)))==1,"Pin cũ chưa an toàn để trả.");
        var existing=db.Rows("SELECT Payload FROM dbo.BssCommands WHERE JSON_VALUE(Payload,'$.sessionId')=@id AND JSON_VALUE(Payload,'$.command')='RETURN_OLD_BATTERY'",("@id",s.Id)).Select(r=>Parse<DeviceCommand>(r["Payload"])).OrderByDescending(c=>c.Timestamp).FirstOrDefault();
        if(existing!=null && existing.Status is "Sent" or "Unconfirmed" or "Pending" or "Completed"){
            if(existing.Status is "Sent" or "Unconfirmed")Queue(db,s.StationId,"QUERY_STATUS",s.ReturnSlot,existing.CommandId,s.Id);
            s.State="ReturningOldBattery";Save(db,s);return s;
        }
        Queue(db,s.StationId,"RETURN_OLD_BATTERY",s.ReturnSlot,s.OldBatteryId,s.Id);s.State="ReturningOldBattery";s.Note="Chờ thiết bị mở ngăn và khách lấy lại pin cũ; chưa thu phí.";Save(db,s);return s;
    });
}
