# 蓝色大肥鱼内部工具参考

标准版通过 ACP 连接本机 CLI Harness，并在创建或恢复专用会话时自动提供本工具服务。用户操作见 [连接 Agent](ACP.md)。鱼鱼以当前进程随机命名的用户范围管道接收控制，随包 stdio 桥接程序使用 `--pipe <本次管道名>` 连接。管道配置由程序生成，无需手工加入全局 Harness 配置。

## MCP 介面（外部客戶端，Windows）

除了內建的 Agent 連線，Windows 版還可以在設定的「Agent 與 MCP」頁開啟「開啟 MCP 介面」，讓 Hermes 橋接程式等本機的 MCP 客戶端連線。這些客戶端直接執行 `BigBlueFish.Mcp.exe`，不帶參數，連到依使用者帳戶計算的固定管道（`ControlTools.PipeName`）；「複製 MCP 連線配置」按鈕會複製對應的 `mcpServers` 設定。開啟前會顯示隱私提示，只有使用者能開關，`get_settings` 只讀得到 `McpEnabled`。兩條連線使用同一份工具清單與事件，可以同時開啟：MCP 介面關閉時外部客戶端的呼叫會回錯誤；`get_operation_guide`、`get_wakeups`、`schedule_wakeup`、`cancel_wakeup` 只供內建的 Agent 連線使用，經 MCP 介面呼叫一律回錯誤，Agent 連線關閉時也會回錯誤。提線木偶模式（`PuppetMode`）由兩者共用，經 MCP 介面開啟時顯示「開啟提線木偶模式」的確認；另一邊也開著時，確認視窗會註明兩邊共用。提線木偶模式已經開著時開啟 Agent 連線，會再請使用者確認讓 Agent 接管日常關心與動作。關閉 Agent 連線會清除該 Harness 的關心預約，即使 MCP 介面仍保持提線木偶模式。

開啟「雙擊與魚魚對話」（`McpChatEnabled`）且至少有一個完成初始化的客戶端時，雙擊魚魚也會打開輸入框；送出的文字與附件以 `user_message` 交給 MCP 介面的客戶端，由客戶端自己決定怎麼回應。沒有開啟「雙擊與魚魚對話」時，MCP 介面的客戶端收不到 `user_message`：輸入框只在內建 Agent 連線開啟時出現，內容只交給內建的 Agent。Linux 版只有內建的 Agent 連線。

## 协议与结果

桥接程序通过 stdin/stdout 传递逐行 UTF-8 JSON-RPC 2.0，诊断只写 stderr。初始化协商支持 `2025-03-26` 和 `2025-11-25`，发送 `notifications/initialized` 后开始工具调用。支持 `ping`、`tools/list`、`tools/call`、日志级别、资源读取与订阅及取消。鱼鱼不通过 sampling 请求模型。

普通成功结果是 MCP `content` 数组，首项为 `type:text`，其 `text` 是 JSON 字符串。工具失败使用 `isError:true` 和说明文字；JSON-RPC 方法、资源和初始化错误使用协议 error。调用方先检查错误，再解析首项文本。`displayed`、`started`、`pending`、`queued` 表示提交状态，不能据此判断主人已看见或动作已播放完毕。

截图结果的首项文字包含目标、身份、宽高与截取时间，第二项是 `type:image`、`mimeType:image/png` 和 base64 `data`。图像在内存中取得；Windows 前台遵循鱼鱼已有前台选择规则，全屏包含全部显示器；Linux 由 X11 或 Wayland 门户捕获，门户授权由桌面环境处理。

显式截图工具始终返回图片，其结果不随 ACP 的 `promptCapabilities.image` 改为 OCR 文本。该能力字段只参与宿主向 Agent 主动投递日常关心时的输入形式选择。

## 设置

`get_settings` 返回 `settings` 与 `state`。settings 为持久化字段及系统启动项；state 为窗口可见性、隐藏来源、当前动作、播放阶段、动作队列、待说文字、工具连接、输入框、Agent状态和模型状态的快照。`modelAvailable` 表示有模型文件，不等于推理进程已驻留。`connections` 是已初始化的客戶端總數，Windows 版的 `mcpClients` 是其中經 MCP 介面連線的外部客戶端。Windows 版 state 另有 `bubbleVisible`、`externalBubble`（目前氣泡來自 say 或 say_next）、`speechPending`、`fullscreen`、`idleSeconds`、`activeMinutes`、`lastBreakMinutes`，以及 walk_to 的 `walk`、`tour`、`lastWalk` 與 `walkBubble`。

