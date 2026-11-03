# Push 前桌面驗證與雲端打包

使用者已同意將互動桌面測試提前到 push 前，GitHub 負責建置與 ZIP 發布，並保留相符測試證據的要求。

## 選擇與流程

採用本機 pre-push + 綁定 commit 的 Git notes。相較於只安裝 hook，雲端可拒絕沒有紀錄的來源；相較於繼續使用自架 runner，不需要本機等待 GitHub 工作。避免將測試結果提交到同一個 commit 造成循環依賴。

1. pre-push 解析 Git stdin 中的 branch/tag 更新，支援 annotated tag；刪除及證據 ref 自身的傳送不執行桌面測試。
2. 所有非刪除來源必須指向目前 HEAD，工作區與 index 必須乾淨，不接受 assume-unchanged 或 skip-worktree。不同分支應在各自 checkout 測試及推送。
3. 每次實際 push 均跑完整 Release 桌面套件，不用快取替代。失敗不送出 branch/tag。
4. 重新核對乾淨來源與原始碼指紋，建立含 commit、tree、時間、Build／Preflight／五個 Release 套件結果的精簡 JSON。排除本機路徑、詳細日誌及使用者內容。
5. 先取得遠端 `refs/notes/snapbar-desktop`，在獨立暫存 ref 加入測試紀錄，再以普通非強制 push 上傳。上傳失敗或並行更新即阻擋原始 push；不覆蓋別人的紀錄。
6. GitHub 的 push、手動演練及 release published 工作皆使用 windows-latest，先 fetch notes，核對確切 commit／tree、Build、Preflight 與所有 Release 結果，再進行既有檢查、self-contained 發布、Probe／Startup smoke、ZIP 結構與 SHA-256 核對。
7. 用 commit／tree 識別相同 Git 來源，匯入報告時記錄該雲端 checkout 的實際檔案指紋，避免 LF／CRLF 差異造成誤判。原始紀錄另存供審查，既有本機發布門檻保持有效。
8. push 產物提供 Actions artifact；只有正式 Release 事件能附加公開下載檔。版本使用可驗證的 CI 版本字串或指定 Release tag。

## 失敗與限制

沒有安裝 hook、使用 --no-verify 或 GitHub 網頁建立的新 commit，會因缺少該 commit 的桌面紀錄而停止打包。新 clone 需自行執行 hooks 安裝器。Git notes 是維護者提供的回歸紀錄，與現有 JSON 報告一樣不是簽章或防竄改證明；不宣稱可抵禦有寫入權限的惡意維護者。

紀錄先於 branch 推送，因此原始 push 最後失敗或 dry-run 時，遠端可能已有不被 branch 引用的測試紀錄；這不會自行產生套件。遠端必須允許 notes ref 寫入。遠端 notes 並行更新失敗時重新執行 push，完整重測。

未調整產品程式碼、選取安全規則或桌面測試範圍。不建立 Release、不自動提交其他工作、不移除已註冊的自架 runner。

## 驗證

以隔離 checkout 與本機 bare remote 執行真實 Git pre-push hook，使用明確的桌面測試替身驗證成功、失敗、紀錄上傳拒絕、branch/tag、舊紀錄保留、dirty gate 及雲端匯入。搭配完整 check.ps1 的 UTF-8、PowerShell parser、actionlint、工具契約與 Core 建置測試。實際桌面 Release 測試另行執行並記錄，GitHub 真實端的傳輸及雲端打包需推送這份變更後驗證。
