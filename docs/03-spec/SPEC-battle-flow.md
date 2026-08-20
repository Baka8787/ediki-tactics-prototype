# SPEC — Battle Flow（回合、AP、動作、勝敗）

> ## ✅ PROTOTYPE BASELINE — 2026-08-13
>
> [OD-01](../OPEN-DECISIONS.md#od-01) 已裁決：**攻擊 = 4 AP**，AP 上限 8（可一回合攻擊兩次）。
> 防禦 = 3 AP（[OD-06](../OPEN-DECISIONS.md#od-06)）。
> 原本標 `OPEN → OD-01` 的條目一律視為 `BASELINE`：**可實作、可測試、未來可重新評估**。
>
> **仍未決**：道具（[OD-09](../OPEN-DECISIONS.md#od-09)，`UseItemCommand` 未實作）、
> AP 跨回合保留（[CONFLICT-05](../CONFLICTS.md#conflict-05)，實作沿用「不保留」）。
>
> 實作：`Assets/Scripts/Core/BattleSimulator.cs`、`Assets/_Project/Resources/Data/units.txt`

| | |
|---|---|
| **Purpose** | 定義一場戰鬥的結構：誰在什麼時候能做什麼、花多少、什麼時候結束 |
| **Audience** | 程式（實作與測試）、企劃（確認規則） |
| **Source of Truth** | 本檔 |
| **Dependencies** | [SPEC-unit-data.md](SPEC-unit-data.md)（AP 成本的值） |
| **Related** | [02-design/battle-experience.md](../02-design/battle-experience.md)（意圖）、[04-architecture/simulation-core.md](../04-architecture/simulation-core.md) |

---

## 1. 回合結構

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-TURN-01** | 採陣營輪流制：玩家陣營全體行動 → 敵方陣營全體行動 → 進入下一回合 | SPEC v0.1 §3.1 | `STABLE` | 從 `TurnStarted` 事件序列可讀出 faction 交替，且一個 turnIndex 內每個陣營恰出現一次 |
| **R-TURN-02** | 一個「回合（turn）」包含所有陣營各一次行動階段 | SPEC v0.1 §3.1 | `DERIVED` ← R-TURN-01 | `turnIndex` 在雙方都結束後才 +1 |
| **R-TURN-03** | 陣營內單位的行動順序由玩家自由決定（玩家陣營） | SPEC v0.1 §3.1 | `STABLE` | Stage 01 玩家只有一個單位，本條在 Stage 01 無可觀察行為，但實作不得寫死「單一單位」假設 |
| **R-TURN-04** | 陣營內單位的行動順序（敵方）＝ **spawn 宣告順序（id 序）**，永不重排 | [OD-10](../OPEN-DECISIONS.md#od-10) | `BASELINE` | `BattleState.Units` 依 id 排序且不重排；`EnemyPhase_IsDeterministic` |
| **R-TURN-05** | 一個陣營的行動階段在 `EndTurnCommand` 被接受後結束 | SPEC v0.1 §6.3 | `STABLE` | `EndTurnCommand(factionId)` 產生 `TurnEnded(turnIndex, factionId)` |

---

## 2. AP

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-AP-01** | 每個單位有一個 AP **上限**，Prototype 基線為 **10** | [OD-21](../OPEN-DECISIONS.md#od-21)（2026-08-14）**覆蓋** GDD 三.1 的「最大 8 點」 | `BASELINE` | `units.txt` 的 `ap=10`；程式碼不得出現字面值 |
| **R-AP-02** | 單位的 AP 在其陣營的回合開始時**恢復 `apRegen`，加在保留下來的餘額上，並以上限截頂**：`Ap = min(殘留 + apRegen, MaxAp)`。基線 `apRegen = 8` | [OD-21](../OPEN-DECISIONS.md#od-21) | `BASELINE` | `ApReset` Effect 的 `NewAp` 等於該式；`ApEconomyTests` |
| **R-AP-03** | 未使用的 AP **跨回合保留**，上限為 R-AP-01 的 `MaxAp` | [OD-21](../OPEN-DECISIONS.md#od-21) —— ✅ **[CONFLICT-05](../CONFLICTS.md#conflict-05) 已因此解決** | `BASELINE` | 花 5 AP 的回合結束後，下回合起始 AP = `min(3+8, 10) = 10` |
| **R-AP-07** | 攻擊的 AP 成本為 **4**（全體） | [OD-01](../OPEN-DECISIONS.md#od-01)（2026-08-13） | `BASELINE` | `units.txt` 的 `attackCost=4`；一個單位在 8 AP 下可攻擊兩次 |

> 🔴 **2026-08-17 回寫**：R-AP-01/02/03 與 §2.1、§2.2 原本停留在 [OD-21](../OPEN-DECISIONS.md#od-21)
> （2026-08-14）與 [OD-01](../OPEN-DECISIONS.md#od-01)（2026-08-13）之前的狀態，
> **與實作及 `units.txt` 直接矛盾**。
> 依 [documentation-rules](../99-governance/documentation-rules.md)，本檔是 battle flow 的 Source of Truth，
> 而**當時的 Source of Truth 是錯的** —— 正確資訊只存在於 `units.txt` 與 OD 索引列。
> 本次修正消除該漂移；**沒有改變任何實作行為**。
| **R-AP-04** | 每個動作的 AP 成本由**資料**定義，可 per-unit 覆寫 | SPEC v0.1 §6.4、§8.1 路線 C | `STABLE` | 架構測試 A5：程式碼中不得出現任何 AP 成本字面值 |
| **R-AP-05** | 若指令的 AP 成本 > 單位當前 AP，指令必須被拒絕，且**不產生任何 Effect、不改變 state** | SPEC v0.1 §6.3（Validate 階段） | `STABLE` | `Execute` 回傳 `Ok = false` 與 `RejectReason`，且傳入 state 的雜湊不變（架構測試 A2） |
| **R-AP-06** | 每次成功的動作產生一個 `ApSpent(unitId, amount, remaining)` Effect | SPEC v0.1 §6.3 | `STABLE` | Effect log 中 `ApSpent` 的 `remaining` 等於扣除後的 AP |

### 2.1 動作成本表（Prototype 基線）

| 動作 | AP 成本 | Source | Status |
|---|---|---|---|
| 移動 | **該格的地形成本**（道路 1／森林 2／泥沼 3） | [OD-04](../OPEN-DECISIONS.md#od-04) —— ✅ [CONFLICT-01](../CONFLICTS.md#conflict-01) 已解決 | `BASELINE` |
| **攻擊** | **4** | [OD-01](../OPEN-DECISIONS.md#od-01) | `BASELINE`（R-AP-07） |
| 防禦 | 3，效果為受到傷害 × 0.5 | [OD-06](../OPEN-DECISIONS.md#od-06) | `BASELINE`（[R-COMBAT-25](SPEC-combat.md)） |
| 休息 | 2，回復 `MaxHp × 10%`，**並結束該單位的行動** | [OD-21](../OPEN-DECISIONS.md#od-21) | `BASELINE` |
| 道具 | 2 | GDD 三.1 | `OPEN → OD-09`（Prototype 不做，`UseItemCommand` 未實作） |

> ⚠️ **實驗性動詞（嘲諷／遲滯／擊退／淨化／破甲）的成本不列在這裡**，
> 因為它們是 per-unit 資料而不是全域規則。見 `units.txt` 與 [OD-34](../OPEN-DECISIONS.md#od-34)。

### 2.2 AP 的組合空間（**攻擊 4、每回合恢復 8、上限 10**）

**這是推導結果，不是規格。**

| 組合 | AP | 備註 |
|---|---|---|
| 攻擊 × 2 | 4+4 = 8 | 🔴 **主流回合，且剛好用完** —— 8 被 4 整除，餘 0 |
| 攻擊 + 移動 4 格（道路） | 4+4 = 8 | 移動與第二次攻擊互斥 |
| 攻擊 + 移動 1 格 + 防禦 | 4+1+3 = 8 | **恰好 8** |
| 純移動 | ≤ 4 格 | 受 MOVE 上限，而非 AP |
| **存 2 AP → 下回合 10 AP** | — | `min(2+8, 10)`。**這是唯一的跨回合槓桿** |

> 🔴 **攻擊 4 讓 8 AP 整除，餘 0** —— 實測顯示這使「零頭該花在哪」不存在，
> 見 [stage-log-2026-08-16-ap-economy](../06-validation/stage-log-2026-08-16-ap-economy.md)。
> **`prototype-charter §6.1` 的「攻擊佔 62%」風險已因 OD-01 消失，但換來的是整除性問題。**

---

## 3. 動作排序

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-ACT-01** | 玩家可自由決定移動與行動的順序，**且可交錯**（移動 → 攻擊 → 移動） | SPEC v0.1 §3.1 | `STABLE` | 存在一組合法指令串 `[Move, Attack, Move]` 全部被接受，只要 AP 足夠 |
| **R-ACT-02** | 敵方 AI 固定「先移動後行動」 | SPEC v0.1 §3.1 | `STABLE`（順序）／完整 AI 規則 `OPEN → OD-10` | 敵方單位的 Effect 序列中，`UnitMoved` 不出現在該單位的 `AttackResolved` 之後 |
| **R-ACT-03** | 沒有任何動作是強制的；單位可以什麼都不做就結束回合 | `DERIVED` ← R-TURN-05 | `DERIVED` | `EndTurnCommand` 在 AP 全滿時仍被接受 |

---

## 4. 勝敗條件

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-WIN-01** | 勝利：全滅敵人（Stage 01 為 4 隻小耗） | GDD Stage 01 四 ＋ SPEC v0.1 §3.5 | `STABLE` | 最後一隻敵方單位 `UnitDied` 之後立即產生 `BattleEnded(outcome = Victory)` |
| **R-WIN-02** | 失敗：桃太郎 HP 歸零 | GDD Stage 01 四 ＋ SPEC v0.1 §3.5 | `STABLE` | 桃太郎 `UnitDied` 之後立即產生 `BattleEnded(outcome = Defeat)` |
| **R-WIN-03** | 勝敗判定在**產生死亡的那個 Command 展開的 Effect 序列內**完成，不等到回合結束 | SPEC v0.1 §6.3（粒度原則） | `DERIVED` | `BattleEnded` 與造成它的 `UnitDied` 出現在同一個 `ExecuteResult` 的 log 中 |
| **R-WIN-04** | 沒有回合數上限、沒有平手 | GDD（未提及）／SPEC v0.1（未提及） | `DERIVED`（負面規格） | 不存在任何產生 `BattleEnded` 的第三種路徑 |
| **R-WIN-06** | 勝利條件由 encounter 資料的 `objective` 指定，**可以覆蓋 R-WIN-01**。`type=kill` 時，殺掉標記 `target=true` 的那隻敵人即勝利，其餘敵人不必死 | 專案負責人 2026-08-15 裁決 → [OD-30](../OPEN-DECISIONS.md#od-30) | `STABLE` | 標記單位 `UnitDied` 之後立即產生 `BattleEnded(Victory)`，且 `CountLiving(Enemy) > 0` |

> **R-WIN-04 是刻意寫下的「沒有」。** 自動化跑分若出現無限迴圈（雙方都不推進），
> 那是 AI 缺陷（OD-10），不是規則缺陷 —— 但跑分工具需要自己的 timeout。
> 見 [playtest-metrics.md](../06-validation/playtest-metrics.md)。
>
> R-WIN-06 的時限仍然屬於 objective 而非規則層，**R-WIN-04 不受影響**
> —— `kill` 和 `rout` 一樣預設無時限。

> ⚠️ **R-WIN-01 現在是「預設值」而不是「唯一值」。**
> 它仍然是任何沒有寫 `objective` 行的 encounter 的行為，而且
> 「清空戰場一律算贏」在所有目標下都成立。
> 但 GDD 為 Stage 01 明訂的全滅條件**已被授權覆蓋**（[OD-30](../OPEN-DECISIONS.md#od-30)），
> 所以 R-WIN-01 的 Source 欄「GDD Stage 01 四」只描述預設值的來源，
> 不再是一條不可違反的約束。`stage01.encounter` 本身未改動。

### 4.1 死亡語意

| ID | Statement | Source | Status |
|---|---|---|---|
| **R-WIN-05** | GDD 區分「昏厥」（人類 HP 歸零）與「消滅」（鬼族 HP 歸零直接移除） | GDD 狀態列表 3 | `CONFLICT → CONFLICT-08` |

Stage 01 雙方各一種陣營，行為上可能無差別。
`UnitDied` Effect 必須帶足夠資訊讓表現層日後分流，但 Stage 01 不實作兩套狀態機。

---

## 5. Command / Effect 契約

這是規則層與表現層之間的**唯一介面**。它是契約，所以在 Specification；
它的設計理由在 [ADR-0002](../07-adr/ADR-0002-single-command-funnel.md)。

### 5.1 契約規則

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-CMD-01** | 所有狀態變更都必須經過 `Execute(state, command)` 這個單一入口 | SPEC v0.1 §6.3 | `STABLE` | 不存在其他公開的狀態修改路徑 |
| **R-CMD-02** | `Execute` 是純函式：同輸入同輸出、無副作用、**不改動傳入的 state** | SPEC v0.1 §6.3 | `STABLE` | 架構測試 **A2** |
| **R-CMD-03** | `Execute` 回傳 `ExecuteResult { State, Log, Ok, RejectReason }` | SPEC v0.1 §6.3 | `STABLE` | 型別存在且欄位齊全 |
| **R-CMD-04** | 被拒絕的 Command 產生 `Ok = false` 與非空 `RejectReason`，且 `Log` 為空 | `DERIVED` ← R-AP-05 | `DERIVED` | 拒絕時 log 長度為 0 |
| **R-EFF-01** | 一個 Effect = 表現層可獨立播放的一個原子事件 | SPEC v0.1 §6.3 | `STABLE` | 每個 Effect 型別都能對應一段獨立演出 |
| **R-EFF-02** | 一個 Command 展開成有序的多個 Effect，**順序即因果順序** | SPEC v0.1 §6.3 | `STABLE` | 見 5.4 的展開範例 |
| **R-EFF-03** | 規則層在瞬間求值時就決定好完整 log；**表現層不做任何判斷，只按序播** | SPEC v0.1 §6.3、§6.9 | `STABLE` | 架構測試 **A7** |

### 5.2 Command 清單（Stage 01）

| Command | 參數 | Status |
|---|---|---|
| `MoveCommand` | unitId, path | `STABLE`（成本模型 `CONFLICT → CONFLICT-01`） |
| `AttackCommand` | attackerId, targetId | `STABLE`（傷害 `CONFLICT → CONFLICT-07`） |
| `DefendCommand` | unitId | `OPEN → OD-06`（效果未定義） |
| `UseItemCommand` | unitId, itemId, targetId | `OPEN → OD-09`（Stage 01 是否有道具未定） |
| `EndTurnCommand` | factionId | `STABLE` |

> `MoveCommand` 的參數是 **path（完整路徑）而非目標格**。
> 這是刻意的：路徑成本依賴經過哪些格，目標格不足以決定成本。
> 驗證階段必須檢查 path 的合法性（連續、可通行、成本足夠），
> 不能信任呼叫端 —— 見 [SPEC-movement.md](SPEC-movement.md) R-MOVE-06。

### 5.3 Effect 清單（Stage 01）

| Effect | 欄位 | Status |
|---|---|---|
| `ApSpent` | unitId, amount, remaining | `STABLE` |
| `UnitMoved` | unitId, from, to, path | `STABLE` |
| `AttackResolved` | attackerId, targetId, hit, roll | `STABLE`（`hit`/`roll` 的意義依賴 `OD-05`） |
| `HpChanged` | unitId, delta, newHp | `STABLE` |
| `UnitDied` | unitId | `STABLE`（語意 `CONFLICT → CONFLICT-08`） |
| `DefendApplied` | unitId, multiplier, expiresAtTurn | `OPEN → OD-06` |
| `FactionActivated` | factionId, triggeredByUnitId | `STABLE` |
| `TurnStarted` / `TurnEnded` | turnIndex, factionId | `STABLE` |
| `BattleEnded` | outcome | `STABLE` |

> **`AttackResolved` 有 `roll` 欄位。** 若 [OD-05](../OPEN-DECISIONS.md#od-05) 裁決為必中，
> 這個欄位在資料上無意義，但**不要移除** —— 兩個封包都要能跑，
> 移除它等於把封包 1 的路徑砍掉。

### 5.4 展開範例

`AttackCommand` 造成擊殺並結束戰鬥時：

```
[ ApSpent(5),
  AttackResolved(hit=true),
  HpChanged(-50),
  UnitDied,
  BattleEnded(Victory) ]
```

`MoveCommand` 觸發敵方陣營啟動時：

```
[ ApSpent(n),
  UnitMoved(from, to, path),
  FactionActivated(enemyFaction, triggeredByUnitId) ]
```

> `FactionActivated` 出現在 `UnitMoved` **之後**，因為啟動是移動的**結果**。
> 順序即因果。見 [SPEC-threat-activation.md](SPEC-threat-activation.md) R-THR-04。
