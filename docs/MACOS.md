<!-- Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0). -->
# macOS 說明（Apple Silicon）

本文說明藍色大肥魚台灣繁體中文版在 Mac 上的安裝、權限、資料夾、設定、更新、解除安裝與從原始碼建置。本版是以原作者 Bai-Cute 的藍色大肥魚 1.1.3（含官方 macOS 版）為基礎、整合 acer1204 台灣版功能的非官方版本，功能介紹見[專案說明](../README.md)。

## 本版的身分

為了不和原版混在一起，本版有自己的名稱、識別碼、資料夾和更新來源：

| | 標準版 | 無MCP版 |
| --- | --- | --- |
| App 名稱 | `藍色大肥魚.app` | `藍色大肥魚無MCP版.app` |
| Bundle ID | `io.github.methrew.bigbluefish.tw` | `io.github.methrew.bigbluefish.tw.safe` |
| 登入項目 | `~/Library/LaunchAgents/io.github.methrew.bigbluefish.tw.plist` | `~/Library/LaunchAgents/io.github.methrew.bigbluefish.tw.safe.plist` |
| 資料夾 | `~/Library/Application Support/bigbluefish-tw`（兩個版本共用） | 同左 |
| DMG | `BigBlueFish-TW-v<版本>-macOS-arm64.dmg` | `BigBlueFish-TW-v<版本>-Safe-macOS-arm64.dmg` |

