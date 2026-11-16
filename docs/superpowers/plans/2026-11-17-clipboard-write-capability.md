# 相容寫入實作計畫

1. 在 Core 契約及政策加入單次候選寫入 token、原生 caret 資格與身分比較，保留普通 clipboard 唯讀。
2. Windows 讀取系統及 MSAA caret metadata；Probe 在 capture 建立資格，validate 只確認既有資格且拒絕來源漂移。
3. App 將 Probe 證據傳入快照，讓貼上／剪下／AI 與自訂替換共用 revalidation。更新雙語狀態區分可編輯相容來源與普通相容讀取。
4. 執行 Core、原生、相容及競態測試；新增有意義的寫入與來源失效驗證。以既有可還原草稿回測，保留失敗證據。
5. 更新驗證紀錄與雙語更新說明，完成檢查，啟動開發版。

五步已完成；測試、實體驗收與限制見 [驗證紀錄](../../2026-11-17-clipboard-write-validation.md)。
