# 議價機制與物品基礎價格表

來源：`https://probablystolenguide.com/zh-hant/wiki/`（繁體中文整理）


---

## 深空當鋪議價 Wiki：討價還價入口、顧客預算與派系聲望

找到交易平板上的議價按鈕，查四種折扣與加價選擇、一次報價的限制、顧客預算變化和各派系聲望算法，按交易金額與特性判斷談價後果。

開啟顧客交易平板，點擊對話氣泡形狀的議價按鈕。先選購買或出售方向，再決定折扣或加價。給顧客優惠可改善其所屬派系的關係；要求顧客讓利可能被拒絕或讓對方離開。報價前懸停查看機率與聲望預覽，成交後才按貨物價值結算交易聲望。

本頁集中說明議價入口、報價選項和成交後果。介面中的「購買」和「出售」都以**你的店舖**為視角。

### 議價入口與解鎖

點擊櫃台前的目前顧客開啟交易平板，再點擊右上方的對話氣泡按鈕，展開議價面板；再點一次可以收起。先結束顧客正在說的話，等對方走到櫃台，再操作。

認準這個對話氣泡圖示，報價選項在展開的議價面板裡。

正常開場在第8天由導師介紹議價；跳過開場會直接啟用。看不到按鈕時，先檢查是否已經解鎖、目前顧客是否允許議價、自己是否在店內，以及是否選了「不善議價」特性。這個特性會停用議價。

傑克遜的常規到店交易不開放議價，部分供貨、任務來客也不開放。解鎖議價後，遇到這些顧客仍不會出現按鈕。

普通議價可以直接進行。放大鏡對質在檢查托盤操作，不是普通議價的前置條件。

### 四種報價與一次機會

四種普通報價都有 **10%、25%、50%、75%** 四檔。顧客買你的貨時，提供出售方向；顧客賣貨給你時，提供購買方向。兼做買賣的顧客可以分別處理兩個方向。

| 選項 | 商品報價 | 買家預算 |
| --- | --- | --- |
| 出售： 提供折扣 | 降低 | 增加 |
| 出售： 抬高售價 | 同意後提高 | 同意後減少 |
| 購買： 要求折扣 | 同意後降低 | 不變 |
| 購買：提出加價 | 提高 | 不變 |

先懸停在百分比上，查看接受、拒絕、離開的機率與購買／出售聲望預覽，再點擊。**同一位顧客、同一方向只能選一次**，拒絕也會用掉機會。關掉再開啟不能重試，不能先試10%再試25%。新顧客到來後才重設。

給顧客優惠會被接受，但須實際成交才能獲得交易聲望。要求讓利時，接受會調整價格；拒絕保留原價；憤怒離開則結束交易，造成基礎−3派系聲望損失，再受難度等修正。

### 售價與顧客預算

對有預算的買家，打折會增加預算；成功抬價會減少預算。預算是這次願意花的總額，與單件價格分別計算。

| 檔位 | 折後價格 | 折扣後的預算 | 加價後價格 | 加價後的預算 |
| --- | --- | --- | --- | --- |
| 10% | ×0.90 | +20% | ×1.10 | −10% |
| 25% | ×0.75 | +50% | ×1.25 | −15% |
| 50% | ×0.50 | +100% | ×1.50 | −20% |
| 75% | ×0.25 | +150% | ×1.75 | −25% |

例如，議價前報價100信用點，打25%折扣後為75；原預算200的買家預算增至300。抬價25%成功後，商品為125，但預算降至170。仍要按顧客需求與剩餘預算配貨。

議價會與商品其他價格修正共同生效，包括零售加成。議價期間補到櫃台的相應貨物也會套用方案，不只影響選中的一件貨。成交前檢查目前總價，基礎值可查物品圖鑑。

例如，只有普通難度15%零售加成時，基礎價值1000的貨物先報價1150；再給50%折扣，報價變575。兩個階段相乘，不能把+15%和−50%直接相加。

### 給誰優惠，增加誰的聲望

交易聲望記到**目前顧客的派系**。找屬於目標派系、允許議價並能正常交易的顧客。職業、外貌與派系不是一一對應；普通傭兵與犯罪傭兵就可能歸屬不同派系。

| 派系 | 可查找的交易來客 | 係數 |
| --- | --- | --- |
| 下層區 | 下層區居民、拾荒者、星際客 | 0.66 |
| 上層區 | 上層區居民 | 1.2 |
| 治安部 | 治安部採購／盈餘商人 | 1 |
| 黑市 | 小偷、犯罪傭兵 | 1 |
| 革命軍 | 革命軍軍需官、游擊隊 | 1 |
| 遊客 | 遊客來客 | 無獨立聲望條 |

