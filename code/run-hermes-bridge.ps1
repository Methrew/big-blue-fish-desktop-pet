# Fork-original file of the taiwan-edition fork by acer1204 (2026-10-26); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
# Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
# 啟動 Hermes 對話橋接：雙擊魚魚輸入的訊息會交給 Hermes，回答顯示在魚魚的氣泡。
# 需要：桌寵已開啟「MCP 介面」與「雙擊輸入交給 MCP 介面（Hermes 橋接程式）」，Hermes 的 .env 已設定 API_SERVER_KEY 並啟動 gateway。
# macOS 與 Linux 請改用 run-hermes-bridge.sh。
param([string]$SdkPath,[string]$McpCommand,[string]$HermesUrl)
$ErrorActionPreference='Stop'
$sdk=& "$PSScriptRoot/tools/Resolve-Sdk.ps1" -SdkPath $SdkPath
$project=Join-Path $PSScriptRoot 'BigBlueFish.HermesBridge/BigBlueFish.HermesBridge.csproj'
# The project also targets net10.0 for macOS and Linux; Windows runs the WPF build (its image shrinking needs WIC).
& $sdk build $project -c Release -f net10.0-windows -r win-x64 -p:RestoreLockedMode=true --nologo -v q
if($LASTEXITCODE -ne 0){throw '橋接程式編譯失敗'}
$bridgeArgs=@()
if($McpCommand){$bridgeArgs+=@('--mcp',$McpCommand)}
if($HermesUrl){$bridgeArgs+=@('--hermes-url',$HermesUrl)}
# Without an .exe of its own (UseAppHost=false, see the project): the process shows up as dotnet running this dll.
& $sdk (Join-Path $PSScriptRoot 'BigBlueFish.HermesBridge/bin/Release/net10.0-windows/BigBlueFish.HermesBridge.dll') @bridgeArgs
