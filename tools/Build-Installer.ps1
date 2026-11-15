# Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
param(
    [string]$SdkPath,
    [string]$RuntimeDirectory,
    [string]$VisualCppDirectory,
    [string]$Version,
    [string]$OutputDirectory,
    [string]$WorkDirectory,
    [switch]$SkipInstallReadme,
    [ValidateSet('Standard','Safe')][string]$Edition = 'Standard',
    [ValidateSet('Ocr','Vision')][string]$InputMode = 'Ocr'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& "$PSScriptRoot/Setup-Gstreamer.ps1"
$Version = & "$PSScriptRoot/Get-ProjectVersion.ps1" -Version $Version
$sdk = & "$root/code/tools/Resolve-Sdk.ps1" -SdkPath $SdkPath
if (!$WorkDirectory) { $WorkDirectory = Join-Path $root '.work' }
$work = [IO.Path]::GetFullPath($WorkDirectory)
$modeName = if ($InputMode -eq 'Ocr') { 'OCR' } else { 'Vision' }
$editionName = if ($Edition -eq 'Safe') { '-Safe' } else { '' }
$baseName = "BigBlueFish-TW-v$Version$editionName-$modeName-Setup"
$payload = Join-Path $work "BigBlueFish-v$Version$editionName-$modeName-payload"
$stub = Join-Path $work "BigBlueFish-v$Version$editionName-$modeName-setup-stub"
$uninstaller = Join-Path $work "BigBlueFish-v$Version$editionName-$modeName-uninstaller"
$payloadZip = Join-Path $work "BigBlueFish-v$Version$editionName-$modeName-payload.zip"
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'release' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$output = Join-Path $OutputDirectory "$baseName.exe"
$shaFile = Join-Path $OutputDirectory "$baseName.sha256"
foreach ($p in @($payload,$stub,$payloadZip,$uninstaller)) {
    if (Test-Path -LiteralPath $p) { throw "中間產物已存在，請先檢查後移走：$p" }
}
if (Test-Path -LiteralPath $output) { throw "發布檔案已存在，請使用新的工作資料夾或先移走：$output" }
New-Item -ItemType Directory -Force -Path $work | Out-Null
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $payload | Out-Null
& $sdk publish "$root/code/BigBlueFish.Windows/BigBlueFish.Windows.csproj" -c Release -p:Platform=x64 "-p:CompanionEdition=$Edition" "-p:CompanionInputMode=$InputMode" -r win-x64 --self-contained true -p:RestoreLockedMode=true "-p:Version=$Version" "-p:InformationalVersion=$Version" -o $payload --nologo
if ($LASTEXITCODE -ne 0) { throw '桌寵發布失敗。' }
if ($Edition -eq 'Standard') {
    & $sdk publish "$root/code/BigBlueFish.Mcp/BigBlueFish.Mcp.csproj" -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:RestoreLockedMode=true "-p:Version=$Version" -o $payload --nologo
    if ($LASTEXITCODE -ne 0) { throw 'MCP 橋接程式發布失敗。' }
}
$runtimeTarget = Join-Path $payload 'runtime'
New-Item -ItemType Directory -Force -Path $runtimeTarget | Out-Null
if ($RuntimeDirectory) {
    if (!(Test-Path -LiteralPath (Join-Path $RuntimeDirectory 'llama-server.exe'))) { throw '執行環境資料夾缺少 llama-server.exe。' }
    Get-ChildItem -LiteralPath $RuntimeDirectory -Force | Copy-Item -Destination $runtimeTarget -Recurse
} else {
    throw '為避免未經核對的執行環境進入正式安裝程式，請明確傳入 -RuntimeDirectory。'
}
& "$PSScriptRoot/Copy-VisualCppRuntime.ps1" -Destination $runtimeTarget -SourceDirectory $VisualCppDirectory
New-Item -ItemType Directory -Path (Join-Path $payload 'licenses') | Out-Null
Get-ChildItem -LiteralPath "$root/code/licenses" | Where-Object Name -NE 'linux' | Copy-Item -Destination (Join-Path $payload 'licenses') -Recurse
Copy-Item -LiteralPath "$root/code/LICENSE", "$root/code/THIRD-PARTY.md" -Destination $payload
if (!$SkipInstallReadme) {
    Copy-Item -LiteralPath "$root/release/README.md" -Destination (Join-Path $payload 'INSTALL-README.md')
}
New-Item -ItemType Directory -Force -Path (Join-Path $payload 'data/models') | Out-Null
& $sdk publish "$root/uninstaller/BigBlueFish.Uninstall.csproj" -c Release -r win-x64 --self-contained true "-p:CompanionEdition=$Edition" -p:RestoreLockedMode=true "-p:Version=$Version" -o $uninstaller --nologo
if ($LASTEXITCODE -ne 0) { throw '解除安裝程式編譯失敗。' }
Copy-Item -LiteralPath (Join-Path $uninstaller 'Uninstall.exe') -Destination $payload
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zipStream = [IO.File]::Open($payloadZip, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $zip = New-Object IO.Compression.ZipArchive($zipStream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        $prefix = $payload.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
        foreach ($item in Get-ChildItem -LiteralPath $payload -Recurse -Force -File) {
            $rel = $item.FullName.Substring($prefix.Length).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $item.FullName, $rel, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose() }
} finally { $zipStream.Dispose() }
New-Item -ItemType Directory -Force -Path $stub | Out-Null
& $sdk publish "$root/installer/BigBlueFish.Setup.csproj" -c Release "-p:CompanionEdition=$Edition" "-p:CompanionInputMode=$InputMode" -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:RestoreLockedMode=true "-p:Version=$Version" -o $stub --nologo
if ($LASTEXITCODE -ne 0) { throw '安裝程式編譯失敗。' }
$stubExe = Join-Path $stub 'BigBlueFish.Setup.exe'
if (!(Test-Path -LiteralPath $stubExe)) { throw '找不到安裝程式的執行檔。' }
$marker = [Text.Encoding]::ASCII.GetBytes('BIGBLUEFISH_PAYLOAD_V1')
$payloadLength = (Get-Item -LiteralPath $payloadZip).Length
$fs = [IO.File]::Open($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $stubBytes = [IO.File]::ReadAllBytes($stubExe)
    $fs.Write($stubBytes, 0, $stubBytes.Length)
    $payloadBytes = [IO.File]::ReadAllBytes($payloadZip)
    $fs.Write($payloadBytes, 0, $payloadBytes.Length)
    $fs.Write($marker, 0, $marker.Length)
    $lengthBytes = [BitConverter]::GetBytes([Int64]$payloadLength)
    $fs.Write($lengthBytes, 0, $lengthBytes.Length)
} finally { $fs.Dispose() }
$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $shaFile -Encoding ascii -Value "$hash  $baseName.exe"
Write-Host "安裝程式已產生：$output"
Write-Host "SHA256：$hash"
