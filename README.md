# SnapBar Windows

依 [macOS 功能說明](Snapbra_REDME.md) 建立的 Windows 選字工具。目前為 **P0 技術原型**，並非原版所有功能的完整移植。

## 直接執行

開啟 `artifacts/p0/SnapBar.App.exe`。這份 Windows x64 自含式套件包含 .NET 執行環境，不需另裝 SDK。

1. 主視窗顯示「準備完成 · Ctrl+Alt+V」。
2. 按「開啟測試視窗」，在獨立視窗內選取文字。
3. 按 `Ctrl+Alt+V`，選取附近會出現複製／剪下／貼上動作列。
4. 如需拖曳／雙擊後自動顯示，回主視窗按「啟用自動偵測」。預設為關閉。
5. 在原 App 按鍵、捲動、點擊外部或切換視窗，動作列會關閉。
6. 關閉主視窗即結束監聽與背景程序；測試視窗是獨立程序，請另行關閉。

分享時需複製整個 `artifacts/p0` 資料夾或完整解壓縮 ZIP，不可只複製單一 EXE。此原型尚未簽章，也不會自動安裝、設定登入啟動或加入系統匣。

## 目前功能

使用者已於 2026-10-22 回報 **Chrome、Edge、記事本測試正常**。測試來源與尚未覆蓋的情境記錄於 [P0 驗證報告](docs/p0-validation.md)。

- UI Automation 選字讀取、範圍定位與手動快捷鍵。
- 可切換的拖曳／雙擊／三擊選字偵測；合併約 120ms 的事件。
- 不取得焦點的動作列，保留來源 App 的文字選取。
- Copy 複製選取純文字；Cut／Paste 在重新驗證後送出 Ctrl+X／Ctrl+V。
- 唯讀、未知編輯狀態、非焦點文字 provider、多段選取不提供寫回。
- 密碼欄位拒絕讀取；視窗、程序、焦點或文字範圍變更即取消動作。
- UIA 背景程序、1.5 秒讀取逾時與下次請求重啟。
- 記憶體內最多 100 筆中性操作狀態，不保存原文或剪貼簿。

預設 UIA 路徑不會模擬複製或暫時覆寫剪貼簿。Sublime 可選擇下述相容模式。Paste 使用當前剪貼簿，P0 只接受含文字的資料。

## Sublime Text 相容模式

Sublime Build 4180 的本機 UIA 樹沒有提供編輯器選取文字。新版提供不安裝程式外掛的替代路徑：

1. 關閉已執行的舊版 SnapBar，再開啟新版 `artifacts/p0/SnapBar.App.exe`。
2. 勾選「Sublime 剪貼簿相容模式（僅複製）」。每次啟動需重新勾選。
3. 在 Sublime 編輯器選取一段文字，按 Ctrl+Alt+V；自動顯示另需啟用「自動偵測」。
4. 相容模式只開放 Copy；Cut／Paste 因無法驗證可編輯狀態而停用。

勾選時會在 `%APPDATA%/Sublime Text/Packages/User/Default (Windows).sublime-keymap` 加入兩條 Ctrl+Alt+Shift+F12 綁定。既有設定會備份並保留註解；快捷鍵衝突則不啟用。不安裝 Python 外掛、不改 Ctrl+C。內建快捷鍵條件只接受單段非空選取，搜尋面板、快速面板與多游標不觸發。攜帶版 Data 目錄目前不支援。

讀取會暫借剪貼簿並嘗試還原，支援文字、常見圖片、檔案清單及可備份的註冊格式；無法備份的特殊格式或超過 64 MiB 時略過。Windows 允許剪貼簿沒有擁有者視窗，Sublime 實測即為此情況；新版不再把它誤判為其他程式更新，而會同時檢查前景、輸入與序號是否穩定。已知其他擁有者或再次變動的序號會取消讀取。來源不明的並行寫入仍無法完全區分。

