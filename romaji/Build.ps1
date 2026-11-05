param([string]$ReferenceDir=(Join-Path $env:LOCALAPPDATA 'Programs\VRCLS'),[string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$ReferenceDir=[IO.Path]::GetFullPath($ReferenceDir)
foreach($project in @('Patcher/Patcher.csproj','Addon/VRCLS.Romaji.csproj','Tests/ForkSmoke.csproj','Launcher/Launcher.csproj')){
    & $Dotnet build (Join-Path $PSScriptRoot $project) -c Release --nologo "-p:VrclsReferenceDir=$ReferenceDir"
    if($LASTEXITCODE -ne 0){throw "Build failed: $project"}
}
$out=Join-Path $PSScriptRoot ('dist/release-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force (Join-Path $out 'payload')|Out-Null
foreach($n in @('Get-Dependencies.ps1','Install.ps1','Manage.ps1','deps-fragment.json')){
    Copy-Item (Join-Path $PSScriptRoot "Manager/$n") $out
}
foreach($n in @('Patcher/bin/Release/net48/Patcher.exe','Launcher/bin/Release/net48/VRCLS-Romaji.exe','Tests/bin/Release/net8.0-windows/win-x64/ForkSmoke.exe','Tests/bin/Release/net8.0-windows/win-x64/ForkSmoke.dll','README.md','Install.cmd','DOTNET-LICENSE.txt')){
    Copy-Item (Join-Path $PSScriptRoot $n) $out
}
Copy-Item (Join-Path $PSScriptRoot 'Addon/bin/Release/net8.0-windows/VRCLS.Romaji.dll') (Join-Path $out 'payload')
$zip=Join-Path $PSScriptRoot 'dist/VRCLS-Romaji-windows-x64.zip'
Compress-Archive -Path "$out/*" -DestinationPath $zip -Force
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'dist/last-build.txt'),$out)
Get-FileHash $zip
