# SnapBar Windows 版功能與開發規劃

日期：2026-10-22  
狀態：使用者於 2026-10-22 指示繼續，已進入 P0 原型；實作與驗證進度見 [P0 驗證報告](../../p0-validation.md)。  
已確認方向：核心功能優先，再補齊 AI／自訂動作。

## 1. 產品目標與範圍

建立常駐 Windows 系統匣的選字工具：使用者在支援的 App 選取文字後，於選取位置附近顯示動作列，快速複製、剪下、貼上、搜尋、開啟網址或在檔案總管顯示路徑。後續加入 AI 改寫、自訂動作及 Planka。

第一階段的成功標準是「選得到、彈得準、不干擾原本操作」。完整功能以 macOS 版為對照，但 Windows 的權限、剪貼簿、圖示與腳本行為採平台原生設計。

建議首發支援仍在 Microsoft 支援期內的 Windows 11 x64 版本，正式測試時記錄確切版本與組建。Windows ARM64、Windows 10 特殊支援環境列為後續相容性評估，不先承諾。預設以一般使用者權限執行；不以管理員常駐作為跨 App 操作的前提。

目前工作目錄只有 README，沒有 Swift 原始碼或動作範例；能依據的是功能契約，不能假設現有程式、測試或 `.snapbaraction` 格式已可直接重用。

## 2. 技術方案比較

| 方案 | 適合之處 | 本專案需承擔的工作 | 建議 |
| --- | --- | --- | --- |
| C# + .NET 10 LTS + WPF + Win32 | 適合桌面常駐工具、獨立小視窗、設定頁與 Windows API 整合 | 自行建立一致的現代樣式，處理 HWND、焦點與 DPI | 優先採用 |
| C# + WinUI 3 + Windows App SDK | Fluent 外觀與現代 Windows 控制項整合度高 | 同樣需要 Win32／UI Automation；需額外驗證無焦點浮窗及部署方式 | 團隊已有 WinUI 經驗時可選 |

Microsoft 將 WinUI 3 列為新 Windows App 的建議原生 UI 框架；本案選 WPF 是依「桌面浮窗與跨 App 操作優先」所作的工程取捨，不代表官方不建議 WinUI。[WPF 說明](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)、[WinUI 3 說明](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)。

基準採 .NET 10 LTS，開工時固定最新受支援的 10.0 修補版。官方列出的支援終止日為 2028-11-14。[.NET 支援政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)。

建議組合：WPF／MVVM 負責 UI，UI Automation 負責讀取選字，Win32 負責快捷鍵、輸入事件與視窗定位，JSON 負責非機密設定，Windows DPAPI 負責加密 API Key。

## 3. 原版功能對照與分期

P0 為技術驗證、P1 為可日常使用的核心版、P2 為 AI 版、P3 為擴充動作版、P4 為整合及發行強化。

