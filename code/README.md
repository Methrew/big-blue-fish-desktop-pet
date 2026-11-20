<!-- Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0). -->
# 開發說明

使用說明見儲存庫根目錄的 [README](../README.md) 與 [macOS 說明](../docs/MACOS.md)，發布說明見 [release/README](../release/README.md)。

MCP 的連線設定、工具欄位、動作與文字排程、事件的接收及錯誤處理見[內部工具參考](../docs/MCP.md)；共用的通訊協定程式碼在 `BigBlueFish.Control/`，stdio 橋接程式的入口在 `BigBlueFish.Mcp/`，各平台的執行端是 Windows 的 `CompanionMcp.cs`，以及 macOS 與 Linux 共用的 `LinuxMcp.cs`、`LinuxMcpInterface.cs`（外部 MCP 介面）。

本版（台灣繁體中文版 `1.1.5-tw.1`）以 macOS（Apple Silicon）為主要平台：Mac 版在這台 Mac 上建置並執行測試；Windows 與 Linux 的原始碼只確認可以編譯，沒有實際執行或測試。

## 專案結構

| 資料夾 | 內容 |
| --- | --- |
| `BigBlueFish.Mac/` | macOS 版 App。編譯 `BigBlueFish.Linux/` 的 Avalonia 原始碼，並連結 `BigBlueFish.Windows/Companion/` 裡共用的 `*.cs`，定義 `COMPANION_MAC`；以 `MacCapture.cs` 取代 `LinuxCapture.cs`。另有 `mac-ocr.swift`（Apple Vision OCR）、`mac-screen.swift`（`find_on_screen` 用的視窗清單與輔助使用讀取）、資料匯入（`MacDataImport.cs`）、登入項目（`MacLoginItem.cs`）、鑰匙圈（`MacKeychain.cs`、`MacSecrets.cs`）、系統讀數（`MacSystem.cs`）與畫面搜尋（`MacScreenSearch.cs`） |
| `BigBlueFish.Linux/` | Avalonia 桌寵，Linux 版直接使用，macOS 版也編譯同一份 |
| `BigBlueFish.Windows/` | WPF 的 Windows 版；`Companion/` 裡有各平台共用的邏輯，例如 `CompanionEdition.cs`（本版的名稱、識別碼與發布來源）、`CompanionTraditional.cs`（OpenCC 繁體轉換）、`CompanionActionNames.cs`（繁體動作名稱）、`CompanionBrain.cs`、`CompanionCloud.cs`（雲端 AI 服務）、`CompanionWeather.cs`、`CompanionRegions.cs`、`CompanionReminders.cs`、`CompanionScreenMatching.cs`、`CompanionAttachmentRules.cs` |
| `BigBlueFish.Agent/` | ACP 用戶端、Harness 清單、工作區、技能與人設（`Skills/`）；`AgentLaunch.cs` 是宿主替某個 Harness 啟動的程式加上的環境變數與對話分組，例如 Claude Code、Codex CLI、Gemini CLI 改用雲端 AI 服務的 API Key（`ApiKeyLaunch`：各自的設定資料夾、環境變數、要拿掉或設成空值的變數、設定檔與連線頁的文字） |
| `BigBlueFish.Control/` | 共用的 MCP 通訊協定與工具定義 |
| `BigBlueFish.Mcp/` | stdio 橋接程式 |
| `BigBlueFish.HermesBridge/` | Hermes 對話橋接程式（`net10.0` 給 macOS／Linux，`net10.0-windows` 給 Windows） |
| `BigBlueFish.Diagnostics/` | 記錄主控台使用的診斷記錄 |
| `../deployment/` | 主程式、安裝程式和解除安裝程式共用的模型準備、下載、更新與現代介面 |
| `../installer/`、`../uninstaller/` | Windows 安裝程式與解除安裝程式 |
| `tests/` | 主控台與介面測試，見下方 |

## macOS（主要平台）

