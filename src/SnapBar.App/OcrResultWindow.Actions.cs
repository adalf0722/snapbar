using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

internal sealed record OcrActionDependencies(Func<ActionSettings> Settings, Func<string> SearchTemplate,
    Func<ActionSettings, bool> Save, Action Manage, AiPreferences? Ai,
    OfflineDictionaryLibrary? Dictionary = null, Action? ManageDictionary = null,
    Action<Uri>? LaunchWeb = null, Func<CustomActionDefinition, string, CancellationToken, Task<ScriptExecutionResult>>? RunCustom = null,
    Func<IPathRevealSession>? CreatePathSession = null, IAiTextClient? AiClient = null)
{
    internal static OcrActionDependencies Default(string searchTemplate) => new(() => new(), () => searchTemplate, _ => true, () => { }, null);
}

internal sealed partial class OcrResultWindow
{
    private OcrActionDependencies actionDependencies = null!;
    private readonly Guid cardId = Guid.NewGuid();
    private long revision, invocation;
    private bool actionClosed;
    private CancellationTokenSource? actionCancellation;
    private ContextMenu? actionMenu;
    private ActionResultWindow? actionResult;
    private AiPanelWindow? actionAi;
    private Button? actionCancel;

    private void InitializeActions(OcrActionDependencies dependencies) => actionDependencies = dependencies;
    private void ActionEditorChanged() { revision++; actionResult?.InvalidateTarget(); actionAi?.InvalidateTarget(); }
    private void DisposeActions()
    {
        actionClosed = true; invocation++; actionCancellation?.Cancel(); actionMenu?.SetCurrentValue(ContextMenu.IsOpenProperty, false);
        // Keep already-produced output available for copying, but detach its stale target.
        actionResult?.InvalidateTarget(); actionResult = null; actionAi?.Close(); actionAi = null;
    }
    private bool CloseActions()
    {
        if (actionMenu?.IsOpen != true) return false;
        actionMenu.IsOpen = false; editor.Focus(); return true;
    }
    private void OpenActions()
    {
        if (actionCancellation != null || actionAi?.IsVisible == true || actionClosed) return;
        var menu = new ContextMenu { PlacementTarget = more, MaxHeight = Math.Max(160, SystemParameters.WorkArea.Height / 2) };
        actionMenu = menu; more.ContextMenu = menu;
        var search = new TextBox { MinWidth = 220, Margin = new(4) };
        AutomationProperties.SetAutomationId(search, "OcrActionSearch"); AutomationProperties.SetName(search, language.Get("Ocr_ActionSearch"));
        var searchItem = new MenuItem { StaysOpenOnClick = true, Header = search }; menu.Items.Add(searchItem);
        void Render()
        {
            while (menu.Items.Count > 1) menu.Items.RemoveAt(1);
            var settings = actionDependencies.Settings(); var query = search.Text.Trim();
            var matches = ActionCatalog.GetLocalActions(settings, query).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var item in ActionCatalog.GetLocalActions(settings).Where(item => matches.Contains(item.Id)
                || UiNames.Action(item, language).Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                var enabled = ActionCatalog.IsApplicableLocal(item, SelectedText()) && (item.BuiltIn != TextAction.Ai || actionDependencies.Ai != null);
                var row = new MenuItem { Header = UiNames.Action(item, language), IsEnabled = enabled,
                    Icon = ActionUi.ItemIcon(item, ActionUi.Ink), ToolTip = enabled ? null : language.Get(item.BuiltIn switch
                    { TextAction.OpenLink => "Ocr_InvalidLink", TextAction.RevealPath => "Ocr_InvalidPath", TextAction.Lookup => "Ocr_InvalidLookup", _ => "Ocr_ActionUnavailable" }) };
                if (enabled && item.Custom?.Output == ActionOutput.ReplaceSelection) row.ToolTip = language.Get("Ocr_Apply");
                if (!enabled)
                {
                    ToolTipService.SetShowOnDisabled(row, true);
                    AutomationProperties.SetHelpText(row, (string)row.ToolTip);
                }
                AutomationProperties.SetAutomationId(row, "OcrAction_" + item.Id);
                row.Click += (_, _) => { menu.IsOpen = false; _ = RunActionAsync(item); };
                menu.Items.Add(row);
            }
            menu.Items.Add(new Separator());
            var manage = new MenuItem { Header = language.Get("Ocr_ManageActions") };
            AutomationProperties.SetAutomationId(manage, "OcrManageActions"); manage.Click += (_, _) => actionDependencies.Manage(); menu.Items.Add(manage);
        }
        search.TextChanged += (_, _) => Render();
        search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; menu.IsOpen = false; editor.Focus(); }
            else if (e.Key is Key.Down or Key.Enter)
            {
                var first = menu.Items.OfType<MenuItem>().FirstOrDefault(item => item != searchItem && item.IsEnabled
                    && AutomationProperties.GetAutomationId(item) != "OcrManageActions");
                if (first != null) { e.Handled = true; if (e.Key == Key.Enter) first.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); else first.Focus(); }
            }
        };
        Render();
        menu.Closed += (_, _) => { if (!actionClosed && actionCancellation == null) editor.Focus(); };
        menu.IsOpen = true; search.Focus();
    }
    private bool DefinitionCurrent(ActionItem item) => ActionCatalog.GetLocalActions(actionDependencies.Settings()).Any(current => current == item);
    private async Task RunActionAsync(ActionItem item)
    {
        if (actionCancellation != null || actionAi?.IsVisible == true || actionClosed) return;
        if (!DefinitionCurrent(item) || !ActionCatalog.IsApplicableLocal(item, SelectedText())) { status.Text = language.Get("Ocr_ActionChanged"); return; }
        var useSelection = item.Custom?.UseSelection ?? true;
        var ticket = LocalActionTicket.Create(cardId, revision, editor.Text, editor.SelectionStart, editor.SelectionLength, useSelection);
        var serial = ++invocation;
        bool Strict() => !actionClosed && invocation == serial && DefinitionCurrent(item)
            && ticket.IsCurrent(cardId, revision, editor.Text, editor.SelectionStart, editor.SelectionLength);
        bool Current() => !actionClosed && invocation == serial && DefinitionCurrent(item)
            && (useSelection || item.Custom?.Output == ActionOutput.ReplaceSelection ? Strict() : true);
        bool Replace(string text)
        {
            if (!Strict()) return false;
            editor.BeginChange();
            try { if (ticket.SelectionLength == 0) editor.SelectAll(); else editor.Select(ticket.SelectionStart, ticket.SelectionLength); editor.SelectedText = text; }
            finally { editor.EndChange(); }
            editor.Focus(); status.Text = language.Get("Ocr_ActionComplete"); return true;
        }
        void ShowResult(string text, bool allowApply, ScriptDiagnostic? diagnostic = null)
        {
            actionResult?.Close();
            var result = new ActionResultWindow(UiNames.Action(item, language), text, item.Custom?.Output == ActionOutput.Markdown,
                allowApply && Strict(), () => { if (!Replace(text)) actionResult?.InvalidateTarget(); }, item.Custom?.Output == ActionOutput.Hud, diagnostic,
                applyLabel: language.Get("Ocr_Apply"), applyHint: language.Get("Ocr_ApplyHint"), staleApplyHint: language.Get("Ocr_ApplyStaleHint"));
            actionResult = result; result.Show();
        }
        using var cancellation = new CancellationTokenSource(); actionCancellation = cancellation;
        more.IsEnabled = false; status.Text = language.Get("Ocr_ActionRunning");
        actionCancel = ActionUi.Button(language.Get("Ocr_ActionCancel"), () => cancellation.Cancel());
        AutomationProperties.SetAutomationId(actionCancel, "OcrActionCancel");
        AutomationProperties.SetAutomationId(status, "OcrActionStatus");
        ((Panel)more.Parent).Children.Add(actionCancel);
        try
        {
            if (item.BuiltIn == TextAction.Ai && actionDependencies.Ai != null)
            {
                actionAi?.Close(); actionAi = new AiPanelWindow(ticket.Input, actionDependencies.Ai, Strict, text => Task.FromResult(Replace(text)), actionDependencies.AiClient);
                actionAi.Show(); Remember(item); return;
            }
            if (item.BuiltIn == TextAction.RevealPath)
            {
                var path = PathActionPolicy.Resolve(ticket.Input);
                if (!path.Success) { status.Text = language.Get("Ocr_ActionUnavailable"); return; }
                using var session = (actionDependencies.CreatePathSession ?? (() => new PathRevealSession(System.IO.Path.Combine(AppContext.BaseDirectory, "Probe", "SnapBar.Probe.exe"))))();
                var prepared = await session.PrepareAsync(path.Path!, cancellation.Token);
                if (!Current() || cancellation.IsCancellationRequested) return;
                var identity = Native.GetIdentity(new WindowInteropHelper(this).Handle);
                if (prepared != "ok" || identity == null) { status.Text = language.Get("Ocr_ActionUnavailable"); return; }
                var revealed = await session.RevealAsync(identity, cancellation.Token);
                if (Current() && !cancellation.IsCancellationRequested && revealed == "ok") { status.Text = language.Get("Ocr_ActionComplete"); Remember(item); }
                else if (Current()) status.Text = language.Get("Ocr_ActionUnavailable");
                return;
            }
            if (item.BuiltIn == TextAction.Lookup)
            {
                var dictionary = actionDependencies.Dictionary;
                var installed = dictionary?.IsInstalled == true;
                DictionaryLookup? found = null; string? message = null;
                if (!installed) message = language.Get("SelectionController_Dictionary_000");
                else
                {
                    try
                    {
                        found = await Task.Run(() => dictionary!.Lookup(ticket.Input), cancellation.Token);
                        if (!dictionary!.Dictionaries.Any(entry => entry.Enabled)) message = language.Get("SelectionController_Dictionary_001");
                        else if (found.UnavailableSources.Count > 0) message = language.Get("SelectionController_Dictionary_002");
                    }
                    catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
                    { message = language.Get("SelectionController_Dictionary_003"); }
                }
                if (!Current() || cancellation.IsCancellationRequested) return;
                var matches = found?.Matches ?? []; var hasMatches = matches.Count > 0;
                var output = hasMatches ? string.Join("\n\n", matches.Select(match => $"【{match.DictionaryName}】\n{match.Text}")) : message ?? language.Get("SelectionController_Dictionary_004");
                actionResult?.Close(); actionResult = new ActionResultWindow(hasMatches ? matches[0].Headword : language.Get("SelectionController_Dictionary_005"), output,
                    false, false, lookup: true, messageOnly: !hasMatches, openSettings: actionDependencies.ManageDictionary,
                    openSettingsLabel: language.Get(installed ? "SelectionController_Dictionary_006" : "SelectionController_Dictionary_007"),
                    lookupMatches: matches, lookupWarning: hasMatches ? message : null);
                actionResult.Show(); actionResult.PlaceNear(region); if (hasMatches) Remember(item); return;
            }
            if (item.Custom is { Kind: not CustomActionKind.Url } custom)
            {
                var result = await (actionDependencies.RunCustom ?? CustomActionRunner.RunAsync)(custom, ticket.Input, cancellation.Token);
                if (!Current() || cancellation.IsCancellationRequested) return;
                var diagnostic = ScriptDiagnostic.Create(custom, result);
                var replaced = false;
                bool CanPresent() => Current() || replaced && !actionClosed && invocation == serial
                    && !cancellation.IsCancellationRequested && DefinitionCurrent(item);
                var host = new ActionOutputHost(CanPresent, () => status.Text = language.Get("Ocr_ActionComplete"), text =>
                {
                    try { Clipboard.SetText(text, TextDataFormat.UnicodeText); status.Text = language.Get("Ocr_ActionComplete"); }
                    catch (ExternalException) { status.Text = language.Get("MainWindow_Ocr_009"); ShowResult(text, false); }
                }, text => replaced = Replace(text), (text, allowApply) => ShowResult(text, allowApply,
                    allowApply && !string.IsNullOrWhiteSpace(result.Error) ? diagnostic : null), () =>
                {
                    actionResult?.Close(); actionResult = new ActionResultWindow(UiNames.Action(item, language), "", false, false, diagnostic: diagnostic, diagnosticOnly: true); actionResult.Show();
                });
                var saved = diagnostic.IsFailure || Remember(item);
                ActionOutputRouter.Route(custom.Output, result.Output, diagnostic.IsFailure, !string.IsNullOrWhiteSpace(result.Error), host);
                if (!saved) status.Text = language.Get("Ocr_ActionSaveFailed");
                return;
            }
            var destination = item.Custom is { Kind: CustomActionKind.Url } url ? CustomActionPolicy.ResolveUrl(ticket.Input, url.Content)
                : item.BuiltIn == TextAction.Search ? WebActionPolicy.Search(ticket.Input, actionDependencies.SearchTemplate())
                : WebActionPolicy.OpenLink(ticket.Input);
            if (!destination.Success || !Current()) { status.Text = language.Get("Ocr_ActionUnavailable"); return; }
            (actionDependencies.LaunchWeb ?? BrowserLauncher.Open)(destination.Uri!); status.Text = language.Get("Ocr_ActionComplete"); Remember(item);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or ArgumentException)
        { if (!actionClosed) status.Text = language.Get("Ocr_ActionFailed"); }
        finally
        {
            if (ReferenceEquals(actionCancellation, cancellation))
            {
                actionCancellation = null;
                if (actionCancel != null) ((Panel)more.Parent).Children.Remove(actionCancel);
                actionCancel = null;
                if (!actionClosed)
                {
                    more.IsEnabled = true;
                    if (status.Text == language.Get("Ocr_ActionRunning"))
                        status.Text = language.Get(cancellation.IsCancellationRequested ? "Ocr_ActionCancelled" : !Current() ? "Ocr_ActionTargetChanged" : "Ocr_ActionComplete");
                }
            }
        }
    }
    private bool Remember(ActionItem item)
    {
        var saved = actionDependencies.Save(ActionCatalog.Remember(actionDependencies.Settings(), item.Id));
        if (!saved) status.Text = language.Get("Ocr_ActionSaveFailed");
        return saved;
    }
}
