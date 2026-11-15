<!-- Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0). -->
# 藍色大肥魚內部工具參考

標準版透過 ACP 連線本機 CLI Harness，並在建立或恢復專用對話時自動提供本工具服務。使用者操作見[連線 Agent](ACP.md)。魚魚以每次執行時隨機命名、只限目前使用者的管道接收控制（macOS 上是 `/tmp` 底下的 Unix socket），隨附的 stdio 橋接程式用 `--pipe <本次管道名稱>` 連線。管道設定由程式產生，不需要手動加入全域的 Harness 設定。無MCP版在編譯時就排除了這些工具。

macOS、Windows 與 Linux 使用同一份工具清單與事件；平台之間的差異寫在各工具的說明裡。`find_on_screen` 與 `walk_to` 只有 Windows 與 macOS 版提供，Linux 版的 `tools/list` 不列出這兩個工具，呼叫時回覆未知工具。

## MCP 介面（外部客戶端）

除了內建的 Agent 連線，也可以開啟「開啟 MCP 介面」，讓 Hermes 橋接程式等本機的 MCP 客戶端連線。開關在 Windows 版設定的「Agent 與 MCP」頁，macOS 與 Linux 版在「連線 Agent」頁下方的「MCP 介面（外部 Agent，例如 Hermes 橋接程式）」。這些客戶端直接執行 `BigBlueFish.Mcp`（Windows 是 `BigBlueFish.Mcp.exe`，macOS 在 `藍色大肥魚.app/Contents/MacOS/` 裡），不帶參數，連到依使用者帳號計算的固定管道（`ControlTools.PipeName`；macOS 上是 `/tmp/bbf-<20 碼>`）。Windows 的「複製 MCP 連線設定」按鈕會複製對應的 `mcpServers` 設定，macOS 與 Linux 的「複製 Hermes 的 MCP 設定」按鈕會複製 Hermes `config.yaml` 的 `mcp_servers` 片段。

開啟前會顯示隱私提示，只有使用者能開關，`get_settings` 只讀得到 `McpEnabled`。兩條連線使用同一份工具清單與事件，可以同時開啟：MCP 介面關閉時，外部客戶端的呼叫會回錯誤；`get_operation_guide`、`get_wakeups`、`schedule_wakeup`、`cancel_wakeup` 只供內建的 Agent 連線使用，經 MCP 介面呼叫一律回錯誤，Agent 連線關閉時也會回錯誤。提線木偶模式（`PuppetMode`）由兩者共用，經 MCP 介面開啟時會顯示「開啟提線木偶模式」的確認；另一邊也開著時，確認視窗會註明兩邊共用。提線木偶模式已經開著時再開啟 Agent 連線，會再請使用者確認讓 Agent 接管日常關心與動作。關閉 Agent 連線會清除該 Harness 的關心預約，即使 MCP 介面仍維持提線木偶模式。

雙擊輸入**只交給一方**：使用者開啟「雙擊輸入交給 MCP 介面（Hermes 橋接程式）」（`McpChatEnabled`），而且 MCP 介面至少有一個接收互動事件的客戶端時（呼叫過 `logging/setLevel` 設為 `debug` 或 `info`，或訂閱了 `bigbluefish://interactions`，例如 Hermes 橋接程式；只呼叫工具的客戶端不算），雙擊魚魚會打開輸入框，送出的文字與附件只以 `user_message` 交給 MCP 介面的客戶端，內建的 Agent 連線收不到；否則輸入只交給內建的 Agent 連線，MCP 介面的客戶端收不到 `user_message`。點選、拖曳等其他事件兩邊都會收到。

## 通訊協定與結果

橋接程式透過 stdin／stdout 傳遞逐行的 UTF-8 JSON-RPC 2.0，診斷訊息只寫到 stderr。初始化協商支援 `2025-03-26` 與 `2025-11-25`，送出 `notifications/initialized` 後開始呼叫工具。支援 `ping`、`tools/list`、`tools/call`、記錄層級、資源的讀取與訂閱，以及取消。魚魚不會透過 sampling 請求模型。

一般的成功結果是 MCP 的 `content` 陣列，第一項為 `type:text`，其 `text` 是 JSON 字串。工具失敗時使用 `isError:true` 和說明文字；JSON-RPC 方法、資源和初始化錯誤使用通訊協定的 error。呼叫端先檢查錯誤，再解析第一項的文字。`displayed`、`started`、`pending`、`queued` 表示提交狀態，不能據此判斷主人已經看見，或動作已經播放完畢。

