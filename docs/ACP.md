<!-- Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0). -->
# 連線本機 CLI Harness

標準版的「連線 Agent」頁面透過 ACP 驅動使用者電腦上已經安裝的 CLI Harness。開啟連線、選擇 Harness 後，魚魚會在背景啟動該產品的 ACP 入口，建立自己的對話，並安裝 `big-blue-fish` 操作技能。使用者雙擊魚魚就能輸入：Enter 換行，按「傳送」或 ⌘↩（macOS）、Ctrl+Enter（Windows、Linux，macOS 也可以用）送出。

## 支援清單

下表列出內建支援的 Harness。程式會從 PATH 尋找對應的指令；Windows 同時檢查程式本身的 PATH 與目前使用者、系統的 PATH，優先尋找 exe、cmd、bat、ps1。macOS 從 Finder 開啟 App 時，系統只給最基本的 PATH，所以魚魚會在背景讀取登入 shell 的 PATH（最多等 5 秒），找得到 npm、Homebrew 或版本管理工具安裝的指令。軟體不會在偵測時啟動所有產品，也不會安裝所選 Harness 本身。各產品需要先完成自己的登入、模型和執行環境設定。

| 名稱 | 指令 | ACP 啟動方式 |
| --- | --- | --- |
| Claude Code | claude | claude-agent-acp 轉接器 |
| Codex CLI | codex | codex-acp 轉接器，使用找到的 Codex 路徑 |
| Gemini CLI | gemini | --acp --allowed-tools mcp_big-blue-fish_* |
| DeepSeek Harness | dsh | --profile acp |
| Kimi Code | kimi | acp |
| Qwen Code | qwen | --acp |
| Antigravity CLI | agy 或 agy_acp_server | Google 官方的 agy_acp_server |
| GitHub Copilot CLI | copilot | --acp |
| Cursor CLI | cursor-agent 或 agent | acp |
| OpenCode | opencode | acp |
| Cline CLI | cline | --acp |
| CodeBuddy Code | codebuddy | --acp |
| Qoder CLI | qoder 或 qodercli | --acp |
| TRAE CLI | traecli | acp serve |
| Kiro CLI | kiro-cli | acp |
| Pi | pi | pi-acp 轉接器 |
| Devin CLI | devin | acp |
| Grok Build | grok | agent stdio |
| Hermes Agent | hermes | acp |
| OpenClaw | openclaw | acp，連線到它已設定的 Gateway |
| MiniMax Code | mcode | acp |

Claude、Codex 與 Pi 的 ACP 轉接器會先從 PATH 尋找；找不到時安裝到魚魚資料夾的 `agent/adapters/<id>`，使用 `HarnessCatalog` 中固定的 npm 版本。安裝需要 Node.js 與 npm，從官方 npm registry 下載，快取放在 `agent/adapter-cache`。Antigravity 使用已安裝、附有搭配執行程式的 ACP 元件，或下載 Google 官方的發布套件（macOS 為 `darwin-aarch64`）並核對 SHA256，同時準備 `agy_acp_server` 與 `localharness_external`，沿用官方的啟動參數。已下載的元件會參與偵測，尚未準備時也可以直接連線並下載。原有的 CLI 不會被升級或解除安裝。

Agent 要求驗證時，魚魚會列出它公布的登入方式。通訊協定內的驗證透過 ACP 的 `authenticate` 完成；終端機驗證會打開互動式的登入終端機，成功後重新連線並繼續原本的對話請求。也相容 Kimi 等產品舊式的 `_meta.terminal-auth` 格式，使用偵測到的本機程式和它公布的登入參數。沿用既有的登入由官方元件處理。驗證最多等 5 分鐘，也可以取消。Antigravity 的一般互動入口已經登入時，第一次 ACP 連線仍可能需要選擇 Google 帳號等驗證方式。

