[CmdletBinding()]param([string]$InstallDir=(Join-Path $env:LOCALAPPDATA 'Programs\VRCLS'),[string]$Destination=(Join-Path $env:LOCALAPPDATA 'Programs\VRCLS-Romaji'),[switch]$NoShortcut)
$ErrorActionPreference='Stop'
$Destination=[IO.Path]::GetFullPath($Destination);$InstallDir=[IO.Path]::GetFullPath($InstallDir)
if(!(Test-Path -LiteralPath (Join-Path $InstallDir 'VRCLS.exe'))){throw 'Select the official VRCLS installation with -InstallDir.'}
if($Destination -eq $InstallDir -or $Destination.StartsWith($InstallDir.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Install the fork outside the official program directory.'}
New-Item -ItemType Directory -Force (Join-Path $Destination 'payload')|Out-Null
foreach($n in @('Manage.ps1','Get-Dependencies.ps1','deps-fragment.json','Patcher.exe','VRCLS-Romaji.exe','ForkSmoke.dll','ForkSmoke.exe','payload/VRCLS.Romaji.dll')){
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $n) -Destination (Join-Path $Destination $n) -Force
}
& (Join-Path $Destination 'Get-Dependencies.ps1') -Destination $Destination
[IO.File]::WriteAllText((Join-Path $Destination 'source.txt'),$InstallDir)
& (Join-Path $Destination 'Manage.ps1') -Action Prepare -InstallDir $InstallDir
if(!$NoShortcut){
    $shell=New-Object -ComObject WScript.Shell
    $link=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'VRCLS Romaji.lnk'))
    $link.TargetPath=Join-Path $Destination 'VRCLS-Romaji.exe';$link.WorkingDirectory=$Destination;$link.Save()
}
Write-Host 'Ready. Open VRCLS Romaji from Start. The official installation was not changed.'
