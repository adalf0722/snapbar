# SnapBar 剪貼簿相容能力交接

日期：2026-10-23。工作目錄：`D:/codex/snapbar`。

**續作更新（2026-10-23）：已實作 UIA 優先路由、共用剪貼簿協調器、Sublime 適配器與條件剪下／貼上，並完成控制器整合及文件結果驗證。最新結果與仍待補驗項目見 [p0-validation.md](p0-validation.md) 頂部，設計見 [clipboard-actions-design](superpowers/specs/2026-10-23-clipboard-actions-design.md)。以下為續作前交接快照，保留作歷史背景。**

**狀態：使用者同意下列設計方向，要求先寫交接文件，之後再處理。本輪僅新增文件，沒有修改程式、開放 Sublime 剪下／貼上或實作 Electron 通用支援。**

## 1. 使用者需求與已確認方向

- Windows 版先完成核心功能，再補 AI／自訂動作。
- 使用者已回報 Chrome、Edge、記事本測試正常；Sublime 曾無法取得選取文字，現已有條件式剪貼簿回退。
- 使用者明確選擇「不裝外掛，使用有條件的剪貼簿相容模式」。目前用 Sublime JSON 快捷鍵設定，沒有安裝 Python 外掛。
- 使用者要求開放 Sublime 剪下／貼上；這項需求仍未完成，保留為下次工作。
- 使用者表示 macOS 也遇過類似問題，詢問是否統一處理 Electron App。已同意：**將剪貼簿回退做成正式的共用能力，依目前控制項的能力選擇路徑，不將所有 Electron App 一律導向回退。**
- 產品目標是選取文字後可靠出現工具列；使用者不必理解 UIA、AX 或 Electron。底層保留 App 差異，介面優先呈現「自動、僅快捷鍵、停用」等選項。這是後續方向，尚未落地。

## 2. 現有實作與驗證基線

Windows P0 使用 C#／.NET 10／WPF，透過獨立 UI Automation helper 讀取選取文字。工具列不搶焦點，提供複製、剪下、貼上；手動快捷鍵為 Ctrl+Alt+V，自動偵測可開關。

### Sublime 目前的實際路徑

1. 使用者啟用相容模式，安裝並驗證 Ctrl+Alt+Shift+F12 的兩條條件綁定：預設 `noop`；單段、非空、非面板／覆蓋介面選取才執行內建 `copy`。
2. 備份目前可安全保存的剪貼簿格式，總量上限 64 MiB。任一格式無法安全備份時取消整次探測，不是丟掉該格式後繼續。
3. 檢查前景視窗與程序身分、輸入代次及剪貼簿序號，送出條件快捷鍵。
4. 等候新的剪貼簿資料與序號穩定，在剪貼簿鎖內讀取文字並還原原內容。
5. 成功後顯示工具列，快照來源為 `sublime-clipboard`，`Editable = null`。複製前再次讀取並比對文字；剪下／貼上仍停用。

目前控制器是「辨識到 Sublime 且開關啟用，直接使用專用回退；其他情況使用 UIA」，**尚未改成通用的能力路由**。

支援備份的格式包含文字、DIB／DIBV5、GDI bitmap、palette、enhanced metafile、檔案清單及可安全複製的註冊格式；不代表任意自訂格式都支援。原生資源依格式使用對應的複製與釋放方式。

設定檔位置：`%APPDATA%/Sublime Text/Packages/User/Default (Windows).sublime-keymap`。安裝保留原有文字、註解與設定，先備份既有檔案；衝突或設定被改動時拒絕使用。攜帶版尚不支援。相容開關目前不持久化，每次啟動需重新啟用。

### 已確認的問題與限制