截圖結果的第一項文字包含目標、身分、寬高與擷取時間，第二項是 `type:image`、`mimeType:image/png` 和 base64 的 `data`。影像在記憶體中取得（macOS 由系統的 `screencapture` 先寫到暫存資料夾，讀入後立刻刪除）。前景截圖遵循魚魚原有的前景視窗選擇規則；Windows 的全螢幕截圖包含所有顯示器，macOS 的 `capture_screen` 只截主螢幕（需要「螢幕與系統錄音」權限），Linux 由 X11 或 Wayland 入口（portal）擷取，入口的授權由桌面環境處理。

明確呼叫的截圖工具一律回傳圖片，結果不會因為 ACP 的 `promptCapabilities.image` 改成 OCR 文字。這個能力欄位只影響魚魚主動交給 Agent 日常關心時用哪種輸入形式。

## 設定

`get_settings` 回傳 `settings` 與 `state`。settings 是會儲存下來的欄位與系統啟動設定；state 是視窗是否顯示、隱藏原因、目前動作、播放階段、動作佇列、待說文字、工具連線、輸入框、Agent 狀態和模型狀態的快照。`modelAvailable` 表示有模型檔，不等於推論程式已經載入。`connections` 是已初始化的客戶端總數，`mcpClients` 是其中經 MCP 介面連線的外部客戶端。各版本的 state 也有 `bubbleVisible`、`externalBubble`（目前氣泡來自 `say` 或 `say_next`）、`speechPending`（魚魚正在準備自己的說話或天氣）、`fullscreen`、`idleSeconds`、`activeMinutes`、`lastBreakMinutes`；Windows 與 macOS 版另有 `walk_to` 的 `walk`、`tour`、`lastWalk` 與 `walkBubble`；macOS 與 Linux 版另有 `platform`（`macos` 或 `linux`）。

`update_settings` 的參數為 `{"settings":{...}}`，省略的欄位保留，欄位名稱區分大小寫，會先驗證整份修改。`AgentEnabled`、`AgentHarnessId` 只能讀取，由使用者在連線頁選擇。`McpEnabled` 與遠端模型設定（`ModelSource`、`RemoteUrl`、`RemoteModel`、`ScreenImages`）同樣只能讀取；遠端模型的 API Key 與中央氣象署授權碼由使用者在設定中填寫（macOS 存在鑰匙圈，Windows 以目前的 Windows 帳戶加密），不會出現在 `get_settings`，也不能透過 `update_settings` 寫入，被拒絕的欄位只列出名稱、不回傳數值。寫入唯讀欄位時，各平台都回覆同樣的原因（例如「模型來源與遠端模型只能由使用者在設定中修改。」），只有真的不存在的欄位才回覆「未知設定：」。以下是可以寫入的欄位：

