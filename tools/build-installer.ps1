param(
    [Parameter(Mandatory=$true)][string]$InnoCompiler,
    [Parameter(Mandatory=$true)][string]$WebView2Bootstrapper,
    [string]$ReleaseDirectory
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$version=[regex]::Match((Get-Content -LiteralPath (Join-Path $root 'src/AssemblyInfo.cs') -Raw),'AssemblyFileVersion\("([0-9.]+)"\)').Groups[1].Value
if(-not $version){throw 'Missing application version'}
$release=if($ReleaseDirectory){[IO.Path]::GetFullPath($ReleaseDirectory)}else{Join-Path $root ('release/public-'+$version)}
$payload=Join-Path $release 'payload'
if(Test-Path -LiteralPath $payload){throw 'Payload already exists; use a new release directory to avoid stale/private files'}
if(-not(Test-Path -LiteralPath $InnoCompiler)){throw 'Inno Setup 6 compiler not found'}
$signature=Get-AuthenticodeSignature -LiteralPath $WebView2Bootstrapper
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation'){throw 'WebView2 bootstrapper must have a valid Microsoft signature'}
& (Join-Path $PSScriptRoot 'build.ps1') -Public -OutputDirectory $payload
if($LASTEXITCODE -ne 0){throw 'Public build failed'}
Copy-Item -LiteralPath (Join-Path $root 'third-party') -Destination $payload -Recurse
$before=(Get-Content -LiteralPath (Join-Path $root 'installer/安装前请读.txt') -Raw).Replace('{{APP_VERSION}}',$version)
[IO.File]::WriteAllText((Join-Path $payload '安装与Codex兼容说明.txt'),$before,[Text.UTF8Encoding]::new($true))
Copy-Item -LiteralPath (Join-Path $root 'docs/发布与兼容性.md') -Destination $payload
$forbidden=@('assets/codex-connection.json','assets/native-task-ui/vendor','qa-profile','native-qa-profile','settings.json','auth.json','.git')
foreach($name in $forbidden){if(Test-Path -LiteralPath (Join-Path $payload $name)){throw ('Private/unlicensed payload content: '+$name)}}
foreach($file in Get-ChildItem -LiteralPath $payload -Recurse -File | Where-Object {$_.Extension -in '.json','.txt','.md','.js','.html','.css','.config'}){
    $text=Get-Content -LiteralPath $file.FullName -Raw
    if($text -match '(?i)[A-Z]:[\\/]+Users[\\/]+[^\\/\r\n]+[\\/]'){throw ('Personal path leaked into '+$file.FullName)}
}
$hashes=Get-ChildItem -LiteralPath $payload -Recurse -File|Sort-Object FullName|ForEach-Object{(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.FullName.Substring($payload.Length+1).Replace('\','/')}
$hashes|Set-Content -LiteralPath (Join-Path $payload 'SHA256SUMS.txt') -Encoding utf8
& $InnoCompiler ('/DPayloadDir='+$payload) ('/DBootstrapper='+[IO.Path]::GetFullPath($WebView2Bootstrapper)) ('/DOutputDir='+$release) (Join-Path $root 'installer/GrassFish.iss')
if($LASTEXITCODE -ne 0){throw 'Inno Setup compilation failed'}
$zip=Join-Path $release ('GrassFish-'+$version+'-win-x64-portable.zip')
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $zip -CompressionLevel Optimal
$setup=Join-Path $release ('GrassFish-Setup-'+$version+'-win-x64.exe')
$info=[Diagnostics.FileVersionInfo]::GetVersionInfo($setup)
$embedded='{0}.{1}.{2}.{3}' -f $info.FileMajorPart,$info.FileMinorPart,$info.FileBuildPart,$info.FilePrivatePart
if($embedded -ne $version -or $info.ProductVersion.Trim() -ne $version){throw 'Installer embedded version does not match the application'}
@($setup,$zip)|ForEach-Object{(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_)}|Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
Write-Output ([pscustomobject]@{Installer=$setup;PortableZip=$zip;Payload=$payload;Checksums=(Join-Path $release 'SHA256SUMS.txt')})
