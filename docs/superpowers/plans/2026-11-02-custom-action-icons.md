# Custom Action Icons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓自訂動作從名稱旁開啟既有 AI 圖庫，以獨立視窗選擇圖示，且保存、匯入／匯出及所有動作呈現一致。

**Architecture:** 保留 `CustomActionDefinition.Icon`，Core 只定義可保存的識別碼格式，App 使用現有 `AiIconCatalog`、`AiIconPresenter`、`AiIconPickerWindow`。擴充呈現器的色彩能力與自訂動作專用入口，不改寫內建圖示或複製圖庫。編輯器保存暫存識別碼，只有儲存動作才提交。

**Tech Stack:** .NET 10、WPF、既有 Fluent Regular 字型與目錄、PowerShell 7、自訂 Core／Windows console runners。

**Approved spec:** `docs/superpowers/specs/2026-11-02-custom-action-icons-design.md`。

**Execution context:** 使用目前已完成探索的 checkout；逐項執行且保持既有變更完整。此計畫不要求發佈。

---

## File map

| File | Responsibility |
| --- | --- |
| `src/SnapBar.Core/CustomActions.cs` | 新增圖示識別碼格式正規化，供匯入／匯出重用 |
| `src/SnapBar.Core/ActionCatalog.cs` | 透過相同規則正規化設定 |
| `src/SnapBar.App/AiIconPresenter.cs` | Fluent／legacy／未知圖示呈現及可設定前景色 |
| `src/SnapBar.App/AiIconPickerWindow.cs` | 共用圖庫標題與取消／選擇契約 |
| `src/SnapBar.App/ActionUi.cs` | 自訂動作專用圖示工廠與按鈕換色相容 |
| `src/SnapBar.App/ActionEditorWindow.cs` | 圖示按鈕、暫存選擇與類型預設契約 |
| `src/SnapBar.App/ActionSettingsPanel.cs` | 清單與預覽的自訂圖示 |
| `src/SnapBar.App/PopupWindow.cs` | 實際動作列及更多選單圖示與主題色 |
| `tests/SnapBar.Tests/ActionCatalogTests.cs` | 正規化及匯入／匯出回歸 |
| `tests/SnapBar.Windows.Tests/AiIconTest.cs` | 共用呈現器、既有 AI 行為回歸 |
| `tests/SnapBar.Windows.Tests/UiAppearanceTest.cs` | 更新舊 ComboBox 假設，驗證圖示切換與草稿 |
| `tests/SnapBar.Windows.Tests/ActionLayoutTest.cs` | 設定／動作列／更多圖示與主題 |
| `docs/p0-validation.md` | 真實驗證結果與限制 |

## Task 1: Preserve icon IDs throughout Core

- [ ] 在 `ActionCatalogTests.cs` 的既有 `Check` 區域加入下列往返案例，並把「unknown action icons normalize to spark」改為未知合法識別碼保留。

```csharp
foreach (var iconId in new[] { "globe", "terminal", "legacy:clean", "translate_20_regular", "unknown-fixture" })
{
    var expected = iconId;
    check("custom icon round trip " + expected, () =>
    {
        var changed = action with { Icon = expected };
        return ActionCatalog.Normalize(settings with { CustomActions = [changed] }).CustomActions[0].Icon == expected
            && CustomActionPolicy.Import(CustomActionPolicy.Export(changed)).Icon == expected;
    });
}
foreach (var iconId in new[] { "", "bad icon", "../icon", "圖示", new string('a', 129) })
{
    var invalid = iconId;
    check("invalid custom icon falls back " + invalid.Length, () =>
        ActionCatalog.Normalize(settings with { CustomActions = [action with { Icon = invalid }] }).CustomActions[0].Icon == "spark"
        && CustomActionPolicy.Import(CustomActionPolicy.Export(action with { Icon = invalid })).Icon == "spark");
}
check("custom icon boundary accepts 128 ASCII characters", () =>
    ActionCatalog.Normalize(settings with { CustomActions = [action with { Icon = new string('a', 128) }] }).CustomActions[0].Icon.Length == 128);
```

- [ ] 執行 `.tools/dotnet/dotnet.exe run --project tests/SnapBar.Tests -c Release`。預期新 Fluent 與未知合法圖示往返案例失敗，確認測試涵蓋既有白名單問題。
- [ ] 在 `CustomActionPolicy` 新增單一格式規則。替換 `Export`、`Import` 與 `ActionCatalog.Normalize` 的舊 `Icons.Contains(...) ? ... : "spark"` 判斷，保留其他欄位與驗證順序。

```csharp
internal static string NormalizeIcon(string? value) =>
    value is { Length: > 0 and <= 128 }
    && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or ':')
        ? value : "spark";
```

替換位置的指定值：

