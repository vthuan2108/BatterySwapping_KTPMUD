param([string]$Database='BatterySwappingDB',[string]$SqlServer='.\SQLEXPRESS')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$runtime=Join-Path $projectRoot '.runtime'
New-Item -ItemType Directory -Force $runtime | Out-Null
$broker=Join-Path $projectRoot '.tools/mosquitto'
if (!(Test-Path (Join-Path $broker 'mosquitto.exe'))) { throw 'Cai Mosquitto Windows vao .tools/mosquitto truoc. Xem docs/LOCAL-DEMO.md.' }
function New-Secret { [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24)) }
$configFile=Join-Path $runtime 'server.json'
if(Test-Path $configFile){$config=Get-Content $configFile -Raw | ConvertFrom-Json; $backendSecret=$config.Broker.Password; $simulatorSecret=$config.Broker.SimulatorPassword}
else {$backendSecret=New-Secret;$simulatorSecret=New-Secret}
$passwordFile=Join-Path $runtime 'passwords'
& (Join-Path $broker 'mosquitto_passwd.exe') -c -b $passwordFile backend $backendSecret
if($LASTEXITCODE -ne 0){throw 'Khong tao duoc tai khoan broker.'}
& (Join-Path $broker 'mosquitto_passwd.exe') -b $passwordFile simulator $simulatorSecret
@'
user backend
topic read bss/station/+/telemetry
topic read bss/station/+/alerts
topic read bss/station/+/events
topic read bss/station/+/acks
topic write bss/station/+/commands
user simulator
topic read bss/station/+/commands
topic write bss/station/+/telemetry
topic write bss/station/+/alerts
topic write bss/station/+/events
topic write bss/station/+/acks
'@ | Set-Content -LiteralPath (Join-Path $runtime 'acl') -Encoding ascii
$passwordConfig=$passwordFile.Replace('\','/')
$aclConfig=(Join-Path $runtime 'acl').Replace('\','/')
@"
per_listener_settings false
allow_anonymous false
password_file $passwordConfig
acl_file $aclConfig
listener 18883 127.0.0.1
protocol mqtt
listener 19001 127.0.0.1
protocol websockets
log_type error
log_type warning
"@ | Set-Content -LiteralPath (Join-Path $runtime 'mosquitto.conf') -Encoding ascii
@{ConnectionStrings=@{Bss="Server=$SqlServer;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5;"};Broker=@{Username='backend';Password=$backendSecret;SimulatorPassword=$simulatorSecret}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $configFile -Encoding utf8
dotnet restore (Join-Path $projectRoot 'BatterySwappingSystem.sln') -p:NuGetAudit=false
if($LASTEXITCODE -ne 0){throw 'Restore that bai.'}
dotnet build (Join-Path $projectRoot 'BatterySwappingSystem.sln') --no-restore -c Release -p:NuGetAudit=false
if($LASTEXITCODE -ne 0){throw 'Build that bai.'}
Write-Output 'Da chuan bi dependency, cau hinh broker va ban Release. Runtime khong can Internet.'