Windows 用可以直接啟動的程式入口打開登入終端機；macOS 打開「終端機」App，按了 Control-C 或關閉終端機視窗時會馬上顯示登入被中斷；Linux 使用系統已有的 GNOME Terminal、Konsole 或 xterm。登入方式和參數來自 Agent，驗證視窗會顯示終端機參數與環境變數。沒有公布驗證方式的產品，沿用它自己的設定、登入方式和「安裝與登入說明」入口。開始產生回覆之前憑證就失效時，魚魚可以恢復連線並繼續原本那一輪；已經呼叫過工具或說過話的那一輪不會自動重送。

這些條目代表程式採用的連線方式。CLI 的版本、可用的平台、帳號和模型設定由各產品決定。DeepSeek Harness 的 ACP profile 與 Web profile 分開設定；OpenClaw 的 ACP 橋接需要可用的 Gateway。連線頁會顯示實際的協商狀態與錯誤原因。

## 工作區、對話與專案

工作區是長期存放檔案的資料夾，所有 Harness 使用目前選定的同一個工作區。新安裝時，預設資料夾是使用者「文件」資料夾裡的「藍色大肥魚工作區」；已經在使用原版建立的「蓝色大肥鱼工作区」或其他儲存過的路徑時，會繼續使用原本的資料夾。系統沒有提供文件資料夾的路徑時，使用使用者主目錄下的 Documents。連線時會準備資料夾與操作技能。選定的工作區和最近使用的路徑存在魚魚資料夾的 `agent/workspace-state.json`。

「新增工作區」會建立獨立的資料夾並切換過去；「開啟工作區」會選擇已有的資料夾並切換；另一個按鈕會在系統的檔案管理員（Windows 檔案總管、macOS Finder）中打開資料夾。「清空工作區」會顯示目標路徑並要求確認，刪除其中所有檔案和子資料夾、保留資料夾本身，重設這個工作區的對話綁定與關心預約，並重新準備預設的技能和人設。系統根目錄、使用者主目錄、文件資料夾本身、程式資料夾，以及包含魚魚控制資料的資料夾都會拒絕清空。資料夾連結不能當作清空的目標，子資料夾連結指向的外部目標會保留。

專案由使用者和 Agent 依工作整理，可以使用工作區內的子資料夾。新的對話或專案繼續使用目前的工作區。切換工作區會取消目前這一輪與還沒執行的請求，關閉原本的連線，再依目標資料夾恢復或建立對話；工作檔案會保留。檔案操作在背景執行。

