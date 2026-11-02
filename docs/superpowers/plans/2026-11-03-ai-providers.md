# AI Providers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓使用者選擇九個 AI 服務之一、設定金鑰與模型、明確測試並保存，保留舊設定與原有文字轉換安全行為。

**Architecture:** Core 提供服務目錄與載入時合併；Windows 共用受限 HTTP 傳輸，依三種協定建立請求及解析。App 保留服務設定草稿，以獨立測試控制器隔離取消、狀態與晚到結果。設定頁提供建議模型及進階網址，不擴張選字或剪貼簿流程。

**Tech Stack:** .NET 10 SDK selected by global.json、WPF、System.Text.Json、HttpClient、CurrentUser DPAPI、自訂 console test runners、PowerShell 7。

---

## 執行範圍與現況

設計來源：`D:/codex/snapbar/docs/superpowers/specs/2026-11-03-ai-providers-design.md`，使用者已核准修訂。沿用目前 checkout；不因純規劃工作另建 worktree。執行前讀取當下 AGENTS.md 與 git status，保留其他修改。

目前 AiSettings 預設兩個 profile、上限 8；AiProtocol 的 Responses=0、ChatCompletions=1，不能重排。AiHttpClient 只有兩個協定，不能讓新協定落入現有 ternary 的 Chat 分支。AiSettingsPanel 約 160 行，包含草稿與金鑰保存；測試新增獨立檔案以免此檔膨脹。AiSettingsWindow 在保存後關閉，MainWindow 內嵌頁保留；兩個宿主都必須處理測試取消。

執行方式由使用者在交接時選擇。引用上方執行技能時必須先找出並讀取實際 SKILL.md；不能假設帶 superpowers 前綴的名稱就是已安裝路徑。此文件本身不授權發佈、真實付費 API 呼叫或並行操作剪貼簿。

## 檔案責任

| 檔案（相對 D:/codex/snapbar） | 變更與責任 |
| --- | --- |
| src/SnapBar.Core/AiProviderCatalog.cs | 新增九個預設、模型建議與官方入口、舊 profile 合併 |
| src/SnapBar.Core/AiSettings.cs | append Messages、上限 32、預設與載入合併 |
| src/SnapBar.Windows/AiHttpClient.cs | 三協定請求、認證政策、受限讀取與分派 |
| src/SnapBar.Windows/AiResponseParser.cs | 抽取現有解析器，加 Messages、MiniMax 最終文字與錯誤代碼分類 |
| src/SnapBar.App/AiSettingsPanel.cs | 保留草稿及保存，調整模型控制項與進階設定 |
| src/SnapBar.App/AiSettingsPanel.Testing.cs | partial 類別；測試控制項、有效草稿金鑰、事件生命週期 |
| src/SnapBar.App/AiServiceTestController.cs | 固定測試請求、取消、晚到結果、僅記憶體狀態 |
| src/SnapBar.App/AiErrorMessages.cs | 測試與一般操作共用固定中文錯誤提示 |
| src/SnapBar.App/AiPanelWindow.cs | 沿用結果保留語意，使用共用錯誤提示 |
| tests/SnapBar.Tests/AiProviderCatalogTests.cs | 新增 Core 行為測試 |
| tests/SnapBar.Tests/AiRuntimeTests.cs | 更新預設數量、上限及舊設定保存預期 |
| tests/SnapBar.Tests/Program.cs | 註冊新 Core 測試 |
| tests/SnapBar.Windows.Tests/AiRuntimeTest.cs | 增加請求、回應與認證測試，沿用假 handler |
| tests/SnapBar.Windows.Tests/AiServicesTest.cs | 新增設定頁及測試控制器行為測試 |
| tests/SnapBar.Windows.Tests/AiPanelTest.cs | 在既有 Application 中呼叫 AiServicesTest |
| tests/SnapBar.Windows.Tests/SettingsNavigationTest.cs | 更新模型 ComboBox 查找，保留草稿保存回歸 |
| docs/p0-validation.md | 記錄實際驗證與限制 |

## 驗證命令

所有命令在 `D:/codex/snapbar` 的 PowerShell 7 執行。用 repository 入口選 SDK，不用 PATH 上任意 dotnet 或 dotnet test 取代 runner。

```powershell
pwsh -NoProfile -File scripts/build.ps1
pwsh -NoProfile -File scripts/check.ps1
pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite AiRuntime,AiPanel,Appearance
```

