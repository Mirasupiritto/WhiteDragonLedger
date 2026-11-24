# G　報警與通緝比對

來源：[實測回報](../白龍店帳-實測回報-0.28-化學家.md) 問題 11、15，以及實測後與玩家討論的結論。
建議負責：Codex　分支：`task/g-report`

## 背景

助手不用圖像辨識。真人玩家看一眼牆上的海報就能認出通緝犯，再按櫃檯警報；助手目前只能靠「台詞裡的名字等於海報上的名字」（`wanted_name_match`），沒報名字的就認不出來（第 7 天詹姆斯·貝利來過，白天完全沒察覺），也沒有工具可以按警報。

分工原則：**程式當眼睛和手指，助手做判斷**。程式提供真人看得到的東西（海報、長相是否相符、警報燈），助手決定報不報。

## 要做的事

### 1. 海報比對 `poster_match`

- 在來客狀態（`next_customer`、`get_trade_context`、`get_counter` 的來客部分）附上 `poster_match`，**只在相符時出現**：
  `{"poster_id": "wanted1", "name": "詹姆斯·貝利", "bounty": 300, "arrested": false}`
- 判定要等同於「真人比對海報長相」：比較來客實際顯示的外觀（`StoreClient.spriteName` 或實際使用的立繪）和海報的立繪。先用 `find_native_references` 查海報元素（`WantedElement`、`WantedUIManager.wantedElements`）用的是哪張圖，再決定怎麼比。
- **偽裝的情況照真人的視角處理**：有些通緝犯是 `isStealthWanted`（例如 `wanted8` John Smith、敲詐者）。如果他們的外觀和海報不同，真人也認不出來，就**不要**回 `poster_match`。不要直接拿 `isWanted`／`wantedPosterId` 當答案。
- 名字比對 `wanted_name_match` 保留。兩者都相符時一起附上。
- `next_action` 從 `player_visual_check` 改成 `report_decision`（只有 `poster_match` 時才改；只有名字相符時維持原樣）。

### 2. 報警工具 `report_client`

原生流程：`StoreSecButton.OnClick` → `PlayerStore.OnCustomerReported`（`PlayerStore.cs:3451` 起），警衛到場後 `StoreUIManager.OnGenericArrived` 播抓捕對話，離場時 `onSecAlarmLeftAction` 發賞金（`GrantWantedCaptureReward`）或檢舉獎勵（`CreateStolenReportReward`）。

- `report_client(mode="read")`：只回玩家看得到的
  - `light`：警報燈是否亮起可按（`StoreSecButton.IsClientArrestable`，與 `Update()` 的燈號判斷一致，含 `reported`、`isClientBeingArrested`、`isHandlingShootout`）
  - `tooltip`：`StoreSecButton.GetTooltip()` 的文字
  - `requires_hold`：`ShouldRequireHold()`
  - `reported`：這位客人是否已舉報過
  - `report_token`
- `report_client(mode="report", visit, report_token, generation, revision)`：
  - 呼叫原生按鈕的同一條路徑（等同 `OnClick`），不要直接改欄位。
  - 回執回 `status="reported"` 和 `next_tool`；抓捕對話和警衛離場是非同步的，用同一 `visit` 重讀，直到回 `arrest_finished`。
  - 完成時回實際結果：抓捕對話台詞、現金差額（賞金或獎勵）、客人是否已被帶走、`shop_update`。聲望不用自己算，請在 docstring 提示助手用 `feedback_box` 前後對照。
  - 分支：`tryInterceptReport` 攔截、改造人 `isAug`、槍戰 `isHandlingShootout`，一律回對應的 `status`，結果不明時回 `outcome_pending`。
  - 同一參數重送要能取回原回執（沿用現有 command_id 機制）。

### 3. 資訊邊界（必守）

**不可回傳**：`isWanted`、`isStealthWanted`、未上海報的賞金、`IsSellingContraband()`／`IsSellingStolen()` 的判定、「報對或報錯」的預告。這些只能在舉報**之後**，從實際台詞和數值變化看出來。

### 4. AGENTS.md 舉報規則

在「一位客人從進門到離店」補一段舉報流程，內容：

- 有 `poster_match` 且 `arrested=false`：預設舉報。先把進行中的交易收掉（貨搬回、單取消），再 `report_client`。
- 以下情況先停下來問玩家：店裡有違禁品不在走私艙（看 `get_shop_status` 的 `security.contraband`）、對方給過名片或屬於劇情人物、玩家事先交代過不要報。
- 只有 `wanted_name_match`（沒有 `poster_match`）：照舊交給玩家目視。
- 舉報後用 `feedback_box` 對照聲望，記入日報。

## 改動檔案

- `mod/Security.cs`（海報比對、警報燈讀取）
- 新檔 `mod/Report.cs`（`report_client` 指令處理）
- `mod/Counter.cs`：**只加一行**呼叫，在來客狀態附 `poster_match`（I 包也會改這個檔，請只加這一行）
- `mod/LedgerMod.cs` 或指令分派處：登記新指令
- `mcp/trade_tools.py` 或新檔 `mcp/report.py`；`mcp/scene_server.py` 的 `MUTATING_TOOLS` 加 `report_client`
- `AGENTS.md`
- 測試：新檔 `tests/ReportReplay.cs`、`tests/test_report.py`

## 驗收

- 回放測試：海報相符／不相符／偽裝（外觀不同但 `isWanted=true`）三種情況；警報燈三種狀態；舉報後非同步讀取到 `arrest_finished`；重送取回原回執。
- 回執裡搜尋不到 `stealth`、`is_wanted`、`selling_contraband` 這類欄位。
- 實機（交給大小姐）：第 8 天存檔有通緝犯會來，實測一次 `poster_match` 加 `report_client`。