```csharp
Icon = NormalizeIcon(action.Icon) // CustomActionPolicy.Export
Icon = NormalizeIcon(envelope.Action.Icon) // CustomActionPolicy.Import
Icon = CustomActionPolicy.NormalizeIcon(action.Icon) // ActionCatalog.Normalize
```

- [ ] 補測 `null!`、控制字元及輸入大小邊界；保留原有匯入版本、重複欄位、內容與 URL 安全案例。
- [ ] 重跑 Core runner，預期全部通過。提交 `feat(actions): preserve custom icon identifiers`，只暫存本任務檔案。

## Task 2: Make the existing icon presenter reusable with theme colors

- [ ] 在 `AiIconTest.cs` 增加呈現器測試：legacy 與裸舊名稱都維持向量圖形、正式 Fluent 名稱顯示字型、未知合法名稱顯示星芒，並驗證改變前景色後的 glyph／Path 顏色。
- [ ] 在 `AiIconPresenter` 增加 dependency property，讓字型與 fallback 同時使用該色彩，預設保持現有 AI 的 Muted 顏色。

```csharp
public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
    nameof(Foreground), typeof(Brush), typeof(AiIconPresenter),
    new PropertyMetadata(ActionUi.Muted, (d, _) => ((AiIconPresenter)d).Render()));
public Brush Foreground
{
    get => (Brush)GetValue(ForegroundProperty);
    set => SetValue(ForegroundProperty, value);
}
```

`Render()` 中 glyph 的 `Foreground` 與 `ActionUi.Icon` 的 color 參數改成 `Foreground`。保留 `AiIconCatalog.Stored` 的舊名稱優先語意，不改 `TryResolve` 的圖庫選擇規則。

- [ ] 在 `ActionUi` 新增 action 專用工廠。新增 `using SnapBar.Core;`，不改現有 `Icon` 的控制項圖示契約。

```csharp
public static FrameworkElement ItemIcon(ActionItem item, Brush color, double size = 18) =>
    item.Custom == null ? Icon(item.Icon, color, size)
        : new AiIconPresenter { IconId = item.Icon, Foreground = color, Width = size, Height = size };
```

- [ ] 圖庫視窗標題從「選擇指令圖示」改為「選擇圖示」。保留 `SelectedId == null` 表示取消、選擇後關閉、IME／鍵盤及虛擬化既有行為。
- [ ] 執行 `pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite AiPanel`，確認共用元件與原有 AI 案例通過；提交 `refactor(icons): support shared themed rendering`。

## Task 3: Replace the custom action ComboBox with a compact icon button

- [ ] 更新 `UiAppearanceTest.cs` 原本尋找「動作圖示」ComboBox 的案例，改尋找下列 automation IDs：`CustomActionChooseIcon`、`CustomActionIconPreview`。先驗證圖示按鈕存在、能以鍵盤取得焦點，現有測試預期因找不到新入口而失敗。
- [ ] 在 `ActionEditorWindow` 移除 `icon` ComboBox 及 13 項建立迴圈，新增以下狀態與方法，建構時以 `existing?.Icon ?? "globe"` 初始化。

```csharp
private string selectedIcon = "globe";
private readonly AiIconPresenter iconPreview = new() { Width = 22, Height = 22 };
private Button iconButton = null!;

private void ChooseIcon()
{
    var picker = new AiIconPickerWindow(selectedIcon) { Owner = this };
    picker.ShowDialog();
    if (picker.SelectedId is { } chosen)
    {
        selectedIcon = chosen;
        iconChosen = true;
        iconPreview.IconId = chosen;
    }
    iconButton.Focus();
}
```

- [ ] 將 identity Grid 設為左側 44 寬圖示、12 間距、右側 `*` 名稱。保留目前動作類型欄位順序；只有名稱／圖示同列重新配置。建立圖示按鈕並將 preview 設為內容：

```csharp
iconButton = ActionUi.Button("", ChooseIcon);
iconButton.Content = iconPreview;
iconButton.Width = iconButton.Height = 44;
iconButton.ToolTip = "更換圖示";
AutomationProperties.SetName(iconButton, "更換圖示");
AutomationProperties.SetAutomationId(iconButton, "CustomActionChooseIcon");
AutomationProperties.SetAutomationId(iconPreview, "CustomActionIconPreview");
```

每個欄位仍以既有 `Field` 方法建立標籤，圖示按鈕與名稱欄頂端及底部對齊，不增加新折疊區、識別碼欄位或頁尾圖庫。

- [ ] `LoadKind` 的 `!iconChosen` 分支改為下列程式碼；`ReadAction` 傳入 `selectedIcon`，移除 ComboBox 的 SelectionChanged 訂閱及舊控制項存取。

```csharp
if (!iconChosen)
{
    selectedIcon = type == CustomActionKind.Url ? "globe" : "terminal";
    iconPreview.IconId = selectedIcon;
}
```

