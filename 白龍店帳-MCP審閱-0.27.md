# 白龍店帳 0.27 MCP 審閱：缺口與優先順序

依據：化學家開局第 1–3 天實機（欄位 12）、水商與水培兩輪紀錄、目前 79 個工具，以及原生程序集中 MCP 尚未接觸的 UI 系統。
已修好或正在修的項目放在最後。

## P0：會讓 AI 做錯決定或卡死

### 1. 混合液體被當成水
- 現象：咖啡豆糊、夢帽菇萃取液、開箱的「小瓶酒精」都標成 `bottle_label: 沟渠水`、`categories: WATER`、`contains_water: true`，`water_ml` 等於總體積。光譜儀實測水只占 4～27%。
- 後果：`find_stock(categories=["WATER"])` 會把酒精或毒品半成品配給買水的客人。
- 建議：`contains_water` 和 `water_ml` 改用實際的水成分（`Liquid` 的 water part）；水不是主成分時不給 `bottle_label`、不歸 WATER。另外加 `liquid_main`（主成分 id 與占比），可直接沿用新的 scan 讀法。

### 2. 以物易物的客人無法交易
- 現象：癮君子拾荒客把線纜放在 front，說要拿東西交換。`stage: not_selling`、`panel_closed`，`get_trade_context` 沒有 barter 選項或 token。`sell_order` 回 `buyer_or_ui_changed`（沒附 reason），`move_item(→back)` 回 `trade_unavailable`。
- 建議：查清楚原生交換流程（可能是把自有貨放上檯讓客人估價），提供 `swap_offer` 或擴充 `barter_offer`。在那之前，至少讓 `get_trade_context` 回 `next_action: "barter_unsupported"`，讓 AI 知道這種客人只能送客。

### 3. 沒有議價工具（`BargainUIManager` 完全沒接）
- 原生功能包括：抬價、打折、要求進貨折扣、威脅、勒索，每種都有離場機率（`GetBargainLeaveChance`）與聲望預覽（`ComputePreviewMultipliers`、`BuildRepGainLine`）。
- 目前 MCP 一律用基礎報價成交，每筆都少賺；「不善議價」這個負面特性對 AI 來說等於不存在。
- 建議工具：`bargain(mode="read")` 列出可用選項、百分比、離場機率、成交倍率與聲望影響；`bargain(mode="offer", type, percent)` 回客人的反應與新報價。決定權留給 AI，程式只負責呈現機率和執行。

### 4. 看不到證據等級與搜查風險
- 原生有 `RaidManager` 和 `SecData.evidenceLevel`。目前只有打開治安紀錄文件（`ShopDocuments`）才讀得到，店況裡沒有。
- 化學家賣夢塵、存違禁品時，AI 不知道離被搜查還有多遠，也不知道走私暗格夠不夠。
- 建議：`get_shop_status` 加 `security`：證據等級（數值與顯示文字）、在店違禁品數量（暗格內外分開算）、最近的通緝來訪。

## P1：化學家流程（已規劃）

5. **`chem_finish` / `chem_prepare`**：程式負責操作，AI 負責選配方、用量、份數與藏貨。光譜儀結構化讀數已完成，可以當作判斷基礎。
6. **成型機配方要能用名稱選**：目前 7 個「选择」按鈕只有負數 ID，要猜順序。建議 `press_machine_button` 或 `item_panel` 接受 `recipe="dream_dust"`，回執附 `recipe_id`。
7. **蒸發器狀態**：視窗看不到容器剩多少液體、電池剩多少電，也沒說餾出液槽為什麼沒收到東西（推測是沒有品質模組）。夜間報告也沒有蒸發結果。
8. **估價與違禁標記**：夢塵 `estimated_value` 18，實賣 33～36，估價沒反映純度。裝萃取液的容器標 `contraband: false`、估價 6。建議估價依純度分級修正；含違禁成分的容器標 `contraband_ingredients`。
9. **過濾預覽**：`water_use` 的 preview 沒說會經過濾網。`Liquid` 有 `particleSize`、`filterVolumeRemoved`、`filterMinimumPurity`，可以算出預計濾掉哪些成分、剩下多少。
10. **機台容器槽寫 1×1 容量**：其實任何大小的容器都放得進去。建議改成 `accepts: "one_container"`，不要給格數。

## P1：一般功能缺口

11. **沒有存檔工具**（`SaveUIManager` 沒接）：遇到「舉報卡特爾」這種分支，沒辦法先存一份再試。建議至少回報最後存檔的時間與日數；遊戲允許的話，提供手動存檔。
12. **通緝名單**（`WantedUIManager.wantedElements` 沒接）：名字和賞金是文字資料，可以讀。客人自報姓名（如奧丁）時就能標 `wanted_name_match`，照片比對仍交給玩家。目前要等隔天的夜間報告才知道。
13. **劇情選項沒有後果提示**：治安部警官「舉報卡特爾／不舉報」沒標 `forced_choice`，也沒有任何提示。至少標 `story_branch: true`，讓 AI 停下來問玩家。
14. **`item_menu` 舊 token**：只回 `status: "menu_changed"`。建議附 `reason: "stale_menu_token"` 和新的 `menu_token`，讓 AI 一步重試。
15. **`sell_order` 的 `buyer_or_ui_changed` 沒附 reason**，AI 分不出是平板關著、客人在說話，還是這種客人根本不收錢。

## P2：回執篇幅

16. `next_customer`、`get_counter` 每次都附上已結束的 `heat` 摘要。建議只在 running 或剛結束的那一次附上。
17. 讀檔後第一個呼叫（不管是什麼工具）會附整份庫存基準（`cursor_reset`）。建議改成摘要加 `next_tool: get_shop_updates`。
18. `move_items` 回 `observation_changed`，下一個動作又把同一批貨全量 upsert；單件供貨商有 13 件貨時，offer 是摘要，`shop_update.upsert` 卻列了全部。
19. `stage: not_selling` 字面像「不買」，實際意思是「客人不是賣家」。建議改名或附 `buyer: true/false`。

## 原生系統中尚未接觸、值得確認的

| 系統 | 推測用途 | 價值 |
|---|---|---|
| `NotebookUIManager` | 各物品類別已發現的特徵（真偽、品相線索） | 高：鑑價與挑貨 |
| `ClienteleUIManager` | 客群或常客紀錄 | 中：預測來客需求 |
| `CommissaryUIManager` | 配給站 | 中（這局被禁用） |
| `PhoneBookUIManager` | 電話簿 | 低：`get_phone_contacts` 大致已涵蓋 |
| `RecorderUIManager`、`LogUIManager`、`PCUIManager` | 錄音機、日誌、電腦 | 待確認 |
| `GameOverUIManager` | 遊戲結束畫面 | 低：至少要能偵測並回報 |

## 已修好、這次確認的

- 報紙堵住交易檯：`open_shop` 的 `document_cleanup` 會自動收進庫存。
- 光譜儀結構化讀數（`chem-workflow` 分支，`055f84f`，已實機驗證）。
- 機台按鈕重複提示 `next_tool: item_panel`（同一個 commit）。
- 控溫高火：`native_target` 低於範圍時開高火，回報 `peak_c`、`reached_range`、`in_range_seconds`，沒到範圍就逾時會附 `range_not_reached`。建置與回放測試已過，待裝進遊戲實測。
