# GoldenDict lookup prototype

## Goal

Let a user look up selected text in an installed GoldenDict-ng popup. Validate the usefulness and responsiveness of offline lookup before building a dictionary engine into SnapBar.

## Approach

Add a built-in lookup action to SnapBar's action catalog and expose it through the existing selection toolbar and action settings. When explicitly invoked, pass the selected text to GoldenDict-ng's documented popup interface. Use the existing selection safety and cancellation rules, and pass text as an argument without shell interpolation. Do not log selected text.

Discover an installed GoldenDict-ng using an appropriate Windows application registration or executable path. If it cannot be found or launched, show an actionable message explaining that GoldenDict-ng and a dictionary must be installed. Do not silently send the text to an online service. GoldenDict-ng and dictionary data are separately installed; their availability and licensing are not assumed.

Preserve existing Azure translation and persisted action choices. Do not introduce a StarDict parser, bundle dictionaries, send text to AI, or change clipboard behavior. The prototype invokes a lookup; elapsed time, lookup coverage, and definition quality require separate measurement with representative text and dictionaries.

## Validation

Test argument construction, missing application and launch failure, action availability, and preservation of existing preferences. Run the relevant existing test runners. Where GoldenDict-ng and a suitable dictionary are available, verify a selected word opens its popup and measure elapsed time; otherwise report that end-to-end verification remains outstanding.
