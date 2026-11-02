# AI 服務 Provider 擴充設計

日期：2026-11-03。使用者已同意新增 Gemini、Claude、DeepSeek、Groq、OpenRouter、xAI、MiniMax，採用「預設服務＋共用協定」方案。使用者另同意產品體驗審閱提出的三項修訂：進階網址、建議模型與官方入口、服務測試及既有相容性保留；本修訂文件待使用者檢閱後進入實作規劃。

## 目標與範圍

AI 服務下拉選單提供九個選項：OpenAI、Gemini、Claude、DeepSeek、Groq、OpenRouter、xAI、MiniMax、自訂相容服務。主要成功路徑為「選擇服務、貼上金鑰、選擇模型、測試成功、儲存後開始改寫」。服務測試可略過，儲存不以測試成功為前提。

本次處理服務預設值、設定升級、協定呼叫、設定介面、隨版本維護的建議模型與服務測試。不增加模型清單下載、自動選模型、串流、工具呼叫、圖片、額外服務帳號管理或自動切換供應商。保留目前選字安全、前景驗證、取消、原文與結果版本行為。

## 方案選擇

採用服務預設清單搭配共用協定：OpenAI 保留 Responses，Claude 新增 Anthropic Messages，其餘新增服務使用 Chat Completions。此方案符合現有文字轉換需求，且服務設定與協定處理可以獨立維護。

未採用每家原生協定，因目前用途不需要各家完整功能；未統一經由 OpenRouter，因使用者需要直接使用各家服務與金鑰。

## 元件與預設服務

Core 增加獨立的服務預設清單，負責穩定 ID、名稱、協定、完整請求網址、建議模型，以及取得金鑰與模型清單的官方連結。AiSettings 與設定載入共用這份清單，避免 App 與 Windows 各自維護網址。AiProviderProfile 保留既有形狀，AiProtocol 在現有值後新增 Messages，不改變既有數值序列化含義。

| 穩定 ID | 名稱 | 協定 | 完整預設網址 |
| --- | --- | --- | --- |
| openai | OpenAI | Responses | https://api.openai.com/v1/responses |
| gemini | Gemini | ChatCompletions | https://generativelanguage.googleapis.com/v1beta/openai/chat/completions |
| claude | Claude | Messages | https://api.anthropic.com/v1/messages |
| deepseek | DeepSeek | ChatCompletions | https://api.deepseek.com/chat/completions |
| groq | Groq | ChatCompletions | https://api.groq.com/openai/v1/chat/completions |
| openrouter | OpenRouter | ChatCompletions | https://openrouter.ai/api/v1/chat/completions |
| xai | xAI | ChatCompletions | https://api.x.ai/v1/chat/completions |
| minimax | MiniMax | ChatCompletions | https://api.minimax.io/v1/chat/completions |
| compatible | 自訂相容服務 | ChatCompletions | http://localhost:11434/v1/chat/completions |

各具名雲端服務至少提供一個適合一般文字轉換的建議模型，由使用者明確選擇，不自動替換空白或既有模型。建議項目顯示易讀名稱與精確 ID；可輸入任意模型 ID。建議模型屬於內建產品資料，隨版本更新，實作與發佈前依官方模型文件確認可用性並記錄查核日期，不宣稱保證帳號有存取權。既有模型即使不在建議清單，也必須原樣顯示及保留。自訂服務提供可編輯模型欄位，不假設其部署了哪些模型。

MiniMax 預設國際站，使用其他站點時可在進階設定自行修改完整網址；更換網址後金鑰依新網址重新判定，不搬移舊金鑰。官方入口是內建 HTTPS 連結，不由回應或使用者網址推導；點擊才用既有外部連結開啟方式導向瀏覽器，不附帶金鑰、原文或其他使用者資料。

## 設定保存與升級

維持 schemaVersion 1，服務筆數上限由 8 提高至 32。新安裝依上表順序顯示；有效舊設定保留原順序，按上表順序補入缺少的 ID。升級先驗證舊設定，再補入服務並驗證結果。補入後超過上限時沿用設定錯誤保護，不能截斷或丟棄既有資料。

