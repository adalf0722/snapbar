# SnapBar Windows

依 [macOS 功能說明](Snapbra_REDME.md) 建立的 Windows 選字工具。目前完成 P0 選字原型，以及 P1 的系統匣、設定保存、排除 App、搜尋、開啟連結與檔案位置，並非原版所有功能的完整移植。

## 直接執行

開啟 `artifacts/p0/SnapBar.App.exe`。這份 Windows x64 自含式套件包含 .NET 執行環境，不需另裝 SDK。

1. 主視窗顯示「準備完成 · Ctrl+Alt+V」。
2. 按「開啟測試視窗」，在獨立視窗內選取文字。
3. 按 `Ctrl+Alt+V`，選取附近會出現複製／剪下／貼上／搜尋動作列；整段有效網址另顯示「開啟連結」，完整路徑另顯示「檔案位置」。
4. 如需拖曳／雙擊後自動顯示，回設定視窗按「啟用自動偵測」。首次預設關閉，之後記住偏好。
5. 在原 App 按鍵、捲動、點擊外部或切換視窗，動作列會關閉。
6. 關閉設定視窗後會留在系統匣；左鍵圖示可返回，右鍵可暫停／恢復自動偵測或結束。暫停時手動快捷鍵仍可使用。
7. 完整結束請使用設定視窗或系統匣的「結束 SnapBar」。測試視窗是獨立程序，請另行關閉。重複啟動會叫回既有設定視窗。

分享時需複製整個 `artifacts/p0` 資料夾或完整解壓縮 ZIP，不可只複製單一 EXE。沿用既有發布路徑。此版本尚未簽章，也不會自動安裝或設定登入啟動；更新前先完整結束舊版。

## 設定保存

自動偵測偏好、排除清單、搜尋網址與動作設定存於 `%LOCALAPPDATA%/SnapBar/settings.json`，不包含選取文字、剪貼簿或診斷紀錄。檔案損壞、無法讀取或版本較新時，採預設值並保留原檔，介面會提示無法保存。舊版 v1–v3 保留偏好並升級至 v4；更新時請使用完整新版套件。Sublime 相容模式仍每次自行啟用，啟動時不自動改寫 Sublime 設定。

## 動作列與自訂 action

新版動作列維持單排，提供深色／淺色、圖示／圖示加文字。唯讀文字收起剪下與貼上；完整網址或路徑提供適用入口。按「⋯」開啟更多動作，不會搶走原 App 的焦點；可輸入英文名稱／別名，或用 Ctrl+V 貼上中文名稱搜尋。上下鍵選取、Enter 執行，Esc 先清空搜尋再關閉。最近五個動作排在前面，清單內不重複。

設定視窗按「管理動作」，可統一安排內建與自訂動作：每列直接選擇動作列／更多選單／隱藏，預設釘選四個，最多六個，拖曳名稱或按上下箭頭排序。新釘選接在目前釘選的最後面；第七個須留在更多。上方顯示完整釘選順序與數量，包含只適用網址或路徑的條件動作，不再以範例文字過濾。實際浮窗才依來源情境收起不適用動作；窄工作區的尾端動作暫時收進更多，不改動已保存的配置。

按「新增動作」可建立最多 32 個自訂動作；新增與匯入預設進入更多。名稱最多 32 字元，內容最多 32,768 UTF-16 code units；整份設定上限仍為 64 KiB，超限時明確提示不能保存。

- **網址**：HTTP／HTTPS 範本中的一個 `***` 會替換成編碼後的完整選取文字。編輯器可預覽目的網址，只有實際點擊動作才開啟瀏覽器。
- **PowerShell**：預設使用 Windows PowerShell，從 `$env:POPCLIP_TEXT` 讀取選取。可指定 PowerShell 7 等執行環境的完整 `.exe` 路徑。
- **JavaScript**：使用 PATH 上的 `node.exe`，或自行指定完整路徑，從 `process.env.POPCLIP_TEXT` 讀取選取。需要自行安裝 Node.js。