遊客沒有獨立聲望條，給遊客打折不會增加上層區聲望。具體歸屬見人物、身分與組織與治安部。

部分特殊來客沒有可結算派系，例如中層區護士。即使面板出現數值預覽，也不代表一定能給某條聲望入帳。

各派系獎勵、負面門檻與關係恢復，見派系聲望攻略。

### 聲望倍率與算例

購買和出售都按同一公式逐件結算。普通難度、合法非贓物、沒有聲望特性時：

`聲望變化 = 議價前商品報價 × 交易倍率 × 派系係數 ÷ 400`

先選對自己這次操作的欄位；買入和賣出的倍率分別記錄。

| 報價檔位 | 給優惠：降價賣出／加價買入，基礎倍率1 | 要求讓利且成功：加價賣出／壓價買入 |
| --- | --- | --- |
| 10% | 1.65 | −0.5 |
| 25% | 2.2 | −1.5 |
| 50% | 3 | −3 |
| 75% | 3.9 | −5 |

普通顧客的基礎交易倍率通常是1。給優惠時，在正的基礎倍率上增加獎勵。治安部徵用官與盈餘商人的基礎倍率是2，給25%優惠後是3.2。這裡指實際採購或賣貨的來客，與只來檢查的警官不同。

套公式時依序看這三步：

- 找商品在本次議價前的完整報價，包含零售加價等其他修正。只有議價後價格時，先除回本次價格倍率：加價25%後買入報價625，用 `625 ÷ 1.25 = 500`；折扣25%後賣出報價300，用 `300 ÷ 0.75 = 400`。

- 取這次方案對應的買入或賣出倍率。買家沒有付足完整報價時，再套下文的預算規則。

- 乘上前表的派系係數，再除以400。難度、特性與特殊貨物的額外影響見下一節。

例如，給基礎倍率1的下層區賣家多付10%，議價前1,000的貨物變成1,100。取買入優惠欄的1.65，得到 `1,000 × 1.65 × 0.66 ÷ 400 = 2.7225` 下層區聲望。

給上層區買家打25%折扣，議價前400變成300。對方付足300時，取賣出優惠倍率2.2，得到 `400 × 2.2 × 1.2 ÷ 400 = 2.64` 上層區聲望。

遊戲在還原價格倍率、套用交易與派系倍率時會取整，因此低價物品可能與未取整算式略有差異。聲望本身保留小數，小額成交後整數顯示可能不變。最終派系聲望保持在−200至200。

面板 `×` 後是倍率，方括號則換算成1,000信用點商品的參考值；懸停預覽擬選方案。實際成交仍逐件套用上式，難度等其餘修正於結算時生效。

#### 顧客預算不足，仍然賣出會怎樣？

超出預算時，交易會先彈出確認，顯示顧客剩餘信用點及你將少收的金額。選「是」會以不超過剩餘預算的付款，出售櫃檯上符合條件的貨物。想保留或收足錢的貨物先拿走；詳見預算與拒收。

每件沒有付足完整報價的商品，賣出聲望倍率再 **加1**。雖然現金少收，聲望仍從該商品的完整報價還原計算。

例如，對上層區買家成功加價10%，議價前1,000變成1,100。付足1,100時，要求讓利倍率−0.5，得到 `1,000 × (−0.5) × 1.2 ÷ 400 = −1.5` 聲望。若櫃檯只有這件商品，買家只剩600預算，你仍確認賣出，就收600、少收500；結算倍率變成 `−0.5 + 1 = 0.5`，得到 `1,000 × 0.5 × 1.2 ÷ 400 = 1.5` 聲望。預算改變的是實付款與這項倍率，並不把聲望計算基礎從1,000改成600。

### 難度、特性與特殊貨物

| 難度或特性 | 正聲望 | 負聲望 |
| --- | --- | --- |
| 舒適 | ×1.5 | ×0.5 |
| 普通 | ×1 | ×1 |
| 困難 | ×0.75 | ×1.25 |
| 挑戰 | ×0.5 | ×2 |
| 舌燦蓮花（Diplomatic） | ×1.33 | — |
| 馬屁精（Sycophant）：上層區 | ×1.25 | — |
| 大嘴巴（Blabbermouth）：黑市／革命軍 | ×0.5 | — |

