# 安裝、升級與解除安裝

## 安裝

1. 從 Releases 下載 `The-NOexistenceN-of-Lilith-Mod-v0.3.4-win-x64.zip`，並核對同頁 SHA-256。
2. 從系統匣結束《不存在的莉莉絲》，確認 Steam 不再顯示「停止」。
3. 將 ZIP 內所有內容解壓到遊戲根目錄。正確結構中，`winhttp.dll`、`Lilith.exe`、`BepInEx` 與 `dotnet` 位於遊戲目錄。
4. 從 Steam 啟動。首次建立 IL2CPP 快取可能較慢，請勿在載入時強制關閉。
5. 右鍵莉莉絲或系統匣開啟「Mod 設定」。在「其他」頁選擇跟隨遊戲或指定語言；AI 對話使用玩家自行提供的 API。

安裝包已包含 BepInEx、Node.js 與媒體橋接；不需要全域安裝或固定 Node 版本。Mod 設定使用 Windows 10/11 內建的 Windows PowerShell 5.1。

遊戲啟動時會在背景檢查一次 GitHub 最新正式版。已是最新版或 GitHub 無法連線時不會顯示對話框；發現新版時只詢問是否開啟 Release，不會自動下載或取代檔案。

## 設定 Gemini

1. 在 Google AI Studio 建立自己的 Gemini API Key，並確認所在地區可使用 Gemini API 與 Google Search grounding。
2. 在「Mod 設定 → AI API」選擇「Google Gemini 官方」；官方 Base URL、`gemini-3.6-flash`、Interactions API 與 `low` 思考強度會自動填入。
3. 需要即時資料時選擇「模型內建搜尋」，或改用關閉、Brave Search、SearXNG。只有 Gemini 實際搜尋時才會開啟獨立視窗並產生相應費用。
4. 無法直接連線 Google 時可填入不含驗證資訊的 HTTP/HTTPS 代理；設定測試與遊戲內請求共用相同代理。

結果視窗使用 Windows 10/11 通常已有的 WebView2 Evergreen Runtime；缺少時會以預設瀏覽器開啟相同的一次性頁面。`store=false` 只停用代管會話鏈，免費配額資料用途與 Search grounding 相關請求保留 30 天仍依 Google 條款。

## 升級

完全結束遊戲後，用新 ZIP 覆蓋相同目錄。`%APPDATA%\LilithAI` 中的 API 設定、會話與玩家記憶不會被安裝包覆蓋。切換 Mod 語言不會翻譯歷史或長期記憶。

## 解除安裝

遊戲結束後，刪除 Mod 隨包附帶的 BepInEx／Doorstop 檔案，或用 Steam 驗證遊戲檔案。若也要刪除 API 設定、會話與玩家記憶，再手動刪除 `%APPDATA%\LilithAI`。

## 故障排除

- Mod 未載入：確認 `winhttp.dll` 與 `Lilith.exe` 同層，並查看 `BepInEx\LogOutput.log`。
- 設定無法開啟：確認 `BepInEx\plugins\companion\configure.ps1` 與 `BepInEx\plugins\locales` 完整存在。
- 字型缺字：先確認遊戲本體已切換到對應語言；Mod 會沿用遊戲 TMP 字型與其 fallback。
- 回報日誌前請移除私人對話、玩家稱呼與本機路徑。
- Gemini 無法連線時，請檢查地區、Key、Base URL 與代理；不支援含帳密或 SOCKS 的代理。
- 獨立搜尋視窗只在模型實際呼叫 Google Search 後開啟；請檢查 WebView2、預設瀏覽器與安全軟體。

更新只替換遊戲目錄中的程式檔案。API 金鑰與設定、聊天記錄及長期記憶保存在 `%APPDATA%\LilithAI`，原生遊戲存檔不會被套件覆蓋。請勿刪除此使用者資料目錄；需要備份時可複製整個目錄。自動備用 DNS 預設開啟，已儲存的模型名稱與手動輸入型號保持原樣。