| 欄位 | 類型與範圍 | 意義 |
| --- | --- | --- |
| ModelEnabled | bool | 設定頁的「啟用模型說話」：允許魚魚用內建或遠端模型自己說話與播報天氣；不限制外部的 `say` |
| GpuEnabled | bool | 本機推論使用 GPU |
| PublicInfo | bool | 內建的天氣與科技新聞 |
| ForegroundEnabled | bool | 內建模型讀取螢幕，以及 Agent 日常關心附上的畫面；不控制明確呼叫的截圖工具 |
| StopModelOnBattery | bool | 拔掉電源時暫停內建模型並釋放記憶體 |
| HideOnFullscreen | bool | 其他程式全螢幕時自動隱藏 |
| ShowOnAllSpaces | bool | 只有 macOS 版：設定頁的「在所有桌面空間顯示桌寵」；其他平台不會列出，寫入時回覆「ShowOnAllSpaces 只有 macOS 版提供。」 |
| CheckUpdatesOnStartup | bool | 啟動時檢查更新 |
| ChatLogShowAll | bool | 輸入框的聊天記錄是否顯示魚魚說過的所有話；false 時只列出主人的訊息與前後相鄰的魚魚話語。目前只有 Windows 版的輸入框有聊天記錄 |
| WeatherRegionCode | 字串 | 內建地區表中最下層地點的代碼：中國為六位區縣代碼（例如 340104；包括原版清單的臺灣省各區，例如 710101），少數直轄縣級行政區為九位代碼（例如 419001000）；國家「臺灣」為鄉鎮市區代碼 TW-三位資料集編號-兩位序號（例如臺北市中正區 TW-061-01）；美國為 US- 加五位郡 FIPS 代碼（例如 US-06037）；其他國家為國家代碼-GeoNames 城市 ID（例如 JP-1850147）。格式為 `^([0-9]{6}\|[0-9]{9}\|TW-[0-9]{3}-[0-9]{2}\|[A-Z]{2}-[0-9]+)$`，而且必須是地區表裡的最下層地點；縣市、省、州或國家代碼與地名都不接受。新安裝和無效代碼使用 TW-061-01，已儲存的有效地點不會被改動。國家「臺灣」的鄉鎮在 macOS 與 Windows 版填了中央氣象署授權碼時使用氣象署的鄉鎮預報，沒有授權碼時（以及 Linux 版）改用 Open-Meteo 在鄉鎮市區中心的估計；中國清單的臺灣省沿用原版，以縣市為單位估算、使用北京時間 |
| Scale / BubbleScale | 0.5–2 的有限數 | 角色／獨立氣泡的倍率 |
| SpeechMinMinutes | 2、3、5、10、15、20 之一 | 分別對應 2–3、3–5、5–8、10–16、15–23、20–30 分鐘 |
| BubbleDurationIndex | 0–7 的整數 | 5、10、15、30、60、120、180 秒，或直到下一句 |
| AnimationIntervalSeconds | 30–300 的整數 | 一般隨機動作事件的等待秒數，等目前片段完整結束再切換 |
| BreakfastHour / LunchHour / DinnerHour | 0–23 的整數 | 本地時間的用餐中心小時 |
| Left / Top | -1000000–1000000 的有限數或 null | Windows 為邏輯座標，macOS 與 Linux 為視窗位置（Linux 是螢幕畫素）；受工作區範圍限制，null 不主動移動 |
| LastBrushDate | yyyy-MM-dd 的有效日期或 null | 每日刷牙動作的去重複日期 |
| StartupEnabled | bool | 目前使用者的開機時啟動（macOS 為登入時自動開啟） |
| PuppetMode | bool | 完全由外部接管；開啟時必須等待使用者確認 |
| AgentDailySpeech | bool | 日常關心要說的話交給內建 Agent |
| AgentRespondToClick | bool | 點選事件是否主動發起 ACP 輪次，預設關閉 |
| AgentRespondToDrag | bool | 拖曳結束是否主動發起 ACP 輪次，預設關閉 |
| McpChatEnabled | bool | 設定頁的「雙擊輸入交給 MCP 介面（Hermes 橋接程式）」：MCP 介面有接收互動事件的客戶端時，雙擊輸入只交給 MCP 介面，否則只交給內建 Agent 連線 |
| SitReminderMinutes | 0、30、45、60、90、120 之一 | 魚魚自己的久坐提醒，0 為關閉，預設 60 |
| MealReminder | bool | 用餐提醒，時間沿用三餐的小時 |
| LateReminder / LateReminderHour | bool；22、23、0、1、2 之一 | 深夜提醒的開關與開始的整點，預設凌晨 0 點 |
| BatteryReminder | bool | 低電量提醒（沒插電且電量剩 20%、10% 時） |
| RainReminder | bool | 下雨提醒（需要開啟 PublicInfo，6 小時內降雨機率 60% 以上） |
| DailyBriefing | bool | 由 Hermes 橋接程式在每天第一次坐下時講今日簡報；魚魚自己不講 |
| WelcomeBackMinutes | 0、30、60、120、240 之一 | 離開至少這麼久再回來時，由 Hermes 橋接程式說期間發生的事，0 為關閉 |
| EventReminderMinutes | 0、5、10、15、30 之一 | Google 日曆行程開始前幾分鐘由 Hermes 橋接程式提醒，0 為關閉 |

