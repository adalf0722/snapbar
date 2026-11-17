# Sublime 通用選字流程可替代性驗證

日期：2026-11-17（Asia/Taipei）。原始碼：`1b507983c747f105aeeaf2d07eec83186d0616f9`。

## 結論

本機 Sublime Text build 4180 的編輯區無法直接改走目前的通用選字流程。除了產品明確排除 Sublime，實測也缺少通用流程要求的 UIA 選字及 MSAA 文字來源證據。一般 Ctrl+C 可複製單段文字，但空選取會複製整行，多選取會合併文字；不能據此證明目前有單段選取。現階段保留專用綁定與首次設定。

## 方法與範圍

- 使用獨立視窗與可丟棄 fixture：`artifacts/sublime-verification/snapbar-sublime-verification.txt`。未編輯使用者原有文件。
- 臨時診斷程式位於 `artifacts/sublime-verification/`，引用目前 Windows／Core 專案，直接呼叫 `NativeClipboardSourceReader.Read/Check`，繞過 App 排除名單評估原生能力；另向既有 Release Probe 發送 `capture`（`PrepareProvider=true`、`Diagnostics=true`）。未修改正式路由。
- 用 computer-use 操作 fixture 的選取及快捷鍵。普通 Ctrl+C 測試不使用 SnapBar 專用綁定；專用複製測試使用 `Ctrl+Alt+Shift+F12`。
- 剪貼簿觀測程式沿用產品 `ClipboardBackup`，完整備份後等待實體快捷鍵造成序號變化，僅輸出 fixture 比對布林值，隨即還原。未將選字或原剪貼簿內容寫入紀錄。
- Sublime 使用者快捷鍵檔未修改、移除或重新安裝。SnapBar 設定與產品程式未修改；既有 SnapBar 發行版保持執行，因此本輪不是獨占桌面套件或完整 Release 證據。

## 原生能力實測

在 fixture 第二行選取單一單字，游標位於編輯區：

| 項目 | 結果 |
| --- | --- |
| UIA 焦點類型 | `50032`（Window） |
| 焦點 TextPattern／ValuePattern | 均未提供 |
| 真實 Probe | `unsupported`，沒有選取與剪貼簿候選 |
| Probe 診斷階段 | `fallback-password-state-unavailable` |
| 原生根視窗與焦點類別 | `PX_WINDOW_CLASS` |
| 原生焦點與滑鼠視窗 | 相同，均屬於 fixture |
| MSAA 滑鼠／焦點角色 | 均為 `10`（client），並非文件或文字角色 |
| MSAA 滑鼠／焦點狀態 | 均為 `1048580` |
| 直接呼叫原生來源資格驗證 | `clipboard-unavailable` |
| 現行通用 App 資格 | `false` |

因此，即使刪除 `ClipboardSelectionAdapter.IsTarget` 的 Sublime 排除條件，實測來源仍不滿足 `NativeClipboardPolicy` 要求的文字／文件角色（42／15）。不能以取消排除名單取代來源驗證。

## 複製對照

| 情境 | 一般 Ctrl+C | SnapBar 專用複製 |
| --- | --- | --- |
| 單段選取 | 成功，內容等於 fixture 的單一選字 | 成功，內容等於同一選字 |
| 空選取，游標在第二行 | 複製整行，非空選取 | 45 秒觀測期內剪貼簿序號不變 |
| 兩段選取 | 以換行合併兩個選字 | 45 秒觀測期內剪貼簿序號不變 |

有複製的四次有效觀測，均確認來源前景未變與完整備份還原成功；兩次專用拒絕觀測未改動剪貼簿。未測試剪下、貼上、唯讀文件或混合 DPI，不能外推為完整 Sublime 動作回歸。

## 過程中排除的觀測

首輪診斷因 Probe 子程序找不到 .NET 10 而失敗，設定 `DOTNET_ROOT` 為專案 `.tools/dotnet` 後完成。初次空選取普通／專用複製的 20 秒觀測窗口不足以可靠涵蓋工具往返，沒有採計；延長為 45 秒並在等待中送鍵後重測。最後的單段正向對照曾收到使用者輸入干擾提示，重新觀察、確認只剩單段選取後才送鍵，成功取得與還原測試文字。

## 產品取捨

目前可精簡設定的呈現方式，但尚無證據可刪除 Sublime 專用整合。若要移除使用者快捷鍵設定，替代方案必須獨立提供來源驗證、單段非空選取判定及可編輯資格；不能只強制對不透明視窗送 Ctrl+C。此輪僅驗證，不新增適配器或放寬安全政策。