黑市負聲望再翻倍；已觸發上層區Petition懲罰時，治安部負聲望也翻倍。買入贓物或高等級違禁品，還會按貨物規則放大交易聲望的正負變化。先看犯罪與藏貨。

### 接受、拒絕和離開的機率

抬高售價與要求折扣使用相同的普通判定。下表按派系聲望0、普通來客、沒有談判加成計算。

| 要求幅度 | 接受 | 拒絕但留下 | 離開 |
| --- | --- | --- | --- |
| 10% | 80% | 8% | 12% |
| 25% | 50% | 20% | 30% |
| 50% | 0% | 40% | 60% |
| 75% | 0% | 10% | 90% |

正聲望每滿6點提供1個百分點；負聲望先做整數除法，再將懲罰翻倍。事件來客額外加15個百分點，成交大師設備加成提高接受率。特殊來客可能固定拒絕，或拒絕後仍留下，報價前以目前提示為準。

改造體會直接拒絕普通抬價與壓價，即使通用提示仍顯示成功機率。不能只憑一次拒絕判斷改造體身分。

### 敲詐勒索與威脅

**敲詐勒索（罪犯）**：檢查對方是否被通緝，不是看是否像小偷。對可交易、非治安警官的來客可獨立嘗試一次。成功後，相應方向價格向你有利的方向調整20%，交易聲望倍率改為−6；若同方向的普通抬價或壓價也成功，負倍率再疊加；對通緝犯失敗，基礎派系聲望−5。

**威脅（改造體）**：在介紹改造體後出現。對真實改造體成功後，獲得20%有利價格，兩個方向的基礎交易聲望倍率設為0；前文賣貨預算不足時加1的結算規則仍適用。

對普通人使用這兩項操作都會失敗：50%留下、基礎聲望−7；50%離開、基礎聲望−10，再套用全局修正。**只要嘗試勒索或威脅，本次就不能再舉報；嘗試威脅後，本次也不能裝備霰彈槍**，無論是否成功。先看通緝身分與改造體識別和處理。
             交易   新手入門        廣告

---

## 深空當舖物品圖鑑與基礎價格：270 件物品對照

搜尋270件正式版物品，對照名稱、遊戲原圖與基礎價格，瞭解品相和內容物對報價的影響，並從裝置連結查操作步驟。
    2026/9/29     1.0 +1
### 怎麼認圖查價？

不知道一件貨叫什麼，先按類別對照圖片；知道名稱時，用上方搜尋框查找中文或英文，點選結果即可跳到對應物品。每件物品的兩種名稱放在一起，讀英文攻略時也能對上。繁中名稱是本站依官方簡體中文整理的譯名。

圖鑑收錄270件已按正式版1.0核對的物品，每項都有基礎值；**0**也是有效記錄。品相、內容物與交易修正會改變實際報價。營養果種子的預設參考值為40信用點；品系解放後，不育與可育種子分別為4和8。本站舊譯名作為別名保留，也可搜尋。

第一次進貨先看新手指南。準備買設備時，水質指南和釀酒指南會解釋各件工具怎麼搭配。

### 物品與設備怎麼用？

- 密碼解序器：給鑰匙卡寫入權限。尋找「解碼箱」用法時，先對照設備原圖與正式名稱。

- 能量電池與設備充電。查設備供電和充能器的用法。

- 模組安裝、升級與取出。查模組相容性、卡住後的處理與提取器用法。

- 棉織物用法：裝入濾槽、倒液與排查。按步驟使用韌棉織物，分清化學粗濾與淨水。

也可以回到知識目錄，按目前遇到的問題找攻略。

### 機器區和機器區（擴建）有多大？

1.0中，機器區的庫存網格為6×8，共48格。機器區（擴建），也就是擴建機器區，網格為11×14，共154格，比普通版多106格，用來放置機器。

| 物品 | 網格，寬×高 | 總格數 | 基礎價格 |
| --- | --- | --- | --- |
| 機器區，Machine Bay | 6×8 | 48 | 300信用點 |
| 機器區（擴建），Machine Bay (Expanded) / Expanded Machine Bay | 11×14 | 154 | 1,200信用點 |

每台機器會佔用網格空間，實際能放多少台取決於機器的佔格形狀和擺放方式。48和154都是庫存格數。

打烊後前往傑克遜博士的商店，在本次貨單中查找機器區，購買前對照上方原圖和名稱確認型號。

