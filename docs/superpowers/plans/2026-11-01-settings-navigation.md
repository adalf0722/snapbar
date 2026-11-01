# Settings Navigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 實作已核准的左側設定導覽、AI 命令管理、草稿與退出流程。

**Architecture:** MainWindow 保留生命週期及控制器，內容拆成可嵌入的 WPF 元件。AI 命令政策放 Core，共享 AI 設定與單次刪除復原放 AiPreferences；獨立視窗重用同一內容。使用現有 checkout，按實作、審查、桌面驗證的順序完成。

**Tech Stack:** .NET 10、WPF、既有 Core／Windows console runners、PowerShell 7。

## Global Constraints

- 需求以 `docs/superpowers/specs/2026-11-01-settings-navigation-design.md` 為準，不修改 schema 或選取寫回權限。
- 工作區限制優先；一般頁標準配置無垂直捲軸，長內容僅局部捲動。
- AI 管理按 Prompts 儲存順序；搜尋與分類同時生效，不變更執行面板既有排序。
- 刪除復原合併最新集合；同 ID、128 筆上限、保存失敗保留復原提示。
- 切頁及收至系統匣保留草稿；真正退出才一次處理待提交變更。金鑰不寫入日誌或草稿檔案。
- Markdown 以 UTF-8 無 BOM 寫入並回讀驗證；桌面測試串行，避免與剪貼簿操作並行。

## File Map

- `src/SnapBar.Core/AiPromptCatalog.cs`：管理篩選、穩定順序、刪除／復原政策。
- `tests/SnapBar.Tests/AiRuntimeTests.cs`：上述純政策案例。
- `src/SnapBar.App/MainWindow.cs` 與 `MainWindow.*.cs`：設定宿主、六分類、工作區限制、退出協調。
- `src/SnapBar.App/ActionSettingsPanel.cs`、`ActionSettingsWindow.cs`：共享動作內容及獨立宿主。
- `src/SnapBar.App/AiSettingsPanel.cs`、`AiSettingsWindow.cs`：服務草稿、提交、捨棄與共享宿主。
- `src/SnapBar.App/AiCommandsPanel.cs`、`AiPreferences.cs`：命令管理與共享復原。
- `src/SnapBar.App/SearchSettingsPanel.cs`：明確未儲存與捨棄。
- `src/SnapBar.App/AiPanelWindow*.cs`、`AiPromptEditorWindow.cs`：導覽入口、共享刪除復原、編輯草稿註冊。
- `tests/SnapBar.Windows.Tests/SettingsNavigationTest.cs`、`UiAppearanceTest.cs`：實際 WPF 互動、尺寸與截圖；由 Appearance 套件串行執行。
- `docs/p0-validation.md`：記錄執行證據與限制。

## Task 1: Implement policy and WPF settings experience

- [ ] 先新增 Core 行為測試，涵蓋管理順序不受 recents／pins 影響、分類加搜尋、刪除後對最新集合復原、ID 衝突、容量及釘選位置。

```csharp
var settings = new AiSettings();
var removed = AiPromptCatalog.Delete(settings, "improve", out var deletion);
var restored = AiPromptCatalog.Restore(removed, deletion);
bool preserved = restored.Prompts.Select(p => p.Id).SequenceEqual(settings.Prompts.Select(p => p.Id));
```

- [ ] 實作以下 Core 合約，透過 AiPolicy.Normalize 保持所有既有驗證；失敗擲 ArgumentException，UI 決定訊息。

```csharp
public sealed record DeletedAiPrompt(AiPrompt Prompt, int Index, int PinnedIndex);
public static class AiPromptCatalog
{
    public static IReadOnlyList<AiPrompt> Search(AiSettings settings, string query, string? category = null);
    public static AiSettings Delete(AiSettings settings, string id, out DeletedAiPrompt deletion);
    public static AiSettings Restore(AiSettings settings, DeletedAiPrompt deletion);
}
```

- [ ] 實作 File Map 中的 WPF 元件；內容嵌入 UserControl，獨立 Window 只提供 Owner、標題及關閉行為。六頁保持單一有效內容實例，切頁只改可見性。服務與搜尋用 IsDirty、TrySave、Discard、Changed 通知；服務提交從最新 AiPreferences.Settings 合併欄位，保存失敗保留草稿。
- [ ] AiPreferences 統一命令編輯、釘選、刪除與復原，只有 TrySave 成功才更新復原紀錄。命令編輯器註冊退出協調並清理註冊；主視窗所有明確退出入口使用同一協調器。
- [ ] 主視窗六分類、一般預覽、動作／搜尋子頁、相容性及支援內容依規格整合；狀態及錯誤固定可達。既有控制器管理入口路由至同一宿主；AI 面板未綁宿主時允許獨立共享視窗。
- [ ] 建置，執行 Core runner，完成實作自審並提交。用 `pwsh -NoProfile -File scripts/build.ps1`，預期 Build 與 Core 均 exit 0。

## Task 2: Review implementation and resolve findings

- [ ] 產生 Task 1 diff package，交由獨立 reviewer 同時檢查規格符合與程式品質；檢查 pending secrets、partial save、shutdown、恢復合併及 UI 宿主焦點。
- [ ] 修正所有重要發現，重跑受影響測試，重新審查；不得以改規格跳過已核准需求。

## Task 3: Desktop verification and delivery

- [ ] 更新舊 Appearance 整頁捲動斷言為固定宿主及可達性檢查。新增 SettingsNavigationTest 並從 Appearance 呼叫，使用臨時 SettingsStore 和受控假 AI 設定，不呼叫真實服務。

```csharp
Check(main.Content is not ScrollViewer, "settings root does not scroll");
Check(main.ActualHeight <= SystemParameters.WorkArea.Height + 1, "settings fits work area");
```

- [ ] 驗證六頁可見性、一般頁無需捲動、搜尋草稿切頁／隱藏保留、捨棄、服務未提交不改生效值、保存錯誤、命令篩選／復原及無服務入口；儲存每頁 WPF 截圖。
- [ ] `pwsh -NoProfile -File scripts/check.ps1`，預期工具契約、Release build、Core 全部成功。
- [ ] 桌面套件串行：`pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite Release,Actions,Exclusions,Layout`；有失敗則依實際輸出修正並只重跑受影響範圍。執行期間不並行操作剪貼簿或前景視窗。
- [ ] 檢視實際截圖並記錄 DPI／工作區覆蓋與未驗證限制至 `docs/p0-validation.md`。檢查圖示、文字、按鈕裁切及預覽。
- [ ] 執行全變更獨立審查；檢查及提交文件和程式，回報完成行為、測試結果及限制。

## Progress

- Plan self-review: covered six pages, embedded hosts, action/search contracts, stable command ordering and undo, dirty forms and secrets, exit coordination, keyboard and DPI limits, shared panel routes, safety regression and desktop evidence.