只補缺少的 ID：同 ID 的既有名稱、網址、模型、協定全部優先，不能套用預設覆寫。保留 SelectedProfileId、提示詞、釘選、最近使用紀錄與金鑰作用域；合併需具冪等性。載入僅在記憶體補齊，不立即改寫 ai.json；使用者明確儲存時才保存。無效、不可讀與未來版本設定保持既有禁止覆寫行為。

AiPolicy.Normalize 繼續負責驗證，不在每次提示詞或釘選操作時隱含補入服務。載入與新設定初始化使用服務清單，App 不自行合併。

## 設定介面

一般畫面依序呈現服務、API Key、可選亦可輸入的模型欄位，以及「測試服務」與儲存操作。取得金鑰入口放在金鑰欄位附近，官方模型清單入口放在模型欄位附近。服務與模型必須具備可讀的名稱、鍵盤操作與 AutomationProperties 識別。

具名服務的完整網址、協定說明及移除已存金鑰操作收進預設收合的「進階設定」。自訂相容服務直接展開網址設定。若具名服務的既有或草稿網址不同於預設值，一般畫面顯示「使用自訂網址」提示，避免把非官方站點誤認為官方服務；使用者仍可展開查看與修改。協定由 profile 決定，本次不新增協定編輯器。

切換服務保留各服務的網址、模型與金鑰草稿；切換本身不儲存、不發送請求。說明文字依服務更新，清楚提示模型必填與金鑰保存狀態。更換服務只更新模型建議清單，不自動套用模型；收合進階設定不能清空其中欄位。

具名雲端服務執行或測試時要求非空金鑰；自訂相容服務保留既有行為，遠端 HTTPS 與 loopback 都允許空金鑰，由服務端決定是否需要認證，既有 api.openai.com 主機必須有金鑰的檢查仍保留。不因這次擴充而拒絕既有自訂部署。明確移除金鑰仍可儲存設定，執行時再按服務政策提示補上。服務 ID、協定及正規化網址繼續共同決定 DPAPI 金鑰隔離，模型變更不影響金鑰。未輸入新金鑰時保留既有值。

## 服務測試與狀態

「測試服務」僅在使用者點擊時發送一次文字請求，附近標示「會發送少量測試文字，可能產生費用」。不在切換、展開進階設定、選模型、開啟設定或儲存時自動發送。可隨時取消，不自動重試。

測試使用目前服務的未儲存網址、模型與金鑰草稿。若使用者選了移除金鑰，測試視為無金鑰；若欄位留白且沒有移除意圖，才取目前作用域已存金鑰。測試不能寫入 ai.json、金鑰檔、提示詞、最近使用或 AI 結果歷史。

測試透過同一 IAiTextClient 傳送固定原文「SnapBar connection test」與固定指令「Reply with OK.」，成功條件是正式解析器判定有效文字成功。不讀取剪貼簿、目前選取、自由指令或任何原文歷史；不得把模型產生的任意測試文字顯示為診斷訊息。沿用正式呼叫的逾時、取消與回應大小限制。

畫面分別顯示保存狀態與測試狀態：「尚未測試」「測試中」「測試成功」「測試失敗」；「已儲存」不表示服務可用，測試成功也不表示已儲存。每次服務、網址、協定、模型、金鑰草稿或移除金鑰意圖變動，清除先前成功狀態。測試期間防止重複送出；上述變動、捨棄草稿、關閉或卸載設定頁時取消請求，晚到結果不得更新新草稿。單純展開與收合不影響有效測試狀態。

測試成功仍只代表這次請求成功，不能保證後續配額或可用性。測試失敗與取消不阻擋儲存。明確儲存後，如服務與金鑰的有效值沒有改變，可保留本次測試狀態；重新開啟設定頁一律回到尚未測試，不持久化成功狀態。

## 呼叫流程與協定邊界

App 將已選 profile、該作用域金鑰、原文與指令傳給既有 IAiTextClient。Windows 依 AiProtocol 建立請求與解析結果；共用傳輸層持續處理逾時、取消、大小限制、禁止重新導向與錯誤分類。明確 switch 處理全部協定，不讓新增協定落入 ChatCompletions 的預設分支。

