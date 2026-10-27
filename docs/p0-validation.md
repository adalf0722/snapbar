# P0 驗證紀錄

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
