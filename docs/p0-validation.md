# P0 驗證紀錄

## 2026-10-31 調整：結果空白符號與輸入／輸出比較

結果標題旁新增「顯示空白符號」，預設保留原始文字。勾選只切換同一份結果的顯示，不重新執行腳本，也不修改輸入、腳本設定或實際輸出。取消勾選會完整還原原始字串，包括 Tab、CR、LF、CRLF 及 Unicode。開始新測試、修改設定或顯示失敗時會清除舊的顯示狀態；網址預覽不提供此開關。

「輸入／輸出比較」維持預設收合，字數與變更摘要移至展開內容，減少預設畫面的重複資訊。比較框及結果的符號模式關閉自動折行，長行以水平捲動查看，避免把版面折行誤認成實際換行。符號包含 `·` 空格、`⇥` Tab、`␍` CR、`↵` LF；單獨 CR 也保留實際行界。既有「使用選取文字」設定仍決定是否顯示範例與比較區；關閉後仍可檢視獨立結果的空白符號，不自動猜測或改寫使用者的動作設定。

本輪 Core **282 項**、原生介面 `--ui-appearance` **51 項**、錯誤診斷 `--action-diagnostics` **25 項**、Probe smoke **6 項**通過，共 **364 項**。Release 建置無警告／錯誤，Windows x64 自含版發布成功。新增驗證涵蓋切換不重跑、原始結果還原、CR／LF／CRLF、長行在一般與最小視窗寬度均不折行、預設收合、失敗／網址／停用選字時的顯示狀態。測試使用暫存設定與可丟棄資料，不連線查詢 IP，也不修改使用者動作或剪貼簿。

提交前補驗：`--custom-action-runner` **20 項通過**，`--action-layout` **13 項通過**。`--custom-actions` 首次因專用 fixture 未取得前景而未進入案例；重跑為 **63 項通過、1 項失敗**，失敗項目是「Apply replaces only the original selected range in disposable editor」。該次測試的退出與剪貼簿還原流程已執行；目前尚未確認失敗原因，不將這輪完整工具列整合測試列為通過。

已檢視 `artifacts/ui-polish/editor-result-compact.png`、`editor-comparison.png` 及 `editor-comparison-small.png` 的實際 WPF 渲染。因正常發布目錄的程式仍在執行，本輪新版放在 `artifacts/whitespace-review/`；保留執行中的版本，退出舊版後可開啟該目錄中的 `SnapBar.App.exe`。混合 DPI／多螢幕仍待人工驗收。

## 2026-10-31 修正：自訂動作完整錯誤與處理建議

自訂腳本失敗時，正式執行會顯示持續保留的診斷視窗；編輯器測試則留在原預覽區。先顯示簡短原因，展開「詳細資訊」後可查看與複製動作名稱、執行環境、原始錯誤、結束代碼及已產生的輸出。錯誤視窗不提供「套用至原文」，也不自動複製或寫入日誌。

PowerShell 的 CLIXML 錯誤會轉成可閱讀文字，原始內容也保留在詳細資訊。只有辨識到實際的非互動提示錯誤才顯示對應建議；對 `curl` 的建議同時核對腳本指令及錯誤來源，避免把其他指令的問題誤歸因給別名。PowerShell 有時會以結束代碼 0 回報無法互動的錯誤，現在也會呈現失敗診斷並阻止空結果替換原文。其他成功程序的 stderr 則保留為診斷訊息，不單憑 stderr 判定失敗。

逾時與輸出過大時保留已擷取內容；每個串流仍限制為 262,144 字元，並明示截斷或未完整讀取。保持原有取消、過期結果抑制、前景／選取重驗證及 Windows Job 子程序清理。

| 項目 | 通過 | 證據 |
| --- | ---: | --- |
| Core 政策與設定 | 275 | 完整既有 runner |
| 自訂腳本實際執行 | 19 | Unicode、非零結束與雙流、非互動提示、逾時前內容、雙流截斷、取消、隔離及子程序清理 |
| 診斷格式與原生介面 | 25 | 原始多行文字、中文 CLIXML、保守提示、截斷／未完成標記、預設收合、無套用、編輯器真實腳本失敗／重試 |
| 自訂動作原生整合 | 54 | 各輸出模式失敗、前景與剪貼簿未變、過期錯誤抑制、明確複製完整內容、可編輯原文未被失敗或代碼 0 的互動錯誤改寫、成功套用回歸 |
| Probe 協定與結束 | 6 | 發布腳本 smoke |

共 **379 項通過**，Release 建置與 Windows x64 自含發布均為 0 errors／0 warnings。整合測試使用專用 fixture，明確複製與成功套用測試後已還原原剪貼簿。以 WPF 實際渲染檢查收合／展開的錯誤視窗及編輯器內嵌錯誤，畫面保存在忽略目錄 `artifacts/diagnostics/`。

```powershell
pwsh -NoProfile -File ./scripts/build.ps1 -Publish
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release --no-build -- --custom-action-runner
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release --no-build -- --action-diagnostics
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release --no-build -- --custom-actions
```

本輪沒有新增混合 DPI／多螢幕或第三方應用程式矩陣驗收。已辨識提示涵蓋實測英文及繁體中文訊息；其他錯誤與語系仍顯示完整原始內容，不推測修復方法。錯誤內容超過串流上限時只能提供有標記的保留部分。

## 2026-10-30 修復：搜尋欄可點入並顯示原生游標

使用者指出更多動作的搜尋欄不像可輸入，也沒有可點入的游標。原介面其實是 TextBlock，僅靠全域按鍵攔截組合字串。現改為真正的 TextBox，包含白底邊框、提示字、I 型滑鼠游標、焦點框線與文字游標；可正常選字、移動、刪除、全選、貼上與使用 Windows 輸入法。展開更多仍不取得焦點，明確點入欄位才啟用搜尋視窗。

控制器僅允許自己的搜尋視窗暫時持有焦點，不因此丟棄來源工作階段。選擇動作時檢查目前仍是自己的搜尋前景及原來源完整身分，再嘗試返回來源並重驗原焦點、範圍、原文與可編輯狀態，未通過便取消。切到第三方視窗會立即失效，不會為過期動作搶回焦點。Esc 可關閉搜尋並返回原來源；Alt+F4 只收起浮窗，不會銷毀可重用的工具列。

本輪原生 `--menu-keyboard` **21 項通過**，以實際滑鼠點擊與未標記的鍵盤事件驗證欄位焦點、游標位置、Unicode 貼上、左右／Backspace、全選／刪除、原來源文字與範圍保留、Esc、Alt+F4 後重用及不搶第三方前景；測試原剪貼簿已還原。`--custom-actions` **29 項通過**，新增跨程序搜尋焦點持有、跨過多次前景 timer tick 仍保留工作階段、點擊搜尋結果後返回來源並重驗、搜尋期間範圍被改變時拒絕，以及切到其他視窗後取消；原有實際 Apply 與剪貼簿恢復亦通過。`--action-layout` **13 項通過**，包含前兩輪的六個排序及更多項目顯示回歸。

測試使用專用視窗與可丟棄 fixture，不修改使用者文件或設定。中文貼上已驗證；不同 Windows 輸入法的候選視窗與組字細節仍列人工補驗。來源 App 若因失焦自行清除選取，返回後會拒絕執行並要求重選，保留安全取消原則。

## 2026-10-29 修復：更多選單遺漏已配置的動作

使用者指出「檔案位置」設為更多選單後，更多動作仍為空。原因有二：GetMore 依適用性直接過濾非路徑文字；選到路徑時，GetBar 又會把設為 More 的項目自動提升到工具列，再從選單移除。

現在完整遵守顯示位置：只有 Bar 項目可顯示於動作列，More 項目保留在選單，Hidden 項目不顯示。More 不依適用性隱藏項目；無法執行者顯示停用按鈕及固定原因文字，例如「請選取完整的檔案或資料夾路徑」。鍵盤只導覽可執行項目。實際執行仍使用既有來源與路徑重新驗證，不把不可用項目變成可執行。

新回歸在修改前重現四項失敗（271 通過／4 失敗），修正後 Core **275 項通過**。原生 WPF `--action-layout` **13 項通過**，新增非路徑文字仍看得見停用的檔案位置與原因、Enter 不會觸發不可用項目、有效路徑留在 More 並啟用、實際選單 routed Click 正確傳遞事件。原有搜尋／路徑工具列測試明確把動作設為 Bar，以保留兩個位置各自的驗證意義。測試不修改使用者設定或剪貼簿。

## 2026-10-27 修復：第六個動作的預覽與新增釘選順序

使用者回報超過五個動作時即時預覽順序錯誤。確認兩個原因：設定預覽使用固定的普通文字範例，讓已釘選的開啟連結／檔案位置被條件過濾；新釘選只切換 placement，會沿用原本更多清單中的舊 slot，導致第六個動作插到第五個自訂動作前面。

現將設定預覽改為完整釘選清單，與管理清單共用已保存的順序，顯示 `已釘選 n/6`。條件動作提供適用條件提示，僅真正的浮窗依選字內容過濾。新釘選一律接在既有釘選最後；六個上限與工作區寬度溢出規則不變。

Core runner **271 項通過**。新增三項驗證第六個沿用 More 舊 slot 的回歸、六個條件動作完整排序、第六個自訂動作移到兩端及重新釘選。新增 `--action-layout` 原生 WPF runner **9 項通過**：預設四個、第五／第六個新增、條件動作保持可見、實際上移按鈕、路徑第六格、第七個拒絕而不損壞狀態、磁碟 roundtrip、重開管理視窗及實際浮窗的相對順序。測試使用專用暫存設定，不改使用者設定與剪貼簿。

```powershell
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Tests -c Release
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release -- --action-layout
```

## 2026-10-27 動作列重新設計與自訂 action

依使用者確認的互動稿完成 WPF 單排動作列、深／淺色及圖示／文字外觀、可搜尋更多面板、五筆最近使用與六個釘選位置。新增動作管理與即時預覽，支援位置切換、拖曳／箭頭排序、三種類型的新增／編輯／複製／刪除復原及完整預覽後匯入／匯出。設定 schema 升至 v4，保留舊版偏好與損壞原檔防護。