第一個命令執行 Release build 與 Core console runner，預期 exit 0、無失敗；第二個增加編碼、腳本、workflow 與 tooling 檢查；第三個執行受控桌面 suites，報告 success=true。PowerShell 陣列參數需在 PowerShell 命令環境執行，若由原生 process 啟動則改成分別執行三個 suite。

每個行為變更先新增相應失敗測試，再實作、執行相關 runner。桌面測試前正常結束 SnapBar（含 tray-only），不強殺程序；不與其他剪貼簿或前景視窗工作並行。每次檢查 exit code，不把截斷輸出或未執行當成通過。

### Task 1: 服務目錄、建議模型與設定升級

**Files:** Create `src/SnapBar.Core/AiProviderCatalog.cs`, `tests/SnapBar.Tests/AiProviderCatalogTests.cs`; modify `src/SnapBar.Core/AiSettings.cs`, `tests/SnapBar.Tests/AiRuntimeTests.cs`, `tests/SnapBar.Tests/Program.cs`.

- [ ] **Step 1: 核對內建資料。** 依設計表建立九個 profile，不變更既有 ID。逐家從規格所列官方文件確認一個目前適合文字轉換且支援指定協定的模型；OpenAI 另查官方 API 模型清單。取得金鑰與模型清單使用官方 HTTPS 頁面，不使用搜尋結果頁或第三方文章。將模型精確 ID、易讀名稱、來源頁與查核日期存進下列 metadata；不把模型塞入 profile.Model。

```csharp
public sealed record AiModelSuggestion(string Id, string Name);
public sealed record AiProviderDefinition(
    AiProviderProfile Profile,
    AiModelSuggestion[] Models,
    string? KeyUrl,
    string? ModelsUrl,
    string VerifiedOn);
```

自訂服務 Models=[]、KeyUrl=null、ModelsUrl=null；具名八家 Models 非空。VerifiedOn 為實際查核的 YYYY-MM-DD，不能宣稱服務實測成功。完整端點與協定直接採用核准設計表。公開唯讀 Definitions 與 Find(string id)，回傳 profile 與模型陣列的副本，避免呼叫者修改全域預設。

- [ ] **Step 2: 寫失敗測試並註冊。** 在 `AiProviderCatalogTests.Run(Action<string, Func<bool>> check)` 增加：

```csharp
var expected = new[] { "openai", "gemini", "claude", "deepseek", "groq",
    "openrouter", "xai", "minimax", "compatible" };
check("AI catalog creates nine stable profiles", () =>
    AiProviderCatalog.CreateProfiles().Select(p => p.Id).SequenceEqual(expected));
check("AI catalog keeps model choice explicit", () =>
    AiProviderCatalog.CreateProfiles().All(p => p.Model.Length == 0));
var legacy = new AiSettings {
    Profiles = [new("compatible", "My server", AiProtocol.ChatCompletions,
        "https://example.invalid/chat", "private-model")],
    SelectedProfileId = "compatible"
};
check("AI catalog preserves custom configuration and selection", () => {
    var merged = AiProviderCatalog.MergeMissing(legacy);
    return merged.Profiles[0] == legacy.Profiles[0]
        && merged.SelectedProfileId == "compatible"
        && merged.Profiles.Length == 9
        && AiProviderCatalog.MergeMissing(merged).Profiles.SequenceEqual(merged.Profiles);
});
```

另外加入八家模型與官方 HTTPS metadata、自訂無建議、預設副本隔離、既有同 ID 不覆寫、1/32/33 筆邊界、補入超界拒絕、壞檔與未來 schema 不寫回。更新舊 defaults.Length==2 與 9 筆拒絕案例，後者改成 33。檔案保存測試使用隔離目錄，保存前後比較 UTF-8 bytes，證明 Load 不寫檔。

- [ ] **Step 3: 執行 `scripts/build.ps1`，確認新目錄 API 尚未存在或新預期失敗。** 新 API 不存在時可先觀察預期編譯失敗；補入空的公開介面後，還需觀察行為斷言失敗。修正測試自身編譯錯誤後才計入預期紅燈，不接受無關 SDK 問題當成紅燈。

- [ ] **Step 4: 實作合併與載入接點。** AiProtocol append Messages；AiSettings.Profiles initializer 呼叫 CreateProfiles；Normalize 上限改 32。合併實作：