- Sublime Build 4180 的編輯器未透過此次 UIA 探測提供文字選取；一般 Ctrl+C 在空選取時可能複製整行，因此必須使用條件命令。
- 舊版把 `GetClipboardOwner() == NULL` 誤判成其他程式更新。實測 Sublime 複製會回傳 NULL；新版在序號穩定、前景相符、輸入仍有效時接受無擁有者結果。
- NULL owner 仍不能證明資料來自 Sublime；同時發生的無擁有者寫入可能無法區分。序號與焦點檢查是降低競態風險，不是來源證明或原子交易。
- 原先 4 MiB／圖片備份限制已改為上述格式支援與 64 MiB 上限。
- 複製探測會真的改變系統剪貼簿；還原目前內容不會清除 Windows／第三方歷史，也不能撤回同步工具已收到的資料。
- 晚到的複製結果、強制結束、並行寫入與取消競態仍有不能保證還原的情境。不得宣稱完全無副作用。
- 只比對選取文字，無法區分兩個內容相同但位置不同的範圍；新增寫入能力時必須重新評估。

### 上次驗證結果（本次未重跑）

| 項目 | 已有證據與界線 |
| --- | --- |
| Release 建置 | 0 errors／0 warnings |
| 自動驗證 | 36 項核心測試、7 項原生剪貼簿往返測試、6 項 helper 協定／結束檢查通過 |
| Sublime Build 4180 | 手動選取 7 字、自動雙擊 9 字、拖曳 15 字均實際顯示工具列；空選取不顯示 |
| 剪貼簿還原 | 原內容 Windows，探測 selection 後貼出仍為 Windows |
| Sublime 工具列動作 | 複製按鈕顯示可用，但按鈕端到端尚未完成自動化；剪下／貼上尚未開放 |
| Chrome／Edge／記事本 | 使用者回報正常；未逐項記錄版本、控制項與每種動作，且回報來自 Sublime 修正前版本 |
| 未驗證 | 多游標／搜尋面板實機條件、剪貼簿管理器競態、極慢回應、讀取中切換／關閉、Word／VS Code、混合 DPI、多螢幕、長時間效能 |

完整紀錄見 `D:/codex/snapbar/docs/p0-validation.md`。不要把工具列出現等同於所有按鈕均通過，也不要把核心政策測試等同於實機競態驗收。

## 3. 下階段架構決策

### 3.1 共用選取流程，以能力決定路徑

建議將狀態至少區分為：成功、確定無選取、不支援、密碼／敏感欄位、逾時、目標失效。名稱與介面尚待設計。

| 判斷結果 | 行為 |
| --- | --- |
| 原生 UIA／AX 能讀取 | 優先使用原生介面 |
| 明確沒有選取 | 停止，不用複製再試一次 |
| 不支援且有經驗證的 App 相容策略 | 允許條件式剪貼簿回退 |
| 未知 App、暫時失敗或逾時 | 保守停止，提供手動觸發；手動觸發也必須遵守適用與排除條件 |
| 密碼欄位、目標失效 | 停止，不以回退繞過排除 |

App／框架分類只作為策略線索，最終判斷針對當下控制項。版本或控制項改變時，舊能力判定不能無限沿用。無法證實控制項安全性時，不應盲目自動複製。

### 3.2 共用剪貼簿協調層，平台與 App 各自適配

共用的是備份、序列化請求、取消、來源與目標驗證、讀取、還原、錯誤分類等規則；Windows 與 macOS 仍需各自實作系統介面，不是共用 Win32 程式碼。

- 同一選取手勢最多啟動一次回退探測，避免重複複製及工具列閃爍。
- 無法完整備份所需資料時，探測前停止；不要默默只保存純文字。
- 能辨識到外部的新內容時，不用舊備份覆蓋。明確保留「來源未知／無法還原」等結果，不能聲稱所有競態都可辨識。
- App 適配器負責條件命令、空選取行為、能力確認及版本差異；不要把所有特殊情況塞進控制器。
- 未經驗證的 App 不因為是 Electron 就自動取得回退資格。
- 日誌記錄狀態、策略、耗時與版本即可；避免預設記錄使用者選取內容或剪貼簿原文。

### 3.3 讀取與寫入能力分開

讀得到文字只代表可顯示讀取型動作。剪下／貼上還需要目前控制項可編輯、單段非空選取、目標與選取仍有效，並在執行前重新驗證。

