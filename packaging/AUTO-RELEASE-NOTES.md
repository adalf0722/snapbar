<!-- snapbar-run: {run} -->
<!-- snapbar-source: {commit} -->

# SnapBar Windows 預覽版 {version}

由 main 推送自動發布，來源 commit：`{commit}`。

## 下載與執行

下載附件 `SnapBar-{version}-win-x64.zip`，完整解壓縮，再執行 `SnapBar.App.exe`。更新前先從系統匣正常結束舊版。套件包含 .NET 執行環境，不需另裝 SDK 或 Runtime。預設選字快捷鍵為 Ctrl+Alt+V，已有個人設定時沿用設定。

請使用上述執行套件，GitHub 的 Source code ZIP 僅含原始碼。同名 `.zip.sha256` 提供 SHA-256 校驗碼。

## 本次更新

{changes}

## 驗證與限制

本版已核對與來源 commit 相符的本機 Release 桌面測試紀錄，並通過雲端專案檢查、自含式建置、Probe／啟動 smoke、ZIP 結構與 SHA-256 核對。

套件尚未簽章，目前沒有自動更新。AI 服務需自行設定；JavaScript 自訂動作另需 Node.js。混合 DPI 與部分跨 App 情境仍待人工驗證。詳細說明及限制見套件內 README.md 與 VALIDATION.md。
