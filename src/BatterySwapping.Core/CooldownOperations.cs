using BatterySwapping.Contracts;
namespace BatterySwapping.Core;
public sealed partial class BssEngine
{
    DateTimeOffset CooldownNow => (clock??TimeProvider.System).GetUtcNow();
    static SlotCooldown CooldownRow(Dictionary<string,object?> r)=>new((string)r["StationId"]!,(int)r["SlotNumber"]!,r["BatteryId"] as string,(DateTimeOffset)r["StartedAt"]!,(DateTimeOffset)r["UnlockAfter"]!,r["ClearedAt"] as DateTimeOffset?,Convert.ToInt32(r["IncidentCount"]),Convert.ToBoolean(r["RequiresAdmin"]));
    static BatteryThermalState ThermalStateRow(Dictionary<string,object?> r)=>new((string)r["BatteryId"]!,Convert.ToInt32(r["IncidentCount"]),Convert.ToInt32(r["TotalIncidents"]),Convert.ToBoolean(r["RequiresAdmin"]),r["LastAdminResetAt"] as DateTimeOffset?);
    public List<SlotCooldown> Cooldowns(string station)=>store.Read(db=>db.Rows("SELECT * FROM dbo.BssSlotCooldowns WHERE StationId=@s",("@s",station)).Select(CooldownRow).ToList());
    public List<BatteryThermalState> ThermalStates()=>store.Read(db=>db.Scalar("SELECT OBJECT_ID('dbo.BssBatteryThermalState')")==null?new List<BatteryThermalState>():db.Rows("SELECT BatteryId,IncidentCount,TotalIncidents,RequiresAdmin,LastAdminResetAt FROM dbo.BssBatteryThermalState ORDER BY BatteryId").Select(ThermalStateRow).ToList());
    static bool CoolingLocked(Db db,string station,int? slot=null)=>Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.BssSlotCooldowns WHERE StationId=@s AND (@n IS NULL OR SlotNumber=@n) AND ClearedAt IS NULL",("@s",station),("@n",slot)))>0;
    void RequireCooldownElapsed(Db db,string station,int slot){
        var r=db.Rows("SELECT * FROM dbo.BssSlotCooldowns WHERE StationId=@s AND SlotNumber=@n AND ClearedAt IS NULL",("@s",station),("@n",slot)).SingleOrDefault();
        if(r!=null){var c=CooldownRow(r);Require(CooldownNow>=c.UnlockAfter,$"Ngăn {slot} đang làm mát tự động, khóa tối thiểu đến {c.UnlockAfter:O}. Không thể nhận/rút pin hoặc mở cửa trong 5 phút.");}
    }
    void ObserveCooldown(Db db,string station,SlotTelemetry t){
        if((t.Temperature>=55 || t.IsOverheated) && t.BatteryId!=null){
            bool activeAtSlot=Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.BssSlotCooldowns WHERE StationId=@s AND SlotNumber=@n AND ClearedAt IS NULL",("@s",station),("@n",t.SlotNumber)))>0;
            if(!activeAtSlot){
                var now=CooldownNow;
                int prevCount=0,prevTotal=0;
                bool hasBatteryHistory=db.Scalar("SELECT OBJECT_ID('dbo.BssBatteryThermalState')")!=null;
                if(hasBatteryHistory){
                    var stateRow=db.Rows("SELECT IncidentCount,TotalIncidents FROM dbo.BssBatteryThermalState WHERE BatteryId=@b",("@b",t.BatteryId)).SingleOrDefault();
                    if(stateRow!=null){
                        prevCount=Convert.ToInt32(stateRow["IncidentCount"]);
                        prevTotal=Convert.ToInt32(stateRow["TotalIncidents"]);
                    }
                    int nextCount=prevCount+1;
                    int nextTotal=prevTotal+1;
                    bool requiresAdmin=nextCount>=3;
                    if(stateRow==null)
                        db.Exec("INSERT dbo.BssBatteryThermalState(BatteryId,IncidentCount,TotalIncidents,RequiresAdmin,LastStationId,LastSlotNumber,LastStartedAt,LastClearedAt) VALUES(@b,@c,@t,@req,@s,@n,@now,NULL)",("@b",t.BatteryId),("@c",nextCount),("@t",nextTotal),("@req",requiresAdmin),("@s",station),("@n",t.SlotNumber),("@now",now));
                    else
                        db.Exec("UPDATE dbo.BssBatteryThermalState SET IncidentCount=@c,TotalIncidents=@t,RequiresAdmin=@req,LastStationId=@s,LastSlotNumber=@n,LastStartedAt=@now,LastClearedAt=NULL WHERE BatteryId=@b",("@b",t.BatteryId),("@c",nextCount),("@t",nextTotal),("@req",requiresAdmin),("@s",station),("@n",t.SlotNumber),("@now",now));
                    db.Exec("IF NOT EXISTS(SELECT 1 FROM dbo.BssSlotCooldowns WHERE StationId=@s AND SlotNumber=@n) INSERT dbo.BssSlotCooldowns(StationId,SlotNumber,BatteryId,StartedAt,UnlockAfter,IncidentCount,RequiresAdmin) VALUES(@s,@n,@b,@now,@until,@c,@req); ELSE UPDATE dbo.BssSlotCooldowns SET IncidentCount=@c,RequiresAdmin=@req,BatteryId=@b,StartedAt=@now,UnlockAfter=@until,ClearedAt=NULL WHERE StationId=@s AND SlotNumber=@n AND ClearedAt IS NOT NULL",("@s",station),("@n",t.SlotNumber),("@b",t.BatteryId),("@now",now),("@until",now.AddMinutes(5)),("@c",nextCount),("@req",requiresAdmin));
                    if(db.Scalar("SELECT OBJECT_ID('dbo.BssBatteryThermalEvents')")!=null)
                        db.Exec("INSERT dbo.BssBatteryThermalEvents(BatteryId,StationId,SlotNumber,EventType,StreakCount,TotalCount,RequiresAdmin,OccurredAt) VALUES(@b,@s,@n,'Overheat',@c,@t,@req,@now)",("@b",t.BatteryId),("@s",station),("@n",t.SlotNumber),("@c",nextCount),("@t",nextTotal),("@req",requiresAdmin),("@now",now));
                }else{
                    db.Exec("IF NOT EXISTS(SELECT 1 FROM dbo.BssSlotCooldowns WHERE StationId=@s AND SlotNumber=@n) INSERT dbo.BssSlotCooldowns(StationId,SlotNumber,BatteryId,StartedAt,UnlockAfter,IncidentCount,RequiresAdmin) VALUES(@s,@n,@b,@now,@until,1,0); ELSE UPDATE dbo.BssSlotCooldowns SET IncidentCount=CASE WHEN BatteryId=@b THEN IncidentCount+1 ELSE 1 END,RequiresAdmin=CASE WHEN BatteryId=@b AND IncidentCount+1>=3 THEN 1 ELSE 0 END,BatteryId=@b,StartedAt=@now,UnlockAfter=@until,ClearedAt=NULL WHERE StationId=@s AND SlotNumber=@n AND ClearedAt IS NOT NULL",("@s",station),("@n",t.SlotNumber),("@b",t.BatteryId),("@now",now),("@until",now.AddMinutes(5)));
                }
            }
        }
        if(CoolingLocked(db,station,t.SlotNumber)){
            db.Exec("UPDATE dbo.NganTu SET KhoaBaoTri=1 WHERE MaTram=@s AND SoThuTu=@n; UPDATE p SET DongDien=0 FROM dbo.Pin p JOIN dbo.NganTu n ON n.MaPinHienTai=p.MaPin WHERE n.MaTram=@s AND n.SoThuTu=@n",("@s",station),("@n",t.SlotNumber));
        }
    }
    void ClearThermalCooldown(Db db,DeviceCommand c){
        var activeRow=db.Rows("SELECT * FROM dbo.BssSlotCooldowns WHERE StationId=@s AND SlotNumber=@n AND ClearedAt IS NULL",("@s",c.StationId),("@n",c.SlotNumber)).SingleOrDefault();
        if(activeRow==null)return;
        var cooldown=CooldownRow(activeRow);
        var now=CooldownNow;
        bool resetStreak=cooldown.RequiresAdmin && c.AdminThermalReset;
        int updatedCount=resetStreak?0:cooldown.IncidentCount;
        bool updatedReq=!c.AdminThermalReset && cooldown.RequiresAdmin;
        db.Exec("UPDATE dbo.BssSlotCooldowns SET IncidentCount=@c,RequiresAdmin=@req,ClearedAt=@now WHERE StationId=@s AND SlotNumber=@n AND ClearedAt IS NULL",("@c",updatedCount),("@req",updatedReq),("@now",now),("@s",c.StationId),("@n",c.SlotNumber));
        if(cooldown.BatteryId!=null && db.Scalar("SELECT OBJECT_ID('dbo.BssBatteryThermalState')")!=null){
            var stateRow=db.Rows("SELECT TotalIncidents FROM dbo.BssBatteryThermalState WHERE BatteryId=@b",("@b",cooldown.BatteryId)).SingleOrDefault();
            int total=stateRow!=null?Convert.ToInt32(stateRow["TotalIncidents"]):cooldown.IncidentCount;
            if(resetStreak)
                db.Exec("UPDATE dbo.BssBatteryThermalState SET IncidentCount=0,RequiresAdmin=0,LastClearedAt=@now,LastAdminResetAt=@now WHERE BatteryId=@b",("@now",now),("@b",cooldown.BatteryId));
            else
                db.Exec("UPDATE dbo.BssBatteryThermalState SET RequiresAdmin=@req,LastClearedAt=@now WHERE BatteryId=@b",("@req",updatedReq),("@now",now),("@b",cooldown.BatteryId));
            if(db.Scalar("SELECT OBJECT_ID('dbo.BssBatteryThermalEvents')")!=null)
                db.Exec("INSERT dbo.BssBatteryThermalEvents(BatteryId,StationId,SlotNumber,EventType,StreakCount,TotalCount,RequiresAdmin,ActorCommandId,OccurredAt) VALUES(@b,@s,@n,@ev,@c,@t,@req,@cmd,@now)",("@b",cooldown.BatteryId),("@s",c.StationId),("@n",c.SlotNumber),("@ev",resetStreak?"AdminReset":(c.Command=="AUTO_RESET_ALARM"?"AutoReset":"ManualReset")),("@c",updatedCount),("@t",total),("@req",updatedReq),("@cmd",c.CommandId),("@now",now));
        }
    }
    void QueueAutomaticThermalRecovery(Db db){
        var rows=db.Rows("SELECT c.* FROM dbo.BssSlotCooldowns c JOIN dbo.NganTu n ON n.MaTram=c.StationId AND n.SoThuTu=c.SlotNumber JOIN dbo.Pin p ON p.MaPin=n.MaPinHienTai WHERE c.ClearedAt IS NULL AND c.RequiresAdmin=0 AND c.IncidentCount<3 AND c.BatteryId=p.MaPin AND p.NhietDo<35 AND n.CuaDangMo=0");
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
        Require(!cooldown.RequiresAdmin || command.Command=="RESET_ALARM"&&command.AdminThermalReset,"Ngăn đã quá nhiệt lần thứ 3; cần admin xác nhận.");
    }

}