模組艙，Module Bay，是單台機器內部安裝模組的空間。模組艙擴建套件用於擴大這個空間；上表尺寸對應的是放置整台機器的機器區。

其他儲物空間見儲藏區對照。

### 生活用品

| 物品 | 基礎價格 |
| --- | --- |
| **高級濾水器** Advanced Water Filter 取得與使用 → | 350 |
| **水質掃描儀** AquaScan 取得與使用 → | 200 |
| **一瓶四合一洗滌劑** Bottle of 4 In 1 Wash | 30 |
| **一瓶淨水藥片** Bottle of Water Purification Tablets 取得與使用 → | 1 |
| **一盒帕克斯衛生棉條** Box of PAX Tampons | 100 |
| **一盒瑞克斯避孕套** Box of REX Condoms | 35 |
| **“永恆”煥活精華液** ETERNAL Rejuvenation Serum | 500 |
| **基因掃描儀** Gene Scanner 取得與使用 → | 500 |
| **海德拉科技微型淨水器** HydraTech Micro Water Purifier 取得與使用 → | 325 |
| **水培營養片** Hydroponic Nutrient Tablet | 5 |
| **水培營養片藥瓶** Hydroponic Nutrient Tablet Bottle | 1 |
| **韌棉織物** Kotton Fabric 取得與使用 → | 65 |
| **“本源”費洛蒙香水** L'ESSENCE Pheromone Perfume 取得與使用 → | 200 |
| **一包海明威香菸** Pack of Hemingway Cigarettes | 80 |
| **一卷紙巾** Roll of Paper Towels | 35 |
| **一卷廁紙** Roll of Toilet Paper | 25 |
| **拾荒掃描儀** Scav Scanner 取得與使用 → | 250 |
| **“同步”神經活性香水** SYNC Neuroactive Perfume 取得與使用 → | 150 |
| **「同步」自動補充神經活性香水** SYNC Self-Refilling Neuroactive Perfume 取得與使用 → | 1000 |
| **一支牙膏** Tube of Toothpaste | 15 |
| **紫外線燈** UV Lamp 取得與使用 → | 150 |
| **濾水器** Water Filter 取得與使用 → | 25 |
| **水罐** Water Pitcher 取得與使用 → | 25 |
| **淨水藥片** Water Purification Tablet 取得與使用 → | 6 |
| **水質檢測試劑盒** Water Testing Kit 取得與使用 → | 1 |
| **水質檢測試紙** Water Testing Strip 取得與使用 → | 3 |

### 護甲

| 物品 | 基礎價格 |
| --- | --- |
| **韌棉襯墊** Kotton Lining 取得與使用 → | 60 |

### 建造用品

| 物品 | 基礎價格 |
| --- | --- |
| **金屬螺絲** Metal Screws 取得與使用 → | 14 |

### 容器

| 物品 | 基礎價格 |
| --- | --- |
| **背包（軍用）** Backpack (Army-Issue) 取得與使用 → | 130 |
| **背包（基礎）** Backpack (Basic) 取得與使用 → | 450 |
| **背包（大）** Backpack (Large) 取得與使用 → | 650 |
| **兒童揹包** Kid's Backpack 取得與使用 → | 200 |
| **兒童揹包（大）** Kid's Backpack (Large) 取得與使用 → | 250 |
| **背包（小）** Backpack (Small) 取得與使用 → | 300 |
| **工程鑄造廠物資箱** Engineering Foundry Supply Crate 取得與使用 → | 200 |
| **腰包** Fanny Pack 取得與使用 → | 50 |
| **大型軍用背包** Large Military Backpack 取得與使用 → | 280 |
| **醫療艙物資箱** Medical Bay Supply Crate 取得與使用 → | 200 |
| **一包營養果種子** Packet of Nutrifruit Seeds | 1 |
| **紅魔鬼啤酒箱** Red Imp Beer Case | 1 |
| **挎包（小）** Satchel (Small) 取得與使用 → | 150 |
| **治安部物證櫃** Security Department Evidence Locker 取得與使用 → | 200 |
| **治安部物資箱** Security Department Supply Crate 取得與使用 → | 200 |
| **後勤服務部物資箱** Service Department Supply Crate 取得與使用 → | 175 |

### 裝備

| 物品 | 基礎價格 |
| --- | --- |
| **探險補給箱** Expedition Supply Box | 0 |

### 爆炸物

