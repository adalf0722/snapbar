# SnapBar 首次使用體驗實作計畫

> 執行方式：在目前 checkout 依序實作與檢查。使用者本次只要求寫計畫；本文件的核取項目尚未執行。

**Goal:** 首次手動開啟即可透過真實選字與搜尋理解 SnapBar，並能略過、重看和安心退回系統匣。

**Architecture:** Core 負責教學保存與首次呈現政策。App 的協調器連接歡迎／完成視窗、獨立練習程序及選字控制器的結構化事件；沿用現有 Probe 與安全流程，不放寬自身程序選字限制。

**Tech Stack:** .NET 10、WPF、現有自訂 console test runners、System.IO.Pipes、英文／繁中 resx、PowerShell 7。

**設計依據：** [首次使用體驗設計](../specs/2026-11-14-first-run-onboarding-design.md)。預估切為六個可驗收工作單元；提交前只納入該單元檔案，不推送或發布。

## 檔案責任與依賴

| 檔案 | 變更與責任 |
| --- | --- |
| `src/SnapBar.Core/OnboardingState.cs` | 新增狀態、讀取結果與首次呈現純政策 |
| `src/SnapBar.Core/OnboardingStore.cs` | 新增獨立 JSON 保存、大小／版本檢查與原子替換 |
| `src/SnapBar.App/OnboardingCoordinator.cs` | 新增主程序工作階段與狀態轉移 |
| `src/SnapBar.App/OnboardingPracticeSession.cs` | 新增 pipe、子程序啟動與有界清理 |
| `src/SnapBar.App/OnboardingProtocol.cs` | 新增受限訊息契約與解析 |
| `src/SnapBar.App/OnboardingWindow.cs` | 新增歡迎／完成 UI；使用 ActionUi 現有樣式 |
| `src/SnapBar.App/OnboardingPracticeWindow.cs` | 新增正式練習 UI 與子程序訊息處理 |
| `src/SnapBar.App/Program.cs` | 新增練習分支，維持既有背景與單一執行個體行為 |
| `src/SnapBar.App/MainWindow.Onboarding.cs` | 新增局部類別，集中與主視窗整合 |
| `src/SnapBar.App/MainWindow.cs`、`MainWindow.General.cs`、`MainWindow.Settings.cs`、`MainWindow.Exit.cs` | 串接初始判定、手動顯示、保存偏好、入口與退出清理 |
| `src/SnapBar.App/SettingsSupportPage.cs` | 新增「使用入門」獨立按鈕，不藏在診斷 expander |
| `src/SnapBar.App/SelectionController.cs` | 新增工具列顯示／搜尋成功結構化事件 |
| `src/SnapBar.App/Resources/Strings.resx`、`Strings.zh-Hant.resx` | 新增全部使用者文案與可及性名稱 |
| `tests/SnapBar.Tests/OnboardingTests.cs`、`Program.cs` | 新增並註冊純規則、保存與身分比對案例 |
| `tests/SnapBar.Windows.Tests/OnboardingTest.cs`、`Program.cs` | 新增桌面整合案例，接入既有 Lifecycle／Appearance／Language 旗標 |
| `README.md`、`README.en.md`、`docs/development.md`、`docs/p0-validation.md` | 更新首次使用與驗證紀錄 |
| `release-notes/2026-11-14-first-run-onboarding.json` | 實作完成時新增繁中使用者說明；若檔名已存在另取唯一名稱 |

不新增桌面 suite 或發布證據格式，將案例納入現有必跑套件，避免同步變更多個發布閘門。實作前確認相關類別的實際測試入口與退出流程，局部加入呼叫，不重構其他設定功能。

## Task 1：首次判定與持久化

- [ ] 在 `OnboardingTests.cs` 以現有 `Check` 模式新增案例並在 Core runner 註冊。先驗證缺檔首次手動呈現、背景延後、舊設定抑制、Presented／Skipped／Completed 抑制、損壞與未支援版本保留、Completed 不降級。
- [ ] 執行 `pwsh -NoProfile -File scripts/build.ps1`，確認新增案例在政策未實作時失敗；若只因編譯不存在而失敗，完成最小型別後再確認行為斷言失敗。
- [ ] 定義最小契約：

