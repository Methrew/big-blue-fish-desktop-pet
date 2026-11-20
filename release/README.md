# 藍色大肥魚 · 1.1.3-tw.3（台灣繁體中文版）

這是以原作者 Bai-Cute 的藍色大肥魚 1.1.5 為基礎、整合 acer1204 台灣版功能的**非官方**台灣繁體中文版。原作者的 1.1.4、1.1.5 只發布 Windows 與 Linux 版，官方 macOS 版仍是 1.1.3；本版的 Mac 版用 1.1.5 的原始碼建置。本版不是由原作者或 acer1204 維護，使用上的問題請回報到 [Methrew/big-blue-fish-desktop-pet](https://github.com/Methrew/big-blue-fish-desktop-pet/issues)。

## 下載

| 版本 | macOS 14 以上 · Apple Silicon |
| --- | --- |
| 標準版 | `BigBlueFish-TW-v1.1.3-tw.3-macOS-arm64.dmg` |
| 無MCP版 | `BigBlueFish-TW-v1.1.3-tw.3-Safe-macOS-arm64.dmg` |

每個 DMG 都附有 `.sha256` 檢查碼檔。打開 DMG 後把 App 拖到「應用程式」檔案夾；App 只有臨時簽章，第一次打開時請到「系統設定 › 隱私權與安全性」按「強制打開」。安裝、權限與解除安裝的說明見本版儲存庫的 `docs/MACOS.md`（[專案頁面](https://github.com/Methrew/big-blue-fish-desktop-pet)）。

本版不提供 Windows 與 Linux 的安裝程式或套件。

## 這一版的更新（1.1.3-tw.3）

- **Agent 沒有回覆時會告訴你**：交代的事情、日常關心、天氣或說話測試交給 Agent 後，Agent 這一輪已經結束或出錯、卻沒有透過魚魚回覆時，魚魚會說「主人，Agent 結束工作了，但沒有回覆。」；連線意外中斷時，會說「主人，Agent 的連線中斷了。」。
- **Agent 會回報進度**：交代較長的工作後，Agent 會先透過魚魚說要做什麼，工作中適度回報進度（只顯示在氣泡），完成或做不到時再回報結果。
- **筆電拔掉電源時也暫停 Agent**：標準版的設定從「筆電拔掉電源時暫停模型（只對本機模型有效）」改成「筆電拔掉電源時暫停本機模型和 Agent」（在「設定 › 模型與資訊」，預設開啟），使用遠端模型或雲端 AI 服務時也可以調整。用電池時，除了暫停本機模型，也會關閉魚魚啟動的 Agent 程式，接上電源後再接回原本的對話；已經打開的輸入框和裡面的草稿、附件都會留著。遠端模型、雲端 AI 服務和外部 MCP 介面（例如 Hermes 橋接程式）照常，但交給 Agent 的日常關心（包括 Agent 預約的關心）要等接上電源後才會恢復。**升級後**，這個設定開著時（預設開啟），筆電一拔掉電源，魚魚啟動的 Agent 就會停下手上的工作；想讓 Agent 用電池時繼續工作，請關閉這個設定。無MCP版的設定改名為「筆電拔掉電源時暫停本機模型」（原本是「筆電拔掉電源時暫停模型（只對本機模型有效）」），行為不變。
- **可以連線 DeepSeek Harness Desktop**：「連線 Agent」多了 DeepSeek Harness Desktop，直接使用桌面版內附的 CLI，不必另外安裝 `dsh`。Mac 版會在「應用程式」資料夾和個人專屬的「應用程式」資料夾尋找桌面版。這個連線方式在 Mac 上還沒有實際測試。
- **動畫檔變小**：原作者重新壓縮了 1280×720、每秒 60 格的高畫質動畫，畫面大小、影格數和透明度都不變，高畫質動畫檔總共從約 862 MB 降到約 389 MB（約少 55%）。App 與 DMG 的大小待重新量測。
- **只有 Windows 與 Linux 的變更**（本版不提供 Windows 與 Linux 的安裝程式）：Windows 的截圖與錄影時隱藏魚魚、滑鼠移到魚魚身上時的手掌游標，以及避免舊版 Windows 一啟動就結束的 CET 相容設定；Windows 與 Linux 的更新程式改用 GitHub 提供的檔案摘要核對下載；Linux 另外讀取互動 shell 的 PATH，找得到只在終端機設定好的 CLI。macOS 版的檢查更新只打開發布頁面，不受影響。
- 已安裝 1.1.3-tw.1、1.1.3-tw.2 或 1.1.3-tw.3 時，按「關於與更新 › 檢查更新」會看到這一版；下載 DMG 後結束魚魚，把新版拖到「應用程式」取代舊版即可，設定和模型不受影響。從 1.1.3-tw.1 或 1.1.3-tw.2 更新時，也會一起得到之後各版的高畫質動畫、對話氣泡、拖放檔案和輸入框修正。

## 和原版相比

原作者的 macOS 版仍是 1.1.3，下面和 Mac 有關的比較都是對原版 1.1.3 的 Mac 版。

- **台灣繁體中文**：設定、選單、提示視窗、模型安裝與更新視窗、Agent 技能與人設、文件都改成台灣繁體中文與台灣用語。氣泡文字顯示前會用 OpenCC 1.4.2 的字典轉成台灣繁體，氣泡字型改用 jf open 粉圓 2.1，介面用蘋方-繁。macOS 的 OCR 先以繁體中文辨識，英文行另外重新辨識。Agent 用繁體寫動作名稱也認得。
- **臺灣鄉鎮天氣**：國家清單和原版相同（包括中國清單裡的臺灣省），另外多一個國家「臺灣 Taiwan」（22 個縣市、368 個鄉鎮市區），預設地點是臺北市中正區。填入中央氣象署授權碼時使用氣象署的鄉鎮天氣預報，授權碼存在 macOS 鑰匙圈；沒有授權碼時改用 Open-Meteo 估計。地名以繁體字顯示。
- **自己的身分**：App 名稱「藍色大肥魚」、Bundle ID `io.github.methrew.bigbluefish.tw`、資料夾 `~/Library/Application Support/bigbluefish-tw`，檢查更新只看本版的發布頁面。第一次啟動時會從原版的資料夾唯讀匯入一次設定、模型選擇和 Agent 資料（模型用 APFS 複本）。可以和原版同時安裝，但同一時間只會執行一隻魚。
- **雲端 AI 服務（本版新增）**：「模型來源」可以改選 Claude API（Anthropic）、OpenAI API 或 Gemini API（Google），用自己的 API Key 讓魚魚說話，不必安裝本機模型。API Key 存在 macOS 鑰匙圈，MCP 與 Agent 讀不到（只有開啟「Claude Code 改用 Claude API Key」、「Codex CLI 改用 OpenAI API Key」或「Gemini CLI 改用 Gemini API Key」時，魚魚啟動的那個 CLI 會拿到同一家的 Key）；可以限制每天最多送出幾次請求（預設 200 次），費用由服務依用量向你的帳戶收取。
- **從台灣版移植到 Mac**：遠端 llama.cpp 模型（API Key 存在鑰匙圈）、魚魚自己的主動提醒（久坐、用餐、深夜、低電量、下雨）、`get_context` 與 `user_returned`、`find_on_screen` 與 `walk_to`、Hermes 對話橋接（標準版 App 內附）、外部 MCP 介面，以及輸入框附件（以 ACP 的 `resource_link` 交給 Agent）。
- **Mac 版的改進**：改用 Mac 的硬體解碼（VideoToolbox）播放原作者提升到 1280×720、每秒 60 格的動畫（原版 Mac 版一直只播 640×360、每秒 24 格的相容動畫），想省電可以在「設定 › 桌寵與顯示」關閉；修正讀不到真正閒置時間、導致自動說話與 Agent 日常關心不會觸發的問題；桌寵跟著切換桌面空間、不搶走鍵盤焦點；點選與拖曳的動作開頭預先解碼；第一次偵測 GPU 時能用 Metal；從 Finder 開啟時也找得到 npm 或 Homebrew 安裝的 Agent CLI；可以把 Finder 裡的檔案直接拖到魚魚身上，打開輸入框並附加給 Agent。
- **拔掉電源時的行為**：原作者 1.1.4 起，拔掉電源時會暫停魚魚所有的 AI 功能；本版只暫停本機模型和魚魚啟動的 Agent，遠端模型、雲端 AI 服務和外部 MCP 介面照常。
- 支援的 22 款 CLI Harness（包括 Claude Code）和原作者 1.1.5 相同，包括 1.1.4 新增的 DeepSeek Harness Desktop。

## 已知限制

- Windows 與 Linux 的原始碼已經合併並確認可以編譯，但沒有實際執行或測試。
- App 只有臨時簽章，沒有經過 Apple 公證。每次更新後，macOS 可能要重新允許「螢幕與系統錄音」「輔助使用」「自動化」；更新後會不會再詢問「區域網路」權限，還沒有驗證過。
- 更新不會在 App 裡自動完成：發現新版時請到發布頁面下載 DMG，自己取代舊版。
- 高畫質動畫每秒要送出 60 格畫面，CPU 用量比相容動畫多：1.1.3-tw.2 在 M4 上量到整個 App 約佔一顆核心的 43%，相容動畫約 23%（多在省電的效率核心上）；原作者重新壓縮動畫後的數字待重新量測。Mac 無法硬體解碼時會自動改播相容動畫。App 內含兩套動畫，大小待重新量測（1.1.3-tw.3 約 1.1 GiB）。
- DeepSeek Harness Desktop 的連線方式是原作者 1.1.4 新增的，原作者的 Mac 版沒有這個功能，本版在 Mac 上也還沒有實際測試；連不上時，請改裝 `dsh` 命令列工具。
- 截圖與錄影時隱藏魚魚、手掌游標只有 Windows 版有；macOS 版會記下 `HideFromCapture` 這個設定，但沒有作用。
- 地名只換字形、不換譯名，所以仍會看到「意大利」「新西蘭」這類原版資料的譯名。106 個動作名稱是動畫檔名，維持簡體字。
- 科技新聞的來源仍是 IT之家，由模型用台灣繁體中文轉述。
- 遠端模型功能是為 llama.cpp 的 llama-server 設計的，其他 OpenAI 相容服務可能不接受請求；自簽憑證的 https 伺服器連不上。
- macOS 的輸入框沒有聊天記錄（Windows 版才有）。選單列圖示的選單字型依系統語言決定。
- OCR 的英文改善只在 macOS 27 上實測，最低支援的 macOS 14 還沒有測試。
- 原作者的功能規格（`docs/AGENT_FUNCTIONAL_SPEC.md`、`docs/specs/`）維持簡體中文原文，描述的是原版的行為。

## 致謝

- 原作者 **Bai-Cute**：[Bai-Cute/big-blue-fish-desktop-pet](https://github.com/Bai-Cute/big-blue-fish-desktop-pet)，以及在原版 1.1.3 貢獻 macOS 移植版的 **YamaFuso**、在原版 1.1.4 貢獻 DeepSeek Harness Desktop 連線與手掌游標的 **kiluahh**。
- 台灣版 **acer1204**：[acer1204/big-blue-fish-desktop-pet 的 taiwan-edition 分支](https://github.com/acer1204/big-blue-fish-desktop-pet/tree/taiwan-edition)。
- 台灣 macOS 版整合：**Methrew**。
- 角色動畫取自 [PC2005-cloud/dsh-pet](https://github.com/PC2005-cloud/dsh-pet)，素材禁止商用，角色形象採 CC BY-NC-SA 4.0；其他元件與資料的授權見 App 內附的 `THIRD-PARTY.md`。
