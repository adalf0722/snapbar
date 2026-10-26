# 在檔案總管中顯示路徑

## 範圍與互動

接續使用者同意的 P1 路徑工作包，依 2026-10-26 使用者對 `D:\codex` 的回饋調整資料夾行為。選取整段 Windows 絕對路徑時，動作列提供「檔案位置」；資料夾與根目錄直接開啟本身，檔案開啟父資料夾並選取，不執行檔案。唯讀內容可使用；不新增動作管理、改鍵或自動複製。

接受磁碟機絕對路徑、UNC、開頭的 `%USERPROFILE%`／`~` 使用者目錄縮寫，移除前後空白及一組成對雙引號，正規化 slash 和 dot segments。拒絕相對路徑、磁碟機相對路徑、裝置路徑、其他環境變數、URI、控制字元、萬用字元、ADS、保留裝置名稱及過長內容；不做 shell 展開。工具列只做純字串判斷，不因選字而存取本機／網路檔案。不存在或無權限的路徑於點擊後提示失敗。

## 方案取捨

- 採用原生 PIDL／Shell API，不拼接 explorer.exe 命令列；中文、空白與逗號作為路徑資料處理。
- 不在 UI 執行緒檢查存在性或解析網路路徑，亦不建立可能長期阻塞的背景工作佇列。
- 沿用現有可終止 probe 程序的通訊模式，但路徑使用獨立的 worker，避免破壞 UIA 選取工作階段。

## 元件與資料流

Core 的 `PathActionPolicy` 只做格式與縮寫處理。Windows 的 `ShellPathSelection` 在 worker 中初始化 COM、驗證檔案系統項目並準備 PIDL。檔案使用 `SHOpenFolderAndSelectItems`；資料夾在 reveal 時再次確認仍為資料夾，再以 PIDL 與明確的 `explore` verb 呼叫 `ShellExecuteExW`，不使用檔案的預設開啟動作或拼接命令列。Probe 的 path-worker 模式以兩步協定保存 PIDL：prepare 不開視窗；reveal 僅使用已準備的項目。IPC 不回傳路徑或 PIDL，原始路徑只存在記憶體與私有 stdin 管線，不放進程序參數。

控制器先沿用既有選取驗證，再 prepare；完成後重新驗證來源與選取，確認代次未失效才要求 reveal。每個 worker 請求最多 1.5 秒；新輸入、切換 App、停止或關閉時取消並終止等待中的 worker，防止延遲結果打開檔案總管。worker 在最終 Shell 呼叫前另比對前景程序身分。Shell 已接收的動作無法撤回，檢查與系統呼叫仍非原子操作。

## 回饋與驗收

狀態只包含格式錯誤、項目不可用、逾時、取消或已交由檔案總管處理，不包含完整路徑與例外訊息。自動化涵蓋字串政策、原生 PIDL prepare、取消／逾時後 worker 結束與恢復、唯讀工具列事件、選取失效及剪貼簿不變。另以可丟棄的中文／空白／逗號檔案實際呼叫 Shell，核對檔案總管選取結果。網路分享及混合 DPI 未實測時明確記錄，不宣稱已通過。

參考：[SHParseDisplayName](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shparsedisplayname)、[SHOpenFolderAndSelectItems](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shopenfolderandselectitems)。