自訂網址在執行前重驗來源，選取文字只作編碼替換；PowerShell／JavaScript 以環境變數取得選取。實際程序使用 UTF-8、有界輸出、逾時及取消；先加入 request-owned Windows Job Object，才由固定 bootstrap 放行腳本，主程序結束後也清除所屬子程序。輸出提供複製、結果／基本 Markdown 視窗、短暫提示及重新驗證後貼回。一般選字與載入／保存／匯入設定不會執行腳本。

本輪驗證：

| 項目 | 通過 | 證據 |
| --- | ---: | --- |
| Core 政策與設定 | 268 | 遷移、64 KiB 原檔保護、排序／隱藏／最近、URL 編碼與輸入限制；另驗證網址動作不受先前腳本輸出模式影響 |
| 自訂腳本實際執行 | 14 | 真實 Windows PowerShell 與 Node Unicode roundtrip、非零結束／stderr、雙流上限、取消／逾時、平行隔離、暫存清理及主程序先結束的子程序清理 |
| 自訂動作原生整合 | 21 | 唯讀來源、更多面板與結果視窗不搶焦點、URL 導向記錄器、過期取消、原生可編輯 fixture 實際套用僅替換選取範圍、剪貼簿結果符合後還原原剪貼簿 |
| 更多面板真實鍵盤 | 10 | 未加 InputMarker 的原生按鍵通過實際 hook，Ctrl+V 搜尋中文／英文、來源不變、Ctrl 與一般鍵平衡、長清單捲動／Enter、Esc 與關閉後 Ctrl+A；剪貼簿已還原 |
| 原有搜尋／連結回歸 | 11 | 使用記錄式啟動器，未連線外部服務，原生選取重驗與剪貼簿未變 |
| 原有檔案位置回歸 | 11 | 路徑處理、過期／取消／再次重驗及真實 Explorer 選取發布版 README |
| Probe 協定與結束 | 6 | 發布腳本檢查請求、錯誤、失效工作階段及 EOF 關閉 |

共 341 項。本輪 Core／Windows 腳本及完整整合均以 console runner 執行，並非 `dotnet test` 的空成功結果。路徑回歸首次最後一項未看到 Explorer 的預期選取；關閉同時執行的主程式並啟用專用 fixture 後，完整 11 項通過。保留此前景依賴記錄，不把桌面整合視為無條件可背景執行。

另以實際 Windows 視窗核對管理入口、完整動作清單與即時預覽、新增編輯器的雙欄版面、網址編碼預覽及儲存／取消按鈕；未為畫面檢查新增使用者動作。圖示選擇提供向量圖示與中文名稱。

審查修正兩項更多面板問題：修飾鍵保留原來源的按下／放開平衡，避免攔截 Ctrl 後無法判斷 Ctrl+V；鍵盤選取在重建清單後會捲入可視範圍。替換結果另檢查剪貼簿文字及序號，以免其他程序插入不同內容時送出貼上。結果視窗顯示的原文／stderr 只在該次 UI 內，不寫入中性診斷。

限制：更多面板維持不搶焦點，因此目前中文名稱以 Ctrl+V 貼上搜尋，不直接支援 IME 組字。Markdown 為基本本機排版。腳本主程序結束會清除子程序，不能用動作啟動常駐背景工作。實體混合 DPI、多螢幕窄工作區，以及更多第三方 App 的自訂結果寫回仍需人工驗收。舊 macOS `.snapbaraction` 格式尚未承諾相容。

## 2026-10-26 修正：資料夾直接開啟本身

使用者回報選取 `D:\codex` 會打開 `D:\`。原實作將所有項目交給 `SHOpenFolderAndSelectItems`，該 API 在此用法會開啟父目錄並選取項目，符合先前規格但不符合此次資料夾使用預期。現改為資料夾／根目錄直接開啟本身，檔案維持在父目錄選取、不執行。資料夾使用已準備的 PIDL 與明確 `explore` verb；reveal 前再次確認資料夾屬性，拒絕查詢後被替換成檔案的目標。保留隔離 worker、逾時、取消與選取重驗證。

本輪 **252 項檢查通過**：212 核心、6 probe、17 路徑 worker、11 路徑控制器及 6 實際 Shell。新版 smoke 的資料夾案例在修復前失敗，修復後確認中文／空白資料夾直接開啟、檔案與 `.cmd` 仍正確選取且不執行、磁碟根目錄及本工作區上層 `D:\codex` 直接開啟。另確認 prepare 後資料夾被替換成檔案會拒絕 reveal。Release／發布 0 errors／0 warnings。實際 UNC 分享仍待驗收，下方同日各工作包保留為歷史基線。

## 2026-10-26 修復：Sublime 剪貼簿備份暫時失敗

使用者回報「剪貼簿來源未提供格式資料」連續出現，之後表示已恢復。已以 Win32 延遲格式重現相同錯誤類型，補上不持鎖的 40／80 ms 有限重試、前景／輸入／剪貼簿序號重驗證、部分備份清理，以及未完成／已消耗備份禁止還原。永久不可用仍保留內容並拒絕讀取，不強行覆寫。原截圖所對應的精確格式未捕捉，未宣稱已唯一定位其來源。

本輪重跑 **250 項檢查**：212 核心、13 剪貼簿往返、17 競態、6 probe 及 2 真實 Sublime；Release／發布 0 errors／0 warnings。四項新增競態修復前失敗、修復後通過，另新增兩項備份還原保護。真實 Sublime fixture 已驗證延遲備份恢復、完整還原與 Copy，檔案未修改。完整證據、指令與界線見 [備份恢復紀錄](2026-10-26-clipboard-backup-recovery.md)。下方 310 項為前輪功能基線，非本輪全部重跑。

## 2026-10-26 續作：在檔案總管中顯示

已加入條件式「檔案位置」動作。整段路徑格式有效時才顯示；選字時不查詢檔案或網路。支援磁碟機絕對路徑、UNC、開頭的 `%USERPROFILE%`／`~`、中文、emoji、空白、逗號及成對外層雙引號。相對／磁碟機相對／裝置路徑、URI、資料流、萬用字元、其他環境變數、控制字元、保留裝置名稱及超長輸入拒絕。設計見 [路徑設計](superpowers/specs/2026-10-26-path-reveal-design.md)。

點擊先重新驗證選取，再由獨立 path worker 檢查存在性並準備 Shell PIDL；完成後控制器再次驗證原目標與範圍，才要求顯示。每個 worker 請求最多 1.5 秒，取消或逾時終止該 worker，下次操作建立新程序。worker 最終呼叫前另檢查前景身分；不以清理 worker 為由終止 Explorer。路徑透過私有 stdin 管線傳入，不放在程序參數、回應或中性操作紀錄。Shell API 開啟父資料夾並選取項目，不執行檔案。

### 自動驗證與實際 Shell 結果

Release／自含式發布建置 0 errors／0 warnings，共 **310 項檢查通過**：212 項核心、15 項常駐／設定生命週期、7 項排除 App、11 項剪貼簿往返、13 項競態、6 項 probe 協定／結束、4 項 UIA 就緒／深層、11 項搜尋／連結事件、1 項瀏覽器本機啟動、15 項路徑 worker、11 項路徑動作整合及 4 項實際 Shell 選取。

- 新增 48 項核心案例，直接驗證路徑格式與正規化；未掛載磁碟的合法字串仍接受，證明此層不以存在性決定按鈕。
- 15 項 worker 檢查確認中文／空白／逗號／ampersand 檔案、資料夾及磁碟根目錄可準備 PIDL；無目標身分拒絕 reveal；準備結果只能使用一次；缺檔／錯誤路徑清除舊項目。測試專用停滯子程序確認取消、逾時、原 PID 結束、下次新程序、預先取消不送請求，以及 Dispose 清理。
- 11 項 WPF／控制器檢查確認唯讀路徑按鈕啟用、寫入停用、網址按鈕隱藏、不搶前景、真正 routed Click 事件傳遞正確路徑、查詢期間失效取消、取消 token 傳遞、資源釋放、無原始路徑的錯誤提示、逾時不 reveal、剪貼簿不變。另透過 UIA 程式化縮短原選取、不發出鍵盤事件，證明查詢後第二次選取驗證會阻止顯示。
- 上述 11 項最後一項使用產品的真實 worker，從唯讀發布版 README 路徑讀取至實際 Explorer，透過 Shell `SelectedItems()` 核對正確項目及中性成功狀態，剪貼簿序號未變。
- 獨立 Shell smoke 在忽略的 `artifacts/path-shell-smoke-*` 目錄建立中文／空白／逗號／emoji 檔名、資料夾和 `.cmd`，三者均由真實 Shell 選取並以 `SelectedItems()` 核對。腳本若執行會建立 marker；marker 未出現，確認只選取未執行。桌面畫面亦確認腳本為選取狀態。測試資料與視窗留於 artifacts，方便回看，未操作使用者文件。

```powershell
./scripts/build.ps1 -Publish
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release --no-build -- --path-worker-tests
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release --no-build -- --path-actions
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release --no-build -- --path-shell-smoke
# 人工路徑 fixture；需另有主程式執行，選取預設已完成
./artifacts/p0/SnapBar.App.exe --fixture --path-fixture
```

既有核心、搜尋／連結、常駐、排除 App、剪貼簿往返／競態及 UIA 檢查均回歸通過。深層 UIA 初次未取得專用 fixture 前景，改以明確啟用專用視窗重跑後冷／暖皆通過；未讀取其 automation tree 預熱。延遲 provider 冷／暖亦通過，瀏覽器仍僅用 loopback 頁驗證。

### 界線

整合測試覆蓋 WPF routed Click 及真實控制器／Shell，未宣稱已用實體滑鼠點擊不搶焦點的浮窗。UNC 僅驗證格式，未連線實際分享或測試權限／離線伺服器；逾時／取消由可控制的停滯 worker 驗證。根目錄僅驗證 PIDL 準備，不承諾不同 Shell 版本的畫面呈現。檢查與 OS 呼叫非原子操作，Shell 已接收的動作無法撤回；回報交由檔案總管不代表所有網路載入已完成。混合 DPI、窄工作區、跨 App 實體按鈕及長時間常駐仍待補齊。動作管理、改鍵、AI、自訂動作與 macOS 未新增。

## 2026-10-26：搜尋與開啟連結

已完成 P1 的 Search／Open Link。搜尋預設使用 Google，可保存 HTTP／HTTPS 範本及恢復預設；`{query}` 必須只有一個且位於查詢參數值。開啟連結只接受整段完整 HTTP／HTTPS 網址，拒絕帳密、原始空白、錯誤百分比跳脫及其他協定。兩者可用於唯讀文字，均沿用動作前的目標／焦點／選取重新驗證，並經 Windows 預設瀏覽器開啟。沒有自動連線搜尋服務，操作紀錄不包含選取內容或目的網址。

設計見 [網頁動作設計](superpowers/specs/2026-10-26-web-actions-design.md)。設定升至 v3，v1／v2 保留自動偵測與排除清單；損壞或未知版本仍保留原檔。工具列新增搜尋與條件式連結按鈕，寬度不足時換行；來源視窗不因顯示工具列失去前景。

### 自動驗證

Release／自含式發布建置 0 errors／0 warnings，共 **232 項檢查通過**：164 項核心、15 項常駐／設定生命週期、7 項排除 App、11 項剪貼簿往返、13 項競態、6 項 helper 協定／結束、4 項 UIA 就緒／深層、11 項搜尋／連結 WPF 動作事件、1 項實際預設瀏覽器本機啟動。

新增核心案例涵蓋中文、emoji、換行、保留符號編碼、查詢參數注入、固定參數與片段保留、IDN、原始／跳脫 emoji、錯誤／重複／錯位範本、空白／超長輸入、禁止的協定及帳密、v1／v2 升級、搜尋設定往返及錯誤設定原檔保留。曾發現此 SDK 的 `Uri.IsWellFormedOriginalString()` 拒絕有效的百分比編碼 emoji；現以明確字元及跳脫驗證取代，原始及跳脫 emoji 回歸均通過。

11 項 WPF 測試透過真正的工具列按鈕 routed Click 事件及記錄式瀏覽器啟動器，確認唯讀搜尋啟用而寫入停用、非網址隱藏連結、工具列不取得前景、預設及自訂搜尋編碼、連結保留參數／片段、無效連結不啟動、過期選取與未知動作拒絕、剪貼簿序號不變，以及啟動失敗時中性提示。另使用產品 `BrowserLauncher` 開啟 `127.0.0.1` 暫存 HTTP listener，確認預設瀏覽器實際送達正確含跳脫參數的 GET。測試未將選取送至 Google 或其他搜尋服務。

```powershell
./scripts/build.ps1 -Publish
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release --no-build -- --web-actions
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release --no-build -- --browser-launch-smoke
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release --no-build -- --tray-lifecycle
```

既有剪貼簿測試結束後還原原桌面剪貼簿。兩項 UIA runner 初次因專用 fixture 未取得前景而停止；改以隱藏的測試父程序啟動後，專用視窗正常取得前景，深層冷／暖與延遲冷／暖共 4 項均通過。

### 設定介面操作與界線

已在自含式新版實際展開搜尋設定，保存缺少 `{query}` 的網址後顯示錯誤，設定檔 SHA-256 完全不變。保存 `https://example.com/search?q={query}` 後，JSON 為 v3；完整結束、重新啟動及展開後，文字框仍顯示自訂範本。實際點擊「恢復 Google 預設」後，介面及 JSON 均回到預設，自動偵測仍為原本的關閉狀態，排除清單仍為空。測試自訂值已清理。

