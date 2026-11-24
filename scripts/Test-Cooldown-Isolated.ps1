# Dedicated test database and ports; does not stop or reconfigure the running demo.
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location $projectRoot
$runDirectory=Join-Path $projectRoot '.runtime/cooldown-tests'
New-Item -ItemType Directory -Force $runDirectory | Out-Null
$configFile=Join-Path $runDirectory 'server.json'
$pidFile=Join-Path $runDirectory 'processes.json'
$config=Get-Content (Join-Path $projectRoot '.runtime/server.json') -Raw | ConvertFrom-Json
$config.ConnectionStrings.Bss='Server=.\SQLEXPRESS;Database=BssArchitectureTests_20261121;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5;'
$config | Add-Member -NotePropertyName Urls -NotePropertyValue 'http://127.0.0.1:5082' -Force
$config.Broker | Add-Member -NotePropertyName Port -NotePropertyValue 18884 -Force
$config.Broker | Add-Member -NotePropertyName ClientId -NotePropertyValue 'bss-cooldown-tests' -Force
$config.Broker | Add-Member -NotePropertyName WebSocketUrl -NotePropertyValue 'ws://127.0.0.1:19002/mqtt' -Force
$config | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $configFile -Encoding utf8
$brokerExecutable=(Resolve-Path '.tools/mosquitto/mosquitto.exe').Path
$passwordTool=(Resolve-Path '.tools/mosquitto/mosquitto_passwd.exe').Path
$passwordFile=Join-Path $runDirectory 'passwords'
& $passwordTool -c -b $passwordFile backend $config.Broker.Password | Out-Null
& $passwordTool -b $passwordFile simulator $config.Broker.SimulatorPassword | Out-Null
$aclFile=(Resolve-Path '.runtime/acl').Path.Replace('\','/')
@"
allow_anonymous false
password_file $($passwordFile.Replace('\','/'))
acl_file $aclFile
listener 18884 127.0.0.1
protocol mqtt
listener 19002 127.0.0.1
protocol websockets
"@ | Set-Content -LiteralPath (Join-Path $runDirectory 'mosquitto.conf') -Encoding ascii
# Reuse the explicit test-only fixture cleanup from the standard integration runner.
$runner=Get-Content (Join-Path $PSScriptRoot 'Test-Integration.ps1') -Raw
$fixture=[regex]::Match($runner,'(?s)\$fixture=@"\r?\n(.*?)\r?\n"@').Groups[1].Value
if(!$fixture.StartsWith('USE [BssArchitectureTests_20261121]')){throw 'Dedicated test fixture guard failed.'}
$fixture+="`n"+((Get-ChildItem 'Database/0[1-5]_*.sql' | Sort-Object Name | ForEach-Object {Get-Content -LiteralPath $_.FullName -Raw}) -join "`nGO`n").Replace('BatterySwappingDB','BssArchitectureTests_20261121')
$fixtureFile=Join-Path $runDirectory 'fixture.sql'
$fixture | Set-Content -LiteralPath $fixtureFile -Encoding utf8
sqlcmd -S .\SQLEXPRESS -E -C -b -Q "IF DB_ID('BssArchitectureTests_20261121') IS NULL CREATE DATABASE BssArchitectureTests_20261121"
sqlcmd -S .\SQLEXPRESS -E -C -b -I -f 65001 -i $fixtureFile
if($LASTEXITCODE -ne 0){throw 'Dedicated fixture failed.'}
foreach($project in @('src/BatterySwapping.Server','tests/BatterySwapping.Tests','tests/BatterySwapping.ArchitectureTests','tests/BatterySwapping.UiTests','src/BatterySwapping.App.Avalonia')){dotnet build $project -c CooldownCheck -v quiet;if($LASTEXITCODE -ne 0){throw 'Build failed.'}}
if($LASTEXITCODE -ne 0){throw 'Build failed.'}
$env:BSS_SETTINGS=$configFile
$env:BSS_TEST_SETTINGS=$configFile
$env:BSS_API_URL='http://127.0.0.1:5082'
$env:BSS_TEST_PIDS=$pidFile
$env:BSS_TEST_CONFIGURATION='CooldownCheck'
$entries=@()
try {
 $broker=Start-Process -FilePath $brokerExecutable -ArgumentList @('-c',('"'+(Join-Path $runDirectory 'mosquitto.conf')+'"')) -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $runDirectory 'broker.log')
 $entries+=@{Id=$broker.Id;Path=$brokerExecutable;Start=$broker.StartTime.ToUniversalTime().ToString('O')}
 $serverExecutable=(Resolve-Path 'src/BatterySwapping.Server/bin/CooldownCheck/net8.0/BatterySwapping.Server.exe').Path
 $server=Start-Process -FilePath $serverExecutable -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runDirectory 'server.log') -RedirectStandardError (Join-Path $runDirectory 'server.err.log')
 $entries+=@{Id=$server.Id;Path=$serverExecutable;Start=$server.StartTime.ToUniversalTime().ToString('O')}
 $entries | ConvertTo-Json | Set-Content -LiteralPath $pidFile
 $ready=$false
 for($i=0;$i -lt 40;$i++){try{if((Invoke-RestMethod 'http://127.0.0.1:5082/health' -TimeoutSec 1).mqtt){$ready=$true;break}}catch{};Start-Sleep -Milliseconds 250}
 if(!$ready){throw 'Isolated server/broker not ready; see cooldown-tests logs.'}
 dotnet run --project tests/BatterySwapping.Tests -c CooldownCheck --no-build -- --integration
 if($LASTEXITCODE -ne 0){throw 'Integration failed.'}
 # Direct engine tests own the outbox; stop the isolated worker before they claim commands.
 $currentServer=@(Get-Content $pidFile -Raw | ConvertFrom-Json) | Where-Object {$_.Path -eq $serverExecutable} | Select-Object -First 1
 if($currentServer){$running=Get-Process -Id $currentServer.Id -ErrorAction SilentlyContinue;if($running -and $running.Path -eq $serverExecutable -and [Math]::Abs(($running.StartTime.ToUniversalTime()-([datetime]$currentServer.Start).ToUniversalTime()).TotalSeconds)-lt 1){Stop-Process -Id $running.Id;$running.WaitForExit(5000) | Out-Null}}

 dotnet run --project tests/BatterySwapping.ArchitectureTests -c CooldownCheck --no-build -- --business --cooldown --simulation --simulation
 if($LASTEXITCODE -ne 0){throw 'Business/cooldown regression failed.'}
 dotnet run --project tests/BatterySwapping.UiTests -c CooldownCheck --no-build
 if($LASTEXITCODE -ne 0){throw 'WPF regression failed.'}
 node tests/BatterySwapping.Web3D.Tests/bms.test.js
 if($LASTEXITCODE -ne 0){throw 'BMS regression failed.'}
 node tests/BatterySwapping.Web3D.Tests/controller.test.js
 if($LASTEXITCODE -ne 0){throw 'Device regression failed.'}
 node tests/BatterySwapping.Web3D.Tests/mqtt.test.js
 if($LASTEXITCODE -ne 0){throw 'MQTT client regression failed.'}
} finally {
 if(Test-Path $pidFile){$entries=@(Get-Content $pidFile -Raw | ConvertFrom-Json)}
 foreach($entry in $entries){$p=Get-Process -Id $entry.Id -ErrorAction SilentlyContinue;if($p -and $p.Path -eq $entry.Path -and [Math]::Abs(($p.StartTime.ToUniversalTime()-([datetime]$entry.Start).ToUniversalTime()).TotalSeconds)-lt 1){Stop-Process -Id $p.Id}}
 Remove-Item Env:BSS_SETTINGS,Env:BSS_TEST_SETTINGS,Env:BSS_API_URL,Env:BSS_TEST_PIDS,Env:BSS_TEST_CONFIGURATION -ErrorAction SilentlyContinue
}

