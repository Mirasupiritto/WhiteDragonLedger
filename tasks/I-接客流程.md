# I　接客流程一次到位

來源：[實測回報](../白龍店帳-實測回報-0.28-化學家.md) 問題 2、3、5（以及結構審查的 D9）。
建議負責：Gemini（做完 H 再接，同樣會動 `mod/Counter.cs` 附近）　分支：`task/i-reception`

## 背景

試營運每位客人要呼叫 5～6 次工具：叫客、等開場、讀需求、配貨、送客。實測卡過三種狀況：

1. **光譜儀面板沒關**：`next_customer` 回 `item_panel_open`，但客人其實已經進門，平板是關的。關掉面板後再叫 `next_customer`，拿到的是**舊回執重播**（`replayed: true`，仍是 `item_panel_open`），得改用 `ring_customer` 或 `reopen_trade_tablet` 才接得上（第 2、5 天各一次）。
2. **開場還在講話就回來了**：`next_customer` 回 `stage: not_selling`、`demand.status: panel_closed`、`interests: []`，要再 `wait_for_opening` 才看得到需求。我的自動迴圈因此漏賣 3 位買家（商人 240、藥商 187、癮君子 81）。
3. **以物易物放貨後讀不到資料**：放上付款物後立刻 `get_trade_context`，沒有 `barter` 欄位（客人正在說「行，就這樣吧」），1.5 秒後再讀才有。

## 要做的事

1. **`item_menu` 掃描完自動關面板**：光譜儀 `use` 拿到 `scan` 之後，如果是工具自己打開的面板就關掉，回執附 `panel_closed: true`。玩家手動打開的面板不要動。
2. **`next_customer` 遇到工具開的面板先關掉再繼續**；遇到玩家開的面板維持 `item_panel_open`，但回執要附**當下**的 `visit`、`customer`、`stage` 和 `next_tool`（`item_panel` 關閉後用 `reopen_trade_tablet`），而且重播判斷不能把「狀態已改變」的情況當成同一張回執。
3. **`next_customer` 等到可以交易才回來**：開場對話跑完、平板打開（或確定是對話客、選項頁、以物易物頁）才回。預設最多等 20 秒，逾時回 `opening_timeout` 附目前狀態。回傳的 `demand` 要是平板打開後的版本。
4. **說話中保留上一份交易資料**：`stage=speaking` 時，`get_trade_context` 照樣附上最後一次觀察到的 `barter`／`demand`，加 `"stale_during_speech": true`；說完再更新。
5. 看情況順手做：`finish_customer` 送客後，如果還有人在排，回執附 `next_tool: "next_customer"`。

## 改動檔案

- `mod/Reception.cs`、`mod/Counter.cs`（G 包只會在 `Counter.cs` 加一行 `poster_match`，合併時注意）
- `mod/ItemMenu.cs`（掃描後關面板）
- `mod/Barter.cs`（說話中保留資料）
- `mcp/counter.py`、`mcp/trade_tools.py`（docstring 與等待逾時）
- `AGENTS.md`（「一位客人從進門到離店」第 1 步：`next_customer` 回來時已可交易）
- 測試：`tests/MixedTradeReplay.cs`、`tests/CounterDayReplay.cs`、`tests/test_counter.py`

## 驗收

- 回放測試：面板開著叫客（工具開的會自動關、玩家開的回當下狀態）；開場對話中叫客會等到平板開；以物易物說話中仍讀得到 `barter`。
- 實機（交給大小姐）：一整天接客，每位客人只用「叫客 → 交易 → 送客」三步，不再出現漏賣或卡面板。