| 物品 | 基礎價格 |
| --- | --- |
| **閃光彈** Flashbang Grenade | 85 |
| **塑性炸藥** Plastic Explosive | 250 |
| **塑性炸藥（已激活）** Plastic Explosive (Armed) | 250 |
| **煙霧彈** Smoke Grenade | 60 |

### 食物

| 物品 | 基礎價格 |
| --- | --- |
| **動物脂肪** Animal Fat | 40 |
| **貝氏優質香草冰淇淋** Bei's Premium Vanilla Icecream 取得與使用 → | 40 |
| **酸性溶液瓶** Bottle of Acidic Solution | 3 |
| **鹼性溶液瓶** Bottle of Basic Solution | 3 |
| **脂合劑瓶** Bottle of Lipobond Agent | 3 |
| **N-羥甲基鹼溶液瓶** Bottle of N-Methylol Base | 3 |
| **還原劑瓶** Bottle of Reducing Agent | 3 |
| **瓶裝泉水** Bottle of Spring Water | 325 |
| **瓶裝水（大）** Bottle of Water (Large) 取得與使用 → | 9 |
| **瓶裝水（微型）** Bottle of Water (Micro) 取得與使用 → | 3 |
| **瓶裝水（小）** Bottle of Water (Small) 取得與使用 → | 4 |
| **瓶裝水（標準）** Bottle of Water (Standard) 取得與使用 → | 5 |
| **貓咪巧克力棒** Cat Chocolate Bar | 15 |
| **空啤酒瓶** Empty Beer Bottle | 5 |
| **能量飲料** Energy Drink | 18 |
| **繁星果糊** Galaxy Blend | 25 |
| **辣醬** Hot Sauce | 50 |
| **速食拉麵** Instant Ramen | 20 |
| **桶裝水** Jug of Water 取得與使用 → | 15 |
| **輕食能量棒** Li-Eat Snack Bar | 17 |
| **一口食物** Morsel 本站舊稱: 生肉（碎塊） | 30 |
| **努特加** Nudka 取得與使用 → | 85 |
| **動物下水** Offal | 12 |
| **“泥炭”合成肉** Peat | 25 |
| **碳酸代乳** Pilk | 16 |
| **合成奶酪** Processed Cheese | 20 |
| **代糖果汁** Puice | 14 |
| **生肉（小）** Raw Meat (Small) | 65 |
| **生肉（標準）** Raw Meat (Standard) | 159 |
| **紅魔鬼啤酒** Red Imp Beer | 18 |
| **密封水配給包（小）** Sealed Water Ration (Small) | 15 |
| **密封水配給包（標準）** Sealed Water Ration (Standard) | 30 |
| **一小口食物** Small Morsel 本站舊稱: 生肉（小碎塊） | 15 |
| **汽水** Soda | 15 |

### 傢俱

| 物品 | 基礎價格 |
| --- | --- |
| **能量陣列** PowerBloc 取得與使用 → | 200 |

### 槍械

| 物品 | 基礎價格 |
| --- | --- |
| **.22 SR彈藥** .22 SR Ammunition | 20 |
| **.22 SR彈藥（穿甲）** .22 SR Ammunition (AP) | 26 |
| **.22 SR彈藥（非致命）** .22 SR Ammunition (Non-Lethal) | 14 |
| **10毫米彈藥** 10mm Ammunition | 30 |
| **10毫米彈藥（穿甲彈）** 10mm Ammunition (AP) | 36 |
| **10毫米彈藥（非致命）** 10mm Ammunition (Non-Lethal) | 24 |
| **“鐵鑄10”手槍** Ironcraft 10 | 160 |
| **蘭斯維爾左輪手槍** Lancewell Revolver | 240 |
| **“解放者”3D列印手槍** Liberator 3D Printed Pistol | 40 |
| **“巢火”緊湊型手槍** Nestfire Compact | 120 |
| **“和平使者”重型衝鋒槍** Peacemaker Heavy SMG | 280 |
| **“脈衝衛士”電擊槍** PulseGuard Stun Gun | 250 |
| **“壁壘”雙動式霰彈槍** Rampart Dual-Action Shotgun | 310 |
| **哨兵3型。** Sentinel Mk. III. | 200 |
| **電擊槍** Tazer | 100 |

### 水培用品