```csharp
public static AiSettings MergeMissing(AiSettings settings)
{
    var valid = AiPolicy.Normalize(settings);
    var known = valid.Profiles.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
    var missing = CreateProfiles().Where(p => !known.Contains(p.Id));
    return AiPolicy.Normalize(valid with { Profiles = [.. valid.Profiles, .. missing] });
}
```

AiSettingsStore.Load 在 schema 驗證之後、writable=true 之前呼叫 MergeMissing，沿用 ArgumentException 轉 settings-invalid。只在 Load 合併，不在 Normalize 或提示詞保存隱含補入。SelectedProfileId、Prompts、PinnedIds、RecentIds 保留正規化的既有值。

- [ ] **Step 5: 重跑 `scripts/build.ps1`；全部 Core 行為通過後提交。**

```powershell
git add -- src/SnapBar.Core/AiProviderCatalog.cs src/SnapBar.Core/AiSettings.cs tests/SnapBar.Tests/AiProviderCatalogTests.cs tests/SnapBar.Tests/AiRuntimeTests.cs tests/SnapBar.Tests/Program.cs
git commit -m "feat(ai): add provider catalog and migration"
```

### Task 2: 三協定 HTTP 與最終文字解析

**Files:** Create `src/SnapBar.Windows/AiResponseParser.cs`; modify `src/SnapBar.Windows/AiHttpClient.cs`, `tests/SnapBar.Windows.Tests/AiRuntimeTest.cs`.

- [ ] **Step 1: 在現有 RunAsync 中加入 Messages 行為測試。** 沿用該檔私有 Handler、Response、Check 與 Execute；不要建立新的真實 HttpClient 網路測試。

```csharp
var claude = new AiProviderProfile("claude", "Claude", AiProtocol.Messages,
    "https://example.invalid/v1/messages", "fixture-model");
await Check("AI Messages separates system and literal source", async () => {
    var valid = false;
    using var http = new HttpClient(new Handler(async (request, token) => {
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
        var root = json.RootElement;
        valid = request.Headers.Authorization == null
            && request.Headers.GetValues("x-api-key").Single() == "fake-key"
            && request.Headers.GetValues("anthropic-version").Single() == "2023-06-01"
            && root.GetProperty("system").GetString()!.EndsWith("shorten")
            && root.GetProperty("max_tokens").GetInt32() == 8192
            && root.GetProperty("messages")[0].GetProperty("content").GetString() == "literal source";
        return Response("""{"type":"message","stop_reason":"end_turn","content":[{"type":"text","text":"done"}]}""");
    }));
    using var client = new AiHttpClient(http);
    var result = await client.ExecuteAsync(claude, "fake-key", "literal source", "shorten", default);
    return valid && result.Success && result.Text == "done";
});
```

測試矩陣再加入：九個 profile 完整 endpoint 與協定（模型使用 fixture-model）；具名八家空金鑰在送出前拒絕；compatible 的 loopback/遠端 HTTPS 空金鑰允許，但 api.openai.com 仍拒絕；Messages 多文字區塊、thinking、max_tokens、refusal、tool_use、缺欄位與不明 stop reason；MiniMax reasoning_content 忽略、開頭 think 去除、未閉合 think 拒絕、普通正文標籤保留；各協定 2 MiB/262144 字元限制、取消與假 timeout。

- [ ] **Step 2: 執行 `scripts/test-desktop.ps1 -Suite AiRuntime`，確認新行為失敗。**

- [ ] **Step 3: 抽取現有 ParseResponses、ParseChat、String、Result、Failure 到 AiResponseParser。** 保持原有拒絕工具與不完整回應邏輯；新增 `Parse(AiProviderProfile, JsonElement)` 統一入口，使用 exhaustive switch：

```csharp
return profile.Protocol switch
{
    AiProtocol.Responses => ParseResponses(root),
    AiProtocol.ChatCompletions => ParseChat(root, profile.Id == "minimax"),
    AiProtocol.Messages => ParseMessages(root),
    _ => new(false, Status: "settings-required")
};
```

Messages 先確認 root.type==message，再判斷 stop_reason：end_turn 繼續、max_tokens 回 incomplete、refusal 回 refused、其他回 invalid-response。content 必須是陣列；依序串接 type=text 的 text，忽略 thinking/redacted_thinking，遇工具或未知區塊拒絕；加總長度不得超過 262144。空白結果拒絕。先驗證 JsonElement 型別，避免 GetString/GetProperty 直接拋未分類例外。