```csharp
public enum OnboardingStatus { Presented, Skipped, Completed }
public sealed record OnboardingState(int Version, OnboardingStatus Status);
public enum OnboardingLoadKind { Missing, Valid, Unavailable, Invalid, Unsupported }
public sealed record OnboardingLoadResult(OnboardingLoadKind Kind, OnboardingState? State);
public static class OnboardingPolicy
{
    public static bool ShouldOffer(bool background, bool settingsExistedAtStartup,
        bool settingsReadable, OnboardingLoadResult onboarding) =>
        !background && !settingsExistedAtStartup && settingsReadable
        && onboarding.Kind == OnboardingLoadKind.Missing;
}
```

- [ ] `OnboardingStore` 使用 `Load()` 與 `Save(OnboardingState)`，只接受 version 1 與已定義狀態。讀取上限 4 KiB、嚴格 UTF-8 無 BOM、同目錄暫存檔加 flush 再替換；寫入失敗清除暫存，不刪舊檔；未知格式不覆寫。使用暫存資料夾測試 Unicode、截斷 JSON、非法狀態、鎖檔與保存失敗後原內容保持。
- [ ] 舊設定存在與設定可讀性由 App 在首次載入前取得，不在政策內探測磁碟。沿用 `SettingsStore.Load()` 的 warning；`hotkey-invalid` 可進教學但先修正鍵位，其餘讀取 warning 抑制自動呈現。
- [ ] 重跑建置／Core runner，確認新案例與既有 SettingsStoreTests 通過。建議提交：`feat(onboarding): add first-run state policy`。

## Task 2：受限的獨立練習程序

- [ ] 在桌面測試加入協定案例：超過 4 KiB、非 JSON、未知版本、重複連線、未回報 HWND、錯誤 HWND、父程序斷線、5 秒連線期限、重試工作階段取代舊工作階段；先確認對應失敗。
- [ ] `OnboardingProtocol.cs` 使用以下訊息結構，解析後依 kind 限制可出現欄位，不接受任意命令或檔案路徑：

```csharp
internal sealed record OnboardingMessage(
    int Version, string Kind, long Window = 0,
    string? Language = null, string? Shortcut = null);
// child -> parent: ready (Window)
// parent -> child: configure (Language, Shortcut), toolbar-shown,
//                 search-launched, browser-unavailable, close
```

- [ ] `OnboardingPracticeSession` 建立 GUID 名稱的單連線 `NamedPipeServerStream`，指定 `PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous`。以 `ProcessStartInfo.ArgumentList` 啟動目前 App 的 `--onboarding-practice` 分支。訊息讀取逐段計算位元組，超限即拒絕，不能先無限 `ReadLineAsync` 再檢查長度。
- [ ] 父程序持有 `Process` 物件，透過 Native 身分確認 ready HWND 的 PID 與啟動時間都符合該子程序。5 秒未 ready 或子程序退出，回到可見教學並顯示「無法開啟練習，請再試一次」。
- [ ] `Program.cs` 在 fixture／單一執行個體之前辨識完整練習參數。練習程序只建立 WPF、pipe 與 `OnboardingPracticeWindow`；ready 後等待 configure 才啟用練習內容，所有 WPF 更新經 Dispatcher。
- [ ] 實作取消與退出：先取消讀取／解除訂閱、送 close、等待最多 2 秒；僅在持有子程序身分仍吻合時結束該練習程序。子程序偵測父連線消失後結束。每次重試使用新 pipe，先清理前次工作階段。
- [ ] 以既有 Lifecycle 桌面 runner 執行程序隔離與清理案例，確認真實使用者偏好與 clipboard 未被測試準備覆寫。建議提交：`feat(onboarding): isolate practice sessions`。

## Task 3：真實操作進度與完成判定

- [ ] 新增案例：同 PID 不同啟動時間、不同 HWND、其他 App 搜尋、舊工作階段成功、搜尋驗證失敗、瀏覽器例外、只有工具列顯示，都不得完成；正確練習來源搜尋成功才完成。
- [ ] 在 Core 定義不含文字的通知契約，事件工作階段為既有選取的 Id，不另產生不相干的 Id：

