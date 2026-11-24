param([switch]$NoBrowser,[switch]$NoDesktop)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$runtime=Join-Path $projectRoot '.runtime'
if(!(Test-Path (Join-Path $runtime 'server.json'))){throw 'Chay scripts/Prepare-Local.ps1 truoc.'}
if(Test-Path (Join-Path $runtime 'processes.json')){throw 'Da co phien duoc quan ly. Chay Stop-Local.ps1 truoc.'}
$launched=@()
$brokerExe=(Get-Item (Join-Path $projectRoot '.tools/mosquitto/mosquitto.exe')).FullName
try {
 $p=Start-Process -FilePath $brokerExe -ArgumentList @('-c',('"'+(Join-Path $runtime 'mosquitto.conf')+'"')) -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtime 'broker.out.log') -RedirectStandardError (Join-Path $runtime 'broker.err.log')
 $brokerProcess=$p
 $launched+=@{Id=$p.Id;Path=$brokerExe;Start=$p.StartTime.ToUniversalTime().ToString('O')}
 $env:BSS_SETTINGS=Join-Path $runtime 'server.json'
 $server=(Get-Item (Join-Path $projectRoot 'BatterySwapping.Server/bin/Release/net8.0/BatterySwapping.Server.exe')).FullName
 $p=Start-Process -FilePath $server -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtime 'server.out.log') -RedirectStandardError (Join-Path $runtime 'server.err.log')
 $launched+=@{Id=$p.Id;Path=$server;Start=$p.StartTime.ToUniversalTime().ToString('O')}
 $serverProcess=$p
 $ready=$false
 for($i=0;$i -lt 30;$i++){if($serverProcess.HasExited -or $brokerProcess.HasExited){throw 'Tien trinh server/broker da thoat. Xem log.'};try{$health=Invoke-RestMethod http://127.0.0.1:5080/health -TimeoutSec 1;if($health.mqtt){$ready=$true;break}}catch{};Start-Sleep -Milliseconds 500}
 if(!$ready){throw 'Server/broker chua san sang. Xem .runtime/*.log.'}
 if(!$NoDesktop){$p=Start-Process -FilePath (Join-Path $projectRoot 'BatterySwapping.App/bin/Release/net8.0-windows/BatterySwapping.App.exe') -PassThru;$launched+=@{Id=$p.Id;Path=$p.Path;Start=$p.StartTime.ToUniversalTime().ToString('O')}}
 $launched | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runtime 'processes.json')
 if(!$NoBrowser){Start-Process 'http://127.0.0.1:5080' -WindowStyle Hidden}
 Write-Output 'BSS san sang: http://127.0.0.1:5080 • API: /api-docs'
} catch {
 foreach($entry in $launched){$p=Get-Process -Id $entry.Id -ErrorAction SilentlyContinue;if($p -and $p.Path -eq $entry.Path){Stop-Process -Id $p.Id}}
 throw
}