MiniMax 的 ParseChat 在既有成功條件之後使用下列完整後處理；不讀 reasoning_content：

```csharp
private static string? FinalMiniMaxText(string text)
{
    var remaining = text.AsSpan().TrimStart();
    while (remaining.StartsWith("<think>", StringComparison.Ordinal))
    {
        var end = remaining.IndexOf("</think>", StringComparison.Ordinal);
        if (end < 0) return null;
        remaining = remaining[(end + "</think>".Length)..].TrimStart();
    }
    return remaining.ToString();
}
```

- [ ] **Step 4: 改請求體分派與 headers。** 保留原 TransformationRules，原文永遠在 user message。所有分支必須明列：

```csharp
object body = profile.Protocol switch
{
    AiProtocol.Responses => new { model = profile.Model, store = false,
        instructions = rules, input = new[] { new { role = "user", content = sourceText } } },
    AiProtocol.ChatCompletions => new { model = profile.Model, stream = false,
        messages = new[] { new { role = "system", content = rules }, new { role = "user", content = sourceText } } },
    AiProtocol.Messages => new { model = profile.Model, stream = false, max_tokens = 8192,
        system = rules, messages = new[] { new { role = "user", content = sourceText } } },
    _ => throw new ArgumentException("Unsupported AI protocol.")
};
if (profile.Protocol == AiProtocol.Messages)
{
    request.Headers.Add("x-api-key", apiKey);
    request.Headers.Add("anthropic-version", "2023-06-01");
}
else if (!string.IsNullOrEmpty(apiKey))
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
```

刪除原本無條件設定 Bearer 的程式。送出前 requiresKey 條件為 catalog 中非 compatible 的具名服務，或既有 api.openai.com 主機。使用 Find 判定，不以所有 HTTPS URL 一概要求金鑰。保留 control-char/8192 長度檢查、60 秒 timeout、AllowAutoRedirect=false、受限串流與取消傳遞。

- [ ] **Step 5: 重跑 AiRuntime，確認舊協定與新矩陣全通過，再提交。**

```powershell
git add -- src/SnapBar.Windows/AiHttpClient.cs src/SnapBar.Windows/AiResponseParser.cs tests/SnapBar.Windows.Tests/AiRuntimeTest.cs
git commit -m "feat(ai): support provider wire protocols"
```

### Task 3: 可執行的固定錯誤提示

**Files:** Create `src/SnapBar.App/AiErrorMessages.cs`; modify `src/SnapBar.Windows/AiHttpClient.cs`, `src/SnapBar.Windows/AiResponseParser.cs`, `src/SnapBar.App/AiPanelWindow.cs`, `tests/SnapBar.Windows.Tests/AiRuntimeTest.cs`.

- [ ] **Step 1: 加入錯誤測試。** 已知 OpenAI-compatible `error.code=model_not_found` 對應 model-unavailable；相同 HTTP 404 搭配 HTML、空白、未知 code 仍為 network。Anthropic 只有 type=not_found_error 沒有可辨識模型代碼時仍保守分類，不分析自然語言 message。超過 2 MiB 的錯誤 body 要回 too-large；取消讀取仍傳遞 cancellation。401/403、429、408/504 固定狀態優先，不讓錯誤內文覆寫。

```csharp
await Check("AI known model code differs from generic 404", async () =>
    (await Execute(OpenAi, """{"error":{"code":"model_not_found","message":"private text"}}""",
        HttpStatusCode.NotFound)).Status == "model-unavailable"
    && (await Execute(OpenAi, "<html>missing path</html>", HttpStatusCode.NotFound)).Status == "network");
```

- [ ] **Step 2: 執行 AiRuntime 確認失敗，再共用受限 body reader。** 將既有成功回應讀取迴圈提成 `ReadBoundedBodyAsync(HttpContent, CancellationToken)`，保留 MaximumBody 上限與 ContentLength 快速拒絕；錯誤 JSON 亦經同一 reader。錯誤無效 JSON 回原 HTTP 分類，不回 invalid-response。代碼白名單實作只讀 error.code 字串，不讀 message：

