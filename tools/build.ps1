param([switch]$Public,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$release=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $root 'release/桌面小猫'}
New-Item -ItemType Directory -Path $release -Force | Out-Null
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$wpf=Join-Path $framework 'WPF'
$refs=@('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll','System.Xaml.dll') | ForEach-Object { '/r:'+(Join-Path $framework $_) }
$refs+=@('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll') | ForEach-Object { '/r:'+(Join-Path $wpf $_) }
$refs+=@('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll') | ForEach-Object { '/r:'+(Join-Path $root ('lib/webview2/'+$_)) }
Push-Location (Join-Path $root 'src')
try {
    $output=Join-Path $release 'DesktopKitten.exe'
    $sources=Get-ChildItem -File -Filter '*.cs' | Select-Object -ExpandProperty Name
    & (Join-Path $framework 'csc.exe') /nologo /optimize+ /target:winexe /platform:x64 /codepage:65001 '/win32manifest:app.manifest' (('/win32icon:')+(Join-Path $root 'assets/cat.ico')) (('/out:')+$output) $refs $sources
    if($LASTEXITCODE -ne 0){throw 'C# build failed'}
} finally { Pop-Location }
if($Public){
    # Whitelist the application asset tree but omit private pairing and official
    # application code. Do not stage them even temporarily in a public payload.
    $assetRoot=Join-Path $root 'assets'
    foreach($file in Get-ChildItem -LiteralPath $assetRoot -File -Recurse){
        $relative=$file.FullName.Substring($assetRoot.Length+1).Replace('\','/')
        if($relative -eq 'codex-connection.json' -or $relative.StartsWith('native-task-ui/vendor/')){continue}
        $destination=Join-Path $release ('assets/'+$relative)
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
    $private=Join-Path $release 'assets/codex-connection.json'
    if(Test-Path -LiteralPath $private){throw 'Public output contains a legacy private pairing; use a clean output directory'}
    if(Test-Path -LiteralPath (Join-Path $release 'assets/native-task-ui/vendor')){throw 'Public output contains official application code; use a clean output directory'}
    $provenance=Join-Path $release 'assets/provenance.json'
    $data=Get-Content -LiteralPath $provenance -Raw|ConvertFrom-Json
    $data.source='user-created my-cat artwork';$data|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $provenance -Encoding utf8
}else{Copy-Item -LiteralPath (Join-Path $root 'assets') -Destination $release -Recurse -Force}
Get-ChildItem -LiteralPath (Join-Path $root 'lib/webview2') -Filter '*.dll' | Copy-Item -Destination $release -Force
Copy-Item -LiteralPath (Join-Path $root 'src/DesktopKitten.exe.config') -Destination $release -Force
Copy-Item -LiteralPath (Join-Path $root 'docs/使用说明.md') -Destination (Join-Path $release '使用说明.md') -Force -ErrorAction SilentlyContinue
Write-Output $output
