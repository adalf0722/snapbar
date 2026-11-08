# 開發與驗證

## 日常檢查

在 Windows 的 PowerShell 7 執行。SDK 依 `global.json`，優先使用 `.tools/dotnet/dotnet.exe`。

```powershell
pwsh -NoProfile -File scripts/check.ps1
pwsh -NoProfile -File scripts/install-hooks.ps1
```

`check.ps1` 驗證 Git 管理及未忽略的新 Markdown／PowerShell 檔案的 UTF-8 無 BOM 編碼、PowerShell 語法、差異空白，以及全部 GitHub workflow 的 actionlint，再跑工具契約測試、Release 建置與 Core 測試。建置會把 Probe 和 fixture README 複製到 App 輸出目錄。GitHub Actions 在 Windows 上執行同一入口並保存日誌；互動式桌面測試需在本機執行。

一般 `build` 的 bin 輸出依賴 .NET 10 Desktop Runtime；透過 `.tools/dotnet` 或臨時 `DOTNET_ROOT` 啟動成功，不代表 Explorer 登入啟動也能找到該 runtime。交付與登入啟動驗收使用 `scripts/build.ps1 -Publish` 產生的自含式 `artifacts/p0`。App 的 `--startup-check` 僅驗證程序可載入，不讀寫設定、不建立系統匣或單一執行個體訊號。啟用登入啟動前會清除子程序的 DOTNET／COREHOST 覆寫並從 Windows 系統目錄驗證此入口；發布閘門另以相同乾淨環境檢查 App 與 Probe，失敗不替換舊套件。

actionlint 固定為 1.7.12，首次檢查 workflow 時自動下載至 `.tools/actionlint/1.7.12/`，核對固定 SHA-256；之後使用快取。首次需要網路，下載或 lint 失敗均阻擋提交／CI，不會略過。workflow 與 actionlint 設定也要求有效的 UTF-8 無 BOM。停用選用的 shellcheck／pyflakes 外掛，以固定各機器的檢查範圍；既有 PowerShell parser 檢查仍保留。

安裝器僅設定本 checkout 的 `core.hooksPath=.githooks`，啟用 pre-commit 與 pre-push。若已有其他 hook 設定或傳統 pre-commit／pre-push，會保留並提示手動整合。pre-commit 驗證暫存區版本的編碼、語法與空白；若 workflow 或 `.github/actionlint.yaml`／`.yml` 有變動，另從 Git index 擷取全部 workflow 與設定至獨立快照並執行 actionlint，避免未暫存的修正掩蓋問題。其他檔案提交不需執行 actionlint。完整檢查由日常入口和 CI 執行，包含隔離 bare remote 的 push 流程測試。Commit message 驗證器維持原狀。

pre-push 在每次推送 branch／tag 前執行完整 Release 桌面測試。先提交或 stash 所有修改、正常結束 SnapBar，並在測試期間避免輸入與複製。推送來源必須指向目前 HEAD；其他分支請切換後個別推送。不接受 assume-unchanged／skip-worktree 的追蹤檔案。刪除遠端 ref 或傳送桌面證據 notes ref 不跑桌面測試。測試失敗會擋下 push；通過後先上傳綁定 commit 的精簡 Git note，再繼續原始 push。新 clone 必須重新安裝 hooks。

## 桌面測試

手動試用開發版時，先從系統匣結束 SnapBar，再在 PowerShell 7 執行：

```powershell
./scripts/build.ps1
./scripts/run-dev.ps1
```

`run-dev.ps1` 會確認 App 和 Probe 都在、設定子程序所需的 `DOTNET_ROOT`，並印出實際啟動路徑。若已有 SnapBar 執行中，腳本會提示先正常結束。若要開啟已備妥的隔離輸出，可用 `-AppDirectory` 指定同時包含 App 執行檔和 `Probe` 資料夾的目錄。

Release 開發版建置後，也可在檔案總管雙擊專案根目錄的 `Start-SnapBar.cmd`。啟動失敗時，視窗會保留錯誤訊息。

```powershell
pwsh -NoProfile -File scripts/test-desktop.ps1 -List
pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite Compatibility
pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite Release
# PowerShell 內可以選多個套件
./scripts/test-desktop.ps1 -Suite Clipboard,Diagnostics,Actions
```

