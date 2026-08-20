# 戰棋 / 策略角色扮演遊戲(SRPG / Tactical RPG)設計資源總整理

*給兩人獨立團隊、和風神話戰棋 RPG prototype 的參考總結(繁體中文/台灣用語)*

## TL;DR(直接回答核心問題)
- 你的 8AP 規格與 Into the Breach 的「完全資訊」哲學高度相容,但有兩個立即該修的風險:**「攻擊 5 AP」佔了 62% 的回合預算**,會讓大多數回合退化成單一選擇(AP 制的典型失敗模式);以及**命中/閃避的隨機性與你確定性的污染機制哲學相矛盾**——建議攻擊改必中,或改用 FE 式 2RN / 乘法命中曲線。
- 敵人「進入威脅範圍才啟動」是 Fire Emblem 與 XCOM 的核心張力,但兩者都有著名失敗模式(逐一釣怪 / pod scamper);**難度應由「同時能接觸到玩家的敵人數(接觸面)」而非敵人總數決定**——你的污染擴散剛好是控制接觸面的絕佳動態槓桿。
- 架構上採 **command pattern + 遊戲邏輯/表現層分離 + 確定性模擬器**,可一次解決重播、undo、AI 推演三件事;移動範圍用 Dijkstra flood fill(吃地形成本)、AI 點對點尋路用 A*。

## Key Findings(各主題重點結論)
1. **AP 制**產生決策空間的條件是「多個成本相近的動作可組合、剩餘 AP 有意義」;單一動作成本過高會殺死決策空間。
2. **aggro / 威脅範圍**是 SRPG 核心張力,但需用地圖幾何避免「逐一釣怪」淪為唯一最佳解。
3. **難度真正的槓桿是接觸面**(同時能攻擊你的敵人數),不是敵人總數。
4. **命中隨機**是最大爭議點:FE 用 2RN 偷偷幫玩家、XCOM 用隱藏補正、Into the Breach 完全移除;你的確定性核心應與命中哲學一致。
5. **傷害公式**你選的百分比減傷是現代主流,但乘法 buff 的實際效果會隨敵人 DEF 變動,須用 TTK 驗證。
6. **架構與 AI**有成熟開源教學與 Unity 框架可直接 fork,兩人團隊應把心力集中在差異化的污染機制。

---

## 1. AP / 行動點制 vs.「移動+一個行動」制

**重點結論:** AP 制提供更高戰術深度,代價是平衡難度與認知負擔;「移動+一個行動」(FFT、Tactics Ogre、XCOM 的 two-action)較易平衡且強迫團隊配合。你的「攻擊 5 AP」佔 8 AP 預算超過一半,是典型高風險點——多數回合會退化成「移動幾格 + 攻擊」。

