# Everything 搜尋 action 設計

## 目的與使用方式

新增內建「Everything 搜尋」action。使用者選取文字後執行 action，SnapBar 以該文字查詢本機 Everything，並在結果視窗列出檔案與資料夾。新 action 預設放在更多動作，既有工具列配置和使用者偏好保持不變，可透過現有設定移動。

## 查詢與結果

透過 Everything 的本機 IPC 介面查詢正在執行的 Everything，沿用 Everything 搜尋語法。查詢在背景執行並設定逾時與結果數量上限，避免大量結果或無回應造成介面卡住；視窗說明結果可能遭截斷。使用者可開啟結果、開啟所在資料夾，或在 Everything 中以相同文字查看完整結果。空結果、Everything 未執行、無法找到程式與查詢失敗均顯示易懂的狀態；可用時提供啟動 Everything 的方式。

## 結構與安全

Core 負責 action 註冊及適用性；Windows 層封裝 Everything IPC、查詢逾時和程式啟動，App 層負責結果視窗及操作。沿用現有選取驗證、密碼欄位拒絕與過期結果取消機制，不記錄選取文字或搜尋結果。對結果路徑使用 Windows Shell 開啟，不將文字組成 shell 指令。支援既有介面語言及無障礙標籤。

## 驗證

以自動測試覆蓋 action 設定相容性、結果解析、空結果、逾時與失敗處理；在 Windows 桌面驗證正常搜尋、開啟結果及 Everything 未執行的情境。新增繁體中文 release note，說明 Everything 的執行前提與使用方式。
