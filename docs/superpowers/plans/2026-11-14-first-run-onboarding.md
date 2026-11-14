# SnapBar 首次使用體驗實作計畫

> 執行方式：在目前 checkout 依序實作與檢查。使用者以「go」授權實作，單一練習畫面與首次啟動整合已完成；下列項目保留為原驗收清單，實際通過範圍與限制以執行紀錄為準。

**Goal:** 首次手動開啟即可透過真實選字與搜尋理解 SnapBar，並能略過、重看和安心退回系統匣。

**Architecture:** App 協調器連接單一獨立練習視窗與搜尋結果事件；先從手動入口串通真正選字搜尋，再驗證新手體驗，最後加入 Core 教學保存與首次呈現政策。沿用現有 Probe 與安全流程，不放寬自身程序選字限制。

**Tech Stack:** .NET 10、WPF、現有自訂 console test runners、System.IO.Pipes、英文／繁中 resx、PowerShell 7。

**設計依據：** [首次使用體驗設計](../specs/2026-11-14-first-run-onboarding-design.md)。依序完成下列工作單元；提交前只納入該單元檔案，不推送或發布。不新增歡迎頁、完成頁、OnboardingWindow、toolbar-shown 或 continue 訊息。

## 執行紀錄（2026-11-14）

- [x] 單一獨立練習程序、受限通訊、取消與清理，並接入真正 UIA／工具列搜尋。
- [x] 原處完成、雙語與目前快捷鍵、偏好保存回復與過期回應拒絕。
- [x] 獨立教學狀態保存、完成不降級、背景後首次手動呈現、重看與舊用戶保留。
- [x] 略過返回既有設定視窗；完成後「開始使用」仍回到系統匣。兩種退場行為由獨立桌面斷言覆蓋。
- [x] 新增 Core、Lifecycle、Appearance、Language 案例、雙語 README 與唯一發布說明；桌面報告及截圖見 [驗證紀錄](../../p0-validation.md)。
- [ ] 未使用過 SnapBar 的真人試做與一分鐘目標量測。尚無受試者，不以工程測試代替；不阻擋其他工程工作。
- [ ] 100%、150%、200% 實際顯示器人工驗收與真實預設瀏覽器端到端試做。已涵蓋程式化小視窗截圖與注入啟動器的真實工具列搜尋，不把它們稱作真人／多 DPI 驗收。
- [ ] 套件發布。這次未要求發布或推送。

## 檔案責任與依賴

| 檔案 | 變更與責任 |
| --- | --- |
| `src/SnapBar.Core/OnboardingState.cs` | 新增狀態、讀取結果與首次呈現純政策 |
| `src/SnapBar.Core/OnboardingStore.cs` | 新增獨立 JSON 保存、大小／版本檢查與原子替換 |
| `src/SnapBar.App/OnboardingCoordinator.cs` | 新增主程序工作階段與狀態轉移 |
| `src/SnapBar.App/OnboardingPracticeSession.cs` | 新增 pipe、子程序啟動與有界清理 |
| `src/SnapBar.App/OnboardingProtocol.cs` | 新增受限訊息契約與解析 |
| `src/SnapBar.App/OnboardingPracticeWindow.cs` | 單一正式練習視窗，原處顯示完成／偏好；使用 ActionUi 現有樣式 |
| `src/SnapBar.App/Program.cs` | 新增練習分支，維持既有背景與單一執行個體行為 |
| `src/SnapBar.App/MainWindow.Onboarding.cs` | 新增局部類別，集中與主視窗整合 |
| `src/SnapBar.App/MainWindow.cs`、`MainWindow.General.cs`、`MainWindow.Settings.cs`、`MainWindow.Exit.cs` | 串接初始判定、手動顯示、保存偏好、入口與退出清理 |
| `src/SnapBar.App/SettingsSupportPage.cs` | 新增「使用入門」獨立按鈕，不藏在診斷 expander |
| `src/SnapBar.App/SelectionController.cs` | 新增搜尋啟動成功／失敗結構化事件，不追蹤工具列顯示 |
| `src/SnapBar.App/Resources/Strings.resx`、`Strings.zh-Hant.resx` | 新增全部使用者文案與可及性名稱 |
| `tests/SnapBar.Tests/OnboardingTests.cs`、`Program.cs` | 新增並註冊純規則、保存與身分比對案例 |
| `tests/SnapBar.Windows.Tests/OnboardingTest.cs`、`Program.cs` | 新增桌面整合案例，接入既有 Lifecycle／Appearance／Language 旗標 |
| `README.md`、`README.en.md`、`docs/development.md`、`docs/p0-validation.md` | 更新首次使用與驗證紀錄 |
| `release-notes/2026-11-14-first-run-onboarding.json` | 實作完成時新增繁中使用者說明；若檔名已存在另取唯一名稱 |

