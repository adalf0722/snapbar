using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;
using SnapBar.Windows.Ocr;

internal static class OcrActionsTest
{
    private static object? Field(object instance, string name) => instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance);
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found) yield return found;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Children<T>(child)) yield return item;
    }
    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var passed = 0; var failed = 0;
        void Check(string name, bool condition)
        { Console.WriteLine((condition ? "PASS " : "FAIL ") + name); if (condition) passed++; else failed++; }
        app.Startup += async (_, _) =>
        {
            try
            {
                foreach (var output in new[] { ActionOutput.Window, ActionOutput.Markdown, ActionOutput.Hud, ActionOutput.ReplaceSelection })
                {
                    var action = new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"), "Fixture", CustomActionKind.PowerShell,
                        "terminal", "fixture", output);
                    var settings = ActionCatalog.Normalize(new ActionSettings { CustomActions = [action] });
                    var calls = 0;
                    var dependencies = new OcrActionDependencies(() => settings, () => "https://example.org/?q={query}",
                        value => { settings = value; return true; }, () => { }, null,
                        RunCustom: (_, _, _) => { calls++; return Task.FromResult(new ScriptExecutionResult("ok", "fixture output", "fixture warning", 0)); });
                    var card = new OcrResultWindow("https://example.org/?q={query}", null, dependencies);
                    card.SetResult(new OcrResult("ok", "fixture input")); card.Show();
                    var editor = Children<TextBox>(card).Single();
                    var more = Children<Button>(card).Single(button => AutomationProperties.GetAutomationId(button) == "OcrMore");
                    if (output == ActionOutput.Window)
                    {
                        Check("More shows its icon with accessible label", ((StackPanel)more.Content).Children.OfType<System.Windows.Controls.Viewbox>().Any()
                            && AutomationProperties.GetName(more) == UiText.Get("Ocr_More"));
                    }
                    more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var invalidLink = more.ContextMenu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "OcrAction_link");
                    if (output == ActionOutput.Window)
                        Check("disabled action exposes its reason", !invalidLink.IsEnabled && ToolTipService.GetShowOnDisabled(invalidLink)
                            && AutomationProperties.GetHelpText(invalidLink) == (string)invalidLink.ToolTip);
                    more.ContextMenu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "OcrAction_" + action.Id)
                        .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    await Task.Delay(50);
                    var result = (ActionResultWindow?)Field(card, "actionResult");
                    var diagnostic = result == null ? null : Children<ActionDiagnosticView>(result).SingleOrDefault();
                    Check(output + " presents successful stderr", calls == 1 && result?.IsVisible == true && diagnostic != null
                        && Children<TextBox>(diagnostic).Single().Text.Contains("fixture warning", StringComparison.Ordinal));
                    if (output == ActionOutput.ReplaceSelection)
                    {
                        Check("replacement is committed once and diagnostic has no apply", editor.Text == "fixture output"
                            && Field(result!, "applyButton") == null);
                    }
                    else
                    {
                        Check(output + " preserves input before explicit apply", editor.Text == "fixture input");
                        editor.Text = "changed after result";
                        var apply = (Button?)Field(result!, "applyButton");
                        var disabled = apply?.IsEnabled == false;
                        apply?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Check(output + " invalidates stale apply", disabled && editor.Text == "changed after result");
                    }
                    if (output == ActionOutput.Hud)
                    {
                        await Task.Delay(2700);
                        Check("HUD warning remains visible beyond ordinary HUD timeout", result?.IsVisible == true);
                    }
                    result?.Close(); card.Close();
                }
            }
            catch (Exception error) { Console.WriteLine("FAIL exception " + error); failed++; }
            finally { foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close(); app.Shutdown(); }
        };
        app.Run(); Console.WriteLine($"{passed} passed, {failed} failed"); return failed == 0 ? 0 : 1;
    }
}
