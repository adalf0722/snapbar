# 自動複製與 Sublime Ctrl 選取交接

交接日期：2026-11-17（Asia/Taipei）。預計 2026-11-18 繼續；使用者要求今天先整理文件，不再修功能。工作目錄：`D:\codex\snapbar`。

## 2026-11-18 接續結果

以下舊交接內容保留為歷史狀態；本節優先。已在真實 Sublime fixture 重現：先按 Ctrl 再拖選會建立多重選取，曾直接觀察到狀態列顯示 `2 selection regions`。既有單段非空選取綁定因此不回傳資料；放鍵等待與原生 Probe 已完成，失敗位於 Sublime 第一次相容性讀取，不是第二次自動複製重驗或工具列呈現。

新增 `SublimeAutoCopyTest.cs`（入口與座標要求見 `docs/development.md`）。最終 18 次手勢／54 項檢查通過：一般拖選 6/6 顯示工具列且不自動複製；Ctrl-before 1/6 複製及顯示、5/6 安全拒絕且剪貼簿不變；Ctrl-during 6/6 複製及顯示，包含 50 與 1100 毫秒放鍵延遲。紀錄：`artifacts/checks/sublime-auto-copy/20261118-ctrl-timing-characterization.log`。這是本機樣本，不宣稱所有 Sublime 組態均可靠。

**使用者已選擇保留安全限制，驗證並說明可行操作。** 目前建議先開始拖選，在放開滑鼠前按住 Ctrl，再放開 Ctrl。英文與繁中設定說明已補充；沒有改綁定、偏好、timeout 或單段選取保護，也沒有以一般 Ctrl+C 取代。先按 Ctrl 的多重選取仍可能略過；不將其標記為已修復或支援。新增的 opt-in 診斷可區分 Sublime 擷取、重驗、最終寫入與工具列呈現，仍不含文字內容。

本輪尚未提交或發布。最新回歸結果與執行狀態見 `docs/p0-validation.md` 的 2026-11-18 小節。

## 最新狀態：以這次使用者回報為準

- **Chrome：使用者確認目前可以。** 先前按 Ctrl 選取不複製的情況，在修正後已改善。
- **Sublime Text：仍未解決。** 使用者原話：「chrome可以,但sublime按ctrl,action bar部出現的機率高」。依上下文記錄為按 Ctrl 選取時，動作列經常不出現；尚無成功／失敗次數與失敗時的階段紀錄。
- 不要因為 fixture 測試通過，將 Sublime 實機問題標記完成。下一輪要同時觀察動作列與剪貼簿，不能假定兩者一定一起失敗。
- 今天不再進行程式修改、重啟、剪貼簿操作或實機重現。未建立明天的自動排程。

## 工作區與執行狀態

目前 HEAD 是 `8e5c207 docs(selection): define auto-copy behavior`，上一筆是 `7d71aae fix(settings): clarify selection assistance labels`。

**自動複製實作、測試及修正仍在工作目錄，尚未提交。** 多個新 `.cs`、release note 及計畫檔尚未追蹤；只看 HEAD 或重新 checkout 會漏掉本輪成果。明天先跑 `git status --short`，保留既有修改，不要 reset／clean／覆蓋。

交接時開發版 App 與 Probe 正在執行。App 路徑為 `src/SnapBar.App/bin/Release/net10.0-windows/SnapBar.App.exe`，Probe 為其下的 `Probe/SnapBar.Probe.exe`。最後一次啟動未啟用持續事件診斷，未發布或替換 `artifacts/p0`。

讀取本機設定時確認：

| 設定 | 交接時的值 |
| --- | --- |
| `automaticDetectionEnabled` | `true` |
| `autoCopy.enabled` | `true` |
| `autoCopy.mode` | `0`，按住 Ctrl 選取才複製 |
| `autoCopy.showFeedback` | `false`，使用者目前關閉提示；不要自行打開 |
| `clipboardCompatibilityEnabled` | `true` |
| `sublimeCompatibilityEnabled` | `true` |

設定檔位於 `%LOCALAPPDATA%\SnapBar\settings.json`。不要輸出整份設定檔，內含與本問題無關的使用者偏好；也不要改動 Sublime 既有綁定。

## 已實作的行為

1. 自動複製預設關閉，與自動顯示動作列獨立。啟用後預設按住 Ctrl 選取才複製；另有選取即複製、按 Ctrl 略過本次的模式。
2. 滑鼠完成選取時記住修飾鍵狀態，等待按鍵放開後才擷取。等待上限現為 **兩秒**，不是初版的 600 毫秒。成功提示可獨立關閉，約 900 毫秒、不搶焦點。
3. 寫入前重驗選取與目標；原生剪貼簿寫入在同一個鎖內檢查序號與請求有效性。手動複製共用此寫入機制。
4. 擷取排到低階輸入 hook 返回後執行，排隊中的請求也受世代與前景檢查保護。

## 已定位並修過的取消原因

