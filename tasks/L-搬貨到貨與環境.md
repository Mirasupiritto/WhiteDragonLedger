# 包 L：搬貨不中斷、來路不明的到貨、版本不一致警告

分支：`task/l-stock`　起點：`chem-workflow` 最新 commit（含本說明）
來源：[第 9～15 天實測回報](F:/WorkSpace/Claude/Chat/Probably%20Stolen/白龍店帳-實測回報-化學家-第9-15天.md)

L 與 K 改的檔案不重疊，可以同時做。共同規則照 [README](README.md)，**測試資料取自真實回執或現有回放格式**。

## L1（低）`move_items` 遇到第一件放不下就整批停住

**實測**：第 9 天打烊後把 9 件貨搬去展示櫃，第 4 件（750 mL 水瓶，1×4）`showcase_full`，後面 5 件全部 `not_attempted`，其中能量飲料、密封水配給包（各 1×2）其實放得下。要重送兩次才搬完。
**要做的**：`no_space`／`showcase_full` 只略過該件，繼續試後面的貨。其他錯誤（換客、版本過期、鎖定）照舊停下。回執的 `status` 在有略過時為 `partial`，並附 `skipped_ids`。

## L2（中）電話訂貨或贈品出現時沒有來源說明

**實測**：第 10 天下午，櫃檯沒有客人時，`next_customer` 回 `status: customer_left`，`shop_update.upsert` 卻多了 8 瓶夢塵（阿希什前一晚電話說要送的免費樣品）。回執沒有任何來源說明，看起來像憑空出現。
**要做的**：
- 用原生 API 查清楚電話訂單與贈品怎麼送達（從 `PhoneClientList`、`DialoguePhoneUIManager`、`ProcurementData` 或阿希什的 `StoreClient` 找交貨方法，用 `find_native_references` 追）。
- 送達時在 `shop_update` 附 `arrivals: [{"ids": [...], "source": "phone_delivery", "contact": "阿希什"}]`。如果只能知道「不是交易帶來的新貨」，就附 `source: "unknown_delivery"`，不要猜聯絡人。
- 送達的貨若是違禁品且不在暗格，在同一回執附 `contraband_outside_bay: <件數>`，提醒 AI 收好（這是玩家看得到的庫存資訊）。

## L3（中）MCP 的 Python 端與 DLL 版本不一致時沒有警告

**實測**：這次驗收開工前，遊戲 DLL 是 `22baa43`，但 MCP 服務的 Python 端從主資料夾載入，而主資料夾停在 `task/i-reception`，所以 `report_client` 等工具根本不存在，也沒有任何提示。
**要做的**：
- 建置時把 git commit（短 hash）寫進 DLL（`build.ps1` 產生一個常數，或寫進 manifest），mod 在 `get_scene` 的 journal 回報。
- Python 端啟動時讀取自己所在 repo 的 `git rev-parse --short HEAD`（失敗就略過），兩邊不同時在 `get_scene` 附 `version_mismatch: {"dll": "22baa43", "mcp": "277a165"}`。
- 不要因此擋住工具，只警告。

## 可以動的檔案

`mod/Move.cs`、`mod/StockTransfer.cs`、`mod/LedgerMod.cs`（shop_update 組裝處，只限 `arrivals`）、`mod/Phone.cs`（只限讀取交貨資訊）、`mod/Scenes.cs`（只限版本欄位）、`scripts/build.ps1`（只限寫入版本常數）、`mcp/moves.py`、`mcp/scene.py`、`mcp/scene_server.py`（只限 `arrivals` 轉發與版本比對）、`tests/StockWindowReplay.cs`、`tests/SceneReplay.cs`、`tests/test_moves.py`、`tests/test_scene.py`、`AGENTS.md`。

注意：`scene_server.py` 的 `_slim_shop_update` 包 K 可能也會碰到 heat 相關欄位。L 只加 `arrivals`，不要改其他鍵。

## 驗收

- 回放：一批 3 件、第 2 件放不下，第 1、3 件都搬成功，回 `partial` 和 `skipped_ids`。
- 回放或單元測試：非交易新增的貨附 `arrivals`。
- 單元測試：版本不同時 `get_scene` 附 `version_mismatch`，相同時不附。
- `-TestDesk` 全部通過，Python 測試全部通過。