不能單靠 App 選單中的 Cut／Paste 是否啟用判定可編輯；Chromium 有不可編輯選取仍啟用選單的情境。未知編輯狀態應維持禁止寫入。

貼上前的重新探測不能把使用者原本要貼上的資料替換成剛讀到的選取。剪下成功後則應保留剪下結果在剪貼簿，不能用探測備份覆蓋它。指令送出與目標文件已成功修改是不同結果。

## 4. 建議續作順序與未定事項

1. **先確認基線並抽出最小共用邊界。** 整理選取結果與能力判斷、剪貼簿協調流程、Sublime 適配器，保留既有行為；避免先建立過大的跨平台框架。
2. **完成使用者已要求的 Sublime 剪下／貼上。** 調查內建快捷鍵的可編輯條件，設計執行時仍受條件保護的 cut／paste。不要只解除按鈕停用，也不要直接使用無條件 Ctrl+X／Ctrl+V。
3. **處理設定升級。** 若需新增條件快捷鍵，應識別現有兩條綁定、保留使用者設定、備份、檢查衝突、確保冪等並提供撤回方式。具體鍵位、可編輯探測方式與設定格式尚未決定。
4. **以實際 Electron App 建立能力矩陣。** 可先用 VS Code／Obsidian 作候選；逐一測試編輯區、唯讀預覽與搜尋欄。先確認無障礙介面，再決定是否需要共用框架策略或個別例外。尚未承諾支援任何特定版本。
5. **再整理 macOS 對應實作。** 本次僅確認設計方向，未修改或驗證 macOS 程式。先定位實際 macOS 原始碼與問題案例，再處理 AX 啟用、選取讀取及剪貼簿適配。

Sublime 的可編輯探測是否需要額外一次複製、如何確認相同文字的不同範圍、動作完成如何回報，均需實作前收斂。任何新探測必須評估延遲及剪貼簿副作用。

### 驗收要求

- 保留 Sublime 手動、雙擊、拖曳、空選取及原剪貼簿還原的既有結果。
- 在可丟棄 fixture 驗證複製、剪下、貼上：只影響指定範圍、周圍文字完整，剪下結果可貼出，貼上使用原預期內容。
- 唯讀、未知編輯狀態、多游標、空選取、面板與密碼欄位不得誤寫入；明確記錄不支援的範圍。
- 驗證動作前切換視窗、移動選取、關閉來源、修飾鍵未放開、剪貼簿被其他程式更新等情況。
- 回歸 Unicode、富文字／註冊格式、圖片、檔案清單、空剪貼簿、超量與無法備份的資料；可備份與不支援案例分開記錄。
- Chrome、Edge、記事本重新確認原路徑；新增 Electron App 按版本、控制項、讀取／複製／剪下／貼上分欄紀錄。
- 記錄冷／暖啟動及回退耗時；不把單次數值當成 P95。
- 既有 UI 自動化工具未能把不搶焦點的工具列列為可操作目標。保留產品焦點行為；以支援的工具或人工測試補證據，不把未完成的按鈕測試標成通過。

## 5. 接手時應先看的檔案

以下路徑皆以 `D:/codex/snapbar/` 為根目錄。

| 路徑 | 用途 |
| --- | --- |
| `src/SnapBar.App/SelectionController.cs` | 路由、失效判斷、動作執行；目前直接限制相容模式只能 Copy |
| `src/SnapBar.App/PopupWindow.cs` | 工具列呈現與按鈕能力 |
| `src/SnapBar.Core/Contracts.cs` | 選取快照、來源、Editable 與目標身分 |
| `src/SnapBar.Core/Policies.cs` | 寫入資格及共用政策 |
| `src/SnapBar.Core/SublimeKeymap.cs` | 現有兩條 F12 綁定、安裝與完整性驗證 |
| `src/SnapBar.Core/ClipboardCapturePolicy.cs` | 剪貼簿來源、序號及目標／輸入失效政策 |
| `src/SnapBar.Windows/SublimeCompatibility.cs` | 程序辨識、設定安裝與適用條件 |
| `src/SnapBar.Windows/ClipboardSelectionReader.cs` | 現有 Sublime 複製探測流程 |
| `src/SnapBar.Windows/ClipboardBackup.cs` | Win32 格式備份、資源生命週期與還原 |
| `src/SnapBar.Windows/Native.cs` | 原生呼叫與快捷鍵注入 |
| `tests/SnapBar.Tests`、`tests/SnapBar.Windows.Tests` | 核心及原生整合測試 |
| `docs/p0-validation.md` | 已驗證與未驗證項目 |
| `docs/superpowers/specs/2026-10-22-sublime-compatibility.md` | 現有僅複製模式規格；與本文件的未來方向需分開閱讀 |