`update_settings` 参数为 `{"settings":{...}}`，省略的字段保留，字段名区分大小写，先验证整个补丁。`AgentEnabled`、`AgentHarnessId` 只读，由用户在连接页选择。`McpEnabled` 與遠端模型設定（`ModelSource`、`RemoteUrl`、`RemoteModel`、`ScreenImages`）同樣只讀；遠端模型的 API Key 與中央氣象署授權碼以 Windows 帳戶加密保存，不會出現在 `get_settings`，也不能透過 `update_settings` 寫入，被拒絕的欄位只列出名稱、不回傳數值。以下为可写字段：

| 字段 | 类型与范围 | 含义 |
| --- | --- | --- |
| ModelEnabled | bool | 启用内置模型 |
| GpuEnabled | bool | 本地推理启用GPU |
| PublicInfo | bool | 内置天气、新闻 |
| ForegroundEnabled | bool | 内置模型读屏及 Agent 日常关心附图；不控制显式截图工具 |
| StopModelOnBattery | bool | 离电暂停并卸载内置模型 |
| HideOnFullscreen | bool | 外部全屏时自动隐藏 |
| CheckUpdatesOnStartup | bool | 启动检查更新 |
| WeatherRegionCode | 字符串 | 内置叶级地点代码：臺灣為鄉鎮市區代碼 TW-三位縣市資料集編號-兩位序號（例如臺北市中正區 TW-061-01，預設地點），中国六位区县代码、美国 US-加五位县 FIPS，其他地点为国家代码-GeoNames 城市 ID，例如340104、US-06037、JP-1850147；格式為 `^([0-9]{6}\|[0-9]{9}\|TW-[0-9]{3}-[0-9]{2}\|[A-Z]{2}-[0-9]+)$`，並須存在於地區表。臺灣地點在 Windows 版填了中央氣象署授權碼時使用氣象署鄉鎮預報，沒有授權碼時（以及 Linux 版）改用 Open-Meteo 在鄉鎮中心點的估計。原有中國地區表「台湾省」底下的代碼（71 開頭）不會出現在選單中，也不能寫入；先前存下的這類代碼在讀取設定時會換成同一個鄉鎮市區的臺灣代碼 |
| Scale / BubbleScale | 0.5–2的有限数 | 角色／独立气泡倍率 |
| SpeechMinMinutes | 2、3、5、10、15、20之一 | 分别对应2–3、3–5、5–8、10–16、15–23、20–30分钟 |
| BubbleDurationIndex | 0–7整数 | 5、10、15、30、60、120、180秒，或直到下一句 |
| AnimationIntervalSeconds | 30–300整数 | 普通随机动作事件等待秒数，等完整片段结束再切换 |
| BreakfastHour / LunchHour / DinnerHour | 0–23整数 | 本地时间餐时中心小时 |
| Left / Top | -1000000–1000000有限数或null | Windows逻辑坐标，Linux像素；受工作区约束，null不主动移动 |
| LastBrushDate | yyyy-MM-dd有效日期或null | 每日刷牙去重日期 |
| StartupEnabled | bool | 当前用户开机启动 |
| PuppetMode | bool | 完全外部接管；开启须等待用户确认 |
| AgentDailySpeech | bool | 日常吐字交给Agent |
| AgentRespondToClick | bool | 点击事件主动发起ACP轮次，默认关 |
| AgentRespondToDrag | bool | 拖动结束主动发起ACP轮次，默认关 |
| McpChatEnabled | bool | 經 MCP 介面雙擊與魚魚對話；還需要完成初始化的客戶端。內建 Agent 連線開啟時輸入框一律可用 |
| SitReminderMinutes | 0、30、45、60、90、120之一 | Windows 版魚魚自己的久坐提醒，0 為關閉，預設 60 |
| MealReminder | bool | Windows 版用餐提醒，時間沿用三餐小時 |
| LateReminder / LateReminderHour | bool；22、23、0、1、2之一 | Windows 版深夜提醒開關與開始的整點，預設凌晨 0 點 |
| BatteryReminder | bool | Windows 版低電量提醒（沒插電且剩 20%、10%） |
| RainReminder | bool | Windows 版下雨提醒（需開啟 PublicInfo，6 小時內降雨機率 60% 以上） |
| DailyBriefing | bool | 由 Hermes 橋接程式在每天第一次坐下時講今日簡報；魚魚自己不講 |
| WelcomeBackMinutes | 0、30、60、120、240之一 | 離開至少這麼久回來時，由 Hermes 橋接程式說期間發生的事，0 為關閉 |
| EventReminderMinutes | 0、5、10、15、30之一 | Google 日曆行程開始前幾分鐘由 Hermes 橋接程式提醒，0 為關閉 |

