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