## 6. 建置、發布與工作環境

- 本地 SDK：`D:/codex/snapbar/.tools/dotnet/dotnet.exe`，先前版本為 10.0.402。
- 先前確認工作目錄未初始化 Git；下次先重新確認，不假設有可用 commit。
- 原發布目錄：`D:/codex/snapbar/artifacts/p0`，套件：`D:/codex/snapbar/artifacts/SnapBar-Windows-P0.zip`。
- 上次驗收執行的是工作目錄內的程式；Downloads 舊版不應被當成最新版本。下次重查程序路徑，不能假設上次程序仍在執行。
- 曾使用 `tests/fixtures/selection.txt` 的獨立 Sublime 視窗測試，可能留有未儲存的測試內容；保留使用者原本文件，不擅自關閉或儲存。

在專案根目錄執行：

```powershell
# 建置與核心測試
./scripts/build.ps1

# 發布自含式 App、執行 helper 檢查、複製發布文件
./scripts/build.ps1 -Publish

# 原生剪貼簿整合測試：會暫時操作真實系統剪貼簿
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release -- --clipboard-roundtrip
```

`build.ps1 -Publish` 不會重建 ZIP；若有交付新版本，需另行更新套件並核對內容。以上是接手時的操作指引，本次沒有執行。

Markdown 一律 UTF-8 無 BOM；優先用 `apply_patch`，中文改動後以嚴格 UTF-8 讀回驗證。

## 7. 技術依據與研究界線

- [Electron Accessibility](https://www.electronjs.org/docs/latest/tutorial/accessibility)：Electron 支援無障礙；`app.setAccessibilitySupportEnabled` 是目標 App 內部 API，不能假設外部 SnapBar 可直接呼叫。macOS 外部輔助工具可用 `AXManualAccessibility` 請求啟用，仍需實測。
- [Sublime 自製 UI 框架](https://www.sublimetext.com/blog/articles/hardware-accelerated-rendering)：Sublime 不是 Electron；自製渲染本身也不等於必然沒有無障礙能力，本案以實測結果為準。
- [Sublime Key Bindings](https://www.sublimetext.com/docs/key_bindings.html)：續作時核對條件命令與可編輯判斷。
- [PopClip 開發者討論：Chromium 可編輯判斷](https://forum.popclip.app/t/can-popclip-detect-editable-fields-in-browsers/3768)：2026 年討論指出選單可用性不一定代表控制項可編輯。
- [PopClip 自訂剪貼簿格式歷史回報](https://forum.popclip.app/t/popclip-deletes-custom-clipboard-data-causes-problems-with-web-apps/677)：2022 年案例用來說明格式保存的重要性，不代表目前版本仍有同一問題。

## 8. 下次續作提示

> 請先閱讀 `D:/codex/snapbar/docs/2026-10-23-clipboard-fallback-handoff.md`，依已確認方向接續 Windows SnapBar。先確認現有僅複製版本的基線，整理最小共用選取／剪貼簿能力邊界，再完成 Sublime 剪下／貼上及實機驗證。不安裝 Sublime 外掛，不將所有 Electron App 一律導向剪貼簿回退；保留原生介面優先、空選取與不支援分流、未知可編輯狀態禁止寫入等原則。回報實際通過與尚未驗證的項目，不把送出快捷鍵當成操作完成。
