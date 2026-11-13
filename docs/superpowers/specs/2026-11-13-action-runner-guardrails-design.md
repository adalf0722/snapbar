# 自訂動作測試門檻與失敗診斷

使用者同意優先處理 retro 的前兩項：關鍵執行測試接入發布必跑，以及改善失敗訊息。

## 驗證入口

- Release 和 All 納入 ActionRunner，強制使用 `--require-runtimes`；單獨 ActionRunner 預設可略過缺少的 Node／Python，也可傳入 `-RequireActionRuntimes` 開啟嚴格模式。
- `check.ps1` 完成建置後執行不依賴前景視窗的 ActionRunner；CI 與 release workflow 明確安裝 Python 3.13、Node 24 並開啟嚴格模式。本機日常檢查預設保留可選環境。
- 原始報告統計 SKIP 訊息數；嚴格 ActionRunner 的證據必須明示 runtimesRequired=true、skipped=0 並有成功測試。發布報告及 Git note 匯入皆核對相同條件，拒絕舊報告或可選模式證據。

## 測試斷言

測試專用 helper 顯示狀態、退出碼、stdout／stderr 長度；文字差異顯示 UTF-16 第一個差異索引、預期與實際字元。CR、LF、TAB 與 EOF 用可讀標記，其餘用 Unicode 編碼，避免整段輸出傾倒。只供可丟棄 fixture 測試使用，不加入產品日誌。

## 驗證計畫

以工具契約測試拒絕缺少 ActionRunner、可選模式、帶略過數及缺少新欄位的報告與 receipt；測試 SKIP 統計。執行真實嚴格 runner，另以空 PATH 確認可選模式略過及嚴格模式失敗。以故意製造 CRCRLF、EOF 與非零退出結果驗證斷言訊息。完成 repository checks 與嚴格 ActionRunner 桌面入口測試，不發布套件。
