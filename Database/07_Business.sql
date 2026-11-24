SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.BssBusinessRequests') IS NULL
 CREATE TABLE dbo.BssBusinessRequests(Id varchar(36) PRIMARY KEY,Kind varchar(30) NOT NULL,Payload nvarchar(max) NOT NULL,Result nvarchar(max) NOT NULL);
IF OBJECT_ID('dbo.BssPayments') IS NULL
 CREATE TABLE dbo.BssPayments(Id varchar(36) PRIMARY KEY,CustomerId int NOT NULL REFERENCES dbo.KhachHang(MaKH),Amount decimal(18,2) NOT NULL CHECK(Amount>0),Status varchar(20) NOT NULL CHECK(Status IN('Pending','Succeeded','Failed','Cancelled')),CreatedAt datetimeoffset NOT NULL DEFAULT SYSDATETIMEOFFSET(),ResolvedAt datetimeoffset NULL,ResolvedBy int NULL);
IF OBJECT_ID('dbo.BssPolicy') IS NULL
 CREATE TABLE dbo.BssPolicy(Id int PRIMARY KEY CHECK(Id=1),Fee decimal(18,2) NOT NULL CHECK(Fee>=0),SupportedBatteryType nvarchar(50) NOT NULL);
IF NOT EXISTS(SELECT 1 FROM dbo.BssPolicy WHERE Id=1)
 INSERT dbo.BssPolicy VALUES(1,25000,N'Lithium LFP 72V-30Ah');
IF OBJECT_ID('dbo.BssBatteryLocations') IS NULL
 CREATE TABLE dbo.BssBatteryLocations(BatteryId varchar(20) PRIMARY KEY REFERENCES dbo.Pin(MaPin),Location varchar(20) NOT NULL CHECK(Location IN('Warehouse','Quarantine')));
IF OBJECT_ID('dbo.BssHandovers') IS NULL
 CREATE TABLE dbo.BssHandovers(Id varchar(36) PRIMARY KEY,CustomerId int NOT NULL REFERENCES dbo.KhachHang(MaKH),BatteryId varchar(20) NOT NULL REFERENCES dbo.Pin(MaPin),ActorId int NOT NULL,Note nvarchar(500) NOT NULL,CreatedAt datetimeoffset NOT NULL DEFAULT SYSDATETIMEOFFSET());
COMMIT;

