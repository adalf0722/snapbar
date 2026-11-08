<!-- snapbar-run: {run} -->
<!-- snapbar-source: {commit} -->

## 本次更新

{changes}

## 下載與更新

[下載 Windows x64 版](https://github.com/{repository}/releases/download/{version}/SnapBar-{version}-win-x64.zip)

1. 更新前，先從系統匣正常結束舊版 SnapBar。
2. 將 ZIP 完整解壓縮到新的資料夾，再執行 `SnapBar.App.exe`。已有個人設定會沿用。
3. 選取文字後按 `Ctrl+Alt+V`，或使用自己設定的快捷鍵。

不需另裝 .NET。請下載上方執行套件；GitHub 的 Source code ZIP 是原始碼。

## 使用前請留意

這是 Windows 預覽版，套件尚未簽章，目前沒有自動更新。AI 服務需自行設定；JavaScript 自訂動作需另裝 Node.js。混合 DPI 與部分跨 App 情境仍待人工驗證。

<details>
<summary>技術細節與完整變更</summary>

完整變更：{comparison}

來源 commit：`{commit}`。

本版已核對與來源 commit 相符的本機 Release 桌面測試紀錄，並通過雲端專案檢查、自含式建置、Probe／啟動 smoke、ZIP 結構與 SHA-256 核對。同名 `.zip.sha256` 附件提供校驗碼。

詳細說明及限制見套件內 README.md 與 VALIDATION.md。

</details>