更新設定不會開啟、啟用或置頂設定視窗。視窗已經開著時，會保留頁面、捲動位置、視窗位置與狀態並重新整理。修改 GPU、模型、地區等欄位時，依原有邏輯取消或重新安排對應的工作；macOS 與 Linux 每次套用設定都會停止本機模型並重新安排計時。開啟關心觸發與動作接管的確認以非同步方式顯示，等待時角色仍可以拖曳；使用者取消時不套用這次的修改。檔案或系統啟動設定寫入失敗可能發生在記憶體中的設定已經變更之後，呼叫端請先讀取狀態再決定下一步。

## 截圖、動作與文字

| 工具 | 輸入 | 行為 |
| --- | --- | --- |
| list_actions | 空物件 | 回傳 106 項精確的 action、pool、基礎 weight 與名義上的 duration_seconds |
| capture_foreground | 選填 max_dimension | 目前的前景截圖，預設最長邊 2560 |
| capture_screen | 選填 max_dimension | 虛擬桌面截圖（macOS 只截主螢幕），預設最長邊 4096 |
| play_action | action | 加入 FIFO 佇列，最多 100 項 |
| interrupt_action | action | 清空尚未播放的佇列，立即切換動作 |
| say | text，選填 transient | 立即逐字顯示原文，取代目前的氣泡並取消舊的本機生成；`transient=true` 表示進度或一時的提示，只顯示在氣泡，不寫進聊天記錄 |
| say_next | text | 覆蓋待說的原文，在下次符合日常節奏時顯示 |
| request_local_speech | 空物件 | 立即請魚魚自己的模型（本機模型或設定的遠端模型）依目前情境產生日常關心；需要 AgentDailySpeech=false 且模型可用 |

max_dimension 為 256–16384 的整數。action 填 `list_actions` 的名稱，最多 200 字元，Disabled 池的動作也可以指定。動作名稱是簡體字，寫成繁體也認得（例如逐字換成繁體，或換成台灣用詞的寫法），回傳的 `action` 是原本的簡體名稱；名稱不對時，錯誤訊息會列出最接近的三個名稱。重複的動作不去重複；隱藏或拖曳時，排隊的動作會等待。外部動作播完後，一般模式恢復自主排程，關心觸發與動作接管模式回到固定待機。立即動作和說話不會自動讓被隱藏的角色重新顯示。

text 不能全為空白，最多 4000 個 UTF-16 單元，以每秒約 28 個組合字元的速度顯示，保留原文，不解析 Markdown，也不經過本機模型的文字清理。台灣版顯示前會把其中的簡體字轉成台灣繁體，工具回傳的 `text` 仍是原文。外部原文不依賴本機模型、GPU 或插電。`say` 不會清除 `say_next`；關閉連線或結束程式時清除待說的原文。`say_next` 遵守一般的可見性、設定視窗、倒數計時、拖曳與閒置條件，關心觸發與動作接管模式會暫停這個節奏；要立即說話請用 `say`。

## 使用者狀況、找畫面與走過去

`get_context`（各版本都有）沒有參數，唯讀，不截圖也不連網。回傳 `summary`（已換算好的一段說明）、`localTime`、`weekday`、`idleSeconds`、`activeMinutes`（這次連續使用幾分鐘，不在電腦前時為 null）、`lastBreakMinutes`、`fullscreen`、`power`（`onAC`、`batteryPercent`，沒有電池時為 null）、`foreground`、`weather`（魚魚快取的天氣原文）、`openFolders` 與 `lastReminder`（魚魚最近一次的主動提醒）。Linux 在 Wayland 桌面上只有 GNOME 會提供閒置時間；其他 Wayland 桌面上 `idleSeconds`、`activeMinutes` 與 `lastBreakMinutes` 為 null，另有 `idleNote` 說明原因，魚魚自己的提醒只依時間判斷，也不會送出 `user_returned`。

- `foreground`：使用者關閉 ForegroundEnabled 時為 null。Windows 與 Linux 為 `{process, title}`；macOS 為 `{process, bundleId, title}`，前景是桌面時為 Finder／「macOS 桌面」，前景是沒有視窗的選單列 App 時給該 App 名稱、標題為空字串，螢幕鎖定時是 `loginwindow`，沒有「螢幕與系統錄音」權限時其他 App 的 `title` 為 null。
- `openFolders`：畫面上開著的檔案總管（macOS 為 Finder）資料夾，`folder` 是完整路徑、`window` 是視窗標題，最上層的排第一且 `front=true`；縮到 Dock 的視窗和「最近項目」「AirDrop」這類不是資料夾的視窗不列。macOS 需要「自動化 › Finder」權限，`openFoldersNote` 說明清單為空的原因（例如還沒允許權限、使用者拒絕或逾時），其他情況為 null。Linux 為空陣列。

