using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class ActionDiagnosticsTest
{
    private static IEnumerable<T> Children<T>(DependencyObject item) where T : DependencyObject
    {
        if (item is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())
            foreach (var descendant in Children<T>(child)) yield return descendant;
    }

    private static void Snapshot(Window window, string file)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
            drawing.DrawRectangle(window.Background, null, bounds);
            drawing.DrawRectangle(new VisualBrush(content), null, bounds);
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory("artifacts/diagnostics");
        using var stream = File.Create(Path.Combine("artifacts/diagnostics", file)); encoder.Save(stream);
    }

    public static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; var passed = 0; var exit = 1;
        void Check(bool ok, string name)
        { if (!ok) throw new InvalidOperationException(name); passed++; Console.WriteLine("PASS " + name); }
        app.Startup += async (_, _) =>
        {
            ActionResultWindow? window = null; ActionEditorWindow? editor = null;
            try
            {
                var action = new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"), "查詢公用 IP", CustomActionKind.PowerShell, "terminal", "curl ifconfig.me");
                const string originalError = "curl : Windows PowerShell is in NonInteractive mode. Read and Prompt functionality is not available.\r\n    + FullyQualifiedErrorId : InvalidOperation\r\n中文錯誤 😀";
                const string originalOutput = "first line\nsecond\tline 😀";
                var result = new ScriptExecutionResult("script-failed", originalOutput, originalError, 7);
                var diagnostic = ScriptDiagnostic.Create(action, result);
                Check(diagnostic.Summary.Contains("curl.exe") && diagnostic.Summary.Contains("互動確認"), "known curl noninteractive failure offers specific fix");
                Check(diagnostic.Details.Contains(originalError) && diagnostic.Details.Contains(originalOutput)
                    && diagnostic.Details.Contains("結束代碼：7"), "formatter retains exact Unicode multiline streams and exit code");
                Check(!ScriptDiagnostic.Create(action with { Content = "Read-Host 'input'" }, result).Summary.Contains("curl.exe"), "unrelated interactive command does not get curl advice");
                Check(!ScriptDiagnostic.Create(action, result with { Error = "generic network failure" }).Summary.Contains("curl.exe"), "unknown error does not invent curl advice");
                Check(!ScriptDiagnostic.Create(action, result with { Error = "Read-Host : Windows PowerShell is in NonInteractive mode. Read and Prompt functionality is not available." }).Summary.Contains("curl.exe"),
                    "presence of curl elsewhere in script does not misattribute Read-Host failure");
                Check(!ScriptDiagnostic.Create(action with { Kind = CustomActionKind.JavaScript }, result).Summary.Contains("curl.exe"), "PowerShell hint is not applied to JavaScript");
                const string clixml = "#< CLIXML\r\n<Objs Version=\"1.1.0.1\" xmlns=\"http://schemas.microsoft.com/powershell/2004/04\"><S S=\"Error\">curl : Windows PowerShell 處於非互動模式。讀取與提示功能無法使用。_x000D__x000A_</S><S S=\"Error\">位置：第一行_x000D__x000A_</S></Objs>";
                var xmlDiagnostic = ScriptDiagnostic.Create(action, result with { Error = clixml });
                Check(xmlDiagnostic.Summary.Contains("curl.exe") && xmlDiagnostic.Details.Contains("位置：第一行\r\n")
                    && xmlDiagnostic.Details.Contains(clixml), "localized CLIXML is readable while original stderr is retained");
                Check(ScriptDiagnostic.Create(action, new("ok", Error: clixml, ExitCode: 0)).IsFailure,
                    "recognized unavailable prompt is a failure even when PowerShell exits zero");
                Check(ScriptDiagnostic.Create(action, result with { Error = "#< CLIXML\n<Objs><S" }).Details.Contains("<Objs><S"), "truncated CLIXML safely falls back to original text");
                var python = action with { Kind = CustomActionKind.Python };
                var moduleError = "Traceback (most recent call last):\nModuleNotFoundError: No module named 'snapbar_missing_fixture'";
                var moduleDiagnostic = ScriptDiagnostic.Create(python, new("script-failed", Error: moduleError, ExitCode: 1));
                Check(moduleDiagnostic.IsFailure && moduleDiagnostic.Summary.Contains("環境缺少模組") && moduleDiagnostic.Details.Contains(moduleError), "Python missing module explains environment and retains traceback");
                Check(ScriptDiagnostic.Create(python, new("runtime-unavailable", ProductErrorCode: "Runtime_PythonMissing")).Summary.Contains("python.exe"), "Python missing runtime explains setup");
                Check(!ScriptDiagnostic.Create(python, new("script-timeout", Error: moduleError)).Summary.Contains("環境缺少模組"), "Python module hint does not hide timeouts");
                var successful = ScriptDiagnostic.Create(action, new("ok", "output", "additional diagnostic", 0));
                Check(!successful.IsFailure && successful.Summary.Contains("程序已結束") && !successful.Summary.Contains("失敗"), "stderr alone does not label successful process as failed");
                var truncated = ScriptDiagnostic.Create(action, new("script-output-too-large", "partial output", "partial error", OutputTruncated: true, ErrorTruncated: true, CaptureIncomplete: true));
                Check(truncated.Details.Contains("標準輸出已截斷") && truncated.Details.Contains("標準錯誤已截斷")
                    && truncated.Details.Contains("部分串流未能完整讀取") && truncated.Details.Contains("結束代碼：未取得"), "truncation and incomplete capture are explicit");
                Check(ScriptDiagnostic.Create(action, new("script-timeout", "partial")).Details.Contains("中止前"), "timeout explains retained partial output");

                window = new ActionResultWindow(action.Name, "", false, false, diagnostic: diagnostic, diagnosticOnly: true);
                window.Show(); window.UpdateLayout();
                var expander = Children<Expander>(window).Single();
                Check(!expander.IsExpanded && Children<TextBlock>(window).Any(t => t.Text == diagnostic.Summary), "diagnostic opens with visible summary and collapsed details");
                var buttons = Children<Button>(window).Select(AutomationProperties.GetName).ToArray();
                Check(buttons.Contains("複製詳細資訊") && buttons.Contains("關閉") && !buttons.Contains("套用至原文") && !buttons.Contains("複製結果"), "diagnostic exposes only copy details and close actions");
                window.InvalidateTarget();
                Check(Children<TextBlock>(window).Any(t => t.Text == diagnostic.Summary), "source invalidation does not replace diagnostic with paste instructions");
                Snapshot(window, "error-collapsed.png");
                expander.IsExpanded = true; window.UpdateLayout();
                var detailBox = Children<TextBox>(window).Single(t => AutomationProperties.GetAutomationId(t) == "ActionDiagnosticDetails");
                Check(detailBox.Text == diagnostic.Details && detailBox.IsReadOnly
                    && detailBox.VerticalScrollBarVisibility == ScrollBarVisibility.Auto, "expanded original diagnostics are readonly and scrollable");
                Snapshot(window, "error-expanded.png");
                window.Close(); window = null;

                window = new ActionResultWindow(action.Name, "successful output", false, true, diagnostic: successful);
                window.Show(); window.UpdateLayout();
                Check(Children<Button>(window).Any(b => AutomationProperties.GetName(b) == "套用至原文" && b.IsEnabled)
                    && Children<TextBox>(window).Any(t => t.Text == "successful output"), "successful output keeps result and apply behavior alongside stderr diagnostics");
                window.Close(); window = null;

                editor = new ActionEditorWindow(action with { Content = "throw 'fixture failure 中文'" });
                editor.Show(); editor.UpdateLayout();
                var test = Children<Button>(editor).Single(b => AutomationProperties.GetAutomationId(b) == "TestCustomAction");
                async Task RunEditorTest()
                {
                    test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var deadline = DateTime.UtcNow.AddSeconds(12);
                    while (!test.IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(20);
                    Check(test.IsEnabled, "editor test completes within deadline"); editor.UpdateLayout();
                }
                await RunEditorTest();
                var inline = Children<ActionDiagnosticView>(editor).Single();
                Check(inline.Visibility == Visibility.Visible && app.Windows.OfType<ActionResultWindow>().Count() == 0,
                    "real editor script failure shows inline without opening another window");
                Check(Children<TextBox>(inline).Single().Text.Contains("fixture failure 中文"), "editor inline view contains original script error");
                Children<Expander>(inline).Single().IsExpanded = true;
                Snapshot(editor, "editor-error.png");
                var script = Children<TextBox>(editor).Single(t => AutomationProperties.GetAutomationId(t) == "CustomActionContent");
                script.Text = "[Console]::Write('recovered')";
                Check(inline.Visibility == Visibility.Collapsed && Children<TextBox>(inline).Single().Text.Length == 0, "editing script clears previous diagnostic content");
                await RunEditorTest();
                Check(inline.Visibility == Visibility.Collapsed && Children<TextBox>(editor).Any(t => t.Text == "recovered"), "successful retest restores output and hides old failure");
                script.Text = "Read-Host 'input'";
                await RunEditorTest();
                Check(inline.Visibility == Visibility.Visible && Children<TextBlock>(inline).Any(t => t.Text.Contains("需要互動輸入")),
                    "nonterminating PowerShell prompt error remains visible even with exit zero");
                exit = 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL " + error.Message); }
            finally
            {
                window?.Close(); editor?.Close();
                Console.WriteLine($"{passed} passed, {(exit == 0 ? 0 : 1)} failed"); app.Shutdown();
            }
        };
        app.Run(); return exit;
    }
}
