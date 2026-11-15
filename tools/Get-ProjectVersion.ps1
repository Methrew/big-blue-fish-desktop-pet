# Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
param([string]$Version, [ValidateSet('Ocr','Vision')][string]$InputMode = 'Ocr')
$ErrorActionPreference = 'Stop'
if (!$Version) {
    [xml]$projectVersion = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../Version.props') -Raw
    $Version = [string]$projectVersion.Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$') {
    throw '版本號應由三段數字組成，可附加預發行版名稱。'
}
return $Version
