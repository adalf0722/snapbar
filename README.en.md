# SnapBar

[繁體中文](README.md) · English

**Select text. Keep your next action close.**

SnapBar is a text-selection toolbar for Windows. Select text in the app you are using, press **Ctrl + Alt + V**, and copy or search beside your selection.

[Download the Windows x64 preview](https://github.com/adalf0722/snapbar/releases)

## Get started

1. Open a preview release, expand **Assets**, and download `SnapBar-*-win-x64.zip`.
2. Extract the entire ZIP to one folder, then open `SnapBar.App.exe`. The unsigned, portable preview includes the .NET runtime.
3. Under **General → 語言 / Language**, choose **English** if needed. New installations follow your Windows display language, with English used for unsupported languages. Existing installations keep Traditional Chinese until you change it.
4. Select a sentence in Notepad or a browser, press **Ctrl + Alt + V**, then choose **Copy** or **Search**.

Enable **Show toolbar automatically** under **General** to show the toolbar after dragging or double-clicking to select text. The default is manual activation. You can change the toolbar shortcut or assign direct Search, Copy, and OCR shortcuts under General.

Changing language updates settings and the tray immediately. Editor and result windows that are already open keep their language and contents; reopen them to use the new language. Your shortcuts, custom actions, AI instructions and OCR language priority remain unchanged.

## What you can do

- **Copy, cut, paste and search.** Read-only sources can still support Copy and Search. Select a complete HTTP/HTTPS URL or an absolute file path to use **Open link** or **Show in folder** from **⋯**.
- **Arrange your toolbar.** Pin up to nine actions, change their order, hide unused actions, or switch between icons and labels. Actions that do not fit move into **⋯**. You can also customize the search URL.
- **Create actions.** URL actions insert URL-encoded selected text at `***`. PowerShell and JavaScript actions receive text through `POPCLIP_TEXT` and run with your Windows user permissions. JavaScript actions require Node.js; ordinary Copy and Search do not.
- **Rewrite with AI.** Configure your provider endpoint, model and any required API key under **AI services**. Then choose **AI rewrite** from **⋯**. Review or copy the result; replacement is offered only when the source is editable and revalidation succeeds. Provider charges may apply. Local compatible services can work without a key.
- **Look up words offline.** Import a StarDict 2.4.2 dictionary through the dictionary manager for the Lookup action. Keep its `.ifo`, `.idx`, and `.dict` or `.dict.dz` together. UTF-8 plain-text definitions and phonetics are supported; dictionaries are supplied by you. Enable multiple dictionaries and view results by source.
- **Capture text with OCR.** Choose the OCR capture action from the tray menu or assign an OCR shortcut. Drag a rectangle on one screen; Esc cancels. Traditional Chinese and English recognition runs locally. Models are included; the OCR engine also needs the [Microsoft Visual C++ x64 runtime](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist). Correct the result before copying or searching it.

## If the toolbar does not appear

1. Click the SnapBar tray icon to open settings. Under **About and support**, expand diagnostics and open the test window. Select some test text and press the shortcut.
2. Check the toolbar shortcut under **General**. If another app has reserved it, choose a different combination.
3. If only one app is affected, check selection compatibility under **Compatibility**. Sublime Text support needs explicit first-time setup of guarded key bindings; the choice is saved.

Passwords are rejected. Selection support varies between apps. If focus or selection changes, reselect the text before trying to replace it. Excluded apps skip automatic detection but remain available through the manual shortcut.

## Background operation and updates

Closing settings leaves SnapBar running in the system tray. Click the tray icon to return to settings. Use **Quit SnapBar** to exit completely. Pausing automatic detection leaves the manual shortcut available.

Before updating, quit the previous copy from its tray menu, extract the new ZIP completely, and start the new executable. Settings and encrypted AI keys remain under the same Windows account. Login startup is optional under General; re-enable it after moving the application folder. There is no automatic updater.

## Your data

SnapBar first tries Windows UI Automation. Compatibility capture can temporarily use the clipboard and attempt to restore it; temporary text may appear in Windows clipboard history. Settings do not store selected source text or clipboard contents.

Search and URL actions send text to the chosen browser destination. Scripts receive text when executed. AI requests send text only when you explicitly run an instruction; keys are encrypted for your Windows account. OCR images stay in local memory, and dictionary lookup stays local.

## Help and development

- [Report an issue](https://github.com/adalf0722/snapbar/issues)
- [Package instructions](packaging/README.en.md)
- [Development and release guide (Traditional Chinese)](docs/development.md)
- [Validation record (Traditional Chinese)](docs/p0-validation.md)
