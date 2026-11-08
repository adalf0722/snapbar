# SnapBar for Windows — quick start

[繁體中文](README.md) · English

## Start with Copy or Search

1. Extract every file in this ZIP to one folder. Open `SnapBar.App.exe` on Windows x64. The .NET runtime is included.
2. Open **General → 語言 / Language** and choose **English** if needed. New installations follow Windows display language; unsupported languages use English. Upgrading an existing installation keeps Traditional Chinese until you change it.
3. Open Notepad, type a disposable sentence, and select it. Press **Ctrl + Alt + V** to show the toolbar, then choose **Copy** or **Search**. You do not need an AI account, Node.js or a dictionary for these actions.

The preview is unsigned. Keep the `Probe` and language-resource folders beside the executable. For help or downloads, visit [SnapBar on GitHub](https://github.com/adalf0722/snapbar).

## Settings and language

Click the tray icon to return to settings. Enable **Show toolbar automatically** under General if you want dragging or double-clicking to trigger the toolbar. General also lets you change shortcuts and optionally start SnapBar when you sign in to Windows.

The language options are **System**, **Traditional Chinese**, and **English** in the English interface. The choice is saved. Settings and tray labels change immediately; already-open editor and result windows keep their language and contents until reopened. Language changes do not alter your AI instructions, OCR language priority or other saved preferences.

Settings live under `%LOCALAPPDATA%\SnapBar`. Existing valid settings upgrade while preserving preferences; unreadable, damaged or newer-format files are not overwritten. Keep this directory private: it includes your custom actions and Windows-encrypted AI credentials.

## More actions

- Use **⋯** for more actions. Complete HTTP/HTTPS links can be opened; absolute file paths can be revealed without executing the file. Invalid or inapplicable actions are disabled with an explanation.
- Pin up to nine actions and reorder them in toolbar settings. Custom URL actions use `***` for URL-encoded selected text. PowerShell/JavaScript scripts read `POPCLIP_TEXT`; JavaScript requires a separate Node.js installation. Scripts run with your Windows account permissions. Saving or importing does not execute them; the test button does.
- For AI rewrite, configure your endpoint, model and any required API key in **AI services**. Only executing an instruction sends text to the chosen service. You can copy results; replacement requires a still-valid editable source. Services may charge for usage.
- For the Dictionary action, import your own StarDict 2.4.2 `.ifo`, with matching `.idx` and `.dict` or `.dict.dz` files in the same folder. Supported definitions are UTF-8 plain text and phonetics. Multiple dictionaries can be enabled independently. No dictionary is bundled.
- For local OCR, use the tray capture action or assign an OCR shortcut. Drag a rectangle on one screen; Esc cancels. English and Traditional Chinese models are included. Install the [Microsoft Visual C++ x64 runtime](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist) if it is missing. Correct the recognized text before copying or searching it.

## If selection fails

Check the shortcut under General and try the test window under **About and support → diagnostics**. Selection compatibility is enabled by default and can temporarily use the clipboard. Sublime support requires explicit first-time setup; its guarded key-binding choice is saved. Different apps expose different selection information.

Password fields are rejected. Read-only or uncertain sources do not authorize replacement. Actions revalidate the original foreground window and selection before writing. Test Cut/Paste on disposable text first. Compatibility capture may appear in clipboard history, and concurrent clipboard changes or forced termination can prevent restoration.

## Quit or update

Closing settings keeps SnapBar running in the tray. Use **Quit SnapBar** in settings or the tray menu to exit completely. Pausing automatic detection does not disable the manual shortcut. Close any separate test window yourself.

Before updating, quit the old copy, extract the new ZIP completely, and start its executable. Saved preferences and AI keys are reused for the same Windows account. If you move the folder, re-enable optional login startup. There is no automatic updater.

For detailed test evidence and known limitations, see `VALIDATION.md` and `CLIPBOARD-RECOVERY.md` (Traditional Chinese). Report problems at [GitHub Issues](https://github.com/adalf0722/snapbar/issues), without posting selected text, clipboard contents or API keys.