```csharp
internal static bool IsModelUnavailable(JsonElement root)
{
    return root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("error", out var error)
        && error.ValueKind == JsonValueKind.Object
        && error.TryGetProperty("code", out var code)
        && code.ValueKind == JsonValueKind.String
        && code.GetString() == "model_not_found";
}
```

HTTP 2xx 內 error 物件也不得成功；其他未知代碼沿用固定分類。不記錄服務端訊息或回傳原始 body。

- [ ] **Step 3: 新增共用 `AiErrorMessages.Describe(string status)`。** 從 AiPanelWindow.ErrorMessage 移動既有固定文案；新增 model-unavailable 為「模型不存在或目前金鑰無權限，請確認模型 ID 與官方模型清單。」；authentication 文案檢查金鑰或權限；rate-limit 文案檢查配額或稍後再試。共用文案不包含「上一版已保留」，由 AiPanelWindow 的呼叫點附加此句，服務測試只顯示共用提示。

```csharp
private static string ErrorMessage(string code) =>
    AiErrorMessages.Describe(code) + "上一版已保留。";
```

- [ ] **Step 4: 重跑 AiRuntime 與 build；提交。**

```powershell
git add -- src/SnapBar.Windows/AiHttpClient.cs src/SnapBar.Windows/AiResponseParser.cs src/SnapBar.App/AiErrorMessages.cs src/SnapBar.App/AiPanelWindow.cs tests/SnapBar.Windows.Tests/AiRuntimeTest.cs
git commit -m "feat(ai): explain provider connection failures"
```

### Task 4: 一般設定、建議模型與進階網址

**Files:** Modify `src/SnapBar.App/AiSettingsPanel.cs`, `tests/SnapBar.Windows.Tests/SettingsNavigationTest.cs`, `tests/SnapBar.Windows.Tests/AiPanelTest.cs`; create `tests/SnapBar.Windows.Tests/AiServicesTest.cs`.

- [ ] **Step 1: 在現有 AiPanelTest 的 Application 中呼叫 `await AiServicesTest.RunAsync(directory, Check, Snapshot)`。** 新測試用自己獨立的子目錄與 Window，不開真實瀏覽器、不讀真實使用者設定。維持既有 Check(bool,string) 與 Snapshot(Window,string) 委派形式。

模型控制項從 TextBox 變為 IsEditable ComboBox，保留 AiModel ID。SettingsNavigationTest 所有 `Find<TextBox>(services, "AiModel")` 改成 ComboBox，仍可用 Text 指定舊測試模型。不藉機降低既有金鑰保存失敗與草稿離開測試要求。

- [ ] **Step 2: 寫測試驗證具名服務進階預設收合、自訂展開、非預設網址提示、模型任意 ID 與既有值保留。** 以 automation ID 查找控制項；測試關鍵狀態：

```csharp
check(!advanced.IsExpanded, "named provider starts with advanced settings collapsed");
check(model.IsEditable && model.Text == "legacy-private-model",
    "model editor preserves a value absent from recommendations");
model.Text = "typed-model";
advanced.IsExpanded = true;
advanced.IsExpanded = false;
check(model.Text == "typed-model" && endpoint.Text == originalEndpoint,
    "collapsing advanced settings preserves field values");
```

在測試方法中從 Window 子樹按 ID 取得 advanced=Expander、model=ComboBox、endpoint=TextBox；先用隔離 preferences 建立 legacy-private-model 與 originalEndpoint。新增九服務逐項切換與各自模型/網址/金鑰草稿保留的行為測試。

- [ ] **Step 3: 執行 AiPanel 確認新介面測試失敗，再改設定頁。** 以 ComboBox model 取代 TextBox model 宣告；現有 endpoint 與 PasswordBox 保留。模型選項使用 AiModelSuggestion 陣列作為 ItemsSource，ItemTemplate 顯示 metadata 的易讀名稱與 ID，TextSearch.TextPath 指向 Id，SelectedValuePath=Id；CurrentDraft 繼續使用 model.Text.Trim()。清單更換時以 syncing 保護，先保存目前 Text，再設定 ItemsSource，最後還原 Text，不自選第一個模型。

模型輸入長度維持 128：取得 ComboBox template 的 PART_EditableTextBox 設 MaxLength=128；policy 仍是最後驗證邊界，不能只依賴 UI。

```csharp
var advanced = new Expander { Header = "進階設定", IsExpanded = false };
AutomationProperties.SetAutomationId(advanced, "AiAdvancedSettings");
var advancedFields = new StackPanel();
advancedFields.Children.Add(endpoint);
advancedFields.Children.Add(removeKey);
advanced.Content = advancedFields;
```