入口先確認 Release 輸出未被 App 或 Probe 占用，且 SnapBar 已正常結束，再重新建置並檢查輸入桌面及 Ctrl+Alt+V。測試會暫時使用剪貼簿與前景視窗；執行期間不要輸入或複製內容。入口以互斥鎖拒絕並行桌面測試。需要切到 fixture 的案例會印出 `WAITING` 提示。沒有互動式桌面時應明確失敗，不能當作通過。相容性測試若失去 fixture 前景，會拒絕對其他 App 發起新 capture；輸入干擾日誌只記錄事件種類，不記錄按鍵或文字。

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
| Lookup | `--lookup` | 多詞典分組查詢、停用來源、複製來源與釋義、重複詞頭、釘住與焦點變更。畫面存於 `artifacts/ui-polish/lookup-*.png` |
| Language | `--interface-language` | 雙語資源、語言切換與保存、系統語言回復、隨附項目名稱、工作內容與視窗語言保留。納入 Release 必要證據 |
| AiRuntime / AiPanel | `--ai-runtime` / `--ai-panel` | HTTP／DPAPI、AI 面板與選取寫回；服務回應使用受控 fixture |
| Ocr | `--screen-ocr` | 實體桌面框選、Esc、本機繁中與英文辨識；需要 VC++ x64 執行階段 |

`Release` = Lifecycle + Appearance + CompatibilitySlow + AiRuntime + AiPanel + Lookup。`All` 包含表中所有隔離套件，略過與慢速套件重複的 Compatibility。需要真實 Sublime、瀏覽器或 Explorer 的 smoke 測試未納入自動 profile，需依個案準備環境。`--ai-preview` 可保留可丟棄的 AI 測試視窗供人工檢視，不呼叫真實服務。

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

## GitHub 自動發布

`.github/workflows/release.yml` 支援 push main 自動發布預覽版、其他 branch／tag push 自動打包、手動完整演練與手動 Release 發布，全部建置／打包均使用 GitHub 託管的 `windows-latest`，不再等待本機自架 runner。版本名稱使用 `v0.1.0`、`v0.1.0-rc.1` 這類格式，並指定含有 workflow 的提交。先將 workflow 與腳本合併並推送至 GitHub，再執行；手動觸發入口需要 workflow 已存在於預設分支。

### 一次性本機設定與測試證據

1. Windows 安裝 Git、PowerShell 7 與 `global.json` 指定的 SDK，執行 `scripts/install-hooks.ps1`。推送帳號必須有寫入 repository 與 `refs/notes/snapbar-desktop` 的權限。
2. 在已登入且未鎖定的互動桌面執行 `git push`，先正常結束 SnapBar。完整 Release 測試會在 push 前執行；失敗時修正問題並重新 push。
3. hook 先取得遠端 notes，在獨立暫存 ref 新增該 commit 的通過紀錄，使用非強制 push 傳送；並行更新或網路錯誤均阻擋 branch／tag 推送。重新 push 會重新完整測試，不覆蓋別人的紀錄。

紀錄只含 commit、tree、時間、原始碼指紋、Build／Preflight 及 Release 各套件的成功狀態、exit code、計數與耗時；本機路徑及詳細日誌不隨 note 上傳。雲端 fetch notes 後核對確切 commit／tree 與完整成功結果，再把紀錄匯入為目前乾淨 checkout 的發布報告。原始紀錄另存於檢查 artifact，Git checkout 的 LF／CRLF 差異不影響 commit 身分。

Git hook 可被 `--no-verify` 略過，GitHub 網頁提交或合併也不會執行本機 hook；但缺少該 commit 紀錄時雲端會拒絕打包。請 checkout 該確切 commit，再以已安裝 hook 的一般 branch／tag push 完成驗證。若來源已在遠端，沒有更新的 push 不會重新測試；可新增指向該 commit 的驗證 branch 或 tag 並推送，再重跑原工作。測試紀錄不是簽章，不能防止有 repository 寫入權限的人偽造紀錄。由於 notes 先送出，原始 push 最後失敗或使用 `git push --dry-run` 時仍可能留下測試紀錄；不會因此自行產生套件。原有自架 runner 可自行停止，本次不自動移除註冊。

### 每次 push main 自動發布

`git push origin main` 通過本機桌面測試後，雲端核對該 commit 的 note，執行完整 repository checks、自含式發布、Probe／Startup smoke 與 ZIP 核對。最後自動建立預覽版 Release，附上 ZIP、SHA-256 與從版本庫彙整的使用者更新說明，一般使用者直接到 Releases 下載。

自動版本固定使用 `v0.1.0-rc.N`，取遠端 tags 與 Releases（含草稿）內同系列最大的整數 N 加一；現有 rc.4 後為 rc.5、rc.6。不同系列與正式版不參與比較，改變系列需另行調整腳本。main push 工作依 concurrency group 序列執行；GitHub 預設只保留一個等待工作，密集連續 push 可能使尚未開始的舊工作被較新的 push 取代。正在執行的工作不會被取消。只有最後發布工作具有 contents write，不需額外 PAT。

發布頁先顯示繁中更新價值及使用方法，再提供直接下載、更新步驟與必要限制；來源 SHA、驗證資訊及完整比較連結放在折疊區。桌面或套件變更需新增 `release-notes/<date>-<topic>.json`，格式與撰寫規則見 [使用者更新說明](../release-notes/README.md)。版本選擇階段以完整 Git 歷史和前一公開 tag 檢查說明，缺漏會在打包前失敗；實際發布亦檢查。純網站、文件或工具變動會明確說明沒有桌面功能更新。既有公開 Release 的文字不會被本次修改追溯覆寫。

