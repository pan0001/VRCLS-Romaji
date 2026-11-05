# Run with Windows PowerShell 5.1. Uses only throwaway copies of the user's files.
param([Parameter(Mandatory=$true)][string]$Bundle,[Parameter(Mandatory=$true)][string]$Official,[Parameter(Mandatory=$true)][string]$Pristine)
$ErrorActionPreference='Stop'
$root=Join-Path ([IO.Path]::GetTempPath()) ('romaji-patch-test-'+[guid]::NewGuid().ToString('N'))
$src=Join-Path $root 'official';$fork=Join-Path $root 'fork'
New-Item -ItemType Directory -Force $src|Out-Null
function Assert($b,$s){if(!$b){throw $s};Write-Host "PASS: $s"}

try{
    Get-ChildItem -LiteralPath $Official -File | Where-Object {$_.Extension -eq '.dll' -or $_.Name -in @('VRCLS.exe','VRCLS.runtimeconfig.json')} | Copy-Item -Destination $src
    foreach($n in @('VRCLS.dll','VRCLS.Core.dll','VRCLS.Services.dll','VRCLS.deps.json')){Copy-Item (Join-Path $Pristine $n) (Join-Path $src $n) -Force}
    $exe=(Get-FileHash (Join-Path $src 'VRCLS.exe')).Hash
    & (Join-Path $Bundle 'Setup.ps1') -InstallDir $src -Destination $fork -ManualOnly
    $first=(Get-Content (Join-Path $fork 'installed.json') -Raw | ConvertFrom-Json).id
    & (Join-Path $fork 'Repair.ps1')
    Assert (((Get-Content (Join-Path $fork 'installed.json') -Raw|ConvertFrom-Json).id) -eq $first) 'Repeat install is a no-op'
    Assert ((Get-FileHash (Join-Path $src 'VRCLS.exe')).Hash -eq $exe) 'Original exe is unchanged'
    Assert (!(Get-ChildItem $fork -Filter 'VRCLS.exe' -Recurse)) 'No independent VRCLS installation retained'
    foreach($n in @('VRCLS.dll','VRCLS.Services.dll','VRCLS.deps.json')){Copy-Item (Join-Path $Pristine $n) (Join-Path $src $n) -Force}
    Add-Type -Path (Join-Path $fork 'Mono.Cecil.dll')
    $inputPath=Join-Path $src 'VRCLS.Services.dll'
    $a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($inputPath)
    $t=$a.MainModule.GetType('VRCLS.Services.VR.SteamVROverlayService')
    $m=New-Object Mono.Cecil.MethodDefinition('ForkTest_UnrelatedUpstreamChange',[Mono.Cecil.MethodAttributes]'Private, Static',$a.MainModule.TypeSystem.Void)
    $m.Body.GetILProcessor().Emit([Mono.Cecil.Cil.OpCodes]::Ret);$t.Methods.Add($m)
    $a.Write($inputPath+'.new');$a.Dispose();Move-Item ($inputPath+'.new') $inputPath -Force
    $updated=(Get-FileHash $inputPath).Hash
    $enabled=Join-Path $fork 'auto.enabled';[IO.File]::WriteAllText($enabled,'1')
    $hostProc=Start-Process -FilePath (Join-Path $fork 'VRCLS-Romaji-Repair.exe') -PassThru -WindowStyle Hidden
    try{
        $deadline=(Get-Date).AddSeconds(120)
        do{Start-Sleep -Seconds 1;$new=Get-Content (Join-Path $fork 'installed.json') -Raw|ConvertFrom-Json}while($new.id -eq $first -and (Get-Date) -lt $deadline)
        Assert ($new.id -ne $first -and (Get-FileHash $inputPath).Hash -eq $new.after.'VRCLS.Services.dll') 'Background observer reapplies a compatible official update'
    }finally{Remove-Item -LiteralPath $enabled;$hostProc.WaitForExit(30000)|Out-Null}
    $a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($inputPath)
    Assert ($a.MainModule.GetType('VRCLS.Services.VR.SteamVROverlayService').Methods.Name -contains 'ForkTest_UnrelatedUpstreamChange') 'Updated code retained in official install'
    $a.Dispose()
    & (Join-Path $fork 'Repair.ps1') -Action Restore
    Assert ((Get-FileHash $inputPath).Hash -eq $updated) 'Uninstall restores updated original, not earlier version'
    $a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($inputPath)
    $m=$a.MainModule.GetType('VRCLS.Services.VR.SteamVROverlayService').Methods | Where-Object Name -eq 'UpdatePanel'
    $il=$m.Body.GetILProcessor();$il.InsertBefore($m.Body.Instructions[0],$il.Create([Mono.Cecil.Cil.OpCodes]::Nop))
    $a.Write($inputPath+'.new');$a.Dispose();Move-Item ($inputPath+'.new') $inputPath -Force
    $bad=(Get-FileHash $inputPath).Hash;$blocked=$false
    try{& (Join-Path $fork 'Repair.ps1')}catch{$blocked=$true}
    Assert ($blocked -and (Get-FileHash $inputPath).Hash -eq $bad) 'Unsupported update left untouched'
}finally{
    $p=[IO.Path]::GetFullPath($root);$tmp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if($p.StartsWith($tmp,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($p) -match '^romaji-patch-test-[a-f0-9]{32}$'){Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction SilentlyContinue}
}
