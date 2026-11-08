using System.IO;
using System.Text;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed partial class SelectionController
{
    internal OfflineDictionaryLibrary? Dictionary { get; set; }
    internal Action? OpenDictionarySettings { get; set; }
    private bool lookupResult;

    private async Task LookupAsync(bool direct)
    {
        if (executing || disposed || stopping || Dictionary is not { } dictionary || selection is not { RangeCount: 1 } current
            || !OfflineDictionary.CanLookup(current.Text)) return;
        var focused = direct ? Native.GetIdentity(Native.GetForegroundWindow()) == current.Target : popup.TryRestoreSourceFocus();
        if (!focused) { Invalidate(); Report("target-changed"); return; }
        var ticket = generation.Next();
        executing = true;
        popup.Dismiss();
        var shown = false;
        try
        {
            var initial = await ValidateCustomAsync(current, ticket, false);
            if (!initial.Success) { if (generation.IsCurrent(ticket)) Report(initial.Status); return; }
            DictionaryLookup? result = null;
            string? message = null;
            var offerSettings = false;
            var installed = dictionary.IsInstalled;
            if (!installed)
            {
                message = UiText.Get("SelectionController_Dictionary_000");
                offerSettings = true;
            }
            else
            {
                try
                {
                    result = await Task.Run(() => dictionary.Lookup(current.Text));
                    if (!dictionary.Dictionaries.Any(item => item.Enabled))
                    {
                        message = UiText.Get("SelectionController_Dictionary_001");
                        offerSettings = true;
                    }
                    else if (result.UnavailableSources.Count > 0)
                    {
                        message = UiText.Get("SelectionController_Dictionary_002");
                        offerSettings = true;
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException)
                {
                    message = UiText.Get("SelectionController_Dictionary_003");
                    offerSettings = true;
                }
            }
            if (!Current(current, ticket)) return;
            var final = await ValidateCustomAsync(current, ticket, false);
            if (!final.Success) { if (generation.IsCurrent(ticket)) Report(final.Status); return; }
            resultWindow?.Close();
            var matches = result?.Matches ?? [];
            var found = matches.Count > 0;
            var text = found ? string.Join("\n\n", matches.Select(match => $"【{match.DictionaryName}】\n{match.Text}"))
                : message ?? UiText.Get("SelectionController_Dictionary_004");
            var window = new ActionResultWindow(found ? matches[0].Headword : UiText.Get("SelectionController_Dictionary_005"), text,
                false, false, lookup: true, messageOnly: !found, openSettings: OpenDictionarySettings,
                openSettingsLabel: installed ? UiText.Get("SelectionController_Dictionary_006") : UiText.Get("SelectionController_Dictionary_007"), lookupMatches: matches,
                lookupWarning: found && offerSettings ? message : null);
            resultWindow = window;
            lookupResult = true;
            window.Closed += (_, _) => { if (resultWindow == window) { lookupResult = false; resultWindow = null; } };
            window.Show();
            window.PlaceNear(current.Bounds);
            shown = true;
            if (found) RememberBuiltIn(TextAction.Lookup);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        { Report("window-error"); }
        finally { executing = false; if (!shown) Invalidate(); }
    }
}
