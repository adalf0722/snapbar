# Push 前桌面驗證與雲端打包驗證

日期：2026-11-03（Asia/Taipei）。本次調整 hooks、測試紀錄傳輸與 workflow，沒有修改產品程式碼。原本 AI 設定介面的工作由其他變更保留；實作過程該變更已另行提交。

## 完成項目

- `scripts/check.ps1` 成功：actionlint 1.7.12、20 項工具契約檢查、當時的 9 項 push 流程檢查、Release 建置及 460 項 Core 測試。
- 補入跨 checkout 的換行測試後，`scripts/test-push.ps1` 全部 10 項成功。使用隔離 checkout、本機 bare remote 與真實 Git pre-push hook；僅互動桌面測試程序使用明確的受控替身，不操作真實剪貼簿或視窗。
- 涵蓋 dirty／staged／untracked／assume-unchanged 拒絕、annotated tag、刪除與 notes-only 免測、測試失敗擋下 branch push、notes 上傳拒絕擋下 branch push、既有 note 保留、commit／tree 不符、缺少或重複套件、失敗 Preflight，以及缺少 notes 的雲端拒絕。
- 新的 CRLF clone 能匯入原本 LF checkout 所測試的同一 commit；匯入後修改來源，現有發布指紋檢查會拒絕。沒有取消發布來源一致性檢查。
- 已執行 hooks 安裝器，本 checkout 的 `core.hooksPath=.githooks`，pre-commit 與 pre-push 均啟用。
- 中文 Markdown 以嚴格 UTF-8 無 BOM 讀回，PowerShell parser 與差異空白檢查通過。

完整檢查建置報告：`artifacts/checks/build/20261103T133228894Z-5f34117782774325a7855f072e3573f3/report.json`。

## 尚未執行

本次沒有重新執行真實 Release 互動桌面套件；測試範圍沒有修改，下一次一般 branch／tag push 會在乾淨來源上完整執行。現有 SnapBar 不需要為了本次工具檢查而強制結束。

沒有將這份變更 commit／push 到 GitHub，也沒有建立 Release 或移除自架 runner 註冊。GitHub 實際 notes 傳輸、雲端打包及 Release 附件上傳需在這份變更推送後驗證；本機 bare remote 的成功不能當作 GitHub 已成功執行。