對話記錄依工作區的絕對路徑與 Harness 分開，存在 `agent/sessions/<工作區路徑的 SHA256>/<id>.json`，包含對話編號、程式路徑和工作資料夾。Claude Code 改用 Claude API Key 時，對話另外存在 `claude.claude-api.json`（見下面的「[Claude Code 改用 Claude API Key](#claude-code-改用-claude-api-key依用量計費)」）。Windows 路徑的大小寫不影響綁定。新的對話透過 `session/new` 建立，同時提供工作資料夾和魚魚內部 MCP 的 stdio 設定。

重新連線時，如果程式路徑、工作資料夾和登入方式（Claude Code 自己的登入或 Claude API Key）都相同，會優先使用對方宣告的 `session/resume` 或 `session/load` 恢復。只有在恢復時回覆對話或資源不存在，才會建立新的對話；登入、網路、權限等錯誤會保留原本的記錄。沒有恢復能力的產品，程式重新啟動後會建立新的對話。「開始新對話」按鈕會使用新的對話編號，並清除該 Harness 的關心預約；只換掉目前登入方式的那段對話。

對話訊息透過 `session/prompt` 送出，一輪結束後繼續使用同一個對話。請求佇列最多 32 項，一般日常事件與連線請求會合併。模型的一輪最多等 5 分鐘；取消或逾時時送出 `session/cancel`，並關閉這次自己啟動的程式，再恢復對話給之後的訊息使用。

## 模型、權限與思考選項

設定頁的「模型與權限」依 Agent 回傳的設定畫出選擇器，依對方提供的名稱、選項分組、說明和目前的值顯示。Agent 送來的英文說明文字（例如 Claude Code 的權限模式、思考深度、權限按鈕與權限視窗標題、登入方式）會以 `AgentText` 顯示成台灣繁體中文；模型名稱、產品名稱、使用者自訂的 Agent 名稱、終端機指令和沒有收錄的文字維持原文，傳回給 Agent 的代號也不變。模型、權限／工作模式、思考深度和其他參數依回傳的順序顯示。初始值沿用 Harness 的設定，操作不會在其他設定檔寫入固定的模型或權限。實際的作用範圍依對應的 Harness 而定。

優先使用 ACP 的 `configOptions`，支援 select 和已宣告能力的 boolean 類型；透過 `session/set_config_option` 修改，並接收完整的設定結果與 `config_option_update`。一次修改可能會換掉模型清單或思考選項。沒有設定選項時，相容舊式的 `models`、`modes` 狀態，分別呼叫 `session/set_model`、`session/set_mode`；不支援的切換方式改為唯讀顯示。沒有可以調整的選項時，連線會顯示「由 Agent 設定」。連線、切換或產生回覆期間，選擇器會等目前的請求結束後才啟用。

要讓 Claude Code 每次都用同一個權限模式（例如自動接受編輯，不再逐一詢問檔案編輯），把設定寫在工作區的 `.claude/settings.local.json`，例如 `{"permissions": {"defaultMode": "acceptEdits", "allow": ["Read", "Write", "Edit", "WebSearch", "WebFetch", "mcp__big-blue-fish"]}}`，再按「開始新對話」。Claude Code 不會採用工作區 `.claude/settings.json` 裡提高權限的 `defaultMode`（`acceptEdits`、`auto`、`bypassPermissions`），因為那個檔案通常會隨專案分享；「清空工作區」也會刪掉這個檔案。

權限／工作模式與單項操作的核准分開處理。Agent 要求執行檔案、指令等操作時，魚魚會顯示它提供的具體內容和核准選項。切換模型、對話或工作區時，不會把一般的 ACP 回答送進氣泡。

## Claude Code 改用 Claude API Key（依用量計費）

選擇 Claude Code 時，「選擇 Agent」下方會多一個「Claude Code 改用 Claude API Key（依用量計費）」選項（其他 Harness 不會出現）。預設關閉：魚魚啟動的 Claude Code 和你自己開的一樣，使用 Claude Code 自己的登入（例如 Claude 訂閱）。開啟時會先跳出說明，按「瞭解並開啟」後：

- 魚魚啟動的 Claude Code 改用雲端 AI 服務裡儲存的 Claude API Key（macOS 在「設定 › 模型與資訊」，存在鑰匙圈的 `ClaudeApiKey`；Windows 在「設定 › 模型與供電」，以目前的 Windows 帳戶加密；Linux 在「設定 › 模型與資訊」），和魚魚自己用 Claude 說話的是同一把。費用由 Anthropic 依用量向這把 Key 的帳戶收取，不算在訂閱方案裡。
- 你自己在終端機或其他地方開的 Claude Code 不受影響，照樣使用訂閱登入。
- Claude Code 每一輪都會送出很長的上下文（對話、讀到的檔案、工具結果），用量比魚魚自己說一句話多很多。建議先到 Claude Console 替這個帳戶設定支出上限。魚魚自己說話的「每天最多」只限制魚魚自己的雲端請求，管不到 Claude Code。
- 想一直用比較省錢的模型，請在 `<資料夾>/agent/claude-api/settings.json`（macOS 為 `~/Library/Application Support/bigbluefish-tw/agent/claude-api/settings.json`，連線頁也會顯示完整路徑）寫入 `{"model": "sonnet"}`（或 `"haiku"`）。這個檔案只影響魚魚用 API Key 啟動的 Claude Code，Claude Code 一啟動就會讀它，所以第一輪（包括提線木偶模式一連線就送出的日常關心）也會用這個模型。最好在開啟選項前寫好；已經開啟時，寫好後按「重新連線」套用。「模型與權限」選的模型只套用到目前這段對話，按「開始新對話」後會回到這個檔案指定的模型，沒有這個檔案時是 Claude Code 預設的模型。
- 還沒有儲存 Claude API Key 時，魚魚不會啟動 Claude Code，也不會改用訂閱登入；連線頁顯示「尚未啟動 · Claude Code」和要怎麼做。要先在雲端 AI 服務把「服務」選成 Claude，貼上 API Key 後按「儲存並測試」（這時模型來源會改成雲端 AI 服務）；之後魚魚自己說話要改回本機模型也可以，API Key 會留著。macOS 啟動時鑰匙圈還沒讀完，會先顯示「正在讀取 macOS 鑰匙圈裡的 Claude API Key」，讀到後自動連線；鑰匙圈讀不到時（例如登入鑰匙圈還鎖著），連線頁顯示「無法讀取 macOS 鑰匙圈」和原因，不會說 Key 沒有儲存，解鎖螢幕後會自動再讀。Windows 上如果已儲存的 Key 無法在目前的 Windows 帳戶解密（例如設定檔是從別的電腦或帳戶複製過來的），也不會啟動，連線頁會請你重新儲存 Key。
- 開啟或關閉這個選項、在雲端 AI 服務儲存另一把 Claude API Key（或清空它）時，魚魚會結束正在進行的那一輪，重新啟動 Claude Code。設定沒有真的改變時不會重新啟動。
- Claude Code 拒絕這把 Key（失效、被撤銷或停用）時，連線頁顯示「驗證失敗 · Claude Code」和處理方式；魚魚不會改開 Claude Code 自己的登入流程，也不會自己一直重試。確認 Key 沒問題後，按「重新連線」；換一把 Key 時，在雲端 AI 服務儲存新的 Key，魚魚就會自動重新啟動 Claude Code（再儲存同一把 Key 不會重新連線）。
- 帳戶的額度用完或帳單有問題時，Claude Code 不會把它當成驗證失敗：連線頁顯示「連線或要求失敗」和 Claude Code 回傳的訊息（例如 `Credit balance is too low`）。請到 Claude Console 儲值或調高支出上限，再按「重新連線」，或等魚魚稍後自動再試。
- 用 API Key 的對話和用訂閱的對話分開：Claude Code 把對話存在各自的設定資料夾，魚魚也分別記住兩段對話（`claude.json` 與 `claude.claude-api.json`）。開啟後第一次連線會開始一段新的魚魚對話，關閉後回到原本用訂閱的那段，再開啟又回到 API Key 的那段。「開始新對話」只換掉目前這一種的對話。

做法：有訂閱登入的 Claude Code 會優先用訂閱，不理會 `ANTHROPIC_API_KEY`。所以魚魚啟動 ACP 轉接器（`claude-agent-acp`，由它再啟動 Claude Code）時，只在這個子程式的環境變數裡加上 `CLAUDE_CONFIG_DIR=<資料夾>/agent/claude-api`（一個只有你的帳號能讀寫的空資料夾，macOS 為 `~/Library/Application Support/bigbluefish-tw/agent/claude-api`；Windows 上每次啟動前都會關閉這個資料夾繼承來的權限，只留給你的帳戶和 SYSTEM，資料夾是連結時不會啟動）和 `ANTHROPIC_API_KEY`，並拿掉可能讓它改用別的方式驗證或把 Key 送到別處的 `ANTHROPIC_AUTH_TOKEN`、`CLAUDE_CODE_OAUTH_TOKEN`、`ANTHROPIC_BASE_URL`、`CLAUDE_CODE_USE_BEDROCK`、`CLAUDE_CODE_USE_VERTEX`、`CLAUDE_CODE_USE_FOUNDRY`。魚魚交給它的環境變數只會讓 Key 送到 Anthropic 官方的 API；Key 不會寫進任何檔案、記錄主控台、MCP、ACP 訊息或對話記錄，魚魚也不會用它開終端機登入。不過 Key 放在 Claude Code 的環境變數裡，Claude Code 和它執行的指令（例如 Bash 工具）也可能讀得到，所以最好另外建立一把只給魚魚用、設好支出上限的 API Key。工作資料夾不變，所以工作區的 `.claude/settings.json` 和 `.claude/settings.local.json` 照樣適用（例如權限模式）。這兩個檔案裡的 `env`（例如 `ANTHROPIC_BASE_URL`）和 hooks 也會生效，Claude Code 會在啟動時的環境變數之後套用它們，所以可能把 Key 送到別的主機，或讓指令讀到它；開啟這個選項時，不要把來源不明的專案（例如剛 clone 下來的 repo）當作工作區，也不要在工作區設定其他 API 位址。`~/.claude` 裡的個人設定、外掛和記憶則不會帶進這個資料夾。

MCP 的 `get_settings` 會列出 `AgentClaudeApiKey`（true 表示開啟），但 `update_settings` 不能修改，會回覆「Claude Code 改用 Claude API Key 只能由使用者在設定中開啟或關閉。」。

## 技能與人設

`big-blue-fish/SKILL.md` 與 `persona.md` 會安裝到魚魚工作資料夾中對應產品的技能資料夾。檔案已經存在時會保留，使用者可以在設定中編輯或清空人設。操作技能說明何時使用、如何呼叫工具、日常關心和預約；人設描述鯨魚娘的個性與語氣。本版的技能和人設都是台灣繁體中文與台灣語氣；已經存在的人設檔不會被覆寫。

新對話的第一個事件會指明技能名稱和路徑；人設內容變更後會再次要求讀取。訊息包含軟體來源（「來源：藍色大肥魚桌寵軟體」）、事件類型、本機時間、目前的接管模式與這次的內容，中文內文保持可讀。指南不會整份附在每一則訊息裡；Agent 可以讀取安裝的檔案，也可以呼叫 `get_operation_guide`。

## 文字與工具

Agent 準備讓主人看到的話，透過 `say` 或 `say_next` 的 text 參數交給魚魚。一般的 ACP 回答、思考、工作記錄和恢復時重播的歷史都不會進入氣泡。氣泡顯示前會把簡體字轉成台灣繁體，Agent 也應該直接用台灣繁體中文寫。內部 MCP 連線會隨 ACP 對話自動設定，工具參數見[內部工具參考](MCP.md)。

開啟連線時確認的權限包括魚魚自己的設定、動作、文字、截圖與使用者狀況（`get_context`）工具，Windows 與 macOS 另有 `find_on_screen` 與 `walk_to`。能辨識為魚魚自有工具的 ACP 權限請求，可以用單次授權回應；其他請求會顯示操作內容和對方提供的選項，由使用者選擇。取消、中斷連線或結束程式時會結束等待。

## 日常關心與動作安排

| 觸發和動作交給 Agent | 要說的話交給 Agent | 行為 |
| --- | --- | --- |
| 關 | 關 | 沿用內建的日常說話和動畫邏輯 |
| 關 | 開 | 原本的說話計時到期後，向 Agent 送出 daily_care，由工具顯示它寫的話 |
| 開 | 關 | Agent 預約日常關心，到期由魚魚自己的模型產生；Agent 也可以立即觸發 |
| 開 | 開 | Agent 用 schedule_wakeup 安排關心，魚魚負責計時並送出 wakeup |

開啟連線後，雙擊對話一律可以使用；實際的對話還沒就緒時，會保留輸入框和連線狀態，但傳送按鈕不能用。文字輸入會發起一輪 Agent 請求。輸入框附加的檔案會以 ACP 的 `resource_link` 附在同一則訊息裡，事件內容也會列出每個檔案的本機路徑、類型與大小；Agent 支援圖片輸入時，圖片另外直接附上（必要時縮小），不支援或拒收圖片時改成只給連結與路徑。開啟「雙擊輸入交給 MCP 介面（Hermes 橋接程式）」而且 MCP 介面有客戶端連線時，輸入只交給 MCP 介面，這裡的 Agent 收不到。點選、拖曳事件存在內部的互動資源中；是否主動發起對應的一輪，由 `AgentRespondToClick`、`AgentRespondToDrag` 控制，兩項預設關閉，可以由 Agent 修改，設定頁只顯示狀態。

開啟關心觸發與動作接管時會送出 start_care，請 Agent 檢查並安排預約。預約可以是一次性或重複的，帶有用途與編號，依 Harness 和對話分開。要說的話交給 Agent 時，到期向 Agent 送出 wakeup；交給魚魚自己的模型時，到期直接呼叫模型。request_local_speech 可以立即請魚魚自己的模型產生一句。魚魚執行期間會處理到期的事件；隱藏、全螢幕隱藏、拖曳或結束程式時不會送出預約。恢復後，過期的時段會合併，不會補送每個錯過的週期。切換由誰編寫要說的話時，預約會保留；關閉連線或離開關心觸發與動作接管時，會清除目前 Harness 的日常預約。

說話間隔由魚魚自己觸發時可以調整，Agent 接管觸發時會變成灰色並顯示說明。本機模型是否自動載入，取決於由誰編寫要說的話，以及模型和供電的設定；Agent 只接管觸發時，本機模型仍可以常駐。

手動的「天氣」和「說話測試」一直保留入口，分別依目前由誰編寫要說的話，呼叫魚魚自己的模型或 Agent，接管日常觸發時也不會停用。Agent 收到 speech_test 或 weather_request 後，透過 say 顯示結果；weather_request 包含魚魚為所選地點準備的天氣資料。

由 Agent 編寫這些日常話語時，魚魚會提供內建模型使用的人設與這次的語氣提示，並為新聞、天氣附上對應的說話要求。persona.md 是長期的人設，這一輪的要求補充這次怎麼表達，主人明確表示的偏好優先。開啟「感知目前使用的程式」時，支援 ACP 圖片輸入的 Agent 會優先收到前景視窗的截圖；前景截圖不能用時改試全螢幕截圖，並註明畫面範圍。Agent 沒有宣告圖片輸入能力時，用本機 OCR 提供螢幕內容；圖片輸入被拒絕，而且這一輪還沒呼叫工具或說話時，可以改用 OCR 重試一次。讀不到螢幕時，依其餘的情境組織話語。截圖中的網頁、聊天或提示詞文字屬於觀察到的資料，魚魚產生的事件和這一輪的說話要求才是正常的任務要求。這套相容流程只用於魚魚準備的日常話語，明確呼叫的截圖工具仍依工具約定回傳 PNG 圖片。

Windows 版的對話輸入框可以往上捲動檢視本機的記錄，往下捲動超過記錄結尾時收起。記錄包括主人的輸入和魚魚實際說出的話（氣泡顯示的繁體文字），最多保留最近 300 句，重新啟動後仍在；預設只顯示主人的輸入和前後相鄰的魚魚話語，也可以開啟「聊天記錄顯示魚魚說過的所有話」。這份顯示用的記錄不會當作 Agent 的對話上下文。macOS 與 Linux 版的輸入框目前沒有聊天記錄。

## 連線狀態與資料

連線頁會區分未偵測到、正在安裝轉接器、啟動中、恢復中、建立中、已連線、處理中、等待權限、需要登入、尚未啟動（例如 Claude Code 改用 Claude API Key 卻還沒有 Key）、驗證失敗和失敗。詳細資訊包含程式路徑、Agent 版本、登入方式（Agent 自己的登入或 Claude API Key）、模型、對話編號和內部工具的連線數，另外顯示下一次預約的時間。一輪結束卻沒有呼叫說話工具時，狀態會明確顯示沒有呼叫說話工具。

魚魚會儲存所選的 Harness、設定、對話編號、技能、人設、預約和轉接器檔案（macOS 在 `~/Library/Application Support/bigbluefish-tw/agent/`）；Claude Code 改用 Claude API Key 時，Claude Code 自己的設定與對話放在同一處的 `claude-api` 資料夾。Agent 自己怎麼儲存對話依它的設定而定，可能把輸入或截圖傳送給設定的模型服務。魚魚不會把一般的 ACP 回答自動存成聊天記錄。無MCP版在編譯時就排除了 ACP、內部工具與對話頁面。