| 原版功能 | Windows 設計 | 階段 |
| --- | --- | --- |
| 拖曳／雙擊／三擊選字 | 觀察滑鼠事件後讀取 App 實際選取範圍；三擊選取單位由原 App 決定 | P0、P1 |
| Copy | 將選取純文字寫入剪貼簿 | P1 |
| Cut／Paste | 原目標仍有效且可編輯時執行 Ctrl+X／Ctrl+V | P1 |
| Search | 預設 Google，允許設定搜尋網址範本，正確編碼文字 | P1 |
| Open Link | 僅辨識完整 http／https 網址，交給預設瀏覽器 | P1 |
| Show in Finder | 改為「在檔案總管中顯示」，開啟父資料夾並選取項目 | P1 |
| Dictionary | 採獨立可設定的網頁字典動作；離線字典不列入初版 | P4 |
| On Bar／In ... Menu／Hidden | 保留三區、右鍵搬移、On Bar 拖曳排序、即時圖示預覽 | P1 |
| 可搜尋的 ... 面板 | 名稱子字串搜尋、不分大小寫、RECENT 最多 5 筆、其餘按名稱排序 | P1 |
| 最近使用持久化 | 以穩定 action ID 儲存；隱藏或刪除後排除，不重複列出 | P1 |
| 選字自動複製 | 預設關閉，與自動彈窗獨立；可要求放開滑鼠時按住 Ctrl | P1 |
| 複製成功回饋 | 預設關閉；勾選提示共用 HUD 時間設定 | P1 |
| 自動彈窗開關與延遲 | 延遲 0–3 秒，預設 0；僅拖曳套用延遲，快捷鍵即時觸發 | P1 |
| Hover dismiss delay | 0–3 秒；必須曾進入動作列才啟動移出倒數 | P1 |
| 快捷鍵錄製與 Reset | 建議 Ctrl+Alt+V，可改鍵；註冊衝突時保留原設定並提示 | P1 |
| 排除 App | 依執行檔完整路徑識別，顯示友善名稱；自動行為停用、快捷鍵仍可用 | P1 |
| 外觀 | 動作列大小、圖示／圖示加文字、淺色／深色／跟隨系統 | P1 |
| AI prompt／自由指令／歷史 | 保留原文、結果、上一版／下一版及重試 | P2 |
| AI Markdown 顯示 | 原文純文字；結果排版；Copy／Apply 使用原始回應文字 | P2 |
| AI providers | OpenAI、Claude、OpenRouter、MiniMax、自訂相容 endpoint | P2 |
| URL 自訂動作 | 保留 `***` 替換規則，對選字做 URL encoding | P3 |
| Shell Script | 預設 Windows PowerShell；可指定 pwsh／Python／其他直譯器 | P3 |
| JavaScript | 使用安裝的 Node.js，允許自訂完整執行檔路徑 | P3 |
| 腳本 timeout／輸出模式 | 保留 1–300 秒、預設 10 秒及六種輸出處理 | P3 |
| 動作匯入／匯出 | `.snapbaraction` 預覽後匯入，新增動作預設進入 ... | P3 |
| 圖示選擇器 | 平台中立 icon ID、繁中／英文關鍵字、Windows 圖示對應 | P2、P3 |
| Add to Planka | 設定服務、看板與清單，使用者明確執行後建立卡片 | P4 |
| Diagnostics | 記憶體內最近 100 筆中性事件、複製摘要、匯出版本化 JSON、清除 | P1 起 |

Dictionary 是功能替代，不承諾與 macOS 內建字典資料一致。SF Symbols 名稱可保留作為匯入中繼資料，但以 Windows 可用圖示呈現；找不到對應時用預設圖示。

## 4. 第一版使用流程與互動契約

### 4.1 啟動與設定

啟動後常駐系統匣，提供開啟設定、暫停自動偵測、恢復、結束。登入自動啟動預設關閉。單一執行個體，重複啟動時開啟既有設定視窗。

第一版設定頁包含 General、Actions、Excluded Apps、Diagnostics、About；AI 與整合頁隨功能推出。介面預設繁體中文，內建動作另提供英文搜尋別名。

### 4.2 選字與動作列

1. 使用者完成拖曳、雙擊或三擊，系統短暫合併事件，初始 debounce 設計值約 120ms。
2. 檢查自動行為開關、排除 App、前景視窗及敏感控制項，再讀取選字。
3. 非空文字才建立一次選取工作階段；自動複製與彈窗共用結果，避免重複模擬複製。
4. 自動複製若開啟，且符合 Ctrl 條件，立即寫入剪貼簿。
5. 自動彈窗若開啟，依拖曳延遲設定顯示動作列；期間前景或選取改變則取消。
6. 動作列靠近選取範圍末端；缺少可靠座標時改用游標位置，限制在當前螢幕工作區內。

建議初始排列為 Copy、Cut、Paste、Search、Open Link、檔案總管、...；不適用的條件式動作不顯示。無法確認可編輯時，不提供 Cut 或覆寫操作。多段不連續選取可用換行組合供 Copy／Search，第一版禁止對多段執行 Cut 或後續 Apply。

