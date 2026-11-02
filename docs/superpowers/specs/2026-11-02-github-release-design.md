# GitHub 自動發布設計

使用者已批准：發布版本後自動產生 Windows x64 免安裝 ZIP，保留現有 Release 桌面測試門檻。

## 流程

GitHub Release 的 `published` 事件觸發工作。自架 Windows x64 runner 使用 `snapbar-desktop` 標籤，在已登入、未鎖定的互動桌面執行日常檢查、Release 桌面測試及既有 `build.ps1 -Publish`。App 與 Probe 都使用 self-contained 發布，不改動選字或設定行為。

新增打包入口，驗證版本名稱與發布目錄的必要檔案，再產生 `SnapBar-v<版本>-win-x64.zip` 及 SHA-256 檔。ZIP 根目錄直接包含 App、README 與 Probe 子目錄。打包使用獨立 artifacts 目錄，驗證 ZIP 後才交付。

測試機僅有 repository 讀取權限。套件經 Actions artifact 傳至 GitHub 託管的 Windows runner，後者取得 contents write，使用 GitHub CLI 附加至觸發的 Release。不使用覆蓋附件選項；失敗時保留既有附件與原套件。所有工作保存檢查日誌。

## 選擇與限制

採用自架桌面 runner，以符合現有互動測試要求。僅使用 GitHub 託管 runner 不能取代這項驗證；加入安裝精靈另屬後續範圍。runner 不處理 pull request，只處理維護者發布的版本；每個測試桌面只啟動一個 runner。版本採 `v1.2.3` 或 `v1.2.3-rc.1` 格式。

發布 workflow 合併、runner 註冊並保持在線後，未來發布即可自動執行。本次不建立 Release、不推送未提交的既有 UI 變更、不代註冊需要帳戶驗證的 runner。

## 驗證

工具測試覆蓋不合法版本、缺件拒絕、ZIP 根目錄結構及 SHA-256 正確性。執行 repository check、workflow YAML 與 actionlint 檢查；在目前桌面可用時執行完整 Release 套件，再以實際 self-contained 套件驗證 ZIP。GitHub 端觸發與上傳需在 workflow 推送及 runner 配置後驗證，不以本機成功代替。
