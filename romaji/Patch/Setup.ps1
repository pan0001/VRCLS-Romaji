[CmdletBinding()]param([string]$InstallDir=(Join-Path $env:LOCALAPPDATA 'Programs\VRCLS'),[string]$Destination=(Join-Path $env:LOCALAPPDATA 'VRCLS-Romaji-Patch'),[switch]$ManualOnly,[switch]$Uninstall)
$ErrorActionPreference='Stop'
$Destination=[IO.Path]::GetFullPath($Destination)
$enabled=Join-Path $Destination 'auto.enabled'
$observer=Join-Path $Destination 'VRCLS-Romaji-Repair.exe'
$run='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$name='VRCLS Romaji Patch Repair'
function DisableObserver{
    if(Test-Path -LiteralPath $enabled){Remove-Item -LiteralPath $enabled}
    $value=(Get-ItemProperty -Path $run -Name $name -ErrorAction SilentlyContinue).$name
    if($value -eq ('"'+$observer+'"')){Remove-ItemProperty -Path $run -Name $name}
    foreach($p in @(Get-Process VRCLS-Romaji-Repair -ErrorAction SilentlyContinue)){if($p.Path -eq $observer -and !$p.WaitForExit(60000)){throw 'Repair is still working; retry later.'}}
}
if($Uninstall){
    DisableObserver
    if(Test-Path (Join-Path $Destination 'installed.json')){& (Join-Path $Destination 'Repair.ps1') -Action Restore}
    return
}
$InstallDir=[IO.Path]::GetFullPath($InstallDir)
if(!(Test-Path (Join-Path $InstallDir 'VRCLS.exe')) -or $InstallDir -eq $Destination -or $Destination.StartsWith($InstallDir.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Supply an official installation and a separate patch tools directory.'}
if((Test-Path (Join-Path $Destination 'source.txt')) -and [IO.File]::ReadAllText((Join-Path $Destination 'source.txt')).Trim() -ne $InstallDir){throw 'Uninstall or choose a separate patch tools directory for a different target.'}
foreach($p in @(Get-Process VRCLS -ErrorAction SilentlyContinue)){if(!$p.Path -or $p.Path -eq (Join-Path $InstallDir 'VRCLS.exe')){throw 'Exit VRCLS, including its tray icon, before installing.'}}
DisableObserver
New-Item -ItemType Directory -Force (Join-Path $Destination 'payload')|Out-Null
foreach($n in @('Manage.ps1','Get-Dependencies.ps1','deps-fragment.json','Patcher.exe','ForkSmoke.dll','ForkSmoke.exe','Repair.ps1','VRCLS-Romaji-Repair.exe','payload/VRCLS.Romaji.dll')){
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $n) -Destination (Join-Path $Destination $n) -Force
}
& (Join-Path $Destination 'Get-Dependencies.ps1') -Destination $Destination
[IO.File]::WriteAllText((Join-Path $Destination 'source.txt'),$InstallDir)
& (Join-Path $Destination 'Repair.ps1') -Action Apply
if(!$ManualOnly){
    [IO.File]::WriteAllText($enabled,'1')
    if(!(Test-Path $run)){New-Item -Path $run|Out-Null}
    New-ItemProperty -Path $run -Name $name -Value ('"'+$observer+'"') -PropertyType String -Force|Out-Null
    Start-Process -FilePath $observer -WorkingDirectory $Destination -WindowStyle Hidden
}
