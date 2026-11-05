param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$packages=@(
@{id='mono.cecil';v='0.11.6';sha='D2A23832AAA948BA9A01ACC42B5726E34C5F995958F1B30D45C0E7C70B3A72D5';files=@{'lib/net40/Mono.Cecil.dll'='Mono.Cecil.dll'}},
@{id='kawazu';v='1.1.4';sha='4AE2C415E100066F360A12684C139449A9907093A8FC08AB746FB4D2A5B21C9A';files=@{'lib/net5.0/Kawazu.dll'='payload/Kawazu.dll'}},
@{id='libnmecab';v='0.10.1';sha='F9945667086BB4DB1A711D255F98287DD810AD0890191812B364337C01E01013';files=@{'lib/netstandard2.0/LibNMeCab.dll'='payload/LibNMeCab.dll'}},
@{id='libnmecab.ipadicbin';v='0.10.0';sha='275B9B15A7191B639C7F20FAC93B3A8FC6AF1C300A8D596A8396C6D6AA396A83';files=@{'lib/netstandard2.0/LibNMeCab.IpaDicBin.dll'='payload/LibNMeCab.IpaDicBin.dll'}}
)
foreach($f in @('AUTHORS','COPYING','char.bin','matrix.bin','sys.dic','unk.dic')){$packages[3].files['content/IpaDic/'+$f]='payload/romaji-dic/'+$f}
$cache=Join-Path $Destination 'packages'
New-Item -ItemType Directory -Force $cache|Out-Null
foreach($dep in $packages){
    $name=$dep.id+'.'+$dep.v+'.nupkg'
    $file=Join-Path $cache $name
    $nuget=Join-Path (Join-Path (Join-Path (Join-Path $env:USERPROFILE '.nuget/packages') $dep.id) $dep.v) $name
    if(!(Test-Path $file) -or (Get-FileHash $file).Hash -ne $dep.sha){
        if((Test-Path $nuget) -and (Get-FileHash $nuget).Hash -eq $dep.sha){Copy-Item $nuget $file -Force}
        else{Invoke-WebRequest -UseBasicParsing -Uri ('https://api.nuget.org/v3-flatcontainer/'+$dep.id+'/'+$dep.v+'/'+$name) -OutFile $file}
    }
    if((Get-FileHash $file).Hash -ne $dep.sha){throw ('Invalid dependency checksum: '+$name)}
    $zip=[IO.Compression.ZipFile]::OpenRead($file)
    try{foreach($entry in $dep.files.Keys){
        $dest=Join-Path $Destination $dep.files[$entry]
        New-Item -ItemType Directory -Force (Split-Path $dest -Parent)|Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry($entry),$dest,$true)
    }}finally{$zip.Dispose()}
}
