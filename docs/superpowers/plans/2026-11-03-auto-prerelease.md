# Push main 自動發布實作

1. 新增自動版本解析與 Release 附件一致性共用函式。
2. 加入版本解析與草稿上傳／發布腳本，來源固定為 workflow 的 SHA，保留失敗工作重跑能力。
3. workflow 僅 main push 啟用自動預覽版；傳遞版本、更新說明基準與 artifact 至最後寫入工作。
4. 新增純政策與受控 gh 的流程測試，接入 check.ps1，更新開發文件。
5. 執行完整檢查與 UTF-8 驗證，記錄本機結果及尚待 main push 的雲端驗證。