```csharp
public enum SelectionProgressKind { ToolbarShown, SearchLaunched, BrowserUnavailable }
public sealed record SelectionProgress(
    string SelectionId, TargetIdentity Target, SelectionProgressKind Kind);
```

- [ ] `SelectionController` 暴露 `event Action<SelectionProgress>? Progress`。在真正顯示工具列後發送 ToolbarShown；在既有搜尋分支 `launchWeb(destination.Uri!)` 成功返回後發送 SearchLaunched；捕捉搜尋啟動例外時發送 BrowserUnavailable。不讓 UI 訂閱例外改變動作本身的執行結果，使用與現有事件相容的隔離通知方法。
- [ ] 協調器只接受 active 練習工作階段與完整 `TargetIdentity` 相符的事件。完成必須對應該來源的目前選取；取消／重試使舊訂閱失效。瀏覽器接走前景後仍接受已驗證成功事件，不再用新的前景視窗反推來源。
- [ ] 若成功發送過但 pipe 已關閉，保留當次父程序成功狀態，不重啟搜尋、不顯示第二個練習視窗。程序關閉後的遲到事件直接忽略。
- [ ] 使用注入的 `launchWeb` 計數並模擬例外，驗證事件順序、實際 URI 使用原有政策、成功只發一次、錯誤不完成；桌面測試不開真實瀏覽器。重跑 Lifecycle 與 WebActions。建議提交：`feat(onboarding): track real search completion`。

## Task 4：歡迎、練習與完成介面

- [ ] `OnboardingWindow` 使用 ActionUi 既有品牌、卡片、按鈕與 WindowWorkArea；兩種狀態頁為 Welcome、Complete。歡迎主文案與教材依設計文件，所有文字使用 resx key `Onboarding_*`。
- [ ] 歡迎顯示「試一次」「稍後再說」與語言選擇。使用者按試一次才啟動練習；啟動成功後隱藏歡迎。關閉練習視為略過並結束該流程，不額外再跳歡迎視窗。
- [ ] `OnboardingPracticeWindow` 顯示三個步驟、唯讀範例、目前快捷鍵與重試入口；不預選教材、不提供 Copy／Paste 模擬流程。ToolbarShown 更新短提示，SearchLaunched 顯示完成與「繼續」。父程序在使用者按繼續後顯示 Complete 頁，可新增 child -> parent `continue` 固定訊息並同步更新協定測試。
- [ ] 快捷鍵衝突在啟動練習前提供「更換快捷鍵」，開啟既有一般頁錄製入口；保存後返回教學。中途變更鍵位透過 configure 更新練習提示，保存失敗維持原鍵位。
- [ ] Complete 顯示目前自動顯示偏好及系統匣說明；明確改動才保存。語言與自動顯示的保存都回傳成功／失敗給 UI，失敗還原控制項並提示，不讓 UI 顯示未保存的成功狀態。
- [ ] 設定 AutomationId：`OnboardingStart`、`OnboardingSkip`、`OnboardingLanguage`、`OnboardingSample`、`OnboardingProgress`、`OnboardingContinue`、`OnboardingAutomatic`、`OnboardingFinish`、`OnboardingOpenSettings`。狀態變化有可讀 AutomationName／LiveSetting，Tab 順序合理、Esc 不標記完成。
- [ ] 新增 Appearance／Language 案例，確認英／繁文字、動態快捷鍵、小工作區捲動、焦點可見與主要按鈕可達。正常與失敗狀態截圖放 `artifacts/ui-polish/onboarding-*.png`。建議提交：`feat(onboarding): add guided first-use screens`。

## Task 5：首次啟動、重看與退出整合

