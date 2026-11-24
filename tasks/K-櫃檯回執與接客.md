# 包 K：盤查提醒、接客等待與回執精簡

分支：`task/k-counter`　起點：`chem-workflow` 最新 commit（含本說明）
來源：[第 9～15 天實測回報](F:/WorkSpace/Claude/Chat/Probably%20Stolen/白龍店帳-實測回報-化學家-第9-15天.md)（欄位 13，2026-11-25）

K 與 L 改的檔案不重疊，可以同時做。共同規則照 [README](README.md)：只改列出的檔案、每個新行為至少一條回放或單元測試、`-TestDesk` 與 Python 測試全過、**測試資料取自真實回執或現有回放格式，不要照想像寫**。

## K1（高）盤查前沒有提醒暗格外的違禁品

**實測**：第 9 天「懶惰的治安部盤查員」上門，`stage: choice_required`，唯一選項「[接受盘查。]」。回執沒有任何提醒；接受後暗格外的 15 瓶夢塵與 4 顆夢帽菇全被沒收（事件回執單：「没收了19件物品」），治安部聲望下降。`get_shop_status` 當時其實有 `security.contraband: {inventory: 16+4, smuggler_bay: 9}`，只是盤查頁沒帶出來。

**要做的**：
- 治安部盤查客的選項頁（從原生判斷：`StoreClientListSecurity` 建立的客人、或 `ContrabandHelper.StartInspection` 會被觸發的對話），在 `next_customer`／`get_trade_context` 附：
  ```json
  "inspection_exposure": {"inventory": 20, "showcase": 0, "smuggler_bay": 9, "on_counter": 0}
  ```
  直接重用 `mod/Security.cs` 的 `SecurityView()` 計數。
- 不要預測「會被沒收幾件」或罰款金額。玩家看得到自己的庫存，所以件數可以給；搜查的隨機結果不能給。
- 選項本身照常可選，不要擋。

## K2（中）`next_customer` 遇到 `customer_departing` 立刻回傳

**實測**：每送走一位客人，下一次 `next_customer` 幾乎都回 `status: customer_departing`，要再呼叫一次才叫到下一位。第 9～12 天約 20 次。
**原因**：`mcp/pawnshop_tools.py:508-509`，看到 `customer_departing` 直接回傳。

**要做的**：在同一次呼叫內等離場完成（沿用 `wait_opening` 的 20 秒上限），然後照常叫鈴、等開場。逾時才回 `customer_departing`。

## K3（中）已結束的 `heat` 摘要反覆出現

**實測**：第 10 天晚上那次加熱（`run_id 344261ba…`，`status: stopped`）在第 11、12 天的每一個 `customer_departing` 回執裡都還在；第 11 天卡特爾接頭人來訪的 `next_customer` 回執也有。包 E 的「結束後只附一次」沒有涵蓋這些路徑。
**原因之一**：同一處 `pawnshop_tools.py:508-509` 的回傳沒有經過 `counter.filter_finished_heat`。另外 `_seen_finished_heat` 是行程內的全域集合，MCP 重連後會清空，重連後第一次又會附上。

**要做的**：
- 所有櫃檯類回傳路徑（`next_customer`、`ring_customer`、`get_counter`、`get_trade_context`、`customer_departing` 分支）都經過同一個過濾函式。
- 「已通知過」的狀態改存在帳本 SQLite（以 `generation`＋`run_id` 為鍵），重連後也不重複。
- 回放或單元測試：結束後連續三個不同工具的回執，只有第一個帶 `heat`。

## K4（低）`client_role` 標錯買水客

**實測**：第 11、12 天兩位要買水的客人（「我想買些水」「你有水嗎」）自帶空瓶要賣，被標成 `client_role: "seller"`，但他們同時有預算、會買水。
**要做的**：客人同時 `IsClientBuying()` 和 `IsClientSelling()` 時回 `"buyer_seller"`（或把 `client_role` 改成陣列）。更新 `AGENTS.md` 對應說明。

## K5（低）議價 `read` 回執太長

**實測**：一次 `bargain(read)` 回 9 個選項，每個都帶完整的 `offer_price_factors`、`reputation_multipliers`、`reputation_hint`。
**要做的**：預設依類型分組：
```json
{"type": "BuyingDiscount", "uses_roll": true, "crime": false,
 "percents": [10, 25, 50, 75], "leave_chance": [0, 15, 45, 75], "success_chance": [95, 65, 15, 0],
 "buy_factor": [0.9, 0.75, 0.5, 0.25], "reputation_buy": [-0.5, -1.5, -3, -5]}
```
`verbose=True` 才回逐項完整物件。`offer` 的參數不變。

## K6（低）`get_trade_context` 的 `seller_water` 與 `offer.items` 重複

**實測**：冰礦工賣 2 瓶水，`seller_water.bottles` 和 `offer.items` 是同樣兩筆完整資料。
**要做的**：`seller_water` 只留 `count` 與 `ids`，細節看 `offer.items`。

## 可以動的檔案

`mcp/pawnshop_tools.py`（`next_customer`、`ring_customer` 區段）、`mcp/counter.py`、`mcp/trade_tools.py`、`mcp/ledger.py`（只限新增 heat 通知紀錄表）、`mod/Counter.cs`、`mod/CounterDialogue.cs`、`mod/Security.cs`（只限把計數抽成可重用）、`mod/Bargaining.cs`（只限 read 的輸出格式）、`tests/test_counter.py`、`tests/test_trade_tools.py`、`tests/CounterDayReplay.cs`、`tests/BargainReplay.cs`、`AGENTS.md`。

## 驗收

- 回放：盤查客選項頁附 `inspection_exposure`，件數與 `security.contraband` 一致。
- 單元測試：`customer_departing` 後一次 `next_customer` 就回到下一位客人。
- 單元測試：結束的 heat 跨三個工具只出現一次；模擬重連後也不重複。
- `-TestDesk` 全部通過，Python 測試全部通過。
- 實機（欄位 13 第 13 天起）：連續接 5 位客人，`next_customer` 每位只需叫一次。
