#!/bin/bash
# 啟動 Hermes 對話橋接（macOS／Linux 版；Windows 用 run-hermes-bridge.ps1）：雙擊魚魚輸入的訊息會交給 Hermes，回答顯示在魚魚的氣泡。
# 需要：桌寵設定的「連線 Agent」頁已開啟「開啟 MCP 介面」與「雙擊輸入交給 MCP 介面（Hermes 橋接程式）」，
# Hermes 的 ~/.hermes/.env 已設定 API_SERVER_KEY，並已啟動 Hermes gateway。
# 用法：code/run-hermes-bridge.sh [--build] [--log] [橋接程式的參數，例如 --mcp <BigBlueFish.Mcp 路徑> --hermes-url <網址>]
#   --build  一律從原始碼編譯（需要 .NET 10 SDK），不使用 App 內附的橋接程式
#   --log    同時把輸出附加到記錄檔（macOS：~/Library/Logs/BigBlueFish/hermes-bridge.log）
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)

build=0; log=0; pass=()
for argument in "$@"; do
    case $argument in
        --build) build=1 ;;
        --log) log=1 ;;
        *) pass+=("$argument") ;;
    esac
done

run() { # the command, then the bridge's own arguments
    if [[ $log == 1 ]]; then
        if [[ $(uname -s) == Darwin ]]; then folder="$HOME/Library/Logs/BigBlueFish"; else folder="${XDG_STATE_HOME:-$HOME/.local/state}/bigbluefish"; fi
        mkdir -p "$folder"
        echo "記錄檔：$folder/hermes-bridge.log"
        "$@" 2>&1 | tee -a "$folder/hermes-bridge.log"
        exit "${PIPESTATUS[0]}"
    fi
    exec "$@"
}

# The macOS app (Standard edition) carries a self-contained bridge next to BigBlueFish.Mcp: no SDK needed.
if [[ $build == 0 && $(uname -s) == Darwin ]]; then
    for app in ${BIGBLUEFISH_APP:+"$BIGBLUEFISH_APP"} "$here/../release/藍色大肥魚.app" "/Applications/藍色大肥魚.app" "$HOME/Applications/藍色大肥魚.app" \
        "$here/../release/蓝色大肥鱼.app" "/Applications/蓝色大肥鱼.app" "$HOME/Applications/蓝色大肥鱼.app"; do
        bundled="$app/Contents/MacOS/BigBlueFish.HermesBridge"
        [[ -x $bundled ]] || continue
        if xattr -p com.apple.quarantine "$bundled" >/dev/null 2>&1; then
            echo "注意：$app 仍帶有下載隔離標記，macOS 可能會擋下橋接程式。請先從 Finder 打開過一次 App，或執行：xattr -dr com.apple.quarantine \"$app\"" >&2
        fi
        run "$bundled" ${pass[@]+"${pass[@]}"}
    done
fi

# Otherwise build from source. A plain build would target win-x64 (code/Directory.Build.props), so the RID is given.
case "$(uname -s)/$(uname -m)" in
    Darwin/arm64) rid=osx-arm64 ;;
    Linux/x86_64) rid=linux-x64 ;;
    *) echo "不支援的平台：$(uname -s) $(uname -m)（支援 Apple Silicon Mac 與 x64 Linux）。" >&2; exit 1 ;;
esac
dotnet=""
for candidate in "${DOTNET:-}" "$HOME/.dotnet/dotnet" "$(command -v dotnet || true)"; do
    if [[ -n $candidate && -x $candidate ]] && [[ $("$candidate" --version 2>/dev/null) == 10.* ]]; then dotnet=$candidate; break; fi
done
[[ -n $dotnet ]] || { echo "找不到 .NET 10 SDK：請安裝 .NET 10，或用 DOTNET 環境變數指定 dotnet 的路徑。" >&2; exit 1; }
project="$here/BigBlueFish.HermesBridge/BigBlueFish.HermesBridge.csproj"
"$dotnet" build "$project" -c Release -f net10.0 -r "$rid" -p:RestoreLockedMode=true --nologo -v q || { echo "橋接程式編譯失敗。" >&2; exit 1; }
# Without an executable of its own (UseAppHost=false, see the project): the process shows up as dotnet running this dll.
run "$dotnet" "$here/BigBlueFish.HermesBridge/bin/Release/net10.0/BigBlueFish.HermesBridge.dll" ${pass[@]+"${pass[@]}"}
