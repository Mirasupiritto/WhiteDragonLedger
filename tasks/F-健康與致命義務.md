# 包 F：健康、成癮與會讓這局結束的義務

建議負責：Codex　分支：`task/f-health`
這包要改 `mod/ShopDay.cs`、`mod/Exploration.cs`。包 C 也會改 `mod/ShopDay.cs`（加 `security` 段），所以 **C 和 F 不要同時做**；同一個人依序做即可，後做的那包從先做那包的分支接著開。

## 問題

有些開局特性會讓這一局直接失敗，或一直扣錢，MCP 卻完全沒提示：

- **哨兵崩潰綜合征**（`sentinel`，−3 點）：每週要注射一次免疫寧，不然會死。開局只送 1 支。
- **成癮**（`alcohol_addiction`、`nicotine_addiction`、`narcotic_addiction`，各 −1 點）：太久沒攝取就承受 8% 交易懲罰。可以治癒。**賭博成癮**（`gambling_addiction`）則會一有機會就自動買彩券。
- **敲詐勒索**（`blackmail`，−2 點）：每週要付封口費給勒索者，不付就在大多數派系中大幅掉聲望。
- **遊戲結束**：破產、死亡等。現在 AI 不會知道這局已經結束了，可能繼續呼叫工具卻一直失敗。

目前 `get_shop_status.status_summary` 只有 `estimated_net_worth`、`active_perks`、`injury`（`mod/Exploration.cs` 的 `InjuryView()`）。

## 已查到的原生 API

`PlayerStore.Instance.healthData`（`HealthData`，`InjuryView()` 已經在用）：

- 成癮：`GetAddictions()`、`HasAnyAddiction()`（static）、`IsAlcoholAddicted()`／`IsNicotineAddicted()`／`IsNarcoticAddicted()`／`IsGamblingAddicted()`（static）、`HasAlcoholDebuff()`／`HasNicotineDebuff()`／`HasNarcoticDebuff()`、`GetTotalAddictionPenalty()`
- 距上次攝取的天數：`daysSinceAlcoholConsumption`、`daysSinceNicotineConsumption`、`daysSinceNarcoticConsumption`、`daysSinceNarcoticRelief`
- 門檻常數：`ADDICTION_DEBUFF_DAY`、`ADDICTION_CURE_DAY`
- 攝取：`CanUseAlcohol()`、`CanUseNicotine()`、`CanUseNarcotic()`、`ConsumeAlcohol(int)`、`ConsumeNicotine(int)`、`ConsumeNarcotic(GameItem)`、`ConsumeNarcoticRelief(...)`
- 哨兵：`IsSentinel()`（static）、`daysSinceSentinelInjection`、`sentinelPool`、`SENTINEL_WARNING_DAY`／`SENTINEL_OVERDUE_DAY`／`SENTINEL_SEVERE_DAY`／`SENTINEL_CRITICAL_DAY`、`SENTINEL_ADVANCED_POOL`／`SENTINEL_FINAL_POOL`／`SENTINEL_DEATH_POOL`、`CanUseImmunivax()`、`ConsumeImmunivax(GameItem)`、`IMMUNIVAX_REGULAR_RESET`／`IMMUNIVAX_EXPIRED_RESET`／`IMMUNIVAX_COUNTERFEIT_RESET`（偽造、過期的免疫寧效果較差）
- 其他：`GetLuck()`、`luck`、`liquidLuckDaysLeft`、`CanRead()`（文盲）、`wentToAfterhoursLastNight`

勒索：`StartingPerk.IsBlackmail()`、`StartingPerkList.BLACKMAIL_VALUE`、`StoreClientListSpec.GetBlackmailCost()`、`StoreClientListSpec.CreateBlackmailer()`、`StoreClientManager.HandleBlackmailClient()`。勒索者似乎是以客人身分上門收錢，請確認是哪一天、用什麼對話選項付款。

遊戲結束：`GameOverUIManager`（`uiPanel`、`reason` 文字）。

