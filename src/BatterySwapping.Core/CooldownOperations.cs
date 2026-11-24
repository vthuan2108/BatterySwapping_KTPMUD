using BatterySwapping.Contracts;
namespace BatterySwapping.Core;
public sealed partial class BssEngine
{
    DateTimeOffset CooldownNow => (clock??TimeProvider.System).GetUtcNow();
    static SlotCooldown CooldownRow(Dictionary<string,object?> r)=>new((string)r["StationId"]!,(int)r["SlotNumber"]!,r["BatteryId"] as string,(DateTimeOffset)r["StartedAt"]!,(DateTimeOffset)r["UnlockAfter"]!,r["ClearedAt"] as DateTimeOffset?,Convert.ToInt32(r["IncidentCount"]),Convert.ToBoolean(r["RequiresAdmin"]));
    public List<SlotCooldown> Cooldowns(string station)=>store.Read(db=>db.Rows("SELECT * FROM dbo.BssSlotCooldowns WHERE StationId=@s",("@s",station)).Select(CooldownRow).ToList());
    static bool CoolingLocked(Db db,string station,int? slot=null)=>Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.BssSlotCooldowns WHERE StationId=@s AND (@n IS NULL OR SlotNumber=@n) AND ClearedAt IS NULL",("@s",station),("@n",slot)))>0;
    void RequireCooldownElapsed(Db db,string station,int slot){
        var r=db.Rows("SELECT * FROM dbo.BssSlotCooldowns WHERE StationId=@s AND SlotNumber=@n AND ClearedAt IS NULL",("@s",station),("@n",slot)).SingleOrDefault();
        if(r!=null){var c=CooldownRow(r);Require(CooldownNow>=c.UnlockAfter,$"Ngăn {slot} đang làm mát tự động, khóa tối thiểu đến {c.UnlockAfter:O}. Không thể nhận/rút pin hoặc mở cửa trong 5 phút.");}
    }
    void ObserveCooldown(Db db,string station,SlotTelemetry t){
        if((t.Temperature>=55 || t.IsOverheated) && t.BatteryId!=null){
            var now=CooldownNow;
            db.Exec("IF NOT EXISTS(SELECT 1 FROM dbo.BssSlotCooldowns WHERE StationId=@s AND SlotNumber=@n) INSERT dbo.BssSlotCooldowns(StationId,SlotNumber,BatteryId,StartedAt,UnlockAfter,IncidentCount,RequiresAdmin) VALUES(@s,@n,@b,@now,@until,1,0); ELSE UPDATE dbo.BssSlotCooldowns SET IncidentCount=CASE WHEN BatteryId=@b THEN IncidentCount+1 ELSE 1 END,RequiresAdmin=CASE WHEN BatteryId=@b AND IncidentCount+1>=4 THEN 1 ELSE 0 END,BatteryId=@b,StartedAt=@now,UnlockAfter=@until,ClearedAt=NULL WHERE StationId=@s AND SlotNumber=@n AND ClearedAt IS NOT NULL",("@s",station),("@n",t.SlotNumber),("@b",t.BatteryId),("@now",now),("@until",now.AddMinutes(5)));
        }
        if(CoolingLocked(db,station,t.SlotNumber)){
            db.Exec("UPDATE dbo.NganTu SET KhoaBaoTri=1 WHERE MaTram=@s AND SoThuTu=@n; UPDATE p SET DongDien=0 FROM dbo.Pin p JOIN dbo.NganTu n ON n.MaPinHienTai=p.MaPin WHERE n.MaTram=@s AND n.SoThuTu=@n",("@s",station),("@n",t.SlotNumber));
        }
    }
    void QueueAutomaticThermalRecovery(Db db){
        var rows=db.Rows("SELECT c.* FROM dbo.BssSlotCooldowns c JOIN dbo.NganTu n ON n.MaTram=c.StationId AND n.SoThuTu=c.SlotNumber JOIN dbo.Pin p ON p.MaPin=n.MaPinHienTai WHERE c.ClearedAt IS NULL AND c.RequiresAdmin=0 AND c.IncidentCount<4 AND c.BatteryId=p.MaPin AND p.NhietDo<35 AND n.CuaDangMo=0");
        foreach(var row in rows){
            var cooldown=CooldownRow(row);
            if(CooldownNow<cooldown.UnlockAfter || !Status(db,cooldown.StationId).Online)continue;
            if(db.Scalar("SELECT MaintenanceId FROM dbo.BssMaintenanceLocks WHERE Resource=@r",("@r",$"slot:{cooldown.StationId}:{cooldown.SlotNumber}"))!=null)continue;
            var last=db.Rows("SELECT Payload FROM dbo.BssCommands WHERE StationId=@s AND JSON_VALUE(Payload,'$.command')='AUTO_RESET_ALARM' AND JSON_VALUE(Payload,'$.slotNumber')=@n",("@s",cooldown.StationId),("@n",cooldown.SlotNumber.ToString())).Select(r=>Parse<DeviceCommand>(r["Payload"])).Where(c=>c.ThermalStartedAt==cooldown.StartedAt).OrderByDescending(c=>c.Timestamp).FirstOrDefault();
            if(last!=null && (last.Status!="Failed" || CooldownNow<last.Timestamp.AddSeconds(5)))continue;
            var command=Queue(db,cooldown.StationId,"AUTO_RESET_ALARM",cooldown.SlotNumber,cooldown.BatteryId);
            command.Timestamp=CooldownNow;command.ThermalStartedAt=cooldown.StartedAt;
            command.PreserveStationLock=(string?)db.Scalar("SELECT TrangThaiHoatDong FROM dbo.TramPin WHERE MaTram=@s",("@s",cooldown.StationId))!="HoatDong";
            Save(db,command);
        }
    }
    void ValidateThermalRecovery(Db db,DeviceCommand command){
        var row=db.Rows("SELECT * FROM dbo.BssSlotCooldowns WHERE StationId=@s AND SlotNumber=@n AND ClearedAt IS NULL",("@s",command.StationId),("@n",command.SlotNumber)).SingleOrDefault();
        if(row==null){Require(command.Command!="AUTO_RESET_ALARM","Không có đợt quá nhiệt để tự phục hồi.");return;}
        var cooldown=CooldownRow(row);RequireCooldownElapsed(db,command.StationId,command.SlotNumber!.Value);
        Require(command.ThermalStartedAt==cooldown.StartedAt,"Lệnh phục hồi không thuộc đợt quá nhiệt hiện tại.");
        Require(!cooldown.RequiresAdmin || command.Command=="RESET_ALARM"&&command.AdminThermalReset,"Ngăn đã quá nhiệt lần thứ4; cần admin xác nhận.");
    }

}