發布先建立對應確切 commit 的草稿，完整上傳兩個附件並核對 digest 後才公開，避免只有原始碼或部分附件的公開版本。整條流程在同一個 workflow 完成；GITHUB_TOKEN 建立的 Release 不會再次觸發 release 事件，避免重複打包。

同一 workflow run 透過 Release 說明中的 run ID 與來源 SHA 標記識別。請優先使用 Re-run failed jobs，沿用原始 artifact，缺少的附件會補傳，既有附件必須名稱、大小、SHA-256 全部相符才略過。其他來源／run 占用版本或附件不同時拒絕覆寫。完整重跑會沿用同 run 已建立的版本；若重新建置使 ZIP 位元組不同，會拒絕替換既有下載。人工操作剛好搶先建立同一版本時，請重跑整條流程，讓版本解析重新選擇下一號。

其他 branch／tag push 維持只產生 Actions 的 `windows-release-package` artifact，不公開 Release；版本為 `v0.0.0-ci.<run number>.g<commit 前綴>`。推送後必須保持工作區乾淨，下一次提交會要求自己的桌面測試證據。

### 先做完整演練

在 GitHub 的 Actions → Publish Windows release → Run workflow，選擇分支，版本預設 `v0.0.0-validation`。也可使用已登入的 GitHub CLI：

```powershell
gh workflow run release.yml --repo adalf0722/snapbar --ref main -f version=v0.0.0-validation
```

演練與正式發布共用 package、verify 工作：核對 push 前的桌面證據、全部 repository checks、自含式打包、artifact 上傳至 GitHub，再由另一個 runner 下載並核對 SHA-256。成功後可在該次 Actions 頁面下載 `windows-release-package`；摘要包含來源 commit、版本與校驗碼。演練不建立 tag／Release，不上傳 Release 附件，upload 工作會明確顯示 skipped。

所有事件均 checkout 觸發時的 commit SHA；分支後續移動不會改變此次來源。同一 commit 可重用 push 前的桌面紀錄；不同 commit 即使只有文件變動，也必須先完成該 commit 的 pre-push 驗證。不要在 GitHub 建立未經本機 push 驗證的新來源 commit 後直接發布。

### 每次發布

main 的日常預覽版直接使用上方自動流程即可。若需特定正式版本或自行指定 tag，可先以一般 push 將已測試來源送到 GitHub，再以該 commit 建立新版本並按 Publish release。流程先以 `import-desktop-evidence.ps1` 核對既有桌面 note，再執行 `check.ps1`、`package-release.ps1 -Version <tag> -DesktopReport <匯入報告>`。發布腳本沿用目前原始碼指紋、桌面測試證據、App／Probe self-contained 發布、Probe／Startup smoke 門檻。沒有相符紀錄或任一步失敗時停止，不會附加套件；不在雲端重跑互動桌面測試。

package 與 verify 只有 contents read，checkout 不保留 Git 憑證。ZIP 經 Actions artifact 傳至 GitHub 託管的 Windows runner，重新核對 SHA-256；成功後，只有 Release 事件才啟動最後的 upload 工作，以該工作專用的 contents write token 上傳相同 artifact 的套件。手動演練不會啟動這個具有寫入權限的工作。無需設定 PAT 或額外發布 secret。

產物為 `SnapBar-v<版本>-win-x64.zip` 與同名 `.zip.sha256`。完整解壓縮後根目錄可找到 `SnapBar.App.exe` 與 `Probe/`。套件仍未簽章；AI 金鑰、使用者設定與 Node.js 不隨套件提供。

雲端檢查日誌與匯入桌面紀錄保存在 workflow 的 `release-check-logs` artifact（14 天），詳細互動桌面日誌保留本機；套件 artifact 保留 7 天，Release 附件不受此保留期影響。失敗可從 Actions 重跑；上傳失敗優先選 Re-run failed jobs，沿用本次已驗證的套件。超過 artifact 保留期需重跑整個流程。上傳不使用 `--clobber`，不會刪除舊附件；若一次上傳僅成功部分附件，請先核對已上傳檔案，僅補上缺少的檔案。啟用 immutable releases 的 repository 需另設草稿上傳流程，本 workflow 適用可在發布後新增附件的 Release。

本機也可執行同一入口：

```powershell
pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite Release
pwsh -NoProfile -File scripts/package-release.ps1 -Version v0.1.0
```

打包會拒絕缺少必要 App／Probe 或執行環境檔案、空檔案、符號連結及不合法版本。每次使用新的 artifacts 目錄；完成 ZIP 結構檢查後才輸出套件路徑與校驗碼，不覆蓋既有 ZIP。

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
