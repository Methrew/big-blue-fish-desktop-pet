<!-- Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0). -->
# 藍色大肥魚桌寵（台灣繁體中文版）

> [!NOTE]
> **這是非官方的台灣繁體中文版，不是原作者發布的版本。**
>
> - 原作者：Bai-Cute（[Bai-Cute/big-blue-fish-desktop-pet](https://github.com/Bai-Cute/big-blue-fish-desktop-pet)）
> - 台灣版：acer1204（[acer1204/big-blue-fish-desktop-pet 的 taiwan-edition 分支](https://github.com/acer1204/big-blue-fish-desktop-pet/tree/taiwan-edition)）
> - 台灣 macOS 版整合：Methrew（[Methrew/big-blue-fish-desktop-pet](https://github.com/Methrew/big-blue-fish-desktop-pet)）
>
> 本版以原作者的藍色大肥魚 1.1.3（含官方 macOS 版）為基礎，整合 acer1204 台灣版的功能並移植到 Mac，再把介面、氣泡與文件改成台灣繁體中文和台灣用語。版本號是 `1.1.3-tw.3`，意思是「原版 1.1.3 的台灣版第 3 次修訂」。acer1204 認可本版整合其台灣版；本版不是由原作者或 acer1204 維護，內容也未經原作者審閱或背書；使用上的問題請回報到[本版的 Issues](https://github.com/Methrew/big-blue-fish-desktop-pet/issues)，不要回報給原作者。

藍色大肥魚是一款住在電腦桌面上的 AI 桌寵軟體。她是一位藍髮的小鯨魚娘，形象來自對 DeepSeek 的二創虛擬形象「藍色大肥魚」。軟體內建 4B 本機語言模型，透過 OCR 讀懂主人正在桌面上做的事情，每隔一段時間就從文字氣泡裡送來一句可愛的話，陪伴和關心主人；她也會看看天氣、瀏覽科技新聞，再用自己的方式講給主人聽。

魚魚準備了一百多種可愛的動作表情，平常會在桌面上呼吸、哼歌、伸懶腰、吃零食、玩耍，到了吃飯時間會認真吃飯，到了節日會換上應景的小活動，被點一下、被拎起來時也有專門設計的反應。每次啟動，她都會提起裙擺向主人行禮請安；關閉時，也會用同樣的禮儀和主人道別。標準版還能透過 ACP 一鍵連線 21 款主流 CLI Agent，陪主人進行穩定的長對話。

本版以 macOS（Apple Silicon）為主要平台，在 Mac 上建置與測試。

<p align="center">
  <img src="docs/images/bigbluefish-rest-transparent.png" width="38%" alt="藍色大肥魚休息狀態" />
  <img src="docs/images/bigbluefish-speaking-transparent.png" width="58%" alt="藍色大肥魚說話狀態" />
</p>

## 下載與安裝（macOS）

需要 Apple Silicon（M 系列晶片）的 Mac，以及 macOS 14 或更新版本。

請到[本版的發布頁面](https://github.com/Methrew/big-blue-fish-desktop-pet/releases)下載 DMG，每個檔案都附有對應的 `.sha256` 檢查碼檔：

| 版本 | 檔案 |
| --- | --- |
| 標準版 | `BigBlueFish-TW-v1.1.3-tw.3-macOS-arm64.dmg` |
| 無MCP版 | `BigBlueFish-TW-v1.1.3-tw.3-Safe-macOS-arm64.dmg` |

檔名開頭是 `BigBlueFish-TW`，和原作者的 `BigBlueFish-v…` 不同。發布頁面還沒有任何版本時，請先[從原始碼建置](#從原始碼建置)。

1. 打開 DMG，把「藍色大肥魚」（無MCP版是「藍色大肥魚無MCP版」）拖到「應用程式」檔案夾，再從「應用程式」開啟。執行所需的 .NET、動畫解碼元件和本機推論程式都包在 App 裡，不必另外安裝。
2. App 只有臨時（ad-hoc）簽章，沒有經過 Apple 公證，第一次打開時 macOS 會擋下來。請先試著打開一次，再到「系統設定 › 隱私權與安全性」，在下方找到藍色大肥魚的提示並按「強制打開」，然後依系統提示確認。
3. 第一次啟動時會出現安裝模型的視窗，可以選擇 Hugging Face（推薦）、中國大陸鏡像（ModelScope）、手動安裝已經下載好的 GGUF 檔案，或先以無模型模式啟動；也可以按「改用雲端 AI 服務」，用自己的 Claude、OpenAI 或 Gemini API Key 讓魚魚說話（見[雲端 AI 服務](#雲端-ai-服務claudeopenaigemini)）。
4. 依需要允許下列權限（都在「系統設定 › 隱私權與安全性」）：

| 權限 | 用途 | 哪個版本 |
| --- | --- | --- |
| 螢幕與系統錄音（macOS 14 為「螢幕錄製」） | 讀取前景視窗的文字（OCR）、截圖，以及讀取其他 App 的視窗標題 | 兩個版本 |
| 輔助使用 | `find_on_screen` 找桌面圖示、Dock 項目與視窗裡的按鈕，`walk_to` 跑到桌面圖示旁邊 | 標準版 |
| 自動化 › Finder | Agent 讀取目前狀況（`get_context`）時，詢問 Finder 開著哪些資料夾；只讀取路徑，不會修改檔案 | 標準版 |
| 區域網路 | 使用區域網路裡的遠端模型伺服器時（macOS 15 以後才會詢問） | 兩個版本 |

因為是臨時簽章，每次更新或重新建置 App 後，系統設定裡的開關可能看起來還開著，實際授權卻已經失效；這時請把開關關掉再打開（或用「−」移除後再用「＋」加回 App），然後重新開啟藍色大肥魚。

完整的安裝、權限、資料夾、鑰匙圈、登入時自動開啟、更新、解除安裝與疑難排解說明，見 [macOS 說明](docs/MACOS.md)。

### Windows 與 Linux

本版的 Windows 與 Linux 原始碼已經一起合併，也確認可以編譯，但沒有在 Windows 或 Linux 上實際執行或測試，也不提供安裝程式或套件。需要這兩個平台時，請使用[原作者的版本](https://github.com/Bai-Cute/big-blue-fish-desktop-pet/releases/latest)，或自行從原始碼建置（未經測試）。

## 本版和原版有什麼不同

- **台灣繁體中文**：設定、選單、提示視窗、模型安裝與更新視窗和魚魚的說話語氣都是台灣繁體中文與台灣用語。氣泡裡的文字（本機或遠端模型的回覆、天氣、Agent 用 `say` 說的話、Hermes 的回答）顯示前都會用 [OpenCC](https://github.com/BYVoid/OpenCC) 1.4.2 的字典把簡體字轉成台灣繁體，已經是繁體或英文的內容維持原樣。氣泡字型改用 [jf open 粉圓 2.1](https://github.com/justfont/open-huninn-font)，粉圓沒有的字與其他介面用系統的蘋方-繁（PingFang TC）。macOS 的 OCR 改成先以繁體中文辨識。106 個動作名稱同時是動畫檔名與 MCP 動作名稱，所以維持原本的簡體字；Agent 用繁體寫動作名稱也認得。
- **臺灣鄉鎮天氣**：國家清單和原版完全相同（中國排第一，包括中國清單裡的臺灣省），只多加一個國家「臺灣 Taiwan」（22 個縣市、368 個鄉鎮市區），新安裝的預設地點是臺北市中正區。選國家「臺灣 Taiwan」並在設定中填入[中央氣象署開放資料](https://opendata.cwa.gov.tw/)的授權碼時，會使用氣象署的鄉鎮天氣預報；授權碼存在 macOS 鑰匙圈，不會寫進設定檔，也不會提供給 MCP。沒有授權碼時，改用 Open-Meteo 估計鄉鎮市區中心附近的天氣。中國清單裡的臺灣省沿用原版資料，以縣市為單位估算、使用北京時間；要看鄉鎮的天氣，請選國家「臺灣 Taiwan」。選單裡的地名一律以繁體字顯示，但只換字形、不換譯名，所以仍會看到「意大利」這類原版資料的譯名。
- **自己的身分與資料夾**：本版的 App 是「藍色大肥魚」（`io.github.methrew.bigbluefish.tw`），資料放在 `~/Library/Application Support/bigbluefish-tw`，檢查更新只看本版的發布頁面，不會被換回原作者的版本。第一次啟動時，會從原版的資料夾**唯讀**匯入一次設定、模型選擇和 Agent 資料（模型用 APFS 複本，不多佔空間），原版的資料不會被移動或修改。本版可以和原版同時安裝，但兩者共用「同一時間只能開一隻」的鎖，所以同一時間只會有一隻魚在執行。
- **雲端 AI 服務（本版新增）**：「模型來源」可以改選 Claude API（Anthropic）、OpenAI API 或 Gemini API（Google），用自己的 API Key 讓魚魚說話，不必在這台電腦上安裝或執行模型。貼上 API Key 會依開頭自動選好服務，「儲存並測試」只讀取模型清單確認金鑰，不花費 token；Claude 預設使用 Claude Opus 5.5，設定頁會顯示每句的大約費用。API Key 存在 macOS 鑰匙圈，MCP 與 Agent 讀不到（只有開啟下面的「Claude Code 改用 Claude API Key」、「Codex CLI 改用 OpenAI API Key」或「Gemini CLI 改用 Gemini API Key」時，魚魚啟動的那個 CLI 會拿到同一家的 Key），也不能修改模型來源與雲端設定。可以限制每天最多送出幾次請求（預設 200 次），設定頁顯示今天與本月的用量；遇到金鑰錯誤、額度用完或服務忙碌時，魚魚會暫停並在狀態列說明原因。
- **Claude Code 改用 Claude API Key（本版新增）**：「連線 Agent」選 Claude Code 時，可以開啟「Claude Code 改用 Claude API Key（依用量計費）」，讓魚魚啟動的 Claude Code 改用雲端 AI 服務裡儲存的 Claude API Key，由 Anthropic 依用量計費；你自己開的 Claude Code 照樣使用訂閱登入。Claude Code 的用量比魚魚自己說話多很多，請先在 Claude Console 設定支出上限；想一直用比較省錢的 Sonnet 或 Haiku，請把模型寫進這個 Claude Code 專用的 `settings.json`（「模型與權限」選的只套用到目前這段對話）。詳見[連線本機 CLI Harness](docs/ACP.md#claude-code-改用-claude-api-key依用量計費)。
- **Codex CLI、Gemini CLI 改用 API Key（本版新增）**：「連線 Agent」選 Codex CLI 時可以開啟「Codex CLI 改用 OpenAI API Key（依用量計費）」，選 Gemini CLI 時可以開啟「Gemini CLI 改用 Gemini API Key（依用量計費）」，讓魚魚啟動的那個 CLI 改用雲端 AI 服務裡儲存的 OpenAI API Key 或 Gemini API Key；你自己開的 Codex CLI、Gemini CLI 照樣使用原本的登入（例如 ChatGPT 帳號、Google 帳號）；`~/.codex` 不會被讀取或修改，`~/.gemini` 裡的登入資料和設定也不會被使用或修改（Gemini CLI 仍可能讀到 `~/.gemini/.env` 裡魚魚沒有設定的變數，以及 `~/.gemini/policies` 的沙箱規則）。魚魚替它們準備各自的設定資料夾：Codex CLI 只把 Key 放在記憶體、不交給它執行的指令；Gemini CLI 預設選用 Gemini API Key（工作區的 `.gemini/settings.json` 仍可能改變），可能從 `.env` 檔改變 API 位址或登入方式的環境變數都會設成空值。詳見[Codex CLI](docs/ACP.md#codex-cli-改用-openai-api-key依用量計費)與[Gemini CLI](docs/ACP.md#gemini-cli-改用-gemini-api-key依用量計費)的說明。
- **從台灣版移植到 Mac 的功能**：
  - 遠端模型：改用另一台電腦上的 llama.cpp 伺服器（OpenAI 相容介面）說話；伺服器支援圖片時可以直接傳前景視窗的截圖。API Key 存在 macOS 鑰匙圈。
  - 魚魚自己的主動提醒：久坐、用餐、深夜、低電量與下雨提醒，不需要模型或 Agent。
  - `get_context` 與 `user_returned`：Agent 可以讀取主人此刻的狀況（時間、閒置多久、連續使用多久、電源、前景程式、Finder 開著的資料夾、魚魚最近一次的提醒）；主人離開 5 分鐘以上再回來時，Hermes 橋接程式等 MCP 客戶端會收到 `user_returned` 事件。
  - `find_on_screen` 與 `walk_to`：在畫面上依名稱找出桌面圖示、視窗和按鈕，再讓魚魚跑到旁邊。
  - Hermes 對話橋接：把雙擊輸入的文字交給本機的 [Hermes Agent](https://github.com/NousResearch/hermes-agent)，另有每日簡報、回來時的摘要、排程回報與 Google 日曆提醒。標準版 App 已內附橋接程式。
  - 外部 MCP 介面：讓 Hermes 橋接程式等本機的 MCP 客戶端透過 `BigBlueFish.Mcp` 控制魚魚，預設關閉；雙擊輸入只會交給一方，不會讓兩個 Agent 各回答一次。
  - 輸入框附件：用迴紋針按鈕、拖曳或 ⌘V 貼上，一次最多附加 10 個圖片、影片或檔案，以 ACP 的 `resource_link` 交給 Agent。
- **Mac 版的改進**：改用 Mac 的硬體解碼（VideoToolbox）播放原作者提升到 1280×720、每秒 60 格的動畫（原版 Mac 版一直只播 640×360、每秒 24 格的相容動畫），想省電可以在「設定 › 桌寵與顯示」關閉；修正原版 Mac 版讀不到真正的閒置時間、導致自動說話與 Agent 日常關心不會觸發的問題；桌寵會跟著切換桌面空間，點一下或拖曳後鍵盤焦點會回到原本的 App；點選和拖曳的動作會預先解碼開頭，反應更即時；第一次偵測 GPU 時等待更久，能用 Metal；從 Finder 開啟時也會讀取登入 shell 的 PATH，找得到 npm 或 Homebrew 安裝的 Agent CLI；可以把 Finder 裡的檔案直接拖到魚魚身上，打開輸入框並附加給 Agent。

## 認識魚魚

魚魚有一頭柔軟的藍色長髮，頭頂戴著蕾絲女僕髮飾，耳邊是小小的鯨鰭，身後搖著一條鯨魚尾巴。她穿著深藍色的女僕裝，白色圍裙上印著一隻小鯨魚，整個人看起來就像一位住進電腦裡的小鯨魚娘。她平常安靜地待在桌面邊緣，身邊透明的地方可以直接點到後面的桌面，主人也可以把她拖到任何喜歡的位置，大小可以在 50% 到 200% 之間調整。

她說話的語氣親切溫柔，又帶著一點可愛俏皮。看見主人在寫程式，她會搖搖尾巴替認真工作的主人加油；看見主人和朋友約明天吃飯，她會忍不住對好吃的動一點小心思；天氣變冷或主人忙了很久，她也會輕輕地送來一句關心。啟動時，她會依時間說「主人早安，魚魚給主人請安啦~」；道別時，她會說「主人再見~魚魚喜歡主人喔」。

<p align="center">
  <img src="docs/images/bigbluefish-maid-bow.gif" width="32%" alt="女僕屈膝禮儀" />
  <img src="docs/images/bigbluefish-idle-breath.gif" width="32%" alt="待機呼吸休閒" />
  <img src="docs/images/bigbluefish-click-wave.gif" width="32%" alt="點選回應元氣揮手" />
</p>

## 她怎樣陪伴主人

魚魚會讀取目前前景視窗的畫面，用 OCR 辨識其中的文字，依標題列、側邊欄、內文等區域整理好，再交給內建的 4B 本機模型理解。不論主人在讀文章、寫文件、改程式，還是在聊天裡提到什麼話題，她都能順著眼前的內容組織一小段新的話；主人停在桌面、首頁或新分頁時，她也會自然地問候、撒嬌或碎碎念。macOS 使用系統的 Apple Vision，Windows 使用系統內建的 Windows OCR，Linux 使用 Tesseract。

啟動約 45 秒後，魚魚會第一次開口，之後在主人選擇的時間範圍內隨機安排下一次說話，範圍從 2–3 分鐘到 20–30 分鐘共六段。她會挑主人暫時停下鍵盤和滑鼠的空檔開口，準備說話時頭邊會轉起一個藍色的小圈圈，接著文字一個字一個字地出現在奶油色的氣泡裡。氣泡大小和停留時間都可以在設定中調整，右鍵選單裡的「說話測試」可以隨時讓她當場說一句。

主人可以選擇世界各地作為天氣地點，也可以選臺灣的鄉鎮市區。魚魚每小時會悄悄更新一次天氣，播報時結合氣溫、體感、濕度、風力、最高最低溫和降雨機率，用鯨魚娘的口吻講給主人聽；右鍵選單裡的「天氣」可以隨時讓她播報。她也會從 IT之家瀏覽最新的科技新聞，挑一則講清楚重點，再加上一小句自己的感想，並用台灣繁體中文轉述。

內建模型每次只產生一段單獨的回覆，沒有對話歷史，也不保留記憶。使用本機模型時，讀到的螢幕內容只在這台電腦上交給模型，不會上傳到雲端，也不會存到磁碟；改用遠端模型時，前景視窗的文字或截圖會傳送到主人自己填寫的伺服器，詳見 [macOS 說明](docs/MACOS.md#遠端模型)；改用雲端 AI 服務時，同樣的內容會傳送到主人選的 Anthropic、OpenAI 或 Google，詳見 [macOS 說明](docs/MACOS.md#雲端-ai-服務)。

筆電拔掉電源時，魚魚可以自動暫停模型並把模型移出記憶體，幫主人照顧續航；其他程式全螢幕時，她會自動躲起來，結束後再回到桌面。這些行為都可以在設定中依自己的習慣調整。

除此之外，魚魚還會自己提醒主人起來走走、記得吃飯、早點睡覺、插上電源和帶傘。這些提醒由魚魚依規則判斷，不需要模型或 Agent；主人不在電腦前、有程式全螢幕或正在拖曳魚魚時，提醒會先等等。

## 一百多種動作表情

魚魚內建 106 段透明動畫，涵蓋待機、點選、拖曳、日常事件、用餐、季節和節日等場景。每一種動作都有自己的出現權重，並會結合目前的時間、日期、季節、用餐時間和主人的操作即時計算，這些機率都經過細心的微調，讓她的表情豐富有趣，又貼合主人當下的生活節奏。每段動作在沒有新的互動打斷時都會完整播放，再自然地銜接到下一段。

<p align="center">
  <img src="docs/images/bigbluefish-meal.gif" width="32%" alt="用餐動作" />
  <img src="docs/images/bigbluefish-fireworks.gif" width="32%" alt="節日煙火動作" />
  <img src="docs/images/bigbluefish-sleep.gif" width="32%" alt="夜間小憩沉睡" />
</p>

待機時，魚魚會在好幾種安靜的動作之間輪流，輕輕地呼吸、悠閒地哼歌；到了晚上 11 點之後，她會開始想睡覺，不時打個小盹，直到隔天早上 8 點。主人點她一下時，她會從好幾種回應裡挑一種，可能元氣地揮揮手，可能害羞地嚇一跳，也可能露出別的小表情，有些回應要多點幾次才比較容易遇到。把她拎起來拖曳時，她會懸在半空中保持被抓住的小模樣，直到主人放手。

每隔一段時間（預設 40 秒，可以在 30 秒到 5 分鐘之間調整），魚魚會從大量日常事件中挑一件小事來做：跳舞、吹泡泡、敲小電腦，還有許許多多精緻華麗的動作。三餐的時間可以在設定中依主人的作息填寫，用餐時間前後一小時裡，她會更常端出好多種美食和小吃。早上她比較容易打哈欠，每天晚上 11 點後有空的時候，還會認真地刷一次牙。

節日是魚魚特別用心準備的部分。春節、元宵、情人節、520、端午、七夕、中秋、萬聖節、聖誕節等節日前後，她都準備了專屬的節日動作，例如春節寫福字、放煙火，中秋賞月吃月餅，有些動作還會隨著節日接近越來越常出現；節日前後的用餐時間，她也會端出應景的節日美食。農曆節日依農曆日期計算，閏月也能正確處理。

季節也會改變她的日常：冬天會堆雪人，秋天會被落葉埋住，夏天還有好幾種消暑的小活動。她偶爾也會跑一跑、學螃蟹橫著走，跑出一小段距離，在那裡待一會兒，再回到原來的位置。

除了這些，魚魚還藏著許多沒有寫在這裡的小心思。願主人在每天的陪伴裡慢慢發現她的可愛，每一次不經意的抬頭，都能撞見一個小小的驚喜。

> macOS 版和 Windows 版都播放原作者提升到 1280×720、每秒 60 格的動畫。macOS 用 VideoToolbox 硬體解碼（Apple Silicon）；無法硬體解碼，或在「設定 › 桌寵與顯示」關閉「高畫質動畫」時，改播 640×360、每秒 24 格的相容動畫。

## 內建模型

魚魚使用 Qwen3.5-4B 的 Q4_K_M 量化 GGUF 模型，由隨附的 [llama.cpp](https://github.com/ggml-org/llama.cpp)（b10809，macOS 使用 Metal，Windows 與 Linux 使用 Vulkan）在這台電腦上推論，可以使用 GPU 加速，也可以改用 CPU 執行。兩個預設來源的模型有些不同，但都適合本程式，主人可以依自己的網路環境選擇。

Hugging Face 來源是 [Biomanticus/Qwen3.5-4B-heretic-gguf](https://huggingface.co/Biomanticus/Qwen3.5-4B-heretic-gguf/tree/b63ff4662e4863cfa005c9cd8ed34b87ed44b7e1)，它是 [p-e-w/Qwen3.5-4B-heretic](https://huggingface.co/p-e-w/Qwen3.5-4B-heretic) 的 GGUF 轉換版本，以 Qwen3.5-4B 為基礎。中國大陸鏡像來源是 ModelScope 上的 [diodel/Qwen3.5-4B-Q4_K_M-GGUF](https://modelscope.cn/models/diodel/Qwen3.5-4B-Q4_K_M-GGUF/files)，是原版 [Qwen/Qwen3.5-4B](https://huggingface.co/Qwen/Qwen3.5-4B) 的 Q4_K_M 轉換版本。兩個來源都使用固定的修訂版本，檔名、大小與檢查碼見[第三方聲明](code/THIRD-PARTY.md)。自動下載的模型約 2.7 GB，下載完成後會核對 SHA-256，中途斷線也能從已有的進度繼續。

模型會依目前裝置可用的顯示記憶體自動調整計算緩衝區，並由 llama.cpp 自動分配模型層，顯示記憶體較小的裝置也能順利載入；GPU 初始化失敗時，魚魚會自動改用 CPU 相容模式，並在設定中顯示實際的推論裝置。

不想在這台電腦上執行模型時，也可以在「設定 › 模型與資訊 › 模型來源」改用遠端的 llama.cpp 伺服器，或下一節的雲端 AI 服務。「啟用模型說話」是魚魚用模型自己說話的總開關，使用遠端模型或雲端 AI 服務時也要開著。

## 雲端 AI 服務（Claude、OpenAI、Gemini）

魚魚也可以改用 Claude API（Anthropic）、OpenAI API 或 Gemini API（Google）說話，日常關心、天氣、新聞和說話測試都由所選的服務產生，這台電腦不必安裝或執行模型。需要主人自己的 API Key，費用由服務依用量向主人的帳戶收取；Claude Pro、ChatGPT Plus 這類聊天訂閱不包含 API 用量。

快速設定：

1. 到服務的網站建立 API Key：[Claude API](https://platform.claude.com/settings/keys)、[OpenAI API](https://platform.openai.com/api-keys)、[Gemini API](https://aistudio.google.com/apikey)。建議為魚魚另外建立一把專用的 Key，並在服務的主控台設定花費上限或預算提醒。
2. 打開「設定 › 模型與資訊」，「模型來源」選「雲端 AI 服務（Claude、OpenAI、Gemini）」，看完會傳送哪些內容的說明後確認。
3. 把 API Key 貼進「API Key」欄（會依開頭自動選好服務），按「儲存並測試」。測試只讀取這把 Key 能用的模型清單，不花費 token。
4. 視需要從「模型」選單改選模型、調整「每天最多」（50、100、200、500 次或不限制，預設 200 次）。改了模型要再按一次「儲存並測試」才會生效；沒按就關掉設定視窗，魚魚會繼續用原本的模型。「每天最多」一改就會儲存，不必另外按。

| 服務 | 預選的模型 | 每句大約 |
| --- | --- | --- |
| Claude API | Claude Opus 5.5（也可以改選 Claude Sonnet 5.5、Claude Haiku 5.5） | US$0.02（Sonnet 5.5 約 US$0.01，Haiku 5.5 約 US$0.0005） |
| OpenAI API | `gpt-6-luna` | US$0.0004 |
| Gemini API | `gemini-3.5-flash-lite` | US$0.001 |

每句的費用是依 2026 年 11 月的價格、以一句附視窗截圖的話估計的，實際以服務的帳單為準。API Key 存在 macOS 鑰匙圈，不會寫進設定檔，MCP 和 Agent 都讀不到（只有開啟「Claude Code 改用 Claude API Key」、「Codex CLI 改用 OpenAI API Key」或「Gemini CLI 改用 Gemini API Key」時，魚魚啟動的那個 CLI 會拿到同一家的 Key），也不能修改模型來源、服務、模型和每天上限。前景視窗的文字（勾選「傳送前景視窗截圖」時是截圖）會傳到所選的服務；會傳送的內容、費用、暫停規則、鑰匙圈與疑難排解，見 [macOS 說明](docs/MACOS.md#雲端-ai-服務)。

## 透過 ACP 連線桌面 Agent

從 1.1.0 起，標準版的藍色大肥魚透過 ACP（Agent Client Protocol）連線主人電腦上已經安裝的 CLI Agent。連線只需要在設定裡點一次：魚魚會在背景啟動所選的 Agent，為自己開一個專用對話，並自動接好動作、截圖、說話等工具。接上之後，主人可以和魚魚長時間聊天，也可以藉助 Agent 的能力，做到操控電腦、和雲端的大型語言模型對話、自訂陪伴和動作邏輯等各種玩法。

魚魚目前支援 21 款 CLI Agent，其中有 Claude Code、Codex CLI、Gemini CLI、DeepSeek Harness、Kimi Code、Qwen Code、GitHub Copilot CLI、Cursor CLI、OpenCode、Hermes Agent 等。全部支援的 Agent 和各自的啟動方式見[連線本機 CLI Harness](docs/ACP.md)。

### 連線方法

1. 依所選 Agent 自己的說明把它裝好，並完成登入和模型設定。
2. 打開魚魚的設定，進入「連線 Agent」頁面，打開「連線本機相容的 Agent 命令列工具」，閱讀並確認跳出的權限說明。
3. 從下拉選單選一個 Agent。電腦上偵測到的 Agent 排在前面；剛裝好新的 Agent 時，按一下「重新偵測」。
4. 等頁面顯示連線狀態、Agent 版本、模型和對話編號，就可以開始用了。

Claude Code、Codex CLI 和 Pi 需要經過 ACP 轉接器連線。電腦上還沒有這些轉接器時，魚魚會透過 Node.js 和 npm 自動裝上固定版本；Antigravity CLI 的官方 ACP 元件也會在第一次連線時自動準備。Agent 要求登入時，魚魚會列出它提供的登入方式；選了終端機登入，魚魚會打開一個登入用的終端機（macOS 是「終端機」App），登入成功後自動重新連線，接著處理剛才的請求。

### 和魚魚長聊

連上之後，雙擊魚魚，她身邊會展開一個小輸入框。寫下想說的話，按「傳送」或 ⌘↩（Windows 與 Linux 是 Ctrl+Enter），Agent 的回覆就會出現在魚魚的氣泡裡。輸入框也可以附加圖片、影片或檔案。第一次連線時，魚魚會替 Agent 裝上「藍色大肥魚操作」技能（`big-blue-fish`），裡面寫著她的鯨魚娘人設，還有播放動作、截圖、說話這些操作的用法；人設也可以在設定裡隨時改成主人喜歡的樣子。

魚魚在每個 Agent 那裡都有一個專屬對話。說完一輪，下一句話還在同一個對話裡繼續，前面聊過的內容都留在上下文中，所以能聊得很長、很穩。Agent 支援恢復對話時，關掉魚魚再打開，或斷線後重新連線，她都會先把原來的對話接回來；登入過期、網路不穩這類情況也會保留原本的對話，恢復後繼續聊。想換個話題從頭開始，按「開始新對話」就好。

### 魚魚的工作區

魚魚有一個屬於自己的工作區，Agent 幫主人整理的文件、表格、程式碼等成果都放在這裡。新安裝時，工作區預設是「文件」資料夾裡的「藍色大肥魚工作區」；已經在使用原版建立的「蓝色大肥鱼工作区」或自己選的資料夾時，會繼續使用原本的資料夾。所有 Agent 共用目前這一個工作區，換了 Agent 或開了新對話，手上的檔案都還在。設定裡可以新增、開啟、檢視和清空工作區；對話依工作區分開儲存，切回原來的工作區，魚魚就接著那裡的對話聊。

同一頁還列出目前 Agent 提供的模型、權限與工作模式和思考選項，主人可以直接在這裡切換。Agent 想讀寫檔案或執行指令時，魚魚會把具體的操作和 Agent 提供的核准選項列出來，由主人決定。

### 讓 Agent 安排日常關心

設定裡有兩個開關，可以分開打開。「把日常關心要說的話交給 Agent」讓魚魚維持原本的說話節奏，每到該開口的時候，請 Agent 看著目前的情境寫一句關心的話。「把日常關心的觸發和日常的動作切換邏輯交給 Agent」則讓魚魚安靜地循環待機，動作由 Agent 安排，下一次關心的時間也由 Agent 來約，可以只約一次，也可以定期重複；時間一到，這句話由 Agent 或魚魚自己的模型來說，看第一個開關怎麼設定。右鍵選單裡的「天氣」和「說話測試」一直都能用，同樣依目前的設定交給魚魚自己的模型或 Agent 來回答。

### 魚魚與 Agent 的分工

連線後，Agent 可以使用以下能力：

- 讀取和修改魚魚的設定，檢視她目前的動作、模型、連線和預約狀態；
- 取得目前前景視窗或整個螢幕的截圖（macOS 的整個螢幕只截主螢幕）；
- 讀取主人此刻的狀況（`get_context`），包括時間、多久沒操作電腦、這次連續使用多久、電源與電量、前景程式，以及 Finder 開著的資料夾；
- 依名稱在畫面上找出桌面圖示、視窗和按鈕，讓魚魚跑到旁邊（`find_on_screen`、`walk_to`，macOS 與 Windows）；
- 查詢全部 106 個動作，讓魚魚馬上播放某個動作，或把動作排進佇列依序播放；
- 讓魚魚說出 Agent 寫好的話，或請魚魚自己的模型依目前情境說一句；
- 預約、檢視和取消日常關心。

即使接上了 AI Agent，魚魚在這條連線中做的事情也始終很簡單：把依主人開啟的設定準備的螢幕畫面、Agent 主動要求的截圖、主人主動輸入的文字和附加的檔案路徑，以及依主人開啟的設定發出的互動事件和日常關心請求交給 Agent，再接收 Agent 送來的文字、動作和設定等控制。電腦操控、雲端對話等各種操作能力，實際上都由 Agent 自己執行。

**風險提醒：** 連線 Agent 後，截圖會交給所連線的 Agent，由 Agent 決定怎麼處理，可能導致主人的資訊被上傳或儲存。開啟連線時確認的權限對所選 Agent 一律有效，Agent 可以使用上面列出的全部能力，不會依工具分別授權。設定中的「感知目前使用的程式」開關控制日常關心時是否自動讀取螢幕，Agent 透過工具主動要求的截圖不受這個開關影響。Agent 連線預設關閉，開啟前軟體會跳出視窗說明這些權限，請只連線你信任的 Agent。

支援方式、登入、工作區和對話的完整說明見[連線本機 CLI Harness](docs/ACP.md)，內部工具的參數見[內部工具參考](docs/MCP.md)。外部 MCP 介面與 Hermes 對話橋接的設定方法見 [macOS 說明](docs/MACOS.md#hermes-對話橋接與外部-mcp-介面)。

## 無MCP版

為了徹底消除 Agent 連線可能帶來的隱私風險，本版也提供無MCP版（App 名稱是「藍色大肥魚無MCP版」；原作者的 README 稱它為「無ACP版」）。無MCP版在編譯時就移除了 Agent 連線、MCP 介面與內部控制服務、MCP 橋接程式、Hermes 橋接程式、雙擊輸入框和相關的設定頁面，只保留魚魚自己的陪伴功能（包括遠端模型、雲端 AI 服務、臺灣鄉鎮天氣和主動提醒），檢查更新時也只會找無MCP版。DMG 的檔名帶有 `Safe` 字樣，下載方式和標準版相同。

在 macOS 上，無MCP版和標準版共用同一個資料夾與鑰匙圈項目，但登入項目和系統權限各自分開。開過無MCP版之後，標準版的 Agent 與 MCP 相關開關會維持原樣，不會被無MCP版關掉。

## 從原始碼建置

儲存庫給開發者和維護者使用，包含原始碼、動畫資源、地區資料、建置與發布腳本、測試程式、相依套件鎖定檔和授權檔案。模型與推論執行環境由腳本另外準備。

### macOS（Apple Silicon）

```bash
xcode-select --install          # clang、swiftc
brew install --cask dotnet-sdk  # 或安裝到 ~/.dotnet，需要 .NET 10 SDK
brew install node               # 只有連線需要轉接器的 Agent（Claude Code、Codex CLI、Pi）時才需要 npm

tools/build-macos.sh            # 標準版：release/藍色大肥魚.app
tools/build-macos.sh Safe       # 無MCP版：release/藍色大肥魚無MCP版.app
DOTNET=~/.dotnet/dotnet tools/build-macos.sh   # 這台 Mac 裝了多個 .NET 時，指定 .NET 10
```

腳本會下載並核對固定版本的 llama.cpp（macOS arm64、Metal）與 GStreamer 元件，編譯 Apple Vision OCR 與畫面讀取的輔助程式，產生自包含的 App，並以臨時簽章簽署。發布用的 DMG 由 GitHub Actions 的「macOS package」工作流程產生並驗證。詳細步驟見 [macOS 說明](docs/MACOS.md#從原始碼建置)。

### Windows 與 Linux（只確認可以編譯）

原作者的 Windows 與 Linux 建置腳本都保留在儲存庫裡，例如 Windows 的 `./code/build.ps1 -Locked -InputMode Ocr`、`./tools/Build-Installer.ps1`，以及 Linux 的 `./tools/Build-LinuxPackage.ps1`。本版只在 Mac 上用 .NET SDK 確認這些專案可以編譯，沒有在 Windows 或 Linux 上執行、測試，也沒有產生安裝程式或套件。

專案版本統一記錄在根目錄的 `Version.props`，程式、MCP 橋接程式、安裝程式和解除安裝程式都從這裡產生版本資訊；本版的名稱、識別碼與發布來源集中在 `code/BigBlueFish.Windows/Companion/CompanionEdition.cs`。原始碼結構、修改入口與測試方式見[開發說明](code/README.md)。

## 文件與回報

本版的變更記錄在 [CHANGELOG.md](CHANGELOG.md)，發布說明在 [release/README.md](release/README.md)。原作者整理的完整行為規格見[功能規格](docs/AGENT_FUNCTIONAL_SPEC.md)，各已發布版本的行為見[版本規格目錄](docs/specs/README.md)；這些規格維持原作者的原文（簡體中文），描述的是原版的行為。

本版的問題、建議和使用回饋，請到 [Methrew/big-blue-fish-desktop-pet 的 Issues](https://github.com/Methrew/big-blue-fish-desktop-pet/issues) 回報，不要回報給原作者或 acer1204。原作者也感謝來自 B 站的小夥伴們一直以來的關愛和建議。

想讓已連線的 Agent 用魚魚的口吻陪伴主人，可以參考這張[藍色大肥魚人設卡](docs/CHARACTER_CARD.txt)。

## 授權與第三方

本專案以 [Apache License 2.0](LICENSE) 發布，原作者的著作權聲明（Copyright 2026 Bai-Cute）和授權條款維持不變，本版的修改同樣以 Apache-2.0 授權發布。依授權條款第 4 條，修改過的原始碼檔案開頭會註明修改者與日期；和原作者 1.1.3 版的完整差異，可以在本儲存庫用 `git diff 75cd217` 檢視。魚魚的形象來自對 DeepSeek 的二創虛擬形象「藍色大肥魚」；專案使用的動畫、模型、執行環境、字型和資料分別來自以下專案，各自遵循其原有授權：

- 角色動畫：106 段透明 WebM 動畫來自 [PC2005-cloud/dsh-pet](https://github.com/PC2005-cloud/dsh-pet)，MIT 授權；播放素材經原作者提升到 1280×720、每秒 60 格，並保留原始解析度與影格率的相容素材（macOS 版兩套都內含：能用 VideoToolbox 硬體解碼時播放 1280×720，否則播放相容素材），動作的選擇與觸發邏輯為本專案自訂。dsh-pet 的 README 另外規定素材禁止商用、角色形象採 CC BY-NC-SA 4.0，並要求衍生作品附上 dsh-pet 的網址；這些限制由 acer1204 的台灣版補充說明，詳見[第三方聲明](code/THIRD-PARTY.md)。本專案的 Apache-2.0 只適用於程式碼，不涵蓋這些動畫與角色形象。
- 本機模型：Qwen3.5-4B 及其衍生的 heretic 版本與 GGUF 轉換，Apache-2.0 授權，模型檔由使用者另外下載；
- 推論與執行環境：llama.cpp（MIT）、.NET 10 執行環境（MIT）、Microsoft Visual C++ 執行階段程式庫（Windows，依 Microsoft 再散布條款）；
- 動畫解碼：macOS 與 Windows 版使用隨附的 GStreamer 元件，Linux 版使用系統的 GStreamer；
- macOS 與 Linux 介面：Avalonia、MicroCom.Runtime、Tmds.DBus.Protocol、SkiaSharp、HarfBuzzSharp（MIT）；
- 文字辨識：macOS 使用系統的 Apple Vision，Windows 使用系統提供的 Windows.Media.Ocr，Linux 使用 Tesseract；
- 氣泡字型：jf open 粉圓 2.1（SIL Open Font License 1.1），取代原版的「乐米小奶泡体」；本版不散布原版字型；
- 繁簡轉換：[OpenCC](https://github.com/BYVoid/OpenCC) 1.4.2 字典（Apache-2.0）；
- 雲端 AI 服務：Claude 透過 Anthropic 官方的 C# SDK（[anthropic-sdk-csharp](https://github.com/anthropics/anthropic-sdk-csharp) 13.8.0，Apache-2.0，改授權前合併的部分仍適用 MIT）與它使用的 Microsoft.Extensions.AI.Abstractions 10.5.1（MIT）；Claude API、OpenAI API、Gemini API 由使用者以自己的帳戶與 API Key 連線，適用各服務自己的條款，本程式沒有經過 Anthropic、OpenAI 或 Google 推薦或背書；
- 天氣資料：[Open-Meteo](https://open-meteo.com/)，CC BY 4.0；臺灣鄉鎮天氣預報：中央氣象署開放資料，依政府資料開放授權條款第 1 版使用，中央氣象署並未推薦或背書本程式；科技新聞：[IT之家 RSS](https://www.ithome.com/rss/)；
- 行政區資料：[AreaCity-JsSpider-StatsGov](https://github.com/xiangyuecn/AreaCity-JsSpider-StatsGov)，MIT 授權，只內嵌層級與近似中心座標；美國州郡、全球城市與國名分別來自美國人口普查局 Gazetteer、GeoNames 與 Unicode CLDR；臺灣鄉鎮市區中心依內政部國土測繪中心的鄉（鎮、市、區）界線計算，依政府資料開放授權條款第 1 版使用。

各元件的版本、修訂、檢查碼和著作權說明見[第三方聲明](code/THIRD-PARTY.md)，完整授權條文見[授權檔案目錄](code/licenses)。
