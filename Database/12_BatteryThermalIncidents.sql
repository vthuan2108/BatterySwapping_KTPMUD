SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.BssBatteryThermalState') IS NULL
 CREATE TABLE dbo.BssBatteryThermalState(
  BatteryId varchar(20) PRIMARY KEY,
  IncidentCount int NOT NULL DEFAULT 0,
  TotalIncidents int NOT NULL DEFAULT 0,
  RequiresAdmin bit NOT NULL DEFAULT 0,
  LastStationId varchar(20) NULL,
  LastSlotNumber int NULL,
  LastStartedAt datetimeoffset NULL,
  LastClearedAt datetimeoffset NULL,
  LastAdminResetAt datetimeoffset NULL
 );
IF OBJECT_ID('dbo.BssBatteryThermalEvents') IS NULL
 CREATE TABLE dbo.BssBatteryThermalEvents(
  Id bigint IDENTITY(1,1) PRIMARY KEY,
  BatteryId varchar(20) NOT NULL,
  StationId varchar(20) NOT NULL,
  SlotNumber int NOT NULL,
  EventType varchar(30) NOT NULL,
  StreakCount int NOT NULL,
  TotalCount int NOT NULL,
  RequiresAdmin bit NOT NULL,
  ActorCommandId varchar(32) NULL,
  OccurredAt datetimeoffset NOT NULL
 );
INSERT dbo.BssBatteryThermalState(BatteryId,IncidentCount,TotalIncidents,RequiresAdmin,LastStationId,LastSlotNumber,LastStartedAt,LastClearedAt)
 SELECT c.BatteryId,c.IncidentCount,c.IncidentCount,c.RequiresAdmin,c.StationId,c.SlotNumber,c.StartedAt,c.ClearedAt
 FROM (
  SELECT BatteryId,IncidentCount,RequiresAdmin,StationId,SlotNumber,StartedAt,ClearedAt,
   ROW_NUMBER() OVER (PARTITION BY BatteryId ORDER BY CASE WHEN ClearedAt IS NULL THEN 0 ELSE 1 END, StartedAt DESC) AS rn
  FROM dbo.BssSlotCooldowns
  WHERE BatteryId IS NOT NULL
 ) c
 WHERE c.rn=1 AND NOT EXISTS(SELECT 1 FROM dbo.BssBatteryThermalState s WHERE s.BatteryId=c.BatteryId);
COMMIT;