手動快捷鍵忽略排除清單及自動彈窗開關，但仍遵守密碼欄位、權限及目標有效性限制；不觸發自動複製。沒有文字選取時，只有在可確認的編輯游標與可貼上內容存在時顯示 Paste，其他情況提示「未取得選取文字」。

### 4.3 焦點與關閉規則

動作列顯示與滑鼠點擊不應先奪走原 App 的文字選取。... 搜尋面板、AI 面板、設定視窗需要輸入時才取得焦點，並保留原目標識別資料。

純動作列遇到外部點擊、一般按鍵或外部捲動時關閉；事件照常傳給原 App。搜尋／AI 面板開啟時，其內部按鍵與捲動依面板規則處理，不套用「任意鍵關閉」。... 面板支援上下移動、Enter 執行、Esc 先清空再關閉；最近使用清單重啟後保留。

HUD auto-dismiss delay 為 0.5–5 秒，預設 2.5 秒；共用於自動複製回饋及後續自訂動作。

## 5. Windows 核心架構

| 模組 | 職責與介面邊界 |
| --- | --- |
| SnapBar.Core | 動作模型、顯示條件、搜尋排序、設定模型、彈窗位置計算；不依賴 WPF／Win32 |
| SnapBar.Windows | UI Automation、滑鼠事件、快捷鍵、前景視窗、剪貼簿及檔案總管整合 |
| SnapBar.App | WPF 動作列、... 面板、系統匣、設定與結果視窗 |
| SnapBar.Integrations | 後續 AI／Planka HTTP clients，透過介面取得設定與機密 |
| SnapBar.Tests | 純邏輯與可控制的整合測試；跨 App 相容性另存實測紀錄 |

資料流：輸入事件 → 選字服務 → SelectionSession → 動作適用性判斷 → 彈窗 → 動作執行器 → HUD／結果面板。

SelectionSession 至少包含 session ID、文字、選取範圍、來源 PID／程序啟動時間／HWND、UIA 元素識別、讀取方式、可編輯狀態（是／否／未知）及建立時間。文字只保留於記憶體，不寫入一般日誌。

每次選取建立 generation ID；新選取、前景切換或關閉會使舊工作失效。晚到的 UIA／AI／腳本結果不得觸發對新目標的寫入。

### 5.1 選字讀取

優先透過 UI Automation TextPattern 取得選取文字與範圍。GetSelection 可能回傳空範圍或 null，不能把「有回傳範圍」當成「有文字」。不要以整個控制項的 Value 或 DocumentRange 冒充選取內容。[TextPattern.GetSelection](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.textpattern.getselection?view=windowsdesktop-10.0)。

UIA 操作放在專用 MTA 背景執行緒，事件 hook 只入列、不等待遠端 App。設定讀取時間預算與過期結果丟棄；若 provider 可能永久阻塞，P0 驗證後決定是否隔離為可重啟的 helper process。一般工作逾時不等於底層 COM 呼叫已被取消，禁止靠不斷建立背景執行緒掩蓋阻塞。[UI Automation 執行緒指引](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading)。

預設自動選字僅使用 UIA；Ctrl+C fallback 只對經過驗證且使用者啟用的 App profile 開放。手動快捷鍵可依設定嘗試 fallback，但終端機／未知控制項預設停用合成複製，避免 Ctrl+C 變成中斷指令。不得直接套用 macOS 的 Chromium accessibility 啟用技巧。

### 5.2 剪貼簿保護

Copy、自動複製及使用者選擇的輸出複製，本來就應改變剪貼簿。只有「為讀取文字暫借剪貼簿」需要還原。

fallback 流程為：確認目標 → 快照已支援的剪貼簿格式 → 發出複製 → 等待剪貼簿變更 → 讀取文字 → 在仍能確認交易有效時還原。交易序列化執行，若偵測使用者或其他 App 已寫入新內容，就不覆蓋新內容。

序號可協助偵測變更，但不能單獨證明寫入來源。需結合 owner、目標及交易狀態；歸屬不明時放棄還原並顯示可理解的回饋。[GetClipboardSequenceNumber](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboardsequencenumber)。