| 物品 | 基礎價格 |
| --- | --- |
| **螢光莓種子** Bloomberry Seed | 40 |
| **咖啡豆** Coffee Beans | 28 |
| **夢帽菇** Dream Cap | 23 |
| **夢帽菇孢子** Dream Cap Spores | 24 |
| **填料草** Fillerweed | 11 |
| **填料草種子** Fillerweed Seed | 9 |
| **水培蘆葦種子** Hydroreed Seed | 20 |
| **韌棉纖維** Kotton Fiber | 46 |
| **韌棉種子** Kotton Seed | 20 |
| **食用帽菇** Meal Cap | 19 |
| **夢魘帽菇** Nightmare Cap | 26 |
| **營養果** Nutrifruit 取得與使用 → | 12 |
| **營養果種子** Nutrifruit Seed | 40 |

### 材料

| 物品 | 基礎價格 |
| --- | --- |
| **3D列印耗材** 3D Printing Filament 取得與使用 → | 12 |
| **化學用品（普通）** Chemical Supplies (Common) | 50 |
| **化學用品（管制）** Chemical Supplies (Restricted) | 100 |
| **電子元件（普通）** Electronic Parts (Common) 取得與使用 → | 33 |
| **電子元件（稀有）** Electronic Parts (Rare) | 350 |
| **助熔劑（高級）** Flux Smelting Agent (Advanced) | 300 |
| **助熔劑（基礎）** Flux Smelting Agent (Basic) | 4 |
| **槍械零件** Gun Parts | 25 |
| **垃圾** Junk | 3 |
| **醫療用品（普通）** Medical Supplies (Common) | 40 |
| **醫療用品（管制）** Medical Supplies (Restricted) | 0 |
| **金屬錠** Metal Ingot | 50 |
| **金屬礦石** Metal Ore | 15 |
| **放射性廢料** Radioactive Waste 取得與使用 → | 0 |
| **稀有金屬礦石** Rare Metal Ore | 30 |
| **廢金屬** Scrap Metal 取得與使用 → | 10 |
| **線纜** Wire 取得與使用 → | 16 |

### 醫療用品

| 物品 | 基礎價格 |
| --- | --- |
| **一袋“藍血”** Bag of "Blue Blood" | 250 |
| **基礎繃帶** Basic Bandage | 8 |
| **避孕藥** Birth Control Pill | 15 |
| **血袋** Blood Bag | 33 |
| **咖啡因藥片** Caffeine Pill 取得與使用 → | 10 |
| **止血繃帶** Hemostatic Bandage | 16 |
| **免疫寧注射器** Immunivax Injector | 160 |
| **南丁格爾軟膏** Nightingale's Salve | 80 |
| **奧克莫吸注射器** Nomore Injector 取得與使用 → | 50 |
| **噬菌黴素片** Phagimycin Pill | 25 |
| **芬太片** Phanta Pill 取得與使用 → | 12 |
| **醫用酒精** Rubbing Alcohol | 35 |
| **嗅鹽** Smelling Salts | 15 |
| **外科手術工具套裝** Surgery Toolset | 200 |
| **賽納普斯注射器** Synaprest Injector 取得與使用 → | 350 |
| **外用藥膏繃帶** Topical Medication Bandage | 16 |
| **無證噬菌黴素片** Unlicensed Phagimycin Pill | 5 |
| **一小瓶“夢塵”** Vial of "Dream Dust" 取得與使用 → | 18 |
| **一小瓶“夢魘塵”** Vial of "Nightmare Dust" 取得與使用 → | 36 |
| **一板零嚼** ZeroChew Tab | 8 |
| **山奈德片** Zyanide Pill | 33 |

### 近戰武器

| 物品 | 基礎價格 |
| --- | --- |
| **美工刀** Box Cutter | 28 |
| **戰鬥匕首** Combat Knife | 75 |
| **撬棍** Crowbar 取得與使用 → | 75 |
| **玻璃刀** Glass Shiv | 8 |
| **短柄斧** Hatchet | 55 |
| **廚刀** Kitchen Knife | 45 |
| **鉛管** Lead Pipe | 15 |
| **切肉刀** Meat Cleaver | 65 |
| **剝皮刀** Skinning Knife | 100 |
| **眩暈警棍** Stun Baton | 165 |

### 雜項