腳本以目前使用者權限執行；保存與匯入不會執行。編輯器的「預覽／測試」會以範例文字**實際執行**腳本，可以取消。逾時 1–300 秒，預設 10 秒；stdout／stderr 各限制 256 Ki 個 UTF-16 字元。每次請求使用獨立程序群組，逾時、取消、輸出超限及主程序結束都會清理所屬子程序。這適合會完成的文字處理腳本，不能用來啟動需持續常駐的子程序。

輸出可選不處理、複製到剪貼簿、替換原選取、短暫提示、結果視窗或 Markdown 視窗。Markdown 支援標題、清單與程式碼區塊等基本本機排版，不執行 HTML 或載入外部內容；複製仍取得原始輸出。替換或按結果視窗「套用至原文」會重新驗證可編輯狀態及原選取，將結果放入剪貼簿並送出貼上；來源已變更便取消。空輸出保留原文，提供結果視窗自行查看。

每個自訂動作的「⋯」提供複製、匯出及刪除；刪除後可復原。`.snapbaraction` 使用版本化 JSON，匯入時先顯示完整內容供確認，再加入更多。macOS 舊格式尚未承諾直接相容。動作列設計及驗證見 [設計規格](docs/superpowers/specs/2026-10-27-action-bar-design.md) 與 [驗證紀錄](docs/p0-validation.md)。

## 搜尋與開啟連結

點「搜尋」後，以預設瀏覽器搜尋選取文字，預設使用 Google；唯讀文字也可搜尋。展開「搜尋設定」可保存自訂網址，例如 `https://www.google.com/search?q={query}`，或按「恢復 Google 預設」。範本須為 HTTP／HTTPS，且只有一個 `{query}`，放在查詢參數值中。中文、emoji 與 `&` 等符號會自動編碼；範本最多 2,048 字元，產生的網址最多 8,192 字元，超長時取消並提示。

「開啟連結」只接受整段選取的完整 HTTP／HTTPS 網址，保留查詢參數與片段；不補上 `https://`，不接受帳密、原始空白或其他協定。兩個動作都會先重新驗證來源與選取；只有點擊後才交給預設瀏覽器，選取本身不會送到搜尋服務。操作狀態不記錄選取內容或目的網址。

## 在檔案總管中顯示

選取完整路徑後，按「檔案位置」：資料夾會直接開啟，例如 `D:\codex` 開到該資料夾；檔案則開啟父資料夾並選取，不執行檔案。唯讀文字也可使用。接受 `C:\Windows\notepad.exe`、`\\server\share\文件.txt`、`%USERPROFILE%\Downloads`、`~\Downloads`，支援中文、空白、逗號及一組外層雙引號。磁碟與分享根目錄亦使用直接開啟路徑；實際網路分享仍待驗收。

工具列出現時只辨識格式，點擊後才存取路徑；項目不存在或無法存取時會提示。路徑查詢與 Shell 呼叫使用獨立 helper，每個請求最多 1.5 秒；查詢後再次驗證選取，期間輸入、切換 App 或結束會取消。拒絕相對路徑、URI、裝置路徑、萬用字元、其他環境變數及檔案資料流，不做命令列展開。路徑僅在記憶體與私有管線中傳遞，不加入診斷或程序參數。實際網路分享仍待人工驗收；Shell 已接收的開啟動作無法撤回。

## 排除 App

展開設定的「排除 App」，按「加入 App…」選擇要排除的 `.exe`，不會執行該檔案。清單顯示友善名稱及選取項目的完整路徑；新增／移除立即生效並保存，重啟後保留。重複項目不會新增，最多 128 項。

在清單中的 App 不會自動讀取選字或彈出工具列；`Ctrl+Alt+V` 仍可手動觸發，也仍遵守密碼與目標有效性檢查。以執行檔完整路徑精確識別，不同安裝位置分開處理；請選實際承載視窗的程式，而非啟動器或捷徑。

## 目前功能

使用者已於 2026-10-22 回報 **Chrome、Edge、記事本測試正常**。測試來源與尚未覆蓋的情境記錄於 [P0 驗證報告](docs/p0-validation.md)。

