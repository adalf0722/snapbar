# Push main 自動發布預覽版

使用者已選擇：push main 後自動發布預覽版，從現有 rc.4 接續遞增 rc.5、rc.6；完整保留 push 前桌面測試與雲端發布驗證。

## 流程與版本

沿用同一條 workflow，main push 才自動發布；其他分支與手動演練維持 artifact，手動建立 Release 仍支援附檔。不採用所有分支公開發布，避免測試分支也產生公開版本；不使用正式版自動遞增，依使用者選擇維持預覽版。

package 工作以唯讀權限列出現有 tags 與 Releases，選擇 `v0.1.0-rc.N` 最大 N 加一，數字比較，草稿及已占用 tag 也算保留版本。main 工作維持序列執行，不取消正在執行的發布；若其他人工操作搶先占用版本，拒絕覆寫並讓維護者重跑。版本系列暫固定 v0.1.0，升級系列需明確調整設定。

桌面紀錄、完整 checks、self-contained 發布、Probe／Startup smoke、ZIP 與跨 runner SHA-256 核對全部成功後，最後一個具有 contents write 的工作下載已驗證的相同產物，建立對應確切 commit 的草稿、上傳 ZIP 與校驗檔，確認完整附件後才發布為 prerelease。不依賴 GITHUB_TOKEN 所建立的 tag／Release 再觸發另一條 workflow。

Release 說明包含執行方式、原始碼 commit、驗證範圍、預覽版限制與 GitHub 自動產生的更新說明。版本及來源均使用結構化環境變數傳遞，不把不受信任的事件文字直接拼成 shell 指令。

## 重跑與失敗

每次自動發布含固定 workflow run ID 標記。相同 run 的失敗工作重跑沿用版本與 artifact；其他 run 或人工建立的相同版本拒絕覆寫。已上傳的附件只有在 SHA-256、名稱及大小符合時才略過，缺少的附件補上，不使用 --clobber。未完整上傳前保持草稿。

整條流程重跑時若已存在同 run 的 Release，沿用原版本，避免產生第二個版本。重新建置可能改變 ZIP 位元組；此時拒絕覆寫既有不同的附件，應優先使用 Re-run failed jobs 接續原產物。

## 驗證

測試版本數字遞增、忽略其他系列、草稿／tags 占用、同 run 恢復、附件匹配與衝突拒絕；以本機受控 gh 替身驗證草稿建立、附件補傳、發布順序及失敗時不發布。完整執行既有 check.ps1 與 actionlint。未經真實 main push 不宣稱雲端自動發布已實跑成功。