將完整網址 label 與依協定更新的說明放入 advancedFields，移除原 fields 中同一元素後才加入新 parent。ShowDraft 按 profile.Id==compatible 設 IsExpanded；具名覆寫端點時顯示 AiCustomEndpointNotice（用 Uri 正規化比較預設網址）。一般 fields 順序為 profiles、key、keyStatus/取得金鑰、model/模型清單、custom notice、advanced、測試區域。保存/捨棄 footer 固定，內容可捲動。

- [ ] **Step 4: 加官方連結與可注入開啟委派。** constructor 增加可選 `Action<Uri>? openLink = null`；預設 BrowserLauncher.Open，測試捕捉 Uri 不啟動瀏覽器。僅在 catalog URL 非空時顯示入口，開啟失敗回固定中文提示，不輸出例外或 URL 內容。metadata 連結只能來自 catalog，不能把 API endpoint 當模型文件網址。

```csharp
Action<Uri> launch = openLink ?? BrowserLauncher.Open;
```

需加入 `using SnapBar.Windows;`。確認既有 new(preferences) 呼叫仍可編譯。測試官方連結呼叫次數、HTTPS 與對應服務；切換/載入不可啟動瀏覽器。

- [ ] **Step 5: 跑 AiPanel 與 Appearance，正常/最小視窗擷取截圖；確認後提交。**

```powershell
git add -- src/SnapBar.App/AiSettingsPanel.cs tests/SnapBar.Windows.Tests/AiServicesTest.cs tests/SnapBar.Windows.Tests/AiPanelTest.cs tests/SnapBar.Windows.Tests/SettingsNavigationTest.cs
git commit -m "feat(ai): simplify provider configuration"
```

### Task 5: 固定文字測試控制器與取消生命週期

**Files:** Create `src/SnapBar.App/AiServiceTestController.cs`; extend `tests/SnapBar.Windows.Tests/AiServicesTest.cs`.

- [ ] **Step 1: 在 AiServicesTest 增加 controllable fake client。** 保存 Calls、Profile、Key、Source、Instruction，TaskCompletionSource<AiTextResult> 使用 RunContinuationsAsynchronously。故意讓 fake 忽略取消再完成舊請求，證明 revision 保護不只依賴供應商合作。

```csharp
private sealed class DeferredClient : IAiTextClient
{
    internal readonly List<TaskCompletionSource<AiTextResult>> Requests = [];
    internal readonly List<(AiProviderProfile Profile, string Key, string Source, string Instruction)> Calls = [];
    public Task<AiTextResult> ExecuteAsync(AiProviderProfile profile, string key,
        string source, string instruction, CancellationToken token)
    {
        Calls.Add((profile, key, source, instruction));
        var completion = new TaskCompletionSource<AiTextResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Requests.Add(completion);
        return completion.Task;
    }
}
```

測試 StartAsync 只送固定文字、Busy 時不重入、Reset 後舊完成不改狀態、新請求完成可成功、Cancel 結束等待但不保存成功、Dispose 後不能更新、失敗只保留 status 不保留 Text。

- [ ] **Step 2: 執行 AiPanel 確認缺少控制器，再新增類別。** 使用下列完整狀態機；所有 public/internal 操作由 WPF dispatcher 執行，不新增跨執行緒 UI 更新：

```csharp
internal sealed class AiServiceTestController(IAiTextClient client) : IDisposable
{
    internal const string Source = "SnapBar connection test";
    internal const string Instruction = "Reply with OK.";
    private CancellationTokenSource? request;
    private int revision;
    private bool disposed;
    internal bool Busy => request != null;
    internal string Status { get; private set; } = "untested";
    internal event Action? Changed;

    internal void Reset()
    {
        revision++;
        var previous = request;
        request = null;
        Status = "untested";
        previous?.Cancel();
        if (!disposed) Changed?.Invoke();
    }

    internal void Cancel()
    {
        Reset();
    }

    internal async Task StartAsync(AiProviderProfile profile, string key)
    {
        if (disposed || Busy) return;
        var currentRevision = ++revision;
        using var cancellation = new CancellationTokenSource();
        request = cancellation;
        Status = "testing";
        Changed?.Invoke();
        try
        {
            var result = await client.ExecuteAsync(profile, key, Source, Instruction,
                cancellation.Token).WaitAsync(cancellation.Token);
            if (!disposed && currentRevision == revision)
                Status = result.Success ? "success" : result.Status;
        }
        catch (OperationCanceledException)
        {
            if (!disposed && currentRevision == revision) Status = "untested";
        }
        catch (Exception error) when (error is InvalidOperationException
            or System.IO.IOException or System.Net.Http.HttpRequestException
            or ObjectDisposedException)
        {
            if (!disposed && currentRevision == revision) Status = "network";
        }
        finally
        {
            if (!disposed && currentRevision == revision)
            {
                request = null;
                Changed?.Invoke();
            }
        }
    }

    public void Dispose()
    {
        disposed = true;
        Reset();
        Changed = null;
    }
}
```