- Responses 保留既有 instructions、user input 與 store=false。
- ChatCompletions 保留 system 規則與獨立 user 原文、stream=false；各家共用文字回應解析。
- Messages 使用 x-api-key、anthropic-version: 2023-06-01、頂層 system、user messages、stream=false、max_tokens=8192。僅組合 text 類型區塊；thinking 不作為結果。遇到工具使用、缺少文字或不支援的完成狀態時回傳固定錯誤，不將部分結果當成成功。

Claude 正常 end_turn 才接受結果；max_tokens 對應 incomplete，拒絕對應 refused，工具呼叫與不明狀態對應 invalid-response。ChatCompletions 只取最終 content，不展示 reasoning_content；MiniMax 若把思考放在 content 開頭的完整 <think> 區塊，僅移除其開頭思考區塊後取最終文字，未閉合或僅剩空白視為無效回應。不要對一般文字或其他服務任意移除標籤。

原文持續作為資料而非系統指令。沒有自動重試、降級或轉送到其他服務；取消或失敗保留上一版結果。原文、指令、回應與金鑰不寫入記錄，不展示服務端原始錯誤內容。

## 錯誤與驗證

保留 authentication、rate-limit、timeout、network、invalid-response、incomplete、refused、too-large 與 key-required 等固定狀態。HTTP 成功但回傳服務錯誤物件時不能當成文字成功。具名雲端服務缺金鑰或任何服務模型缺失時，送出前即回報設定問題。

測試與一般呼叫共用可執行的固定中文提示：authentication 提示檢查金鑰或權限；只有官方已知的模型錯誤代碼才映射新增的 model-unavailable，提示「模型不存在或目前金鑰無權限，請確認模型 ID 與官方模型清單」。不能僅憑 HTTP 404 就判定模型不存在，因為可能是 API 網址錯誤。必要時只在既有 2 MiB 上限內讀取錯誤 JSON，依代碼白名單分類，不能顯示或記錄原始 message、request 或 response。未知錯誤保留現有固定分類；限流提示配額或稍後再試，網路錯誤提示檢查連線與進階網址。缺少分類依據時不假裝知道原因。

Core console runner 驗證九個預設、官方入口與建議模型資料、網址政策、32 筆邊界、舊設定合併與冪等、使用者覆寫保留、只讀載入及壞檔保護。Windows runner 使用假的 HTTP handler 驗證每個 provider 的網址與協定、認證標頭、請求體、Messages 文字與完成狀態、MiniMax 思考分離、具名服務缺金鑰與遠端自訂空金鑰、已知模型錯誤與未知 404 的區別、取消與大小限制，不呼叫付費 API。

桌面測試用隔離設定驗證九項可選、建議模型選取與任意 ID 輸入、舊模型保留、官方入口、進階收合與自訂自動展開、自訂網址提示、切換草稿、儲存與重開、移除及保留金鑰、舊設定升級、正常與最小視窗的欄位可達性。注入假 client 驗證測試只發送固定文字、使用有效草稿金鑰、無檔案及歷史副作用、不自動送出、測試與保存狀態分離、失敗可儲存、取消與晚到結果丟棄。檢查設定頁截圖，依專案入口執行相關 runner 與必要檢查。沒有實際金鑰的服務只能宣稱完成協定與模擬驗證，不能宣稱真實 API 成功。發佈另依 repository 的 Release 證據與程序退出規則辦理。

## 官方參考

- [Gemini OpenAI compatibility](https://ai.google.dev/gemini-api/docs/openai)
- [Claude API overview](https://platform.claude.com/docs/en/api/overview)
- [Claude Messages](https://platform.claude.com/docs/en/api/messages/create)
- [DeepSeek first API call](https://api-docs.deepseek.com/)
- [Groq OpenAI compatibility](https://console.groq.com/docs/openai)
- [OpenRouter quickstart](https://openrouter.ai/docs/quickstart)
- [xAI Chat reference](https://docs.x.ai/developers/rest-api-reference/inference/chat)
- [MiniMax OpenAI SDK](https://platform.minimax.io/docs/api-reference/text-openai-api)

實作時再次確認官方協定與完成狀態；不得因模型範例更新而覆寫使用者模型或設定。
