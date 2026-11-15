# Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
param(
    [string]$SdkPath,
    [string]$Distribution = 'BigBlueFish-Verification',
    [ValidateSet('Standard','Safe')][string]$Edition = 'Standard',
    [Parameter(Mandatory=$true)][string]$RuntimeDirectory,
    [string]$WorkDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sdk = & "$root/code/tools/Resolve-Sdk.ps1" -SdkPath $SdkPath
$version = & "$PSScriptRoot/Get-ProjectVersion.ps1"
if (!$WorkDirectory) { $WorkDirectory = Join-Path $root ".work/linux-$Edition" }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'release' }
$work = [IO.Path]::GetFullPath($WorkDirectory)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$runtime = [IO.Path]::GetFullPath($RuntimeDirectory)
if (Test-Path -LiteralPath $work) { throw "建置資料夾已存在，請使用新的資料夾：$work" }
if (!(Test-Path -LiteralPath (Join-Path $runtime 'llama-server'))) { throw '請提供已核對的 Linux x64 llama.cpp 執行環境資料夾。' }
New-Item -ItemType Directory -Path $work | Out-Null
New-Item -ItemType Directory -Path $output -ErrorAction SilentlyContinue | Out-Null
$payload = Join-Path $work 'payload'
& $sdk publish "$root/code/BigBlueFish.Linux/BigBlueFish.Linux.csproj" -c Release -r linux-x64 --self-contained true "-p:CompanionEdition=$Edition" -p:RestoreLockedMode=true -o $payload --nologo
if ($LASTEXITCODE -ne 0) { throw 'Linux 程式建置失敗。' }
if ($Edition -eq 'Standard') {
    & $sdk publish "$root/code/BigBlueFish.Mcp/BigBlueFish.Mcp.csproj" -c Release -r linux-x64 --self-contained true -p:RestoreLockedMode=true -o $payload --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Linux MCP 元件建置失敗。' }
}
function LinuxPath([string]$path) {
    $result = & wsl -d $Distribution -- wslpath -a ($path.Replace('\','/'))
    if ($LASTEXITCODE -ne 0) { throw "無法存取 Linux 建置路徑：$path" }
    return ($result | Select-Object -Last 1).Trim()
}
& wsl -d $Distribution -- bash (LinuxPath "$PSScriptRoot/build-linux-deb.sh") (LinuxPath $root) (LinuxPath $work) (LinuxPath $runtime) (LinuxPath $output) $version $Edition
if ($LASTEXITCODE -ne 0) { throw 'Linux 安裝套件產生失敗。' }
