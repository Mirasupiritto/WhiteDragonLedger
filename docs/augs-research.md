# 改造體與櫃檯自衛研究

**供大小姐審閱，尚待玩家決定是否開實作包。** 欄位 13 第 12 天的導師筆記和鑑別指南已能從 MCP 讀取。現在遇到需要判讀外觀的客人，可把當前台詞和來訪編號交給玩家，讓玩家看畫面並決定如何處理。

## 原生互動

| 玩家看得到的階段 | 原生路徑與限制 |
|---|---|
| 櫃檯出現霰彈槍 | `Shotgun.Unlock` 解鎖，`LoadShotgun` 控制場景物件顯示。持槍許可撤銷也會讓槍收起來。這條櫃檯流程使用場景物件，未發現玩家背包彈藥、彈數檢查或扣彈。讀取介面可省略彈量。 |
| 游標提示能否拿起 | `Shotgun.GetTooltip`、`CanPickup` 與 `ShouldRequireHold` 共同決定顯示及長按。空櫃檯、正在逮捕、治安部處置中、已威嚇或這位客人的原生限制都可能讓按鈕不可用。工具只需回可見提示與按鈕狀態。 |
| 拿起、放下 | 原生按鈕 `Shotgun.OnClick` 進入或退出 `StoreUIManager.EnterShootingMode / ExitShootingMode`。轉場會切換鏡頭和介面，部分對話暫停，部分被中斷。拿起後已有遊戲效果，讀取工具不應代玩家拿起。 |
| 射擊與離場 | 原生輸入由 `Arm.Update` 處理，射擊前呼叫 `StoreUIManager.CanShoot`。已射擊或正等待倖存者反應時無法再次射擊。一般流程由 `Shoot` 進入動畫，再由 `StartShootoutSequence` 設定 `PlayerStore.isHandlingShootout` 並呼叫治安部。警衛到場後由 `OnGenericArrived("SEC")` 接上對話。查得的櫃檯射擊呼叫鏈沒有經過 `CombatManager`。 |

原生索引的 `find_native_references` 查到 `StoreUIManager.Shoot` 的直接呼叫者是 `Arm.Update`。可重查 [Shotgun.cs](D:/workspace/ps-decomp/Shotgun.cs)、[Arm.cs](D:/workspace/ps-decomp/Arm.cs) 與 [StoreUIManager.cs](D:/workspace/ps-decomp/StoreUIManager.cs)。

## 開槍以後

以下是供實作者辨識回執時機的原生規則。玩家端要等台詞、事件單、現金與聲望實際變動後才報結果。

- **改造體**。警衛對話結束時 `AugHelper.OnGoodKill` 發放獎金及事件單。基礎獎金為導師介紹後經過的天數加 100，限制在 100～250；特性會再調整。原生調整治安部、革命、黑市與下層聲望，並依難度降低威脅值。
- **無辜客人**。`SecShootoutDialog` 的誤擊分支在結尾呼叫 `AugHelper.CleanupKill`。革命與黑市聲望基礎值各減 10，下層與治安部各減 15。原生以 `SecData.CommitCrime("MANSLAUGHTER", amount)` 記案，第 25 天前基礎案值 250，第 25～49 天為 500，第 50 天起為 750。案值可受特性調整；證據值另由 `SecData.OnNewDay` 更新，不應把案值當場顯示成罰款或證據增量。
- **特殊客人**。原生另有通緝案作為抓捕結算的分支，對話結束時走 `PlayerStore.OnWantedClientShot`。倖存的劇情角色則走 `ShootSurvivor` 和後續對話。送出操作時只確認已受理；玩家中途退出、場景切換或尚未看到結算時，應保留結果待確認。

對照 [StoreClientDialogList.cs](D:/workspace/ps-decomp/StoreClientDialogList.cs)、[AugHelper.cs](D:/workspace/ps-decomp/AugHelper.cs) 與 [SecData.cs](D:/workspace/ps-decomp/SecData.cs)。

## 放跑的結局門檻

原生以 `PlayerStore.augPresence` 累積威脅。一般導師解鎖時設為 15。`PlayerStore.DismissCurrentClient` 遇到活著離開的改造體時呼叫 `AugHelper.OnAugLeftAlive`，寫夜報並增加威脅。擊倒後按難度扣回，最低為 0。數值供研究用，MCP 仍以玩家已看到的夜報為準。

| 原版難度 ID | 每次放跑 | 每次確認擊倒 | 威脅達 100 的結局條件 |
|---|---:|---:|---|
| `cozy` | +10 | −25 | 有「改造體危機」特性 |
| `normal` | +10 | −25 | 有「改造體危機」特性 |
| `hard` | +15 | −10 | 此難度即啟用 |
| `challenge` | +16 | −8 | 此難度即啟用 |

結局在過夜後、次日開始且存檔前檢查。`EndOfDayUIManager` 使用 `augPresence >= 100 && (Difficulty.Rules.augGameOverAtFullPresence || StartingPerk.IsAugMenace())`。普通難度單純遇到導師、解鎖槍，仍須另外具備該特性才會觸發此結局。[Difficulty.cs](D:/workspace/ps-decomp/Difficulty.cs) · [EndOfDayUIManager.cs](D:/workspace/ps-decomp/EndOfDayUIManager.cs)

## 台詞與玩家判讀

原生對話生成會組合購物需求、預算和附加句，也可能只有一般購物台詞。導師指南提到過度正式、機械化的用詞；助手可逐字轉述本次已顯示的句子，讓玩家結合瞳孔、表情、晶片、衣著及動作判讀。不要從模板鍵、未顯示的後續句或角色內部資料產生身分標記或可疑分數。這次實測記錄的是導師教學，沒有已確認改造體的逐句現場樣本。[第 9～15 天實測回報](<F:/WorkSpace/Claude/Chat/Probably Stolen/白龍店帳-實測回報-化學家-第9-15天.md>)

## 工具介面草案與待決問題

1. `defend(mode="read")` 回目前來訪、場景版本、可見的槍、按鈕提示、是否需長按及射擊介面是否開啟。沿用 `get_trade_context` 讀已顯示台詞。這一步純讀。
2. 拿起與放下各自對應一個操作，帶當前來訪及版本。先驗證原生按鈕可用，完成鏡頭切換後重新讀取；玩家已介入、介面關閉或客人離開就結束這次請求。
3. `defend(mode="fire", visit=..., generation=..., revision=..., confirm_token=...)` 需玩家明確同意**這一次、這位客人**。建議由玩家專屬 UI 顯示一次性憑證，玩家在對話中交付；一般工具回執不能發出足以自行授權的 token。若目前 harness 無法區分玩家交付與模型自填，保留玩家自行操作。是否提供這個確認管道，留給大小姐與玩家決定。
4. 重送同一請求只取回既有回執，換客、讀檔或退出射擊模式即令未使用的確認失效。送出後用同一來訪等候可見對話與結算；中途斷線先查原回執。待實機核對的項目為解鎖後的 tooltip、轉場時可操作的按鈕、倖存者對話，以及警衛離場與事件單出現的順序。

本包僅有研究文件。工具介面、玩家確認方式與實機驗收範圍均待審閱後另案決定。