不新增桌面 suite 或發布證據格式，將案例納入現有必跑套件，避免同步變更多個發布閘門。實作前確認相關類別的實際測試入口與退出流程，局部加入呼叫，不重構其他設定功能。

## Task 1：受限的獨立練習程序

- [ ] 在桌面測試加入協定案例：超過 4 KiB、非 JSON、未知版本、重複連線、未回報 HWND、錯誤 HWND、父程序斷線、5 秒連線期限、重試工作階段取代舊工作階段；先確認對應失敗。
- [ ] `OnboardingProtocol.cs` 使用以下訊息結構，解析後依 kind 限制可出現欄位，不接受任意命令或檔案路徑：

```csharp
internal sealed record OnboardingMessage(
    int Version, string Kind, long Window = 0,
    string? Language = null, string? Shortcut = null,
    long RequestId = 0, bool? Automatic = null,
    bool? HotkeyRegistered = null, string? Error = null);
// child -> parent: ready(Window), skip, finish, open-settings,
//                 change-shortcut, set-language(RequestId, Language),
//                 set-automatic(RequestId, Automatic)
// parent -> child: configure(Language, Shortcut, Automatic, HotkeyRegistered),
//                 search-launched, search-failed(Error),
//                 preference-result(RequestId, Language, Automatic, Error), close
```

- [ ] `OnboardingPracticeSession` 建立 GUID 名稱的單連線 `NamedPipeServerStream`，指定 `PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous`。以 `ProcessStartInfo.ArgumentList` 啟動目前 App 的 `--onboarding-practice` 分支。訊息讀取逐段計算位元組，超限即拒絕，不能先無限 `ReadLineAsync` 再檢查長度。
- [ ] 父程序持有 `Process` 物件，透過 Native 身分確認 ready HWND 的 PID 與啟動時間都符合該子程序。5 秒未 ready 或子程序退出，回到可見設定並顯示「無法開啟練習，請再試一次」，保留使用入門重試入口。
- [ ] `Program.cs` 在 fixture／單一執行個體之前辨識完整練習參數。練習程序只建立 WPF、pipe 與 `OnboardingPracticeWindow`；ready 後等待 configure 才啟用練習內容，所有 WPF 更新經 Dispatcher。
- [ ] 第一個開發版本從「關於與支援 → 使用入門」手動開啟，不接自動首次判定、不寫教學狀態。建立最小可操作 UI：雙語標題、唯讀教材、目前快捷鍵與略過。主設定在練習呈現後隱藏；正常情況只存在一個教學視窗。重複使用入門只啟用同一練習程序。
- [ ] 實作取消與退出：先取消讀取／解除訂閱、送 close、等待最多 2 秒；僅在持有子程序身分仍吻合時結束該練習程序。子程序偵測父連線消失後結束。每次重試使用新 pipe，先清理前次工作階段。
- [ ] 以既有 Lifecycle 桌面 runner 執行程序隔離與清理案例，確認真實使用者偏好與 clipboard 未被測試準備覆寫。建議提交：`feat(onboarding): isolate practice sessions`。

## Task 2：真實操作進度與完成判定

- [ ] 新增案例：同 PID 不同啟動時間、不同 HWND、其他 App 搜尋、舊工作階段成功、搜尋驗證失敗、瀏覽器例外、只有工具列顯示，都不得完成；正確練習來源搜尋成功才完成。
- [ ] 在 App 定義不含文字的搜尋專用通知契約，不把教學 UI 概念放入 Core。SelectionId 為既有選取的 Id，練習工作階段另由協調器持有的連線／訂閱識別：

```csharp
internal sealed record OnboardingSearchResult(
    string SelectionId, TargetIdentity Target, bool Launched);
// SelectionController 內：
internal event Action<OnboardingSearchResult>? OnboardingSearchFinished;
```