Windows／第三方剪貼簿歷程仍可能留下暫存複製；極慢的目標、並行操作或強制結束程序無法保證還原。

取消勾選可停止相容模式。需要撤回 Sublime 設定時，移除該設定檔 SnapBar 註解下方新增的兩條綁定，保留其他快捷鍵。已在 Sublime Build 4180 實測手動、雙擊與拖曳顯示工具列、空選取不顯示，以及偵測後貼出原剪貼簿文字。細項見驗證報告。

## 已知限制

- 一般 App 需要提供 UIA 選字介面；只有明確啟用的 Sublime 相容模式會使用剪貼簿。
- 暫無 Search、Open Link、檔案總管、... 選單、排除清單、改鍵設定、AI、自訂動作與 Planka；依規劃後續加入。
- Ctrl+Alt+V 衝突會顯示提示；P0 可改用自動偵測，尚未提供改鍵 UI。
- Cut／Paste 只對原本仍在前景、單段選取、已確認可編輯且有焦點的文字 provider 執行；不嘗試強制恢復前景。檢查與輸入仍受 Windows 非原子操作及 UIPI 限制。
- 輸入成功插入佇列只顯示「已送出指令」，不宣稱目標 App 已完成處理。
- 最多讀取 32,768 個 UTF-16 code units、16 個選取範圍，超限回報錯誤。
- 多螢幕 DPI 已有定位邏輯，但混合 DPI 的實體裝置驗收尚未完成。
- 初次 UIA 讀取有冷啟動成本；詳細數據與未驗證項目見 [P0 驗證報告](docs/p0-validation.md)。

## 建置與測試

使用 `.NET SDK 10.0.402` 或 global.json 允許的相容修補版。此工作區已將 SDK 安裝於 `.tools/dotnet`，未修改全域 PATH。其他機器可自行安裝相同 SDK，再使用建置腳本。

```powershell
# 建置全部專案並執行核心政策測試
pwsh -NoProfile -File .\scripts\build.ps1

# 額外發布自含式套件並執行 helper 通訊 smoke test
pwsh -NoProfile -File .\scripts\build.ps1 -Publish

# 僅執行純邏輯測試（失敗回傳非零 exit code）
& .\.tools\dotnet\dotnet.exe run --project .\tests\SnapBar.Tests -c Release

# 原生剪貼簿整合測試：暫時寫入測試資料，完成後還原原剪貼簿；執行時請勿同時複製內容
& .\.tools\dotnet\dotnet.exe run --project .\tests\SnapBar.Windows.Tests -c Release -- --clipboard-roundtrip

# 直接開啟獨立測試視窗
& .\artifacts\p0\SnapBar.App.exe --fixture
```

測試採 console runner，刻意不依賴第三方測試套件；使用 `build.ps1` 或 `dotnet run`，不是以 `dotnet test` 的空白成功結果判定測試通過。

## 專案結構

| 專案 | 職責 |
| --- | --- |
| SnapBar.Core | 選字通訊模型、寫入政策、過期結果防護、位置計算 |
| SnapBar.Windows | Win32、輸入監聽、helper 程序管理 |
| SnapBar.Probe | MTA 上的 UIA 讀取與範圍重新驗證 |
| SnapBar.App | WPF 原型控制視窗、動作列、測試視窗 |
| SnapBar.Tests | 核心政策與位置邊界測試 |
| SnapBar.Windows.Tests | 實際 Win32 剪貼簿備份／還原整合測試（需明確參數啟用） |

## 規劃與紀錄

- [Windows 完整功能規劃](docs/superpowers/specs/2026-10-22-snapbar-windows-design.md)
- [P0 任務清單](docs/superpowers/plans/2026-10-22-snapbar-p0.md)
- [P0 驗證報告與人工驗收步驟](docs/p0-validation.md)
- [瀏覽器測試頁](tests/fixtures/selection.html)

Markdown 與程式碼採 UTF-8；中文 Markdown 使用無 BOM 編碼。
