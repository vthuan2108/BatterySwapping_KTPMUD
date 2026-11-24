$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$pidFile=Join-Path $projectRoot '.runtime/processes.json'
if(!(Test-Path $pidFile)){Write-Output 'Khong co phien do launcher quan ly.';return}
foreach($entry in @(Get-Content -LiteralPath $pidFile -Raw | ConvertFrom-Json)){
 $p=Get-Process -Id $entry.Id -ErrorAction SilentlyContinue
 if($p -and $p.Path -eq $entry.Path -and [Math]::Abs(($p.StartTime.ToUniversalTime() - ([datetime]$entry.Start).ToUniversalTime()).TotalSeconds) -lt 1){Stop-Process -Id $p.Id;$p.WaitForExit(5000) | Out-Null}
}
Remove-Item -LiteralPath $pidFile
Write-Output 'Da dung cac tien trinh do launcher nay khoi tao.'
