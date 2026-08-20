# SPEC — AI Behaviour

| | |
|---|---|
| **Purpose** | 定義敵方 AI 怎麼決定要做什麼 |
| **Audience** | 程式（實作與測試）、企劃（調 AI Profile） |
| **Source of Truth** | 本檔（源自 [OD-10](../OPEN-DECISIONS.md#od-10) 的裁決） |
| **Dependencies** | [SPEC-movement.md](SPEC-movement.md)、[SPEC-threat-activation.md](SPEC-threat-activation.md)、[SPEC-combat.md](SPEC-combat.md) |
| **Related** | `Assets/_Project/Resources/Data/ai-profiles.txt` |

> 本檔在 2026-08-13 建立，關閉了 [ODD-01](../DOCUMENT-MAP.md#odd-01)（AI 缺 Source of Truth）。

---

## 1. 四個責任

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-AI-01** | AI 必須支援 Perception / Target Selection / Position Selection / Action Selection | OD-10 | `BASELINE` | 四者各有對應測試 |
| **R-AI-02** | AI 只能透過 `BattleSimulator.Execute` 改變狀態，**不得有第二套規則實作** | [R-CMD-01](SPEC-battle-flow.md) | `BASELINE` | `AiNeverBypassesTheSimulator_AndNeverProducesIllegalMoves` |
| **R-AI-03** | AI 的行為差異必須來自**資料**（AI Profile），不得寫死在 per-enemy 的 class | OD-10 | `BASELINE` | `AiProfile_IsDataDriven_NotHardcodedPerUnitType` |
| **R-AI-04** | AI 決策必須是確定性的：同一個 state 永遠產生同一個 Command | [ADR-0003](../07-adr/ADR-0003-deterministic-rule-layer.md) | `BASELINE` | `EnemyPhase_IsDeterministic` |

> **R-AI-03 的實作後果**：`EnemyAi` 是 `sealed`，沒有 virtual method，不能靠繼承客製。
> 想讓某種敵人行為不同 → 加一個 AI Profile，不是加一個 class。

---

## 2. Perception

| ID | Statement | Status | Acceptance |
|---|---|---|---|
| **R-AI-05** | **沒有合法可攻擊目標的單位不得永久 Idle**：選定玩家為接敵目標並朝其移動；無法移動時才走既有 Guard / Wait | `BASELINE`（[OD-16](../OPEN-DECISIONS.md#od-16)） | `UnengagedUnit_ClosesInInsteadOfIdlingForever`、`NoEnemyRemainsIdleAcrossAWholeBattle` |
| **R-AI-05b** | `IsActivated` 是**純觀測閂鎖**：記錄「是否曾感知到玩家」並驅動 HUD，**不控制行為** | `BASELINE`（OD-16） | `ActivationLatch_IsObservabilityOnly_AndNoLongerGatesBehaviour` |
| **R-AI-06** | 玩家單位站進某敵人的威脅範圍時，**該敵人**啟動並發出 `UnitActivated` | `BASELINE` | `Perception_LatchesOnceThePlayerStepsIntoRange` |
| **R-AI-07** | 啟動是**閂鎖（latch）**：一旦啟動不會再變回未啟動 | `BASELINE` | `Perception_StaysLatchedAfterThePlayerBacksOff` |

> **R-AI-05 刻意取代了 [R-THR-03](SPEC-threat-activation.md)**（「敵人在玩家進入其威脅範圍前不啟動」）。
> OD-16 裁定不允許永久 Idle，因為那會造成永久僵局，讓 M3/M5/Q3 無法量測。
>
> **OD-16 明確排除的東西**：回合數上限（作為 Gameplay Rule）、
> Last Known Position、Fog of War、Patrol、Search State、AI Memory。

> **R-AI-06 解決了 [R-THR-06](SPEC-threat-activation.md) 的矛盾。**
> SPEC v0.1 §6.3 用的是陣營層級的 `FactionActivated`，那會讓「踩到一隻等於拉全部」，
> 與「一次只拉一隻」的設計意圖衝突。實作改成**逐單位** `UnitActivated`。

---

## 3. Target Selection

| ID | Statement | Status |
|---|---|---|
| **R-AI-08** | 目標從 `AiProfile.TargetPreference` 決定：`nearest` / `lowestHp` / `lowestDef` | `BASELINE` |
| **R-AI-09** | 同分時取 unit id 較小者（列舉順序即 id 順序） | `BASELINE`（確定性戒二） |

---

## 4. Position Selection

| ID | Statement | Status |
|---|---|---|
| **R-AI-10** | 對每個可達格算分，取最高分；同分取先出現者（`ReachableCells` 已排序） | `BASELINE` |
| **R-AI-11** | 評分公式（utility-lite） | `BASELINE` |

```
score  = -|distance(cell, target) - PreferredDistance| * 100     // 想站在偏好距離
score -= StaticExposure(cell) * (100 - Aggression) / 10          // 越怕死越避開空曠
score -= moveCost(cell)                                          // 同分時走近的
```

撤退時（HP% ≤ `RetreatHpPercent`）第一項改成 `+distance * 100`（越遠越好）。

> **這是刻意的「夠用就好」**，不是完整 utility system。
> 三個項、整數運算、沒有 consideration 曲線、沒有 blackboard。
> 要調整行為請先改資料；只有在資料調不出想要的行為時才改公式。

> 🔴 **已知缺陷：`distance` 是曼哈頓距離，看不見牆。**
> 在有隘口的地圖上，雙方可能各自貼到牆的兩側、誰都不走隘口 → **死鎖**。
> 200 場跑分中有 6.5% 的場次因此跑不完。
> 詳見 [OD-18](../OPEN-DECISIONS.md#od-18)。**規則未修改，等待裁決。**

---

## 5. Action Selection

| ID | Statement | Status |
|---|---|---|
| **R-AI-12** | 決策順序：能攻擊就攻擊 → 否則移動到最佳位置 → 移動後能攻擊就攻擊 → 否則視情況 Guard → 否則 Wait | `BASELINE` |
| **R-AI-13** | HP% ≤ `GuardHpPercent` 且無法攻擊且 AP 足夠時，發出 `GuardCommand` | `BASELINE` |
| **R-AI-14** | 一個單位的活動在它回傳 `WaitCommand` 或無合法動作時結束 | `BASELINE` |
| **R-AI-15** | 執行器對每個單位設有指令數上限（16），避免 AI 缺陷造成無限迴圈 | `BASELINE` | 

> **R-AI-12 與 [R-ACT-02](SPEC-battle-flow.md)（「敵方 AI 固定先移動後行動」）一致**，
> 但更精確：AI 可以「已經在射程內 → 直接攻擊」而不先移動。

---

## 6. AI Profile schema

檔案：`Assets/_Project/Resources/Data/ai-profiles.txt`

| 欄位 | 型別 | 意義 | 預設 |
|---|---|---|---|
| `id` | string | 識別 | 必填 |
| `target` | `nearest` / `lowestHp` / `lowestDef` | 目標偏好 | `nearest` |
| `distance` | int | 想維持的距離（1 = 近戰） | 1 |
| `aggression` | int 0–100 | 越高越不在乎自身暴露 | 70 |
| `retreatHp` | int 0–100 | 低於此 HP% 改為拉開距離；0 = 不撤退 | 0 |
| `guardHp` | int 0–100 | 低於此 HP% 且無法攻擊時 Guard；0 = 不 Guard | 0 |

| ID | Statement | Status |
|---|---|---|
| **R-AI-16** | 敵人透過 encounter 的 `spawn ... ai=<id>` 指定 profile | `BASELINE` |
| **R-AI-17** | 未知或未指定的 profile 退回 `AiProfile.Default`，**不得拋例外** | `BASELINE` |

---

## 7. 目前**沒有**做的

| 不做 | 為什麼 |
|---|---|
| Behaviour Tree / Editor | OD-10 明確排除 |
| MCTS / 多步推演 | 沒有需求；`Clone()` 已備妥，未來要做不需改架構 |
| 隊形／群組協調（`group=` 欄位目前只是標籤） | 沒有需求 |
| 技能選擇 | Stage 01 沒有技能 |
| 對玩家 Guard 狀態的反應 | 沒有需求 |

---

## 8. 已知的行為後果

| 現象 | 說明 |
|---|---|
| ~~永久僵局（未啟動的敵人永遠不動）~~ | ✅ 已由 [OD-16](../OPEN-DECISIONS.md#od-16) 解決 |
| **殘留死鎖（6.5%）** | 曼哈頓距離看不見牆，雙方在隘口兩側各自卡住 → [OD-18](../OPEN-DECISIONS.md#od-18) |
| 隊列 | 走廊只容一隻，其餘敵人會在後方排隊 Wait。這是 occupancy（OD-03）的正確結果 |
| MOVE 可被串接繞過 | 見 [OD-17](../OPEN-DECISIONS.md#od-17) |