- [ ] 在 `MainWindow.Onboarding.cs` 集中教學擁有權。主視窗建構前段取得設定存在性與教學記錄，初始化控制器後，首次實際手動顯示觸發一次 welcome；必須使用 Dispatcher 避免 SourceInitialized 內建立視窗引發重入。
- [ ] 歡迎呈現前寫入 Presented；略過寫入 Skipped；搜尋成功寫入 Completed。已 Completed 的重看只在記憶體更新進度，不降級檔案。失敗保存顯示可理解訊息，保留手動重看入口。
- [ ] 背景啟動不顯示教學。`ShowSettings` 被重複手動啟動事件或系統匣觸發時，使用啟動時保存的首次資格；初始化時系統匣建立失敗的回退顯示，不誤算成使用者手動開啟。
- [ ] 「關於與支援」新增使用入門按鈕。已有教學時重看只啟用現有視窗，不建立第二個工作階段。從教學打開設定時保留未保存草稿與既有離頁／退出判定。
- [ ] 正常關閉歡迎／練習視為略過；應用退出先停止教學工作階段再清理 SelectionController。結束教學時，系統匣可用則回背景；不可用則顯示一般設定。App SessionEnding 也執行有界清理，不阻擋 Windows 登出。
- [ ] Lifecycle 案例使用隔離 SettingsStore／OnboardingStore：乾淨首次、舊 schema 8、舊 schema 7、背景後手動開啟、略過後重啟、完成後重啟、教學損壞、不可寫入、系統匣失敗、重複手動啟動、重看、程式退出。每例比對既有快捷鍵、相容性、AI 設定與動作偏好保持。
- [ ] 執行針對性回歸，於 PowerShell 使用陣列：`./scripts/test-desktop.ps1 -Suite Lifecycle,Appearance,Language,WebActions,Compatibility`。預期所有案例成功、沒有遺留練習程序。建議提交：`feat(onboarding): connect first-run lifecycle`。

## Task 6：發布前說明與驗收

- [ ] 更新雙語 README 的第一次使用段落：完整解壓、執行 App、試一次、略過後從關於與支援重看；仍保留瀏覽器選字與目前快捷鍵的操作路徑。
- [ ] 新增唯一繁中 release note，包含 title、summary、1 至 3 項 highlights、usage。說明可以親手練習、隨時重看、不需要 AI 帳號；搜尋需可用瀏覽器，網頁結果需網路。未完成的功能不得寫成已交付。
- [ ] 更新 `docs/development.md` 說明教學測試包含在哪些既有 suites；在 `docs/p0-validation.md` 記錄實際測試日期、來源指紋、報告與截圖路徑，明示尚未執行的人工項目。
- [ ] 執行 `pwsh -NoProfile -File scripts/check.ps1`。預期工具契約、編碼、建置、Core 與 ActionRunner 成功；不以 `dotnet test` 代替自訂 runner。
- [ ] 正常結束 SnapBar 後執行 `pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite Release`。預期現行必要 suites 全數成功，ActionRunner 為嚴格 runtimes 模式且 skipped=0。桌面測試期間避免輸入與複製，不與其他桌面操作並行。
- [ ] 人工驗證 100%、150%、200% DPI、最小工作區、純鍵盤、繁中與英文；驗證真實預設瀏覽器搜尋範例開啟後不被教學搶回焦點。選字後移動焦點、取消與快速重試不得造成錯誤完成或不相關輸入。
- [ ] 記錄首次使用者由歡迎到搜尋啟動的耗時與卡住步驟；未接觸過 SnapBar 的人能獨立完成才算體驗驗收。若無法安排新手測試，明示未驗證，不以工程人員操作替代。
- [ ] 依變更範圍確認所有功能與語言資源隨包可用。只有使用者要求發布時才執行 `pwsh -NoProfile -File scripts/build.ps1 -Publish`，且 Release 證據須符合當前來源；本計畫不要求自動發布或推送。

## 最終核對

- [ ] 首次不看 README 就能理解下一步，教材使用真正工具列。
- [ ] 略過、舊用戶、背景啟動、損壞狀態都有明確而不反覆打擾的行為。
- [ ] 成功只代表搜尋已交給啟動器，沒有誤稱網頁已載入。
- [ ] 安全政策、既有設定、真實使用者資料與剪貼簿不因教學準備而改變。
- [ ] IPC 有界、來源身分完整比對、退出無遺留程序，產品日誌無選字內容。
- [ ] 雙語與 DPI 截圖、相關回歸、完整 Release、使用者說明與尚存限制可供檢閱。