- [ ] 依現有 `AiIconTest` 的 Dispatcher 排程方式，在模態視窗開啟後操作 `ChooseIcon.Execute("translate_20_regular")`。驗證選完關閉、預覽更新、名稱／內容／樣本不變、編輯視窗尺寸及內容位置不變、焦點返回圖示按鈕。
- [ ] 分別測試關閉、取消與 Escape 回傳空選擇且不修改圖示；測試選圖後切換三種類型保留圖示、新動作未選圖時保留 globe／terminal 預設、編輯既有及未知合法圖示不被重設。
- [ ] 驗證儲存產出所選 Icon，關閉編輯器則沒有 `SavedAction`；不新增真實 URL／腳本執行。執行 `pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite Appearance,Actions`，提交 `feat(actions): add compact custom icon picker`。

## Task 4: Render chosen icons consistently in settings and the popup

- [ ] 在 `ActionSettingsPanel` 兩處 `ActionUi.Icon(item.Icon, ...)` 改呼叫 `ActionUi.ItemIcon(item, ...)`，沿用原來的尺寸、Margin、深淺主題及自訂動作 Accent 顏色。
- [ ] 在 `PopupWindow` 建立 bar／more 按鈕後，保留既有 `ActionUi.Button` 標籤、事件與權限邏輯，只替換自訂動作的首個圖示元素。替換時保留原元素 Margin。

```csharp
if (item.Custom != null)
{
    var content = (StackPanel)button.Content;
    var previous = (FrameworkElement)content.Children[0];
    var replacement = ActionUi.ItemIcon(item, ActionUi.Ink, previous.Width);
    replacement.Margin = previous.Margin;
    content.Children.RemoveAt(0);
    content.Children.Insert(0, replacement);
}
```

- [ ] 在 popup 換色及 `ActionUi.TintButton` 處，先判斷 `AiIconPresenter` 再處理舊 Viewbox／Path。保留內建按鈕與「更多」圖示路徑，不對 Fluent presenter 強制轉型成 Path。

```csharp
if (iconElement is AiIconPresenter presenter) presenter.Foreground = fg;
else if (iconElement is Viewbox { Child: System.Windows.Shapes.Path path }) path.Stroke = fg;
```

其中 `iconElement` 是現有換色邏輯取得的首個圖示元素，`fg` 是該段既有主題前景色；不修改 hover、disabled 或鍵盤行為。

- [ ] 在 `ActionLayoutTest` 放入同時包含 legacy、Fluent、未知合法識別碼的自訂動作，分別配置 Bar、More；驗證設定預覽、清單與 popup 中的 `AiIconPresenter.IconId` 等於保存值，未知值仍有 fallback。
- [ ] 驗證 Dark／Light、ShowLabels 開關、更多選單、停用動作、內建複製／搜尋圖示都能正常呈現；執行 `pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite Layout,Keyboard`。提交 `feat(actions): render selected icons throughout`。

## Task 5: Verify the full behavior and record evidence

- [ ] 執行 `pwsh -NoProfile -File scripts/check.ps1`。預期工具契約、Release 建置及 Core runner 全部通過；失敗先修正，不以 `dotnet test` 取代 runner。
- [ ] 正常退出目前 SnapBar 後，執行 `pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite Appearance,Actions,Layout,Keyboard,AiPanel`。預期所有指定 suite 成功；若先前相同原始碼已有成功報告，不重複執行無變更 suite。
- [ ] 用既有桌面 snapshot helper 保存標準與最小編輯尺寸、圖庫開啟、圖庫選擇後、深淺色 bar／more 的截圖，逐張檢視裁切、對齊、圖示顏色及操作可見性。
- [ ] 以實際 `ActionPreferences` 保存／重載新圖示，並以 Export／Import 確認一致；確認取消不保存，其他動作與 AI 命令資料不變。測試使用可丟棄偏好目錄。
- [ ] 在 `docs/p0-validation.md` 記錄日期、具體報告路徑、截圖位置、通過案例與未完成的 DPI／跨 App 覆蓋。以嚴格 UTF-8 讀回所有修改的中文 Markdown，執行 `git diff --check`。
- [ ] 提交驗證紀錄 `docs(actions): record icon picker validation`，更新本計畫完成狀態，向使用者回報行為、驗證及剩餘限制。不得自動執行 Publish。

## Self-review

- 規格涵蓋：入口及焦點／草稿對應 Task 3；共用圖庫及顏色對應 Task 2；保存與格式對應 Task 1；四個呈現位置及主題對應 Task 4；驗證紀錄對應 Task 5。
- API 一致：Core 使用 `NormalizeIcon`，App 使用既有 `IconId` 與 `SelectedId`，新增 `Foreground` 與 `ItemIcon`；保留既有 `iconChosen` 語意。
- 範圍完整：不變更內建圖示，不新增圖片上傳、色彩設定或自動發佈。使用現有測試 runner，不建立第二套測試框架。
