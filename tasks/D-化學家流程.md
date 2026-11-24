# 包 D：成型機按名稱選配方、蒸發器狀態、純度估價、`chem_finish`

建議負責：大白龍　分支：`task/d-chem`

## 背景

化學家一輪要做：攪拌 → 加溶劑 → 加熱到轉化 → 過濾 → 放進蒸發器過夜 → 隔天放進成型機、選配方、輸出、收貨。第 1–2 天用現有工具全程手動跑完，大約 40 次呼叫，而且好幾處要靠猜。目標是「程式負責操作，AI 負責決策」。

這個分支已經有的基礎：

- 光譜儀結構化讀數：`item_menu(use)` 的回執與 `item_panel` 都附 `scan.components[{id,name,ul,ml,percent}]`、`tested`、`label`、`effects`（`mod/NativeItemPanels.cs` 的 `ScannerReading()`）。
- 控溫修正：`control_heat` 低於範圍會開高火，摘要有 `peak_c`、`reached_range`、`in_range_seconds`。
- 成型機批次輸出：`press_machine_button(quantity=1..64)`（`mod/Chemist.cs` 的 `ProduceBatch`）。

## 要做的（依順序，每一步各自 commit）

1. **成型機按配方名稱選**
   - 現在 `item_panel` 讀成型機配置面板，`text` 列出 7 個配方，`controls` 卻是 7 個 `"选择"` 按鈕、負數 ID（-57964…），對不上名稱。
   - 讓 `chemical_finisher` 面板的每個選擇按鈕附上 `recipe_id`（例如 `dream_dust`、`caffeine_pill`）與顯示名稱、所需體積。從原生 `ChemFinisherUIManager` 追按鈕和配方的對應，不要靠文字順序猜。
   - 另外在成型機視窗的回執裡回報目前選中的 `recipe_id`（`SELECTED_CHEM_ID_TAG`，`ProduceBatch` 已在用）。
2. **蒸發器狀態**
   - `inspect_item_window` 讀蒸發器時，容器槽附上容器內的液體總量與主成分（可直接用 `ScannerReading` 的讀法，或包 A 之後的共用方法；**不要等包 A**，先在本包寫一個私有小方法，合併時再統一），電池槽附剩餘電量。
   - 研究餾出液槽：什麼條件下會收到液體（推測和品質模組有關，見蒸發器說明「質量影響蒸氣捕獲量」），在視窗回執附 `distillate_capture: false` 與原因。
3. **機台容器槽的容量**
   - 現在容器槽寫 `capacity: {w:1,h:1}`，但 1×2 量筒、1×4 瓶子、3×4 水罐都放得進去。這類槽改成 `accepts: "one_container"`，不要給格數（`mod/Workstation.cs` 的 `DescribeControl`）。
4. **純度估價**
   - 夢塵 `estimated_value` 18，實賣 33～36（61% 街頭標準級）。化學手冊第 5 頁的純度分級：99% 以上 +135%、90–99% +85%、80–90% +40%、60–80% 不變、45–60% −45%、30–45% −75%、30% 以下 −95%。
   - 先查原生估價（`GetNegociatedValue` 或 `PriceHelper`）有沒有已經算進純度。有的話只補 `purity`、`grade` 欄位；沒有的話照原生公式補，不要硬套手冊數字。
5. **`chem_finish` 工具**（Python 串現有指令，新檔 `mcp/chem_workflow.py`）
   - 輸入：`vessel_id`（蒸發完的容器）、`recipe_id`、`quantity`（可省略，代表做到試劑不足）、`stash_ids` 或 `stash_count`（可選，做完放進走私暗格）。
   - 流程：從蒸發器取出容器 → 掃描確認沒有液態原料（水、酒精）→ 放進成型機 → 按名稱選配方 → 批次輸出 → 收進庫存 → 依指示放進暗格 → 回報。
   - 回執：每一步的狀態、成品 ID 清單、成品掃描（純度與分級，取一瓶）、剩餘試劑量、停止原因。中途任何一步失敗就停下，回報停在哪一步、目前物品在哪裡，讓 AI 或玩家接手。
   - 不要自己決定配方或數量，全部由參數給。
6. `chem_prepare`（白天那段，需要背景等待）**不在本包**。做完 1–5 後，在回報裡寫下你對 `chem_prepare` 的設計建議即可。

## 可以動的檔案

`mod/Chemist.cs`、`mod/Workstation.cs`、`mod/NativeItemPanels.cs`（只限 `chemical_finisher` 面板的按鈕標註；不要改 `ScannerReading`，包 A 會改）、`mod/ItemPanels.cs`（如需在 `ItemPanelView` 加欄位）、`mcp/chem_workflow.py`（新）、`mcp/ledger_mcp.py`（註冊新工具）、`mcp/pawnshop_tools.py`（只改 `press_machine_button`、`inspect_item_window` 的 docstring）、`tests/ChemistReplay.cs`、`tests/WorkstationReplay.cs`、新的 `tests/test_chem_workflow.py`、`AGENTS.md`（「搬貨、文件與機台」章節加化學流程一段）。

## 驗收

- 回放：成型機按鈕附 `recipe_id`；按 `dream_dust` 的按鈕後，視窗回 `recipe_id: dream_dust`。
- 回放：蒸發器視窗附容器液量、電池電量。
- 回放：容器槽回 `accepts: "one_container"`。
- Python 單元測試：`chem_finish` 的成功流程、中途失敗（例如容器還有酒精）時停在正確步驟。
- `-TestDesk` 全部通過。
- 實機（需要遊戲）：欄位 12 蒸發器裡有一支 50 mL 量筒（咖啡因約 30%）。用 `chem_finish(recipe_id="caffeine_pill")` 跑一次，記下成品數量與純度。
