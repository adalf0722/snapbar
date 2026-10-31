# 開發檢查與發佈流程

使用者已同意 retrospective 的高／中優先項目；本輪不改 commit-message-helper。

CI 與本機 check 使用同一入口，執行 UTF-8 without BOM、PowerShell 語法、diff whitespace、工具自身測試、Release 建置與 Core runner。本機 pre-commit 只檢查 index 的編碼、語法與 whitespace；不把未暫存的內容當成提交內容。啟用 hook 時保留既有 hooks 設定，存在衝突就停止。

桌面入口列出具名 suite、先檢查互動桌面及快捷鍵，串行執行，保存每次的獨立 UTF-8 日誌及 JSON 報告。Release profile 包含 UIA lifecycle、appearance 與包含 35 秒案例的 compatibility suite。一般 compatibility suite 不等待 35 秒，時間邊界以可控制的 TimeProvider 在 Core 驗證。fixture 與 Probe 取自剛建置的輸出，不依賴 artifacts/p0。

發佈在任何建置／覆寫前檢查輸出目錄的 App／Probe 程序，保留使用者程式；並要求必要桌面 suites 全部通過，報告的 source fingerprint 必須吻合目前 src、tests、scripts、build 設定及 workflow。先發佈至新的 staging 目錄，通過 smoke 後再次檢查程序與 fingerprint，再交換正式目錄；失敗回復原目錄。所有搬移目標先驗證位於本 repo 的 artifacts 內。舊套件留在 backup，不遞迴刪除。

AGENTS 僅保留入口、分層導覽與當前選取／剪貼簿政策，詳細命令與 review 的跨模組生命週期檢查放 development 文件。實測失敗路徑包括程序占用、過期／缺 suite 的報告、非法編碼／腳本、錯誤測試旗標，以及 staging 失敗不破壞舊套件。