需要 Apple Silicon Mac、macOS 14 或更新版本、Xcode 命令列工具（`clang`、`swiftc`、`python3`）與 .NET 10 SDK。建置完整的 App：

```sh
tools/build-macos.sh            # 標準版：release/藍色大肥魚.app
tools/build-macos.sh Safe       # 無MCP版：release/藍色大肥魚無MCP版.app
DOTNET=~/.dotnet/dotnet tools/build-macos.sh   # 指定 .NET 10
```

詳細步驟、權限與發布方式見 [macOS 說明](../docs/MACOS.md#從原始碼建置)。

`code/Directory.Build.props` 全域預設 `RuntimeIdentifier=win-x64`，所以在 Mac 上直接用 `dotnet` 建置或測試時，一定要加 `-r osx-arm64`。只想確認可以編譯時：

```sh
DOTNET=~/.dotnet/dotnet
$DOTNET build code/BigBlueFish.Mac/BigBlueFish.Mac.csproj -c Release -r osx-arm64 -p:RestoreLockedMode=true -o /tmp/bbf-mac
$DOTNET build code/BigBlueFish.Mac/BigBlueFish.Mac.csproj -c Release -r osx-arm64 -p:CompanionEdition=Safe -p:RestoreLockedMode=true -o /tmp/bbf-mac-safe
```

- 不同目標的建置會共用 `bin/` 資料夾，建置不同版本或平台時請用 `-o` 指定各自的輸出資料夾，或加上 `--no-incremental`。
- 鎖定檔：Mac 專案的標準版用 `packages.lock.json`，無MCP版用 `packages.safe.lock.json`；`BigBlueFish.Control`、`BigBlueFish.Mcp`、`BigBlueFish.Agent`、`BigBlueFish.Diagnostics` 與 `BigBlueFish.HermesBridge` 依 RID 分開，使用 `obj/<rid>/` 與 `packages.<rid>.lock.json`。
- Mac、Linux、Windows 三個 App 專案和 `tests/RemoteModelVerification` 都參考 Anthropic 官方的 C# SDK（NuGet `Anthropic` 13.8.0，雲端 AI 服務的 Claude 用）。改版本時，要一起重新產生這些專案（Windows 另有 `packages.vision.lock.json`）與編譯 Windows 版的測試專案的鎖定檔，`-p:RestoreLockedMode=true` 的建置才會成功；新的套件也要寫進[第三方聲明](THIRD-PARTY.md)。
- 橋接程式在 Mac 上要用 `-f net10.0 -r osx-arm64` 編譯，否則產生的 dll 無法在 Apple Silicon 的 dotnet 上載入。
- `--test-mode` 的魚魚會讀取資料夾裡的 `test-command.txt`（一次一個指令，執行完寫出內容相同的 `test-done.txt`，出錯時另外寫出 `test-error.txt`）並寫出 `test-status.json` 等狀態檔（標準版的 `agentStatus` 是 Agent 連線的狀態文字），可用的指令分散在 `BigBlueFish.Linux/` 的 `LinuxTestCommands.cs`、`LinuxBubbleText.cs`、`LinuxProactive.cs` 與 `LinuxWalk.cs`（例如 `bubble <文字>`、`stream <文字>`、`walk:x,y`、`find:查詢文字`、`reminder-clock:…`、`open-input`、`attach <路徑>`）。用測試模式時請搭配 `BIGBLUEFISH_DATA` 指定暫存資料夾；魚魚同一時間只能執行一隻（原版也算），測試前請先關閉正在執行的魚魚。
  - 檢查畫面用的指令（`LinuxTestCommands.cs`）：`settings-page <導覽按鈕文字>`、`settings-size <寬>x<高>`、`settings-scroll <位置|end>`、`render-settings-full [檔名.png]`（目前設定頁從頭到尾，不受捲動範圍限制）、`list-windows`（寫出 `windows.json`）、`render-window <標題的一部分> <檔名.png>`、`texts <標題的一部分> <檔名.json>`（視窗顯示的文字，不必 OCR）、`close-window <標題的一部分>`、`render-bubble`、`open-menu` 與 `render-menu`、`click <按鈕或核取方塊的文字>`、`select <目前顯示的選項>=<新選項>`、`type <提示文字>=<內容>`、`install-model`、`check-updates [網址]`（網址代替 GitHub 的最新版本 API，可以測試發現新版本的對話框）。圖片以兩倍解析度存在資料夾裡，`render`、`render-settings` 等也可以在後面指定檔名；開啟對話框的指令不會等對話框關閉，接著可以用 `render-window` 或 `click` 處理它。`type` 只能填測試用的值：在設定頁儲存的授權碼和 API Key 會寫進這台 Mac 真正的鑰匙圈。

### 在 Mac 上執行主控台測試

下列測試不需要開啟 App，可以直接在 Mac 上執行。指令都在儲存庫根目錄執行：先建置到暫存資料夾，再執行輸出資料夾裡的同名程式。

```sh
DOTNET=~/.dotnet/dotnet
name=TraditionalTextVerification   # 換成下表的專案名稱
$DOTNET build "code/tests/$name" -c Release -r osx-arm64 -p:NuGetLockFilePath=obj/packages.osx-arm64.lock.json -o "/tmp/bbf-tests/$name"
DOTNET_ROOT=~/.dotnet "/tmp/bbf-tests/$name/$name" [參數]
```

- `-p:NuGetLockFilePath=obj/packages.osx-arm64.lock.json` 讓這次還原把鎖定檔寫到 `obj/`，不會改動儲存庫裡為 win-x64 記錄的鎖定檔。
- `RemoteModelVerification` 的 osx-arm64 與 win-x64 鎖定檔（`packages.osx-arm64.lock.json`、`packages.win-x64.lock.json`）在儲存庫裡，建置它時改用 `-p:RestoreLockedMode=true`、不加 `NuGetLockFilePath`，就會照記錄的版本還原 Anthropic SDK 等套件。
- .NET 安裝在 `~/.dotnet`、不在 PATH 上時，執行輸出的程式要設定 `DOTNET_ROOT=~/.dotnet`；也可以改用 `$DOTNET "/tmp/bbf-tests/$name/$name.dll"`，但 `MacEditionVerification` 必須執行同名程式本身，因為它會在模擬的 App 裡再啟動自己。

| 專案 | 檢查內容 | 參數 |
| --- | --- | --- |
| `TraditionalTextVerification` | 氣泡的繁體轉換（8591 筆語料）、台灣用語修正與串流顯示 | 無 |
| `WeatherRegionVerification` | 地區清單、繁體地名、MCP 地點代碼、儲存的地點、macOS 鑰匙圈流程（中央氣象署授權碼、遠端模型與三個雲端 AI 服務的 API Key；用假的 `security`，不動到真正的鑰匙圈）、`Cloud` 模型來源的設定正規化與天氣快取 | `live`：實際連線 Open-Meteo 與中央氣象署 |
| `RemoteModelVerification` | 遠端模型：API Key、圖片支援判斷、連線測試訊息、只截前景視窗、拒收圖片時改送 OCR 文字（用假的 OpenAI 相容伺服器）。雲端 AI 服務（`CloudVerification.cs`）：用三個本機的假 Claude、OpenAI、Gemini 伺服器，檢查模型清單、分頁與預選、請求內容（金鑰的標頭、人設、圖片、思考強度與備援，沒有 llama.cpp 欄位與取樣參數）、思考設定被拒時的重試、各種錯誤的中文訊息與暫停時間、婉拒與串流中斷、拒收截圖改送文字、每天上限與 `cloud-usage.json`，以及記錄主控台裡沒有金鑰或服務回覆的原文 | `live <網址> [API Key] [模型名稱]`：對真的伺服器測試遠端模型；雲端 AI 服務只用假的伺服器，不會連線真正的 API |
| `ReminderVerification` | 主動提醒（模擬多種日子，含睡眠計時） | 無 |
| `ScreenMatchingVerification` | `find_on_screen` 與 `walk_to` 的名稱比對規則、假的畫面 | `BigBlueFish.MacScreen` 的路徑：另外讀這台 Mac 的真實畫面並檢查 JSON 格式 |
| `MacEditionVerification` | 版本比較、資產檔名、從原版匯入、登入項目（都在暫存資料夾裡） | `[工作資料夾] [--safe-app <無MCP版建置資料夾>]` |
| `MacLogicVerification` | 推論裝置名稱、只有相容動畫時的動畫規則、登入 shell 的 PATH（慢速、卡住的 shell）、前景視窗規則、終端機登入指令稿 | `[儲存庫根目錄]` |
| `MacSystemVerification` | 這台 Mac 的閒置時間、睡眠也計時的時鐘、電池、鎖定與前景程式讀數 | `--finder`：實際詢問 Finder（可能跳出「自動化」權限詢問） |
| `McpProtocolVerification` | 真正的 stdio 橋接程式與通訊協定、依平台的工具清單（Linux 15 個，Windows 與 macOS 17 個）、唯讀設定與金鑰被拒絕的原因（包括雲端 AI 服務的欄位） | 輸出資料夾裡 `BigBlueFish.Mcp` 的路徑 |
| `AgentVerification` | ACP 連線、對話恢復、工作區、輸入框附件（`resource_link`、圖片區塊與兩種退回）；DeepSeek Harness Desktop 的 app.asar 檢查（用假的桌面版）；拔掉電源時暫停 Agent 與恢復連線；Agent 沒有回覆或連線中斷時的提示；Claude Code、Codex CLI、Gemini CLI 改用 API Key（`LaunchVerification.cs`：環境變數只給選定的 Harness、拿掉原本登入的權杖與轉址變數、Gemini CLI 的變數設成空值、Codex CLI 的 `config.toml` 與 Gemini CLI 的 `settings.json`、換設定時重新啟動、沒有 Key 不啟動、Key 被拒絕時顯示處理方式、Key 不進記錄與對話檔、兩種登入的對話分開恢復） | 工作資料夾（可省略） |
| `AnimationVerification` | 動作權重、日期與農曆節日規則 | `[動畫資料夾]`（預設從儲存庫根目錄找） |
| `OcrLayoutVerification` | OCR 文字依版面整理 | 無 |

Python 測試（Python 3.9 以上）：

- `python3 code/tests/HermesBridgeVerification/fake_e2e.py [BigBlueFish.HermesBridge.dll] <BigBlueFish.Mcp>`：用假的魚魚與假的 Hermes 檢查整條橋接路徑（macOS／Linux）。先用 `code/run-hermes-bridge.sh --build` 與 `dotnet publish code/BigBlueFish.Mcp -r osx-arm64` 建置。
- `python3 code/tests/HermesScenarioVerification/scenarios.py`：需要正在執行的魚魚、Hermes gateway 與模型（Windows、macOS、Linux），魚魚沒有的工具對應的情境會略過。
- `python3 tools/Verify-MacPackage.py <App 路徑> --edition Standard|Safe --output <資料夾>`：檢查建置好的 App，會以測試模式短暫啟動它。標準版另外以 `--test-battery`（模擬使用電池）再啟動一次，確認魚魚啟動的 Agent 停在「使用電池 · Agent 已暫停」、不會執行任何 CLI，拖到魚魚身上的檔案仍留在輸入框，魚魚也會說明 Agent 已暫停；這一輪的記錄和狀態寫在輸出資料夾的 `battery.log` 與 `battery-status.json`。

原作者 1.1.4 用 `python3 tools/Compress-Animations.py <輸出資料夾> [--apply]` 重新壓縮了 106 段高畫質動畫（libvpx-vp9、CRF 34、`yuva420p`，仍是 1280×720、每秒 60 格），每段都核對影格數、長度和透明度後才取代原檔；相容動畫沒有改變。這個腳本需要 Python 3.11 以上與 FFmpeg（含 ffprobe），只有要重新壓縮動畫時才用得到。

### 台灣繁體檢查

`python3 tools/Check-Traditional.py` 會檢查出貨的字串與使用者文件是不是台灣繁體中文：簡體字是錯誤，大陸用語與台灣不用的異體字是警告（`--strict` 時警告也算失敗）。合理保留的簡體字或用語（例如動畫檔名、和外部資料比對的字串、原版的資料夾名稱）寫在 `tools/traditional-allowlist.txt`，每行是「路徑<Tab>文字<Tab>原因」。常用參數：`--summary`、`--errors-only`、`--scope mac`、`--include-tests`，以及 `--assembly <dll...>` 檢查建置好的 DLL。GitHub Actions 的 macOS 工作流程在建置前會執行 `--errors-only`。

## 修改入口

| 檔案 | 功能 |
| --- | --- |
| `BigBlueFish.Windows/Companion/CompanionEdition.cs` | 本版的名稱、Bundle ID、資料夾、發布來源與致謝；建置腳本透過 `tools/Get-EditionIdentity.py` 讀取 |
| `BigBlueFish.Linux/LinuxPetWindow.cs`、`LinuxSettings.cs` | macOS 與 Linux 的生命週期、動畫、設定頁 |
| `BigBlueFish.Linux/LinuxModelSettings.cs`、`LinuxWeatherSettings.cs`、`LinuxProactive.cs` | 遠端模型與雲端 AI 服務、天氣地點與中央氣象署授權碼、主動提醒的設定頁與邏輯；`ApplyModelSource()` 把儲存的模型來源交給 `CompanionBrain` |
| `BigBlueFish.Linux/LinuxMaintenance.cs` | 安裝模型、「關於與更新」頁與檢查更新 |
| `BigBlueFish.Windows/Companion/CompanionWindow.cs` | Windows 的生命週期、拖曳、動畫、設定和電源事件 |
| `BigBlueFish.Windows/Companion/CompanionMenus.cs` | Windows 的右鍵選單、地點選擇和手動天氣播報 |
| `BigBlueFish.Windows/Companion/CompanionPreferences.cs` | 設定的預設值、驗證和不可分割的儲存 |
| `BigBlueFish.Windows/Companion/CompanionRegions.cs` | 地區層級、繁體顯示名稱與定位 |
| `BigBlueFish.Windows/Companion/CompanionWeather.cs` | 天氣請求、天氣代號的中文對照、中央氣象署鄉鎮預報和依地點快取 |
| `BigBlueFish.Windows/Companion/CompanionBrain.cs` | 模型啟動、推論、遠端模型、雲端 AI 服務的分支（`UseCloudModel`、測試、每天上限與暫停）、電池策略和提示詞 |
| `BigBlueFish.Windows/Companion/CompanionCloud.cs` | 雲端 AI 服務：Claude API（Anthropic 官方 C# SDK）、OpenAI API（Chat Completions）與 Gemini API（generateContent）的請求與串流、模型清單與預選、每句費用估計、錯誤的中文訊息、暫停規則（`CloudPause`）與每天用量（`CloudUsage`、`cloud-usage.json`）。官方端點寫在 `CompanionCloud.Providers`，`CloudGuard` 只讓請求送往這些端點；`RedirectForTests` 只給測試把端點換成本機伺服器，App 沒有任何設定或環境變數能改端點。Mac、Linux 專案和 `RemoteModelVerification` 以連結編譯這個檔案 |
| `BigBlueFish.Windows/Companion/CompanionTraditional.cs` | 氣泡文字的台灣繁體轉換 |
| `BigBlueFish.Windows/Companion/CompanionSpeech.cs` | Windows 所有模型回覆的逐字顯示、Unicode 字元邊界、取代和取消 |
| `BigBlueFish.Windows/Companion/CompanionEdgeLayout.cs` | 貼邊、獨立氣泡視窗和 DPI 換算 |
| `BigBlueFish.Windows/Companion/CompanionNews.cs` | 新聞機率、RSS 標題和摘要整理 |
| `BigBlueFish.Windows/Companion/CompanionVideoPlayer.cs` | Windows 的透明影片解碼、影格計時與播放 |
| `BigBlueFish.Windows/assets/regions/` | 地區資料、來源版本、輸入 SHA256 與繁體顯示名稱表 |
| `../deployment/` | 主程式、安裝程式和解除安裝程式共用的模型準備、下載、更新與現代介面 |
| `BigBlueFish.Windows/Companion/CompanionMaintenance.cs` | Windows 設定中的安裝模型、檢查更新與更新提示 |
| `tests/InstallerVerification/` | 下載故障、模型安裝互動、更新流程和完整的安裝與解除安裝驗證 |
| `tests/RunnerVerification/` | 目前的提示詞、真實外部程式的故障恢復和本機模型驗證 |

## 地區資料

中國的地區層級與中心座標以 MIT 授權的 [AreaCity](https://github.com/xiangyuecn/AreaCity-JsSpider-StatsGov) `2025.251231.260403` 版為基礎，包含 34 個省級單位、392 個市級單位、3209 個縣區級單位，臺灣省縣市的座標結合 GeoNames 的縣市政府所在地座標補齊。美國的州郡資料來自美國人口普查局 2026 Gazetteer；全球其他地點的城市、行政區與座標來自 GeoNames，國家的中文名稱來自 Unicode CLDR 47。國家「臺灣」的縣市與鄉鎮市區清單（`taiwan-regions.json`）由 `tools/import-taiwan-regions.py` 產生，鄉鎮市區中心由 `tools/fill-taiwan-township-coordinates.py` 依內政部國土測繪中心的鄉（鎮、市、區）界線計算。來源、輸入檔的檢查碼與授權見地區資源中的來源記錄和[第三方聲明](THIRD-PARTY.md)。

本版不修改原版的地區資料檔：選單與天氣文字顯示的繁體地名來自 `assets/regions/region-display-names.json`，由 `python3 code/tools/make-region-display-names.py` 產生（只需要 Python 3.9 以上）。轉換方式是 OpenCC 1.4.2 的 s2tw，不套用 TWPhrases，所以只換字形、不換譯名；人工審過的例外寫在腳本的 `OVERRIDES`，加上 `--review` 會列出所有改到「繁體也合法的字」的地名。地區資料檔更新後要重跑腳本。

更新中國的地區資料時，下載對應的 `ok_data_level3.csv`、`ok_geo.csv`，執行：

```powershell
python ./tools/import-regions.py "ok_data_level3.csv" "ok_geo.csv"
```

腳本只保留行政區劃和中心點；中國大陸的中心點由 GCJ-02 近似轉換為 WGS84。更換版本時，請一併更新腳本中的版本、提交和授權資訊。

## Windows（只確認可以編譯）

以下是原作者的 Windows 開發流程，本版保留這些腳本，但只在 Mac 上用 .NET SDK 確認 Windows 專案可以編譯，沒有在 Windows 上執行、測試或產生安裝程式。在 Mac 上做編譯檢查時，要加上 `-r win-x64 -p:Platform=x64 -p:EnableWindowsTargeting=true`。

需要 Windows x64、.NET 10 SDK，以及第一次還原 NuGet 相依套件時的網路連線。

```powershell
./build.ps1 -Locked -InputMode Ocr
./run.ps1 -InputMode Ocr -RuntimeDirectory "推論執行環境資料夾" -ModelsDirectory "GGUF 模型資料夾"
```

預設可以沿用本機 `C:\Program Files\藍色大肥魚` 的執行環境，也支援 `BLUE_WHALE_RUNTIME`、`BLUE_WHALE_MODELS`、`BLUE_WHALE_DOTNET` 環境變數。執行腳本透過資料夾連結沿用模型，不會把模型提交進儲存庫。SDK 可以用 `-SdkPath` 指定。

準備內建的 4B 本機模型時，可以執行下列任一種指令。`DestinationDirectory` 是模型準備資料夾，腳本會在其中建立 `models` 並儲存 `model-selection.json`；兩個來源的模型有些不同，但都適合本程式。

```powershell
../tools/Setup-Model.ps1 -Source HuggingFace -DestinationDirectory "D:\BigBlueFish-local"
../tools/Setup-Model.ps1 -Source ModelScope -DestinationDirectory "D:\BigBlueFish-local"
../tools/Setup-Model.ps1 -Source Manual -ModelFile "D:\下載\我的模型.gguf" -DestinationDirectory "D:\BigBlueFish-local"
```

Hugging Face 是推薦的來源，ModelScope 是中國大陸鏡像。手動安裝會保留檔案的實際名稱並檢查 GGUF 檔頭；SHA-256 不屬於兩個預設模型時，會在提示確認後仍然可以使用。自動化呼叫可以用 `-AllowUnverifiedModel` 明確接受這個提示。OCR 版的模型準備只下載主模型。

從原始碼啟動時，會讀取模型資料夾上一層的 `model-selection.json`，依選擇記錄沿用實際的檔名，也能辨識兩個來源的標準檔名。以上述資料夾為例，執行 `./run.ps1 -InputMode Ocr -ModelsDirectory "D:\BigBlueFish-local\models" -RuntimeDirectory "推論執行環境資料夾"`。切換資料夾時，腳本會更新建置資料夾中的連結，保留原本的模型資料夾和設定。

無模型啟動用 `./run.ps1 -InputMode Ocr -NoModel`，這時不需要模型檔或推論執行環境。也可以先執行 `../tools/Setup-Model.ps1 -Source None -DestinationDirectory "D:\BigBlueFish-local"` 儲存無模型的選擇，再用對應的 `-ModelsDirectory` 啟動。內建模型可以暫時不用，歡迎與道別、動畫和滑鼠互動照常運作。

OCR 版的輸出資料夾是 `BigBlueFish.Windows/bin/x64/Release/net10.0-windows10.0.26100.0/`；視覺版是 `BigBlueFish.Windows/bin/x64/Release/net10.0-windows7.0/`。主程式 `BigBlueFish.exe` 與 `BigBlueFish.dll` 由原始碼產生。OCR 版的相依套件鎖定在 `packages.lock.json`，視覺版使用 `packages.vision.lock.json`。

驗證與發布前，先關閉桌寵，再執行：

```powershell
./test.ps1 -InstalledDirectory "C:\Program Files\藍色大肥魚" -InputMode Ocr
../tools/Build-Installer.ps1 -InputMode Ocr -RuntimeDirectory "已核對的執行環境資料夾"
```

預設的測試包含 Runner 故障恢復與目前提示詞的驗證、動畫權重和日期規則、真實的前景視窗擷取、WPF 動畫與設定介面的驗證。實際視窗的測試使用 `.verification/` 的獨立設定，沿用參考安裝資料夾的模型和執行環境。安裝程式驗證專案的 `--lifecycle` 檢查一般使用者的寫入權限、Windows 註冊、捷徑和兩種資料保留方式的解除安裝；`--ui-layout <資料夾>` 檢查安裝、解除安裝、模型與更新視窗的版面；`--deployment <資料夾>` 檢查模型選擇、下載恢復、更新中繼資料與失敗恢復；`--model-ui <資料夾>` 驗證下載失敗的出口與手動安裝的確認。

發布腳本產生 `BigBlueFish-TW-v<版本>-OCR-Setup.exe` 一鍵安裝程式；Vision 的建置入口保留給歷史維護，不隨公開的 Release 一起發布。安裝程式包含附 .NET 執行環境的 Windows x64 程式，以及已核對的 llama.cpp `b10809` Vulkan 執行環境；模型可以從 Hugging Face 或 ModelScope 的固定來源下載並核對，也支援手動安裝或無模型啟動。模型選擇記錄在安裝資料夾的 `data/model-selection.json`。

根目錄的 `Version.props` 統一管理專案版本。主程式、安裝程式和解除安裝程式的產品版本由它產生；Windows 的檔案版本採用對應的四段格式：去掉 `-` 之後的標記再補上 `.0`，例如 `1.1.5-tw.1` 的檔案版本是 `1.1.5.0`。建置腳本預設讀取這個檔案，也可以用 `-Version` 指定建置版本。`Version.props` 也依原作者 1.1.4 把 `CetCompat` 設為 `false`，讓 Windows 的主程式、MCP 橋接程式、安裝程式和解除安裝程式關閉 CET 相容標記，避免部分沒有更新的舊版 Windows 一啟動就結束；這個設定只影響 Windows 的執行檔。發布出來的 Windows 執行檔可以用 `python3 tools/Verify-WindowsHosts.py <exe 路徑…>` 檢查是否已關閉 CET。

專案提供 Vision 與 Ocr 兩種輸入路線，預設建置 Ocr，用 `-InputMode Vision` 建置視覺版。Vision 直接把前景截圖交給多模態模型；Ocr 辨識前景視窗的文字，依位置整理成文字再交給本機模型。`tools/Build-LocalVariant.ps1 -InputMode Ocr -SdkPath <SDK 絕對路徑>` 產生自包含的程式，改成 Vision 就能建置視覺版；`code/build.ps1` 和 `code/run.ps1` 也接受 `-InputMode`。

## Linux 與無MCP版

Windows、Linux 和 macOS 都有標準版與無MCP版。無MCP版用 `CompanionEdition=Safe` 編譯，排除控制服務、工具執行端和對話輸入視窗，安裝程式與套件不散布 MCP 橋接程式、Hermes 橋接程式和 Control 程式庫（`BigBlueFish.Control.dll`）；`Safe` 是建置和檔名中的版本類型標記。

Windows 安裝程式執行 `../tools/Build-Installer.ps1 -Edition Standard -RuntimeDirectory "Windows 執行環境資料夾"`，把 `Standard` 改成 `Safe` 就能產生無MCP版。直接建置 Windows 無MCP版用 `dotnet build ./BigBlueFish.Windows/BigBlueFish.Windows.csproj -c Release -p:Platform=x64 -p:CompanionEdition=Safe -p:RestoreLockedMode=true`。

Linux 版使用 Avalonia、GStreamer 與 Tesseract。需要 .NET 10 SDK，以及含 gcc、pkg-config、libdbus-1-dev、dpkg-deb 和 FFmpeg 的 WSL Ubuntu／Debian 建置環境；FFmpeg 用來在打包時產生圖示。執行 `../tools/Build-LinuxPackage.ps1 -Distribution "WSL 發行版名稱" -RuntimeDirectory "Linux llama.cpp 執行環境資料夾" -Edition Standard`，同樣可以用 `-Edition Safe`。輸出是自包含 .NET 的 linux-x64 程式與 `.deb` 套件（`bigbluefish-tw`、`bigbluefish-tw-safe`），系統相依套件由 apt 安裝。本版沒有實際產生或測試 Linux 套件。

Linux 的主要入口在 `BigBlueFish.Linux/`，跨平台的控制通訊協定在 `BigBlueFish.Control/`，stdio 橋接在 `BigBlueFish.Mcp/`。共用專案分別鎖定 Windows、macOS 和 Linux 的 RID 相依套件；Windows 無MCP版另外使用 `packages.safe.lock.json`。Linux 的使用者資料夾、X11 截圖、Wayland 入口及更新行為見原作者的[功能規格](../docs/AGENT_FUNCTIONAL_SPEC.md)。

`tests/McpProtocolVerification/` 驗證真實的 stdio 橋接與通訊協定，`tests/McpUiVerification/` 驗證 Windows 的 MCP、視窗焦點、確認互動與拖曳，`tests/LinuxVerification/` 驗證 Linux 的實際視窗與 MCP；`tests/SafeEditionVerification/` 檢查 Windows 無MCP版的排除結果。這些測試需要分別用對應的平台與版本類型建置。