WPF routed Click 覆蓋按鈕至控制器的事件串接，未宣稱已用實體滑鼠點擊不搶焦點的浮窗。跨 App 實際搜尋／開啟連結、混合 DPI、窄工作區及長時間效能仍待人工驗收。瀏覽器接受啟動只表示已交由瀏覽器處理，不宣稱遠端網站載入成功。檔案總管、AI、自訂動作、Electron 通用回退及 macOS 未新增。

## 2026-10-24 續作：排除 App 與設定升級

使用者明確回報「系統匣測試 OK」，登錄為系統匣人工驗收補充；下方前輪「待人工補驗」保留為歷史紀錄。本輪接續原 P1 的排除 App，設計見 [排除設計](superpowers/specs/2026-10-24-excluded-apps-design.md)。

已加入可收合的清單、原生 EXE 選擇器、友善名稱、完整路徑及移除。排除只影響自動讀取與浮窗；Ctrl+Alt+V 仍可用，未改動密碼／權限／有效性與寫入驗證。設定從 v1 升級到 v2，保留原自動偵測偏好；清單立即保存、重啟載入，大小寫不敏感精確比對完整路徑，不以同名程式或前綴代替。

### 自動驗證

Release 建置 0 errors／0 warnings，共 **166 項檢查通過**：114 項核心、11 項常駐與清單生命週期、7 項排除 App 原生整合、11 項剪貼簿往返、13 項剪貼簿競態、6 項 helper 協定／結束及 4 項 UIA 就緒／深層檢查。

新增核心檢查涵蓋同路徑／不同大小寫、同名不同安裝位置、前綴與子目錄不誤排除、未知路徑、slash／dot segments、延伸 DOS／UNC 路徑、相對路徑／萬用字元拒絕、重複／null／超量清單、友善名稱、v1 升級、Unicode 往返，以及保存上限不覆蓋原檔。生命週期新增清單保存、重複項目不改檔、重新載入及移除。

原生整合直接使用產品控制器與獨立 fixture，7 項確認：

1. 程序啟動身分不同時不接受其執行檔路徑。
2. 已排除的自動手勢不新增 helper、不產生文字快照、不變動剪貼簿。
3. 已排除目標仍能經 UIA 手動取得 7 字可編輯選取。
4. 清單變更立即使已存在的快照失效。
5. 移除排除後，自動 capture 恢復。
6. 同時暫停自動偵測與排除 App 時，手動 capture 仍可用。
7. 讀取尚未完成時變更清單，晚到結果不會被發布。

```powershell
./scripts/build.ps1 -Publish
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release --no-build -- --app-exclusions
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release --no-build -- --tray-lifecycle
```

原生整合若未取得前景會等候專用 fixture 啟用，最多 60 秒；不操作使用者文件。

### 桌面操作與界線

已在自含式發布版展開清單，從原生檔案選擇器加入本專案的 `SnapBar.App.exe`，顯示 `SnapBar.App` 及正確完整路徑。完整結束後再啟動，清單數量仍為 1；移除按鈕實際點擊後畫面及設定檔均回到 0。測試項目已清理，原保存的手動模式未變更。螢幕閱讀器的清單名稱也使用友善名稱，避免暴露 C# record 表示法。

另在檔案選擇器手動輸入既有 `README.md`，確認原生對話框確實接受非 EXE 副檔名；產品會顯示「請選擇有效的 .exe 程式檔案，排除清單未變更」，主程式持續運作、清單維持 0，沒有執行或新增該檔案。

此原生整合測試呼叫與手勢／快捷鍵相同的控制器入口，不宣稱測遍所有實體滑鼠操作或 App。權限不足而無法取得執行檔路徑時，有排除項目的自動讀取採保守停止；手動仍依原權限限制。符號連結、啟動器、不同安裝位置不保證視為同一 App；應選實際承載視窗的 EXE。Search、Open Link、AI、自訂動作與 macOS 未新增。

## 2026-10-24：系統匣與基本設定保存

使用者回報「我手動測試應該 OK」，隨後同意先完成系統匣與基本設定保存。此回報登錄為人工整體操作正常的補充，未推定每個 App／版本／異常情境均通過。設計見 [常駐設計](superpowers/specs/2026-10-24-tray-settings-design.md)。

已實作：關閉設定視窗隱藏到系統匣、圖示／選單的開啟設定與自動偵測切換、明確結束、重複啟動叫回既有視窗。自動偵測偏好保存到 `%LOCALAPPDATA%/SnapBar/settings.json`；Sublime 相容模式不持久化。損壞、無法讀取、超量或不相容版本的設定保留原檔，不自動覆寫。

### 自動檢查

Release 建置 0 errors／0 warnings。共 **128 項檢查通過**：87 項核心（新增 12 項設定檢查）、7 項常駐生命週期、11 項原生剪貼簿往返、13 項協調器競態、6 項 helper 協定／結束及 4 項深層／延遲 UIA 檢查。設定及生命週期測試使用專用暫存目錄，未改寫使用者設定。

設定檢查涵蓋缺檔預設、不含 BOM 與選取內容的 JSON、重新載入、暫停保存、損壞／null／錯誤型別、未知版本、超量、錯誤 UTF-8、檔案鎖造成替換失敗時原檔保留及暫存檔清理。生命週期檢查涵蓋關閉後仍存活、同一視窗重新開啟、控制器狀態與保存一致、新視窗工作階段載入偏好，以及明確結束後可重新註冊同一全域快捷鍵。

```powershell
./scripts/build.ps1 -Publish
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release --no-build -- --tray-lifecycle
```

### 已發布套件的桌面檢查

| 案例 | 實際結果 |
| --- | --- |
| 視窗按鈕啟用／暫停 | 畫面模式與 JSON 同步；結束並重新啟動後載入啟用狀態；測試結束已恢復手動模式 |
| 右上角關閉 | 設定視窗消失，原程序仍存活 |
| 再執行 EXE | 回到同一個 HWND／PID，第二程序結束，未新增控制器 |
| 先執行 fixture 再啟動主程式 | 兩個獨立程序正常共存；fixture 不再佔用主程式鎖 |
| 主視窗隱藏後按 Ctrl+Alt+V | 實際 fixture 的 7 字選取顯示工具列；診斷 203 ms（helper 69 ms），單次樣本 |
| 設定中的「結束 SnapBar」 | 主程式與 helper 均結束；fixture 保留至另行關閉 |
| 預設視窗大小 | 調整為 700×720，完整顯示關閉規則及結束按鈕；小視窗仍可捲動 |

深層 UIA 冷／暖單次樣本 287／147 ms；延遲 provider 649 ms、3 次查詢，暖樣本 59 ms。不作 P95 推論。

界線：本輪確認了主視窗實際點擊與程序生命週期；電腦操作工具未提供可用的工作列視窗，因此系統匣圖示的實際滑鼠左鍵／右鍵選單操作仍待人工補驗。明確結束等待交易的程式路徑已實作，但未新增「Sublime 真實讀取途中結束」端到端案例。Windows 強制終止、工作階段結束時的剪貼簿還原仍無保證。Search、Open Link、路徑、排除清單、AI、自訂動作及 macOS 未新增。

## 2026-10-23 續作：剪貼簿取消與來源關閉

