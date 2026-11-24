$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$runtime=Join-Path $projectRoot '.runtime'
New-Item -ItemType Directory -Force $runtime | Out-Null
if(Test-Path (Join-Path $runtime 'processes.json')){throw 'Dung phien launcher bang Stop-Local.ps1 truoc khi kiem thu.'}
$configFile=Join-Path $runtime 'server.json'
$previous=if(Test-Path $configFile){Get-Content -LiteralPath $configFile -Raw}else{$null}
try {
 $fixture=@"
USE [BssArchitectureTests_20261121];
GO
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID('dbo.BssSlotCooldowns') IS NOT NULL DROP TABLE dbo.BssSlotCooldowns;
IF OBJECT_ID('dbo.BssMaintenanceLocks') IS NOT NULL DROP TABLE dbo.BssMaintenanceLocks;
IF OBJECT_ID('dbo.BssMaintenance') IS NOT NULL DROP TABLE dbo.BssMaintenance;
IF OBJECT_ID('dbo.BssPayments') IS NOT NULL DROP TABLE dbo.BssPayments;
IF OBJECT_ID('dbo.BssHandovers') IS NOT NULL DROP TABLE dbo.BssHandovers;
IF OBJECT_ID('dbo.BssBatteryLocations') IS NOT NULL DROP TABLE dbo.BssBatteryLocations;
IF OBJECT_ID('dbo.BssBusinessRequests') IS NOT NULL DROP TABLE dbo.BssBusinessRequests;
IF OBJECT_ID('dbo.BssPolicy') IS NOT NULL DROP TABLE dbo.BssPolicy;
IF OBJECT_ID('dbo.BssReservations') IS NOT NULL DELETE dbo.BssReservations;
IF OBJECT_ID('dbo.BssCommands') IS NOT NULL DELETE dbo.BssCommands;
IF OBJECT_ID('dbo.BssMessages') IS NOT NULL DELETE dbo.BssMessages;
IF OBJECT_ID('dbo.BssDevices') IS NOT NULL DELETE dbo.BssDevices;
IF OBJECT_ID('dbo.BssSessions') IS NOT NULL DELETE dbo.BssSessions;
GO
"@
 # The cleanup batch runs only if the dedicated test database already exists.
 sqlcmd -S .\SQLEXPRESS -E -C -b -I -Q "IF DB_ID('BssArchitectureTests_20261121') IS NULL CREATE DATABASE BssArchitectureTests_20261121"
 if($LASTEXITCODE -ne 0){throw 'Khong tao duoc database kiem thu.'}
 $fixture+="`n"+((Get-ChildItem (Join-Path $projectRoot 'Database/0[1-5]_*.sql') | Sort-Object Name | ForEach-Object {Get-Content -LiteralPath $_.FullName -Raw}) -join "`nGO`n").Replace('BatterySwappingDB','BssArchitectureTests_20261121')
 $fixtureFile=Join-Path $runtime 'integration-fixture.sql'
 $fixture | Set-Content -LiteralPath $fixtureFile -Encoding utf8
 sqlcmd -S .\SQLEXPRESS -E -C -b -I -f 65001 -i $fixtureFile
 if($LASTEXITCODE -ne 0){throw 'Nap fixture that bai.'}
 & (Join-Path $PSScriptRoot 'Prepare-Local.ps1') -Database BssArchitectureTests_20261121
 & (Join-Path $PSScriptRoot 'Start-Local.ps1') -NoBrowser -NoDesktop
 dotnet run --project (Join-Path $projectRoot 'tests/BatterySwapping.Tests') --no-build -c Release -- --integration
 if($LASTEXITCODE -ne 0){throw 'Kiem thu API/MQTT that bai.'}
 dotnet run --project (Join-Path $projectRoot 'tests/BatterySwapping.ArchitectureTests') --no-build -c Release -- --business --cooldown
 if($LASTEXITCODE -ne 0){throw 'Kiem thu mat khau that bai.'}
 node (Join-Path $projectRoot 'tests/BatterySwapping.Web3D.Tests/bms.test.js')
 if($LASTEXITCODE -ne 0){throw 'Kiem thu BMS that bai.'}
 node (Join-Path $projectRoot 'tests/BatterySwapping.Web3D.Tests/controller.test.js')
 if($LASTEXITCODE -ne 0){throw 'Kiem thu dieu khien thiet bi that bai.'}
} finally {
 & (Join-Path $PSScriptRoot 'Stop-Local.ps1')
 if($null -ne $previous){$previous | Set-Content -LiteralPath $configFile -Encoding utf8}
}
