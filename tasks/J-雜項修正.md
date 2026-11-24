# J　雜項修正

來源：[實測回報](../白龍店帳-實測回報-0.28-化學家.md) 問題 4、6、7、8、10、13、14。
建議負責：Codex（做完 G 再接）　分支：`task/j-misc`

每一項都很小，可以各自一個 commit。

## 1. 勒索到期當天的警示（問題 4、13）

- 現象：第 7 天睡前與第 8 天早上都只有 `blackmail_due_tomorrow`；第 8 天早上 `get_shop_status.alerts` 是 null。付清 48 之後，`obligations` 仍顯示 `due_day: 8`，打烊後才變成第 15 天／55。
- 要做：
  - 到期當天回 `blackmail_due_today`（附金額與「敲詐者今天會來，用對話付款」）。
  - 付款後的那次讀取就要反映：已付清或下次的日期與金額。先查原生在哪裡記錄本週已付（敲詐者 `CreateBlackmailer` 付款回呼只做 `ModCash(-cost)` 和離場，日期可能是用 `dayCounter % 7` 推算的），找不到記錄就在 mod 端以「今天該客人已離場且現金減少」為準，並在註解說明。
- 檔案：`mod/Health.cs`、`tests/HealthReplay.cs`

## 2. `move_items` 說明與行為不一致（問題 6）

- 現象：docstring 寫「來客期間可帶 `visit`」，實際回 `A visit is only used for trade table moves`（`mcp/moves.py:32`）。開店期間也不能把庫存搬到展示櫃。
- 要做：查原生在開店、客人在場時能不能拖庫存到展示櫃（以真人能不能拖為準）。能拖就放行；不能拖就改 docstring，並在拒絕時回 `reason: "showcase_locked_while_open"` 之類的明確原因，不要直接丟例外。
- 檔案：`mcp/moves.py`、必要時 `mod/Move.cs`、`tests/test_moves.py`（沒有就新增）

## 3. `water_use` 對空容器的判斷（問題 7、8，**重新歸因**）

- 原本記錄的「倒進排水口一律失敗」「從水龍頭裝水回 `use_unconfirmed`」，複查後原因是**來源容器本來就是空的**：
  - drain：瓶子已空，原生 `CanUseOnItem` 回 false，工具回 `target_blocked / native_target_unavailable`（`mod/WaterUse.cs:146-147`）。
  - 空瓶倒進水罐：預覽照樣給 `use_token`，套用後回 `use_unconfirmed`、`target_change_ul: 0`。
- 要做：
  - `reason` 的判斷把 drain 和 pour 也納入：**來源**容量為 0 時回 `source_empty`（目前只檢查目標、只涵蓋 purify/test/tablet）。
  - pour 預覽時，來源為空就直接回 `source_empty`，不發 `use_token`。
- 檔案：`mod/WaterUse.cs`、`tests/WaterUseReplay.cs`（或現有的水務回放檔）

## 4. `sell_order` 取回後留下未結單（問題 10）

- 現象：第 3 天中層區護士，`sell_order` 先回 `sale_pending`，用原參數重送取回 `finished`（`replayed: true`），但 `get_shop_ledger` 仍有一張 `last_status: "source_changed"` 的 open ticket（`56c016f3182f51aaa666076cf45f0692`），要手動 `dismiss_trade_ticket`。這是因為中間我用**新的 revision** 再送了一次同樣的 lines，ledger 把它當成另一張單。
- 要做：同一 `visit` 和同一組 `lines` 的單已經 `finished` 時，新 revision 的重送直接回原收據（或回 `already_finished` 附原 ticket），不要另開一張 open ticket。
- 檔案：`mcp/ledger.py`、`mcp/trade_journal.py`、`tests/test_ledger.py`

## 5. `query_inventory` 的分類欄位（問題 14）

- 現象：用 `query_inventory` 的 `items[].categories` 篩 FOOD 拿不到展示櫃的合成肉；`find_stock(categories=["FOOD"])` 才正確。
- 要做：查出 `query_inventory` 的 `categories` 和 `find_stock` 的分類來源為何不同（可能是快取或只含部分分類），兩邊統一成同一份原生分類。
- 檔案：`mcp/stock_query.py`、必要時 `mod/StockRows.cs`、對應測試

## 驗收

- 每一項至少一條回放測試或 Python 單元測試。
- 測試資料要取自實際回執（實測回報裡有 command_id 和欄位值），不要憑空捏造。
