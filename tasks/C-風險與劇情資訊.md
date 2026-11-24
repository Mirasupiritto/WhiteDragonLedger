# 包 C：證據等級、通緝名單、存檔資訊、劇情分支提示

建議負責：第二輪（誰先做完 A、B、D 誰接）　分支：`task/c-risk`
注意：C 和 E 都會改 `mod/Counter.cs`，不要同時做。

## 問題

1. **看不到證據等級**：原生有搜查系統（`RaidManager`、`RaidScene`）和 `SecData.evidenceLevel`、`SecData.GetEvidenceLevelDisplay()`。目前只有打開治安紀錄文件（`mod/ShopDocuments.cs` 的 `security_record`）才讀得到。化學家一直賣夢塵、存違禁品，AI 卻不知道離被搜查還有多遠。
2. **通緝名單讀不到**：`WantedUIManager.wantedElements`（`List<WantedElement>`）沒接。第 2 天奧丁·哈拉爾德森自報姓名、說「別按報警器」，回執沒有任何標記，要到第 3 天的夜間報告才出現 `wanted_visited`。
3. **劇情分支沒提示**：治安部警官問「[举报卡特尔] / [不举报]」，回執只有 `dialog_choices`，沒有 `forced_choice` 或後果提示。這種選擇應該停下來問玩家。
4. **不知道最後存檔在哪**：`SaveUIManager` 沒接。重大分支前沒辦法確認「有沒有存檔可以退」。實測第 2 天打烊後關遊戲，重開是第 3 天早上，這點要先弄清楚。

## 要做的

1. `get_shop_status` 新增 `security` 段（未變動時跟 calendar、trash 一樣可省略，`verbose=True` 才完整列）：
   - `evidence_level`（數值）、`evidence_display`（原生文字）
   - 店內違禁品件數：一般庫存、展示櫃、走私暗格內分開算（暗格的判定看 `smuggler_bay` 類物品的內容）
   - 若原生有「下次搜查」的機率或門檻，一併列出；沒有就不要猜
2. 通緝名單：
   - 新增唯讀工具，或在 `get_shop_status(verbose=True)` 附上 `wanted`：每人的名字、賞金、是否已逮捕（`OnArrested`）。**不要輸出照片或外觀描述**，照片比對交給玩家（見 `AGENTS.md`）。
   - 客人上櫃時，若客人顯示名稱與通緝名單上的名字**完全相符**，在 `next_customer` 和 `get_trade_context` 附 `wanted_name_match: {"name": ..., "bounty": ...}` 與 `next_action: "player_visual_check"`。名字不符就什麼都不附。
3. 劇情分支：在 `CounterDialogue` 的選項頁，若選項屬於劇情或派系關鍵選擇（請從原生找判斷依據，例如對話節點的旗標、選項是否觸發派系事件；找不到可靠依據時，至少列出觸發的派系或事件名稱），附 `story_branch: true` 與可讀的 `consequence_hint`。找不到就附 `consequence: "unknown"`，不要編。
4. 存檔資訊：在 `get_scene` 或 `get_shop_status` 附 `last_save`（日數、階段、時間）。研究原生是否允許隨時手動存檔；允許的話，另開工具 `save_game`，並在說明裡寫清楚會覆蓋哪個欄位。不允許就只回報資訊。

## 可以動的檔案

`mod/ShopDay.cs`、`mod/Calendar.cs`、`mod/ShopDocuments.cs`、`mod/CounterDialogue.cs`、`mod/Counter.cs`（只限附加 `wanted_name_match`）、`mod/Scenes.cs`（`last_save`）、新檔 `mod/Security.cs`、`mcp/shop_day.py`、`mcp/scene.py`、`mcp/counter.py`（docstring）、對應的 `tests/DayReplay.cs`、`tests/CounterDayReplay.cs`、`tests/SceneReplay.cs`、`AGENTS.md`（「打烊、過夜與外出」與「一位客人」章節）。

## 驗收

- 回放：`security` 段的件數正確（暗格內外分開）；未變動時省略。
- 回放：名字相符時附 `wanted_name_match`，不符時不附；回執沒有任何外觀資訊。
- 回放：劇情選項頁附 `story_branch` 或 `consequence: "unknown"`。
- `-TestDesk` 全部通過，Python 單元測試通過。
- 實機（可選）：欄位 12 第 3 天以後可讀到證據等級；奧丁已在通緝名單上。