更新设置不打开、激活或置顶设置窗口。窗口已打开时保留页面、滚动、位置和窗口状态并刷新。修改GPU、模型、地区等按现有逻辑取消或重排对应任务；Linux每次应用设置都会停止本地模型并重排计时。开启关心触发与动作接管的确认异步显示，等待时角色仍可拖动；取消则不应用该补丁。IO或系统启动项写入失败可能发生在内存设置已变更后，调用方读取状态再决定下一步。

## 截图、动作与文字

| 工具 | 输入 | 行为 |
| --- | --- | --- |
| list_actions | 空对象 | 返回106项精确action、pool、基础weight和名义duration_seconds |
| capture_foreground | 可选max_dimension | 当前前台截图，默认最长边2560 |
| capture_screen | 可选max_dimension | 虚拟桌面截图，默认最长边4096 |
| play_action | action | 加入FIFO队列，最多100项 |
| interrupt_action | action | 清空未播放队列，立即切换动作 |
| say | text | 立即原文逐字显示，替换当前气泡并取消旧本地生成 |
| say_next | text | 覆盖待说原文，在下次符合日常节奏时显示 |
| request_local_speech | 空对象 | 立即请求真实本地模型按当前情境生成日常关心；需要AgentDailySpeech=false和可用的本地模型 |

max_dimension为256–16384整数。action精确匹配list_actions，最多200字符，Disabled池动作也可指定。重复动作不去重；隐藏或拖动时排队动作等待。外部动作播完后，普通模式恢复自主调度，关心触发与动作接管回到固定待机。立即动作和说话不会自动恢复被隐藏的角色。

text非空白且最多4000个UTF-16单元，按约28个组合字符/秒显示，保留原文，不执行Markdown、不经过本地模型文本清理。外部原文不依赖本地模型、GPU或插电。say不清除say_next；关闭连接或退出清除待说原文。say_next服从正常可见性、设置窗口、倒计时、拖动及空闲门槛，关心触发与动作接管暂停该节奏；立即说话应使用say。

## 使用者狀況、找畫面與走過去（Windows 版）

Linux 版呼叫這三個工具時回覆尚不支援。

`get_context` 無參數，唯讀，不截圖也不連網。回傳 `summary`（已換算好的一段說明）、`localTime`、`weekday`、`idleSeconds`、`activeMinutes`（這次連續使用幾分鐘，不在電腦前為 null）、`lastBreakMinutes`、`fullscreen`、`power`（`onAC`、`batteryPercent`）、`foreground`（前景程式與視窗標題；關閉 ForegroundEnabled 時為 null）、`weather`（魚魚快取的天氣原文）、`openFolders`（畫面上開著的檔案總管資料夾，最上層的 `front=true`）與 `lastReminder`（魚魚最近一次主動提醒）。

`find_on_screen` 輸入 `query`（必填，用 `|` 分隔多種說法）、可選 `where`（all、desktop、windows）與 `limit`（1–50）。依名稱找出現在看得到的桌面圖示（附對應檔案）、視窗標題與視窗內的介面元素，依閱讀順序回傳中心點 `x`、`y` 與 `width`、`height`（Windows 實體畫素），可原樣交給 `walk_to`。

`walk_to` 讓魚魚跑到目標旁邊停下，不遮住目標：目標用 `x`、`y`（可加 `width`、`height`）或 `desktop_item` 指定，`space=screen` 為實體畫素虛擬桌面座標，`space=capture` 為本服務截圖的影像座標（可附 `capture_id`、`image_width`、`image_height`）；`stops` 可依序跑過最多 20 站，`pause_seconds` 控制每站停留。可選 `side`、`arrive_action`、`say`、`label` 與 `wait`。使用者拖曳或點選、`interrupt_action`、新的 `walk_to`、隱藏、退出或修改位置與大小都會讓魚魚停在當下位置，結果的 `cancelled` 說明原因。行走中 `state.animationPhase` 為 WalkStart、WalkLoop 或 WalkStop，`state.walk`、`state.tour` 為進度，`state.lastWalk` 為最近一次的結果。

