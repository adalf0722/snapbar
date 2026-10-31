# 開發與驗證

## 日常檢查

在 Windows 的 PowerShell 7 執行。SDK 依 `global.json`，優先使用 `.tools/dotnet/dotnet.exe`。

```powershell
pwsh -NoProfile -File scripts/check.ps1
pwsh -NoProfile -File scripts/install-hooks.ps1
```

`check.ps1` 驗證 Git 管理及未忽略的新 Markdown／PowerShell 檔案的 UTF-8 無 BOM 編碼、PowerShell 語法、差異空白，再跑工具契約測試、Release 建置與 Core 測試。建置會把 Probe 和 fixture README 複製到 App 輸出目錄。GitHub Actions 在 Windows 上執行同一入口並保存日誌；互動式桌面測試需在本機執行。

安裝器僅設定本 checkout 的 `core.hooksPath=.githooks`。若已有其他 hook 設定或 pre-commit，會保留並提示手動整合。pre-commit 只驗證暫存區版本的編碼、語法與空白，避免未暫存的修正掩蓋問題。完整檢查由日常入口和 CI 執行。Commit message 驗證器維持原狀。

## 桌面測試

```powershell
pwsh -NoProfile -File scripts/test-desktop.ps1 -List
pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite Compatibility
pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite Release
# PowerShell 內可以選多個套件
./scripts/test-desktop.ps1 -Suite Clipboard,Diagnostics,Actions
```

入口每次先重新建置，再確認目前是可操作的輸入桌面且 Ctrl+Alt+V 可註冊。請先從系統匣或設定正常結束 SnapBar。測試會暫時使用剪貼簿與前景視窗；執行期間不要輸入或複製內容。入口以互斥鎖拒絕並行桌面測試。需要切到 fixture 的案例會印出 `WAITING` 提示。沒有互動式桌面時應明確失敗，不能當作通過。相容性測試若失去 fixture 前景，會拒絕對其他 App 發起新 capture；輸入干擾日誌只記錄事件種類，不記錄按鍵或文字。

| 套件 | 原生旗標 | 範圍 |
| --- | --- | --- |
| Lifecycle | `--tray-lifecycle` | 系統匣、開關、熱鍵與儲存設定 |
| Appearance | `--ui-appearance` | 外觀、捲動與拖曳把手 |
| Compatibility | `--clipboard-compatibility` | 快速剪貼簿相容性與焦點安全 |
| CompatibilitySlow | `--clipboard-compatibility-long` | 相容性全套，包含真正等待 35 秒的腳本 |
| Clipboard / ClipboardRaces | `--clipboard-roundtrip` / `--clipboard-races` | 格式還原與競態 |
| UiaReadiness / UiaDeepTree | `--uia-readiness` / `--uia-deep-tree` | 延遲 provider 與深層樹 |
| Actions / ActionRunner / Diagnostics | `--custom-actions` / `--custom-action-runner` / `--action-diagnostics` | 自訂動作與結果 |
| Layout / Keyboard | `--action-layout` / `--menu-keyboard` | 動作列與鍵盤 |
| PathWorker / PathActions | `--path-worker-tests` / `--path-actions` | 路徑 worker 與動作 |
| WebActions / Exclusions | `--web-actions` / `--app-exclusions` | 網址與排除政策 |

`Release` = Lifecycle + Appearance + CompatibilitySlow。`All` 包含表中所有隔離套件，略過與慢速套件重複的 Compatibility。需要真實 Sublime、瀏覽器或 Explorer 的 smoke 測試未納入自動 profile，需依個案準備環境。

原生 runner 可以用以下命令列舉所有旗標；未知或多個測試旗標回傳非零。

```powershell
.tools/dotnet/dotnet.exe tests/SnapBar.Windows.Tests/bin/Release/net10.0-windows/SnapBar.Windows.Tests.dll --list-tests
```