- UI Automation 選字讀取、範圍定位與手動快捷鍵。
- 可切換的拖曳／雙擊／三擊選字偵測；合併約 120ms 的事件。
- 不取得焦點的動作列，保留來源 App 的文字選取。
- Copy 複製選取純文字；Cut／Paste 在重新驗證後送出動作，Sublime 使用有條件的專用快捷鍵。
- Search 搜尋選取文字；Open Link 開啟整段 HTTP／HTTPS 網址，唯讀內容也可使用。
- 檔案位置將完整路徑交由檔案總管顯示並選取，不執行檔案；選字時不查詢網路路徑。
- 唯讀、未知編輯狀態、非焦點文字 provider、多段選取不提供寫回。
- 密碼欄位拒絕讀取；視窗、程序、焦點或文字範圍變更即取消動作。
- UIA 背景程序、每次最多 1.5 秒讀取逾時與下次請求重啟；僅不支援狀態會有限重試，總預算 2 秒。
- 記憶體內最多 100 筆中性操作狀態，不保存原文或剪貼簿。

預設 UIA 路徑不會模擬複製或暫時覆寫剪貼簿。Sublime 可選擇下述相容模式。Paste 使用當前剪貼簿，P0 只接受含文字的資料。

## Sublime Text 相容模式

Sublime Build 4180 的本機 UIA 樹沒有提供編輯器選取文字。新版提供不安裝程式外掛的替代路徑：

1. 關閉已執行的舊版 SnapBar，再開啟新版 `artifacts/p0/SnapBar.App.exe`。
2. 勾選「Sublime 剪貼簿相容模式（可編輯時提供剪下／貼上）」。每次啟動需重新勾選。
3. 在 Sublime 編輯器選取一段文字，按 Ctrl+Alt+V；自動顯示另需啟用「自動偵測」。
4. 確認為單段非空且可編輯的選取時提供 Copy／Cut／Paste／Search；唯讀或未知編輯狀態不提供 Cut／Paste。整段有效網址另提供 Open Link。

勾選時會在 `%APPDATA%/Sublime Text/Packages/User/Default (Windows).sublime-keymap` 安裝 Ctrl+Alt+Shift+F12／F11／F10／F9 共八條綁定，分別負責一般讀取、可編輯讀取、剪下、貼上及各自的預設 noop。完整符合舊版的兩條綁定可升級；既有設定會備份並保留註解，衝突或防護條件被修改時拒絕啟用。不安裝 Python 外掛、不改 Ctrl+C。條件只接受單段非空選取，排除搜尋面板、快速面板與多游標；寫入前另檢查可編輯狀態。攜帶版 Data 目錄目前不支援。

選字先走 UIA，只有明確不支援且相容模式已啟用的 Sublime 目標才回退；空選取、密碼、逾時與目標失效不回退。讀取得到的文字不等於寫入資格，動作前仍重新驗證。貼上保留並使用原本預期的剪貼簿內容；剪下結果不會被探測備份覆蓋。

讀取會暫借剪貼簿並嘗試還原，支援文字、常見圖片、檔案清單及可備份的註冊格式；無法備份的特殊格式或超過 64 MiB 時略過。Windows 允許剪貼簿沒有擁有者視窗，Sublime 實測即為此情況；新版不再把它誤判為其他程式更新，而會同時檢查前景、輸入與序號是否穩定。已知其他擁有者或再次變動的序號會取消讀取。來源不明的並行寫入仍無法完全區分。

若來源暫時未提供已宣告的格式，會釋放剪貼簿鎖，等待 40／80 ms 後最多重試兩次，無須先手動複製純文字。每次重建完整備份；新輸入或剪貼簿被更新便取消，保留新內容。仍無法備份時明確提示未讀取選字，不強行覆寫；不完整或已使用的備份也不能清空剪貼簿。修復驗證見 [剪貼簿恢復紀錄](docs/2026-10-26-clipboard-backup-recovery.md)。

Windows／第三方剪貼簿歷程仍可能留下暫存複製；極慢的目標、並行操作或強制結束程序無法保證還原。輸入失效或來源關閉時會停止等待；晚到的寫入不再用舊備份覆蓋，因此暫存複製仍可能留在剪貼簿。

