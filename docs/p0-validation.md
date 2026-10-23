# P0 驗證紀錄

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
