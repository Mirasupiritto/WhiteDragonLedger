# 包 A：混合液體不再算成水、過濾預覽

建議負責：Gemini4 Hydrogen Medium　分支：`task/a-liquid`

## 問題

化學家第 1–2 天實測：攪拌後的咖啡豆糊、夢帽菇萃取液、開箱得到的「小瓶酒精」，庫存與 `find_stock` 都回：

```json
{"bottle_label": "沟渠水", "categories": ["WATER"], "contains_water": true, "water_ml": 250, "liquid_ml": 250}
```

光譜儀實測水只占 4%～27%（例如酒精瓶：酒精 175 mL、水 75 mL）。結果是 `find_stock(categories=["WATER"])` 會把酒精或毒品半成品配給買水的客人。

另外，用 `water_use` 把液體倒進裝有韌棉織物的水罐時，preview 不會說會經過濾網、會濾掉什麼。實測 50 mL → 24.1 mL，靠光譜儀才知道濾掉的是植物質。

## 現在的程式

- `mod/ShopDesk.cs`：`HasWater(item)` 只要 water part > 0 就算有水；`BottleLabel(item)` 有水就給水質標籤。
- `mod/ItemDetails.cs`：`AddWaterContainerFields` 把 `water_ml` 設成總體積（只要 `HasWater`）。
- `categories` 裡的 `WATER` 在哪裡加上去要自己追（`mod/StockRows.cs`、`mod/ItemDetails.cs` 一帶）。
- 成分的正確讀法已經有了：`mod/NativeItemPanels.cs` 的 `ScannerReading()` 逐一走 `Liquid.Liquids`，用 `item.GetTagReadonly(liquid.currentPart).GetInt()` 讀 µL。

## 要做的

1. **抽出共用的成分讀取**：新增 `mod/LiquidParts.cs`，提供類似 `static List<KeyValuePair<Liquid,int>> LiquidParts(GameItem item)` 的方法（µL，依量排序）。讓 `ScannerReading()` 改用它，回傳內容不變。
2. **水量用實際的水成分**：`water_ml` = water part 的 mL。`liquid_ml` 保持總體積。
3. **主成分不是水就不算水**：定一個門檻（建議水占比 ≥ 50%，寫成常數並註解理由）。未達門檻時：
   - `contains_water: false`
   - 不給 `bottle_label`（`BottleLabel` 回 null）
   - `categories` 不加 `WATER`
4. **新增 `liquid_main`**：非空液體容器附 `{"id": "alcohol", "name": "酒精", "percent": 70.0}`（只放主成分，避免回執變大）。
5. **過濾預覽**：`water_use` preview 的目標是有濾網的容器時（`WaterHelper.GetFilterItem(target)` 不為 null），在 `plan` 裡加 `filter`：
   - 濾網的 `id`、名稱
   - 預計會被濾掉的成分清單與 mL
   - 預計倒入後的體積

   原生 `Liquid` 有 `particleSize`、`filterVolumeRemoved`、`filterMinimumPurity`、`filterDurabilityUsage`，濾網有 `Liquid.FILTER_SIZE_TAG`。實際的過濾規則請用 `find_native_references` 追原生倒液的程式（從 `WaterHelper`／`LiquidContainerHelper` 找 pour 或 filter 相關方法），照原生規則算，不要自己發明。真的算不準時，就只列「顆粒大於濾網的成分」並標 `estimate: true`。

## 可以動的檔案

`mod/LiquidParts.cs`（新）、`mod/NativeItemPanels.cs`（只改 `ScannerReading` 改用共用方法）、`mod/ShopDesk.cs`（`HasWater`、`BottleLabel`）、`mod/ItemDetails.cs`、`mod/StockRows.cs`、`mod/WaterUse.cs`、`mcp/stock_query.py`（docstring）、`mcp/pawnshop_tools.py`（只改 `water_use`、`find_stock`、`query_inventory` 的 docstring）、`tests/WaterUseReplay.cs`、`tests/ChemistReplay.cs`、`AGENTS.md`（「查 `WATER` 會含空容器」那段）。

注意：`HasWater` 也被水商流程用（`inspect_purifiers`、試紙、淨水器）。改門檻後要確認純水、髒水、空瓶的行為不變：**水占比高的瓶子照舊**，空瓶照舊 `empty: true`、`contains_water: false`，`find_stock(categories=["WATER"])` 照舊能找到空水瓶。

## 驗收

- 回放：酒精瓶（酒精 175、水 75 mL）→ `contains_water: false`、無 `bottle_label`、`categories` 無 `WATER`、`liquid_main.id == "alcohol"`。
- 回放：純淨水瓶 → 跟改之前完全一樣。
- 回放：空瓶 → 跟改之前完全一樣。
- 回放：倒進有濾網的容器時，preview 附 `filter`，並列出被濾掉的成分。
- `-TestDesk` 全部通過，Python 單元測試通過。
- 實機（可選，有就更好）：化學家存檔欄位 12 有「小瓶酒精」（約 250 mL）和「脂合剂瓶」，用 `query_inventory(name="小瓶酒精")` 確認。