Windows 剪貼簿包含延遲呈現、OLE 與私有格式，不能保證所有內容均可無損快照。必測 Unicode 文字、HTML、RTF、圖片與檔案清單；遇到不能可靠保存的格式時跳過 fallback，不以「完整還原所有格式」作產品承諾。暫借剪貼簿也可能被剪貼簿歷史或第三方工具觀察到，需於相容模式說明。[剪貼簿操作與延遲呈現](https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/dataxchg/clipboard-operations.md)。

### 5.3 目標還原與寫回

Cut、Paste、AI Apply、腳本直接貼上都透過同一個 TargetWriter。執行前重新驗證原程序、視窗、控制項、可編輯能力與選取；AI 面板停留很久後尤其不可只依舊 HWND 貼上。不可用 ValuePattern.SetValue 替換整個欄位來模擬取代選字。

SetForegroundWindow 可能被 Windows 拒絕；SendInput 受 UIPI 限制，無法任意注入較高完整性層級的 App。回復前景失敗、選取已變動或無法確認目標時停用寫回，提供「複製結果」。SendInput 成功送入事件也不等於目標已完成貼上。[前景視窗限制](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)、[SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)。

AI Apply 與腳本直接貼上預設將結果保留在剪貼簿，不在固定短延遲後還原，避免目標非同步讀取時拿到舊內容。UI 清楚標示「套用並複製結果」。

### 5.4 路徑、快捷鍵與排除規則

路徑接受磁碟機絕對路徑、UNC、`%USERPROFILE%` 與 `~` 使用者目錄縮寫；移除成對外層引號，不執行 shell 展開。網路路徑檢查置於背景並設時間預算。使用 Shell API 開啟父資料夾並選取，不把原始選字拼進命令列。[SHOpenFolderAndSelectItems](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shopenfolderandselectitems)。

快捷鍵透過 RegisterHotKey 註冊，避免預設使用 Windows 保留組合。Ctrl+Alt+V 是可調整的建議值，需在不同鍵盤配置與 AltGr 環境驗證。[RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)。

排除清單以執行檔路徑為主；封裝 App 視實測補充套件識別，不能只比對視窗標題。密碼欄位與安全桌面不提供讀取或覆寫，未知控制項不推定為安全可編輯。

## 6. 後續功能設計

### 6.1 P2：AI 改寫

保留五個內建 prompt：修正文法拼字、改善寫作、縮短、摘要、翻成英文；支援自由指令、prompt 篩選與圖示選擇。各 provider 分別記住 model，模型 ID 可手動設定，不將特定模型永久寫死。

先以 OpenAI 與自訂相容 endpoint 完成垂直流程，再在 P2 補齊 Claude、OpenRouter、MiniMax；每個 provider 使用獨立 adapter，開工時按其官方 API 驗證請求、錯誤及回應格式，不能假設相容 endpoint 完全等同。

只有使用者按下 prompt 或送出指令才發送文字；原文作為待處理資料，與固定轉換指令分開。此包裝降低指令混淆風險，不宣稱能完全消除 prompt injection；模型回應不執行程式或自動啟動動作。

結果支援標題、粗斜體、行內程式碼、清單、引言與程式碼區塊；程式碼區塊可水平捲動並獨立複製。原文一律純文字。停用任意 HTML、腳本及自動遠端資源載入，Copy／Apply 使用未經排版修改的文字。

請求中保留目前內容，顯示細進度條；可取消，失敗保留舊結果。缺 key 顯示設定入口；逾時、429、認證失敗各有明確提示。歷史僅在本次面板工作階段留存，上一版後產生新結果會截斷原先的前進分支。

Ctrl+Enter 套用結果；Ctrl+C 在結果區有文字選取時複製該選取，否則依面板焦點規則複製目前結果，不攔截指令輸入框的標準複製。輸入框需正確處理繁中 IME，組字確認 Enter 不得誤送出。

API Key 以 DPAPI CurrentUser 加密後獨立保存，依 provider／endpoint profile 分開；不進入設定匯出與診斷。這是 Windows 使用者範圍保護，不是同一使用者帳號內的程序隔離。[ProtectedData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata?view=windowsdesktop-10.0)。