`find_on_screen`（Windows 與 macOS）輸入 `query`（必填，可以用 `|` 分隔多種說法）、選填 `where`（all、desktop、windows）與 `limit`（1–50）。依名稱找出現在看得到的桌面圖示（附對應檔案的 `path`）、視窗標題與視窗內的介面元素（Windows 用 UI Automation，macOS 用「輔助使用」），依閱讀順序回傳中心點 `x`、`y` 與 `width`、`height`，可以原樣交給 `walk_to`。`kind` 為 `desktop_icon`、`window` 或 `element`，Finder（Windows 為檔案總管）視窗和它裡面的元素附 `openFolder`；`unfinished` 列出來不及讀完的視窗，`similar` 列出名稱相近的結果，`note` 提醒下一步該怎麼做。macOS 沒有「輔助使用」權限時只比對視窗標題，`note` 會說明；Dock 項目以 `kind=element`、`window="Dock"` 出現。

`walk_to`（Windows 與 macOS）讓魚魚跑到目標旁邊停下，不遮住目標：目標用 `x`、`y`（可以加 `width`、`height`）或 `desktop_item` 指定，`space=screen` 為螢幕座標，`space=capture` 為本服務截圖的影像座標（可以附 `capture_id`、`image_width`、`image_height`）；`stops` 可以依序跑過最多 20 站，每站可以有自己的 `label` 與 `pause_seconds`。選填 `side`、`arrive_action`、`say`、`label` 與 `wait`。使用者拖曳或點選、`interrupt_action`、新的 `walk_to`、隱藏、全螢幕、開啟設定、修改位置與大小、切換提線木偶模式、關閉 MCP、螢幕配置改變或結束程式，都會讓魚魚停在當下的位置，結果的 `cancelled` 說明原因（click、drag、interrupt、replaced、hidden、settings、puppet_mode、mcp_disabled、display_changed、exit 等）。行走中 `state.animationPhase` 為 WalkStart、WalkLoop 或 WalkStop，`state.walk`、`state.tour` 為進度，`state.lastWalk` 為最近一次的結果。

座標單位：

| 平台 | `find_on_screen` 結果、`walk_to` 的 `space=screen`、截圖的 `screen` 與 `pet` | `coordinateSpace` |
| --- | --- | --- |
| Windows | 實體畫素的虛擬桌面座標（與 UI Automation 相同） | `physical` |
| macOS | macOS 全域座標點（主螢幕左上角為原點，與「輔助使用」及 CGWindowList 相同；Retina 螢幕上一點是兩個實體畫素） | `macos-points` |

Windows 與 macOS 的截圖結果第一項的 metadata 另含 `captureId`、`screen`（影像在螢幕上的來源矩形）、`scale`（影像畫素 ÷ 座標單位，macOS Retina 約為 2）、`coordinateSpace` 與 `pet`（魚身矩形）；螢幕 X ＝ `screen.x` ＋ 影像 X ÷ `scale`，Y 同理。`walk_to` 的 `arrive_action` 也接受繁體動作名稱。

## 操作指南與預約

`get_operation_guide` 沒有參數，回傳工作資料夾中已安裝的技能全文、人設與對應的檔案路徑。檔案內容以使用者實際儲存的版本為準。

`get_wakeups` 沒有參數，回傳目前 Harness 與對話的預約，以及預約機制是否啟用。

`schedule_wakeup` 輸入：

```json
{
  "purpose": "看看主人是否適合休息一下",
  "delay_seconds": 600,
  "repeat_seconds": 600
}
```

purpose 為 1–1000 字元；delay_seconds 與選填的 repeat_seconds 為 30–604800 的整數秒。省略 repeat_seconds 或填 null 表示只預約一次。可以傳入最多 100 字元的既有 id 修改目前對話的預約；新預約會回傳自己的 id、Harness、對話、用途、到期時間和週期。全部最多 32 項。