第一次使用者回報 Chrome／Sublime 都失敗。開啟 opt-in 事件診斷並請使用者重現後，看到 `input-selection Control` 與 `auto-copy-wait start`，接著連續 `input-key`，沒有進入原生擷取。

已確認重複 Ctrl-down 會被當成新操作，使整個選取請求失效；新增的回歸案例在修正前確實失敗。`InputMonitor` 現在以 `heldModifiers` 記錄左右輔助鍵，只在第一次按下時使舊請求失效，同一鍵持續按住的重複事件仍傳給來源程式，但不再次取消。放開後重新按下仍屬新操作。

實機紀錄也顯示放開滑鼠後仍按住 Ctrl 超過一秒，因此將放鍵等待由 600 毫秒增為兩秒。等待結束後才建立候選，沒有延長 Probe 擷取預算或候選 lease。

**上述修正足以讓 Chrome 可用，但不能證明 Sublime 剩餘問題也是同一原因。** 尚未取得修正後 Sublime 失敗的診斷紀錄。

## 明天優先排查

先在可丟棄的 Sublime fixture 重現，不要先加大 timeout 或放寬驗證。可使用 `tests/fixtures/sublime-actions.txt`；只做選取與複製，避免修改真實文件。正常結束 SnapBar 後才能建置或跑桌面測試，包含系統匣背景程序。

建立簡單對照，分別記錄動作列出現／剪貼簿是否改變／失敗階段：

- 一般拖選、按 Ctrl 再拖選、拖選途中再按 Ctrl。
- 放開滑鼠後立即放 Ctrl、延遲約一秒放 Ctrl，以及超過兩秒仍按住的情況。
- 工具列開啟時，自動複製關閉與開啟的對照；測試後恢復原偏好。
- 從未反白的文字開始選取，避免把拖曳既有反白誤認為重新選字。Sublime 的 Ctrl 滑鼠操作也可能影響多選取，必須確認單段選取狀態；目前僅是排查方向，尚未證實是根因。

沿著這條路徑定位失敗，不要只看最後有沒有提示：

| 階段 | 檢查重點 |
| --- | --- |
| `InputMonitor.MouseCallback` | 是否真的送出 `input-selection`；Ctrl 快照是否正確，拖選／雙擊是否漏判 |
| `OnInput` 與排隊 callback | 是否被後續真正按鍵、點擊、焦點或新世代取消 |
| `auto-copy-wait` | 是否有 `released`，或兩秒後成為 `modifier-held` |
| 原生回應與 Sublime fallback | 是否辨識 Sublime、是否進入專用讀取、綁定是否 Ready、是否仍有輔助鍵按住 |
| 第一次剪貼簿交易 | 是否 `busy`、序號改變、來源失效、無回應、還原失敗或選取不合資格 |
| 自動複製重驗 | Sublime 會再讀一次；原文、目標、序號是否一致，有無新的取消事件 |
| `popup.Present` 之前 | 目前是在自動複製重驗之後才呈現工具列；查明是否因此被延遲或取消。這是待驗證假設，不是已確認根因 |

Sublime 專用讀取使用 `Ctrl+Alt+Shift+F11`（可編輯）／`F12`（一般）綁定，注入事件以 `Native.InputMarker` 排除。檢查真實來源處理時序與按住狀態，但不得吞掉來源應收到的鍵盤事件。

先前實機調查顯示，本機 Sublime build 4180 沒有 UIA TextPattern／ValuePattern，不能把它當成 WPF fixture。普通 Ctrl+C 在空選取時可能複製整行，多選取也可能合併；不要為了讓工具列出現而改用不受保護的普通 Ctrl+C。背景與方法見 [Sublime 驗證紀錄](2026-11-17-sublime-generic-flow-verification.md)。

## 診斷入口

`SelectionDiagnostics` 可從「關於與支援」啟動兩分鐘重現記錄，也可用程序環境變數 `SNAPBAR_SELECTION_DIAGNOSTICS=1` 在啟動時開始。重現快照位於 `%LOCALAPPDATA%\SnapBar\diagnostics\reproduction.json`。平常的選字事件留在記憶體。診斷欄位排除選取文字、剪貼簿內容、按鍵值與控制項文字。

目前已有 `input-*`、`auto-copy-wait`、`native`、`capture-result` 等階段。Sublime 第一次及第二次讀取的細部階段不一定足夠，必要時補上不含內容的狀態與交易時序。先前重現檔為 `selection-20261117T140906420Z-24632.jsonl`；**它是上一個問題的紀錄，不是目前 Sublime 失敗的證據**。

正常結束現有 SnapBar 後，可在 PowerShell 7 暫時啟用診斷啟動：