接續交接文件的未驗證競態項目，新增 `tests/SnapBar.Windows.Tests/ClipboardRaceTest.cs`，直接呼叫產品的 `ClipboardSelectionReader`。資料來自專用 WPF 視窗及獨立子程序；不操作 Sublime 或使用者文件、不修改快捷鍵設定。子程序有自己的 HWND 與訊息迴圈，作為真實不同程序的剪貼簿擁有者。

新增測試重現來源 HWND 在複製後關閉時回報 `no-response`：原先等待迴圈只在剪貼簿開啟成功後檢查目標／輸入；已銷毀的 owner HWND 無法開啟剪貼簿，因此持續等到無回應。沒有複製結果時，輸入失效也可能被報為無回應。現在每次輪詢及穩定等待後先檢查輸入代次與前景身分，分別回報 `clipboard-input-changed`／`target-changed`，不繼續觸發下一次複製。

13 項新增整合測試全部通過：

| 案例 | 實際確認 |
| --- | --- |
| 正常 Unicode 選取與註冊格式 | 取得文字及能力；還原原始文字、註冊格式逐位元組及回傳還原後序號 |
| 延遲 150 ms 回應 | 同次交易成功，只送出一次複製請求 |
| 同時第二次讀取 | 回報 busy，未再次注入；第一筆正常完成 |
| 可編輯條件無回應、一般條件有回應 | 讀取成功，Editable 保持未知 |
| 完全無回應 | 原剪貼簿與序號不變 |
| 輸入失效後 100 ms 才回應 | 拒絕選取，保留晚到資料，不用舊備份覆蓋 |
| 輸入失效且沒有回應 | 回報輸入失效，原剪貼簿保留 |
| 穩定等待期間再次寫入 | 回報 clipboard-changed，保留新版本 |
| 空字串／超過 32,768 字元 | 分別拒絕並還原原剪貼簿（兩項） |
| 送出複製失敗 | 回報 input-blocked，原資料／序號保留且解除 busy |
| 真實其他程序寫入 | 回報 clipboard-other-owner，保留外部資料 |
| 來源視窗關閉 | 回報 target-changed，拒絕選取，未用舊備份覆蓋現有資料 |

每個案例後確認協調器解除 busy；整組結束後已還原測試前的桌面剪貼簿，並結束專用子程序。

本輪最終回歸：Release 建置 0 errors／0 warnings，75 項核心測試、11 項剪貼簿往返測試、6 項 helper 協定／結束檢查及 4 項 UIA 深層／延遲檢查全部通過；連同新增 13 項，共 109 項。UIA 深層 fixture 冷／暖樣本 294／81 ms；延遲 provider 同次 capture 成功，1167 ms、4 次查詢，暖樣本 59 ms；均未變更剪貼簿，不作 P95 推論。已重新發布 Windows x64 自含式套件。

```powershell
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release -- --clipboard-races
```

此命令會暫時使用真實剪貼簿；Windows 若阻止自動取得前景，請在 60 秒內啟用「SnapBar clipboard race fixture」。測試期間不要複製、切換視窗或持續按住修飾鍵。未取得前景就中止，不會改寫剪貼簿。

界線：這些是協調器的原生整合測試，不是實際浮窗按鈕或 Sublime 快捷鍵端到端測試。延遲案例只覆蓋可控制的時序，不代表剪貼簿管理器、所有晚到回應或來源不明寫入均已通過。失效後的晚到複製仍可能留在剪貼簿；取消不能撤回已送出的命令，也不保證還原。Electron 通用回退與 macOS 未新增。

## 2026-10-23 續作：ChatGPT 桌面選字間歇不支援

使用者確認 Sublime 已正常，回報 ChatGPT 桌面 App 一開始不顯示工具列、之後能取得唯讀選取。完整 SnapBar 狀態紀錄還有成功之後再次不支援的情況，因此不能將所有失敗歸因於冷啟動。未自動操作 ChatGPT 對話介面，ChatGPT 實際冷啟動首筆仍待使用者回測。

本次修正兩個原生探測弱點：

1. 原先只查詢一次；現在僅 `unsupported` 會於 120／250／500 ms 後重新查詢，最多四次、整組原生請求共用 2 秒預算。第一次不支援時讀取目標視窗的直接子節點一次，讓按需公開的 provider 有機會就緒；不掃描整個對話。新輸入／前景切換立即失效；空選取、密碼、逾時、provider 錯誤立即停止。Sublime 不增加這段等待，ChatGPT 不新增剪貼簿回退。
2. 原先只向上找 16 層祖先；32 層控制項 fixture 已重現「明明有 TextPattern 卻因尚未確認視窗歸屬而丟棄整個分支」的失敗。上限改為 64 層，保留目標歸屬與 helper 逾時防護。這是已確認的程式限制，未證明 ChatGPT 實際選取節點正好超過 16 層。

驗證：Release 建置 0 errors / 0 warnings；75 項核心測試、11 項原生剪貼簿測試、6 項 helper 協定檢查通過。新增的 4 項桌面整合檢查如下，均走實際 helper／控制器且無剪貼簿變動：

| 案例 | 結果 |
| --- | --- |
| 32 層控制項，原 16 層邊界 | 重現失敗，重試後仍 unsupported |
| 32 層控制項，64 層邊界 | 首次查詢即成功，283 ms（helper 101 ms） |
| 同一深層 provider 再次選取 | 直接成功，168 ms，無就緒等待 |
| 首次查詢後延後 600 ms 公開 TextPattern | 同次 capture 成功；樣本 648 ms／3 次、1430 ms／4 次查詢 |
| provider 就緒後再次選取 | 直接成功，56～58 ms，無重試等待 |

核心測試另涵蓋重試上限、總預算、傳給 helper 的剩餘時間、取消、前景失效、晚到結果丟棄，以及禁止對終止狀態重試。以上單次樣本不代表 P95。

可重跑：

```powershell
./scripts/build.ps1 -Publish
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release --no-build -- --uia-deep-tree
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release --no-build -- --uia-readiness
```

兩個整合驅動只開啟／關閉各自的獨立測試視窗，不修改使用者文件。設計見 `docs/superpowers/specs/2026-10-23-uia-readiness-design.md`。錯誤訊息已改為「目前未取得介面，可能尚未就緒」，成功診斷在重試時記錄查詢次數；唯讀 ChatGPT 回覆僅提供複製是預期行為。

## 2026-10-23 續作：共用回退與 Sublime 寫入

本節為最新結果；後續 2026-10-22 紀錄保留作歷史基線。

- 路由已改為 UIA 優先；有選取介面但空選取、密碼、逾時、失效不回退。只有不支援且符合已啟用 Sublime 適配器的目標才用剪貼簿。
- 共用協調器負責備份、串行、失效檢查、讀取／還原；Sublime 適配器負責條件快捷鍵與可編輯能力。
- 快捷鍵：Ctrl+Alt+Shift+F12 一般條件讀取、F11 可編輯條件讀取、F10 剪下、F9 貼上。每個鍵均有 noop 預設；寫入要求單段非空、非面板／覆蓋介面及 `read_only not_equal true`。
- Build 4180 的 `read_only equal false` 實測落到 noop；改用明確 `not_equal true` 後，可編輯與唯讀正反案例均通過。未推定其他版本的表現。
- 完整符合舊版 F12 綁定的設定可升級，保留原文並備份；新鍵位、別名／修飾鍵不同順序衝突及遭修改的防護條件均拒絕。
- 自動驗證：56 項核心測試、11 項 Win32 剪貼簿測試、6 項 helper 協定／結束檢查通過；Release 建置 0 errors / 0 warnings。
- 最終自含式發布版的原生 UIA fixture 回歸通過：可編輯 7 字時三個按鈕可用、唯讀 7 字時僅 Copy 可用、密碼欄位明確拒絕讀取。UIA 冷啟動樣本 712 ms（helper 69 ms），暖啟動樣本 60 ms（helper 4 ms）；不作 P95 推論。

### 本機 Sublime Build 4180 實測

僅操作 `tests/fixtures/sublime-actions.txt`；原有使用者文件未儲存、未關閉。診斷使用內建 console 查詢／暫設 fixture 的唯讀狀態，未安裝外掛；已恢復可編輯並關閉暫時輸入／命令記錄。

| 案例 | 結果與證據 |
| --- | --- |
| 手動讀取／可編輯判定 | 9 字 `selection`；工具列三個按鈕可用；探測後原剪貼簿哨兵保留 |
| 自動雙擊／拖曳 | 9 字／20 字選取均顯示可編輯工具列 |
| 空選取 | 等候完整探測後無工具列，文件完整 |
| 複製 | 整合驅動呼叫工具列相同的控制器入口，重新驗證後剪貼簿為 `selection` |
| 剪下 | 同一控制器路徑，剪貼簿為 `selection`；畫面核對第 2 行由 `LEFT selection RIGHT` 變成 `LEFT  RIGHT`，其他行完整 |
| 貼上 | 同一控制器路徑，重新探測前的 `replacement` 保留；畫面核對只變成 `LEFT replacement RIGHT`，其他行完整 |
| 唯讀 | console 確認 `view.is_read_only()` 為 True；F11 不複製，F10／F9 不修改文件，剪貼簿哨兵保留；之後恢復 False |
| 多游標 | 2 selection regions，F10／F9 不修改文件，剪貼簿哨兵保留 |
| 搜尋面板焦點 | F10／F9 未修改搜尋字串或文件，剪貼簿哨兵保留 |
| 移到相同文字的另一範圍 | 整合驅動等待期間，以滑鼠把第 2 行選取移至第 4 行 `selection`；原剪下被失效代次拒絕，文件與預期剪貼簿保留 |
| 動作前切換視窗 | 整合驅動等待期間切到主視窗，原貼上被拒絕；返回 fixture 核對文件完整 |

測試修改均以 Undo 還原，fixture 沒有未儲存修改。整合驅動在讀取前備份原剪貼簿；Copy／Cut 驗證其輸出後才還原測試前內容，Paste 驗證模式需人工核對緩衝區後發出檔案訊號才允許還原，以免測試清理搶在 App 消費貼上資料前發生。產品剪下流程本身不還原剪下結果。

