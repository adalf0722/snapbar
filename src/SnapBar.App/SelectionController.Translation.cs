using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed partial class SelectionController
{
    private TranslationService? translation;
    private Action? openTranslationSettings;

    internal void ConfigureTranslation(TranslationService service, Action openSettings)
    {
        translation = service;
        openTranslationSettings = openSettings;
    }

    private async Task TranslateAsync(bool direct)
    {
        if (executing || selection is not { } current || disposed || stopping || translation == null) return;
        var sourceFocused = direct ? Native.GetIdentity(Native.GetForegroundWindow()) == current.Target
            : popup.TryRestoreSourceFocus();
        if (!sourceFocused) { Invalidate(); Report("target-changed"); return; }
        var settings = translation.Load();
        if (settings == null)
        {
            Invalidate();
            openTranslationSettings?.Invoke();
            return;
        }

        executing = true;
        var ticket = generation.Next();
        popup.Dismiss();
        using var cancellation = new CancellationTokenSource();
        customAction = cancellation;
        var keepResult = false;
        try
        {
            var validation = await ValidateCustomAsync(current, ticket, false);
            if (!validation.Success) { if (generation.IsCurrent(ticket)) Report(validation.Status); return; }
            if (!Current(current, ticket)) return;
            Status?.Invoke("正在使用 Azure Translator 翻譯…");
            var result = await translation.TranslateAsync(settings, current.Text, cancellation.Token);
            if (!Current(current, ticket) || cancellation.IsCancellationRequested) return;
            if (result.Text is not { } text)
            {
                var message = result.Status switch
                {
                    "credentials" => "Azure Translator 拒絕驗證，請檢查金鑰與區域。",
                    "quota" => "Azure Translator 已達用量限制，請稍後再試。",
                    "timeout" => "翻譯逾時，請稍後再試。",
                    "length" => "選取文字為空或過長，請重新選字。",
                    _ => "翻譯未完成，請檢查網路與翻譯設定。"
                };
                ShowTranslationWindow(new("Azure Translator", message, false, false, compact: true, messageOnly: true), current);
                Status?.Invoke(message);
                return;
            }

            var window = new ActionResultWindow("Azure Translator · 譯文", text, false,
                SessionPolicy.CanWrite(validation.Selection, current.Target),
                () => _ = ApplyResultAsync(current, ticket, text), compact: true);
            ShowTranslationWindow(window, current);
            RememberBuiltIn(TextAction.Translate);
            keepResult = true;
            Status?.Invoke("譯文已顯示。");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        { Report("window-error"); }
        finally
        {
            customAction = null;
            executing = false;
            if (!keepResult) Invalidate();
        }
    }

    private void ShowTranslationWindow(ActionResultWindow window, SelectionSnapshot current)
    {
        resultWindow?.Close();
        resultWindow = window;
        window.Closed += (_, _) => { if (resultWindow == window) resultWindow = null; };
        window.Show();
        window.PlaceNear(current.Bounds);
    }
}
