# AI 服務 Provider 擴充設計

日期：2026-11-03。使用者已同意新增 Gemini、Claude、DeepSeek、Groq、OpenRouter、xAI、MiniMax，採用「預設服務＋共用協定」方案；本文件待使用者檢閱後進入實作規劃。

## 目標與範圍

AI 服務下拉選單提供九個選項：OpenAI、Gemini、Claude、DeepSeek、Groq、OpenRouter、xAI、MiniMax、自訂相容服務。使用者選擇服務、填入模型 ID 與該服務 API Key，明確儲存後即可沿用現有文字轉換流程。

本次處理服務預設值、設定升級、協定呼叫與設定介面說明。不增加模型清單下載、自動選模型、串流、工具呼叫、圖片、額外服務帳號管理或自動切換供應商。保留目前選字安全、前景驗證、取消、原文與結果版本行為。

## 方案選擇

採用服務預設清單搭配共用協定：OpenAI 保留 Responses，Claude 新增 Anthropic Messages，其餘新增服務使用 Chat Completions。此方案符合現有文字轉換需求，且服務設定與協定處理可以獨立維護。

未採用每家原生協定，因目前用途不需要各家完整功能；未統一經由 OpenRouter，因使用者需要直接使用各家服務與金鑰。

## 元件與預設服務

Core 增加獨立的服務預設清單，負責穩定 ID、名稱、協定及完整請求網址。AiSettings 與設定載入共用這份清單，避免 App 與 Windows 各自維護網址。AiProviderProfile 保留既有形狀，AiProtocol 在現有值後新增 Messages，不改變既有數值序列化含義。

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

模型 ID 預設留白，必須由使用者填入；不把文件範例模型當成長期可用的預設模型。MiniMax 預設國際站，使用其他站點時可自行修改完整網址；更換網址後金鑰依新網址重新判定，不搬移舊金鑰。

## 設定保存與升級

維持 schemaVersion 1，服務筆數上限由 8 提高至 32。新安裝依上表順序顯示；有效舊設定保留原順序，按上表順序補入缺少的 ID。升級先驗證舊設定，再補入服務並驗證結果。補入後超過上限時沿用設定錯誤保護，不能截斷或丟棄既有資料。

只補缺少的 ID：同 ID 的既有名稱、網址、模型、協定全部優先，不能套用預設覆寫。保留 SelectedProfileId、提示詞、釘選、最近使用紀錄與金鑰作用域；合併需具冪等性。載入僅在記憶體補齊，不立即改寫 ai.json；使用者明確儲存時才保存。無效、不可讀與未來版本設定保持既有禁止覆寫行為。

AiPolicy.Normalize 繼續負責驗證，不在每次提示詞或釘選操作時隱含補入服務。載入與新設定初始化使用服務清單，App 不自行合併。

## 設定介面

沿用現有下拉選單、完整網址、模型 ID、API Key 與儲存操作。切換服務保留各服務的網址、模型與金鑰草稿；切換本身不儲存、不發送請求。說明文字依服務協定更新，清楚提示模型 ID 必填、完整網址格式與金鑰保存狀態。

公開雲端服務要求非空金鑰；自訂服務使用遠端 HTTPS 時同樣要求金鑰，loopback 相容服務允許留白。明確移除金鑰仍可儲存設定，執行時再提示補上。服務 ID、協定及正規化網址繼續共同決定 DPAPI 金鑰隔離，模型變更不影響金鑰。未輸入新金鑰時保留既有值。

## 呼叫流程與協定邊界

App 將已選 profile、該作用域金鑰、原文與指令傳給既有 IAiTextClient。Windows 依 AiProtocol 建立請求與解析結果；共用傳輸層持續處理逾時、取消、大小限制、禁止重新導向與錯誤分類。明確 switch 處理全部協定，不讓新增協定落入 ChatCompletions 的預設分支。

- Responses 保留既有 instructions、user input 與 store=false。
- ChatCompletions 保留 system 規則與獨立 user 原文、stream=false；各家共用文字回應解析。
- Messages 使用 x-api-key、anthropic-version: 2023-06-01、頂層 system、user messages、stream=false、max_tokens=8192。僅組合 text 類型區塊；thinking 不作為結果。遇到工具使用、缺少文字或不支援的完成狀態時回傳固定錯誤，不將部分結果當成成功。

Claude 正常 end_turn 才接受結果；max_tokens 對應 incomplete，拒絕對應 refused，工具呼叫與不明狀態對應 invalid-response。ChatCompletions 只取最終 content，不展示 reasoning_content；MiniMax 若把思考放在 content 開頭的完整 <think> 區塊，僅移除其開頭思考區塊後取最終文字，未閉合或僅剩空白視為無效回應。不要對一般文字或其他服務任意移除標籤。

原文持續作為資料而非系統指令。沒有自動重試、降級或轉送到其他服務；取消或失敗保留上一版結果。原文、指令、回應與金鑰不寫入記錄，不展示服務端原始錯誤內容。

## 錯誤與驗證

保留 authentication、rate-limit、timeout、network、invalid-response、incomplete、refused、too-large 與 key-required 等固定狀態。HTTP 成功但回傳服務錯誤物件時不能當成文字成功。公開服務缺金鑰或模型缺失時，送出前即回報設定問題。

Core console runner 驗證九個預設、網址政策、32 筆邊界、舊設定合併與冪等、使用者覆寫保留、只讀載入及壞檔保護。Windows runner 使用假的 HTTP handler 驗證每個 provider 的網址與協定、認證標頭、請求體、Messages 文字與完成狀態、MiniMax 思考分離、各類錯誤、取消與大小限制，不呼叫付費 API。

桌面測試用隔離設定驗證九項可選、切換草稿、儲存與重開、移除及保留金鑰、舊設定升級、正常與最小視窗的欄位可達性。檢查設定頁截圖，依專案入口執行相關 runner 與必要檢查。沒有實際金鑰的服務只能宣稱完成協定與模擬驗證，不能宣稱真實 API 成功。發佈另依 repository 的 Release 證據與程序退出規則辦理。

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
