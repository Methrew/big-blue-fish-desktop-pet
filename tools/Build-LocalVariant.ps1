param(
    [ValidateSet('Ocr', 'Vision')][string]$InputMode = 'Ocr',
    [Parameter(Mandatory = $true)][string]$SdkPath
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& "$PSScriptRoot/Setup-Gstreamer.ps1"
$version = & "$PSScriptRoot/Get-ProjectVersion.ps1" -InputMode $InputMode
$destination = Join-Path $root ".work/BigBlueFish-v$version-$($InputMode.ToLowerInvariant())"
& $SdkPath publish "$root/code/BigBlueFish.Windows/BigBlueFish.Windows.csproj" -c Release `
    -p:Platform=x64 "-p:CompanionInputMode=$InputMode" -r win-x64 --self-contained true -o $destination --nologo
if ($LASTEXITCODE -ne 0) { throw '本機對照版本建置失敗。' }
$actual = (Get-Item -LiteralPath (Join-Path $destination 'BigBlueFish.exe')).VersionInfo.ProductVersion
if ($actual -ne $version) { throw "建置版本不一致：預期 $version，實際 $actual。" }
Write-Output $destination