建立預約需要連線已就緒，並開啟關心觸發與動作接管。切換由誰編寫日常要說的話時，預約會保留；關閉連線、離開關心觸發與動作接管，或開始新對話時，會清除目前 Harness 的預約。預約不要求 Agent 在一輪內持續等待，目前這一輪可以結束。

AgentDailySpeech=true 時，到期會透過 ACP 向同一個對話送出 wakeup，附上預約用途；false 時，到期由魚魚直接呼叫自己的模型產生日常關心。隱藏、拖曳或未連線時會延後；多個到期的預約會合併。一次性預約在送出前移除，重複的預約從這次送出的時間重新計時。Agent 可以檢查、取消或修改之後的預約。

`cancel_wakeup` 輸入 `{"id":"預約編號"}`，只取消目前 Harness 對話中的對應預約，回傳是否已取消。

## 事件與輸入

點選、拖曳開始與結束，以及使用者輸入，會形成含 sequence、UTC time、kind、detail 的事件。最近 128 筆只存放在控制服務的記憶體中，URI 為 `bigbluefish://interactions`。`resources/read` 回傳目前的陣列，不會清空；`resources/subscribe` 透過 `notifications/resources/updated` 告知有變化，呼叫端再自行讀取。

info 層級的 `notifications/message` 使用 logger `bigbluefish.interaction`，可以用 `logging/setLevel` 調整。通知和資源是同一事件的兩種取得方式，依服務執行個體與 sequence 去除重複。超過佇列容量的舊事件會被覆蓋，服務關閉時釋放事件記錄。

雙擊輸入交給內建 Agent 時，直接透過 ACP 發起 user_message，內容是主人送出的原文。Agent 的一般文字不會進入氣泡，回覆要透過 `say` 完成。連線還沒就緒時，傳送按鈕會停用並保留草稿。點選和拖曳是否另外發起 Agent 輪次，由 AgentRespondToClick 與 AgentRespondToDrag 兩個欄位決定。

雙擊輸入框可以用附加按鈕、拖曳或貼上（macOS 是 ⌘V，Windows 與 Linux 是 Ctrl+V）加入最多 10 個圖片、影片或檔案；貼上的圖片會先存成 PNG（macOS 在資料夾的 `attachments/`，Windows 在 `%LOCALAPPDATA%\BigBlueFish\attachments`）。`user_message` 事件的 `detail` 另有 `attachments`，每項含 `path`（本機絕對路徑）、`name`、`kind`（image、video、file）、`mimeType`、`size` 與 `source`（`picker`、`drop`、`clipboard-files` 或 `clipboard`），只有路徑、不含檔案內容；沒有附件時省略這個欄位。交給內建 Agent 時，每個附件另外以 ACP 的 `resource_link`（`file://` URI）附在同一則訊息裡；Agent 支援圖片輸入時，圖片也直接以 `image` 區塊附上（必要時縮小到長邊 1568 像素），Agent 拒收圖片時改成只送連結重試一次。文字的 4000 字上限只算使用者打的字，不含附件。兩邊都不能接收時，傳送按鈕停用並保留草稿與附件。外部 Agent 的氣泡還在顯示時，魚魚不會自己開口把它蓋掉。

各版本都有 `user_returned` 事件：使用者離開 5 分鐘以上再回到電腦前時推送，`detail` 含 `breakMinutes`、`since`、`at` 與 `watching`（離開期間大多開著全螢幕程式，可能只是在看影片）。電腦睡眠的時間也算在離開時間裡。這個事件只給 MCP 客戶端，不會讓內建 Agent 開始新的一輪；Hermes 橋接程式依它和 `DailyBriefing`、`WelcomeBackMinutes` 決定要不要說今日簡報或期間發生的事。

## 生命週期

關閉連線或結束程式時，會取消工具請求和確認等待、結束 ACP 啟動的 Agent 程式，並釋放內部服務。請求可以透過 `notifications/cancelled` 取消；工具在執行前及非同步工作之後都會檢查是否已取消。動作和文字已經提交到介面之後的副作用不會自動復原。多個工具請求可以同時等待，介面上的實際控制由同一個排程器執行。

Agent 自行儲存對話和處理模型請求；輸入、截圖或工具結果可能依它的設定傳送給雲端模型。魚魚的連線告知涵蓋它自己的控制工具；一般的檔案、終端機等 Agent 操作，仍依對應 Harness 的權限請求處理。