- **AP 制代表作:** Fallout、Divinity: Original Sin(初代基礎 7 max AP、每回合恢復 3.5)、Divinity: Original Sin 2、Jagged Alliance、Wasteland、Battle Brothers。**DOS2 的具體數值:所有玩家角色預設以 4 AP 起手、單回合上限 6 AP**(Lone Wolf 天賦可把上限提到 8、每回合恢復提到 6);DOS2 刻意讓每人固定 AP,正是為了避免初代因 AP 可堆疊而生的平衡問題。來源:[Divinity Wiki - Original Sin 2 Action Points](https://divinity.fandom.com/wiki/Original_Sin_2_Action_Points)、[Rolemaster Blog](https://www.rolemasterblog.com/rmu-update-an-action-point-system-in-action-divinity-original-sin-2/)。
- **社群共識(核心取捨):** 單一動作制較易平衡、強迫玩家把角色當成互補小隊使用;AP 制允許「走出掩體、射擊、再走回掩體」這類個體操作,深度更高但需更多平衡心力。來源:[rpgcodex 論壇](https://rpgcodex.net/forums/threads/tb-system-action-points-vs-single-action.22403/)、[GameDev.net](https://www.gamedev.net/forums/topic/696888-action-points-or-not/)。
- **XCOM two-action 的啟示:** 資深玩家指出新 XCOM 的「簡化」two-action 反而讓他更專注戰術而非瑣事(站起、坐下、整理背包)。啟示:別讓玩家把 AP 花在「無論如何都得做、沒有戰術決策」的瑣事上。來源:[GameDev.net](https://gamedev.net/forums/topic/696888-action-points-or-not/5379672/)。
- **常見失敗模式:** 當「必要動作」佔用大量點數時,壞運氣或高成本會把角色鎖死好幾回合(Galaxy Defenders 玩家回報:武器卡彈耗掉關鍵動作點,角色數回合形同廢人)。來源:[BoardGameGeek](https://boardgamegeek.com/thread/1249275/variant-simplified-action-point-system)。

**AP 制產生決策空間的三條件(綜合社群共識):**(a) 有多個成本相近、彼此競爭的動作;(b) 未用完的 AP 有意義(可跨回合保留或觸發反應);(c) 單一動作不吃掉超過半數預算。

---

## 2. 敵人啟動 / aggro 機制與威脅範圍

**重點結論:** 「威脅範圍(threat range / danger zone)」應對玩家可見(FE 的 danger zone 顯示鍵)。用啟動範圍逐一分割敵人(pulling / baiting)是雙面刃:是好玩的戰術,但一旦變成唯一最佳解就會讓遊戲變無聊。

- **Fire Emblem 威脅範圍:** 選取敵人可顯示其攻擊範圍(移動 + 武器射程);Three Houses/Engage 可一鍵顯示全體敵人危險區,用於規劃站位並「引誘敵人移動到你想要的位置」。敵人通常優先攻擊「較軟」單位(法師、低等單位)。來源:[SuperCheats - Danger Radius](https://www.supercheats.com/fire-emblem-three-houses/walkthrough/danger-radius)、[Fire Emblem Wiki - Range](https://fireemblemwiki.org/wiki/Range)、[Gameranx Engage 戰鬥指南](https://gameranx.com/features/id/430966/article/fire-emblem-engage-complete-combat-guide/)。
- **XCOM pod activation(scamper)爭議:** 敵人以 pod 為單位待命,被發現才啟動並衝向掩體。最常見批評:它導致「最佳戰術永遠是龜速前進,一次啟動一個 pod、殲滅、重複」,一位玩了 30 年戰棋的玩家稱其為「在拋光一坨屎」,並指出 XCOM 用任務計時器與空降增援「補洞」。來源:[X-COM 2 深度書評/Goodreads](https://www.goodreads.com/author_blog_posts/10153778-game-review-x-com-2-sequels-rebellions-the-rule-of-cool-verisimil)、[Steam 討論](https://steamcommunity.com/app/268500/discussions/0/364041776196273323/)。
- **敵人 AI 啟動實作(社群慣例):** 常分為「攻擊型(進入範圍即啟動追擊)/防守型(原地待命)/群組型(整組一起啟動)」。你的「進入威脅範圍才啟動」正是這套 aggro 設計的標準形式。

**對難度的影響:** 逐一釣怪之所以成為最佳解,是因為地圖允許玩家一次只暴露在一個敵群威脅範圍內。要讓 aggro 有趣,關卡必須控制接觸面(見第 3 節)。

---

## 3. 難度來源:同時能接觸玩家的敵人數 vs. 敵人總數

**重點結論:** 戰棋難度的真正槓桿是「每回合有多少敵人能實際攻擊到你(engagement surface / 接觸面)」,而非地圖上敵人總數。瓶頸與地圖幾何決定這個數字——這也是為何 Into the Breach 說「多一個敵人就從『有趣挑戰』變成『完全不可能』」。

- **Chokepoint 是控制接觸面的工具:** 瓶頸讓少數單位守住通往地圖其他部分的通道;「強迫敵人通過瓶頸永遠對玩家有利」。 [Game Developer](https://www.gamedeveloper.com/design/the-metrics-of-space-tactical-level-design) 可用狹窄地形、障礙、環境效果製造瓶頸。來源:[gdp3 - Choke Points](http://virt10.itu.chalmers.se/index.php/Choke_Points)、[Game Developer - The Metrics of Space](https://www.gamedeveloper.com/design/the-metrics-of-space-tactical-level-design)。
- **地圖幾何塑造戰術情緒:** 《The Metrics of Space》示範同樣的敵人配置,只要改變幾何(把玩家逼進角落 vs. 給遮蔽物)就能反轉戰術優勢。來源:[Game Developer](https://www.gamedeveloper.com/design/the-metrics-of-space-tactical-level-design)。
- **ITB 的極端案例:** 因地圖只有 8x8、每場只有數個敵人,接觸面被壓到極小,「多一個敵人」的邊際影響巨大(見第 5 節引言)。

**對你的專案的意義:** 你的「污染擴散」本質上就是動態接觸面控制——被污染格等於擴大敵方有效威脅面積,淨化則是玩家縮小接觸面的動作。這是很好的設計槓桿(見建議)。

---

## 4. 命中率 / 隨機性的設計爭議

**重點結論:** 這是你規格最需要下決定之處。三大做法:(a) FE 的 2RN「善意謊言」;(b) XCOM 的隱藏補正;(c) ITB 完全移除。**由於你已有確定性的污染/淨化核心(ITB 路線),混入隨機命中會與整體哲學衝突。** 建議走必中或乘法命中曲線。

- **Fire Emblem「true hit / 2RN」:** 從《The Binding Blade》(FE6)起,命中判定擲兩個亂數取平均,再與顯示命中率比較。效果是 sigmoid 曲線:高命中(90+)幾乎不會 miss,低命中(<50)比顯示值更易 miss。因玩家單位命中率通常較高,這對玩家有利——是刻意的「善意謊言」。來源:[Fire Emblem Wiki - True hit](https://fireemblemwiki.org/wiki/True_hit)、[Serenes Forest - True Hit](https://serenesforest.net/general/true-hit/)。Fates/Shadows of Valentia/Engage 改用以 50% 為界的「hybrid」系統。
- **玩家心理(loss aversion / near-miss):** 損失趨避是刻意設計的心理槓桿。行為經濟學的估計係數 **λ≈2.25(損失的心理權重約為等量獲得的 2.25 倍)**,出自 Tversky & Kahneman 1992《Advances in Prospect Theory: Cumulative Representation of Uncertainty》(*Journal of Risk and Uncertainty*, Vol. 5, pp. 297–323),此係數從 25 名 Berkeley/Stanford 研究生的假設性賭局選擇估得(1979 年原始論文僅定性提出「losses loom larger than gains」,未給數字)。實務效果:錯過一個高命中攻擊讓玩家覺得「被騙」,閃過一個低命中攻擊卻覺得「戰術天才」——這正是 2RN 存在的理由。近失(near-miss)研究另指出「近失比明確輸贏更能激勵玩家再試一次」。來源:[Game Designing - Designing for Chance (RNG)](https://gamedesigning.org/beyond/designing-for-chance-the-evolution-of-rng-random-number-generation-in-2026/)、[R.L. Reid, The Psychology of the Near Miss (PDF)](https://www.stat.berkeley.edu/~aldous/157/Papers/near_miss.pdf)。
- **XCOM 的隱藏命中補正(具體數值):** 在 Legend 以外的難度,遊戲依「連續 miss、折損士兵、隊伍人數」偷偷調整命中率,且 UI 不顯示。依玩家逆向工程的 `DefaultGameCore.ini`:命中上限 `MaxAimAssistScore=95`、`ReasonableShotMinimumToEnableAimAssist=50`(顯示命中 >50% 才啟動補正)、`NormalSquadSize=4`。各難度補正遞減:Rookie 有整體命中乘數 `BaseXComHitChanceModifier=1.2`、連續 miss 加成 `MissStreakChanceAdjustment=10`、折損士兵加成 `SoldiersLostXComHitChanceAdjustment=15`、敵方連續命中減成 `HitStreakChanceAdjustment=-10`;Veteran 乘數降為 1.1;Commander 降為 1.0(僅保留連續 miss 加成 =15);**Legend 全部歸零(完全停用補正)**。這引發長年爭議:「戰術遊戲不該顯示錯誤的骰率」。來源:[Steam - Remove Aim Assists mod 說明(id=617993180)](https://steamcommunity.com/sharedfiles/filedetails/?id=617993180)、[Steam 討論 - Hidden Aiming](https://steamcommunity.com/app/268500/discussions/0/412448158159953952/)。
- **Into the Breach 完全移除隨機命中:** Justin Ma 說他們想做「每次死亡都覺得是自己的錯」的遊戲,因此用「敵人預告攻擊」取代隨機:「當每個敵人攻擊都被預告、你的攻擊選項也沒有隨機,遊戲就開始像解謎。」 [gamedeveloper](https://www.gamedeveloper.com/game-platforms/road-to-the-igf-subset-games-i-into-the-breach-i-)  [Game Developer](https://www.gamedeveloper.com/game-platforms/road-to-the-igf-subset-games-i-into-the-breach-i-) 關鍵論點:完全資訊讓玩家失敗時無法怪罪黑箱,失敗焦點從「我沒猜對」轉為「我的策略不夠好」。 [Jeremiahgames](https://jeremiahgames.com/2019/03/04/perfect-information-the-killer-feature-of-slay-the-spire-and-into-the-breach/) 來源:[Game Developer - Road to the IGF](https://www.gamedeveloper.com/game-platforms/road-to-the-igf-subset-games-i-into-the-breach-i-)、[Jeremiah Games - Perfect Information](https://jeremiahgames.com/2019/03/04/perfect-information-the-killer-feature-of-slay-the-spire-and-into-the-breach/)。
- **Advance Wars 的折衷:** 傷害有小範圍隨機(顯示 52% 傷害 = 80% 機率造成 5 點、20% 機率 6 點),變異限制在很窄範圍,所以玩家能同時規劃兩種結果。來源:[BrainGoodBlog - Advance Wars Game Design](https://blog.braingoodgames.com/2017/02/13/advance-wars-game-design/)。

---

## 5. Into the Breach 的設計(與你的污染機制最相關)

**重點結論:** ITB 整套設計圍繞「完全資訊 + 保護建築而非殲滅 + push 作為核心動詞 + 小地圖短局」這幾個彼此鎖定的約束,是你「把地圖狀態當成勝敗核心」的最佳範本。核心方法論是 Matthew Davis 在 GDC 2019 講的「**Designing with Constraints(用約束來設計)**」:找出要固定的關鍵設計,然後跟隨這些約束所要求的更大設計——「跟隨設計」而非「創造設計」。

一手來源:Matthew Davis, **GDC 2019「Into the Breach Design Postmortem」**(投影片 PDF:[GDC Vault media mirror](https://media.gdcvault.com/gdc2019/presentations/Into%20the%20Breach%20Postmortem%20Final.pdf);影片免費於 GDC YouTube,見 [Game Developer 報導](https://www.gamedeveloper.com/design/video-how-subset-games-designed-i-into-the-breach-i-);GDC Vault 頁 [gdcvault.com](https://gdcvault.com/play/1026333/-Into-the-Breach-Design))。

- **完全資訊 / 移除 RNG:** 投影片列出「Telegraphed Attacks:所有敵人攻擊都顯示 / 無命中判定 / 玩家回合完全確定性」,並把「Reduce Random Chance」列為 Subset 核心約束。Justin Ma:「我們偏好規則清楚的遊戲……想做一個每次死亡都是你自己的錯的遊戲。」來源:GDC 投影片、[Road to the IGF](https://www.gamedeveloper.com/game-platforms/road-to-the-igf-subset-games-i-into-the-breach-i-)。
- **保護建築而非殲滅:** 投影片直白寫著「**殺敵不如操縱敵人(Killing enemies isn't as fun as manipulating them)**」,並記錄從攻擊型 win-state 轉向「Defensive Gameplay」(Power Grid 作 fail-state)。靈感來自超級英雄電影「整座城市被毀但沒人在意」。 [gamedeveloper](https://www.gamedeveloper.com/game-platforms/road-to-the-igf-subset-games-i-into-the-breach-i-) Ma:「當你有多元目標與優先級而不只是『殺光敵人』,就會產生有趣選擇……只要存活、把損害降到最低撐到戰鬥結束,往往比消滅敵人更重要。」來源:GDC 投影片、[Road to the IGF](https://www.gamedeveloper.com/game-platforms/road-to-the-igf-subset-games-i-into-the-breach-i-)。
- **push / 擊退作為核心動詞:** Davis:「你必須永遠能操縱它們。如果敵人處在你無法移動它的位置,它就變成一股無法阻擋的力量,那一點都不好玩。」地圖刻意避免把建築排成對角/L 形,因為許多攻擊會把目標擊退撞進建築。 [Game Developer](https://www.gamedeveloper.com/design/reimagining-failure-in-strategy-game-design-in-i-into-the-breach-i-) Ma:「第一種火砲攻擊可以傷害敵人,但它把相鄰格擊退的副作用通常更有影響力。」 [gamedeveloper](https://www.gamedeveloper.com/game-platforms/road-to-the-igf-subset-games-i-into-the-breach-i-) 來源:[Game Developer - Reimagining failure](https://www.gamedeveloper.com/design/reimagining-failure-in-strategy-game-design-in-i-into-the-breach-i-)、[Road to the IGF](https://www.gamedeveloper.com/game-platforms/road-to-the-igf-subset-games-i-into-the-breach-i-)。
- **8x8 小地圖 + 手工設計:** Davis 說控制地圖設計很重要,因 8x8 只有 64 格,「手工設計 100 張地圖比做一套程序生成系統便宜得多」。來源:[Game Developer - Reimagining failure](https://www.gamedeveloper.com/design/reimagining-failure-in-strategy-game-design-in-i-into-the-breach-i-)。
- **短局 / 難度是懸崖:** 投影片「Puzzle-Game Difficulty」段落:「太難會變成『無解』/ 太難的門檻是一道懸崖 / 難度受設計約束 / 簡單也可以很好玩」。 [gdcvault](https://media.gdcvault.com/gdc2019/presentations/Into%20the%20Breach%20Postmortem%20Final.pdf) Ma:「多一個敵人就把一場戰鬥從『有趣挑戰』變成『完全不可能』,所以這是非常微妙的平衡。」 [gamedeveloper](https://www.gamedeveloper.com/game-platforms/road-to-the-igf-subset-games-i-into-the-breach-i-)  [Game Developer](https://www.gamedeveloper.com/game-platforms/road-to-the-igf-subset-games-i-into-the-breach-i-) (注:campaign「1–2 小時」是評測描述,一手來源只提「2–4 島」「Dynamic Length」「Short Experiences」。)來源:GDC 投影片、[Road to the IGF](https://www.gamedeveloper.com/game-platforms/road-to-the-igf-subset-games-i-into-the-breach-i-)。
- **失敗的重新設計:** 機甲每場戰鬥後完全回血,「這樣你可以真正把血條當成戰鬥中的工具,而不是每次受傷都擔心」; [gamedeveloper](https://www.gamedeveloper.com/design/reimagining-failure-in-strategy-game-design-in-i-into-the-breach-i-) 要玩家「忘掉幾乎所有其他策略遊戲教的事——失去你的角色是最糟的事」。 [gamedeveloper](https://www.gamedeveloper.com/design/reimagining-failure-in-strategy-game-design-in-i-into-the-breach-i-) 當機甲是唯一目標時威脅太輕(「機甲只要走開就好」),唯一讓它變難的方法是「用敵人與危險區灌滿棋盤,那會變得不必要地複雜——正是我們想避免的」。來源:[Game Developer - Reimagining failure](https://www.gamedeveloper.com/design/reimagining-failure-in-strategy-game-design-in-i-into-the-breach-i-)。
- **設計哲學總結(投影片結語):** 「刪掉壞設計可以很有趣 / 意識到你被困在某個類型裡 / 跟隨自然的設計約束 / 『最不糟』的選項也可以 ok」。 [gdcvault](https://media.gdcvault.com/gdc2019/presentations/Into%20the%20Breach%20Postmortem%20Final.pdf) 來源:GDC 投影片。

其他一手訪談:[Noclip - The Design of FTL & Into the Breach(YouTube)](https://www.youtube.com/watch?v=BT-qkoaeGrw)、[2018 TGDF - Justin Ma「Design Lessons from FTL and Into the Breach」(YouTube)](https://www.youtube.com/watch?v=4LDazcvZwzI)、[Designer Notes 70: Justin Ma(播客)](https://www.idlethumbs.net/designernotes/episodes/justin-ma-1)。

---

## 6. 傷害公式與數值設計

**重點結論:** 你選的百分比減傷(ATK×100/(100+DEF))是現代 ARPG 主流,優點是沒有「懸崖」、每點防禦邊際遞減、裝備能平滑成長;缺點是防禦與「傷害減免%」的角色容易模糊。減法(ATK−DEF)則製造「懸崖」:防禦低於門檻幾乎無敵、高於門檻幾乎無意義。**關鍵陷阱:乘法 buff(ATK×1.3)在百分比公式下,對最終傷害的實際加成會因敵人 DEF 而異,不是固定 +30%。**

- **減法 vs. 除法(核心差異):** 減法在攻防接近時,一點差距造成巨大百分比波動(11 傷 vs 10 甲 = 1 點,12 傷 = 2 點,+100%);高攻時一點幾乎無感。「減法系統製造懸崖——這是設計意圖,不是 bug」,適合想要清楚力量分層的遊戲(打不動龍直到拿到屠龍劍)。除法(DMG×100/(100+Armor))則平滑、漸近、永不達 100% 減傷。來源:[Toolhub - RPG damage formulas explained](https://toolhub.software/articles/rpg-damage-formulas/)。
- **減法的長遊戲問題:** 多階裝備下減法會讓傷害逼近 0 或爆炸,平衡「非常微妙」;RPG Maker 社群共識是減法「容易把防禦做到沒用或無敵,甚至在不同時期兩者都發生」。來源:[RPG Maker Forums - Armor values](https://forums.rpgmakerweb.com/threads/armor-values.108924/)。
- **百分比/除法讓 HP 更好調:** 開發者指出用除法公式時「想拉長戰鬥只要加敵人 HP,不用煩惱該給多少 DEF」;buff/debuff 改用「增減傷害%」的狀態而非改 DEF。 [RPG Maker Forums](https://forums.rpgmakerweb.com/threads/armor-values.108924/) 來源:[RPG Maker Forums](https://forums.rpgmakerweb.com/threads/armor-values.108924/)。
- **TTK(time to kill)作為平衡工具:** 用「幾次攻擊能擊殺」而非單次傷害來平衡回合制;damage calculator 類工具會算 expected damage、crit-adjusted average、DPS 檢查是否意外變成一擊必殺。來源:[Toolhub - Damage Calculator](https://toolhub.software/damage-calculator/)。
- **權威書籍:** *Game Balance*(Ian Schreiber & Brenda Romero, CRC Press 2021)有專章討論 transitive mechanics(成本曲線)、intransitive mechanics(剋制關係/payoff matrix)、機率與亂數、situational balance。Romero 本人參與過 Wizardry 與 Jagged Alliance。 [Routledge](https://www.routledge.com/Game-Balance/Schreiber-Romero/p/book/9781498799577) 來源:[Routledge - Game Balance](https://www.routledge.com/Game-Balance/Schreiber-Romero/p/book/9781498799577)。

---

## 7. 戰棋的關卡設計與教學關設計

**重點結論:** 業界最一致的教學原則是「一次教一個機制(one mechanic at a time / layering)」,並在孤立安全環境中讓玩家練習後再組合。Advance Wars 是黃金範本。

- **Layering(逐層堆疊):** Advance Wars 前 10 關逐步引入機制,「到第 10 課玩家已有一大套動詞可用」。好處:降低製作風險(核心機制是地基、新元素是 bonus)、學習路徑清晰;壞處:學習曲線變陡。來源:[Lostgarden - Game Design Review: Advance Wars](https://lostgarden.com/2005/09/14/game-design-review-advance-wars-dual-strike/)。
- **教學設計的層級:** Game Developer《Teaching Game Mechanics: A Hierarchy of Learning》提出用「玩家怎麼贏/怎麼輸/怎麼變強」三問來設計教學流。來源:[Game Developer](https://www.gamedeveloper.com/design/teaching-game-mechanics-a-hierarchy-of-learning)。
- **教/練/測(teach, train, test):** 獨立開發者實務心得——每關引入一個機制(移動→旋轉→梯子→輸送帶),簡單關其實很花時間但能避免壓垮玩家。 [itch](https://asteriagames.itch.io/instructions-not-included/devlog/573918/day-5) 來源:[itch.io devlog - day 5](https://asteriagames.itch.io/instructions-not-included/devlog/573918/day-5)。
- **地圖尺寸與單位數量:** Advance Wars「用相對低的複雜度(單位種類、地形種類、規則)達成很強的設計」。來源:[BrainGoodBlog](https://blog.braingoodgames.com/2017/02/13/advance-wars-game-design/)。
- **必研究的 7 款戰棋:** Game Developer 邀多位開發者選出:FFT(把既有框架改造進新類型)、Battle Brothers(決策生深度)、Fire Emblem(用規則而非玩家自由度製造深度,類似擴充版剪刀石頭布)、 [gamedeveloper](https://www.gamedeveloper.com/design/7-great-tactical-rpgs-that-every-developer-should-study) The Banner Saga(確定性戰鬥 + 敘事)、 [Game Developer](https://www.gamedeveloper.com/design/7-great-tactical-rpgs-that-every-developer-should-study) Jagged Alliance(幽默與角色)、Valkyria Chronicles(移除嚇人 UI 但保留深度)、Disgaea(位移類技能強化站位)。 [gamedeveloper](https://www.gamedeveloper.com/design/7-great-tactical-rpgs-that-every-developer-should-study) 來源:[Game Developer - 7 great tactical RPGs](https://www.gamedeveloper.com/design/7-great-tactical-rpgs-that-every-developer-should-study)。

---

## 8. 實作架構參考

**重點結論:** 三個支柱一次解決重播、undo、AI 推演:(1) **command pattern**——把每個動作封裝成可執行/可還原/可序列化的物件;(2) **邏輯與表現層分離**——遊戲邏輯純資料、確定性,表現層只負責動畫;(3) **AI 用同一個模擬器推演**——AI 呼叫與玩家相同的 command 來評估結果。

- **Command pattern(一手經典):** 《Game Programming Patterns》Command 章說明:把動作做成 command 物件即可實作 undo(單人回合制策略讓玩家專注策略而非猜測)、 [Game Programming Patterns](https://gameprogrammingpatterns.com/command.html) replay(記錄每個實體每幀執行的 command 再重跑模擬,而非存整個遊戲狀態)、 [Game Programming Patterns](https://gameprogrammingpatterns.com/command.html) 網路傳輸。來源:[Game Programming Patterns - Command](https://gameprogrammingpatterns.com/command.html)。
- **戰棋實例(Rads & Relics):** 該戰棋部落格詳述如何用 command 系統實作 undo(戰棋常見痛點:玩家忘記某攻擊會暴露自己、或誤點格子), [Rads and Relics](https://radsandrelics.com/posts/command-systems/) 並得到 instant replay 除錯的好處。來源:[Rads and Relics - Commands in Games](https://radsandrelics.com/posts/command-systems/)。
- **狀態機架構(最完整教學):** The Liquid Fire《Tactics RPG》系列(Unity/C#)用 state machine 管理整個戰鬥流程,被社群譽為「網路上最好的戰棋教學資源」; [The Liquid Fire](https://theliquidfire.com/2015/06/01/tactics-rpg-state-machine/) 有完整的 turn order、hit rate、status effects、AoE、AI 各章。另有 Godot 版《Godot Tactics RPG》。來源:[The Liquid Fire - Tactics RPG State Machine](https://theliquidfire.com/2015/06/01/tactics-rpg-state-machine/)。
- **移動範圍 vs. 尋路(關鍵區分):**
  - **移動/攻擊範圍:用 Dijkstra / BFS flood fill**——從單位往四方向擴散、考慮地形加權成本,得到「所有可達格 + 到達路徑」。正對應你的「地形移動成本」。來源:[Lucas Gray - 2D TRPG Pathfinding in Unity](https://www.lucasegray.com/blog/2d-trpg-pathfinding-in-unity)、[The Liquid Fire - Path Finding](https://theliquidfire.com/2015/06/08/tactics-rpg-path-finding/)。
  - **點對點最短路:用 A***——有啟發函數,比 Dijkstra 快,適合 AI 即時反應(避免卡頓)。來源:[dev.to - A* vs Dijkstra](https://dev.to/ffteamnames/a-vs-dijkstra-choosing-the-right-pathfinding-algorithm-for-a-browser-based-tactics-game-3837)。
  - The Liquid Fire 解釋為何移動範圍用 flood fill 而非 A*:戰棋是「先算出所有可達格再讓玩家選目標」,不是先知道目標。 [The Liquid Fire](https://theliquidfire.com/2015/06/08/tactics-rpg-path-finding/) 來源:[The Liquid Fire](https://theliquidfire.com/2015/06/08/tactics-rpg-path-finding/)。
- **確定性與重播:** command pattern 是確定性重播的基礎——重播時只要用相同 command 重跑正常模擬。 [Game Programming Patterns](https://gameprogrammingpatterns.com/command.html) 對你的污染狀態機尤其重要:確定性讓 AI 能可靠推演污染擴散結果。來源:[Game Programming Patterns - Command](https://gameprogrammingpatterns.com/command.html)。
- **AI:utility-based(業界主流):**
  - **核心概念:** 每個可能動作用啟發函數算出 0–1 的 utility 分數,選最高分(或加權隨機)。 [github](https://github.com/WarreVannTittelboom/UtilityAI) Dave Mark 的 GDC 2010《Improving AI Decision Modeling Through Utility Theory》是公認入門;XCOM: EU 就用 utility 值為戰術移動評分。 [GameDev.net](https://www.gamedev.net/forums/topic/692379-ai-for-turn-based-strategy-game-in-style-of-total-war/) 來源:[Game AI Pro - An Introduction to Utility Theory (PDF)](https://www.gameaipro.com/GameAIPro/GameAIPro_Chapter09_An_Introduction_to_Utility_Theory.pdf)、[GameDev.net](https://www.gamedev.net/forums/topic/692379-ai-for-turn-based-strategy-game-in-style-of-total-war/)。
  - **戰棋實例:** Heralds of the Order 開發者分享其 grid-based tactics AI 用 utility theory(move / use ability / end turn,每個動作有多個 consideration)。來源:[itch.io - Heralds of the Order AI devlog](https://archeangames.itch.io/heralds-of-the-order/devlog/97239/our-take-on-grid-based-tactics-ai-overview)。
  - **進階:** Utility AI + Monte Carlo Tree Search(MCTS)透過 blackboard 架構結合可做更強推演(如 Tactical Troops: Anthracite Shift)。來源:[arXiv - The Many Challenges of Human-Like Agents (PDF)](https://arxiv.org/pdf/2505.20011)。

---

## 9. 實用工具與社群資源

**開源 Unity / 引擎專案:**
- [EmblemForge](https://github.com/DallenLarson/EmblemForge)——Unity C# 戰棋框架,模仿 FE/Advance Wars,含 GameManager、UnitController、Tile(地形效果/buff)、MapGenerator,MIT 授權。 [GitHub](https://github.com/DallenLarson/EmblemForge)
- [TacticsKitUnity3D](https://github.com/JeffersonBC/TacticsKitUnity3D)——Unity 3D 戰棋套件(注意:2015 年舊 Unity 版本)。 [GitHub](https://github.com/JeffersonBC/TacticsKitUnity3D)
- [zkauff/TacticsGame](https://github.com/zkauff/TacticsGame)、[Kassout/unityProject_RogueTactics](https://github.com/Kassout/unityProject_RogueTactics)、[moonantonio/TacticTurnBased](https://github.com/ninpl/TacticTurnBased/blob/master/README.en.md)——FE/FFT 風格 Unity 原型。
- GitHub topic 匯總:[tactical-rpg](https://github.com/topics/tactical-rpg)、[final-fantasy-tactics](https://github.com/topics/final-fantasy-tactics)。

**教學/部落格:**
- [The Liquid Fire - Tactics RPG 系列](https://theliquidfire.com/2015/06/01/tactics-rpg-state-machine/)(Unity,最完整)。
- [Lucas Gray - 2D TRPG Pathfinding](https://www.lucasegray.com/blog/2d-trpg-pathfinding-in-unity)。
- [Rads and Relics - Command Systems](https://radsandrelics.com/posts/command-systems/)。

**GDC 演講 / 一手來源:**
- Matthew Davis, [Into the Breach Design Postmortem (GDC 2019)](https://gdcvault.com/play/1026333/-Into-the-Breach-Design)。
- Dave Mark & Kevin Dill, Improving AI Decision Modeling Through Utility Theory (GDC 2010)。

**書籍:**
- Ian Schreiber & Brenda Romero, [*Game Balance*](https://www.routledge.com/Game-Balance/Schreiber-Romero/p/book/9781498799577)(CRC Press, 2021)。
- Dave Mark, *Behavioral Mathematics for Game AI*;*Game AI Pro* 系列([線上免費](https://www.gameaipro.com/))。

**設計參考 wiki:**
- [Fire Emblem Wiki](https://fireemblemwiki.org/)、[Serenes Forest](https://serenesforest.net/)(FE 數值系統)。

---

## Recommendations(針對你這套 8AP 規格的具體建議)

**階段一(prototype 核心,先驗證手感):**
1. **重新檢視 AP 成本結構。** 攻擊 5 AP 佔 8 AP 的 62%,會讓多數回合只剩「移動 + 攻擊」——正是 AP 制的典型失敗模式。建議測試把攻擊降到 3–4 AP,讓「移動+攻擊+防禦」「攻擊兩次」「移動+道具+攻擊」等組合都可行。**基準:** 若 playtest 中超過 70% 的回合玩家都做同一組動作,代表決策空間塌陷,須調整成本。可參考 DOS2「固定 4 起手、上限 6」的收斂做法。
2. **命中系統與污染機制哲學對齊。** 既然污染/淨化是確定性的地圖核心(ITB 路線),強烈建議**攻擊改為必中**,把不確定性放在「敵人預告攻擊 + 污染擴散」層面,而非命中骰。若堅持保留閃避,採 FE 式 2RN 或乘法命中曲線(高命中更穩、低命中更飄)以緩和 λ≈2.25 的損失趨避痛感。**觸發改變的門檻:** 若 playtest 顯示玩家頻繁 save-scum 或抱怨 miss,立即移除隨機命中。

**階段二(關卡與難度):**
3. **用地圖幾何(接觸面)而非敵人總數調難度。** 把污染擴散當成「動態接觸面」:污染格擴大敵方有效威脅範圍,淨化則是玩家縮小接觸面的動作——與 ITB「保護地圖 > 殲滅敵人」完全同構。設計瓶頸與地形,讓玩家能主動控制「同時有幾個敵人能攻擊到我」。
4. **威脅範圍必須對玩家可見**(FE 的 danger zone 一鍵顯示)。這是完全資訊哲學的基礎,也讓你的「進入威脅範圍才啟動」變成玩家可規劃的資源,而非陷阱。
5. **警惕「逐一釣怪」變成唯一最佳解**(XCOM pod 的教訓)。用計時壓力(污染每回合擴散)或目標設計(淨化特定格、保護特定物件)獎勵主動出擊而非龜速。

**階段三(數值與架構):**
6. **傷害公式保留 ATK×100/(100+DEF),但明確區分「防禦」與「傷害減免%」的角色**,避免功能重疊。記得乘法 buff:ATK×1.3 的實際最終傷害加成會隨敵人 DEF 變動,做數值表時用 TTK(幾發擊殺)驗證,而非只看單發傷害。
7. **架構直接採 command pattern + 邏輯/表現分離 + 確定性模擬器。** 這一次投資同時給你 undo(戰棋必備,減少誤點挫折)、replay(除錯污染擴散)、AI 推演(AI 用同一模擬器評估污染結果)。移動範圍用 Dijkstra flood fill(吃地形成本)、AI 尋路用 A*。
8. **瀕死/狂暴閾值(HP ≤ 15%)是很好的「確定性戲劇」**——因為確定觸發,玩家能預測與規劃,符合完全資訊哲學。可把它做成敵人的「預告」之一(下回合狂暴),讓它成為玩家要處理的謎題而非隨機驚嚇。

**團隊只有兩人的務實建議:**
9. **直接 fork 一個開源框架**(EmblemForge,或跟著 The Liquid Fire 系列做)當地基,把有限心力集中在差異化的污染機制上。ITB 的核心教訓就是「用約束來設計」——8x8、手工地圖、少數敵人,兩人團隊反而該擁抱這種小而精的約束。
10. **地圖手工設計、保持小尺寸**(ITB:64 格手工比程序生成便宜)。教學關一次教一個機制(Advance Wars 路線):先教移動與 AP,再教污染,再教淨化,最後教瀕死狂暴。

---

## Caveats(來源性質與限制說明)

- **有明確一手來源的事實:** Into the Breach 的設計決策(完全資訊、保護建築、push、8x8、難度懸崖)來自 Matthew Davis 的 GDC 2019 投影片與 Justin/Jay Ma 的多篇 2018 訪談,屬設計師本人陳述。FE 的 2RN、XCOM 的隱藏 aim assist(寫在 `DefaultGameCore.ini`)、DOS2 的 AP 數值皆有 wiki/檔案佐證。Command pattern 的用途來自《Game Programming Patterns》原文。λ≈2.25 出自 Tversky & Kahneman 1992 的同儕審查論文。
- **屬社群共識/設計慣例(非單一權威):** AP 制 vs. 單動作制的取捨、敵人 AI 的「攻擊/防守/群組」分類、「逐一釣怪是 XCOM 最佳解」的批評——來自論壇、玩家與開發者部落格,反映廣泛共識但非受控研究。
- **ITB「1–2 小時 campaign」數字:** 屬評測與二手描述;Davis/Ma 一手來源只提到「2–4 島」「Dynamic Length」「Short Experiences」約束,未給明確小時數。
- **near-miss / loss aversion 心理學:** 有紮實學術基礎(Reid 的近失研究、Tversky & Kahneman 的損失趨避 λ≈2.25),但把它直接套到戰棋 miss 的因果推論屬設計圈的合理外推,非針對戰棋的實證研究;λ 係數本身估自 25 名研究生的假設性賭局,樣本小。
- **XCOM aim assist 數值(95% 上限、Legend 停用、各難度乘數):** 來自玩家逆向工程 .ini 與 mod 說明,非 Firaxis 官方文件,但一致性高、可自行在 `DefaultGameCore.ini` 驗證。
- **傷害公式的優缺點:** Toolhub 與 RPG Maker 社群整理清楚且與主流認知一致,但 Toolhub 屬工具站/二手整理,建議搭配《Game Balance》一手章節交叉確認。
- **本報告未能取得的一手影像逐字稿:** GDC 講座無公開逐字稿;ITB 引用以官方投影片 PDF 為主,口述細節以同期訪談交叉佐證。開源 Unity 框架多為個人/小團隊專案,程式品質與維護狀態不一,fork 前請自行審視 commit 活躍度與授權。