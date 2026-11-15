# 藍色大肥魚 · 1.1.3-tw.1（台灣繁體中文版）

這是以原作者 Bai-Cute 的藍色大肥魚 1.1.3（含官方 macOS 版）為基礎、整合 acer1204 台灣版功能的**非官方**台灣繁體中文版，也是本版第一次以自己的名稱與更新來源發布。本版不是由原作者或 acer1204 維護，使用上的問題請回報到 [Methrew/big-blue-fish-desktop-pet](https://github.com/Methrew/big-blue-fish-desktop-pet/issues)。

## 下載

| 版本 | macOS 14 以上 · Apple Silicon |
| --- | --- |
| 標準版 | `BigBlueFish-TW-v1.1.3-tw.1-macOS-arm64.dmg` |
| 無MCP版 | `BigBlueFish-TW-v1.1.3-tw.1-Safe-macOS-arm64.dmg` |

每個 DMG 都附有 `.sha256` 檢查碼檔。打開 DMG 後把 App 拖到「應用程式」檔案夾；App 只有臨時簽章，第一次打開時請到「系統設定 › 隱私權與安全性」按「強制打開」。安裝、權限與解除安裝的說明見本版儲存庫的 `docs/MACOS.md`（[專案頁面](https://github.com/Methrew/big-blue-fish-desktop-pet)）。

本版不提供 Windows 與 Linux 的安裝程式或套件。

## 和原版 1.1.3 相比

- **台灣繁體中文**：設定、選單、提示視窗、模型安裝與更新視窗、Agent 技能與人設、文件都改成台灣繁體中文與台灣用語。氣泡文字顯示前會用 OpenCC 1.4.2 的字典轉成台灣繁體，氣泡字型改用 jf open 粉圓 2.1，介面用蘋方-繁。macOS 的 OCR 先以繁體中文辨識，英文行另外重新辨識。Agent 用繁體寫動作名稱也認得。
- **臺灣鄉鎮天氣**：國家清單和原版相同（包括中國清單裡的臺灣省），另外多一個國家「臺灣 Taiwan」（22 個縣市、368 個鄉鎮市區），預設地點是臺北市中正區。填入中央氣象署授權碼時使用氣象署的鄉鎮天氣預報，授權碼存在 macOS 鑰匙圈；沒有授權碼時改用 Open-Meteo 估計。地名以繁體字顯示。
- **自己的身分**：App 名稱「藍色大肥魚」、Bundle ID `io.github.methrew.bigbluefish.tw`、資料夾 `~/Library/Application Support/bigbluefish-tw`，檢查更新只看本版的發布頁面。第一次啟動時會從原版的資料夾唯讀匯入一次設定、模型選擇和 Agent 資料（模型用 APFS 複本）。可以和原版同時安裝，但同一時間只會執行一隻魚。
- **雲端 AI 服務（本版新增）**：「模型來源」可以改選 Claude（Anthropic）、ChatGPT（OpenAI）或 Gemini（Google），用自己的 API Key 讓魚魚說話，不必安裝本機模型。API Key 存在 macOS 鑰匙圈，MCP 與 Agent 讀不到；可以限制每天最多送出幾次請求（預設 200 次），費用由服務依用量向你的帳戶收取。
- **從台灣版移植到 Mac**：遠端 llama.cpp 模型（API Key 存在鑰匙圈）、魚魚自己的主動提醒（久坐、用餐、深夜、低電量、下雨）、`get_context` 與 `user_returned`、`find_on_screen` 與 `walk_to`、Hermes 對話橋接（標準版 App 內附）、外部 MCP 介面，以及輸入框附件（以 ACP 的 `resource_link` 交給 Agent）。
- **Mac 版的改進**：App 只內含實際播放的相容動畫，從約 1.0 GiB 縮小到約 0.2 GiB；修正讀不到真正閒置時間、導致自動說話與 Agent 日常關心不會觸發的問題；桌寵跟著切換桌面空間、不搶走鍵盤焦點；點選與拖曳的動作開頭預先解碼；第一次偵測 GPU 時能用 Metal；從 Finder 開啟時也找得到 npm 或 Homebrew 安裝的 Agent CLI。
- 支援的 21 款 CLI Harness（包括 Claude Code）和原版相同。

## 已知限制

- Windows 與 Linux 的原始碼已經合併並確認可以編譯，但沒有實際執行或測試。
- App 只有臨時簽章，沒有經過 Apple 公證。每次更新後，macOS 可能要重新允許「螢幕與系統錄音」「輔助使用」「自動化」；更新後會不會再詢問「區域網路」權限，還沒有驗證過。
- 更新不會在 App 裡自動完成：發現新版時請到發布頁面下載 DMG，自己取代舊版。
- macOS 版播放 640×360、每秒 24 格的相容動畫，不是原作者提升到 1280×720、每秒 60 格的版本。
- 地名只換字形、不換譯名，所以仍會看到「意大利」「新西蘭」這類原版資料的譯名。106 個動作名稱是動畫檔名，維持簡體字。
- 科技新聞的來源仍是 IT之家，由模型用台灣繁體中文轉述。
- 遠端模型功能是為 llama.cpp 的 llama-server 設計的，其他 OpenAI 相容服務可能不接受請求；自簽憑證的 https 伺服器連不上。
- macOS 的輸入框沒有聊天記錄（Windows 版才有）。選單列圖示的選單字型依系統語言決定。
- OCR 的英文改善只在 macOS 27 上實測，最低支援的 macOS 14 還沒有測試。
- 原作者的功能規格（`docs/AGENT_FUNCTIONAL_SPEC.md`、`docs/specs/`）維持簡體中文原文，描述的是原版的行為。

## 致謝

- 原作者 **Bai-Cute**：[Bai-Cute/big-blue-fish-desktop-pet](https://github.com/Bai-Cute/big-blue-fish-desktop-pet)，以及在原版 1.1.3 貢獻 macOS 移植版的 **YamaFuso**。
- 台灣版 **acer1204**：[acer1204/big-blue-fish-desktop-pet 的 taiwan-edition 分支](https://github.com/acer1204/big-blue-fish-desktop-pet/tree/taiwan-edition)。
- 台灣 macOS 版整合：**Methrew**。
- 角色動畫取自 [PC2005-cloud/dsh-pet](https://github.com/PC2005-cloud/dsh-pet)，素材禁止商用，角色形象採 CC BY-NC-SA 4.0；其他元件與資料的授權見 App 內附的 `THIRD-PARTY.md`。