**每個門檻的實際意義請用 `find_native_references` 追引用確認**，例如 `daysSinceSentinelInjection` 跟哪個常數比、`sentinelPool` 怎麼變動、超過哪一天會死。不要只看常數名稱猜。

## 要做的

1. **`get_shop_status` 新增 `health` 段**（沿用 calendar、trash 的做法：沒變動時省略，`verbose=True` 才完整列；但**有警示時一律要附**）：
   ```json
   "health": {
     "addictions": [
       {"kind": "nicotine", "days_since": 2, "debuff_after_days": 3, "debuff_active": false, "cure_after_days": 7}
     ],
     "trade_penalty_percent": 0,
     "sentinel": {"days_since_injection": 5, "warning_day": 6, "overdue_day": 7, "stage": "ok", "immunivax_owned": 1},
     "luck": 0,
     "can_read": true
   }
   ```
   沒有成癮就不附 `addictions`，不是哨兵就不附 `sentinel`。`stage` 依原生門檻分成 `ok`、`warning`、`overdue`、`severe`、`critical`。`immunivax_owned` 是庫存裡能用的免疫寧件數（分正常、過期、偽造）。
2. **每週義務**：在 `health` 段（或 `calendar`，看哪裡比較自然）附 `obligations`，列出「勒索封口費」的下次到期日、金額、不付的後果。日曆原本的房租資訊不要動。
3. **警示**：哨兵到 `warning` 以上、成癮懲罰已經生效、勒索封口費明天到期，都要放進 `get_shop_status` 和 `sleep` 回執的 `alerts`（沿用 `night_report.alerts` 的格式，例如 `{"type": "sentinel_injection_due", "days_left": 1}`），讓 AI 睡覺前一定看得到。
4. **注射與攝取**：先確認 `item_menu` 對免疫寧、香菸、酒是否已經有原生的 `use`／`activate` 選項可以用。可以的話，只要在 `health` 段附 `next_tool: "item_menu"` 和可用物品 ID，不用新增工具；不行再新增最小的 `consume_item` 工具，並在說明裡寫清楚會消耗物品。**攝取與注射一律由 AI 決定，程式不要自動做。**
5. **遊戲結束偵測**：`GameOverUIManager.uiPanel` 開著時，`get_scene` 回 `status: "game_over"`、附 `reason`（原生文字）；其他工具回 `tool_status: "game_over"`，不要再嘗試操作。

## 可以動的檔案

`mod/ShopDay.cs`（`StatusSummaryView` 與 alerts）、`mod/Exploration.cs`（`InjuryView` 附近，可以把健康讀取抽出成新檔）、新檔 `mod/Health.cs`、`mod/Scenes.cs`（只限 game_over）、`mcp/shop_day.py`、`mcp/scene.py`（docstring 與 game_over 狀態）、`tests/DayReplay.cs`、`tests/SceneReplay.cs`、新的 `tests/HealthReplay.cs`、`AGENTS.md`（「打烊、過夜與外出」加一段健康與每週義務；「新遊戲」第 3 步提醒選了哨兵或成癮特性要留意 `health`）。

## 驗收

- 回放：沒有成癮、不是哨兵時，`health` 段不附 `addictions`、`sentinel`；沒變動時整段省略。
- 回放：尼古丁成癮、超過懲罰天數 → `debuff_active: true`、`trade_penalty_percent` 大於 0、`alerts` 有對應項目。
- 回放：哨兵距上次注射到警告日 → `stage: "warning"`，`sleep` 回執的 `alerts` 也有。
- 回放：`GameOverUIManager.uiPanel` 開著 → `get_scene` 回 `game_over` 並附 reason。
- `-TestDesk` 全部通過，Python 單元測試通過。
- 實機（需要新開檔）：選「哨兵崩潰綜合征」＋「尼古丁成癮」開一局，第 1 天確認 `health` 的數值，睡一晚後確認天數有增加。記下各門檻實際是第幾天。