| 物品 | 基礎價格 |
| --- | --- |
| **鑑定委員會初級證書** Appraisal Board Junior Certificate | 0 |
| **改造體掃描儀** Aug Scanner 取得與使用 → | 200 |
| **暗燈** Black Lamp 取得與使用 → | 120 |
| **螢光莓果釀發酵指南** BloomBerry Wine Fermenting Guide 取得與使用 → | 25 |
| **名片（化學家）** Business Card (Chemist) | 0 |
| **名片（GP採礦合作社）** Business Card (GP Mining Coop) | 0 |
| **名片（綠）** Business Card (Green) | 0 |
| **名片（喬·王爾德）** Business Card (Joe Wild) | 0 |
| **名片（藥商）** Business Card (Pharmacist) | 0 |
| **名片（紅魔鬼釀酒廠）** Business Card (Red Imp Brewery) | 0 |
| **營業許可證** Business Permit 取得與使用 → | 0 |
| **一次性鑰匙卡（空白）** Disposable Keycard (Blank) 取得與使用 → | 15 |
| **一次性鑰匙卡（指揮）** Disposable Keycard (Command) 取得與使用 → | 125 |
| **一次性鑰匙卡（工程）** Disposable Keycard (Engineering) 取得與使用 → | 75 |
| **一次性鑰匙卡（醫療）** Disposable Keycard (Medical) 取得與使用 → | 75 |
| **一次性鑰匙卡（研發）** Disposable Keycard (Research) | 100 |
| **一次性鑰匙卡（治安）** Disposable Keycard (Security) 取得與使用 → | 100 |
| **一次性鑰匙卡（服務）** Disposable Keycard (Service) 取得與使用 → | 40 |
| **一次性鑰匙卡（補給）** Disposable Keycard (Supply) 取得與使用 → | 40 |
| **檔案箱** Dossier | 0 |
| **能量電池** Energy Cell 取得與使用 → | 10 |
| **能量電池（2型）** Energy Cell (Mk. II) 取得與使用 → | 30 |
| **探險日誌** Expedition Log | 0 |
| **手電筒** Flashlight | 40 |
| **食品券** Food Stamp | 30 |
| **食品券鑑定手冊** Food Stamp Identification Handbook | 50 |
| **碎玻璃** Glass Shard | 2 |
| **手寫便籤** Handnote | 0 |
| **海明威色卡** Hemingway Color Checker | 50 |
| **海明威鑑定指南** Hemingway Identification Guide | 50 |
| **免疫寧檢測指南** Immunivax Assessment Guide | 50 |
| **事件回執單** Incident Slip | 0 |
| **租賃協議** Lease Agreement | 0 |
| **導師手冊** Mentor's Handbook | 0 |
| **冶金手冊** Metallurgy Manual | 25 |
| **報紙** Newspaper | 0 |
| **袋裝肋排三明治** Packaged Rib Sandwich | 150 |
| **等離子燃料匣** Plasma Cartridge | 300 |
| **等離子燃料匣鑑定指南** Plasma Cartridge Appraisal Guide | 50 |
| **採購憑單** Procurement Slip | 0 |
| **共和國標誌校驗卡** Republic Logo Checker | 50 |
| **肋明治鑑定指南** Ribwich Appraisal Guide | 50 |
| **拾荒者信物** Scavenger's Token | 100 |
| **刮刮樂彩票** Scratcher Ticket | 20 |
| **治安部檔案** Security Record | 0 |
| **治安部通緝名單** Security Wanted List | 0 |
| **自充電能量電池** Self Charging Energy Cell 取得與使用 → | 400 |
| **憑單** Slip | 0 |
| **一疊刮刮樂彩票（5張）** Stack of Scratcher Tickets (5) | 100 |
| **垃圾桶** Trash Can | 0 |
| **地下交易所清單** Underground Exchange Directive | 0 |
| **排水口** Water Drain | 0 |
| **水龍頭** Water Faucet | 0 |
| **水資源交易手冊** Water Trading Manual | 25 |

### 模組

