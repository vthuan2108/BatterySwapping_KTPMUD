SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.BssMaintenance') IS NULL
 CREATE TABLE dbo.BssMaintenance(Id varchar(32) PRIMARY KEY,RequestId varchar(80) NOT NULL UNIQUE,StationId varchar(20) NOT NULL,SlotNumber int NOT NULL,Action varchar(30) NOT NULL,OldBatteryId varchar(20) NULL,NewBatteryId varchar(20) NULL,Note nvarchar(1000) NOT NULL,ActorId int NOT NULL,CommandId varchar(32) NOT NULL,Status varchar(30) NOT NULL,Result nvarchar(1000) NULL,CreatedAt datetimeoffset NOT NULL DEFAULT SYSDATETIMEOFFSET());
IF OBJECT_ID('dbo.BssMaintenanceLocks') IS NULL
 CREATE TABLE dbo.BssMaintenanceLocks(Resource varchar(100) PRIMARY KEY,MaintenanceId varchar(32) NOT NULL REFERENCES dbo.BssMaintenance(Id));
COMMIT;
