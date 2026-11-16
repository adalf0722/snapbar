# 相容輸入框寫入：實作與驗證

## 完成行為

共用 UIA 快速讀取與 Ctrl+C fallback 保留。普通相容來源仍不提供寫入；Probe 只有在標準 UIA Edit 明確提供 ValuePattern.IsReadOnly=false，或已驗證企業微信來源具有相符原生 caret 時，才建立與候選 ID 綁定的單次寫入資格。

Core 要求 Editable=true、單一非空選取、目標相符及 WriteToken 等於目前選取 ID。Native caret 資格保存 client 座標及 caret HWND；來源漂移、caret 消失、焦點／可編輯性改變或期限失效後拒絕。Probe 驗證失敗會清除可寫入候選期限，不能在原欄位恢復可寫時復活舊資格。

App 複製前後重新驗證來源，並在送出 Ctrl+X／Ctrl+V 前再次檢查前景、generation、貼上資料序號及既有 caret 身分。AI 與自訂替換沿用原文重讀、資格及 generation 驗證。密碼、保護狀態、相容開關與 Sublime 明確設定仍保留。原生 UIA 成功不會額外走 clipboard。

標準 UIA 可寫入檢查使用 GetCurrentPropertyValue(..., true) 排除未提供屬性時的預設 false；依據 [Microsoft API 文件](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement.getcurrentpropertyvalue)。企業微信資格僅限既有程序／原生主視窗適配，不把所有未知 opaque Window 視為可編輯。

## 自動測試

- Core 619 項通過，包含普通 clipboard 不可寫、token／目標／range 綁定、已知 caret 正向能力、未知／保護／唯讀／隱藏／異位置拒絕，以及雙擊水平容許範圍仍保留嚴格行高限制。
- NativeClipboard 11、CompatibilitySlow 32、Clipboard 13、ClipboardRaces 17、UiaReadiness 2、UiaDeepTree 2，共 77 項通過。包含真正的 Paste／Cut／自訂替換、同視窗換欄位拒絕、同欄位改選拒絕、延遲結果拒絕，以及撤銷資格不得恢復。CompatibilitySlow 包含真實等待 35 秒及候選期限延長。報告：`artifacts/checks/desktop/20261116T233251588Z-952917af3b744663aeea42ec60097d1c/report.json`；此份報告整體因後續 AiPanel 失敗而為 false，僅上述已完成分組作局部證據。
- AiPanel 254、Language 68 最終通過。報告：`artifacts/checks/desktop/20261116T233704748Z-dae40a11b3a3458bb354039615234022/report.json`。該輪與前輪產品程式相同，另外增加 AiPanel 測試的失敗診斷欄位；沒有改動 AI 複製行為。
- `scripts/check.ps1 -RequireActionRuntimes` 完整通過，包含 Core 619、ActionRunner 37，零略過。入口紀錄：`artifacts/checks/clipboard-write-final-check.log`；runner 報告：`artifacts/checks/action-runner/20261116T233850239Z-ba30ceb7b09644559cdf9f59edb4aeb5/report.json`。
- 當前來源重新建立自含式隔離 Probe，乾淨環境協定與啟動 smoke 11 項通過。輸出：`artifacts/investigations/wxwork-write/probe/`。

## 企業微信實機

使用者在關閉擋住工具列的 WinPoP 後，確認原型顯示「可編輯」。修正版再以實際企業微信草稿測試 SnapBar 工具列貼上已準備的測試字串、Ctrl+Z 復原，接著剪下、Ctrl+Z 復原；使用者對包含復原要求的問題回覆「都成功」。這是人工實體操作證據，不冒充自動化成功率。

提交前，使用者再回覆「簡單測試 OK」，並要求先建立本機 commit。

最終診斷紀錄的成功 capture 約 667 ms（首次）、147 ms、222 ms、232 ms。ClipboardWritable 以純布林值記錄，未保存選字、剪貼簿文字或寫入 token。原始紀錄：`artifacts/investigations/wxwork-write/final-selection-trace.jsonl`。樣本不足以推論所有版本／環境成功率。

測試前使用產品的完整剪貼簿備份能力保存原內容，暫時置入已知測試字串。測試後 guard 明確回報 `RESTORED: full original clipboard snapshot` 並正常退出，原有格式一併還原；未按送出鍵。

## 途中失敗與修正

- 第一版距離條件會拒絕部分雙擊：點擊在字串中央，caret 在端點。修正版把有界的水平允許距離與更嚴格的行高檢查分開，依 DPI 縮放；長距離無證據仍拒絕，不硬編碼草稿框螢幕位置。
- 寫入測試曾在 SendInput 送出後固定 100 ms 檢查時失敗；送出指令不等於獨立程序已處理完成，因此 fixture 改為最多三秒觀察同一個指令的完成狀態，絕不重送動作或修改期望文字。新測試最終全數通過，未以該結果宣稱所有時序根因均已確定。
- AiPanel 的長 Markdown 原文複製檢查在兩輪失敗（`20261116T233251588Z-952917af3b744663aeea42ec60097d1c`、`20261116T233429159Z-5017c14065d94ed1b5e05689524276b9`）。增加只含布林及長度的失敗診斷後重跑通過，未證實根因，不能宣稱修復該間歇性問題。
- CUA 對工具列的自動化點擊會先啟動其主設定視窗，使原選取正確失效。沒有把該次點擊當作 Paste 成功；改由使用者實體點擊完成驗收。
- 一輪完整 check 曾在使用者仍操作原型時被輸出鎖擋下；正常結束後最終完整 check 通過。未強制終止使用者 App。

## 範圍與限制

沒有執行完整 Release 發布 profile，沒有發布／替換 artifacts/p0。尚未驗證混合 DPI、多行／RTL 及各企業微信版本／登入頁面。caret 缺少、選字位置不符或候選過期時仍拒絕寫入；大範圍雙擊選字也可能維持唯讀。相容 AI 寫回仍要求原候選尚未過期，並未延長原有 AI 期限。

本輪實作與自動測試完成，開發版供實機試用；既有日誌不記錄選字與剪貼簿內容的界線保留。
