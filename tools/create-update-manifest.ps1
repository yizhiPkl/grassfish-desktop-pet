param(
    [Parameter(Mandatory=$true)][string]$Installer,
    [Parameter(Mandatory=$true)][string]$SigningKey,
    [Parameter(Mandatory=$true)][string]$NotesFile,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$key=[IO.Path]::GetFullPath($SigningKey)
if($key.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Signing private key must be outside the repository'}
$file=Get-Item -LiteralPath $Installer
$info=$file.VersionInfo
$version=[version]('{0}.{1}.{2}.{3}' -f $info.FileMajorPart,$info.FileMinorPart,$info.FileBuildPart,$info.FilePrivatePart)
$name='GrassFish-Setup-'+$version.ToString(4)+'-win-x64.exe'
if($file.Name -ne $name){throw 'Installer name does not match its embedded version'}
if($file.Length -lt 1024 -or $file.Length -gt 256MB){throw 'Installer size outside update limits'}
$notes=[IO.File]::ReadAllText([IO.Path]::GetFullPath($NotesFile))
if($notes.Length -gt 8000){throw 'Release notes are too long'}
$public=[regex]::Match([IO.File]::ReadAllText((Join-Path $root 'src/UpdateTrust.cs')),'PublicKeyXml="([^"]+)"').Groups[1].Value
if(-not $public){throw 'Missing embedded update verification key'}
$rsa=New-Object Security.Cryptography.RSACryptoServiceProvider
$rsa.PersistKeyInCsp=$false
try{
    $rsa.FromXmlString([IO.File]::ReadAllText($key))
    if($rsa.ToXmlString($false) -ne $public){throw 'Signing key does not match the client verification key'}
    $manifest=[ordered]@{schemaVersion=1;repository='yizhiPkl/grassfish-desktop-pet';version=$version.ToString(4);channel='stable';platform='win-x64';installerFile=$name;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant();size=$file.Length;notes=$notes}
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($manifest|ConvertTo-Json -Depth 4))
    $signature=$rsa.SignData($bytes,[Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'))
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $OutputDirectory 'grassfish-update.json'),$bytes)
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'grassfish-update.sig'),[Convert]::ToBase64String($signature),[Text.UTF8Encoding]::new($false))
    Write-Output ('Ready for a manually approved GitHub Release v'+$version.ToString(4)+': installer + grassfish-update.json + grassfish-update.sig. Nothing uploaded.')
}finally{$rsa.Dispose()}
