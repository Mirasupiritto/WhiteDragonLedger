# 包 B：議價工具、以物易物的客人

建議負責：Codex 6.2 Astra High　分支：`task/b-bargain`

## 問題一：完全沒有議價

遊戲原生有完整的議價系統 `BargainUIManager`，MCP 一個都沒接。現在每筆交易都用基礎報價成交，等於每一單都少賺；「不善議價」這個負面特性對 AI 來說也等於不存在。

原生已查到的成員（`get_native_symbol("BargainUIManager")`，共 89 個）：

- 能不能做：`CanOfferMarkup()`、`CanOfferDiscount()`、`CanDemandBuyingDiscount()`、`CanOfferBuyingMarkup()`、`CanBlackmail()`、`CanThreatenAug()`、`CanOffer(BargainType)`（private）
- 動作：`DemandBuyingDiscount(int percent)`、`Blackmail()`，以及其他 offer 類方法（請列完整清單）
- 預覽：`GetBargainLeaveChance(BargainType,int percent)`（public）、`ComputePreviewMultipliers(BargainType,int)`、`BuildRepGainLine(...)`、`ComputeRepPer1000Credits(...)`
- 規則：`BargainAffectsBuySide`、`BargainAffectsSellSide`、`BargainCanLeave`、`BargainUsesRoll`、`GetBadBargainPenalty`
- 介面：`CloseUI()`、`DockToTablet()`、`ButtonLabel(BargainType,int)`、`BuildGroups(...)`
- 對話：`CreateAcceptGratefulDialog`、`CreateAcceptReluctantDialog`、`CreateRejectDialog`、`CreateLeaveDialog`

先追清楚：議價面板怎麼打開（掛在交易平板上，看 `NegociationUIManager` 怎麼呼叫）、`BargainType` 的列舉值、百分比的可選範圍、結果怎麼反映到 `GetNegociatedValue()`（`sell_order`／`trade_batch` 用它當報價）。

### 要做的

新增 MCP 工具 `bargain`（建議放在 `mcp/trade_tools.py`，mod 端新檔 `mod/Bargaining.cs`）：

- `bargain(mode="read", generation, revision, visit)`：回目前可用的議價選項。每個選項附：`type`、可選 `percent` 範圍、離場機率（`GetBargainLeaveChance`）、預覽倍率（買／賣兩側）、聲望影響文字、是否會擲骰（`BargainUsesRoll`）。附上一份 `bargain_token`。
- `bargain(mode="offer", type, percent, bargain_token, ...)`：執行一次議價。回客人的反應（接受、勉強接受、拒絕、離開）、新的報價倍率，以及客人是否還在。
- 已經議價過的同一位客人，`read` 要反映目前狀態（已套用的倍率、還能不能再議）。
- **決策交給 AI**：工具只負責呈現機率和執行，不要自動挑最划算的選項。
- 勒索（Blackmail）與威脅（ThreatenAug）屬於犯罪行為，回執要清楚標示 `crime: true`，以及會影響哪些聲望。

## 問題二：以物易物的客人無法交易

第 2 天的「瘾君子拾荒客」：

```
lines: "有什么能让我缓一缓的东西吗？奥克西莫、梦尘，什么都行。…我有些东西可以拿来交换。"
stage: "not_selling", demand: {"source":"trade_panel","status":"panel_closed"}
front: 3 條线缆（owned: false）
```

- `get_trade_context` 沒有 barter 選項，也沒有 `barter_token`。
- `sell_order` 回 `buyer_or_ui_changed`、`not_attempted`，沒附 reason。
- `move_item(inventory→back)` 回 `trade_unavailable`。
- 最後只能送客。後來的醫生同樣是 `not_selling`＋`panel_closed`，`sell_order` 卻能成交，所以問題只出在以物易物的客人。

### 要做的

1. 追清楚這類客人的原生流程。現有的 `mod/Barter.cs` 處理的是「食物換獎勵」，看這類客人是不是同一套、還是另一種 `StoreClient` 旗標。
2. 能做就提供工具（擴充 `barter_offer`，或新增 `swap_offer`），讓 AI 選要給哪件自有貨、換對方哪些貨，並在成交前預覽雙方價值。
3. 真的做不到，至少讓 `get_trade_context` 和 `next_customer` 回 `next_action: "barter_unsupported"` 附原因，讓 AI 不要白試。

## 可以動的檔案

`mod/Bargaining.cs`（新）、`mod/Barter.cs`、`mod/TradeTools.cs`、`mod/SaleOrder.cs`（只限 `buyer_or_ui_changed` 附 reason）、`mcp/trade_tools.py`、`tests/BarterReplay.cs`、新的 `tests/BargainReplay.cs`、`AGENTS.md`（交易表格加議價一列）。

不要改 `mod/Counter.cs`（包 C、E 會改）。真的需要時，只加最小的 hook，並在回報裡說明。

## 驗收

- 回放：`read` 列出可用選項與離場機率；`offer` 成功後報價倍率改變；舊的 `bargain_token` 被拒；客人離開回 `customer_left`。
- 回放：以物易物的客人回 `barter_unsupported`，或新工具的完整流程。
- `-TestDesk` 全部通過，Python 單元測試通過。
- 實機（需要遊戲）：找一位普通買家，`bargain(read)` 看選項，試一次小幅抬價，比較 `sell_order` 回的實收。記下離場機率和實際結果。
