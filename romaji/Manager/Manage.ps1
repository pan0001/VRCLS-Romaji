[CmdletBinding()]param([ValidateSet('Prepare','Launch','Status','PatchStage')][string]$Action='Launch',[string]$InstallDir,[hashtable]$OriginalFiles=@{})
$ErrorActionPreference='Stop'
$base=$PSScriptRoot
$sourceFile=Join-Path $base 'source.txt'
if(!$InstallDir){if(Test-Path $sourceFile){$InstallDir=[IO.File]::ReadAllText($sourceFile).Trim()}else{$InstallDir=Join-Path $env:LOCALAPPDATA 'Programs\VRCLS'}}
$InstallDir=[IO.Path]::GetFullPath($InstallDir)
$state=Join-Path $base 'current.txt'
$status=Join-Path $base 'status.txt'
if($Action -eq 'Status'){if(Test-Path $status){Get-Content -LiteralPath $status -Encoding UTF8};return}
function Digest($p){(Get-FileHash -Algorithm SHA256 -LiteralPath $p).Hash}
function StringHash([string]$s){$sha=[Security.Cryptography.SHA256]::Create();try{([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($s)))).Replace('-','')}finally{$sha.Dispose()}}
function AtomicText($p,$v){$tmp=$p+'.'+[guid]::NewGuid().ToString('N')+'.tmp';[IO.File]::WriteAllText($tmp,$v);if([IO.File]::Exists($p)){[IO.File]::Replace($tmp,$p,$nullDest)}else{[IO.File]::Move($tmp,$p)}}
# File.Replace needs a real backup path on Windows PowerShell 5.1.
$nullDest=Join-Path $base 'pointer.previous'
function Current {
    if(!(Test-Path -LiteralPath $state)){return}
    $id=[IO.File]::ReadAllText($state).Trim()
    if($id -notmatch '^app-[0-9A-F]{16}-[a-f0-9]{8}$'){return}
    $dir=Join-Path $base $id
    try {
        $m=Get-Content -LiteralPath (Join-Path $dir 'fork-ready.json') -Raw | ConvertFrom-Json
        foreach($n in @('VRCLS.exe','VRCLS.dll','VRCLS.Services.dll','VRCLS.Core.dll','VRCLS.deps.json','VRCLS.runtimeconfig.json','VRCLS.Romaji.dll')){
            if((Digest (Join-Path $dir $n)) -ne $m.hashes.$n){return}
        }
        return $dir
    }catch{return}
}
function Tool([string[]]$arguments){& (Join-Path $base 'Patcher.exe') @arguments; if($LASTEXITCODE -ne 0){throw "Compatibility/IL check failed ($($arguments[0]))."}}
function InputFiles {
    $map=@{}
    $legacy=@{
        'VRCLS.dll'=@('842140870C0EDB7E8F7BF37014491EBF78367D7027DE7A076A2FE4827218E49C')
        'VRCLS.Services.dll'=@('84FFD346188647365D90629765544878C53CC6BE69EF7DE3CCA2B672306A782A','5636E9977904234146A5238D82E1FD14B233EED6ED3138820EB55D13A980EA5A','6044F4B433E5D855835D8FA288B477EE37232AB3BCC27A69FE6DED19AFA5038A')
        'VRCLS.deps.json'=@('39EEF11DD856D321411FA202B1DA02A8532D9F4697180C77D2A8298E0E6069A5')
    }
    $pristine=@{'VRCLS.dll'='98E0046265557AC62154624886203DF5A370BE9B29AF2C8B66E67FA281573A41';'VRCLS.Services.dll'='7D1EC7908BF50FB11339579A6552CB3CEAB4DABCD3FE137E886A701948A1D69A';'VRCLS.deps.json'='DE530E2951A888432EE5DFD79F4F84C31DBC4007DB198E60AC93B667F5CB0AEA'}
    foreach($n in @('VRCLS.exe','VRCLS.runtimeconfig.json','VRCLS.dll','VRCLS.Services.dll','VRCLS.Core.dll','VRCLS.deps.json')){
        $p=Join-Path $InstallDir $n
        if($OriginalFiles.ContainsKey($n)){$map[$n]=$OriginalFiles[$n];continue}
        if($legacy.ContainsKey($n) -and (Digest $p) -in $legacy[$n]){
            $p=$null
            foreach($b in @('steamvr-romaji-package-backup-1015','romaji-backup-20260915')){
                $candidate=Join-Path (Join-Path $InstallDir $b) $n
                if((Test-Path -LiteralPath $candidate) -and (Digest $candidate) -eq $pristine[$n]){$p=$candidate; break}
            }
            if(!$p){throw "Cannot locate verified original of $n"}
        }
        $map[$n]=$p
    }
    return $map
}
function Identity($map){
    $items=@($map.Keys | Sort-Object | ForEach-Object { $_+':'+(Digest $map[$_]) })
    $items+=(Digest (Join-Path $base 'payload\VRCLS.Romaji.dll'))
    $items+=(Digest (Join-Path $base 'Patcher.exe'))
    $items+=(Digest $PSCommandPath)
    foreach($n in @('deps-fragment.json','Get-Dependencies.ps1','ForkSmoke.dll')){$items+=Digest (Join-Path $base $n)}
    StringHash ($items -join '|')
}
function PrepareVersion($map,$key){
    foreach($pair in @(@('VRCLS.Core.dll','core'),@('VRCLS.dll','ui'),@('VRCLS.Services.dll','services'))){Tool @('check',$map[$pair[0]],$pair[1]) | Out-Host}
    $id='app-'+$key.Substring(0,16)+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)
    $dir=Join-Path $base $id
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
    foreach($f in Get-ChildItem -LiteralPath $InstallDir -File){
        if($f.Extension -notin @('.dll','.exe') -or $f.Name -like 'unins*.exe' -or $f.Name -like 'VRCLS.Romaji*' -or $map.ContainsKey($f.Name)){continue}
        Copy-Item -LiteralPath $f.FullName -Destination $dir
    }
    if($Action -ne 'PatchStage'){foreach($sub in Get-ChildItem -LiteralPath $InstallDir -Directory){
        if(($sub.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and ($sub.Name -in @('runtimes','data','scripts','translator','pre-models','Assets','plugins') -or $sub.Name -cmatch '^[a-z]{2}(-[A-Za-z]{2,4})?$')){
            Copy-Item -LiteralPath $sub.FullName -Destination $dir -Recurse
        }
    }
    }
    foreach($n in $map.Keys){Copy-Item -LiteralPath $map[$n] -Destination (Join-Path $dir $n)}
    Copy-Item -Path (Join-Path $base 'payload\*') -Destination $dir -Recurse -Force
    if($Action -eq 'PatchStage'){
        $clean=Join-Path $dir 'pristine';New-Item -ItemType Directory $clean|Out-Null
        foreach($n in @('VRCLS.dll','VRCLS.Services.dll','VRCLS.deps.json')){Copy-Item -LiteralPath $map[$n] -Destination $clean}
    }
    $helper=Join-Path $dir 'VRCLS.Romaji.dll'
    $argv=@($map['VRCLS.Services.dll'],$helper,(Join-Path $dir 'VRCLS.Services.dll')); if($Action -eq 'PatchStage'){$argv=@('in-place')+$argv};Tool $argv | Out-Host
    Tool @('desktop',$map['VRCLS.dll'],$helper,(Join-Path $dir 'VRCLS.dll')) | Out-Host
    $deps=Get-Content -LiteralPath $map['VRCLS.deps.json'] -Raw | ConvertFrom-Json
    $target=$deps.targets.($deps.runtimeTarget.name)
    $frag=Get-Content -LiteralPath (Join-Path $base 'deps-fragment.json') -Raw | ConvertFrom-Json
    foreach($k in $frag.libraries.PSObject.Properties.Name){
        if($deps.libraries.PSObject.Properties[$k] -or $target.PSObject.Properties[$k]){throw "Dependency collision: $k"}
        $deps.libraries | Add-Member -NotePropertyName $k -NotePropertyValue $frag.libraries.$k
        $target | Add-Member -NotePropertyName $k -NotePropertyValue $frag.target.$k
    }
    $services=@($target.PSObject.Properties.Name | Where-Object {$_ -like 'VRCLS.Services/*'})
    if($services.Count -ne 1){throw 'Unexpected application dependency manifest'}
    $target.($services[0]).dependencies | Add-Member -NotePropertyName 'VRCLS.Romaji' -NotePropertyValue '1.0.0'
    [IO.File]::WriteAllText((Join-Path $dir 'VRCLS.deps.json'),($deps|ConvertTo-Json -Depth 100 -Compress))
    foreach($n in @('ForkSmoke.exe','ForkSmoke.dll')){Copy-Item -LiteralPath (Join-Path $base $n) -Destination $dir}
    foreach($n in @('runtimeconfig.json','deps.json')){Copy-Item -LiteralPath (Join-Path $dir ("VRCLS."+$n)) -Destination (Join-Path $dir ("ForkSmoke."+$n))}
    $smoke=Start-Process -FilePath (Join-Path $dir 'ForkSmoke.exe') -WorkingDirectory $dir -PassThru -WindowStyle Hidden
    if(!$smoke.WaitForExit(30000)){$smoke.Kill();throw 'New generation smoke test timed out.'}
    if($smoke.ExitCode -ne 0){throw 'New generation failed its runtime smoke test.'}
    if((Identity (InputFiles)) -ne $key){throw 'The official application changed while preparing; retry after update.'}
    $hashes=@{};foreach($n in @($map.Keys)+'VRCLS.Romaji.dll'){$hashes[$n]=Digest (Join-Path $dir $n)}
    [IO.File]::WriteAllText((Join-Path $dir 'fork-ready.json'),(@{key=$key;hashes=$hashes}|ConvertTo-Json))
    if($Action -ne 'PatchStage'){AtomicText $state $id}
    return $dir
    }catch{
        $safe=[IO.Path]::GetFullPath($dir)
        if([IO.Path]::GetDirectoryName($safe) -eq [IO.Path]::GetFullPath($base) -and [IO.Path]::GetFileName($safe) -match '^app-[0-9A-F]{16}-[a-f0-9]{8}$' -and !(Test-Path (Join-Path $safe 'fork-ready.json'))){
            Remove-Item -LiteralPath $safe -Recurse -Force -ErrorAction SilentlyContinue
        }
        throw
    }
}
$mutex=New-Object Threading.Mutex($false,('Local\VRCLS-Romaji-'+(StringHash $base).Substring(0,20)))
$owned=$false
try{
    try{$owned=$mutex.WaitOne(60000)}catch [Threading.AbandonedMutexException]{$owned=$true}
    if(!$owned){throw 'Another fork preparation is still running.'}
    if($Action -eq 'PatchStage'){$map=InputFiles; PrepareVersion $map (Identity $map); return}
    $last=Current
    try{
        $map=InputFiles; $key=Identity $map
        if($last -and (Get-Content (Join-Path $last 'fork-ready.json') -Raw|ConvertFrom-Json).key -eq $key){$dir=$last}else{$dir=PrepareVersion $map $key}
        $message='Ready: '+[IO.Path]::GetFileName($dir)
    }catch{
        if(!$last){throw}
        $dir=$last; $message='Using previous verified version: '+$_.Exception.Message
        Write-Warning $message
    }
    [IO.File]::WriteAllText($status,((Get-Date -Format o)+' '+$message))
    if($Action -eq 'Launch'){
        foreach($p in @(Get-Process VRCLS -ErrorAction SilentlyContinue)){if($p.Path -eq (Join-Path $dir 'VRCLS.exe')){return};throw 'Exit the running VRCLS instance, then open VRCLS Romaji.'}
        $env:VRCLS_ROMAJI_LAUNCHER=Join-Path $base 'VRCLS-Romaji.exe'
        Start-Process -FilePath (Join-Path $dir 'VRCLS.exe') -WorkingDirectory $dir
    }else{Write-Output $dir}
}finally{if($owned){$mutex.ReleaseMutex()};$mutex.Dispose()}