```powershell
$previousDiagnostics = $env:SNAPBAR_SELECTION_DIAGNOSTICS
try {
    $env:SNAPBAR_SELECTION_DIAGNOSTICS = '1'
    ./scripts/run-dev.ps1
}
finally {
    if ($null -eq $previousDiagnostics) {
        Remove-Item Env:SNAPBAR_SELECTION_DIAGNOSTICS -ErrorAction SilentlyContinue
    } else {
        $env:SNAPBAR_SELECTION_DIAGNOSTICS = $previousDiagnostics
    }
}
```

啟動後的 App 會繼承診斷環境並自動開始兩分鐘的重現記錄。

## 程式與文件入口

| 路徑 | 責任 |
| --- | --- |
| `src/SnapBar.Windows/InputMonitor.cs` | 滑鼠選取、修飾鍵快照、`heldModifiers` 與注入事件排除 |
| `src/SnapBar.App/SelectionController.cs` | 輸入取消、排隊擷取、放鍵等待、fallback 與工具列呈現 |
| `src/SnapBar.App/SelectionController.AutoCopy.cs` | 兩秒等待常數、自動複製重驗、最終寫入與提示 |
| `src/SnapBar.Windows/SublimeSelectionAdapter.cs` | Sublime 專用讀取計畫 |
| `src/SnapBar.Windows/ClipboardSelectionReader.cs` | 按鍵狀態、剪貼簿備份／還原、來源與序號交易 |
| `src/SnapBar.Windows/SelectionClipboardWriter.cs` | 鎖內序號／有效性檢查與 Unicode 寫入 |
| `src/SnapBar.App/SelectionDiagnostics.cs` | opt-in、限量且不含內容的診斷 |
| `tests/SnapBar.Windows.Tests/AutoCopyTest.cs` | WPF fixture、自動複製與 Ctrl 重複／放鍵延遲回歸 |
| `tests/SnapBar.Windows.Tests/SublimeActionTest.cs` | 既有 Sublime 可丟棄 fixture 驗證入口，不等於自動 Ctrl 拖選實機驗收 |

設計：[自動複製設計](superpowers/specs/2026-11-17-auto-copy-design.md)。計畫：[實作計畫](superpowers/plans/2026-11-17-auto-copy.md)。完整驗證：[P0 驗證紀錄](p0-validation.md)。新增但未發布的說明為 `release-notes/2026-11-17-auto-copy.json` 與 `release-notes/2026-11-17-auto-copy-held-control.json`。

其他未提交實作包含 `AutoCopySettings.cs`、`SettingsStore.cs`、一般設定的 AutoCopy partial、MainWindow 設定接線、`CopyFeedbackWindow.cs`、雙語資源、Core／Windows 測試，以及將 AutoCopy 列入 Release 的腳本。明天應以整份工作區為基礎，不只攜帶 InputMonitor 的最後修正。

## 已完成的驗證與界線

| 驗證 | 結果與報告 |
| --- | --- |
| 修正前重複 Ctrl 回歸 | 明確失敗：`artifacts/checks/desktop/20261117T141055282Z-30a8fb0fc98e4ac0b2e43f9d78927e91/report.json` |
| 忽略同次長按重複事件後 | AutoCopy 32、Compatibility 33、Keyboard 26、Lifecycle 144 通過：`artifacts/checks/desktop/20261117T141308240Z-0d685b36c07143918eeec78e7b25b20a/report.json` |
| 放鍵等待延長為兩秒後 | AutoCopy 32 通過，含延遲 1.1 秒及三次重複 Ctrl-down：`artifacts/checks/desktop/20261117T141424560Z-24dc528a2910416198bfb0556a0fdfd9/report.json` |
| 最後完整 `scripts/check.ps1` | 通過；Core 688、ActionRunner 37，零略過。建置：`artifacts/checks/build/20261117T141608594Z-2d48ff6ace164a26a8daf27d4c259401/report.json`；動作執行：`artifacts/checks/action-runner/20261117T141616892Z-fafee33dcd204c08a569a950fd47610e/report.json` |
| 使用者實機確認 | Chrome 可以；Sublime 按 Ctrl 時動作列仍常不出現，待修 |

桌面測試中曾發生注入漏接、前景取得及 UIA 執行緒問題，過程有失敗報告；以上只列最後的通過證據。不要以反覆重跑直到通過來替代 Sublime 問題定位。WPF fixture 的 UIA 代理需在 MTA 建立及使用，避免阻塞安裝 hook 的 UI 執行緒。

未完成完整 Release 套件、混合 DPI／多螢幕人工驗收，也沒有發布套件。明天完成修正後應先跑針對性的回歸及實際 Sublime 對照，再跑必要檢查，記錄實際結果。

## 必須保留的安全與使用者選擇

密碼拒絕、前景／焦點重驗、候選身分、過期結果取消、剪貼簿序號與完整備份還原均不可放寬。通用相容性與 Sublime 明確啟用偏好、既有綁定、目前關閉的成功提示均須保留。不得記錄選取或剪貼簿內容。測試前使用可丟棄文字並保存剪貼簿，避免同時複製或執行其他桌面測試。
