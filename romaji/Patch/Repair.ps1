[CmdletBinding()]param([ValidateSet('Apply','Restore')][string]$Action='Apply')
$ErrorActionPreference='Stop'
$base=$PSScriptRoot
$root=[IO.File]::ReadAllText((Join-Path $base 'source.txt')).Trim()
$main=@('VRCLS.dll','VRCLS.Services.dll','VRCLS.deps.json')
$files=$main+@('VRCLS.Romaji.dll','Kawazu.dll','LibNMeCab.dll','LibNMeCab.IpaDicBin.dll')+@('AUTHORS','COPYING','char.bin','sys.dic','matrix.bin','unk.dic'|ForEach-Object {"romaji-dic/$_"})
$context=@('VRCLS.exe','VRCLS.Core.dll','VRCLS.runtimeconfig.json')
$active=Join-Path $base 'installed.json';$pending=Join-Path $base 'transaction.json';$stage=$null
function FileDigest($path){if([IO.File]::Exists($path)){(Get-FileHash -LiteralPath $path).Hash}else{'absent'}}
function Json($path,$obj){
    $tmp=$path+'.tmp';[IO.File]::WriteAllText($tmp,($obj|ConvertTo-Json -Depth 8))
    if([IO.File]::Exists($path)){[IO.File]::Replace($tmp,$path,$path+'.old')}else{[IO.File]::Move($tmp,$path)}
}
function ReadJson($path){if(Test-Path -LiteralPath $path){Get-Content -LiteralPath $path -Raw|ConvertFrom-Json}}
function Idle{foreach($p in @(Get-Process VRCLS -ErrorAction SilentlyContinue)){if(!$p.Path -or $p.Path -eq (Join-Path $root 'VRCLS.exe')){throw 'Exit official VRCLS first; automatic repair waits for it to close.'}}}
function Status($message){[IO.File]::WriteAllText((Join-Path $base 'status.txt'),((Get-Date -Format o)+' '+$message));Write-Host $message}
function ReplaceFile($from,$to){
    New-Item -ItemType Directory -Force (Split-Path -Parent $to)|Out-Null
    $tmp=$to+'.romaji-'+[guid]::NewGuid().ToString('N')+'.tmp'
    try{
        Copy-Item -LiteralPath $from -Destination $tmp
        if([IO.File]::Exists($to)){[IO.File]::Replace($tmp,$to,[System.Management.Automation.Language.NullString]::Value)}
        else{[IO.File]::Move($tmp,$to)}
    }finally{if(Test-Path -LiteralPath $tmp){Remove-Item -LiteralPath $tmp}}
}
function Generation($id){if($id -notmatch '^[0-9a-f]{32}$'){throw 'Invalid backup identifier'};Join-Path $base "backups/$id"}
function Recover{
    $tx=ReadJson $pending;if(!$tx){return}
    $rec=ReadJson $active
    if($rec -and $rec.id -eq $tx.id){Remove-Item -LiteralPath $pending;return}
    $d=Generation $tx.id
    foreach($n in @($tx.changed)){
        if($n -notin $files){throw 'Unexpected transaction entry'}
        $h=FileDigest (Join-Path $root $n)
        if($h -ne $tx.before.$n -and $h -ne $tx.after.$n){throw "A newer update changed $n; refusing to roll it back"}
        if($tx.before.$n -ne 'absent' -and (FileDigest (Join-Path $d "undo/$n")) -ne $tx.before.$n){throw "Invalid transaction backup: $n"}
    }
    foreach($n in @($tx.changed)){
        $p=Join-Path $root $n
        if((FileDigest $p) -eq $tx.before.$n){continue}
        if($tx.before.$n -eq 'absent'){Remove-Item -LiteralPath $p}else{ReplaceFile (Join-Path $d "undo/$n") $p}
    }
    Remove-Item -LiteralPath $pending
}
$mutex=New-Object Threading.Mutex($false,'Local\VRCLS-Romaji-InPlace-Repair')
$locked=$false
try{
    try{$locked=$mutex.WaitOne(60000)}catch [Threading.AbandonedMutexException]{$locked=$true}
    if(!$locked){throw 'Another patch operation is in progress'}
    Idle;Recover
    $rec=ReadJson $active
    $before=@{};foreach($n in $files+$context){$before[$n]=FileDigest (Join-Path $root $n)}
    $sources=@{};$after=@{};$orig=@{};$override=@{}
    foreach($n in $files){$after[$n]=$before[$n]}
    if($Action -eq 'Restore'){
        if(!$rec -or $rec.restored){Status 'No active patch.';return}
        $d=Generation $rec.id
        foreach($n in $main+$context){
            if($before[$n] -ne $rec.after.$n){throw "Installation updated since patching: $n. Refusing to restore old binaries."}
        }
        foreach($n in $main){
            $sources[$n]=Join-Path $d "original/$n";$after[$n]=FileDigest $sources[$n]
            if($after[$n] -ne $rec.original.$n){throw 'Original backup verification failed'}
        }
    }else{
        $version=(FileDigest (Join-Path $base 'payload/VRCLS.Romaji.dll'))+(FileDigest (Join-Path $base 'Patcher.exe'))+(FileDigest $PSCommandPath)
        if($rec -and !$rec.restored){
            $same=$rec.version -eq $version
            foreach($n in $files+$context){if($before[$n] -ne $rec.after.$n){$same=$false}}
            if($same){Status 'Patch is current.';return}
            foreach($n in $main){if($before[$n] -eq $rec.after.$n){
                $p=Join-Path (Generation $rec.id) "original/$n"
                if((FileDigest $p) -ne $rec.original.$n){throw "Invalid original backup: $n"}
                $override[$n]=$p
            }}
        }
        $stage=& (Join-Path $base 'Manage.ps1') -Action PatchStage -InstallDir $root -OriginalFiles $override
        foreach($n in $files){$sources[$n]=Join-Path $stage $n;$after[$n]=FileDigest $sources[$n];if($after[$n] -eq 'absent'){throw "Missing staged file: $n"}}
        foreach($n in $main){$orig[$n]=FileDigest (Join-Path $stage "pristine/$n")}
        foreach($n in $files){
            if($n -in $main -or $n -eq 'VRCLS.Romaji.dll' -or $before[$n] -eq 'absent' -or $before[$n] -eq $after[$n]){continue}
            if(!$rec -or $rec.after.$n -ne $before[$n]){throw "Another component owns $n. Nothing was changed."}
        }
    }
    $id=[guid]::NewGuid().ToString('N');$d=Generation $id
    $changed=@($files|Where-Object {$before[$_] -ne $after[$_]}|Sort-Object @{Expression={if($_ -in $main){1}else{0}}})
    foreach($n in $context){$after[$n]=$before[$n]}
    if($Action -eq 'Apply'){
        New-Item -ItemType Directory -Force "$d/original"|Out-Null
        foreach($n in $main){Copy-Item -LiteralPath (Join-Path $stage "pristine/$n") -Destination "$d/original"}
    }
    Idle
    foreach($n in $files+$context){if((FileDigest (Join-Path $root $n)) -ne $before[$n]){throw 'Update still in progress. Retry after it finishes.'}}
    foreach($n in $changed){if($before[$n] -ne 'absent'){
        $p=Join-Path $d "undo/$n";New-Item -ItemType Directory -Force (Split-Path -Parent $p)|Out-Null
        Copy-Item -LiteralPath (Join-Path $root $n) -Destination $p
        if((FileDigest $p) -ne $before[$n]){throw "Backup verification failed: $n"}
    }}
    Json $pending @{id=$id;before=$before;after=$after;changed=$changed}
    try{
        foreach($n in $changed){
            Idle;$p=Join-Path $root $n
            if((FileDigest $p) -ne $before[$n]){throw "Concurrent modification: $n"}
            ReplaceFile $sources[$n] $p;if((FileDigest $p) -ne $after[$n]){throw "Write verification failed: $n"}
        }
        Json $active @{id=$id;version=$version;original=$orig;after=$after;restored=($Action -eq 'Restore')}
        Remove-Item -LiteralPath $pending
    }catch{Idle;Recover;throw}
    Status $(if($Action -eq 'Restore'){'Official binaries restored.'}else{'In-place patch verified. Start VRCLS normally.'})
}catch{Status ('Not applied: '+$_.Exception.Message);throw}
finally{
    if($stage){$safe=[IO.Path]::GetFullPath($stage);if([IO.Path]::GetDirectoryName($safe) -eq $base -and [IO.Path]::GetFileName($safe) -match '^app-[0-9A-F]{16}-[a-f0-9]{8}$'){Remove-Item -LiteralPath $safe -Recurse -Force -ErrorAction SilentlyContinue}}
    if($locked){$mutex.ReleaseMutex()};$mutex.Dispose()
}
