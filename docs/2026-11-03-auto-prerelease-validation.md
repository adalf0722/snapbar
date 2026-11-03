# Main push 自動預覽版發布驗證

日期：2026-11-03（Asia/Taipei）。使用者選擇 push main 後自動接續 rc 編號發布預覽版。

## 本機完成

- `scripts/check.ps1` 全部成功：actionlint 1.7.12、20 項工具契約、10 項 push 流程、10 項自動發布測試、Release 建置與 460 項 Core 測試。
- 自動發布測試使用受控 gh 替身，沒有建立任何真實 Release。涵蓋整數版本遞增、草稿／tag 占號、同 run 版本恢復、同附件略過、衝突拒絕、先草稿再上傳後公開、失敗重跑補傳、其他來源／run 拒絕、GitHub 查詢失敗、tag 被移動及損壞 ZIP 拒絕。
- 使用現有 GitHub CLI 對 repository 做唯讀版本解析，確認目前將從 `v0.1.0-rc.4` 接續為 `v0.1.0-rc.5`，更新說明基準為 rc.4。沒有預先保留版本或建立 tag。
- 中文 Markdown 使用嚴格 UTF-8 無 BOM 驗證，PowerShell parser、差異空白與更新後的 workflow actionlint 均成功。

建置報告：`artifacts/checks/build/20261103T140857630Z-7388fed288704edd8521bb4c10cbcbef/report.json`。

## 尚待真實推送

本次沒有修改產品程式碼，也沒有重跑互動桌面測試；下一次 main push 仍須經過完整 pre-push Release 桌面測試。既有 rc.4 的成功發布證明先前流程，不能當作本次新增的自動版本／草稿發布步驟已在 GitHub 實跑。

這份變更尚未 commit／push。首次 main push 後需確認 auto-release 工作成功、產生下一個 rc 預覽版、ZIP 與 SHA-256 完整附上，且更新說明來源為該次 SHA。密集 main push 受 GitHub concurrency 預設僅保留一個等待工作的行為影響，未開始的舊等待工作可能被取代；正在執行的發布不取消。
