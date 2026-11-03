# Push 前桌面驗證實作

依據 [設計](../specs/2026-11-03-pre-push-desktop-design.md)，實作範圍限定 hooks、驗證證據傳輸、GitHub workflow 與文件。

1. 建立 pre-push 與證據共用入口，嚴格核對來源、Release 報告及精簡紀錄；使用非強制 notes push。
2. 新增雲端匯入入口，維持現有發布指紋與套件 smoke 門檻。
3. 發布 workflow 改用 windows-latest，支援 push artifact，Release 才附加下載檔；notes 更新不觸發建置。
4. 安裝器保護既有 pre-commit／pre-push hooks；更新日常開發與發布文件。
5. 執行本機 bare remote 的 hook 整合測試、actionlint、完整 check.ps1，並視互動桌面狀態執行 Release 桌面測試。記錄未在 GitHub 實跑的限制。