整合驅動是 `tests/SnapBar.Windows.Tests/SublimeActionTest.cs`，呼叫和按鈕相同的 `CaptureAsync`／`ExecuteAsync`。**未自動點擊實際浮窗按鈕**：Computer Use 沒有把不搶焦點的浮窗列為獨立可操作視窗，因此按鈕點擊端到端仍需人工補驗。

可重跑命令（先在 Sublime 開啟上述 fixture、選取單段 `selection`，並啟用目前設定；暫停另一份 SnapBar 的自動偵測）：

```powershell
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release -- --sublime-action Copy
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release -- --sublime-action Cut
# Paste 必須使用新的訊號檔；驅動顯示 READY 後核對文件，再建立該檔以允許清理。
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release -- --sublime-action Paste --after-action-file D:/codex/snapbar/artifacts/paste-verified.signal
```

每個寫入案例後先復原 fixture；驅動拒絕其他檔名／程序，並只接受 `selection` 的可編輯快照。移動選取／切換視窗測試可加 `--expect-rejected --continue-file <尚不存在的訊號檔>`，READY 後改變目標，再建立訊號檔。

### 效能與驗證界線

本輪冷啟動控制器讀取樣本 386、334、309、325、324 ms；主程式自動雙擊 359 ms、拖曳 200 ms。每次整合驅動都新建 helper，不能把這些當作暖啟動或 P95。唯讀需等待可編輯探測無回應後才進行一般讀取；空選取最多兩個 800 ms 等候窗口，另有 UIA／排程成本。

仍需補驗：實際按鈕點擊、修飾鍵持續按住、讀取中關閉來源、極慢／晚到複製、剪貼簿管理器在探測中競爭、Chrome／Edge／記事本新版逐動作回歸、Word／VS Code／Obsidian 能力矩陣、混合 DPI、多螢幕及長時間效能。注入前序號變更已有原生測試，但不等於所有並行寫入競態通過。

相同內容被背景程式改到另一範圍且未產生監聽到的輸入，仍無法識別。無擁有者剪貼簿寫入不能證明來源；檢查與指令處理間仍有競態。還原目前剪貼簿不清除歷史／同步資料。Electron 未新增通用回退；macOS 本輪未修改。

---

日期：2026-10-22。結論：**Sublime 已實測手動、雙擊與拖曳顯示工具列；空選取不顯示，讀取後原剪貼簿文字可貼出。使用者先前回報 Chrome、Edge、記事本實測正常。** 完整動作覆蓋、異常情境與多螢幕驗收仍依下方紀錄追蹤。

## 使用者實測回報

2026-10-22，使用者回報：「chrome,edge,記事本測試正常」。三個 App 均登錄為使用者實測正常，作為跨 App 相容性的人工測試證據。

回報未列 App 版本、測試控制項及各動作的個別結果，因此不額外推定所有版本、所有控制項或每個異常情境皆通過。先前工具中止的紀錄僅代表自動化測試限制，不影響本次使用者回報。

## 環境與建置

- Windows x64；系統登錄顯示版本 25H2，組建 26200.9711。
- 專案 SDK：10.0.402，位於 `.tools/dotnet`；系統原本只有 .NET 6 runtime。
- Release build：0 errors、0 warnings。
- x64 自含式發布：`artifacts/p0`；UIA helper 位於 `Probe/`。
- 沒有 Git repository，未建立 commit。

## 自動驗證

Sublime 修正版本：Release build 0 errors / 0 warnings，36 項核心測試、7 項原生剪貼簿整合測試與 6 項 helper 協定／結束檢查通過。核心測試涵蓋快捷鍵設定及來源判定；原生測試實際呼叫 Win32 備份、覆寫、還原，逐位元組或像素比對。

| 項目 | 狀態 | 證據／涵蓋範圍 |
| --- | --- | --- |
| 核心政策、位置與 Sublime 設定測試 | 36 項全部通過 | 原有 21 項、8 項設定測試與 7 項來源／競態判斷回歸 |
| Win32 剪貼簿往返測試 | 7 項全部通過 | Unicode、複合格式、零值 metadata、檔案清單、GDI bitmap、超過 4 MiB 的 DIB、空剪貼簿 |
| helper ping | 通過 | 收到 ok，無選取資料 |
| 不支援操作／錯誤 JSON | 通過 | 回傳 invalid-request，不終止程序 |
| 無效目標 HWND | 通過 | 回傳 target-changed，不讀取文字 |
| 過期 session | 通過 | 回傳 expired |
| stdin EOF | 通過 | helper 正常退出，exit code 0 |

最初用 PowerShell pipeline 作 smoke test 時，第一行的 BOM 導致 JSON 解析失敗。已允許行首 BOM，並改以明確 UTF-8 無 BOM 的程序管線執行可重現測試；這不影響 App 本來使用的無 BOM 管線。

## Windows UI 實測

使用 computer-use 技能操作本專案獨立 fixture。所有文字都是專門建立的測試資料。

| 案例 | 結果 | 觀察 |
| --- | --- | --- |
| 自含式 App 啟動 | 通過 | 控制視窗與快捷鍵初始化完成 |
| Ctrl+Alt+V 讀取可編輯欄位 | 通過 | 取得選取的 7 字；可編輯狀態正確 |
| 顯示動作列 | 通過 | 浮窗出現在選取文字附近，來源選取仍保留 |
| 切換至主視窗 | 通過 | 浮窗在前景檢查週期後關閉 |
| 自動雙擊唯讀文字 | 通過 | 取得 7 字，顯示唯讀，剪下／貼上呈停用 |
| 密碼欄位快捷鍵 | 通過 | 主視窗回報「密碼欄位不提供選字動作」 |
| Copy／Cut／Paste 按鈕端到端 | 自動化未完成；已有使用者整體正常回報 | 工具未將不搶焦點的動作列列為可操作 target；使用者未分別列出三種動作的結果 |
| Chrome | 使用者實測正常 | 2026-10-22 人工回報；先前自動化因工具無法可靠確認目前瀏覽器 URL 而中止，App 版本與控制項尚未記錄 |
| Edge | 使用者實測正常 | 2026-10-22 人工回報；App 版本與控制項尚未記錄 |
| 記事本 | 使用者實測正常 | 2026-10-22 人工回報；App 版本與控制項尚未記錄 |
| Word／VS Code | 未驗證 | 仍需逐項測試與記錄 App 版本 |
| 混合 DPI／多螢幕實體環境 | 未驗證 | 目前只驗證純邏輯邊界計算 |
| 卡住的第三方 UIA provider | 未驗證 | 已實作 helper 逾時回收，但尚未實際注入 provider 卡死案例 |
| 8 小時常駐與效能 P95 | 未驗證 | 不以單次量測推論長時間穩定性 |

自動化 UI 測試後的小幅修正包含更保守的非焦點 provider 寫入條件及通訊 BOM 容忍。最終版本已重新建置與執行自動測試，之後收到上述三個 App 的使用者正常回報；完整 UI 回歸仍未全部記錄。

## Sublime 問題定位與修正驗收

- 本機 Sublime Text Build 4180，在獨立開啟的 `tests/fixtures/selection.txt` 選取 Windows 七個字元，畫面顯示選取，但 UIA 樹沒有編輯器文字或 selected_text。
- 使用者選擇不裝外掛的有條件剪貼簿模式。新增明確啟用開關、內建 copy 的條件快捷鍵設定、格式備份／還原、空選取與多游標限制。僅開放 Copy。
- 上一輪因 Esc 中止而未完成實機驗證。使用者要求繼續修正後，本輪恢復測試並成功重現失敗。
- 根因一：Sublime 複製成功後 GetClipboardOwner() 回傳 NULL，舊版誤判成其他程序寫入。診斷版畫面確認「剪貼簿已更新，但 Windows 沒有回報擁有者視窗」。新版接受無擁有者情況，但仍檢查序號穩定、前景身分與輸入代次。
- 根因二：舊版拒絕圖片格式且備份上限 4 MiB。新版用原生格式對應的複製／釋放方式保存圖片、檔案清單等，備份上限 64 MiB，錯誤訊息區分過大、格式不支援、來源無資料、輸入變更與其他程序擁有者。
- 已關閉 Downloads 舊版，改執行 `D:/codex/snapbar/artifacts/p0/SnapBar.App.exe`，啟用相容模式及自動偵測。保留使用者原始 Sublime 文件，只在獨立 fixture 視窗操作。
- 手動 Ctrl+Alt+V：Windows 單字 7 字，工具列實際顯示。
- 自動雙擊：selection 單字 9 字，工具列實際顯示，選取仍保留。
- 拖曳：SnapBar Windows 共 15 字，工具列實際顯示。
- 剪貼簿還原：先前剪貼簿是 Windows，偵測 selection 後移到 fixture 空行貼上，結果仍為 Windows，而非暫存的 selection。
- 空選取：在有文字的行尾只有游標時按 Ctrl+Alt+V，等待後不顯示工具列，沒有誤觸整行選字。
- 工具列上 Copy 可用、Cut/Paste 停用。Copy 按鈕端到端尚未由電腦操作工具驗證：不搶焦點浮窗未列入工具可操作視窗，不能把顯示成功當成按鈕端到端成功。
- 尚待實機驗收：多游標與搜尋面板條件、並行剪貼簿管理器、極慢回應、讀取中切換／關閉。核心政策測試已涵蓋已知其他擁有者、序號再次變動、焦點及輸入失效，但不等同所有實際競態都已覆蓋。
- Chrome、Edge、記事本的正常回報來自修正前版本；本次只完成建置與自動邏輯回歸，未再操作三個 App。

## 原 UIA 路徑已觀察的延遲

- 冷啟動手動讀取：主程式 961ms，其中 UIA 519ms；超過原規劃的 350ms 目標。
- helper 暖啟動後的自動雙擊：主程式 144ms，其中 UIA 8ms，包含約 120ms 的 debounce。
- 以上均為單次觀察，不是 P95、平均或所有 App 的保證。需加入多樣本量測，並評估啟動時預熱 helper 的成本。

## 人工驗收步驟

只使用 fixture 內的可丟棄文字，避免把尚未驗證的剪下／貼上套用到重要文件。