| 物品 | 基礎價格 |
| --- | --- |
| **警報傳輸模組** Alarm Transmitter Module | 100 |
| **空白模組** Blank Module | 60 |
| **化學設備模組** Chemical Equipment Module | 250 |
| **損壞模組** Corrupted Module 取得與使用 → | 45 |
| **密碼解序器晶片（指揮）** Cryptographic Desequencer Chip (Command) 取得與使用 → | 350 |
| **密碼解序器晶片（工程）** Cryptographic Desequencer Chip (Engineering) 取得與使用 → | 250 |
| **密碼解序器晶片（醫療）** Cryptographic Desequencer Chip (Medical) 取得與使用 → | 250 |
| **密碼解序器晶片（治安）** Cryptographic Desequencer Chip (Security) 取得與使用 → | 300 |
| **密碼解序器晶片（服務）** Cryptographic Desequencer Chip (Service) 取得與使用 → | 200 |
| **密碼解序器晶片（補給）** Cryptographic Desequencer Chip (Supply) 取得與使用 → | 200 |
| **環保模組** Eco Module 取得與使用 → | 100 |
| **效率模組** Efficiency Module 取得與使用 → | 70 |
| **熔爐模組（高爐）** Furnace Module (Blast) | 150 |
| **熔爐模組（垃圾處理）** Furnace Module (Junk Processing) | 120 |
| **模組艙擴建套件** Module Bay Expansion Kit 取得與使用 → | 150 |
| **模組提取器（高級）** Module Extractor (Advanced) 取得與使用 → | 350 |
| **模組提取器（基礎）** Module Extractor (Basic) 取得與使用 → | 100 |
| **神經核心模組（受限）** Neural Core Module (Capped) 取得與使用 → | 250 |
| **節點（中）** Node (Medium) 取得與使用 → | 50 |
| **節點（小）** Node (Small) 取得與使用 → | 50 |
| **超頻模組** Overclock Module 取得與使用 → | 100 |
| **性能模組** Performance Module 取得與使用 → | 70 |
| **質量模組** Quality Module 取得與使用 → | 70 |
| **精煉模組** Refinement Module 取得與使用 → | 100 |
| **報廢模組** Ruined Module 取得與使用 → | 30 |
| **優化模組** Streamlining Module | 125 |

### 飛船系統

| 物品 | 基礎價格 |
| --- | --- |
| **化學師儲藏區** Chemist Storage Bay 取得與使用 → | 400 |
| **槍械師儲藏區** Gunsmith Storage Bay 取得與使用 → | 400 |
| **機器區** Machine Bay | 300 |
| **機器區（擴建）** Machine Bay (Expanded) · Expanded Machine Bay · 擴建機器區 | 1200 |
| **簡易儲藏區** Makeshift Storage Bay | 250 |
| **迷你走私儲藏區** Mini Smuggler's Bay 取得與使用 → | 150 |
| **走私者暗格** Smuggler's Bay 取得與使用 → | 250 |
| **走私者暗格（改進）** Smuggler's Bay (Modified) 取得與使用 → | 400 |
| **儲藏區** Storage Bay 取得與使用 → | 300 |
| **儲藏區（擴建）** Storage Bay (Extended) 取得與使用 → | 850 |

### 工具

| 物品 | 基礎價格 |
| --- | --- |
| **高級渦輪增壓器** Advanced Turbo Booster 取得與使用 → | 300 |
| **藍色標籤列印機** Blue Labeler 取得與使用 → | 50 |
| **一次性渦輪增壓器** Disposable Turbo Booster 取得與使用 → | 35 |
| **食品霓虹燈牌** Food Neon Sign | 100 |
| **日用品霓虹燈牌** Household Item Neon Sign | 100 |
| **標籤列印機** Labeler 取得與使用 → | 50 |
| **放大鏡** Magnifying Glass 取得與使用 → | 50 |
| **材料霓虹燈牌** Material Neon Sign | 100 |
| **醫療霓虹燈牌** Medical Neon Sign | 200 |
| **等離子切割機** Plasma Cutter | 80 |
| **螺絲刀** Screwdriver | 25 |
| **工具箱** Toolbox 取得與使用 → | 150 |
| **水霓虹燈牌** Water Neon Sign | 100 |
| **剪線鉗** Wire Cutter | 40 |

### 酒類

| 物品 | 基礎價格 |
| --- | --- |
| **螢光莓** BloomBerry 取得與使用 → | 14 |
| **超級酵母** Super Yeast 取得與使用 → | 20 |
| **合成酵母** Synthetic Yeast 取得與使用 → | 8 |
| **酒瓶** Wine Bottle 取得與使用 → | 12 |
| **酵母** Yeast 取得與使用 → | 10 |
| **酵母生物反應器** Yeast Bioreactor 取得與使用 → | 250 |

收購贓物或違禁品前，還要查看犯罪與藏貨指南中的熱度和違禁等級。

### 遊戲內手冊

- 仿生機械手鑑定指南

- 等離子燃料匣鑑定指南

- 肋明治鑑定指南

### 物品種類詞典：全部60類標籤與用途

- 物品種類詞典：全部60類標籤與用途

- 顧客需求面板：限制條件、種類多樣與預算

             物品   交易        廣告
