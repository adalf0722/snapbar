# 使用入門選字辨識實作計畫

**Goal:** 第一眼就能辨識唯一可選的教材，選取後得到真實且不誤導的下一步提示。

**Architecture:** 在既有教學 Presentation 局部類別中分離教材表面與鍵帽列；以真正 TextBox.SelectionChanged 更新局部提示，不更動控制器、IPC 或進度保存。

**Tech Stack:** .NET 10、WPF、雙語 resx、現有桌面 runner。

設計：[使用入門選字辨識](../specs/2026-11-14-onboarding-selection-affordance-design.md)。使用者已明確要求優化；在目前 checkout 內實作並驗證，不推送或發布。

- [x] 修改 `src/SnapBar.App/OnboardingPracticeWindow.Presentation.cs`：BuildPractice 只放真實 TextBox；BuildShortcut 獨立放在表面外，保留完整快捷鍵的可及性名稱。
- [x] 為真正可選表面指定 OnboardingSelectionSurface，教材游標 I-beam；滑入、聚焦及選字更新邊界／輕量底色，保持邊界寬度固定，避免選字時畫面跳動。
- [x] 以 SelectionChanged 更新固定寬度的 OnboardingSelectionCue，空選取與非空選取分別顯示選好後按／現在按；不更動 Completed 或送出協定訊息。
- [x] 修改 `Resources/Strings.resx`、`Strings.zh-Hant.resx`：刪除含糊的 PracticeLabel，說明明確指向下方句子，新增已選取提示；兩語格式一致。
- [x] 更新 `tests/SnapBar.Windows.Tests/OnboardingTest.cs`：驗證教材表面沒有其他標籤、鍵帽在表面外、I-beam、真實選取／清除回饋與不誤判完成，加入實際反白的截圖。保留完整 UIA／搜尋與既有退場案例。
- [x] 正常結束舊開發版後執行 `./scripts/test-desktop.ps1 -Suite Appearance,Language,Lifecycle`，逐張檢閱初始、真實選取、完成與小視窗畫面；必要時針對失敗修正後重跑。
- [x] 新增唯一 `release-notes/2026-11-14-onboarding-selection-affordance.json`，更新 `docs/p0-validation.md` 的實際證據及限制；確認中文 UTF-8 無 BOM 與暫存區檢查後提交。

實際多 DPI、新手盲測與真實瀏覽器載入不以程式化測試替代。只宣稱可驗證的改變，不宣稱已達到不可量測的「完美」。
