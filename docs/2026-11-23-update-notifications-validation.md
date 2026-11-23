# 更新通知驗證紀錄

日期：2026-11-23

## 驗證範圍

公開版本的背景更新檢查、正式／預覽通道、獨立下載與通知目標、忙碌延後、單版去重、手動檢查、取消、雙語介面與發布摘要契約。此功能不下載或安裝套件，不改變選取、剪貼簿或動作權限。

## 已完成的驗證

- Core runner：732 項通過、0 失敗。包含語意版本比較、通知版本下限、64 筆歷史截斷、契約驗證、UTF-8 與原子保存、損壞／唯讀／較新設定檔保護。
- Updates runner：36 項通過、0 失敗。包含超過 100 筆分頁、不可信分頁拒絕、頁數及回應大小上限、來源識別矛盾／重複、限流、取消、30 秒啟動等待、24 小時節流、手動冷卻與已看過處理、忙碌延後、跨重啟去重、晚到結果、保存失敗及撤回版本。
- 系統匣以實際 TrayIcon 搭配模擬通知呈現驗證路由：縮到系統匣提示返回一般設定；更新提示導向明確版本；重疊的隱藏提示不能覆蓋更新目標。測試不發出真正 Windows 通知。
- WPF 更新面板已渲染並目視檢查繁中／英文、760／460 像素視窗，文字及按鈕完整可見。圖片位於 `artifacts/checks/updates-preview/zh-Hant-760.png`、`zh-Hant-460.png`、`en-760.png`、`en-460.png`。
- 模擬來源測試不連線 GitHub，不送出選取文字或剪貼簿內容，不修改使用者一般設定。
- `scripts/check.ps1` 最終執行通過：建置與 Core 732 項、ActionRunner 38 項，另含版本、發布、工作流程、網站及工具檢查。發布摘要 13 組與自動發布 23 組測試通過。
- 桌面個別套件通過：Lifecycle 173、Appearance 266、ClipboardRaces 17、SupportRecovery 14、AiRuntime 117、AiPanel 272、Lookup 20、Language 69、AutoCopy 52、Updates 36，均無失敗或略過。

## 完整 Release 未通過

完整 Release 連續執行仍未取得成功報告。兩次在 CompatibilitySlow 的「Copy revalidates generic selection and copies exact text」遇到剪貼簿序號變動並安全拒絕操作；最後一次停在「fixture must be foreground」。未略過安全檢查，也未把失敗當成成功證據。

將變更前的 `18f1c09` 匯出到隔離目錄，完整建置並設定相同本機 .NET runtime 後，原始版單獨執行 CompatibilitySlow 為 43 通過、0 失敗；改動版單獨執行亦為 43 通過、0 失敗。這證明單獨案例可在兩版本通過，但尚不足以斷言連續執行問題與改動無關。剪貼簿相關實作與測試未為本功能更改。

目前不得以本輪證據發布套件，仍需在穩定且可保持 fixture 前景的桌面重跑完整 Release。主要證據：

- `artifacts/checks/updates-check-final.log`：repository checks 成功。
- `artifacts/checks/desktop/20261123T134055003Z-552414a69fd84ce1b2243a1dc876091f/report.json`：其餘八項必要套件成功。
- `artifacts/checks/desktop/20261123T134546604Z-5e8cd1d7d7e440d2bf78c1d5712f9470/report.json`：最後一次完整 Release 失敗，前景 fixture 未就緒。
- `artifacts/checks/updates-baseline/compatibility.log` 與 `artifacts/checks/updates-compatibility-isolated.log`：原始版／改動版單獨相容性對照均通過。

## 驗證邊界

Windows 通知是否顯示仍由系統通知偏好決定，持續更新入口不依賴通知顯示。尚未以真實公開的新套件驗證 Windows 通知顯示、滑鼠點擊及瀏覽器下載到安裝的完整流程；本次未建立 GitHub Release，也未發布套件。原有跨 App／混合 DPI 的選取限制仍以既有驗證紀錄為準。

發布說明及正式版私有草稿工具使用隔離 Git／GitHub 模擬驗證，不對遠端帳號寫入。完整 repository checks 與桌面 Release 報告保存在忽略的 `artifacts/checks/`。
