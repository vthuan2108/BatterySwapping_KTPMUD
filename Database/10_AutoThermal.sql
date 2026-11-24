SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.BssSlotCooldowns','IncidentCount') IS NULL
 ALTER TABLE dbo.BssSlotCooldowns ADD IncidentCount int NOT NULL CONSTRAINT DF_BssIncidentCount DEFAULT 1;
IF COL_LENGTH('dbo.BssSlotCooldowns','RequiresAdmin') IS NULL
 ALTER TABLE dbo.BssSlotCooldowns ADD RequiresAdmin bit NOT NULL CONSTRAINT DF_BssRequiresAdmin DEFAULT 0;
-- Preserve a valid in-flight reset created before this additive upgrade.
UPDATE c SET Payload=JSON_MODIFY(c.Payload,'$.thermalStartedAt',CONVERT(varchar(40),t.StartedAt,127))
 FROM dbo.BssCommands c JOIN dbo.BssMaintenance m ON m.CommandId=c.Id
 JOIN dbo.BssSlotCooldowns t ON t.StationId=m.StationId AND t.SlotNumber=m.SlotNumber
 WHERE c.Status IN('Pending','Sent','Unconfirmed') AND m.Action='RESET_ALARM'
 AND t.ClearedAt IS NULL AND m.CreatedAt>=t.StartedAt AND JSON_VALUE(c.Payload,'$.thermalStartedAt') IS NULL;
COMMIT;
