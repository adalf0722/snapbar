# AI Fluent Icons Implementation Plan

**Goal:** Deliver offline named Fluent icons and a searchable, virtualized picker without losing existing user choices.

**Architecture:** A dedicated catalog owns metadata, name resolution and bundled font rendering. A small presenter works in WPF templates. The editor owns its draft selection and opens an owned picker; AI command rendering uses the catalog's effective factory defaults. Core formats and selection safety remain unchanged.

- [x] Vendor the pinned Microsoft Regular font, codepoint map, LICENSE and provenance under `src/SnapBar.App/Assets/FluentIcons`; include them as resources and ship attribution. Preserve a baseline package size of 326747611 bytes.
- [x] Add `AiIconCatalog.cs` and `AiIconPresenter.cs`: lazy immutable metadata, full/bare/prefixed name resolution, legacy IDs, common Chinese labels/aliases and categories, one entry per shape, supplementary codepoints and five factory presentation defaults. Use bundled FontFamily and verify GlyphTypeface coverage; report actual resource sizes and glyph coverage.
- [x] Add `AiIconPickerWindow.cs` plus row/tile templates: query and category controls, preview/count, six-column row virtualization, click/arrow/Enter selection, Escape/cancel. Store only the selected ID. Check realized row count instead of assuming virtualization works.
- [x] Replace the editor icon ComboBox with a name field, live preview and picker action; preserve unknown unchanged IDs, validate changed values and normalize accepted official names. Reuse the catalog in command tiles; keep other action icons unchanged.
- [x] Update editor tests and add catalog/picker desktop checks for real glyph coverage, factory/custom distinction, search, aliases, invalid input, cancellation, keyboard, persistence and minimum-size layouts. Run check.ps1, focused AiPanel and inspect screenshots.
- [x] Run Release + Layout against final source, record asset/performance measurements and validation, publish with the successful report, launch the package and verify the user ai.json hash is unchanged. Commit only owned changes and the appended validation section.

Completed inline. Final desktop evidence: artifacts/checks/desktop/20261101T100034035Z-1709ab6994c14ecf8d7230398e315160/report.json (AiPanel 103 and all Release/Layout suites). Publish evidence: artifacts/checks/build/20261101T100303622Z-85ed9b180420424cbe957933161d0393/report.json. Package started and returned to tray; user AI settings hash unchanged. Initial failed clipboard/replacement runs and single-machine resource measurements are recorded in docs/p0-validation.md.
