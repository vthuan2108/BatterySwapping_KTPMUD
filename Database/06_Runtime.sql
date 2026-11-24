-- Additive migration: run against the configured database. Never seeds/drops existing data.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.BssSessions') IS NULL
BEGIN
 CREATE TABLE dbo.BssSessions(Id varchar(32) PRIMARY KEY, CustomerId int NOT NULL,
  RequestKey varchar(80) NOT NULL, StationId varchar(20) NOT NULL, Active bit NOT NULL,
  Hold decimal(18,2) NOT NULL, Payload nvarchar(max) NOT NULL,
  CONSTRAINT UQ_BssSessionRequest UNIQUE(CustomerId, RequestKey));
 CREATE UNIQUE INDEX UQ_BssCustomerActive ON dbo.BssSessions(CustomerId) WHERE Active=1;
END;
IF OBJECT_ID('dbo.BssReservations') IS NULL
 CREATE TABLE dbo.BssReservations(Resource varchar(100) PRIMARY KEY, SessionId varchar(32) NOT NULL
 REFERENCES dbo.BssSessions(Id));
IF OBJECT_ID('dbo.BssCommands') IS NULL
 CREATE TABLE dbo.BssCommands(Id varchar(32) PRIMARY KEY, StationId varchar(20) NOT NULL,
  Status varchar(30) NOT NULL, Payload nvarchar(max) NOT NULL);
IF OBJECT_ID('dbo.BssDevices') IS NULL
 CREATE TABLE dbo.BssDevices(StationId varchar(20) PRIMARY KEY, ClientId varchar(64) NOT NULL,
  LeaseId varchar(32) NOT NULL, LastSeen datetimeoffset NOT NULL, Sequence bigint NOT NULL DEFAULT -1);
IF OBJECT_ID('dbo.BssMessages') IS NULL
 CREATE TABLE dbo.BssMessages(Id varchar(64) PRIMARY KEY, ReceivedAt datetimeoffset NOT NULL DEFAULT SYSDATETIMEOFFSET());
IF OBJECT_ID('dbo.BssTopups') IS NULL
 CREATE TABLE dbo.BssTopups(RequestId varchar(36) PRIMARY KEY,CustomerId int NOT NULL,Amount decimal(18,2) NOT NULL CHECK(Amount>0));
COMMIT;