- [ ] 在既有 Search 分支 `launchWeb(destination.Uri!)` 成功返回後發送 Launched=true；捕捉搜尋啟動例外時發送 false。不將 OpenLink 算成搜尋，不新增工具列顯示事件。隔離 UI 訂閱例外，不改變動作結果、不重複啟動搜尋。
- [ ] 協調器只接受 active 練習工作階段與完整 `TargetIdentity` 相符的事件。完成必須對應該來源的目前選取；取消／重試使舊訂閱失效。瀏覽器接走前景後仍接受已驗證成功事件，不再用新的前景視窗反推來源。
- [ ] 若成功發送過但 pipe 已關閉，保留當次父程序成功狀態，不重啟搜尋、不顯示第二個練習視窗。程序關閉後的遲到事件直接忽略。
- [ ] 使用注入的 `launchWeb` 計數並模擬例外，驗證事件順序、實際 URI 使用原有政策、成功只發一次、錯誤不完成；桌面測試不開真實瀏覽器。重跑 Lifecycle 與 WebActions。建議提交：`feat(onboarding): track real search completion`。
- [ ] 此單元即接上最小原處完成回饋；使用入門開啟後能親手選字、按快捷鍵、點真正搜尋並收到完成狀態。尚未保存教學進度，僅供開發驗證；來源驗證、通訊界限、取消及清理已具備，不延至發布前補做。

## Task 3：原處完成與偏好介面

- [ ] `OnboardingPracticeWindow` 沿用 ActionUi 與 WindowWorkArea，開啟即顯示三個短指示、唯讀教材與目前快捷鍵，不預選文字。所有文字使用雙語 resx key `Onboarding_*`；不增加起始按鈕或步驟打勾事件。
- [ ] 保留「稍後再說」、語言與關閉；關閉未完成練習視為略過，不跳出另一個視窗。此階段狀態先存在記憶體，後續保存單元才接持久化。
- [ ] 搜尋成功直接在原畫面展開完成回饋、使用偏好及系統匣說明，顯示「開始使用／開啟設定」。不新增「繼續」或額外完成頁，也不搶回瀏覽器前景。
- [ ] 快捷鍵衝突在原練習畫面提供「更換快捷鍵」，明確操作後開啟既有一般頁錄製入口。保留練習工作階段，保存後可從使用入門返回同一視窗；鍵位變更透過 configure 更新提示，保存失敗維持原鍵位。
- [ ] 完成區顯示目前自動顯示偏好；明確改動才經父程序保存。語言與開關請求帶遞增 RequestId，送出後暫時停用控制項；父程序回傳實際值及固定錯誤碼，失敗還原並提示。忽略過期回應，不允許子程序直接讀寫設定。
- [ ] 設定 AutomationId：`OnboardingSkip`、`OnboardingLanguage`、`OnboardingSample`、`OnboardingProgress`、`OnboardingChangeShortcut`、`OnboardingAutomatic`、`OnboardingFinish`、`OnboardingOpenSettings`。狀態變化有可讀 AutomationName／LiveSetting，Tab 順序合理、Esc 不標記完成，成功後關閉不降級為略過。
- [ ] 新增 Appearance／Language／Lifecycle 案例，確認英／繁文字、動態快捷鍵、小工作區捲動、焦點可見與主要按鈕可達，並驗證偏好保存失敗回復及過期 RequestId 不覆寫新狀態。正常與失敗狀態截圖放 `artifacts/ui-polish/onboarding-*.png`。執行 `./scripts/test-desktop.ps1 -Suite Lifecycle,Appearance,Language`，預期全部通過。建議提交：`feat(onboarding): simplify practice completion`。

## 體驗驗證：在自動呈現與保存之前

- [ ] 讓未使用過 SnapBar 的人從已可操作的單一練習畫面開始，不預先解說快捷鍵或工具列。記錄從畫面出現到搜尋交給啟動器的時間、卡住位置及是否需要口頭協助，不記錄選字內容、不新增遙測服務。
- [ ] 約一分鐘是目標，沒有測試不得宣稱達成。若無新手可試做，在 `docs/p0-validation.md` 明示尚未驗證及工程自測觀察，不以工程人員操作替代新手結果；其他獨立工程項目可繼續。
- [ ] 依實際障礙修正文案、焦點或排版，重跑受影響案例，不先新增頁面、動畫或逐步打勾機制。這個手動版本尚缺首次政策與持久化，不得當作完整首次體驗發布。

## Task 4：首次判定與持久化

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

## Task 5：首次啟動、重看與退出整合