控制器不保存金鑰欄位或測試回應文字。StartAsync 的區域參數只存活於請求生命週期。WaitAsync 讓忽略取消的假 client 也能釋放 UI 等待；晚到結果不能觸發 Changed。Reset 在 Unloaded 時可重用，Dispose 只在 Detach 真正移除服務頁時執行。

- [ ] **Step 3: 重跑 AiPanel，提交狀態機與測試。**

```powershell
git add -- src/SnapBar.App/AiServiceTestController.cs tests/SnapBar.Windows.Tests/AiServicesTest.cs
git commit -m "feat(ai): isolate service test lifecycle"
```

### Task 6: 接入服務測試、有效金鑰與保存狀態

**Files:** Create `src/SnapBar.App/AiSettingsPanel.Testing.cs`; modify `src/SnapBar.App/AiSettingsPanel.cs`, `tests/SnapBar.Windows.Tests/AiServicesTest.cs`.

- [ ] **Step 1: 建立設定頁整合失敗測試。** constructor 增加可選 `IAiTextClient? testClient = null`，不影響宿主既有建構。測試注入 DeferredClient：填未存模型/網址/金鑰後測試，斷言收到草稿而非 persisted profile；空白金鑰用對應已存 secret、移除選取傳空、換網址不得使用舊 secret；本次行為不改 ai.json/secrets bytes。測試前後記錄所有隔離設定檔 bytes，不把內容印出。

測試欄位變更取消、切換服務取消、Discard 取消、Unloaded 取消、Detach 取消、重開頁尚未測試、單純進階展開不取消、測試失敗仍可保存、測試成功不清除 IsDirty、保存成功不代表測試成功。保存後清空 PasswordBox 是程式化同步，不得把有效值相同的測試成功誤清除。

- [ ] **Step 2: 執行 AiPanel 確認測試失敗，再將 AiSettingsPanel 改為 partial。** `AiSettingsPanel.Testing.cs` 負責 InitializeTesting、RunTestAsync、ResetTest、RenderTestStatus、DisposeTesting；不把 secrets 保存邏輯移到測試控制器。

InitializeTesting 建立所擁有的 AiHttpClient（未注入時）與 AiServiceTestController，建立 AiTestService、AiCancelServiceTest、AiServiceTestStatus 控制項，顯示「會發送少量測試文字，可能產生費用」。RenderTestStatus 只依 Status/Busy 顯示固定文字，不展示 AiTextResult.Text。

有效金鑰計算留在設定頁，使用與 Scope(CurrentDraft()) 一致的 profile：

```csharp
private string EffectiveTestKey(AiProviderProfile profile)
{
    if (removeKey.IsChecked == true) return "";
    if (key.Password.Length > 0) return key.Password;
    if (keyChanges.TryGetValue(Scope(profile), out var pending))
        return pending.Key ?? "";
    return preferences.Secrets.Get(profile) ?? "";
}
```

RunTestAsync 先呼叫 AiPolicy.ValidateProfile，失敗不送出；捕捉 secrets 讀取的既有 IO/權限/加密錯誤並顯示固定提示；接著 await controller.StartAsync(CurrentDraft(), EffectiveTestKey(profile))。不呼叫 TrySave、CaptureDraft、NotifyChanged 或任何結果面板操作。

- [ ] **Step 3: 接入事件生命週期。** 所有使用者造成的 profiles.SelectionChanged、endpoint.TextChanged、model 的可編輯文字變動、key.PasswordChanged、removeKey.Checked/Unchecked 在 !syncing 時 ResetTest。ComboBox 可編輯文字須用 `AddHandler(TextBoxBase.TextChangedEvent, ...)` 監聽內部 TextBox 的文字變動，不能只用 SelectionChanged，否則手動 ID 不會清掉測試狀態。