外部 smoke 旗標為 `--sublime-action`、`--browser-launch-smoke`、`--path-shell-smoke`。直接執行需先設定 `$env:DOTNET_ROOT` 為所用 dotnet 所在目錄，並確認外部應用與可丟棄的 fixture 已準備。完整參數請查對應 `SublimeActionTest.cs`、`BrowserLaunchTest.cs`、`PathShellSmokeTest.cs`；它們可能啟動外部應用、操作焦點或剪貼簿。

每次執行保存在 `artifacts/checks/desktop/<UTC時間>-<識別碼>/`：各階段 `.log` 與 `report.json` 包含 exit code、通過／失敗數、耗時和原始碼指紋。`latest.json` 是最近一次執行的副本，失敗也會更新。建置及工具測試分別保存在 `artifacts/checks/build/`、`artifacts/checks/tooling/`。這些路徑均被 Git 忽略。Fixture 直接使用本次建置的 App／Probe，不依賴舊 `artifacts/p0`。

## 發佈

```powershell
pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite Release
pwsh -NoProfile -File scripts/build.ps1 -Publish
# 指定之前保留的完整報告
pwsh -NoProfile -File scripts/build.ps1 -Publish -DesktopReport artifacts/checks/desktop/<run>/report.json
```

發佈在建置前檢查套件內 App／Probe 程序，含沒有視窗的系統匣程序。腳本不會強制終止它們。Release 桌面報告必須成功並符合目前原始碼指紋；修改 `src/`、`tests/`、`scripts/`、workflow、hooks 或根目錄建置設定後需重跑。文件變更不使二進位測試證據失效。

App 與 Probe 先發佈到全新 `artifacts/publish/<識別碼>/package`，通過 Probe 協定 smoke 後，再檢查程序與指紋並以目錄搬移切換。舊套件保留在同次 `previous`，第二次搬移失敗會嘗試回復舊目錄；搬移、備份路徑必須在本 repository 的 artifacts 內，且不能穿過 junction／symlink。任一步失敗不會以部分新檔覆寫既有套件。保存的報告是本機回歸證據，不是簽章或防竄改證明；跨應用程式、DPI 與真實長腳本仍需按變更內容判斷驗證範圍。

## 找程式與審查

| 需求 | 入口 |
| --- | --- |
| 動作編輯與 UI | `src/SnapBar.App/ActionEditorWindow.cs`、`Themes/Controls.xaml` |
| 自訂動作契約 | `src/SnapBar.Core/CustomActions.cs`、`ActionCatalog.cs` |
| 選取安全與路由 | `src/SnapBar.Core/Policies.cs`、`SelectionRoutingPolicy.cs` |
| Clipboard 候選期限 | `src/SnapBar.Core/ClipboardCandidateLease.cs`、`src/SnapBar.Probe/SelectionReader.cs` |
| 執行與延長／釋放租期 | `src/SnapBar.App/SelectionController.CustomActions.cs`、`SelectionController.Compatibility.cs` |
| 桌面原生測試入口 | `tests/SnapBar.Windows.Tests/Program.cs` |

Core 以注入的 `TimeProvider` 驗證期限邊界，無需真實等待；慢速桌面案例則驗證跨程序整合。改動 timeout、lease 或結果套用時，一併檢視 Core、Windows、Probe、App 的上限、清理與取消。改動相容模式須保留密碼拒絕、前景／焦點／候選身分驗證、明確無選取的拒絕，以及舊結果取消；不可只檢查 UI 開關。一般相容模式預設啟用並遵循已儲存偏好，Sublime 初次寫入快捷鍵仍需明確設定。

Review 應指出可重現的使用情境、原因與影響，先處理高／中風險，修正後依行為重跑相關套件。跨應用與 DPI 人工驗證記錄在 `docs/p0-validation.md`；PR 說明行為改變、已做驗證及尚未涵蓋的條件，UI 改動附截圖。