取消勾選可停止相容模式。需要撤回 Sublime 設定時，移除 SnapBar 註解下方新增的全部八條綁定，保留其他快捷鍵；也可從 `.snapbar-*.bak` 備份恢復，先保留之後自行新增的設定。Build 4180 已實測選字與控制器複製／剪下／貼上路徑，實際浮窗按鈕點擊仍待人工補驗。細項見驗證報告。

## 已知限制

- 一般 App 需要提供 UIA 選字介面；只有明確啟用的 Sublime 相容模式會使用剪貼簿。
- 暫無改鍵設定、AI 與 Planka；依規劃後續加入。更多面板的中文搜尋目前以 Ctrl+V 貼上為主，尚未提供直接 IME 組字。
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

# 剪貼簿協調器競態測試：使用專用視窗及子程序，結束後還原剪貼簿
# 視窗出現後需於 60 秒內啟用，測試期間勿切換視窗或複製內容
& .\.tools\dotnet\dotnet.exe run --project .\tests\SnapBar.Windows.Tests -c Release -- --clipboard-races

# 常駐生命週期與偏好載入：只使用專用暫存設定，先結束其他 SnapBar 以免快捷鍵衝突
& .\.tools\dotnet\dotnet.exe run --project .\tests\SnapBar.Windows.Tests -c Release -- --tray-lifecycle

# 排除 App 原生整合：開啟專用 fixture，驗證自動停止／手動可用／移除恢復
& .\.tools\dotnet\dotnet.exe run --project .\tests\SnapBar.Windows.Tests -c Release -- --app-exclusions

# 搜尋／連結動作：使用唯讀 fixture 與記錄式啟動器，不連線搜尋服務
& .\.tools\dotnet\dotnet.exe run --project .\tests\SnapBar.Windows.Tests -c Release -- --web-actions

# 自訂腳本：執行可丟棄 PowerShell／Node 範例，驗證逾時、取消及子程序清理
& .\.tools\dotnet\dotnet.exe run --project .\tests\SnapBar.Windows.Tests -c Release -- --custom-action-runner

# 自訂動作 UI／控制器：專用可丟棄編輯器，套用測試會暫借並還原剪貼簿
& .\.tools\dotnet\dotnet.exe run --project .\tests\SnapBar.Windows.Tests -c Release -- --custom-actions

# 更多面板實際鍵盤：使用專用來源，暫借並還原剪貼簿，執行時請勿同時複製
& .\.tools\dotnet\dotnet.exe run --project .\tests\SnapBar.Windows.Tests -c Release -- --menu-keyboard

# 實際預設瀏覽器啟動：僅開啟本機 loopback 測試頁，會留下測試分頁
& .\.tools\dotnet\dotnet.exe run --project .\tests\SnapBar.Windows.Tests -c Release -- --browser-launch-smoke

# 路徑 worker：原生準備、取消、逾時、程序結束及重啟，不開啟檔案總管
& .\.tools\dotnet\dotnet.exe run --project .\tests\SnapBar.Windows.Tests -c Release -- --path-worker-tests

# 路徑動作列及控制器整合：會開啟檔案總管並選取發布版 README
& .\.tools\dotnet\dotnet.exe run --project .\tests\SnapBar.Windows.Tests -c Release -- --path-actions

# 實際 Shell 選取：在 artifacts 建立可丟棄檔案，驗證中文檔名、資料夾及腳本只被選取
& .\.tools\dotnet\dotnet.exe run --project .\tests\SnapBar.Windows.Tests -c Release -- --path-shell-smoke

# 直接開啟獨立測試視窗
& .\artifacts\p0\SnapBar.App.exe --fixture

# 唯讀路徑測試視窗；先啟動主程式，再按 Ctrl+Alt+V
& .\artifacts\p0\SnapBar.App.exe --fixture --path-fixture
```

測試採 console runner，刻意不依賴第三方測試套件；使用 `build.ps1` 或 `dotnet run`，不是以 `dotnet test` 的空白成功結果判定測試通過。

`build.ps1 -Publish` 更新 `artifacts/p0/`，不會自動重建 `artifacts/SnapBar-Windows-P0.zip`；交付 ZIP 時需另行重新打包。

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
