# H　對話客放貨（卡特爾配額）

來源：[實測回報](../白龍店帳-實測回報-0.28-化學家.md) 問題 1、12，以及〈資訊邊界原則〉。
建議負責：Gemini　分支：`task/h-dialogue-stage`

## 背景

第 8 天卡特爾接頭人來收夢塵，台詞是「10份梦尘准备好了吗？／纯度必须至少达到40%。／把货放到柜台上。」，選項只有 `[继续]`。原生流程是**先把夢塵拖上櫃檯，再按繼續**：選項回呼直接點交易檯上的 `dream_dust`（`StoreClientListCartel.CreateCartelHandlerWeekly`，反編譯 `StoreClientListCartel.cs:247-258`）。

但 `move_item(inventory→back)` 在 `stage=choice_required` 時回 `trade_unavailable`（`mod/CounterStock.cs:27` 只放行 `ready` 和 `barter`），所以工具完全無法自行交貨。第一次試營運就因為按了繼續、檯上 0 瓶而 game over；第二次是玩家手動拖貨才過關。

## 要做的事

### 1. 對話選項頁允許放自有貨

- 在 `MoveCounterStock` 加一個條件：客人是**對話客**（`clientIntent == DIALOGUE`）、停在選項頁、原生交易檯沒有被鎖（真人此時拖得動，就要放行）時，允許把**自有**的庫存或展示櫃物品放到 `back`，也允許從 `back` 收回。
- 先用 `find_native_references` 確認原生在這個階段的鎖定狀態（`NegociationUIManager.IsTradeLocked`、`lockAllInventoryFlag`、交易檯視窗 `isLocked`），以「真人能不能拖」為準，不要一律放行。
- `move_items` 走同一條路徑，同樣支援。
- 對話客的 `stage` 放貨後保持 `choice_required`，`dialog_choices` 不變。

### 2. 選項前的放貨提醒（只用畫面上看得到的）

- 在 `get_trade_context`／`next_customer` 的對話頁，如果**台詞**要求把貨放上櫃檯，附上：
  `"counter_request": {"from_lines": ["10份梦尘准备好了吗？", "纯度必须至少达到40%。", "把货放到柜台上。"], "table_items": [...自有檯上物品...]}`
- 判斷「台詞要求放貨」的方式：比對本地化字串鍵（`dialog_cartel_weekly_3` 等），**或**直接在這位客人的選項頁一律附上檯上自有物品清單。由你選較穩定的做法，並在回報中說明。
- **不可**附上「不放會 game over」或程式裡的分支後果；`consequence` 維持 `unknown`。數量與純度要求只能從台詞來（台詞本來就寫明）。
- 檯上物品清單要附每件的 `kind`。純度只在玩家已經掃描過或瓶貼看得到時才附（沿用現有 `purity` 欄位的規則，不要新開管道讀隱藏值）。

### 3. AGENTS.md

在「對話」那段補一句：**台詞要求把貨放到櫃檯上時，先用 `move_items` 把貨放上 back，核對 `counter_request.table_items` 的件數和種類，再選選項。**

## 改動檔案

- `mod/CounterStock.cs`
- `mod/CounterDialogue.cs`（`counter_request`）
- `mcp/moves.py`（docstring）、`mcp/trade_tools.py`（docstring）
- `AGENTS.md`
- 測試：`tests/CounterDayReplay.cs` 或新檔 `tests/DialogueStageReplay.cs`

## 驗收

- 回放測試：對話客在選項頁放貨成功；交易檯被鎖時仍回 `trade_unavailable` 並附 `reason`；非對話客的行為不變；`counter_request` 只在對話選項頁出現。
- 實機（交給大小姐）：第 15 天卡特爾收 15 瓶，全程用工具完成。