```csharp
model.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
    new TextChangedEventHandler((_, _) =>
    {
        if (!syncing) { ResetTest(); UpdateState(); }
    }));
Unloaded += (_, _) => ResetTest();
```

Discard 開頭 ResetTest；Detach 先取消、移除 controller.Changed 訂閱，再 Dispose 控制器及自己擁有的 AiHttpClient，保留既有 preferences 訂閱解除與草稿解註冊。注入 client 不由設定頁 dispose。TrySave 若忙碌先取消測試；完成保存清除 key/removeKey 的整段必須包在 syncing try/finally 中。任一步金鑰保存失敗取消測試成功狀態，不顯示整體保存成功。頁面隱藏觸發 Unloaded 後仍能重新載入並測試。

- [ ] **Step 4: 重跑 AiPanel、Appearance 與 AiRuntime，擷取四種狀態。** 正常具名服務、最小視窗、自訂展開、測試成功/失敗使用固定 fake client，不觸發付費 API。確認測試按鈕與保存 footer 都能用鍵盤到達。

```powershell
git add -- src/SnapBar.App/AiSettingsPanel.cs src/SnapBar.App/AiSettingsPanel.Testing.cs tests/SnapBar.Windows.Tests/AiServicesTest.cs
git commit -m "feat(ai): test provider drafts before saving"
```

### Task 7: 整合檢查與交付證據

**Files:** Modify `docs/p0-validation.md`; inspect all changed source and tests.

- [ ] **Step 1: 自我審查。** 確認沒有實際金鑰、原文、clipboard、HTTP message 或錯誤 body 被記錄；沒有覆寫舊模型、既有網址或服務選擇；沒有新的 selection/foreground/action permission 放寬；協定 enum 未改號。檢查模型建議與官方連結已填入具體資料、查核日期與來源，沒有待補欄位。

- [ ] **Step 2: 執行完整日常檢查與相關桌面 suites。**

```powershell
pwsh -NoProfile -File scripts/check.ps1
pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite AiRuntime
pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite AiPanel
pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite Appearance
```

每項 exit 0、報告 success=true 才算通過；記錄實際通過數，不把測試列表數量當通過數。若桌面無法使用，明確記錄未完成的 suites 與原因，不聲稱驗收完成。依測試擷取的實際路徑逐張檢視設定頁 PNG，發現截斷、遮擋或不可達欄位先修復再重跑受影響 suites。

- [ ] **Step 3: 更新驗證紀錄。** 記錄日期、來源提交、各報告絕對路徑、通過數、截圖、八家具名服務僅完成模擬協定驗證的限制；沒有使用者金鑰就不宣稱真實連通性。Markdown 使用 apply_patch，寫後以嚴格 UTF-8 無 BOM 解碼確認中文正確。提交：

```powershell
git add -- docs/p0-validation.md
git commit -m "docs(ai): record provider validation"
git status --short
```

- [ ] **Step 4: 回報功能與證據。** 提供變更摘要、實際測試、未完成項目與截圖。此次計畫不包含 publish；若使用者要求套件，再執行 `scripts/test-desktop.ps1 -Suite Release` 與 `scripts/build.ps1 -Publish`，不可沿用不同來源的 Release 證據或繞過程序退出檢查。

## 規格覆蓋自查

| 核准要求 | 對應任務 |
| --- | --- |
| 九個服務、官方端點、建議模型與官方入口 | 1、4 |
| schema 1、32 上限、冪等升級、不覆寫舊設定 | 1 |
| Responses、ChatCompletions、Messages、MiniMax 思考分離 | 2 |
| 有界錯誤讀取、模型錯誤不混淆未知 404、固定文案 | 3 |
| 一般畫面簡化、進階網址、自訂提示、鍵盤與最小視窗 | 4、6 |
| 固定文字測試、明確費用提示、不讀選取、不隱含保存 | 5、6 |
| 草稿金鑰、移除意圖、取消、晚到結果、保存狀態分離 | 5、6 |
| 舊自訂空金鑰、DPAPI 作用域、結果及選字安全回歸 | 2、6、7 |
| 真實服務限制、截圖與 repository 檢查 | 7 |