### 6.2 P3：自訂動作

提供 URL、Shell Script、JavaScript 三種。文字經 `POPCLIP_TEXT` 傳入，PowerShell 使用 `$env:POPCLIP_TEXT`，Node 使用 `process.env.POPCLIP_TEXT`。不把選取文字拼進 shell 程式碼或參數字串。

執行檔路徑與參數清單分開儲存，採參數陣列啟動。預設直譯器為 Windows PowerShell；若使用者指定 pwsh、Python 或 Node，先驗證路徑與可用性。macOS 的 `/bin/sh` 腳本不自動改寫；需標示不相容並由使用者指定 Windows 版本。初版不整合 WSL。

環境變數有容量限制：設定合理的輸入大小上限，過長時明確拒絕，不靜默截斷；後續可增設 stdin／檔案輸入模式。UTF-8 為腳本檔及輸出預設，PowerShell 與其他直譯器的實際編碼差異需整合測試。

逾時範圍 1–300 秒，無效值回復 10 秒。以 Job Object 等方式管理子程序樹，測試逾時後孫程序也退出；限制 stdout／stderr 容量並同步讀取，避免管線阻塞。一般使用者權限執行，不宣稱這是安全沙箱。

輸出模式包含不處理、複製、直接貼上、HUD、純文字視窗、Markdown 視窗。六種模式共用成功／失敗回饋；非零 exit code 或逾時不得顯示成功。直接貼上沿用 TargetWriter 驗證。

`.snapbaraction` 採有 schemaVersion 的 JSON，記錄穩定 ID、種類、名稱、圖示、平台、直譯器、腳本、輸出模式與逾時。匯入前顯示完整內容與可執行程式碼；匯入本身不執行。沒有原版檔案樣本前，不承諾格式完全互通；實作時增加原版讀取 adapter 與相容性測試。

預留 `exports/actions/` 與 `exports/prompts/`，匯出不包含 key、使用歷史或來源選字。圖示使用具授權的本機資產及穩定 ID。

### 6.3 P4：Planka 與字典

Planka 提供服務位址、認證及看板／清單設定；建立卡片成功後顯示連結。寫入請求逾時但結果未知時，不盲目自動重送以免重複建立。API 細節以目標部署版本驗證。

字典提供獨立 URL 範本，以選字查詢；與一般 Search 分開設定。若未來要離線字典，另開子專案處理資料授權、索引與查詢 UI。

## 7. 儲存、診斷與發行

