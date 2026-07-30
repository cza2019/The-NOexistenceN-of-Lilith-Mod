# 《不存在的莉莉絲》Mod

[简体中文](README.md) · [繁體中文](README.zh-TW.md) · [English](README.en.md) · [日本語](README.ja.md)

`The NOexistenceN of Lilith Mod` 為 Steam 桌寵遊戲《不存在的莉莉絲》加入生成式 AI 對話、玩家長期記憶、原作劇情檢索、本機狀態感知與外部音樂同步。一般玩家只需設定自己的模型服務，或只使用音樂同步功能。

> 目前版本：0.3.0 · Windows 10/11 x64

## 主要功能

- 莉莉絲固定延續《不／存在的你，和我》結局 1（Happy Ending）之後的設定。
- 單一安裝包支援簡體中文、繁體中文、English、日本語；預設跟隨遊戲，也可在 Mod 設定中覆蓋。
- 支援 OpenAI 相容介面與 DeepSeek、Kimi、Qwen、GLM、火山方舟等預設。
- 本機保存對話與玩家長期記憶；四語共用同一份玩家原文，不會自動翻譯或建立重複檔案。
- 只在相關提問中附加本機時間與白名單音樂軟體的媒體狀態。
- 可使用模型內建搜尋、Brave Search 或 SearXNG；搜尋失敗不影響一般對話。
- Unity 遊戲內聊天視窗、頭頂回覆、對話歷史、角色氣泡入口與外部音樂動作同步。

## 安裝

1. 從 GitHub Releases 下載 `The-NOexistenceN-of-Lilith-Mod-v0.3.0-win-x64.zip`。
2. 完全結束《不存在的莉莉絲》。
3. 將 ZIP 內所有內容解壓到遊戲根目錄，讓 `winhttp.dll` 與 `Lilith.exe` 位於同一層。
4. 從 Steam 啟動遊戲；首次載入 BepInEx 可能較慢。
5. 右鍵莉莉絲或系統匣開啟「Mod 設定」，選擇語言並填入自己的模型 API。

安裝包已包含 BepInEx 6 IL2CPP、Unity 基礎類庫、Node.js 24 LTS 與自包含媒體橋接；玩家不必另外安裝。詳細步驟見 [安裝、升級與解除安裝](docs/installation.zh-TW.md)。

## 隱私與授權

- API 金鑰只儲存在 `%APPDATA%\LilithAI\config.json`。
- 對話及玩家啟用的臨時狀態會傳送給玩家自行設定的模型或搜尋服務商。
- 不讀取瀏覽器影片、剪貼簿、活動視窗、檔案、電池或網路狀態。
- 程式碼使用 [MIT License](LICENSE)；角色素材與第三方元件另見 [素材聲明](ASSET-NOTICE.md) 與 [第三方元件聲明](THIRD-PARTY-NOTICES.md)。
- 本專案不包含遊戲本體，使用者必須透過 Steam 合法安裝《不存在的莉莉絲》。
