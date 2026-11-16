# 統一選字讀取實作計畫

依已核准的 UIA 快速讀取＋統一 Ctrl+C fallback 設計實作，包含使用者明確要求的未知 App 擴充。

1. 將原生重試、備援路由、目標綁定及退避移入 Core 的 SelectionCapturePipeline。App 只解析已驗證來源的讀取計畫。
2. 保留通用 UIA 相容候選，新增未知 App 的正向原生／MSAA 能力驗證。企業微信及 Sublime 僅保留必要適配與原有安全條件。
3. 加入原生成功零複製、終止狀態零複製、過期及異目標拒絕、退避隔離的行為測試。以真實原生控制項驗證 MSAA 互通、密碼框及不同輸入框拒絕。
4. 執行 Core、NativeClipboard、Compatibility、UiaReadiness、UiaDeepTree、Clipboard、ClipboardRaces、儲存庫檢查及隔離 Probe 協定 smoke。保存原始報告，更新驗證紀錄。
5. 啟動本次開發版供使用者實機測試；正式自含式發布仍需完整 Release 證據。

五步已完成；自動回歸、企業微信本輪讀取及限制見 [驗證紀錄](../../2026-11-17-unified-selection-validation.md)。
