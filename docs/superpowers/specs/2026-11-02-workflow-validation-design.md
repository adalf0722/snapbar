# Workflow 檢查與發布演練

使用者已批准優先處理 retro 的第 1、2 項。

## 自動檢查

將 actionlint 1.7.12 接入現有 `check.ps1`，沿用 pre-commit 與 CI 的入口。工具首次使用時下載至忽略的 `.tools/`，以固定 SHA-256 驗證；下載、啟動或 lint 失敗均停止檢查。停用非必要的 shellcheck／pyflakes 外掛，避免依賴各機器安裝狀態。

一般檢查驗證工作區全部 workflow。暫存檢查在 workflow 或 actionlint 設定有變更時，從 Git index 擷取全部 workflow 與設定至獨立快照，以快照內容驗證；未暫存的修正不能掩蓋錯誤。其他檔案提交不需下載或執行 actionlint。保留既有 UTF-8、語法、空白、工具測試與 Core 門檻。

## 發布演練

在同一個 release workflow 增加 `workflow_dispatch`，輸入僅作為驗證 ZIP 的版本標籤，預設 `v0.0.0-validation`。正式發布與演練共用 package 與跨 runner verify 工作：完整 Release 桌面測試、既有 gated publisher、ZIP、SHA-256、artifact 上傳／下載及校驗均相同。

Release 附件寫入獨立為最後工作，僅 `release` 事件可執行並取得 contents write。手動演練不建立 tag 或 Release，僅保留 Actions artifact 與摘要。兩條路徑都以觸發事件的 commit SHA checkout；人工演練不會自動允許日後不同來源略過桌面門檻。

## 驗證

工具回歸案例必須證明：不合法 GitHub context／YAML 會失敗；工作區修正不能掩蓋暫存錯誤；暫存 workflow 與設定正確時，工作區尚未暫存的錯誤不影響此次提交。執行完整 check。推送後實際手動演練，確認 package、verify 成功、upload 跳過、Release 與 tag 清單沒有新增；runner 離線或桌面不可用時明確回報，不略過測試。