1. 啟動主程式，開啟測試視窗；保持自動偵測關閉。
2. 選取 `SnapBar`，按 Ctrl+Alt+V，點「複製」。在另外一個空白測試欄位按 Ctrl+V，確認只有 `SnapBar`。
3. 回到 fixture 選取 `SnapBar`，按 Ctrl+Alt+V，點「剪下」。確認只刪除該字，周圍文字完整；檢查剪貼簿後以 Ctrl+Z 復原測試文字。
4. 先複製固定測試字串，再選取 fixture 的一個字，叫出動作列並點「貼上」。確認只取代選取範圍。
5. 選取後切換視窗、移動選取或關閉來源視窗，確認舊動作列消失且不發送到其他 App。
6. 選取唯讀文字與密碼欄位，確認前者不提供寫回、後者不讀取。
7. 分別將文字、圖片、檔案清單放在剪貼簿，只選字叫出動作列而不點動作；確認剪貼簿不改變。
8. 啟用自動偵測，測試拖曳、雙擊、三擊、一般單擊、捲動與按鍵關閉；停用後確認只剩快捷鍵觸發。
9. 在 Chrome／Edge 本機 HTML fixture、記事本文字 fixture 中重複以上步驟，依讀取、定位、Copy、Cut、Paste 分欄記錄，不把讀取成功視為整個 App 支援。

## 後續驗證項目

- Chrome、Edge、記事本已有使用者實測正常回報；後續補齊 App 版本、控制項及 Copy／Cut／Paste 的逐項結果。
- 補上原目標變動與 UIA provider 卡住的整合測試證據。
- 量測冷啟動及暖啟動的多樣本延遲，決定是否需要預熱。
- 補實際多螢幕／混合 DPI 驗證；目前不發布「所有 App 都支援」的說明。

這份紀錄保留實際限制，沒有把 UI 工具的中止推定為 App 本身相容或不相容。

## 2026-10-31 UI 精修驗證

使用者已同意「安靜的 Windows 工具」方向。共用 WPF 樣式統一中性灰白、藍色主操作、中文字體、圓角與控制項狀態；設定改為分區卡片及自動偵測開關，紀錄收進診斷；動作管理改為緊湊預覽；編輯器保留雙欄並固定儲存頁尾。AI panel 不在此次實作範圍。

| 驗證 | 結果 | 證據 |
| --- | --- | --- |
| Release 建置／自含式發佈 | 通過 | `scripts/build.ps1 -Publish`，0 warnings、0 errors；輸出 `artifacts/p0` |
| Core runner | 275 通過 | 沿用選取、寫入、設定與動作政策檢查 |
| `--action-layout` | 13 通過 | 六個釘選上限、排序、預覽與更多選單 |
| `--action-diagnostics` | 25 通過 | 完整診斷、唯讀詳情、編輯器失敗及重試 |
| `--tray-lifecycle` | 15 通過 | 自動偵測保存、關閉隱藏、重開、退出與快捷鍵釋放 |
| `--menu-keyboard` | 21 通過 | 原生輸入游標、Unicode 貼上、鍵盤捲動、焦點返回與不搶其他 App 焦點；結束還原剪貼簿 |
| `--custom-actions` | 54 通過 | 使用可丟棄 fixture 驗證執行、結果、失效取消及原選取替換；結束還原剪貼簿 |
| `--ui-appearance` | 8 通過 | 最小視窗捲動、固定儲存頁尾、類型對應按鈕、執行中停止及取消後復原 |
| Probe protocol smoke | 6 通過 | ok、invalid-request、target-changed、expired、EOF |
| 原生下拉選單 | 通過 | computer-use 實際開啟類型選單並切換至 PowerShell，確認表單及測試按鈕更新 |
| 實體混合 DPI／第三方 App 全套重驗 | 未執行 | 本輪不以 WPF 截圖或 fixture 推論實體多螢幕及所有 App 相容性 |

測試計數共 411 項，另有 6 項 Probe smoke。`--ui-preview` 可開啟使用暫存設定的可丟棄預覽，關閉編輯器後結束。

截圖位於忽略的 `artifacts/ui-polish/`：`settings.png`、`actions.png`、`actions-light.png`、`editor.png`、`editor-url.png`、`toolbar-dark.png`、`toolbar-light.png`、`more-dark.png`、`more-light.png`，以及三個小視窗版本。截圖由實際 WPF 視覺樹輸出，非設計示意圖。

## 2026-10-31 第二輪：密度與測試結果

依使用者對實際畫面的確認，縮短搜尋／診斷的收合列、動作列預覽及清單間距，清單列最小高度 56 DIP。移除可操作按鈕的淡化，保留真正停用狀態。主設定視窗高度由 850 改為 760 DIP；正常紀錄移進診斷，設定錯誤與初始化問題仍提供可見回饋。

編輯器使用較小的待測試區、提高測試按鈕對比。成功後保留精確原始輸出，另顯示依 Unicode 文字元素計算的輸入／輸出字元數及內容是否相同。可展開並排空白比較，空格、Tab、CR、LF 僅在比較區轉成可見符號，沒有改寫實際範例或輸出。執行中修改任何相關欄位後，舊結果不會成為新內容的測試結果。新腳本預設終端機圖示，已保存及明確選擇的圖示保留。

| 驗證 | 結果 |
| --- | --- |
| Core | 275 通過 |
| UI appearance | 20 通過，含原始 Unicode／空白保留、相同內容、空輸出、編輯失效、圖示預設與使用者選擇保留 |
| Action layout | 13 通過 |
| Action diagnostics | 25 通過 |
| Tray lifecycle | 15 通過 |
| Release／自含式發佈 | 通過，最終建置 0 warnings、0 errors |
| Probe protocol | 6 通過 |
| 原生新版啟動 | computer-use 確認已保存的自動偵測恢復，頂部顯示自動偵測中，設定頁正常顯示 |

本輪共 348 個相關檢查，另有 6 個 Probe smoke；未重複未變更的 keyboard-menu／custom-actions runner。預覽程序曾造成一次 DLL 複製鎖定，正常關閉後重建成功。已先確認舊版沒有開啟的動作編輯器，再透過「結束 SnapBar」正常退出並更新。

`artifacts/ui-polish/` 的截圖已更新，新增 `editor-comparison.png`，並使用六個釘選及兩個自訂動作的可丟棄資料產生動作管理畫面。一般及最小視窗均已檢視；實體混合 DPI 與第三方 App 全套重驗仍屬未執行項目。重啟後 Sublime 相容模式依既有設計需使用者再次明確勾選。

## 2026-10-31 收斂：展開可見性與工具列實測

- 進階設定在 WPF layout 完成後，把第一個欄位連同標籤捲入視野；只做 BringIntoView，不呼叫 Focus。排程執行前確認仍展開且可見，避免收合或關閉後捲動。最小 880 × 640 視窗內已確認欄位完整可見，截圖 `artifacts/ui-polish/editor-advanced-small.png`。
- 空的隱藏群組不顯示；移入動作後重新顯示並標示正確數量。
- 透過獨立程序 WPF fixture 的真實 UIA 選取，確認工具列位於工作區內且沒有遮住選取行。
- 真實滑鼠移入複製按鈕，確認 hover 高亮、浮窗仍開啟、前景仍屬於來源程序。截圖 `toolbar-native.png`／`toolbar-native-hover.png` 來自此實際工作階段。
- 透過原生滑鼠按下／放開點擊 Copy，確認剪貼簿等於 fixture 原選取內容、浮窗關閉且來源前景保留。測試在第一次 Copy 前備份剪貼簿，於完成後還原。
- 真實來源捲動會關閉浮窗並使工作階段失效。既有 keyboard／custom-action runner 另確認 Esc、Alt+F4、搜尋焦點返回及切換其他視窗後不搶回焦點。

本輪 Core 275、UI appearance 23、action layout 13、action diagnostics 25、custom actions 61、menu keyboard 21，共 418 項通過；自含式發佈另通過 6 項 Probe smoke。最終 Release 建置零警告、零錯誤。原生 hover／Copy 驗證以可丟棄 WPF fixture 為範圍，不能推論所有第三方 App 或實體混合 DPI 均已驗證。

## 2026-10-31 情境設定與動作查找

- 結果說明依六種輸出模式更新；短暫提示不再顯示替換原文的說明。測試不套用結果的契約保留。
- 管理動作新增動作列／更多分頁，隱藏分頁僅在有項目時出現，預覽不隨分頁切換。切換不保存設定；位置調整、六項上限與排序仍通過既有驗證。`actions-more.png` 顯示可直接到達自訂動作的檢視。
- 搜尋提供 Google、Bing、自訂；只有自訂顯示範本欄位，切換後仍需明確儲存。測試確認未保存的自訂草稿保留，Bing／自訂網址能保存、讀回與重設。原生已開啟選單確認三個選項可用，未修改使用者的實際搜尋設定。
- 腳本提供預設勾選的使用選取文字選項。關閉後隱藏範例與比較、停用插入變數，編輯器及正式控制器都傳空字串；執行器亦確保 POPCLIP_TEXT 為空。獨立 fixture 驗證關閉輸入仍不能繞過唯讀來源的替換拒絕。
- 設定 schema 升為 5，動作檔 envelope 升為 2；讀取舊設定和舊動作時缺少旗標預設 true，保留既有行為。舊設定僅在記憶體遷移，正常明確保存時才寫入新版。版本 1 動作檔若含 false 旗標會拒絕，以避免降級語意。網址動作不能停用輸入。

| 驗證 | 通過數 |
| --- | --- |
| Core（含遷移、預設值、版本與設定往返） | 282 |
| UI appearance（含分頁、搜尋、六種說明、輸入關閉與儲存） | 40 |
| Action layout | 13 |
| Action diagnostics | 25 |
| 原生 PowerShell／Node runner | 20 |
| Custom action 整合（含原生滑鼠與 no-input 分派） | 65 |
| Tray lifecycle | 15 |

本輪共 460 項，另有 6 項 Probe smoke；最終 Release 與自含式發佈零警告、零錯誤。已重新啟動 `artifacts/p0/SnapBar.App.exe`，原有自動偵測載入正常，並用 computer-use 點擊更多分頁確認自訂動作可直接到達；沒有自動改寫既有動作的輸入選項。實體混合 DPI／第三方 App 全套重驗仍未執行。

## 2026-10-31 編輯頁與動作清單收尾

