# 初始特性比較

遊戲 0.46D 原版，依特性頁的順序排列。名稱沿用遊戲的簡體中文；正點數代表花費，負點數代表選取後獲得點數。基礎為 3 點、4 格，部分特性會增加可帶數量。首輪遊玩時由遊戲鎖住選取。

各特性的效果說明與當次可用選項可用 `starting_perks` 查閱。互斥欄列出兩個方向的關係，選取後回執的 `locked_by` 會指出實際擋住它的特性。

| 遊戲名稱 | 選擇 ID | 類型 | 點數 | 額外格數 | 互斥 |
|---|---|---|---:|---:|---|
| 警觉 | `alert` | 正面 | 2 | 0 | — |
| 舌灿莲花 | `diplomatic` | 正面 | 2 | 0 | — |
| 凶神恶煞 | `intimidating` | 正面 | 2 | 0 | — |
| 自动贩卖机 | `vending` | 正面 | 2 | 0 | — |
| 八面玲珑 | `well_connected` | 正面 | 2 | 0 | 全民公敌 |
| 管道畅通 | `working_pipes` | 正面 | 2 | 0 | — |
| 改造体危机 | `aug_menace` | 中性 | 1 | 0 | — |
| 奇人异士 | `colorful_character` | 中性 | 1 | 3 | — |
| 巧手 | `handy` | 正面 | 1 | 0 | — |
| 幸运儿 | `lucky` | 正面 | 1 | 0 | 厄运缠身 |
| 简易反应堆 | `makeshift_reactor` | 中性 | 1 | 0 | — |
| 模范公民 | `model_citizen` | 正面 | 1 | 0 | 前科犯 |
| 熟练的拾荒客 | `proficient_scavenger` | 正面 | 1 | 0 | 肢体残缺 |
| 心向革命军 | `revolution_sympathizer` | 正面 | 1 | 0 | — |
| 存款 | `saving` | 正面 | 1 | 0 | — |
| 索耶的伙计 | `sawyer_crew` | 正面 | 1 | 0 | — |
| 神秘仰慕者 | `secret_admirer` | 正面 | 1 | 0 | — |
| 短眠者 | `short_sleeper` | 正面 | 1 | 0 | — |
| 马屁精 | `sycophant` | 正面 | 1 | 0 | — |
| 磁带迷 | `tapehead` | 正面 | 1 | 0 | — |
| 改装能手 | `tinkerer` | 正面 | 1 | 0 | — |
| 低保户 | `welfare_program` | 正面 | 1 | 0 | 被配给站拒之门外 |
| 王尔德的红人 | `wild_favorite` | 正面 | 1 | 0 | 王尔德的眼中钉 |
| 谨慎的拾荒客 | `careful_scavenger` | 中性 | 0 | 0 | 肢体残缺 |
| 囤积癖 | `hoarder` | 中性 | 0 | 0 | — |
| 黄金地段 | `prime_real_estate` | 中性 | 0 | 0 | 偏街陋巷 |
| 广场恐惧症 | `agoraphobia` | 負面 | -1 | 0 | — |
| 酒精成瘾 | `alcohol_addiction` | 負面 | -1 | 0 | — |
| 高昂房租 | `bad_lease` | 負面 | -1 | 0 | — |
| 被配给站拒之门外 | `banned_commissary` | 負面 | -1 | 0 | 低保户 |
| 前科犯 | `convict` | 負面 | -1 | 0 | 模范公民 |
| 退行性骨关节炎 | `degenerative_osteoarthritis` | 負面 | -1 | 0 | 肢体残缺 |
| 赌博成瘾 | `gambling_addiction` | 負面 | -1 | 0 | — |
| 文盲 | `illiterate` | 負面 | -1 | 0 | — |
| 麻醉品成瘾 | `narcotic_addiction` | 負面 | -1 | 0 | — |
| 尼古丁成瘾 | `nicotine_addiction` | 負面 | -1 | 0 | — |
| 偏街陋巷 | `side_street` | 負面 | -1 | 0 | 黄金地段 |
| 厄运缠身 | `unlucky` | 負面 | -1 | 0 | 幸运儿 |
| 王尔德的眼中钉 | `wild_bad_side` | 負面 | -1 | 0 | 王尔德的红人 |
| 大嘴巴 | `blabbermouth` | 負面 | -2 | 0 | — |
| 敲诈勒索 | `blackmail` | 負面 | -2 | 0 | — |
| 人尽皆知的告密者 | `known_snitch` | 負面 | -2 | 0 | — |
| 肢体残缺 | `maimed` | 負面 | -2 | 0 | 熟练的拾荒客、谨慎的拾荒客、退行性骨关节炎 |
| 极简主义者 | `minimalist` | 負面 | -2 | 0 | 苦行者 |
| 全民公敌 | `public_enemy` | 負面 | -2 | 0 | 八面玲珑 |
| 不善议价 | `awful_negotiator` | 負面 | -3 | 0 | — |
| 哨兵崩溃综合征 | `sentinel` | 負面 | -3 | 0 | — |
| 苦行者 | `ascetic` | 負面 | -4 | 0 | 极简主义者 |
