# 使用入門視覺精修實作計畫

**Goal:** 讓單一練習畫面具有清楚的視覺重心、真實鍵帽與緊湊完成狀態。

**Architecture:** 將教學視窗的排版集中到 `OnboardingPracticeWindow.Presentation.cs` 局部類別；原檔保留訊息處理、生命週期與實際操作。沿用 ActionUi 與 WindowWorkArea，維持原生 WPF 控制項。

**Tech Stack:** .NET 10、WPF、雙語 resx、既有桌面 console runner。

設計：[使用入門視覺精修](../specs/2026-11-14-onboarding-polish-design.md)。使用者已批准視覺方向，本輪在現有 checkout 內實作，不推送／發布。

## 工作

- [x] `src/SnapBar.App/OnboardingPracticeWindow.cs` 改為 partial，成功狀態呼叫重繪；配置預設 660 × 590 DIP。
- [x] 新增 `src/SnapBar.App/OnboardingPracticeWindow.Presentation.cs`，以 Grid 的 Auto／Star／Auto 分別放頂部、可捲動內容和固定操作區。將原 Render／MakeButton／Identify／Error／SetPending 移到此檔，集中視覺欄位。
- [x] 真正教材 TextBox 維持 OnboardingSample 與唯讀；練習表面去掉內層輸入框邊線。快捷鍵以 `configuration.Shortcut.Split(" + ", StringSplitOptions.None)` 呈現動態鍵帽，保留完整可及性名稱 OnboardingShortcutKeys。
- [x] 初始只顯示必要操作說明，錯誤才展開提示。完成狀態使用勾選標記、精簡教材及偏好區，收起已完成操作提示；固定底部保留開始使用與開啟設定。
- [x] 更新 `Resources/Strings.resx`、`Strings.zh-Hant.resx` 的教學短文案，新增鍵帽前後提示及教材標籤；雙語格式參數一致。
- [x] 更新 `tests/SnapBar.Windows.Tests/OnboardingTest.cs`，驗證動態鍵帽語意名稱、完成後固定操作可見、小視窗主要操作可達，更新普通／完成／衝突畫面截圖。保留既有保存失敗、過期回應、真實 UIA／搜尋及退場路由測試。
- [x] 正常結束舊開發版後執行 `./scripts/test-desktop.ps1 -Suite Lifecycle,Appearance,Language`；若畫面或斷言失敗，針對原因修正並重跑受影響套件。逐張檢閱 `artifacts/ui-polish/onboarding-*.png`。
- [x] 新增 `release-notes/2026-11-14-onboarding-polish.json`，更新 `docs/p0-validation.md` 的實際證據與限制。檢查中文 UTF-8 無 BOM、差異及暫存區，再提交。

不新增與靜態排版互為鏡像的測試，不更動選字安全與進度協定。實際多 DPI 與新手試做尚未完成時，持續明示限制。
