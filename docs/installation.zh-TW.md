# 安裝、升級與解除安裝

## 安裝

1. 從 Releases 下載 `The-NOexistenceN-of-Lilith-Mod-v0.3.0-win-x64.zip`，並核對同頁 SHA-256。
2. 從系統匣結束《不存在的莉莉絲》，確認 Steam 不再顯示「停止」。
3. 將 ZIP 內所有內容解壓到遊戲根目錄。正確結構中，`winhttp.dll`、`Lilith.exe`、`BepInEx` 與 `dotnet` 位於遊戲目錄。
4. 從 Steam 啟動。首次建立 IL2CPP 快取可能較慢，請勿在載入時強制關閉。
5. 右鍵莉莉絲或系統匣開啟「Mod 設定」。在「其他」頁選擇跟隨遊戲或指定語言；AI 對話使用玩家自行提供的 API。

安裝包已包含 BepInEx、Node.js 與媒體橋接；不需要全域安裝或固定 Node 版本。Mod 設定使用 Windows 10/11 內建的 Windows PowerShell 5.1。

## 升級

完全結束遊戲後，用新 ZIP 覆蓋相同目錄。`%APPDATA%\LilithAI` 中的 API 設定、會話與玩家記憶不會被安裝包覆蓋。切換 Mod 語言不會翻譯歷史或長期記憶。

## 解除安裝

遊戲結束後，刪除 Mod 隨包附帶的 BepInEx／Doorstop 檔案，或用 Steam 驗證遊戲檔案。若也要刪除 API 設定、會話與玩家記憶，再手動刪除 `%APPDATA%\LilithAI`。

## 故障排除

- Mod 未載入：確認 `winhttp.dll` 與 `Lilith.exe` 同層，並查看 `BepInEx\LogOutput.log`。
- 設定無法開啟：確認 `BepInEx\plugins\companion\configure.ps1` 與 `BepInEx\plugins\locales` 完整存在。
- 字型缺字：先確認遊戲本體已切換到對應語言；Mod 會沿用遊戲 TMP 字型與其 fallback。
- 回報日誌前請移除私人對話、玩家稱呼與本機路徑。
