using BatterySwapping.Contracts;
namespace BatterySwapping.Core;
public sealed partial class BssEngine
{
    DateTimeOffset CooldownNow => (clock??TimeProvider.System).GetUtcNow();
    static SlotCooldown CooldownRow(Dictionary<string,object?> r)=>new((string)r["StationId"]!,(int)r["SlotNumber"]!,r["BatteryId"] as string,(DateTimeOffset)r["StartedAt"]!,(DateTimeOffset)r["UnlockAfter"]!,r["ClearedAt"] as DateTimeOffset?);
    public List<SlotCooldown> Cooldowns(string station)=>store.Read(db=>db.Rows("SELECT * FROM dbo.BssSlotCooldowns WHERE StationId=@s",("@s",station)).Select(CooldownRow).ToList());
    static bool CoolingLocked(Db db,string station,int? slot=null)=>Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM dbo.BssSlotCooldowns WHERE StationId=@s AND (@n IS NULL OR SlotNumber=@n) AND ClearedAt IS NULL",("@s",station),("@n",slot)))>0;
    void RequireCooldownElapsed(Db db,string station,int slot){
        var r=db.Rows("SELECT * FROM dbo.BssSlotCooldowns WHERE StationId=@s AND SlotNumber=@n AND ClearedAt IS NULL",("@s",station),("@n",slot)).SingleOrDefault();
        if(r!=null){var c=CooldownRow(r);Require(CooldownNow>=c.UnlockAfter,$"Ngăn {slot} đang làm mát tự động, khóa tối thiểu đến {c.UnlockAfter:O}. Không thể nhận/rút pin hoặc mở cửa trong 5 phút.");}
    }
    void ObserveCooldown(Db db,string station,SlotTelemetry t){
        if((t.Temperature>=55 || t.IsOverheated) && t.BatteryId!=null){
            var now=CooldownNow;
            db.Exec("IF NOT EXISTS(SELECT 1 FROM dbo.BssSlotCooldowns WHERE StationId=@s AND SlotNumber=@n) INSERT dbo.BssSlotCooldowns(StationId,SlotNumber,BatteryId,StartedAt,UnlockAfter) VALUES(@s,@n,@b,@now,@until); ELSE UPDATE dbo.BssSlotCooldowns SET BatteryId=@b,StartedAt=@now,UnlockAfter=@until,ClearedAt=NULL WHERE StationId=@s AND SlotNumber=@n AND ClearedAt IS NOT NULL",("@s",station),("@n",t.SlotNumber),("@b",t.BatteryId),("@now",now),("@until",now.AddMinutes(5)));
        }
        if(CoolingLocked(db,station,t.SlotNumber)){
            db.Exec("UPDATE dbo.NganTu SET KhoaBaoTri=1 WHERE MaTram=@s AND SoThuTu=@n; UPDATE p SET DongDien=0 FROM dbo.Pin p JOIN dbo.NganTu n ON n.MaPinHienTai=p.MaPin WHERE n.MaTram=@s AND n.SoThuTu=@n",("@s",station),("@n",t.SlotNumber));
        }
    }
}