截圖結果首項的 metadata 另含 `captureId`、`screen`（影像在虛擬桌面上的來源矩形）、`scale`（影像畫素÷實體畫素）、`coordinateSpace` 與 `pet`（魚身矩形）。`play_action`、`interrupt_action` 與 `arrive_action` 的動作名稱寫成繁體也認得；名稱不對時錯誤訊息會列出最接近的名稱。Windows 台灣版顯示 `say`、`say_next` 的文字前會把簡體字轉成繁體，工具回傳的仍是原文。

## 操作指南与预约

`get_operation_guide` 无参数，返回工作目录中已安装的技能正文、人设与相应文件路径。文件内容以用户实际保存的版本为准。

`get_wakeups` 无参数，返回当前Harness、会话的预约以及预约机制是否启用。

`schedule_wakeup` 输入：

```json
{
  "purpose": "看看主人是否适合休息一下",
  "delay_seconds": 600,
  "repeat_seconds": 600
}
```

purpose为1–1000字符；delay_seconds与可选repeat_seconds为30–604800整数秒。省略或null的repeat_seconds表示一次性。可传最多100字符的既有id修改当前会话的预约，新预约返回自己的id、Harness、会话、用途、到期时间和周期。全局最多32项。

创建需要连接就绪并开启关心触发与动作接管。切换吐字编写者保留预约；关闭连接、离开关心触发与动作接管或开始新对话会清除当前Harness的预约。预约不要求Agent在一轮内持续等待，当前轮次可以结束。

AgentDailySpeech=true时，到期通过ACP向同一对话提交wakeup，附预约用途；false时，到期由鱼鱼直接调用本地模型生成日常关心。隐藏、拖动或未连接时推迟；多个到期预约合并。一次预约在提交前移除，重复预约从本次提交时间重新计时。Agent可检查、取消或修改后续预约。

`cancel_wakeup` 输入 `{"id":"预约编号"}`，只取消当前Harness会话中的对应项，返回是否取消。

## 事件与输入

点击、拖动开始和结束，以及用户输入形成sequence、UTC time、kind、detail事件。最近128项只保存在控制服务内存中，URI为`bigbluefish://interactions`。`resources/read`返回当前数组，不清空；`resources/subscribe`通过`notifications/resources/updated`告知变化，调用方自行读取。

info级别的`notifications/message`使用logger `bigbluefish.interaction`，可以通过`logging/setLevel`调整。通知和资源是同一事件的两种获取方式，按服务实例和sequence去重。超过队列容量的旧事件被覆盖，服务关闭释放事件记录。

双击输入直接通过ACP发起user_message，内容为主人提交的原文。Agent普通文字不进入气泡；回复通过say完成。输入固定可用，连接未就绪时发送按钮置灰并保留草稿。点击和拖动是否另发起Agent轮次由两个响应字段决定。

雙擊輸入框可以用附加按鈕、拖曳或 Ctrl+V 加入最多 10 個圖片、影片或檔案；貼上的圖片先存成 PNG（`%LOCALAPPDATA%\BigBlueFish\attachments`）。Agent 連線收到的訊息最後會列出「附件（本機路徑）」；`user_message` 事件的 `detail` 另有 `attachments`，每項含 `path`、`name`、`kind`（image、video、file）、`mimeType`、`size` 與 `source`，只有路徑不含檔案內容，沒有附件時省略此欄位。Agent 已連線時，同一則輸入在開啟「雙擊與魚魚對話」的情況下也會以 `user_message` 交給 MCP 介面的客戶端；Agent 未連線而 MCP 介面可對話時，輸入只以 `user_message` 交給外部客戶端；兩者都不能接收時，傳送按鈕停用並保留草稿與附件。外部 Agent 的氣泡還在顯示時，魚魚不會自己開口把它蓋掉。

Windows 版另有 `user_returned` 事件：使用者離開 5 分鐘以上後回到電腦前，`detail` 含 `breakMinutes`、`since`、`at` 與 `watching`（離開期間大多開著全螢幕程式，可能只是在看）。Hermes 橋接程式依此和 `DailyBriefing`、`WelcomeBackMinutes` 決定要不要說今日簡報或期間發生的事。

## 生命周期

关闭连接或退出取消工具请求、确认等待和ACP进程，释放内部服务。请求可通过`notifications/cancelled`取消；工具在执行前及异步工作后检查取消。动作和文字已经提交到UI后的副作用不自动回滚。多个工具请求可同时等待，UI中的实际控制由相同调度器执行。

Agent自行保存会话和处理模型请求；输入、截图或工具结果可能经其配置发送给云端模型。鱼鱼的连接告知统一覆盖其自身的控制工具；普通文件、终端等Agent操作仍按对应Harness的权限请求处理。
