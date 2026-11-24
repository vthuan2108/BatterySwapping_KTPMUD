SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.BssSlotCooldowns') IS NULL
 CREATE TABLE dbo.BssSlotCooldowns(StationId varchar(20) NOT NULL,SlotNumber int NOT NULL,BatteryId varchar(20) NULL,StartedAt datetimeoffset NOT NULL,UnlockAfter datetimeoffset NOT NULL,ClearedAt datetimeoffset NULL,PRIMARY KEY(StationId,SlotNumber),CONSTRAINT CK_BssCooldownMinimum CHECK(UnlockAfter>=DATEADD(minute,5,StartedAt)));
-- Existing hot/latched-overheat pins start a conservative new five-minute lock on upgrade.
INSERT dbo.BssSlotCooldowns(StationId,SlotNumber,BatteryId,StartedAt,UnlockAfter)
 SELECT n.MaTram,n.SoThuTu,p.MaPin,SYSDATETIMEOFFSET(),DATEADD(minute,5,SYSDATETIMEOFFSET())
 FROM dbo.NganTu n JOIN dbo.Pin p ON p.MaPin=n.MaPinHienTai
 WHERE (p.NhietDo>=55 OR p.TrangThai=N'LoiQuaNhiet') AND NOT EXISTS(SELECT 1 FROM dbo.BssSlotCooldowns c WHERE c.StationId=n.MaTram AND c.SlotNumber=n.SoThuTu);
UPDATE n SET KhoaBaoTri=1 FROM dbo.NganTu n JOIN dbo.BssSlotCooldowns c ON c.StationId=n.MaTram AND c.SlotNumber=n.SoThuTu WHERE c.ClearedAt IS NULL;
COMMIT;
