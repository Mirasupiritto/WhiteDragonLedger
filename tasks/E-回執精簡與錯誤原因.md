# 包 E：回執減重、舊 token 與錯誤附原因

建議負責：第二輪　分支：`task/e-receipts`
注意：C 和 E 都會改 `mod/Counter.cs`，不要同時做。

## 問題

1. **加熱摘要到處附**：`next_customer`、`get_counter` 每次都帶著已經 `stopped`／`finished` 的舊 `heat`，又長又沒有新資訊。
2. **讀檔後第一個呼叫倒出整份庫存**：換 DLL 重讀後，第一個 `item_menu(read)` 的 `shop_update` 是 `mode: baseline`、`reason: cursor_reset`，附上全部 36 件貨。
3. **重複 upsert**：`move_items` 回 `shop_update.status: "observation_changed"`，下一個動作（`open_shop`）又把同一批 6 件全量 upsert。單件供貨商有 13 件貨時，offer 已經是摘要，`shop_update.upsert` 卻還是列了全部 13 件。
4. **`item_menu` 舊 token**：用過期的 `menu_token` 選動作，只回 `status: "menu_changed"`，沒說是 token 過期，也沒給新 token。讀過另一件物品的選單、或客人離場後，舊 token 就會失效。
5. **`not_selling` 字面誤導**：意思其實是「客人不是賣家」，字面卻像「不買」。實測醫生是 `not_selling`，卻照樣買了三件。

## 要做的

1. `heat` 只在 `running` 時附在櫃檯類回執；結束後**只附一次**（第一次讀到結束狀態的那個回執），之後不再附。`control_heat(mode="status")` 照舊隨時可查。
2. 讀檔後的基準：非庫存類工具（`item_menu`、`inspect_item_window`、`item_panel` 等）遇到 `cursor_reset` 時，`shop_update` 改成摘要（件數、現金、日數、`next_tool: "get_shop_updates"`），不要附全部貨。`get_shop_updates`、`query_inventory` 照舊回完整基準。
3. 重複 upsert：
   - 已經在上一個回執的 `items` 或 `upsert` 列過、而且內容沒變的貨，不要再 upsert。
   - offer 已經是摘要（`summary_only: true`）時，同一回執的 `shop_update.upsert` 也只列件數，或改成 `upsert_summary`，`verbose=True` 才列全部。
4. `item_menu` 舊 token：回 `status: "stale_menu_token"`，附這件物品目前的 `menu_token` 與 `enabled`，讓 AI 一步重試。`menu_changed` 留給「選單內容真的變了」的情況。
5. `not_selling`：新增 `client_role: "buyer" | "seller" | "barter" | "service"`（照原生的 `IsClientSelling` 等判斷），附在 `next_customer`、`get_trade_context`、`get_counter`。`stage` 值不改，避免舊流程壞掉。

## 可以動的檔案

`mod/Counter.cs`、`mod/LedgerMod.cs`（shop_update 組裝處）、`mod/ItemMenu.cs`、`mod/HeatControl.cs`（只限摘要附加邏輯，不改控溫）、`mcp/counter.py`、`mcp/ledger.py`（若 shop_update 在 Python 端組裝）、`mcp/pawnshop_tools.py`（只改 `item_menu` 的 docstring）、`tests/CounterDayReplay.cs`、`tests/ItemMenuReplay.cs`、`tests/HeatControlReplay.cs`、`tests/test_counter.py`、`tests/test_ledger.py`、`AGENTS.md`（「接手店面與查帳」提到 cursor 的那段）。

## 驗收

- 回放：結束後的 `heat` 只出現一次。
- 回放：`cursor_reset` 時非庫存工具回摘要。
- 回放：連續兩個動作，沒變動的貨不會被 upsert 第二次。
- 回放：舊 `menu_token` 回 `stale_menu_token` 並附新 token；新 token 能成功選動作。
- 回放：醫生（買家）回 `client_role: "buyer"`，拾荒客（賣家）回 `"seller"`。
- `-TestDesk` 全部通過，Python 單元測試通過。
- 用 `tests/measure.py`（如適用）比較同一流程改前改後的回執大小，寫進回報。
