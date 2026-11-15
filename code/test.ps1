# Modified by acer1204 on 2026-10-26 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
# Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
param([string]$SdkPath,[string]$InstalledDirectory='C:\Program Files\藍色大肥魚',[ValidateSet('Ocr','Vision')][string]$InputMode='Ocr')
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    & "$PSScriptRoot/tests/PowerShellEncodingVerification.ps1"
    if(Get-CimInstance Win32_Process -Filter "Name='BigBlueFish.exe' OR Name='VPet-Simulator.Windows.exe'"){throw '請先從系統匣選單結束桌寵，再執行實際視窗測試。'}
    $sdk=& "$PSScriptRoot/tools/Resolve-Sdk.ps1" -SdkPath $SdkPath
    & $sdk run --project "$PSScriptRoot/tests/TraditionalTextVerification/TraditionalTextVerification.csproj" -c Release
    if($LASTEXITCODE -ne 0){throw '氣泡繁體轉換驗證失敗'}
    $sandbox=Join-Path $PSScriptRoot '.verification'
    New-Item -ItemType Directory -Force -Path $sandbox | Out-Null
    foreach($name in @('assets','runtime','models')){
        $target=if($name -eq 'assets'){Join-Path $PSScriptRoot 'BigBlueFish.Windows/assets'}else{Join-Path $InstalledDirectory $name}
        $link=Join-Path $sandbox $name
        if(!(Test-Path -LiteralPath $link)){New-Item -ItemType Junction -Path $link -Target $target | Out-Null}
    }
        & $sdk run --project "$PSScriptRoot/tests/RunnerVerification/RunnerVerification.csproj" -c Release -p:Platform=x64 "-p:CompanionInputMode=$InputMode" -- --weather-cache (Join-Path $sandbox "weather-$InputMode")
        if($LASTEXITCODE -ne 0){throw '天氣快取驗證失敗'}
        & $sdk run --project "$PSScriptRoot/tests/RunnerVerification/RunnerVerification.csproj" -c Release -p:Platform=x64 "-p:CompanionInputMode=$InputMode" -- (Join-Path $sandbox "runner-$InputMode")
        if($LASTEXITCODE -ne 0){throw 'Runner 故障復原或提示詞驗證失敗'}
        & $sdk run --project "$PSScriptRoot/tests/AnimationVerification/AnimationVerification.csproj" -c Release -- "$PSScriptRoot/BigBlueFish.Windows/assets/fish"
        if($LASTEXITCODE -ne 0){throw '動畫規則驗證失敗'}
        & $sdk run --project "$PSScriptRoot/tests/ForegroundVerification/ForegroundVerification.csproj" -c Release -p:Platform=x64 "-p:CompanionInputMode=$InputMode"
        if($LASTEXITCODE -ne 0){throw '前景視窗驗證失敗'}
        & $sdk run --project "$PSScriptRoot/tests/AnimationUiVerification/AnimationUiVerification.csproj" -c Release -p:Platform=x64 "-p:CompanionInputMode=$InputMode" -- $sandbox
        if($LASTEXITCODE -ne 0){throw '動畫與設定實際視窗驗證失敗'}
        & $sdk run --project "$PSScriptRoot/tests/InstallerVerification/InstallerVerification.csproj" -c Release -p:Platform=x64 "-p:CompanionInputMode=$InputMode" -- --update-notes (Join-Path $sandbox "update-notes-$InputMode")
        if($LASTEXITCODE -ne 0){throw '更新頁面 Markdown 驗證失敗'}
} finally {Pop-Location}
