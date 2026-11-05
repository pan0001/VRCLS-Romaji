# Run with Windows PowerShell 5.1. Uses only throwaway copies of the user's files.
param([Parameter(Mandatory=$true)][string]$Bundle,[Parameter(Mandatory=$true)][string]$Official,[Parameter(Mandatory=$true)][string]$Pristine)
$ErrorActionPreference='Stop'
$root=Join-Path ([IO.Path]::GetTempPath()) ('romaji-update-test-'+[guid]::NewGuid().ToString('N'))
$src=Join-Path $root 'official';$fork=Join-Path $root 'fork'
New-Item -ItemType Directory -Force $src|Out-Null
function Assert($b,$s){if(!$b){throw $s};Write-Host "PASS: $s"}
function CurrentName{[IO.File]::ReadAllText((Join-Path $fork 'current.txt')).Trim()}
try{
    Get-ChildItem -LiteralPath $Official -File | Where-Object {$_.Extension -eq '.dll' -or $_.Name -in @('VRCLS.exe','VRCLS.runtimeconfig.json')} | Copy-Item -Destination $src
    foreach($n in @('VRCLS.dll','VRCLS.Core.dll','VRCLS.Services.dll','VRCLS.deps.json')){Copy-Item (Join-Path $Pristine $n) (Join-Path $src $n) -Force}
    & (Join-Path $Bundle 'Install.ps1') -InstallDir $src -Destination $fork -NoShortcut
    $one=CurrentName
    & (Join-Path $fork 'Manage.ps1') -Action Prepare
    Assert ((CurrentName) -eq $one) 'No rebuild on unchanged official version'
    Add-Type -Path (Join-Path $fork 'Mono.Cecil.dll')
    $inputPath=Join-Path $src 'VRCLS.Services.dll'
    $a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($inputPath)
    $t=$a.MainModule.GetType('VRCLS.Services.VR.SteamVROverlayService')
    $m=New-Object Mono.Cecil.MethodDefinition('ForkTest_UnrelatedUpstreamChange',[Mono.Cecil.MethodAttributes]'Private, Static',$a.MainModule.TypeSystem.Void)
    $m.Body.GetILProcessor().Emit([Mono.Cecil.Cil.OpCodes]::Ret);$t.Methods.Add($m)
    $a.Write($inputPath+'.new');$a.Dispose();Move-Item ($inputPath+'.new') $inputPath -Force
    $updated=(Get-FileHash $inputPath).Hash
    & (Join-Path $fork 'Manage.ps1') -Action Prepare
    $two=CurrentName
    Assert ($two -ne $one) 'Compatible upstream change automatically generates a new patched version'
    $a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path (Join-Path $fork $two) 'VRCLS.Services.dll'))
    Assert ($a.MainModule.GetType('VRCLS.Services.VR.SteamVROverlayService').Methods.Name -contains 'ForkTest_UnrelatedUpstreamChange') 'New upstream code survives patching';$a.Dispose()
    Assert ((Get-FileHash $inputPath).Hash -eq $updated) 'Official installation is untouched'
    $a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($inputPath)
    $m=$a.MainModule.GetType('VRCLS.Services.VR.SteamVROverlayService').Methods | Where-Object Name -eq 'UpdatePanel'
    $il=$m.Body.GetILProcessor();$il.InsertBefore($m.Body.Instructions[0],$il.Create([Mono.Cecil.Cil.OpCodes]::Nop))
    $a.Write($inputPath+'.new');$a.Dispose();Move-Item ($inputPath+'.new') $inputPath -Force
    & (Join-Path $fork 'Manage.ps1') -Action Prepare
    Assert ((CurrentName) -eq $two -and (Get-Content (Join-Path $fork 'status.txt') -Raw) -like '*previous verified*') 'Incompatible change preserves the working version without reinstall'
    # Inspect the generated manifest without starting SteamVR or microphone capture.
    $run=Join-Path $fork $two
    $env:VRCLS_ROMAJI_LAUNCHER=Join-Path $fork 'VRCLS-Romaji.exe'
    & (Join-Path $run 'ForkSmoke.exe') manifest
    Assert ($LASTEXITCODE -eq 0) 'SteamVR manifest points to permanent launcher'
}finally{
    $p=[IO.Path]::GetFullPath($root);$tmp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if($p.StartsWith($tmp,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($p) -match '^romaji-update-test-[a-f0-9]{32}$'){Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction SilentlyContinue}
}