- 編輯區預設由 184 改為 120 DIP，提供可見的調整把手；滑鼠拖曳與上下鍵限制在 96–480 DIP，Home 還原預設。自動化確認調整不修改腳本與選取範圍，原生滑鼠亦確認高度確實改變。
- 收斂表單間距，保留字級與按鈕大小。1080 × 850、1024 × 800 視窗的基本設定與進階入口完整可見；880 × 640 仍可捲動，儲存列固定，展開進階後第一個欄位可見。已檢視 `artifacts/ui-polish/editor.png`、`editor-compact.png`、`editor-small.png` 與 `editor-advanced-small.png`。
- 清單移除重複群組標題與內建動作副標，自訂動作保留類型。每個釘選動作提供六點把手，名稱仍可拖曳，上下移按鈕保留。排序把手及名稱使用手形游標；上下雙箭頭僅用於編輯框的高度調整。滑鼠捕捉維持至系統拖曳門檻，避免快速跨列時遺失起點；放在原位置不保存。
- 使用 computer-use 在可丟棄的 UI preview 實際把複製從第一列拖到第三列，清單及預覽同步成為剪下、貼上、複製。此操作只改暫存資料，未改使用者的動作順序。既有 layout runner 另確認箭頭排序、設定保存與重新開啟後順序、六項上限及情境篩選。

Core 282、UI appearance 58、action layout 13、action diagnostics 25，共 378 項通過；發佈另通過 6 項 Probe smoke。Release 建置零警告、零錯誤。原生拖曳及調整高度已實測；實體混合 DPI 與第三方 App 全套重驗未在本輪執行。

## 2026-10-31 自動選字相容讀取與偏好保存

本節取代先前「Sublime 每次啟動需重新勾選」的產品規則。使用者已同意實作預設自動相容與偏好保存。

- 新增預設啟用的「選字相容模式」，關閉亦保存；Sublime 支援改為持久偏好。舊版有效的受保護快捷鍵綁定用來恢復過去已啟用的選擇，明確 false 不會因綁定仍存在而重開。schema 6 支援舊版 1–5 的記憶體遷移，錯誤／未來版本保留原檔並停用相容讀取。
- UIA 仍優先，延遲 provider 重試與 64 層祖先界線保留。只有 unsupported 且 Probe 可驗證非密碼的文字焦點、祖先歸屬與啟用狀態，才建立候選 token；明確空選取、逾時及其他失敗不回退。自動觸發還要求滑鼠位置位於焦點控制項內。候選 30 秒有效；Ctrl+C 前及讀取後確認原焦點 runtime ID、前景及程序身分。終端機／主控台排除一般 Ctrl+C 回退。
- 通用讀取重用原生剪貼簿備份、格式保存、串行與擁有者／序號穩定檢查。讀取後還原原內容，再提供複製、搜尋／開啟或不寫入原文的腳本。通用快照的可編輯狀態未知，Core 另明確拒絕通用來源的寫入，即使將來錯誤帶有 editable 旗標亦不能授權。Sublime 仍走原有條件快捷鍵適配器。
- 拒絕 MSDEVLineSelect、Visual Studio 整行標記、直接 VS Code metadata 的空選取旗標；亦解析 Chromium Web Custom MIME Data Format 的對齊 UTF-16 Pickle 封裝。截斷、過大、重複或不明編輯器資料拒絕。已知 VS Code 衍生程式需要明確中繼資料。
- 獨立 WPF 子程序刻意不公開 TextPattern，經真正 Ctrl+C 驗證 Unicode、原剪貼簿還原、Copy／Search、腳本輸入、禁止 Apply／Replace／Cut、同視窗焦點變更、不同選取內容、空選取、三類整行標記、Chromium 封裝、密碼與非文字焦點。此為通用讀取協定驗證，不能等同全部 Electron App 實測。
- 以 computer-use 實際關閉並再啟用模式，確認 schema 6 保存對應布林值；正常結束後重新啟動，模式與 Sublime 支援均自動恢復。Sublime keymap 的 SHA-256 在前後一致，確認啟動及恢復未改寫快捷鍵。現行發佈版已啟動，原有自動偵測維持開啟。

| 驗證 | 通過數 |
| --- | --- |
| Core（遷移、偏好、寫入政策與中繼資料） | 292 |
| UI appearance（含錯誤設定停用與新介面） | 60 |
| Tray lifecycle（偏好保存／重啟／快捷鍵釋放） | 20 |
| Action layout | 13 |
| Clipboard roundtrip | 13 |
| Clipboard races | 17 |
| Generic clipboard compatibility | 20 |
| Native UIA readiness／deep tree | 4 |
| Custom action 整合與原生滑鼠 | 65 |

共 504 項通過，另有 6 項 Probe smoke；最終 Release 建置與發佈零警告、零錯誤。第一次 lifecycle 回歸因既有 SnapBar 占用快捷鍵失敗，正常結束舊版後通過。測試 fixture 需前景，透過 computer-use 啟用；原生 readiness 的 45 秒等待僅供取得前景，未放寬實際選字讀取預算。截圖更新於 `artifacts/ui-polish/settings.png`。

範圍限制：未逐一測試真實 Electron App、實體混合 DPI 或全套 Sublime 寫入。未知 App 可能自行改寫複製行為，沒有完整可移植的選取範圍證明；內容相同的不同範圍不能以重複複製區分。剪貼簿歷程可能觀察到暫存複製，已在設定頁說明；失效後晚到的複製仍依既有競態規則保留不確定的新內容，不宣稱還原具有原子性。

## 2026-10-31 Review 修正：UIA 開關與長時間腳本

- 相容與 Sublime 開關改由 Checked／Unchecked 同步，實際呼叫 IToggleProvider.Toggle() 可更新控制器及保存偏好。初始化先設定值再訂閱，程式化同步／安裝失敗回復有重入保護。Sublime 安裝與就緒檢查可注入；測試不修改使用者 keymap，並驗證初始載入與有效綁定重新啟用時不執行安裝。
- 一般候選仍預設 30 秒，僅通用來源的腳本在初始來源及文字比對通過後，申請逾時秒數加 10 秒的期限（最多 310 秒）。延長請求先驗證尚未過期的 token、焦點、密碼與來源；腳本後仍重新複製比對。finally 釋放期限，清理僅縮短且不復活過期或替換的候選。通用來源仍無寫入權限。
- 獨立不公開 TextPattern 的 WPF 來源，經完整 SelectionController／Probe 流程，以注入 runner 實際等待 35 秒（動作逾時設為 60 秒），確認原本 30 秒後仍顯示精確結果並還原剪貼簿。另確認腳本執行中程式化切換同視窗焦點，延長資格仍無法繞過最終來源驗證。
- 擴充 Probe smoke：過期 token 無法延長、超出 1–310 秒範圍拒絕、未知候選無法釋放；原協定與 EOF 關閉案例保留。

本輪 Core 292、tray lifecycle 28、generic compatibility 22、UI appearance 60、action diagnostics 25，共 427 項通過；另有 10 項 Probe smoke。最終 Release 建置與自含式發佈零警告、零錯誤。舊版已正常結束，修正版重新啟動。此次未重跑前節的全部跨 App、原生滑鼠或實體 DPI 驗證，不以測試合計取代這些尚存的覆蓋界線。

## 2026-10-31 開發檢查與發佈護欄

- 新增共同 check、CI workflow 與本 checkout 的 pre-commit hook。完整檢查已通過 UTF-8 無 BOM、PowerShell 語法、差異空白、12 項工具契約與 298 項 Core 測試；最後建置零警告、零錯誤。另通過 17 項 PathWorker 與 10 項 Probe 協定 smoke。
- Core 使用注入的 TimeProvider 測試 30 秒精確邊界、延長上限、不縮短延長期限、清理縮短、不復活及候選替換。一般相容性 suite 略過真正 35 秒等待；慢速 suite 保留完整流程。
- 實測系統匣無視窗的舊 App／Probe 會在發佈建置前被攔截，套件 DLL 雜湊不變；缺少桌面報告亦被攔截。工具契約涵蓋報告過期、失敗、缺必要 suite、非法成功值、路徑越界、切換失敗回復舊目錄，以及成功切換保留備份。另一個桌面 runner 持有互斥鎖時，新執行在建置及剪貼簿操作前失敗。
- 使用獨立 Git index 驗證 staged BOM／語法錯誤即使工作目錄已修好仍被拒絕，正確 staged 內容可通過；沒有改變使用者實際 index。安裝後另以 `git hook run pre-commit` 確認 hook 可執行。
- 本輪曾分別通過 lifecycle 28、appearance 60，以及含真實 35 秒等待的 compatibility 22。後續整合重跑多次觀察到外部 click 或焦點變動，按既有安全規則取消。測試另加 fixture 前景檢查，拒絕對其他 App 發起新 capture；診斷只記錄輸入種類與布林狀態。最終原始碼尚無完整且一致的 Release 桌面報告，因此本輪未更新 `artifacts/p0`，未以較早版本的成功報告繞過新門檻。

最後快速檢查日誌：`artifacts/checks/final-check.log`；獨立 PathWorker：`artifacts/checks/independent/20261031T134035013Z-c4a7f9a125db4a49adfe906e23d04137/PathWorker.log`；Probe：`artifacts/checks/independent/20261031T134231078Z-cb1cffd5274f4b40941ec1aa22e68bf1/ProbeSmoke.log`。測試入口與重跑方式見 `docs/development.md`。GitHub Actions 尚未在遠端執行；真實跨 App／實體 DPI 沒有新增覆蓋。

使用者隨後同意暫停鍵盤滑鼠；最終同一份原始碼的 Release 桌面回歸已全部通過：lifecycle 28、appearance 60、compatibility slow 22（含真實 35 秒等待），另有前置檢查 1 項。原始碼指紋在執行前後一致，完整報告為 `artifacts/checks/desktop/20261031T134518370Z-4ab6e2f624d94879afedaf1966241b4c/report.json`。此結果取代上段尚未取得最終報告的待驗證狀態。

接續使用該份報告完成自含式 staging 發佈，Core 298 與 Probe smoke 10 再次通過，切換前重新確認原始碼指紋與程序狀態。成功更新 `artifacts/p0`；前一套件保留於 `artifacts/publish/f78cd4a213c848669498e47f53625ab9/previous`，確認新舊 Core DLL 雜湊不同且舊套件完整存在。發佈報告為 `artifacts/checks/build/20261031T134654585Z-5b0117aece944f3aabc508f69d61b1e2/report.json`。新版正常啟動，自動偵測、選字相容模式與 Sublime 支援維持既有開啟偏好，設定視窗已關閉並回到系統匣待命。本節最終狀態為已驗證並發佈；遠端 CI、真實跨 App／實體 DPI 的覆蓋限制仍維持上述說明。

