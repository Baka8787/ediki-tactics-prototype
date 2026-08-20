# SPEC — Combat（攻擊、傷害、命中、防禦、死亡）

> ## ✅ PROTOTYPE BASELINE — 2026-08-13
>
> [OD-05](../OPEN-DECISIONS.md#od-05)：**Deterministic Hit**（無 HIT/EVA/Critical RNG）＋
> **`Damage = max(1, ATK - DEF)`**。
> [OD-06](../OPEN-DECISIONS.md#od-06)：Guard = 3 AP，`Incoming Damage × 0.5`，持續到該單位下一回合開始。
>
> 🔴 **本檔 §2 的除法公式 `ATK×100/(100+DEF)` 與 §3.1 的兩個數值封包已作廢**
> —— 見 [CONFLICT-07](../CONFLICTS.md#conflict-07)。保留原文僅供追溯，**不得據以實作**。
> §2.1 的 DEF 減傷率表、TTK 推算、Exposure 承傷表全部隨之作廢。
>
> `IRandomSource` **未建立**：目前沒有第二個實作要跑。
>
> 實作：`Assets/Scripts/Core/BattleRules.cs`、`BattleSimulator.cs`

| | |
|---|---|
| **Purpose** | 定義攻擊怎麼判定、傷害怎麼算、單位怎麼死 |
| **Audience** | 程式（實作與測試）、企劃（數值裁決） |
| **Source of Truth** | 本檔 |
| **Dependencies** | [SPEC-unit-data.md](SPEC-unit-data.md)、[SPEC-battle-flow.md](SPEC-battle-flow.md) |
| **Related** | [02-design/battle-experience.md](../02-design/battle-experience.md) |

> 🔴 **本檔的核心公式沒有可查證的來源。**
> 傷害公式只出現在 SPEC v0.1，GDD 全文沒有任何傷害公式 →
> [CONFLICT-07](../CONFLICTS.md#conflict-07)。
> 命中模型與數值封包未拍板 → [OD-05](../OPEN-DECISIONS.md#od-05)。
> 防禦的效果從未被定義 → [OD-06](../OPEN-DECISIONS.md#od-06)。

---

## 1. 攻擊

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-COMBAT-01** | Stage 01 的攻擊射程為 **1**（僅相鄰格） | SPEC v0.1 §2.1（「近戰射程 1」）、GDD（小耗持長矛，未給射程數值） | `STABLE` | 對非相鄰目標的 `AttackCommand` 被拒絕 |
| **R-COMBAT-02** | 攻擊消耗該單位的攻擊 AP 成本（來自資料） | GDD 三.1、SPEC v0.1 §3.1 | `STABLE`（結構）／值 `OPEN → OD-01` | 見 [SPEC-battle-flow](SPEC-battle-flow.md) R-AP-04 |
| **R-COMBAT-03** | 攻擊目標必須是敵對陣營的存活單位 | `DERIVED` | `DERIVED` | 對己方／已死單位的攻擊被拒絕 |
| **R-COMBAT-04** | 一次攻擊產生 `AttackResolved` → （命中時）`HpChanged` → （HP ≤ 0 時）`UnitDied` 的有序 Effect 序列 | SPEC v0.1 §6.3 | `STABLE` | Effect 順序符合因果 |

> **R-COMBAT-01 的射程 1 是從 Exposure 的定義反推的**，
> 而不是 GDD 直接給的。GDD 說小耗持長矛但沒給射程；
> SPEC v0.1 §2.1 用「近戰射程 1」推導 Exposure。
> 若射程不是 1，[R-GRID-06](SPEC-grid-terrain.md) 立刻失效。
> **建議請企劃確認**（低風險確認題）。

---

## 2. 傷害

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-COMBAT-05** | 傷害 = `ATK × 100 / (100 + DEF)`，**無條件捨去** | SPEC v0.1 §3.4（標記「確定」） | `UNSOURCED` → [CONFLICT-07](../CONFLICTS.md#conflict-07) | 給定 ATK/DEF 對照表逐項比對 |
| **R-COMBAT-06** | 傷害計算全程使用整數運算 | SPEC v0.1 §6.6 戒一 | `STABLE` | 規則層不出現 `float` / `double` |
| **R-COMBAT-07** | 傷害永遠 ≥ 0，且**永遠不會被降到 0**（除法減傷無下限截斷） | `DERIVED` ← R-COMBAT-05 | `DERIVED` | ATK ≥ 1 且 DEF 任意大時傷害仍 ≥ 0；ATK=0 時傷害 = 0 |
| **R-COMBAT-24** | 傷害公式是**每場戰鬥的資料**（`rules damage=`），不是常數。未指定 = [OD-05](../OPEN-DECISIONS.md#od-05) 的減法基線 | `DERIVED` ← 2026-08-15 實驗需求 | `BASELINE`（gym 專用） | `DefaultRuleSet_IsTheDecidedBaseline`；沒有 `rules` 行的 encounter 與此規則存在之前逐位元相同 |
| **R-COMBAT-25** | 百分比模式：傷害 = `ATK × (100 − min(DEF, 90))%`，下限 1 | `DERIVED` ← [OD-27](../OPEN-DECISIONS.md#od-27) | `BASELINE`（gym 專用，**未裁決**） | `PercentageModel_*` 三條測試 |
| **R-COMBAT-26** | **破甲**：傷害結算一律使用 `EffectiveDef(target)` ＝ `max(0, DEF − 破甲量)`，而不是 `UnitDef.Def`。攻擊與反擊都適用 | `DERIVED` ← [OD-34](../OPEN-DECISIONS.md#od-34) | `BASELINE`（**未裁決**） | `ArmorBreak_CutsDefAndRaisesDamage`；`ArmorBreak_NeverDrivesDefBelowZero` |
| **R-COMBAT-27** | 破甲以**回合戳記**記錄（`ArmorBrokenUntilTurn`），生效條件為 `TurnIndex ≤ 戳記`；扣減量存在**目標**身上（`ArmorBrokenAmount`） | `DERIVED` ← [OD-34](../OPEN-DECISIONS.md#od-34) | `BASELINE` | `ArmorBreak_SurvivesIntoTheTargetsOwnPhase` |
| **R-COMBAT-28** | 破甲**不可疊加**：對已破甲的目標再次施放一律拒絕 | `DERIVED` ← [OD-34](../OPEN-DECISIONS.md#od-34) | `BASELINE` | `ArmorBreak_DoesNotStack` |

> ⚠️ **編號說明**：本次擴充原本要求編為 `R-COMBAT-24`，但該 ID 已被
> 「傷害公式資料化」佔用（[workflows §3](../05-development/workflows.md)：**ID 不重用**），
> 因此順延為 **26–28**。

> 🔴 **R-COMBAT-26 是唯一一條會改變既有傷害結算路徑的規則。**
> 它之所以沒有改變任何既有結果，是因為未破甲時 `EffectiveDef` 逐位元等同 `Def.Def` ——
> A4 golden hash 未更動即為證據。
> **新增任何傷害計算時必須走 `EffectiveDef`；直接讀 `Def.Def` 是這條規則要防的 bug。**
>
> 破甲量（目前 20）**是資料，不是規格**：它由 `units.txt` 的 `armorBreakAmount` 決定。
> 之所以是 20，是因為本專案的刀數階梯是懸崖而非斜坡，不跨階的破甲一文不值 ——
> 見 [`ShippedGenjinB_BreaksArmourAcrossAHitCountStep`](../06-validation/test-strategy.md) 的斷言。

> **R-COMBAT-24/25 沒有改變任何既有行為。** 它們把「用哪個公式」變成資料，
> 預設仍然是 [OD-05](../OPEN-DECISIONS.md#od-05) 的 `max(1, ATK − DEF)`。
> 兩者的實測差異見 [playtest-metrics §11.3](../06-validation/playtest-metrics.md)：
> **減法會讓低攻單位對高防目標的傷害掉到保底 1，也就是完全無法處理它** ——
> 這是兩個公式最重要的差別，而它不是平衡問題，是設計語言問題。**待 OD-27 裁決。**

### 2.1 DEF 的實際減傷率（推導，非規格）

| DEF | 減傷 |
|---|---|
| 20 | 16.7% |
| 50 | 33.3% |
| 100 | 50% |
| 200 | 66.7% |

**這張表有一個必須知道的副作用**：DEF 邊際遞減。
把 DEF 從 50 加倍到 100，傷害只從 66 掉到 50（−24%）；要把傷害砍半得把 DEF 加到 203。

> → 直接推導出 [OD-06](../OPEN-DECISIONS.md#od-06)：**防禦不能定義成 DEF 加成。**

### 2.2 平衡方法

| ID | Statement | Source | Status |
|---|---|---|---|
| **R-COMBAT-08** | 所有數值調整必須用 **TTK（幾次攻擊能擊殺）** 複驗，不看單發傷害 | SPEC v0.1 §4.4、RESEARCH §6 | `STABLE`（流程規範） |

理由：乘法 buff 在除法公式下的實際加成會隨目標 DEF 浮動 ——
`ATK × 1.3` 不等於最終傷害 +30%。

> 這是一條**流程規格**而非行為規格，Acceptance 是「調整數值的 PR 附 TTK 表」。
> 見 [05-development/definition-of-done.md](../05-development/definition-of-done.md)。

---

## 3. 命中

| ID | Statement | Source | Status |
|---|---|---|---|
| **R-COMBAT-09** | 命中模型（隨機命中 vs 必中） | GDD（全體單位有 HIT/EVA）／SPEC v0.1 §4（兩個互斥封包） | `OPEN → OD-05` ／ [CONFLICT-04](../CONFLICTS.md#conflict-04) |
| **R-COMBAT-10** | 若採隨機命中：`命中率 = HIT × (1 − EVA)` | SPEC v0.1 §3.4（標記「待決」） | `OPEN → OD-05` |
| **R-COMBAT-11** | 命中判定必須透過 `IRandomSource`，規則層**不得直接呼叫任何 RNG** | SPEC v0.1 §6.4 | `STABLE` | Core 中不出現 `System.Random` / `UnityEngine.Random` |
| **R-COMBAT-12** | 若使用 RNG，必須是**整數** RNG（0–99 比較整數化命中率），不得用 float | SPEC v0.1 §6.6 戒一 | `STABLE` | `IRandomSource.NextInt(exclusiveMax)` 是唯一的隨機來源 |

### 3.1 兩個數值封包的戰鬥後果（**互斥，未拍板**）

**選一個，不要混。** 見 [OD-05](../OPEN-DECISIONS.md#od-05) 與 [CONFLICT-03](../CONFLICTS.md#conflict-03)。

> 📍 **兩個封包的單位數值請看
> [SPEC-unit-data.md §3](SPEC-unit-data.md)** —— 那是單位數值的 Source of Truth。
> 本節只列**戰鬥層面的後果**，不重複數值。

#### 封包 1：隨機命中（貼近現行 GDD）

桃 → 小 50 傷（**一擊必殺**）；小 → 桃 66 傷；
桃命中 0.704 / 小命中 0.48；清場期望 **5.7 回合**。

| Exposure | 累計承傷 | 結果 |
|---|---|---|
| 1 | 180 | **勝，剩 120 HP（40%）** |
| 2 | 315 | 一線之差落敗 |
| 3 | 405 | 明確落敗 |
| 4 | 450 | 約第 2.4 回合倒 |

**優點**：只改一個數字，貼近 GDD。一擊必殺讓傷害預覽變成二元、好讀。
**缺點**：命中骰帶來的挫折感。一次 miss 就可能翻盤，而 prototype 樣本小、
玩家會歸因到運氣而非設計。

#### 封包 2：完全資訊（攻擊必中）

桃 → 小 50 傷（**2 刀**）；小 → 桃 **33 傷**（撐 9 刀）；
清場**剛好 8 回合**（全部可心算）。

| Exposure | 每回合承傷 | 結果 |
|---|---|---|
| 1 | 33 | **勝，剩 36 HP（12%）** |
| 2 | 66 | 第 5 回合倒 |
| 3 | 99 | 第 3–4 回合倒 |
| 4 | 132 | 第 3 回合倒 |

**優點**：所有數字可心算 → 完全資訊 → 每場戰鬥變成謎題。
玩家失敗時無法怪黑箱。而且**規則層完全不需要亂數**（見 §3.2）。
**缺點**：偏離 GDD 的 HIT/EVA 設定。

> **兩組後果都建立在 R-COMBAT-05 上**，而 R-COMBAT-05 是 `UNSOURCED`。
> 公式若被推翻，兩個封包都要重算。

> **推算的限制（SPEC v0.1 附錄 B 自述）**：以上回合數與承傷基於
> 「每回合各單位攻擊一次」的簡化模型，**未計入移動、卡位與變異數**。
> 實際數字要用自動化跑分複驗，見 [playtest-metrics.md](../06-validation/playtest-metrics.md)。

### 3.2 封包 2 的技術後果

| ID | Statement | Status |
|---|---|---|
| **R-COMBAT-13** | 若採封包 2，規則層**完全不需要亂數**，[確定性戒一](../04-architecture/determinism.md)自動滿足 | `DERIVED` |

這是選封包 2 的隱藏技術紅利：確定性測試變成純粹的「同指令串 → 同雜湊」，
不需要 seed 管理。**但 `IRandomSource` 介面仍必須存在**（R-COMBAT-11），
因為封包 1 是要跑的變體。

---

## 4. 防禦

| ID | Statement | Source | Status |
|---|---|---|---|
| **R-COMBAT-14** | 防禦動作的 AP 成本為 **3** | GDD 三.1 | `STABLE` |
| **R-COMBAT-15** | 防禦的效果 | — | `OPEN → OD-06` **（BLOCKER，GDD 從未定義）** |
| **R-COMBAT-16** | 防禦**不得**實作為 DEF 加成，必須是受到傷害的直接乘數 | `DERIVED` ← R-COMBAT-05（§2.1 的邊際遞減） | `DERIVED`（依賴 `UNSOURCED` 的 R-COMBAT-05） |

未定義的附帶問題（見 [OD-06](../OPEN-DECISIONS.md#od-06)）：
同回合可否多次防禦、是否疊加、防禦後可否移動、AI 會不會用防禦。

---

## 5. 死亡

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-COMBAT-17** | 單位 HP ≤ 0 時死亡，產生 `UnitDied` | SPEC v0.1 §6.3 | `STABLE` | HP 降至 0 或負值時恰產生一次 `UnitDied` |
| **R-COMBAT-18** | 死亡單位立即從戰場移除，不再佔據格子、不再計入 Exposure | `DERIVED` | `DERIVED` | 死亡後該格可被進入；Exposure 查詢結果隨之改變 |
| **R-COMBAT-19** | 死亡語意（昏厥 vs 消滅） | GDD 狀態列表 3 | `CONFLICT → CONFLICT-08` | Stage 01 行為上可能無差別 |

> **R-COMBAT-18 沒有書面來源，但影響很大**：
> SPEC v0.1 §2.3 要求 UI 顯示「目前有幾隻**活著的**敵人的威脅範圍涵蓋這格」，
> 「活著的」三個字隱含死亡單位不再計入。標為 `DERIVED` 並建議企劃確認。

---

## 6. Stage 01 明確不做

| ID | Statement | Source | Status |
|---|---|---|---|
| **R-COMBAT-20** | 不實作 GDD 的任何狀態效果（強壯／脫力／固元／虛弱／加速／緩速／中毒／暈眩／流血／瀕死） | [prototype-charter](../01-vision/prototype-charter.md) §4 | `STABLE`（負面規格） |
| **R-COMBAT-21** | 不實作瀕死（HP ≤ 15%）觸發 | 同上 | `STABLE`（負面規格） |
| **R-COMBAT-22** | 不實作技能（斬／討鬼斬／仙照・淺淨／號令・不屈）；只有通用「攻擊」動作 | 同上 ＋ [ODD-04](../DOCUMENT-MAP.md#odd-04) | `STABLE`（負面規格） |
| **R-COMBAT-23** | 不實作裝備效果（桃木刀 命中 +10%、行腳鎧 防禦 +1） | 同上 | `STABLE`（負面規格） |

> ⚠️ **R-COMBAT-23 有一個張力**：GDD Stage 01 明確寫「桃太郎裝備桃木刀（命中 +10%）、
> 行腳鎧（防禦 +1）」。若採封包 1（隨機命中），桃木刀的 +10% 命中是否計入？
> 封包 1 的 `桃太郎 HIT 0.80` 是含裝備還是不含裝備？**文件沒說。**
> 這是 [OD-05](../OPEN-DECISIONS.md#od-05) 裁決時要一併釐清的細節。