一般設定放在 `%LOCALAPPDATA%\SnapBar\`，分為 settings、actions、prompts、recent-actions；採 schemaVersion、原子替換寫入及升級前備份。資料損壞時保留原檔並載入可用預設，避免整個 App 無法啟動。

Diagnostics 僅保存最近 100 筆記憶體事件：時間、事件類型、App 識別、耗時、字數、成功與錯誤分類。不記錄文字、剪貼簿、prompt、腳本、API body、金鑰、視窗標題、完整網址或含使用者名稱的路徑；例外訊息先清理再輸出。

P1 交付 Windows x64 自含式試用套件，使用者無須另裝 .NET；P4 再完成簽章安裝程式、登入啟動、卸載及更新流程。自含式套件須隨 .NET 安全修補重新發布。首版更新以人工下載安裝為主，自動更新另行設計，不阻塞核心版。

## 8. 開發順序、交付與驗收

以下為單一熟悉 C#／Windows 桌面開發者的粗估工作日，含基本整合測試；不含等待簽章、服務帳號或未預期的第三方 App 相容性排查。P0 完成後重新估算，不作交期承諾。

| 階段 | 粗估 | 可審閱交付物 | 通過條件 |
| --- | --- | --- | --- |
| P0 技術驗證 | 3–5 天 | 選字／焦點／剪貼簿實驗程式、App 相容性報告 | 在記事本、Chrome／Edge 文字與輸入框完成讀取及基本動作；UI 不被不良 provider 卡死；列出 fallback 限制 |
| P1 核心可用版 | 10–15 天 | 動作列、六個核心動作、...、設定、自動複製、排除清單、診斷、試用套件 | 完成核心流程矩陣與多螢幕測試，無錯貼／誤剪／覆蓋使用者新剪貼簿等阻斷問題 |
| P2 AI 版 | 7–10 天 | 五類 provider、prompt、Markdown、歷史、Copy／Apply、機密儲存 | 可控回應測試及各 provider 連線驗證；請求失敗保留結果；原目標變動不寫回 |
| P3 自訂動作版 | 7–10 天 | 三種動作、六種輸出、逾時、匯入／匯出、圖示選擇 | 特殊字元／中文完整傳入；非零碼正確失敗；逾時清理程序樹；匯入不執行 |
| P4 整合與發行 | 5–8 天 | Planka、網頁字典、簽章安裝及升級文件 | 相容性回歸、設定升級、卸載及無機密洩漏檢查完成 |

核心版約 13–20 工作日；全功能約 32–48 工作日，另預留 20–30% 相容性與發行緩衝。若資源有限，P1 即可作為獨立可用產品發布，P2／P3 分別交付，不互相綁定。

### 8.1 測試矩陣

| 類別 | 必測情境 |
| --- | --- |
| 目標 App | 記事本、Chrome、Edge、Word、VS Code；Windows Terminal、LINE／SeaTalk、PDF 閱讀器列相容性探索，不預先宣稱支援 |
| 選字 | 拖曳、雙擊、三擊、多行、空白、中文、emoji、超長文字、多段選取、無選取 |
| 寫回 | 可編輯／唯讀／未知、原視窗關閉、原文改變、切換 App、權限不同、面板停留後 Apply |
| 剪貼簿 | 文字／HTML／RTF／圖片／檔案、被占用、第三方修改、延遲呈現、不支援格式 |
| 顯示 | 單／雙螢幕、負座標、100／150／200% 縮放、混合 DPI、工作列邊界 |
| 輸入 | 繁中 IME、快捷鍵衝突、AltGr、滑鼠進出、滾輪、觸控板、點擊外部 |
| 長時間 | 連續選字、8 小時常駐、休眠／喚醒、螢幕鎖定／解鎖、目標 App 無回應 |

每個 App 分開標記「讀取、定位、Copy、Cut／Paste、AI Apply」是否通過，記錄版本、控制項與讀取策略，避免把「可讀到文字」誤標為全部支援。

### 8.2 可量測的品質目標

- UIA 正常路徑、Popup delay=0、固定測試機：從滑鼠放開到可操作彈窗的 P95 目標不超過 350ms；先由 P0 建立基線。
- 閒置時 CPU 平均目標低於單一邏輯核心的 1%；P1 啟動穩定後 private bytes 初始預算 150MB，測試報告記錄量測口徑與硬體。
- 100 次連續選字／關閉後，hook、視窗與工作數量回到穩定基線；8 小時常駐不持續累積資源。
- 排除 App 的自動讀取、彈窗、自動複製均不發生；快捷鍵仍依手動規則運作。
- 測試中只要出現一次寫入錯誤 App、誤刪內容或覆蓋使用者更新的剪貼簿，即阻擋核心版發布。

單元測試集中於動作適用性、設定遷移、排序、URL／路徑判斷、彈窗定位、交易狀態與失效工作階段；跨程序選字與焦點操作以實際 App 整合測試為準。

## 9. 建議的下一個工作包

審閱本規劃後，先執行 P0：只驗證「UIA 讀取 → 不搶焦點動作列 → Copy／Cut／Paste → 剪貼簿保護」，交付確切的 App 相容性表與失敗原因。P0 結果確認技術路線可行後，再拆出 P1 的實作任務與測試清單。

本文件記錄整體目標，非全部功能已實作的聲明。後續已建立 P0 原型、專案內 SDK 與自含式套件；完整跨 App 相容性仍待驗證。工作目錄目前不是 Git repository，因此文件未提交 Git。
