# SnapBar AI panel 設計

使用者於 2026-11-01 依六輪互動線框確認設計，並以「可以，go」授權實作。參考線框保存在 `.superpowers/brainstorm/2026-11-01-ai-panel/content/ai-panel-workflow-v6.html`。

## 已確認的體驗

800 × 630 DIP 的原生 WPF 面板，左側約 280 DIP 為常駐指令區，右側以「原文／結果」分頁共用閱讀區。開啟直接顯示純文字原文；尚無結果時結果分頁停用。完成後切到結果，但等待期間使用者已手動切換時，保留其畫面並標示新結果。

只有一個指令輸入框，輸入即搜尋全部已存指令，名稱、內容及中英文別名皆可搜尋。輸入框 Enter 執行自己寫的內容；方向鍵或點擊選到已存指令後 Enter 執行該指令。Shift+Enter 換行，繁中 IME 組字確認不得送出。空白輸入不執行。釘選與最近使用優先且不重複，分類收在預設關閉的篩選中，只限制空白查詢的瀏覽。

五個內建指令：修正文法拼字、改善寫作、縮短、摘要、翻成英文。支援新增、編輯、刪除及釘選自訂指令，最多 128 個。指令與設定可跨次啟動保存，原文、輸出、自由指令及版本歷史只存在本次面板記憶體。

完成後預設接續新版本，顯示「基於：第 n 版」，清空已送出的輸入並提示下一次要求。等待時新寫的草稿與其原處理對象必須保留。提供「從原文重做」及必要時「接續這一版」。歷史最多 20 版，上一版後重新產生將截斷該分支；每版保存其指令及處理來源供回看。

結果主按鈕為「替換原文」，次按鈕為「複製結果」，說明替換也會複製結果。唯讀、未知編輯狀態與通用剪貼簿相容來源不提供寫回。來源失效後保留結果，突出複製操作。

## 請求與設定

第一個垂直流程支援 OpenAI Responses 與自訂 Chat Completions 相容服務，各自記住 endpoint 與手動輸入的 model ID。模型不寫死。一般 AI 設定獨立存入 `%LOCALAPPDATA%/SnapBar/ai.json`，保留既有 settings.json schema 6 與使用者動作設定。

API Key 依 profile、協定與完整 endpoint 分別使用 DPAPI CurrentUser 保護，存入獨立機密目錄；更換 endpoint 不沿用舊服務的金鑰。UI 不回填完整金鑰、不匯出機密。無金鑰時有設定入口；相容服務可使用不需金鑰的本機模型。

只在明確執行後傳送文字。固定轉換指示、使用者轉換要求與原文資料分開包裝；不執行模型產生的動作、HTML、腳本或外部資源。OpenAI 請求使用 `store:false`，不使用遠端會話。HTTP 請求 60 秒上限，可停止；回應最多 2 MiB，輸出最多 262,144 UTF-16 code units。停止、失敗與過期結果皆保留上一版，空白及不完整結果不可套用。

錯誤以本機固定訊息顯示缺少設定、認證、429、逾時、連線、回應格式與超限，不顯示原始 response body、網址中的機密或例外字串。自訂 endpoint 允許 HTTPS 及 loopback HTTP，拒絕帳密、query、fragment 與遠端明文 HTTP；不跟隨重新導向。

官方格式依據：[文字產生](https://developers.openai.com/api/docs/guides/text)、[Responses 遷移](https://developers.openai.com/api/docs/guides/migrate-to-responses)、[Chat Completions](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create)。

## 選取與寫回

AI 是可在管理動作中安排的內建動作，新增時預設在更多選單，保留既有釘選位置。開啟前依現有 Probe 與相容政策重驗選取。面板與其自有設定／指令對話框取得焦點時不失效；外部輸入、切換其他 App、新選取及關閉會失效舊目標並取消在途請求。失效後仍可自行處理記憶體內的文字及複製結果。

替換必須由 AI 面板發起，且面板仍在前景。只可返回原程序、原 HWND，並再次驗證原控制項、選取內容、可編輯狀態與 generation。沿用現有貼上路徑及剪貼簿序號檢查；不使用 SetValue 替換整個欄位，不搶回其他 App 的前景。貼上指令已送出不代表目標一定完成，返回來源由使用者確認結果；失敗保留面板及結果。

## 驗證

Core runner 覆蓋全域搜尋、去重排序、設定邊界、版本分支、取消／晚到結果、草稿保留與處理來源。HTTP 以注入 handler 驗證兩種 wire format、限制、錯誤及取消，不使用真實金鑰；DPAPI 在暫存目錄往返。WPF runner 覆蓋分頁、IME、鍵盤、設定、指令管理及真實控制器的焦點／來源失效與替換。保留現有動作、相容性、剪貼簿及設定回歸；無真實服務金鑰時明確保留連線驗收限制。
