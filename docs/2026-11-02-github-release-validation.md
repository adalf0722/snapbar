# GitHub 自動發布驗證

日期：2026-11-02（Asia/Taipei）。本次在現有 checkout 驗證，包含工作區原有、尚未提交的圖示變更；本次實作未修改 App／Probe 產品程式碼。

## 已完成

- `scripts/check.ps1` 成功：15 項工具檢查、420 項 Core 測試及 Release 建置。
- actionlint 1.7.12 驗證全部 workflow 成功；另檢查 published 事件、自架 runner 標籤、package 工作唯讀及 upload 工作 contents write 權限。
- Release 桌面套件成功：Lifecycle 28、Appearance 164、CompatibilitySlow 22、AiRuntime 35、AiPanel 130，共 379 項，另有 1 項桌面 preflight。
- `scripts/package-release.ps1 -Version v0.0.0-local` 成功：App／Probe self-contained 發布、10 項 Probe smoke、原始碼指紋及桌面證據核對、ZIP 結構與 SHA-256 產生。
- ZIP 完整解壓縮後，再以解壓縮的 Probe 執行 `test-probe.ps1`，10 項檢查成功；ZIP 與校驗碼相符。
- 修改的中文 Markdown 已以嚴格 UTF-8 重新讀取並確認沒有 BOM。

桌面成功報告：`artifacts/checks/desktop/20261102T124634200Z-1360c015cd9b4708864308b308063ae8/report.json`。

發布成功報告：`artifacts/checks/build/20261102T124801760Z-e31cb4a2e0c74f85a8482bb331351f20/report.json`。

驗證 ZIP：`artifacts/releases/f70e081b1bc94aa7aa20f07097385f36/SnapBar-v0.0.0-local-win-x64.zip`，143,028,511 bytes。這是本機驗證版本，未上傳 GitHub。

## 過程與限制

初次桌面測試被正在執行的 SnapBar 佔用快捷鍵擋下，之後透過介面正常結束。第一次完整執行在相容讀取期間偵測到剪貼簿變動；第二次在 AI 長 Markdown 複製檢查失敗。第三次完整執行全部成功，未改動測試、未略過任何套件，也未降低發布門檻；前次失敗日誌仍保留。剪貼簿相關測試有間歇性失敗，runner 必須使用專用且無人操作的桌面。

GitHub 端的事件觸發、artifact 傳輸與 Release 附件上傳尚未實跑；需先推送 workflow，並由 repository 管理者完成帶有 `snapbar-desktop` 標籤的 Windows runner 註冊。runner 必須在已登入且未鎖定的互動桌面執行。設定與失敗重跑方式見 [開發文件](development.md#github-自動發布)。