- 版本號是 `1.1.3-tw.1`，意思是「原版 1.1.3 的台灣版第 1 次修訂」。在 Finder 的「取得資訊」會看到「1.1.3 (1.1.3-tw.1)」。
- 「設定 › 關於與更新」列出版本、致謝和專案連結：原作者 Bai-Cute（Bai-Cute/big-blue-fish-desktop-pet）、台灣版修改 acer1204（acer1204/big-blue-fish-desktop-pet）、台灣 macOS 版整合 Methrew。App 的版權資訊（Info.plist）也寫著同樣的致謝。
- 這不是原作者發布的版本，使用上的問題請回報到 [Methrew/big-blue-fish-desktop-pet](https://github.com/Methrew/big-blue-fish-desktop-pet/issues)。

## 安裝

需要 Apple Silicon（M 系列晶片）的 Mac，以及 macOS 14 或更新版本。執行所需的 .NET、動畫解碼元件（GStreamer）和本機推論程式（llama.cpp）都包在 App 裡。

1. 到[本版的發布頁面](https://github.com/Methrew/big-blue-fish-desktop-pet/releases)下載標準版或無MCP版的 DMG。想確認檔案完整，可以把同名的 `.sha256` 一起下載到同一個資料夾，在終端機執行：

   ```sh
   shasum -a 256 -c BigBlueFish-TW-v1.1.3-tw.1-macOS-arm64.dmg.sha256
   ```

2. 打開 DMG，把 App 拖到「應用程式」檔案夾，再從「應用程式」開啟。請不要直接從 DMG 執行：退出磁碟映像後 App 就不見了，「登入時自動開啟」也會拒絕設定。
3. App 只有臨時（ad-hoc）簽章，沒有經過 Apple 公證，第一次打開時 macOS 會擋下來。請先試著打開一次，再到「系統設定 › 隱私權與安全性」，在下方找到藍色大肥魚的提示並按「強制打開」，然後依系統提示確認。
4. 第一次啟動時會出現安裝模型的視窗：可以選擇 Hugging Face（推薦）或中國大陸鏡像（ModelScope）自動下載約 2.7 GB 的模型，或選「手動安裝」挑選已經下載好的 GGUF 檔案，也可以先以無模型模式啟動。不想在這台 Mac 上執行模型時，選「改用雲端 AI 服務」，就會打開「設定 › 模型與資訊」的雲端設定，改用 Claude API、OpenAI API 或 Gemini API（需要自己的 API Key，見「[雲端 AI 服務](#雲端-ai-服務)」）。之後可以到「設定 › 模型與資訊」安裝或管理模型。如果「模型來源」已經設成遠端伺服器或雲端 AI 服務，啟動時不會要求安裝本機模型。

App 啟動後會常駐在選單列。選單列圖示的選單有「顯示桌寵」「設定」「天氣」「說話測試」與「關閉桌寵」；在魚魚身上按右鍵，也有「設定」「天氣」「說話測試」「隱藏桌寵」與「關閉桌寵」。設定視窗分成「桌寵與顯示」「模型與資訊」「動作與作息」「連線 Agent」（只有標準版）「關於與更新」與「記錄主控台」。

## 權限

下列權限都在「系統設定 › 隱私權與安全性」裡設定。沒有允許時，相關功能會停用或改用其他方式，不會影響其他功能。

| 權限 | 用途 | 沒有允許時 |
| --- | --- | --- |
| 螢幕與系統錄音（macOS 14 為「螢幕錄製」） | 讀取前景視窗的文字（Apple Vision OCR）、截圖（`capture_foreground`、`capture_screen`），以及讀取其他 App 的視窗標題 | 魚魚只能依時間等其他資料說話；設定頁與錯誤訊息會提示要去哪裡允許。 |
| 輔助使用（標準版） | `find_on_screen` 讀取 Finder 桌面圖示、Dock 項目和視窗裡的介面元素，`walk_to` 用 `desktop_item` 跑到桌面圖示旁邊 | `find_on_screen` 只比對視窗標題，並在結果裡說明還沒找桌面圖示與介面元素。 |
| 自動化 › Finder（標準版） | Agent 讀取目前狀況（`get_context`）或用 `find_on_screen` 時，詢問 Finder 開著哪些資料夾；只讀取資料夾路徑，不會修改檔案，每次最多 3 秒 | Finder 開著的資料夾清單是空的，其他結果不受影響。 |
| 區域網路 | 連線到區域網路裡的遠端模型伺服器（例如 `192.168.x.x`、`10.x.x.x` 或 `名稱.local`），macOS 15 以後才會詢問 | 連線測試會顯示「無法連線遠端模型」。網際網路上的 https 伺服器和這台 Mac 自己（`127.0.0.1`）不受影響。 |
| 檔案與資料夾 | Agent（或啟動它的終端機）讀取「桌面」「文件」「下載」等資料夾裡的檔案時 | 由 macOS 依 Agent 本身詢問；被擋住時請在這裡允許。 |

- 第一次在沒有「輔助使用」權限時呼叫 `find_on_screen`（或用 `desktop_item` 呼叫 `walk_to`），macOS 會詢問是否允許藍色大肥魚使用輔助使用功能（每次啟動最多詢問一次）。按「打開系統設定」並打開藍色大肥魚的開關後，再呼叫一次就能找到桌面圖示和介面元素。
- 第一次有 Agent 呼叫 `get_context` 時，macOS 會詢問是否允許藍色大肥魚控制 Finder；這一次的結果先回傳空清單與說明，按「好」之後再呼叫就會列出資料夾。
- 第一次連線區域網路裡的遠端模型時，macOS 會詢問是否允許藍色大肥魚尋找並連線區域網路上的裝置，請選「允許」。App 只會連線你在「設定 › 模型與資訊 › 模型來源」填寫的伺服器。
- 標準版和無MCP版的 Bundle ID 不同，權限要各自允許。

> **重新建置或更新後要重新授權**：App 只有臨時簽章，每次重新建置或更新後簽章都會改變，macOS 可能把它當成另一個 App。系統設定裡的開關看起來還開著，實際授權卻可能已經失效：請先關掉再打開「螢幕與系統錄音」（以及「輔助使用」「自動化」）的開關，必要時用「−」移除後再用「＋」加回 App，然後重新開啟藍色大肥魚。區域網路權限會不會再詢問一次，還沒有驗證過。

## 資料夾與第一次啟動的匯入

本版的設定、模型、Agent 資料和快取放在 `~/Library/Application Support/bigbluefish-tw`，可以在「設定 › 關於與更新」按「開啟資料夾」打開。之所以不和原版共用 `bigbluefish`，是因為原版認不得臺灣的天氣地點（例如臺北市中正區 `TW-061-01`），會把它改回預設值，存檔時也會丟掉本版才有的設定。

第一次開啟本版、而且這個資料夾還不存在時，本版會**只讀取**原版的資料夾 `~/Library/Application Support/bigbluefish`，複製一次：

- `preferences.json`（和它的備份 `preferences.json.bak`）：設定，包括原本選好的天氣地點。
- `agent/`：Agent 的工作區設定、對話記錄和轉接器。
- `models/`：用 APFS 複本（clone）複製，不會多佔磁碟空間。如果原版資料夾在不同的磁碟、無法建立複本，就**不複製**模型（避免多佔好幾 GB），`model-selection.json` 也一起略過；這時第一次啟動會出現安裝模型的視窗，可以選「手動安裝」，再選原版資料夾 `models` 裡的 `.gguf` 檔案。
- `model-selection.json`：模型選擇，只在模型一起複製時才複製。

天氣、新聞等快取不會複製，會重新取得。原版的資料夾不會被移動、修改或刪除。匯入了哪些項目，寫在新資料夾的 `import-from-original.txt`，「設定 › 關於與更新」也會提到。匯入只會發生一次，之後兩個版本各自儲存設定、互不影響。想重新匯入，先結束本版，刪掉（或改名）`bigbluefish-tw` 資料夾，再重新開啟。

指定環境變數 `BIGBLUEFISH_DATA` 時，會改用它指定的資料夾，也不會從原版匯入。

標準版和無MCP版共用 `bigbluefish-tw`。開過無MCP版之後，標準版的「連線本機相容的 Agent 命令列工具」、「把日常關心要說的話交給 Agent」、「把日常關心的觸發和日常的動作切換邏輯交給 Agent」、「開啟 MCP 介面」和「雙擊輸入交給 MCP 介面（Hermes 橋接程式）」這五個開關會維持原樣，不會被無MCP版關掉。

## 和原版同時安裝

- 原版是 `蓝色大肥鱼.app`（`io.github.bai-cute.bigbluefish`），本版是 `藍色大肥魚.app`，兩個可以同時放在「應用程式」檔案夾。兩者的資料夾、登入項目和系統權限都是分開的。
- **同一時間只會有一隻魚在執行**：原版和本版共用同一個「只能開一隻」的鎖，也共用同一個 Agent 連線位置，所以後開的那一個不會啟動，而是跳出「已經有一隻魚在執行」的提示（登入時自動開啟的那一次只會安靜結束）。從終端機或另一個登入工作階段再開一隻本版，也會被擋下。想換另一個版本，先從選單列圖示「關閉桌寵」結束目前這一隻。魚魚已經在執行而且被隱藏時，從 Finder、Launchpad 或 Spotlight 再開一次 App，魚魚就會回來。

## 天氣與地點

天氣地點在「設定 › 模型與資訊 › 天氣地點」：先選國家，再選下一層。

- 國家清單和原版完全相同：「中國 China」排第一，其餘國家依英文名稱排序。本版只多加一個國家「臺灣 Taiwan」，依英文名 Taiwan 排在英文名 T 開頭的國家之間（「敘利亞 Syria」之後、「塔吉克斯坦 Tajikistan」之前）。
- 「臺灣 Taiwan」分兩層：縣市 → 鄉鎮市區，共 22 個縣市、368 個鄉鎮市區。中國分三層（省 → 市 → 區縣），美國分兩層（州 → 郡），其他國家也是兩層（一級行政區 → 城市）。
- 選單裡的地名一律以繁體字顯示，例如廣州市、瀋陽市、臺灣省。這裡只換字形，不換譯名，所以會看到「意大利」「新西蘭」「乍得」這些原版資料的譯名。原版的資料檔、地點代碼和排列順序都沒有改。
- 新安裝的預設地點是臺北市中正區（地點代碼 `TW-061-01`）。設定裡存的地點代碼無效時，也會改用這個地點。已經選好的地點不會被改掉：從原版匯入的設定會保留原本的地點，例如合肥市蜀山區（`340104`），或中國清單裡臺灣省的某個區（例如 `710101`）。
- 切換國家時的起始地點：選「臺灣 Taiwan」從臺北市中正區開始，選「中國」從合肥市蜀山區開始，其他國家從清單第一項開始。

### 兩個「臺灣」有什麼不同

| | 國家「臺灣 Taiwan」 | 中國 › 臺灣省 |
| --- | --- | --- |
| 來源 | 台灣版的鄉鎮市區清單 | 原版的中國清單，原樣保留 |
| 地點範圍 | 鄉鎮市區 | 縣市以下的區，但天氣座標是縣市政府所在地，同一縣市的區都相同 |
| 天氣（填了中央氣象署授權碼） | 中央氣象署鄉鎮天氣預報 | Open-Meteo 估計 |
| 天氣（沒有授權碼） | Open-Meteo 在鄉鎮市區中心的估計 | Open-Meteo 估計 |
| 天氣文字的時間 | 臺灣時間 | 北京時間（沿用原版） |
| 地點代碼格式 | `TW-` 加三位資料集編號與兩位序號，例如 `TW-061-01` | `71` 開頭的 6 位數，例如 `710101` |

要看鄉鎮的天氣，請選國家「臺灣 Taiwan」。在設定頁選到中國清單裡的臺灣省時，也會出現這段提醒。

### 天氣來源

- **國家「臺灣 Taiwan」，有中央氣象署授權碼**：使用中央氣象署「鄉鎮天氣預報」（F-D0047），取接下來兩個 3 小時時段。
- **國家「臺灣 Taiwan」，沒有授權碼**：用 Open-Meteo 估計鄉鎮市區中心附近的天氣，只能當概略參考。鄉鎮市區中心是依內政部國土測繪中心的鄉鎮界線算出的最大陸地塊中心；山區的大鄉（例如仁愛鄉、和平區、桃源區）中心可能離人口聚集處很遠。
- **其他地點**：Open-Meteo，以所選地點中心附近估算。美國地點使用華氏溫度和英里／小時，其他地點使用攝氏和公里／小時。
- 天氣每小時最多更新一次，超過 6 小時的資料不再使用。完全沒有網路回應時（例如剛喚醒電腦），2 分鐘後重試。中央氣象署的預報和 Open-Meteo 的估計分開快取，換授權碼或移除授權碼時不會混用。

### 中央氣象署授權碼

1. 到中央氣象署「氣象資料開放平臺」（<https://opendata.cwa.gov.tw/>）註冊會員，免費取得授權碼，格式類似 `CWA-XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX`。授權碼只能包含英文字母、數字和連字號。
2. 打開「設定 › 模型與資訊 › 天氣地點」，國家選「臺灣 Taiwan」，就會出現「中央氣象署授權碼」欄位。
3. 貼上授權碼後按「儲存並測試」。設定頁會用目前的鄉鎮查一次預報（目前地點不在臺灣時，改查臺北市中正區），結果顯示在按鈕下方。授權碼被中央氣象署拒絕時會顯示原因，這時臺灣地點也拿不到天氣預報。
4. 按「移除授權碼」會刪除授權碼，臺灣地點改回 Open-Meteo 估計。

授權碼存在 macOS 鑰匙圈，詳見下方「[鑰匙圈裡的資料](#鑰匙圈裡的資料)」。MCP 的 `get_settings` 看不到授權碼；`update_settings` 想寫入時會被拒絕，並回覆「API Key 與授權碼只能由使用者在設定中填寫。」授權碼也不會寫進記錄主控台或天氣快取。

## 遠端模型

魚魚可以改用另一台電腦上的 llama.cpp 伺服器說話，這台 Mac 就不必載入本機模型。設定在「設定 › 模型與資訊」。想改用 Claude API、OpenAI API 或 Gemini API，請看下一節「[雲端 AI 服務](#雲端-ai-服務)」。

1. 「啟用模型說話」要開著。這是魚魚用模型自己說話、播報天氣的總開關（設定鍵仍是 `ModelEnabled`）。使用遠端模型時也要開著，只是不會在這台電腦上載入模型。
2. 「模型來源」選「遠端 llama.cpp 伺服器（OpenAI 相容介面）」。
3. 填寫：
   - **伺服器網址**：例如 `http://192.168.1.10:8080` 或 `https://example.com`。結尾的 `/` 和 `/v1` 可以省略，填了也會自動去掉。只接受 `http://` 和 `https://`。
   - **API Key**：伺服器用 `--api-key` 啟動時填同一組字串，沒有設定金鑰就留空。只能用英文字母、數字和常見符號，不能有空白、引號或反斜線，最長 256 個字元。API Key 存在 macOS 鑰匙圈。
   - **模型名稱**：留空時使用伺服器列出的第一個模型。
   - **模型支援圖片時，直接傳送視窗截圖（取代 OCR 文字）**：見下方「會傳送哪些內容」。
4. 按「儲存並測試連線」。切換「模型來源」或勾選截圖選項時會立即儲存；只改網址、API Key 或模型名稱時要按這個按鈕才會儲存。還沒儲存的內容在設定頁重新整理時會保留，關掉設定視窗就會捨棄。App 剛啟動或正在重讀鑰匙圈時，這個按鈕會暫時停用。

使用遠端模型時，「使用 GPU 加速」和「筆電拔掉電源時暫停模型」會變成灰色，因為它們只對本機模型有效；使用電池時，遠端模型照常說話。

| 測試結果 | 意思 |
| --- | --- |
| 遠端模型就緒：模型名稱（支援圖片） | 連線、API Key 和模型名稱都正確，伺服器也接受了測試圖片。 |
| 遠端模型就緒：模型名稱（僅文字） | 可以使用，但伺服器沒有回報支援圖片，所以魚魚會傳視窗文字。 |
| 遠端模型就緒：模型名稱（伺服器拒收測試圖片（HTTP 500），會改傳視窗文字） | 伺服器說支援圖片，實際卻拒收。之後改傳視窗文字，直到再按一次測試或修改遠端設定。 |
| 遠端模型就緒：模型名稱（回報支援圖片，但測試圖片逾時未回應） | 文字可以用，測試圖片在時限內沒有回應。 |
| 遠端模型拒絕存取，請檢查 API Key | 伺服器回覆 401 或 403。 |
| 遠端模型回傳 HTTP 狀態碼 | 伺服器回覆其他錯誤。 |
| 無法連線遠端模型：… | 連不上伺服器。請檢查網址、伺服器是否在執行、防火牆，以及「區域網路」權限。 |
| 連線遠端模型逾時 | 10 秒內沒有回應模型清單，或整個測試超過 30 秒。 |
| 網址無效：請填寫 http:// 或 https:// 開頭的網址… | 網址格式不對，這時仍然使用本機模型。 |

llama.cpp 用 `-m` 載入模型、沒有加 `--alias` 時，模型名稱就是模型檔的完整路徑；這時訊息只顯示檔名，送給伺服器的仍是完整名稱。

### 會傳送哪些內容

使用遠端模型時，下列內容會傳送到你填寫的伺服器：

- 魚魚的說話設定（人設與說話要求）和目前時間。
- 前景 App 的名稱與視窗標題。
- 前景視窗的內容，二選一：
  - **視窗文字**（預設）：在這台 Mac 上用 Apple Vision 辨識出來的文字，不會傳圖片。
  - **視窗截圖**：要勾選截圖選項，而且伺服器支援圖片時才會傳。格式是 JPEG（品質 85），長邊最多 1568 像素，視窗透明的圓角會補成白色。
- 開啟天氣和新聞時，還有天氣地點與預報、新聞內容。

不會傳送的內容：

- 只讀取最前面的那個視窗，不會傳送整個螢幕或其他視窗。本機模型在擷取視窗失敗時會改讀整個螢幕，遠端模型不會。
- 最前面是桌面時，不截圖也不做 OCR，只告訴模型「目前在桌面」。
- 連線測試只傳「你好」和一張 64×64 的空白測試圖，不會傳螢幕上的任何內容。
- API Key 只放在 `Authorization: Bearer` 標頭裡，不會寫進記錄主控台。
- 由 Agent 編寫日常關心時走的是另一條路：截圖或文字交給 Agent，不經過遠端模型。

截圖和 OCR 都需要「螢幕與系統錄音」權限（macOS 14 為「螢幕錄製」，App 的提示會依系統版本寫出正確的名稱）；沒有權限時兩者都會失敗，魚魚只會傳「使用者正在使用電腦。」。

魚魚用三種方式判斷伺服器是否支援圖片，任一個成立就算支援：`/v1/models` 回應的 `data[].architecture.input_modalities` 含有 `image`；llama.cpp 的 `/v1/models` 回應中 `models[].capabilities` 含有 `multimodal`；llama.cpp 的 `/props` 回應中 `modalities.vision` 為 `true`（llama-server 加了 `--mmproj` 時就會是這樣）。模型清單每 5 分鐘最多重新讀取一次。送出截圖後伺服器回覆錯誤時（例如沒有 `--mmproj` 的 llama-server 會回 HTTP 500），魚魚會立刻用同一個視窗的 OCR 文字重送一次，這次說話不會中斷；之後這台伺服器改傳文字，直到你再按一次「儲存並測試連線」或修改遠端設定。

限制：

- 這個功能是為 llama.cpp 的 llama-server 設計的。請求裡有 llama.cpp 專用的欄位（`cache_prompt`、`chat_template_kwargs`、`stream_options`），其他 OpenAI 相容服務可能會回覆 400。
- 自簽憑證的 https 伺服器連不上，請改用 http 或受信任的憑證。
- 每次回覆最多等 100 秒。

### 用 App 內附的 llama-server 當遠端伺服器

App 內附的 llama-server 可以在另一台 Mac（或同一台）當遠端伺服器。下面的例子用 8080 連接埠，不會和魚魚本機模型的隨機連接埠衝突：

```sh
RUNTIME="/Applications/藍色大肥魚.app/Contents/MacOS/runtime"
"$RUNTIME/llama-server" -m ~/Library/Application\ Support/bigbluefish-tw/models/Qwen3.5-4B-heretic-Q4_K_M.gguf \
  --host 0.0.0.0 --port 8080 --api-key test-key --alias qwen --jinja --reasoning off -c 8192
```

- 只在同一台 Mac 上測試時，把 `--host 0.0.0.0` 改成 `--host 127.0.0.1`，網址填 `http://127.0.0.1:8080`。
- 要測截圖，再加上 `--mmproj <視覺投影檔.gguf>`，這時連線測試應該顯示「（支援圖片）」；沒有加時顯示「（僅文字）」。
- 模型檔的位置和檔名請依實際安裝調整。設定頁填入網址、API Key `test-key`、模型名稱 `qwen`（或留空），按「儲存並測試連線」。

## 雲端 AI 服務

魚魚也可以改用 Claude API（Anthropic）、OpenAI API 或 Gemini API（Google）說話。日常關心、天氣、新聞、說話測試，以及 Agent 請魚魚自己的模型說的話（`request_local_speech`、預約到期時由魚魚自己說），都會改由所選的服務產生；這台 Mac 不載入本機模型，也不必先安裝模型。標準版和無MCP版都有這個功能。

開始之前：

- 需要你自己的 API Key，費用由服務依 token 用量向你的帳戶收取。Claude Pro、ChatGPT Plus 這類聊天訂閱方案不包含 API 用量，API 要在各服務的開發者平台另外付款或儲值。
- 每一句話都是一次獨立的請求，不帶對話歷史，服務那邊也不會因此記得魚魚之前說過什麼。
- 這和「連線 Agent」裡的 Claude Code、Codex CLI、Gemini CLI 是兩回事：Agent 使用它們自己的登入和設定，不會用到這裡的 API Key。例外是在「連線 Agent」開啟「Claude Code 改用 Claude API Key（依用量計費）」、「Codex CLI 改用 OpenAI API Key（依用量計費）」或「Gemini CLI 改用 Gemini API Key（依用量計費）」時，魚魚啟動的那個 CLI 會改用這裡同一家的 API Key（見「[連線 Agent 與輸入框附件](#連線-agent-與輸入框附件標準版)」）。

### 1. 取得 API Key

| 服務 | 建立 API Key 的網頁 | Key 的開頭 |
| --- | --- | --- |
| Claude API（Anthropic） | <https://platform.claude.com/settings/keys> | `sk-ant-` |
| OpenAI API | <https://platform.openai.com/api-keys> | `sk-`（例如 `sk-proj-`） |
| Gemini API（Google） | <https://aistudio.google.com/apikey> | `AIza` 或 `AQ.` |

設定頁的「取得 API Key」按鈕會用瀏覽器打開所選服務的這個網頁。建議：

- **為魚魚另外建立一把專用的 Key**，不要和其他程式共用。這樣在服務的用量頁面看得出魚魚用了多少；不想用時，也可以只撤銷這一把，不影響其他程式。
- **在服務的主控台設定花費上限或預算提醒**。下面的「每天最多」只計算這台 Mac 送出的請求次數，不知道實際金額。各服務提供的方式不同，有的超過上限就停止請求，有的只寄送提醒，請以服務的說明為準。

### 2. 在設定頁填寫

設定在「設定 › 模型與資訊」。

1. 「啟用模型說話」要開著。這是魚魚用模型自己說話的總開關，使用雲端 AI 服務時也要開著。
2. 「模型來源」選「雲端 AI 服務（Claude、OpenAI、Gemini）」。切換時會先跳出「改用雲端 AI 服務？」的說明，列出會傳送給服務的內容和金鑰存放的位置；按「改用雲端 AI 服務」後才會切換，按「取消」維持原本的模型來源。
3. 「服務」選「Claude API（Anthropic）」、「OpenAI API」或「Gemini API（Google）」。
4. 把 Key 貼到「API Key」欄。欄位平常會遮住內容，勾選「顯示 API Key」才看得到。貼上時會依 Key 的開頭自動選好服務：`sk-ant-` 是 Claude，`AIza` 和 `AQ.` 是 Gemini，其他 `sk-` 開頭的是 OpenAI API。三個服務的 Key 和模型分開記住，換服務不會刪掉另一家的 Key。
5. 按「儲存並測試」。魚魚會用這把 Key 讀取服務的模型清單：讀得到就表示 Key 有效，並把這把 Key 能用的模型列進「模型」選單。這個測試只讀清單、不產生文字，不花費 token，也不算進每天的請求次數。Key 的開頭和「服務」對不上時（例如服務選「Claude API（Anthropic）」、卻貼了 OpenAI 的 Key），不會儲存，按鈕下方會說明這把 Key 是哪一家的。
6. 視需要調整「模型」和「每天最多」，再按一次「儲存並測試」。改選服務、改 Key 或改模型之後，都要按「儲存並測試」才會生效。

測試成功時，模型狀態會顯示「雲端 AI 服務就緒：Claude（claude-opus-5-5）（API Key 有效，找到 12 個可用模型）」這類文字；魚魚準備說話時，狀態會顯示「雲端生成短句」。

- API Key 存在 macOS 鑰匙圈（見「[鑰匙圈裡的資料](#鑰匙圈裡的資料)」），只能包含英文字母、數字和 `._~+/=:@-` 這些符號，最長 256 個字元；一般的 Key 都符合。
- 還沒填 Key 時，魚魚不會說話，「模型狀態」顯示「尚未填寫 Claude 的 API Key，魚魚先不說話」；App 剛啟動、還在從鑰匙圈讀出 Key 的那幾秒顯示「正在讀取 macOS 鑰匙圈」，讀不到時顯示「無法讀取 macOS 鑰匙圈裡 Claude 的 API Key，魚魚先不說話」。魚魚不會因此改用本機模型。
- 「使用 GPU 加速」和「筆電拔掉電源時暫停模型」只對本機模型有效；使用雲端 AI 服務時，用電池也照常說話。

### 3. 選擇模型與費用

「儲存並測試」成功後，「模型」會預選：你之前選過、而且這把 Key 還能用的模型；沒有的話，選服務的建議模型（Claude 是 Claude Opus 5.5，OpenAI API 是 `gpt-6-luna`，Gemini API 是 `gemini-3.5-flash-lite`）；清單裡也沒有時，選第一個適合聊天的模型。也可以直接輸入清單以外的模型名稱。「模型」留空時，使用服務的建議模型。

Claude 的三個模型：

| 模型 | 每句大約 | 價格（每百萬 token，輸入／輸出） |
| --- | --- | --- |
| Claude Opus 5.5（`claude-opus-5-5`，預設） | US$0.02 | US$4／US$20 |
| Claude Sonnet 5.5（`claude-sonnet-5-5`） | US$0.01 | US$2／US$10 |
| Claude Haiku 5.5（`claude-haiku-5-5`） | US$0.0005 | US$0.10／US$0.50 |

Claude Opus 5.5 是 Anthropic 建議的預設模型；想省錢時可以改選 Sonnet 5.5 或 Haiku 5.5。OpenAI API 與 Gemini API 有估計的模型：

| 服務 | 模型 | 每句大約 |
| --- | --- | --- |
| OpenAI API | `gpt-6-luna`（預選） | US$0.0004 |
| OpenAI API | `gpt-5.6-luna` | US$0.0008 |
| OpenAI API | `gpt-6.1-terra` | US$0.0035 |
| Gemini API | `gemini-3.5-flash-lite`（預選） | US$0.001 |

- 選好模型後，設定頁會在旁邊顯示這個估計。估計依各服務在 2026 年 11 月公布的價格，以一句附視窗截圖的話（約 2,000 個輸入 token、幾百個輸出 token，包括思考）計算；只傳視窗文字時通常更少。其他模型不顯示估計。實際費用以服務的帳單為準。
- 以預設的說話間隔（10–16 分鐘）來算，一小時大約說 4–6 句，用 Claude Opus 5.5 約 US$0.1。「每天最多」設 200 次時，Claude Opus 5.5 一天最多約 US$4，Claude Haiku 5.5 約 US$0.1。
- 魚魚只需要一小句話，所以會請服務少想一點：Claude 的思考強度（effort）設為低，OpenAI API 的推論強度（reasoning effort）設為低，Gemini API 用該系列最低的思考設定；每次最多輸出 4,096 個 token（包括思考）。不送 temperature 這類取樣參數。模型不接受這些設定時，會自動拿掉再送一次，這次重送不另外計次。
- 使用 Claude Opus 5.5、Claude Opus 5、Claude Sonnet 5.5、Claude Fable 5.1 或 Claude Fable 5.2 時，請求會開啟 Anthropic 的伺服器端備援（server-side fallback）：模型婉拒時，由 Anthropic 改用它建議的備援模型，在同一次請求裡重新產生。仍然被婉拒時，魚魚這次就不說話。

### 4. 每天最多幾次

「每天最多」可以選 50、100、200（預設）、500 次或「不限制」。

- 每送出一次請求就算一次，失敗的請求也算；按「儲存並測試」不算。視窗截圖被服務拒收、改傳文字重送時，會再算一次。
- 達到上限後，魚魚當天不再送出請求，也不讀取螢幕，狀態列顯示「今天的雲端請求已達上限（200 次），魚魚先休息，明天會自動恢復；可以在設定 › 模型與資訊調整每天的上限。」。過了這台 Mac 的午夜就會恢復；調高上限也會立刻恢復。
- 設定頁會顯示用量，例如「雲端用量：今天 3 次（上限 200 次），輸入 4,567、輸出 890 個 token；本月 20 次，輸入 31,200、輸出 5,800 個 token。」。token 數取自服務回覆的用量。
- 用量記在資料夾裡的 `cloud-usage.json`：每天一筆請求次數和輸入、輸出 token 數，保留約 100 天，不含金鑰、提示詞或回覆。標準版和無MCP版共用這個檔案；刪掉它只會讓計數歸零。
- 這只是魚魚自己的計數，不是帳單。實際用量與費用請看服務的用量頁面。

### 5. 會傳送哪些內容

使用雲端 AI 服務時，下列內容會傳送到所選服務的官方 API：

| 服務 | 端點 |
| --- | --- |
| Claude API | `https://api.anthropic.com`（透過 Anthropic 官方的 C# SDK） |
| OpenAI API | `https://api.openai.com/v1`（Chat Completions） |
| Gemini API | `https://generativelanguage.googleapis.com/v1beta`（generateContent） |

- 魚魚的說話設定（人設與說話要求）和目前時間。
- 前景 App 的名稱與視窗標題。
- 前景視窗的內容，二選一：
  - **視窗文字**（預設）：在這台 Mac 上用 Apple Vision 辨識出來的文字，不會傳圖片。
  - **視窗截圖**：勾選「傳送前景視窗截圖」時改傳截圖，不傳 OCR 文字。格式和遠端模型相同：JPEG（品質 85），長邊最多 1568 像素。服務以 HTTP 400 或 413 拒收截圖時，魚魚會立刻改用同一個視窗的 OCR 文字重送一次，這次說話不會中斷；之後改傳文字，直到你再按一次「儲存並測試」或修改設定。
- 開啟天氣和新聞時，還有天氣地點與預報、新聞內容。

不會傳送的內容：

- 只讀取最前面的那個視窗，不會傳送整個螢幕或其他視窗；最前面是桌面時，不截圖也不做 OCR，只告訴模型「目前在桌面」。
- 「儲存並測試」只讀取模型清單，不會傳送螢幕上的任何內容。
- 沒有 Key、暫停中或已達每天上限時，不讀取螢幕，也不送出任何請求。
- API Key 只放在送往該服務官方端點的請求標頭裡（Claude API 和 OpenAI API 是 `Authorization: Bearer`，Gemini API 是 `x-goog-api-key`，不會放在網址裡），不會寫進記錄主控台、用量檔、錯誤訊息或 MCP。記錄主控台也不記錄服務回覆的原文，只記下失敗的種類（例如 `invalid_key`、`billing`、`rate_limited`）。
- 由 Agent 編寫日常關心時走的是另一條路：截圖或文字交給 Agent，不經過這裡的雲端 AI 服務。

端點寫在程式裡，不能改成其他網址，也不受 `ANTHROPIC_BASE_URL`、`ANTHROPIC_CUSTOM_HEADERS` 這類環境變數影響；送往其他位址的請求會被擋下，也不跟隨轉址，所以 Key 不會被帶到別的伺服器。送給 OpenAI 的請求帶有 `store: false`，不會把這次的請求和回覆存進 OpenAI 平台的記錄。

服務收到內容之後如何儲存、使用，依你和該服務之間的條款與隱私權政策為準。特別是 Gemini：沒有開啟付費的 Google 專案使用的是免費額度，依 Google 的條款，送出的內容可能被用來改進 Google 的產品，也可能經過人工審閱；不希望這樣時，請在 Google AI Studio 為專案開啟付費。

截圖和 OCR 都需要「螢幕與系統錄音」權限（macOS 14 為「螢幕錄製」）；沒有權限時，魚魚只會傳「使用者正在使用電腦。」。連到這些網際網路上的 https 端點不需要「區域網路」權限。

### 6. 暫停與錯誤訊息

服務回覆錯誤時，狀態列會用中文說明原因（不會顯示 Key 或服務回覆的原文），並依原因暫停送出請求。暫停期間魚魚不讀取螢幕、不送出請求，也不會改用本機模型；換服務、換 Key、換模型，或按「儲存並測試」成功時，暫停立刻解除。只切換截圖選項或調整「每天最多」不會解除暫停。暫停期間，設定頁的雲端用量下方會多一行「雲端請求暫停中，14:05 自動恢復…」，「模型狀態」也維持顯示暫停的原因。

| 狀態列的訊息（節錄） | 原因 | 魚魚怎麼做 |
| --- | --- | --- |
| 尚未填寫 Claude 的 API Key… | 還沒填 Key | 不說話，直到填好 Key |
| 正在讀取 macOS 鑰匙圈、無法讀取 macOS 鑰匙圈裡 Claude 的 API Key… | App 剛啟動還在讀鑰匙圈，或鑰匙圈讀不到 Key（例如登入鑰匙圈還鎖著） | 不說話；讀不到時按「重新讀取鑰匙圈」 |
| Anthropic 不接受這把 API Key…、OpenAI 不接受這把 API Key…、Gemini 不接受這把 API Key… | Key 打錯、被撤銷或停用 | 暫停 30 分鐘 |
| Google 判定這把 Gemini API Key 已外洩並停用了… | Google 停用了這把 Key | 暫停 30 分鐘，要建立新的 Key |
| Anthropic 帳戶的額度不足…、OpenAI 帳戶的額度或花費上限已用完…、OpenAI 帳戶目前沒有啟用…、Gemini 的預付額度已用完…、這個 Google 專案目前不能使用 Gemini 的免費額度…、Google 帳單有未付的款項… | 帳戶沒有餘額、達到花費上限、沒有可用的付款方式或帳單有問題 | 暫停 30 分鐘 |
| 已達到你在 platform.claude.com 設定的 API 花費上限… | 達到你在 Claude Console 為組織或工作區設定的花費上限 | 暫停 30 分鐘；調高或移除上限後按「儲存並測試」 |
| 這把 Claude API Key 沒有使用這個模型的權限…、這把 OpenAI API Key 沒有使用這項服務的權限… | 組織、專案或地區的限制 | 暫停 30 分鐘 |
| 這把 OpenAI API Key 是限制權限的 Key，缺少魚魚需要的權限… | 只開了部分權限的 Key 缺少產生文字（模型使用）的權限 | 暫停 30 分鐘；開啟權限後按「儲存並測試」 |
| Google 依使用政策限制或停用了這個帳號使用 Gemini… | Google 依使用政策限制了這個帳號 | 暫停 30 分鐘 |
| 這把 Claude API Key 不能使用模型 claude-opus-5-5… | 模型名稱打錯、已經停止提供，或這把 Key 不能用 | 暫停 30 分鐘 |
| Claude 的請求太頻繁了，魚魚先安靜到 14:05。 | 達到服務的速率限制（HTTP 429） | 依服務要求的時間暫停，沒有說明時 5 分鐘，最長 30 分鐘 |
| Gemini 今天的請求額度用完了（每天台灣時間下午三點或四點重置）。 | Gemini 免費額度的每日上限 | 暫停 30 分鐘後再試 |
| Claude 目前忙碌中，魚魚先安靜到 14:02。 | 服務過載（HTTP 529 或 503） | 暫停 2 分鐘；服務要求更久時照它的時間 |
| Claude 婉拒了這次的請求…、還沒說出內容就用完了輸出上限…、回覆中途中斷了…、Gemini 這次沒有產生內容… | 這一句沒有可以說的內容 | 這次不說話，下一次照常 |
| 找不到 Claude 的伺服器…、連不上 Claude…、Claude 回應逾時… | 網路問題，或 60 秒內沒有任何回應 | 這次不說話，下一次照常 |
| 今天的雲端請求已達上限（200 次）… | 達到「每天最多」 | 到這台 Mac 的午夜，或調高上限 |
| 這把 OpenAI API Key 沒有讀取模型清單的權限… | 按「儲存並測試」時，只開了部分權限的 Key 沒有讀取模型清單的權限 | 不暫停；可以直接在「模型」輸入名稱，例如 `gpt-6-luna` |

## 鑰匙圈裡的資料

中央氣象署授權碼、遠端模型的 API Key 和雲端 AI 服務的 API Key 存在 macOS 的**登入鑰匙圈**，類型是「一般密碼」：

| 資料 | 服務 | 帳號 |
| --- | --- | --- |
| 中央氣象署授權碼 | `io.github.methrew.bigbluefish.tw` | `CwaApiKey` |
| 遠端模型的 API Key | `io.github.methrew.bigbluefish.tw` | `RemoteApiKey` |
| Claude API Key | `io.github.methrew.bigbluefish.tw` | `ClaudeApiKey` |
| OpenAI API Key | `io.github.methrew.bigbluefish.tw` | `OpenAiApiKey` |
| Gemini API Key | `io.github.methrew.bigbluefish.tw` | `GeminiApiKey` |

- 標準版和無MCP版共用同一筆。`preferences.json` 只記下 `"CwaApiKeyProtected": "keychain"`、`"RemoteApiKeyProtected": "keychain"`（雲端 AI 服務是 `ClaudeApiKeyProtected`、`OpenAiApiKeyProtected`、`GeminiApiKeyProtected`）這類標記，不會有金鑰本身，所以備份或複製資料夾時，金鑰不會跟著外流。
- 開啟「Claude Code 改用 Claude API Key」、「Codex CLI 改用 OpenAI API Key」或「Gemini CLI 改用 Gemini API Key」時，同一家的 API Key 也會交給魚魚啟動的那個 CLI：只放進那個程式的環境變數，不會寫進檔案；但 CLI 本身和它執行的指令可能讀得到（Codex CLI 不會把它放進所執行指令的環境變數，但在沙箱外執行的指令仍可能間接讀到），所以最好另外建立一把只給魚魚用的 Key。
- MCP 的 `get_settings` 看不到這些資料，`update_settings` 也不能寫入。遠端模型的網址、模型名稱、模型來源、截圖選項，以及雲端 AI 服務的服務、各服務的模型和每天上限，可以從 `get_settings` 讀到，但 MCP 和 Agent 都不能修改。
- 鑰匙圈項目由 Apple 內建的 `/usr/bin/security` 建立，App 更新後不會再跳出鑰匙圈授權視窗。相對地，同一個使用者帳號下的其他程式也能透過這個工具讀到它們，保護程度和 Windows 版的 DPAPI 相同。
- 每次存取鑰匙圈最多等 5 秒。鑰匙圈鎖住時（例如只透過 SSH 登入，或啟動時鑰匙圈還沒解鎖），讀不到授權碼和 API Key：臺灣地點改用 Open-Meteo 估計，遠端模型的請求不帶 API Key，雲端 AI 服務沒有 Key 就先不說話，設定頁的金鑰欄位下方會顯示「無法讀取 macOS 鑰匙圈：」和原因。鑰匙圈裡的資料不會因此被刪除。App 會在 30 秒、2 分鐘、10 分鐘後各重讀一次，解鎖螢幕和打開設定時也會重讀；也可以按欄位下方的「重新讀取鑰匙圈」。每次讀取鎖住的鑰匙圈，macOS 可能會跳出解鎖視窗，所以不會一直重試。都讀不到時，重新啟動 App 就會再讀一次。
- 寫入失敗時，設定頁會顯示「無法存入 macOS 鑰匙圈」和原因，設定保持原樣，金鑰也不會改存到檔案裡。只有真的寫入或移除成功，設定頁才會顯示「已存入鑰匙圈」或「已從鑰匙圈移除」。
- 舊版（使用者自己移植的 macOS 版）把金鑰以 Base64 存在 `preferences.json`。第一次讀到時會移進鑰匙圈，並同時改寫 `preferences.json` 和它的備份 `preferences.json.bak`。金鑰含有鑰匙圈不接受的字元（例如空白、`!`、`$`）時，這次執行仍會使用，但會從設定檔移除；設定頁會請你改成鑰匙圈接受的金鑰後重新儲存，否則下次啟動就不會再有。

檢視或刪除：

- 打開「鑰匙圈存取」App，搜尋 `io.github.methrew.bigbluefish.tw`。
- 在設定頁按「移除授權碼」，或清空遠端模型的 API Key 欄後按「儲存並測試連線」、清空雲端 AI 服務的 API Key 欄後按「儲存並測試」（只刪除目前所選服務的 Key）。鑰匙圈讀不到時，空白的 API Key 欄不會被當成要刪除；切換模型來源、服務或截圖選項也不會改動鑰匙圈裡的金鑰。
- 刪除這台 Mac 上的 Key 不會讓它失效。不再使用時，請到服務的網頁撤銷（刪除）這把 Key。
- 在終端機刪除：

  ```sh
  security delete-generic-password -s io.github.methrew.bigbluefish.tw -a CwaApiKey
  security delete-generic-password -s io.github.methrew.bigbluefish.tw -a RemoteApiKey
  security delete-generic-password -s io.github.methrew.bigbluefish.tw -a ClaudeApiKey
  security delete-generic-password -s io.github.methrew.bigbluefish.tw -a OpenAiApiKey
  security delete-generic-password -s io.github.methrew.bigbluefish.tw -a GeminiApiKey
  ```

  回覆 `The specified item could not be found in the keychain.` 表示本來就沒有這筆資料。

## 主動提醒

魚魚自己會提醒，不需要本機模型，也不需要連線 Agent，標準版和無MCP版都有。設定在「設定 › 動作與作息」三餐時間下方的「主動提醒」。

| 提醒 | 選項 | 說明 |
| --- | --- | --- |
| 久坐提醒 | 關閉、連續使用 30／45／60／90／120 分鐘後 | 中間離開 5 分鐘以上才算休息。沒理會的話，每再連續使用同樣長度（至少 30 分鐘）再提醒一次，措辭每次不同。 |
| 用餐提醒 | 開／關 | 早、午、晚餐時間（上方滑桿）整點起 90 分鐘內，你在電腦前時各提醒一次，並播放吃早餐、吃午餐或吃晚餐的動畫。 |
| 深夜提醒 | 關閉、晚上 10 點起、晚上 11 點起、凌晨 0 點起、凌晨 1 點起、凌晨 2 點起 | 到早上 5 點為止，每小時最多一次。 |
| 低電量提醒 | 開／關 | 沒插電且電量剩 20%、10% 時各一次，插上電源後重新計算。iMac、Mac mini、Mac Studio、Mac Pro 沒有內建電池，設定頁會註明不會提醒。 |
| 下雨提醒 | 開／關 | 需要開啟「連網取得天氣和科技新聞」。6–22 點之間，6 小時內降雨機率 60% 以上時提醒帶傘，6 小時內不重複。 |

共同規則：

- 你 60 秒內沒碰鍵盤、滑鼠或觸控式軌跡板時不會提醒；兩則提醒至少相隔 3 分鐘；其他提醒後 15 分鐘內不提醒久坐。
- 下列情況提醒會先等一下：魚魚隱藏中、有程式全螢幕、正在拖曳或點選魚魚、設定或輸入框開著、提線木偶模式、魚魚自己正在準備說話、Agent 正在準備回覆，或 Agent 60 秒內剛讓魚魚說過話。
- 電腦睡眠的時間也算休息：闔上螢幕 40 分鐘回來，久坐時間會重新計算。
- 已經說過的提醒記在資料夾裡的 `cache/reminders.json`，重新啟動魚魚不會重複；重新啟動不到 5 分鐘時，連續使用時間會接續計算。

> 原作者 1.1.3 的 Mac 版讀到的閒置時間會卡在好幾天，所以 Mac 上的自動說話與 Agent 日常關心其實不會觸發。本版改讀系統真正的閒置時間，「自動說話間隔」在 Mac 上才真正開始運作。

## 連線 Agent 與輸入框附件（標準版）

「連線 Agent」頁的用法和支援的 21 款 CLI Harness 見[連線本機 CLI Harness](ACP.md)。Mac 版另外要注意：

- 從 Finder 開啟 App 時，系統只給最基本的 PATH。魚魚會在背景讀取登入 shell（`$SHELL -ilc`，會讀 `.zprofile` 和 `.zshrc`）的 PATH，用來找到 npm、Homebrew 或版本管理工具安裝的 Agent CLI，最多等 5 秒；這段期間魚魚的視窗、選單列圖示和動畫都不會被耽誤，只有 Agent 連線會等 PATH 讀完。PATH 的順序沿用登入 shell 的設定；讀不到時，會補上存在的 `/opt/homebrew/bin`、`/usr/local/bin`、`~/.local/bin`、`~/.volta/bin`、`~/.bun/bin`。剛啟動的前 5 秒內打開「連線 Agent」頁時，偵測結果可能還沒包含登入 shell 的 PATH，重新切換一次頁面即可。
- Agent 要求在終端機登入時，魚魚會打開「終端機」App。按了 Control-C 或關掉終端機視窗時，設定頁會馬上顯示登入被中斷；取消登入不會關掉「終端機」裡正在進行的登入。
- 工作區預設在「文件」資料夾裡；從原版匯入的設定會沿用原本的工作區。
- 選 Claude Code 時可以開啟「Claude Code 改用 Claude API Key（依用量計費）」：魚魚啟動的 Claude Code 改用「設定 › 模型與資訊」雲端 AI 服務裡的 Claude API Key（鑰匙圈的 `ClaudeApiKey`），由 Anthropic 依用量計費；你自己開的 Claude Code 照樣用訂閱登入。這個 Claude Code 使用自己的設定資料夾 `~/Library/Application Support/bigbluefish-tw/agent/claude-api`（不是 `~/.claude`），對話也和用訂閱時分開。啟動時鑰匙圈還沒讀完，會等讀到 Key 再連線；沒有 Key 就不啟動。Claude Code 的用量比魚魚自己說話多很多，請先在 Claude Console 設定支出上限；想一直用比較省錢的 Sonnet 或 Haiku，請按這個選項下方的「開啟設定檔資料夾」，在 Finder 選取的 `settings.json` 加上 `"model": "sonnet"`（或 `"haiku"`；引號要用半形的 `"`），「模型與權限」選的模型只套用到目前這段對話。詳細說明見[連線本機 CLI Harness](ACP.md#claude-code-改用-claude-api-key依用量計費)。
- 選 Codex CLI 時可以開啟「Codex CLI 改用 OpenAI API Key（依用量計費）」：魚魚啟動的 Codex CLI 改用雲端 AI 服務裡的 OpenAI API Key（鑰匙圈的 `OpenAiApiKey`），由 OpenAI 依用量計費；你自己開的 Codex CLI 照樣用原本的登入（例如 ChatGPT 帳號），`~/.codex` 裡的登入資料不會被讀取或修改。這個 Codex CLI 使用自己的資料夾 `~/Library/Application Support/bigbluefish-tw/agent/codex-api`（`CODEX_HOME`），Key 只放在記憶體，不會寫進 `auth.json` 或鑰匙圈，也不會放進 Codex CLI 執行的指令的環境變數（但在沙箱外執行的指令仍可能間接讀到）；對話和用 ChatGPT 帳號時分開。詳細說明見[連線本機 CLI Harness](ACP.md#codex-cli-改用-openai-api-key依用量計費)。
- 選 Gemini CLI 時可以開啟「Gemini CLI 改用 Gemini API Key（依用量計費）」：魚魚啟動的 Gemini CLI 改用雲端 AI 服務裡的 Gemini API Key（鑰匙圈的 `GeminiApiKey`），依 Google 的 Gemini API 方案計費；你自己開的 Gemini CLI 照樣用原本的登入（例如 Google 帳號），`~/.gemini` 裡的登入資料和設定不會被使用或修改（Gemini CLI 仍可能讀到 `~/.gemini/.env` 裡魚魚沒有設定的變數，以及 `~/.gemini/policies` 的沙箱規則）。這個 Gemini CLI 使用自己的資料夾 `~/Library/Application Support/bigbluefish-tw/agent/gemini-api`（`GEMINI_CLI_HOME`），不會碰 macOS 鑰匙圈裡 Gemini CLI 自己存的登入資料；想固定用比較省錢的模型，請按這個選項下方的「開啟設定檔資料夾」，在 Finder 選取的 `settings.json`（在 `gemini-api/.gemini` 裡）加上 `"model": {"name": "gemini-3-flash-preview"}`。詳細說明見[連線本機 CLI Harness](ACP.md#gemini-cli-改用-gemini-api-key依用量計費)。
- 這三個選項在啟動時都會等鑰匙圈讀完；沒有 Key 就不啟動，也不會改用 CLI 自己的登入。

### 附加檔案

雙擊魚魚打開輸入框後，可以用三種方式附加圖片、影片或其他檔案：

- **迴紋針按鈕**：按輸入框左下角的迴紋針，從選擇檔案的視窗挑一個或多個檔案。可以切換「圖片與影片」「圖片」「影片」「所有檔案」；要附 PDF、文字檔等文件時，請選「所有檔案」。選擇檔案的視窗會以獨立視窗出現。
- **拖曳**：把 Finder 裡的檔案拖到輸入框上，框線變藍時放開。
- **貼上**：在輸入框按 ⌘V。在 Finder 拷貝的檔案會用原本的檔案附加；只有圖片的剪貼簿（例如 ⌘⌃⇧4 截圖到剪貼簿、在瀏覽器「拷貝影像」）會存成 PNG 再附加；剪貼簿裡有文字時，照常貼上文字。

附加後，檔案會以縮圖排在文字框下方（iPhone 的 HEIC 照片透過系統內建的 `sips` 產生縮圖；影片與其他檔案顯示圖示和檔名）。滑鼠移到縮圖上會顯示完整路徑與大小，按縮圖右上角的「×」可以移除。

- 一則訊息最多 10 個檔案；不能附加資料夾，同一個檔案不會重複附加。圖片超過 20 MB 時會提醒你，Agent 可能無法直接檢視。
- 只附檔案、不打字也可以傳送。文字上限 4000 字只算你打的字，不含附件。
- 按「傳送」或 ⌘↩（Ctrl+Enter 也可以）。輸入框下方會顯示「已傳送給 Agent（含 2 個附件）」這類結果；附加後被刪除或搬走的檔案會另外說明，其餘照常傳送。傳送失敗時，文字和附件會留在輸入框裡。

交給「連線 Agent」時，每個檔案都以 ACP 的 `resource_link` 交給 Agent，事件內容也會列出每個檔案的本機路徑、類型與大小。Agent 支援圖片輸入時，圖片另外直接附在訊息裡：本來就小的 PNG、JPEG、GIF、WebP 原樣附上，其他的縮小到長邊 1568 像素（HEIC、TIFF 先用 `sips` 轉檔，照片的拍攝方向會轉正）。Agent 不支援圖片，或模型服務拒收圖片時，會自動改成只給連結和路徑。影片和其他檔案由 Agent 需要時用自己的工具讀取。

檔案放在哪裡：

- 選擇、拖曳和在 Finder 拷貝的檔案**不會被複製、搬移或刪除**，Agent 讀的是原本的檔案。
- 貼上的圖片存在 `~/Library/Application Support/bigbluefish-tw/attachments/`，檔名是 `paste-日期-時間-編號.png`。還沒傳送就關掉輸入框、或按「×」移除時，貼上的圖片會立刻刪除；已傳送的保留 7 天，讓 Agent 之後還能回頭看；超過 7 天，或資料夾超過 500 MB 時，從最舊的開始刪除。
- 附件的路徑、檔名與大小會交給收到這則訊息的 Agent 或 MCP 客戶端；附上的圖片內容會送到 Agent 使用的模型服務。Agent 讀取其他檔案時，是以你的身分讀取。

## Hermes 對話橋接與外部 MCP 介面

Hermes 對話橋接程式（`BigBlueFish.HermesBridge`）會把你雙擊魚魚輸入的文字交給本機的 [Hermes Agent](https://github.com/NousResearch/hermes-agent)，再把 Hermes 的回答顯示在魚魚的氣泡裡。等待時魚魚會播放思考動作，氣泡會顯示 Hermes 正在做什麼（例如「正在上網搜尋…（1 分 05 秒）」）。橋接程式也負責每日簡報、離開很久回來時的摘要、Hermes 排程的執行結果，以及 Google 日曆的行程提醒；這些都只在橋接程式執行時才有。標準版 App 已內附橋接程式，無MCP版沒有。

橋接程式透過魚魚的「MCP 介面」控制魚魚。MCP 介面是給本機其他程式用的固定通道（macOS 上是 `/tmp/bbf-<20 碼>` 的 Unix socket），和「連線 Agent」頁內建的 ACP 連線是兩回事：

- **連線 Agent（ACP）**：魚魚自己啟動 Claude Code、Codex CLI、Hermes（`hermes acp`）等 21 款 CLI，有登入與權限核准對話框，不必另外開程式。
- **MCP 介面**：給外部程式（例如 Hermes 橋接程式）使用，預設關閉，開啟時需要確認。

### 1. 準備 Hermes

1. 安裝 Hermes Agent。Hermes 在 macOS 的資料放在 `~/.hermes`（可以用環境變數 `HERMES_HOME` 改到別處）。
2. 在 `~/.hermes/.env` 加入一組 16 字元以上的 API 金鑰，例如用 `openssl rand -hex 24` 產生：

   ```sh
   API_SERVER_KEY=這裡換成你產生的金鑰
   ```

   部分 Hermes 版本另外需要 `API_SERVER_ENABLED=true`。橋接程式只在啟動時從這個檔案讀取金鑰，不會顯示或另外儲存；也可以改用環境變數 `BIGBLUEFISH_HERMES_KEY` 提供。
3. 在 Hermes 的設定檔 `~/.hermes/config.yaml` 加入魚魚的 MCP 伺服器。名稱**必須是 `big_blue_fish`**，因為橋接程式給 Hermes 的說明是用 `mcp__big_blue_fish__…` 呼叫魚魚的工具：

   ```yaml
   mcp_servers:
     big_blue_fish:
       command: "/Applications/藍色大肥魚.app/Contents/MacOS/BigBlueFish.Mcp"
       args: []
   ```

   在魚魚設定開啟 MCP 介面（下一步）後，「連線其他 MCP 軟體」選單選「Hermes 橋接程式」，按「複製設定」就會複製這一段，路徑是你目前執行的 App 裡的 `BigBlueFish.Mcp`。
4. 重新啟動 Hermes gateway（`hermes gateway restart`），確認 `http://127.0.0.1:8642/health` 有回應。

### 2. 在魚魚開啟 MCP 介面

打開「設定 › 連線 Agent」，捲到下方的「MCP 介面（外部 Agent，例如 Hermes 橋接程式）」：

1. 勾選「開啟 MCP 介面」，看完提示後按「瞭解並開啟」。
2. 勾選「雙擊輸入交給 MCP 介面（Hermes 橋接程式）」。
3. 視需要調整「每天第一次坐下時講今日簡報」、「離開很久回來時說期間發生的事」（關閉、30 分鐘、1、2、4 小時）與「Google 日曆行程開始前提醒」（不提醒、5、10、15、30 分鐘）。
4. 不需要開啟「提線木偶模式」。開了的話，魚魚會停止自己說話和切換動作，橋接程式關閉後也要手動關掉才會恢復；它和上面 Agent 連線的「把日常關心的觸發和日常的動作切換邏輯交給 Agent」是同一個開關。

**雙擊輸入只會交給一方**：開啟「雙擊輸入交給 MCP 介面」而且橋接程式正在執行時，輸入只傳給 MCP 介面，上面的 Agent 連線收不到，所以不會兩邊各回答一次；橋接程式沒在執行時，輸入照常交給 Agent 連線。只有接收互動事件的客戶端（橋接程式會呼叫 `logging/setLevel` 設為 info）才算，Hermes 自己的 `big_blue_fish` 連線只呼叫工具，就算還連著也不會接走輸入。點選、拖曳等其他事件兩邊都會收到。

### 3. 執行橋接程式

在終端機執行：

```sh
code/run-hermes-bridge.sh
```

- 腳本會依序在 `release/`、`/Applications`、`~/Applications` 找到 App 內附的橋接程式（`藍色大肥魚.app/Contents/MacOS/BigBlueFish.HermesBridge`）直接執行，不需要安裝 .NET SDK；也可以直接執行 App 裡的這個檔案。
- 找不到 App，或加上 `--build` 時，腳本會用 .NET 10 SDK 從原始碼編譯（`-r osx-arm64`）再執行。
- `--log` 會同時把輸出寫到 `~/Library/Logs/BigBlueFish/hermes-bridge.log`。
- 其他參數會原樣交給橋接程式，例如 `--mcp <BigBlueFish.Mcp 路徑>`、`--hermes-url <網址>`；`--help` 列出全部參數。不在這台電腦上的 Hermes 必須使用 https。
- 關閉終端機視窗、按 Ctrl+C，或由 launchd 停止（SIGTERM）時，橋接程式都會正常結束。同一個使用者同時只能執行一個橋接程式。

在輸入框打字後按「傳送」或 ⌘↩。特殊指令：`/new` 開始新的對話、`/stop`（或 `/停止`）請 Hermes 停止目前的工作（連同它開出來的背景程式）、`/jobs`（或 `/排程`）列出 Hermes 的排程。進度氣泡不會寫進聊天記錄。

橋接程式的狀態（對話編號、已說過的排程回報、今天是否已經簡報等）存在 `~/Library/Application Support/bigbluefish-tw/hermes-bridge.json`（可以用 `--state` 或 `BIGBLUEFISH_DATA` 改位置）。

### Google 日曆（選用）

行程提醒和簡報裡的行程，需要另外替 Hermes 安裝 `google-calendar` 技能並完成授權（只需要日曆權限）。橋接程式用 Hermes 自己的 Python 執行這個技能：會依序找 `~/.hermes/hermes-agent/venv/bin/python`、`.venv/bin/python`，再找 `hermes` 指令實際使用的 Python；找不到時可以用環境變數 `BIGBLUEFISH_CALENDAR_PYTHON` 指定。沒有設定時，行程相關的功能會自動略過。

### Mac 上的 Hermes

- 橋接程式給 Hermes 的說明會依這台 Mac 改寫：終端機是 zsh、沒有 PowerShell、檔案管理員是 Finder、刪除的檔案放到垃圾桶、可以用 `open` 與 `mdfind`，並提醒 Hermes 遇到隱私權限制時請你到「系統設定 › 隱私權與安全性」允許。
- 說明只會提到魚魚在這台電腦上真的有的工具（橋接程式連上時讀取魚魚的工具清單）。macOS 標準版的魚魚有 `get_context`、`user_returned`、自己的主動提醒，以及 `walk_to`、`find_on_screen`（需要「輔助使用」權限）。使用者說「這個資料夾」時，Hermes 會先看 `get_context` 回傳的 Finder 資料夾（需要「自動化 › Finder」權限）。
- 附加的圖片太大時，橋接程式用系統內建的 `sips` 縮小並轉成 JPEG（也能轉 iPhone 的 HEIC 照片）；有透明背景的 PNG 轉成 JPEG 後背景會變成單色。

### 安全性提醒

- 開啟 MCP 介面後，這台電腦上以你的帳號執行的程式，都可以透過 `BigBlueFish.Mcp` 擷取螢幕畫面、讀取與修改魚魚的設定，並收到你在魚魚輸入框打的字。
- Hermes 回答時可以使用它所有的工具組；透過 API 伺服器，terminal、file 等工具**不需要你核准**就能以你的身分執行指令、修改檔案。能在魚魚輸入框打字的人，就能請 Hermes 做這些事。橋接程式啟動時若發現 Hermes 開著這類工具組，會先顯示提醒。訊息也會送到 Hermes 設定的模型服務。
- 請只在自己的電腦上使用，視需要在 Hermes 的設定中限制 API 平台可用的工具組，不用時關閉「開啟 MCP 介面」。
- 同時開啟「連線 Agent」裡的 Hermes（`hermes acp`）時，請不要讓兩邊都負責日常關心或提線木偶模式，避免同一件事被兩個 Hermes 對話各做一次。

## 在畫面上找東西，再讓魚魚跑過去（標準版）

連線的 Agent 可以用 MCP 工具 `find_on_screen` 依名稱找出畫面上看得到的東西：桌面圖示（附檔案的完整路徑）、視窗標題、Dock 項目，以及每個看得到的視窗裡的按鈕、分頁、清單項目、連結和文字；再用 `walk_to` 讓魚魚小步快跑（水平為主）或漂浮踏步（垂直為主）到目標旁邊，站在目標的左邊或右邊（放不下時改到下方或上方），不會擋住目標，抵達後面向它並停在那裡。用 `stops` 可以一次依序跑過最多 20 個目標。參數與座標見[內部工具參考](MCP.md#使用者狀況找畫面與走過去)。

- 被其他視窗擋住的、捲到畫面外的，以及魚魚自己的視窗都不算。符合的桌面圖示都被視窗擋住時，Agent 會請你先顯示桌面（按 F11，筆電可能要按 fn＋F11，或在觸控式軌跡板上用拇指和三指張開）再找一次。關閉 Finder 的桌面項目，或使用「幕前調度」隱藏桌面項目時，找不到桌面圖示。
- 每次最多讀 6 秒、40 個視窗，每個視窗最多 3,000 個有名稱的元素；來不及讀完的視窗會列在結果裡，通常再呼叫一次就讀得到。Electron、Chromium 類的 App 不一定對外提供完整的介面元素，可能只找得到視窗標題。
- 你點一下或拖曳魚魚、開啟設定、修改位置或大小、隱藏魚魚或有程式全螢幕、接上或拔掉螢幕、關閉 MCP 或關閉魚魚，都會讓魚魚停在當下的位置。跑動時會收起雙擊輸入框：框裡已經有打到一半的文字或附件時只是暫時藏起來，跑完（或整趟跑完）就會回來，草稿和貼上的圖片都還在。行走中魚魚自己的主動提醒會先等一下。

## 繁體顯示、字型與 OCR

- 對話氣泡的文字（本機模型、遠端模型或雲端 AI 服務的回覆、Agent 透過 MCP `say`／`say_next` 說的話、Hermes 的回答、問候與提示訊息）顯示前都會用內建的 OpenCC 1.4.2 字典轉成台灣繁體。已經是繁體或英文的文字維持原樣；簡繁混雜的句子只改簡體的部分。模型邊產生邊顯示時，最後一段還沒寫完的中文會先暫停顯示，等下一段文字到了再一起轉換，避免先閃出錯字，所以氣泡有時會短暫變短，屬於正常現象。MCP `say` 回傳給 Agent 的 `text` 仍是 Agent 送出的原文。
- 氣泡字型是 **jf open 粉圓 2.1**（justfont，SIL OFL 1.1）；粉圓沒有的字用系統內建的「蘋方-繁（PingFang TC）」補上。設定視窗、右鍵選單、輸入框、提示框等介面預設使用 PingFang TC，不會因為系統語言是英文或簡體中文而變成簡體字形；記錄主控台使用 Menlo 加 PingFang TC。系統語言是繁體中文（臺灣）時，標點也是置中的臺灣字形：App 以不帶語言的文化排字，因為排字引擎只會把 zh-TW 當成「zh」，再套用簡體中文的字形。選單列圖示的選單由 macOS 自行繪製，字型依系統語言決定。
- 106 個動作的名稱（也是動畫檔名）維持原本的簡體字，`list_actions` 列出的也是簡體名稱；Agent 用繁體寫動作名稱（例如「點擊回應-元氣揮手」或「點選回應-元氣揮手」）也會播放同一個動作。
- Apple Vision 文字辨識依序使用繁體中文、簡體中文與英文；這台 Mac 的 Vision 不接受這個組合時，會自動改回簡體中文加英文。畫面中不含中文的那幾行，會再用純英文模式辨識一次，結果比較準確（例如 `API` 不會變成 `APl`）。在 macOS 27 上用 13 px 的英文介面文字測試，完全正確的行數從 7/12 提高到 11/12，每次讀取大約多花 60 毫秒；最低支援的 macOS 14 還沒有實測。

## Mac 版的改進

- **App 變小**：macOS 版一律播放 640×360 的相容動畫，所以 App 只內含 `assets/fish/compat/` 和 `animations.json`，不再內含 macOS 從不播放的 1280×720 動畫，從約 1.0 GiB 降到約 0.2 GiB。
- **點選和拖曳的反應更即時**：5 個點選動作和拖曳動作的開頭 0.25 秒會在啟動後先解碼好（約 33 MB 記憶體）；第一次 `walk_to` 時也會預先解碼走路片段的開頭（約多 22 MB）。點選或拖曳時會先播出這段開頭，同時準備後面的動畫，不會停在上一個畫面。
- **在所有桌面空間顯示**：切換桌面空間時，桌寵、對話氣泡和輸入框會跟著過去，Mission Control 也不會移動它們，按 ⌘` 切換視窗時會略過桌寵。預設開啟，可以在「設定 › 桌寵與顯示 › 在所有桌面空間顯示桌寵」關閉。「全螢幕時自動隱藏桌寵」開著的時候（預設），桌寵不會出現在全螢幕 App 上方；關閉後，桌寵也會顯示在全螢幕 App 上方。
- **不會搶走鍵盤**：點一下或拖曳桌寵之後，鍵盤焦點會回到原本正在使用的 App。如果這次點選打開了輸入框、設定或提示視窗，焦點就留在桌寵這邊。
- **透明的地方不擋點選**：動畫切換或暫停時，游標下的點選穿透也會每 40 毫秒更新一次；App 剛啟動、第一個畫面還沒出現之前，整個桌寵視窗都會讓點選穿透。
- **隱藏後再顯示不會重來一次**：從選單列或右鍵選單隱藏後再顯示，或離開全螢幕 App 後，不會再重播開場動畫和問候，也不會重新建立選單列圖示或重新檢查更新。
- **讀取螢幕更省電**：截圖後的解碼、縮放和編碼改在背景執行，截圖時動畫不再停頓；前景視窗和全螢幕的判斷改成每 250 毫秒才列舉一次所有視窗。只有 Finder 在最前面、而且沒有開啟視窗時，才會當成「桌面」；只有選單列圖示或浮動面板的 App 在最前面時，會沿用上一個視窗。
- **GPU 偵測**：新安裝後第一次偵測 GPU 時，系統會先對 llama-server 做安全性掃描（M4 上實測 20–24 秒），現在最多等 60 秒，第一次使用就能用 Metal。設定頁的推論裝置會正確顯示「CPU」或「GPU：Apple M4（MTL0）」這類名稱。

## 登入時自動開啟

- 在「設定 › 桌寵與顯示」打開「登入時自動開啟」，會在 `~/Library/LaunchAgents` 建立本版自己的登入項目（檔名見上方「本版的身分」），標準版和無MCP版各一個，不會互相蓋掉，也不會動到原版的登入項目。
- App 直接從 DMG 執行，或被 macOS 暫時搬到別的位置執行（App Translocation）時，登入後會找不到它，所以這時會拒絕設定，並提示先把 App 拖到「應用程式」檔案夾、再從那裡重新開啟。開關會恢復成實際的狀態。放在外接磁碟上的正常安裝不受影響。
- 每次啟動時，如果登入項目指向的 App 已經不在原處（例如搬過位置），會自動改成目前這一份；指向另一份仍然存在的 App 時不會更動。
- 舊的登入項目（原版的 `io.github.bai-cute.bigbluefish.plist`，或舊的台灣 Mac 移植版的 `io.github.bigbluefish.pet*.plist`）只有在它啟動的正是這一份 App 時，才會被移除並換成本版的登入項目；指向原版 App 的登入項目一律不動。

## 更新

- 檢查更新只看本版的發布頁面 [Methrew/big-blue-fish-desktop-pet/releases](https://github.com/Methrew/big-blue-fish-desktop-pet/releases)，不會把本版換成原作者的版本。可以在「設定 › 關於與更新」按「檢查更新」，或打開「每次啟動時自動檢查更新」。
- 版本依「原版版本號＋台灣版修訂號」比較：`1.1.3` < `1.1.3-tw.1` < `1.1.3-tw.2` < `1.1.4`。
- 還沒有發布任何正式版本時，啟動時的自動檢查不會出聲；手動檢查會顯示「目前還沒有發布任何正式版本」。
- macOS 版不會在 App 裡自動替換。發現新版時按提示開啟發布頁面，下載 `BigBlueFish-TW-v<版本>-macOS-arm64.dmg`（無MCP版是 `BigBlueFish-TW-v<版本>-Safe-macOS-arm64.dmg`），結束目前的藍色大肥魚，打開 DMG，把新版拖到「應用程式」檔案夾取代舊版。設定和模型在資料夾裡，不受影響。取代後如果讀不到畫面，請照「[權限](#權限)」一節重新授權。
- 從原始碼建置的使用者，拉取新的原始碼後重新執行建置步驟即可。

## 解除安裝

1. 從選單列圖示選「關閉桌寵」，結束藍色大肥魚。
2. 在「設定 › 桌寵與顯示」關掉「登入時自動開啟」，或直接刪除登入項目：

   ```sh
   rm -f ~/Library/LaunchAgents/io.github.methrew.bigbluefish.tw.plist ~/Library/LaunchAgents/io.github.methrew.bigbluefish.tw.safe.plist
   ```

3. 把 `藍色大肥魚.app`（和 `藍色大肥魚無MCP版.app`）從「應用程式」檔案夾移到垃圾桶。
4. 不再需要設定和模型時，刪除資料夾（模型可能有好幾 GB）：

   ```sh
   rm -rf ~/Library/Application\ Support/bigbluefish-tw
   ```

   如果還留著 `~/Library/Application Support/.bigbluefish-tw.importing` 或 `.bigbluefish-tw.import-lock`（匯入中斷時才會有），也可以一併刪除。
5. 刪除鑰匙圈裡本版儲存的金鑰：照「[鑰匙圈裡的資料](#鑰匙圈裡的資料)」一節刪除 `CwaApiKey`、`RemoteApiKey`、`ClaudeApiKey`、`OpenAiApiKey` 與 `GeminiApiKey`，或打開「鑰匙圈存取」搜尋 `io.github.methrew.bigbluefish.tw` 後刪除。用過雲端 AI 服務的話，也請到 Claude、OpenAI 或 Google AI Studio 的網頁撤銷給魚魚用的 API Key。
6. 移除系統權限：在「系統設定 › 隱私權與安全性」的「螢幕與系統錄音」「輔助使用」「自動化」選取藍色大肥魚後按「−」，或執行：

   ```sh
   tccutil reset ScreenCapture io.github.methrew.bigbluefish.tw
   tccutil reset ScreenCapture io.github.methrew.bigbluefish.tw.safe
   tccutil reset Accessibility io.github.methrew.bigbluefish.tw
   tccutil reset AppleEvents io.github.methrew.bigbluefish.tw
   ```

7. Hermes 橋接程式的記錄檔在 `~/Library/Logs/BigBlueFish`（有用 `--log` 時才有）。Agent 的工作區放在「文件」資料夾裡，裡面是你自己的檔案，需要時再自行刪除。

原版藍色大肥魚的 App、資料夾 `~/Library/Application Support/bigbluefish` 和登入項目都不受上面的步驟影響。

## 從原始碼建置

### 1. 準備（一次）

```sh
xcode-select --install          # clang、swiftc
brew install --cask dotnet-sdk  # 或安裝到 ~/.dotnet，需要 .NET 10 SDK
brew install node               # 只有連線 Claude Code、Codex CLI、Pi 等需要轉接器的 Agent 時才需要 npm
```

需要 Apple Silicon Mac、macOS 14 或更新版本，以及 Xcode 命令列工具附的 `python3`。第一次建置會下載固定版本的 llama.cpp（b10809，macOS arm64、Metal）與 GStreamer 元件，核對 SHA-256 後快取在 `.work/macos-download`。

### 2. 建置

```sh
tools/build-macos.sh            # 標準版
tools/build-macos.sh Safe       # 無MCP版
```

這台 Mac 裝了多個 .NET 時，指定 .NET 10：

```sh
DOTNET=~/.dotnet/dotnet tools/build-macos.sh
```

產生的 App 在 `release/藍色大肥魚.app`（無MCP版是 `release/藍色大肥魚無MCP版.app`）；第二個參數可以指定其他輸出資料夾。標準版另外內含 `BigBlueFish.Mcp` 與 Hermes 橋接程式。App 名稱、Bundle ID 與致謝都從 `code/BigBlueFish.Windows/Companion/CompanionEdition.cs` 讀取。

`python3 tools/Verify-MacPackage.py "release/藍色大肥魚.app" --edition Standard --output <輸出資料夾>` 會檢查 App 的內容（相容動畫 106 個、沒有 1280×720 動畫、輔助程式是 arm64 等），並以測試模式和暫存資料夾短暫啟動 App；執行前請先關閉正在執行的魚魚（包括原版）。發布用的 DMG 由 GitHub Actions 的「macOS package」工作流程建置、驗證並產生 `.sha256`。

### 3. 執行

```sh
open release/藍色大肥魚.app
```

- 從原始碼建置的 App 同樣只有臨時簽章；讀取螢幕和截圖需要「螢幕與系統錄音」權限，每次重新建置後可能要重新授權。
- 主控台測試的執行方式見[開發說明](../code/README.md)。

## 疑難排解

| 狀況 | 處理方式 |
| --- | --- |
| 第一次打開時 macOS 說無法打開或無法驗證開發者 | 到「系統設定 › 隱私權與安全性」，在下方按「強制打開」。 |
| 從終端機執行 `BigBlueFish.HermesBridge` 或 `BigBlueFish.Mcp` 被擋下 | 下載的 App 帶有 `com.apple.quarantine` 標記。先在 Finder 打開 App 一次，或執行 `xattr -dr com.apple.quarantine "/Applications/藍色大肥魚.app"`。`run-hermes-bridge.sh` 偵測到這個標記時也會提醒你。 |
| 已經勾選「螢幕與系統錄音」（macOS 14 為「螢幕錄製」），還是讀不到螢幕 | 更新或重新建置後授權可能已經失效：把開關關掉再打開，或用「−」移除後再用「＋」加回 App，然後重新開啟藍色大肥魚。 |
| `find_on_screen` 只找到視窗標題 | 還沒允許「輔助使用」，或授權在更新後失效；請照上面的方式重新允許。Electron、Chromium 類的 App 本來就可能只找得到視窗標題。 |
| `get_context` 的 Finder 資料夾清單是空的 | 結果裡的 `openFoldersNote` 會說明原因，例如還沒允許「自動化 › Finder」、你拒絕了詢問，或 Finder 逾時沒有回應。 |
| 遠端模型顯示「無法連線遠端模型」 | 檢查網址、伺服器是否在執行、防火牆，以及「區域網路」權限。 |
| 雲端 AI 服務顯示「不接受這把 API Key」 | Key 貼錯、前後多了字元，或已經被撤銷。到服務建立 Key 的網頁確認，必要時建立新的 Key，貼上後按「儲存並測試」。Google 判定 Gemini 的 Key 外洩時會直接停用它，只能建立新的 Key。 |
| 雲端 AI 服務顯示額度不足、花費上限已用完或帳單有問題 | 到服務的帳單頁面儲值、付款或調高上限（Claude API 在 Claude Console，OpenAI API 在 OpenAI Platform，Gemini API 在 Google AI Studio），再按「儲存並測試」。沒有按的話，魚魚會先暫停 30 分鐘再試。Gemini 顯示不能使用免費額度時，要為專案開啟付費。 |
| 雲端 AI 服務顯示「請求太頻繁了，魚魚先安靜到 …」 | 達到服務的速率限制，魚魚會自己等到顯示的時間再試，不需要處理。常常出現時，把「自動說話間隔」調長，或到服務的主控台確認帳戶的使用等級。Gemini 免費額度的每日上限用完時，要等到每天的重置時間（台灣時間下午三點或四點），或為專案開啟付費。 |
| 雲端 AI 服務顯示「不能使用模型 …」或「模型清單裡沒有 …」 | 模型名稱打錯、模型已經停止提供，或這把 Key 沒有權限。按「儲存並測試」重新讀取清單，再從「模型」選單選一個；清空「模型」就會使用服務的建議模型。 |
| 選了雲端 AI 服務，魚魚卻不說話 | 看「設定 › 模型與資訊」的模型狀態：可能還沒填 Key、鑰匙圈讀不到 Key、正在暫停，或今天的請求已經達到「每天最多」的次數。 |
| 臺灣地點沒有天氣，或顯示中央氣象署不接受授權碼 | 到「設定 › 模型與資訊 › 天氣地點」重新貼上授權碼並按「儲存並測試」。鑰匙圈鎖住時會改用 Open-Meteo 估計：解鎖螢幕、打開設定或按「重新讀取鑰匙圈」後會再讀一次，也可以重新啟動 App。 |
| 開啟本版後跳出「已經有一隻魚在執行」 | 兩個版本（以及原版）同一時間只能執行一隻，後開的不會啟動。先從選單列的魚魚圖示選「關閉桌寵」，再開另一個版本。 |
| 「登入時自動開啟」打不開 | App 正在從 DMG 或暫時的位置執行。把 App 拖到「應用程式」檔案夾，再從那裡開啟後設定。 |
| 連線 Agent 時找不到剛用 npm 或 Homebrew 裝好的 CLI | 剛啟動的前 5 秒內登入 shell 的 PATH 可能還沒讀完；重新切換一次「連線 Agent」頁，或按「重新偵測」。 |
| 想知道從原版匯入了什麼 | 看資料夾裡的 `import-from-original.txt`，或「設定 › 關於與更新」。 |
| 其他問題 | 「設定 › 記錄主控台」會列出最近的事件與錯誤，回報問題時可以一起附上。 |