- [ ] 在 `MainWindow.Onboarding.cs` 集中教學擁有權。主視窗建構前段取得設定存在性與教學記錄，初始化控制器後，首次實際手動顯示直接開啟練習；必須使用 Dispatcher 避免 SourceInitialized 內建立視窗引發重入。
- [ ] 練習 ready 後、通知顯示前嘗試寫入 Presented；啟動失敗不寫入。略過寫入 Skipped；搜尋成功寫入 Completed。已 Completed 的重看或關閉不降級檔案。失敗保存顯示可理解訊息，保留手動重看入口。
- [ ] 背景啟動不顯示教學。`ShowSettings` 被重複手動啟動事件或系統匣觸發時，使用啟動時保存的首次資格；初始化時系統匣建立失敗的回退顯示，不誤算成使用者手動開啟。
- [ ] 「關於與支援」新增使用入門按鈕。已有教學時重看只啟用現有視窗，不建立第二個工作階段。從教學打開設定時保留未保存草稿與既有離頁／退出判定。
- [ ] 正常關閉未完成練習視為略過；應用退出先停止教學工作階段再清理 SelectionController。結束教學時，系統匣可用則回背景；不可用則顯示一般設定。App SessionEnding 也執行有界清理，不阻擋 Windows 登出。
- [ ] Lifecycle 案例使用隔離 SettingsStore／OnboardingStore：乾淨首次、舊 schema 8、舊 schema 7、背景後手動開啟、略過後重啟、完成後重啟、教學損壞、不可寫入、系統匣失敗、重複手動啟動、重看、程式退出。每例比對既有快捷鍵、相容性、AI 設定與動作偏好保持。
- [ ] 執行針對性回歸，於 PowerShell 使用陣列：`./scripts/test-desktop.ps1 -Suite Lifecycle,Appearance,Language,WebActions,Compatibility`。預期所有案例成功、沒有遺留練習程序。建議提交：`feat(onboarding): connect first-run lifecycle`。

## Task 6：發布前說明與驗收

- [ ] 更新雙語 README 的第一次使用段落：完整解壓、執行 App 後直接選範例、按目前快捷鍵並搜尋，略過後從關於與支援重看；仍保留在其他 App 選字的操作路徑。
- [ ] 新增唯一繁中 release note，包含 title、summary、1 至 3 項 highlights、usage。說明可以親手練習、隨時重看、不需要 AI 帳號；搜尋需可用瀏覽器，網頁結果需網路。未完成的功能不得寫成已交付。
- [ ] 更新 `docs/development.md` 說明教學測試包含在哪些既有 suites；在 `docs/p0-validation.md` 記錄實際測試日期、來源指紋、報告與截圖路徑，明示尚未執行的人工項目。
- [ ] 執行 `pwsh -NoProfile -File scripts/check.ps1`。預期工具契約、編碼、建置、Core 與 ActionRunner 成功；不以 `dotnet test` 代替自訂 runner。
- [ ] 正常結束 SnapBar 後執行 `pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite Release`。預期現行必要 suites 全數成功，ActionRunner 為嚴格 runtimes 模式且 skipped=0。桌面測試期間避免輸入與複製，不與其他桌面操作並行。
- [ ] 人工驗證 100%、150%、200% DPI、最小工作區、純鍵盤、繁中與英文；驗證真實預設瀏覽器搜尋範例開啟後不被教學搶回焦點。選字後移動焦點、取消與快速重試不得造成錯誤完成或不相關輸入。
- [ ] 確認前述體驗驗證的紀錄適用最終介面；若修訂造成流程改變，重新試做。未接觸過 SnapBar 的人能獨立完成才算體驗驗收；沒有新手測試則仍明示未驗證。
- [ ] 依變更範圍確認所有功能與語言資源隨包可用。只有使用者要求發布時才執行 `pwsh -NoProfile -File scripts/build.ps1 -Publish`，且 Release 證據須符合當前來源；本計畫不要求自動發布或推送。

## 最終核對

- [ ] 首次不看 README 就能理解下一步，教材使用真正工具列。
- [ ] 開啟即能選字、搜尋後原處完成，沒有起始／繼續轉場；先完成真實操作與體驗驗證，再加入首次政策及保存。
- [ ] 略過、舊用戶、背景啟動、損壞狀態都有明確而不反覆打擾的行為。
- [ ] 成功只代表搜尋已交給啟動器，沒有誤稱網頁已載入。
- [ ] 安全政策、既有設定、真實使用者資料與剪貼簿不因教學準備而改變。
- [ ] IPC 有界、來源身分完整比對、退出無遺留程序，產品日誌無選字內容。
- [ ] 雙語與 DPI 截圖、相關回歸、完整 Release、使用者說明與尚存限制可供檢閱。
