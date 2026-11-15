# Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
param([string]$SdkPath,[switch]$Locked,[ValidateSet('Ocr','Vision')][string]$InputMode='Ocr')
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    & "$PSScriptRoot/../tools/Setup-Media.ps1"
    $sdk=& "$PSScriptRoot/tools/Resolve-Sdk.ps1" -SdkPath $SdkPath
    $project=Join-Path $PSScriptRoot 'BigBlueFish.Windows/BigBlueFish.Windows.csproj'
    $argsBuild=@('build',$project,'-c','Release','-p:Platform=x64',"-p:CompanionInputMode=$InputMode",'--nologo')
    if($Locked){$argsBuild+='-p:RestoreLockedMode=true'}
    & $sdk @argsBuild
    if($LASTEXITCODE -ne 0){throw '編譯失敗'}
} finally {Pop-Location}
