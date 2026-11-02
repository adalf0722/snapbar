# Workflow 檢查與發布演練驗證

日期：2026-11-02（Asia/Taipei）。本次處理 retro 第 1、2 項，未修改 App／Probe 產品行為。

## actionlint 門檻

`scripts/check.ps1` 已整合固定版本 actionlint 1.7.12，首次下載核對 SHA-256。日常檢查、pre-commit 與 GitHub CI 使用同一入口；lint 非零退出碼會使檢查失敗。暫存檢查以 index 的 workflow 與設定建立快照，不受未暫存修正影響，刪除設定檔也會重新檢查剩餘 workflow。

本機完整 check 通過 20 項工具檢查、420 項 Core 測試與 Release 建置。[遠端 CI 41459418708](https://github.com/adalf0722/snapbar/actions/runs/41459418708) 亦通過，來源為 `a1c9e3c`。回歸案例涵蓋不合法 runner context、錯誤 YAML、有效暫存版本搭配無效工作檔、自訂 runner 標籤只採用 index 設定，以及刪除 index 設定仍有未暫存設定檔時的拒絕行為。

## 手動演練

[Actions 工作 41458944720](https://github.com/adalf0722/snapbar/actions/runs/41458944720) 以 `workflow_dispatch` 及 `version=v0.0.0-validation` 觸發，固定來源為 `db7b454c1ccddfa21d64cd63b8bc168a9b889821`。此執行發生在新增「刪除設定」回歸案例之前，該案例由上述後續本機與 CI 檢查另行驗證；演練期間 main 更新未改變此次 checkout 的來源。

- package 成功：actionlint、當時的 19 項工具檢查、420 項 Core、Release 桌面套件、自包含發布與 Probe smoke。
- 桌面套件：Lifecycle 28、Appearance 164、CompatibilitySlow 22、AiRuntime 35、AiPanel 130，共 379 項；另有成功的桌面 preflight。
- verify 成功：另一個 GitHub 託管 runner 下載 artifact，核對 ZIP SHA-256，產生含來源 commit 與版本的工作摘要。
- upload 工作為 skipped；演練前後核對 Release、既有附件的名稱／大小／digest，以及 tag ref／SHA，均未改變。
- `windows-release-package` artifact 為 142,585,535 bytes，另保存 `release-check-logs`。artifact 保留期限沿用 workflow 設定。

這次沒有建立新的 tag 或 Release。使用方式見 [完整演練說明](development.md#先做完整演練)。未來正式 Release 仍需自行跑完整測試，不能以這份演練報告略過不同來源的驗證。
