# Python 自訂動作

## 已同意的範圍

使用者於 2026-11-13 同意最小 Python 支援：接上既有 Python 3 或虛擬環境，沿用現有文字輸入、結果輸出、逾時、取消及子程序清理。不內附 Python、不安裝套件，也不增加完整環境管理。

## 設計

- 在既有 enum 尾端加入 Python，保持 URL、PowerShell、JavaScript 的序列化值不變。匯入、匯出及設定保存沿用現有契約。
- 預設尋找 PATH 中的 python.exe；使用者可指定完整 .exe 路徑，包括虛擬環境。編輯器按語言保存尚未儲存的路徑，避免不同語言共用解譯器。
- 以 UTF-8 無 BOM 暫存 action.py；固定 bootstrap 等待 request-owned Job Object 就緒後才以 __main__ 執行。以 -X utf8、-u 及 PYTHONIOENCODING=utf-8 確保 Unicode 和及時輸出，關閉 stdout／stderr 的換行轉換以保留 CRLF。
- 選取文字只放入 POPCLIP_TEXT，不拼進指令。不傳入選取時給空字串。stdout、stderr、逾時、取消、輸出上限與程序樹清理共用現有實作。
- 編輯器加入 Python 範例、變數插入、執行環境說明；找不到環境和缺少模組提供雙語提示，詳細資訊保留原始 traceback。

## 驗證

Core 驗證 Python 匯出匯入、設定及搜尋。原生 runner 實際驗證 Unicode、無選取、明確路徑、虛擬環境、錯誤、逾時、取消、輸出上限及子程序清理。桌面測試驗證編輯器切換、範例預覽、保存與診斷。執行 repository checks 及相關桌面套件，記錄實際結果。