## 2026-11-01 AI 改寫面板

- 新增「AI 改寫」內建動作，預設位於更多選單。面板開啟先顯示原文，原文與結果共用閱讀區；同一輸入框支援全指令搜尋與直接下指令，分類只限制空白時的瀏覽。支援釘選、最近使用、指令管理、接續版本與從原文重做。
- 執行中編輯的新要求及手動切換的閱讀分頁不被完成事件覆蓋。停止、來源失效及關閉會取消請求；不合作的晚到結果亦不能回填。長指令以單行省略顯示，完整內容在面板所屬的可捲動視窗閱讀，避免擠掉結果區。
- 真實 HTTP client 支援 OpenAI Responses 與自訂 Chat Completions 網址，具逾時、取消、回應大小限制及固定錯誤訊息。AI 設定獨立保存；API Key 依服務、協定及網址以 CurrentUser DPAPI 保護。原文、自由指令、結果與最多 20 版歷史只留於記憶體，不寫入設定或記錄。
- 結果僅以本機安全 Markdown 排版，複製與替換使用原始結果文字。替換前返回原來源並重驗前景、程序、焦點、選取身分、範圍、文字與寫入權限；沿用既有剪貼簿序號與貼上防護。唯讀或來源失效保留結果及複製功能。

`scripts/check.ps1` 通過 12 項工具契約、389 項 Core 測試，Release 建置零警告、零錯誤。同一份原始碼的完整桌面回歸通過 Lifecycle 28、Appearance 60、CompatibilitySlow 22（含實際 35 秒腳本）、AiRuntime 35、AiPanel 24、Layout 22、Keyboard 21、Actions 66 及 Preflight 1。報告：`artifacts/checks/desktop/20261031T235510121Z-105607034a174e93bcbedc4635200722/report.json`。既有原生滾輪案例曾間歇未收到輸入；最終完整重跑在移除診斷訂閱後通過，未改用直接呼叫取消來替代真實滾輪。

AI runtime 使用注入 HTTP handler 驗證請求、錯誤、取消及邊界；原生 panel/controller 使用受控 client 驗證成功、失敗、連續改寫、過期完成、唯讀、來源變更及實際原選取替換，測試後還原剪貼簿。已目視核對 `artifacts/ai-panel/original.png`、`editable-result.png`、`small.png`、`service-settings.png` 及 `long-instruction.png`；獨立規格與品質審查通過。

本輪未填入真實服務金鑰、未呼叫付費模型；服務與模型相容性需使用實際設定確認。IME 測試涵蓋組字中 Enter 不提交的模擬事件，尚未等同實體繁中輸入法驗收；實體混合 DPI 與第三方 App 全套驗證仍未新增。

使用同一份桌面報告完成自含式 staging 發佈，Core 389 與 Probe smoke 10 再次通過；`artifacts/p0` 已更新，舊套件保留於 `artifacts/publish/4f3220d17447405d8378f9f610b34dcd/previous`。發佈報告：`artifacts/checks/build/20261031T235652632Z-6ea95c9c0bb34334a15a5d51bf82633c/report.json`。以 computer-use 啟動新版，確認原有自動偵測、選字相容模式及 Sublime 支援恢復；點擊「設定 AI 服務」正常開啟服務、網址、模型及金鑰表單，未儲存測試值。AI 設定視窗保留開啟供使用者填入。

## 2026-11-01 AI panel 視覺整合

- 依使用者確認的方向，統一 24 DIP 外側留白、標題與淺灰指令區／白色閱讀卡片。原文及結果分頁沿用既有動作管理的選取樣式；正文增為 15 DIP、Markdown 行高 24 DIP、閱讀內距 20 DIP。透明閱讀背景保留卡片圓角。
- 指令名稱維持直接顯示；釘選與選項改用共用線條圖示，滑鼠移入或鍵盤聚焦該列時顯示管理按鈕，已釘選的狀態另有小圖示。按鈕保留鍵盤焦點與原位置，開啟選單後仍顯示選項入口。服務入口縮成供應商名稱與設定圖示，完整模型名稱保留在提示及設定內。
- 移除重複的結果標題，版本、基準與指令整合成一條有界資訊列；完整指令仍可開啟所屬的捲動視窗。來源狀態與操作並列；唯讀／失效來源的結果複製採藍底白字，可替換時保留「替換原文」為主按鈕。AI 請求、session、控制器與選取安全流程未修改。
- 目視核對一般、唯讀、可替換、最小尺寸及長指令畫面（`artifacts/ai-panel/`）。截圖改為直接繪製視窗 client 根視覺，避免外側留白被 VisualBrush 再次縮放。新增鍵盤聚焦揭露管理按鈕及唯讀複製對比檢查。

快速檢查通過工具契約 12、Core 389，建置零警告／零錯誤；最終相同原始碼的 Release + Layout 通過 Lifecycle 28、Appearance 60、CompatibilitySlow 22（實際 35 秒腳本）、AiRuntime 35、AiPanel 26、Layout 22 與 Preflight 1。桌面報告：`artifacts/checks/desktop/20261101T004222963Z-3a975a9064d245efaa2e1831d7d984be/report.json`。專用 fixture 的前景啟用由 computer-use 協助；最終指紋相符。較早一次前置檢查因舊 App 尚未退出而拒絕，正常結束後重跑；開發中的聚焦測試曾因截圖修正而變更指紋，沒有使用該份報告發佈。

本輪未呼叫真實模型、未修改使用者服務／模型／金鑰設定；實體 IME、混合 DPI 與第三方 App 完整回歸的覆蓋限制沿用前節。

使用上述相同原始碼報告完成 staging 發佈，Core 389、Probe smoke 10 再次通過，`artifacts/p0` 已更新。舊套件保留於 `artifacts/publish/7a47359908864a80bfa691b417ac3b42/previous`；發佈報告：`artifacts/checks/build/20261101T004531328Z-01681e5d77ee47d3a7dd9b2138abcf6b/report.json`。以 computer-use 正常啟動新套件，確認自動偵測、選字相容及 Sublime 偏好恢復，再關閉設定視窗回到系統匣待命。

## 2026-11-01 AI panel layout 精修

- 輸入框由固定 84 DIP 改為兩行起始、最小 60 DIP，多行及自動換行可增高至 120 DIP 後捲動。收斂分類及列間距，保留 40 DIP 指令按鈕；標準 800 × 650 視窗完整顯示五個預設指令，不需要捲動。
- 基準列保留 28 DIP，進度列保留 2 DIP；顯示「從原文重做」、停止與進度時不再推動輸入框。原文／結果分頁、搜尋與直接輸入的流程維持。
- 結果摘要顯示已執行的指令名稱或「自訂指令」，不再露出截斷的英文 prompt。名稱在送出時取快照，只在記憶體與版本一起保留，分支或淘汰舊版本時清理；最多對應 20 個版本。基準與完整指令保留於所屬詳情視窗，只有一版時隱藏歷史箭頭。
- 唯讀及來源失效狀態的文案，依目前原文／結果分頁對應複製內容。未放寬前景、焦點或選取失效規則；既有面板持有焦點與外部焦點取消的原生案例仍通過。

快速檢查通過工具契約 12、Core 389，Release 建置零警告／零錯誤。相同最終原始碼的 Release + Layout 報告通過 Lifecycle 28、Appearance 60、CompatibilitySlow 22（含真實 35 秒腳本）、AiRuntime 35、AiPanel 35、Layout 22 與 Preflight 1：`artifacts/checks/desktop/20261101T010246578Z-1bd685290c6a406891bcf68d03d61288/report.json`。AI 幾何檢查確認五個按鈕完全位於 viewport、沒有可捲動高度，以及送出前／執行中／完成後輸入框 Y 座標一致。另確認多行增高有界、指令名稱、歷史箭頭及原文／結果文案。

已目視核對 `artifacts/ai-panel/original.png`、`saved-command-result.png`、`multiline-input.png`、`long-instruction.png` 與可替換結果。測試使用隔離設定及受控 client，未呼叫真實模型或改動使用者 AI 設定；實體 IME、混合 DPI 與第三方 App 全套驗證限制仍沿用前節。

同一份成功桌面報告已用於自含式 staging 發佈；Core 389、Probe smoke 10 再次通過，`artifacts/p0` 已更新。發佈報告：`artifacts/checks/build/20261101T010418548Z-31b119044c86459281a74c7dc17f4c36/report.json`；舊套件保留於 `artifacts/publish/2693486129b84493ac23b16abcde38e3/previous`。以 computer-use 確認新版正常啟動與既有偏好載入，關閉設定頁後回到系統匣待命。

## 2026-11-01 AI panel copy 操作辨識

依使用者同意的收尾方向，將「基於」明確標示為「下次改寫」，切換來源改為「改用第 n 版」及「改用原文」。基準文字保持單行並保留完整提示，避免兩個來源切換按鈕同時顯示時換行。右上改成「指令詳情」文字入口，唯讀結果提示改為「此處無法直接替換，請複製結果。」；原文分頁保留對應文案。未修改 session、來源失效或安全政策。

快速檢查通過工具契約 12、Core 389，建置零警告／零錯誤；相同最終原始碼的 Release + Layout 通過 Lifecycle 28、Appearance 60、CompatibilitySlow 22、AiRuntime 35、AiPanel 35、Layout 22 及 Preflight 1。報告：`artifacts/checks/desktop/20261101T011146025Z-bd7406a6d29c4ad498c725224e3bf671/report.json`。已目視核對一般結果與最小尺寸長指令截圖，並沿用既有幾何、完整指令、原文／結果文案與實際原選取替換案例。沒有呼叫真實模型或改動使用者 AI 設定；實體 IME、混合 DPI 及第三方 App 覆蓋限制維持。

同一份成功報告已完成自含式發佈，Core 389、Probe smoke 10 通過；新版 `artifacts/p0` 已正常啟動並回到系統匣。發佈報告：`artifacts/checks/build/20261101T011323176Z-42196545ee6540a68da67182f477c68d/report.json`；舊套件：`artifacts/publish/46674e352623415daa3093934b54e96b/previous`。原有偏好及 AI 設定保留。
