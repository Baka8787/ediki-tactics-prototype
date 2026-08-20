# SPEC — Threat Range & Activation（威脅範圍、敵人啟動）

> ## ✅ PROTOTYPE BASELINE — 2026-08-13
>
> 威脅範圍與啟動已實作，計算與實際移動共用同一套 flood fill（R-THR-02）。
>
> 🔴 **R-THR-06 已解決**：啟動改為**逐單位** `UnitActivated`，不是陣營層級 `FactionActivated`。
> 理由與規格見 [SPEC-ai-behaviour.md](SPEC-ai-behaviour.md) R-AI-06。
>
> **實作時的修正**：威脅範圍的射程展開**只計入可通行格**。牆不是可被攻擊的位置，
> 把牆塗紅只是雜訊。
>
> 🔴 **R-THR-03 已被 [OD-16](../OPEN-DECISIONS.md#od-16) 取代。**
> 「敵人在玩家進入其威脅範圍前不啟動」造成永久僵局，
> 裁決改為「沒有可攻擊目標的 AI 主動接敵」。
> 啟動閂鎖 `IsActivated` 保留，但降級為**純觀測用途，不控制行為**
> → [SPEC-ai-behaviour R-AI-05](SPEC-ai-behaviour.md)。
>
> ⚠️ **威脅範圍現在也受 MOVE 限制**（一次移動 ＋ 一次攻擊）。
> 因為移動可以串接，**危險區會低報** → [OD-17](../OPEN-DECISIONS.md#od-17)。
>
> ⚠️ **殘留死鎖**：OD-16 之後仍有 6.5% 場次跑不完，原因是位置評分用曼哈頓距離
> → [OD-18](../OPEN-DECISIONS.md#od-18)。
>
> 實作：`Assets/Scripts/Core/ThreatAndExposure.cs`、`BattleSimulator.CheckActivations`

| | |
|---|---|
| **Purpose** | 定義威脅範圍怎麼算、敵人什麼時候啟動、玩家看得到什麼 |
| **Audience** | 程式（實作與測試）、企劃（確認可見性需求） |
| **Source of Truth** | 本檔 |
| **Dependencies** | [SPEC-movement.md](SPEC-movement.md)、[SPEC-combat.md](SPEC-combat.md)、[SPEC-grid-terrain.md](SPEC-grid-terrain.md) |
| **Related** | [02-design/exposure.md](../02-design/exposure.md)、[02-design/battle-experience.md](../02-design/battle-experience.md) |

---

## 1. 威脅範圍

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-THR-01** | **威脅範圍** = 敵人能在同一回合內移動到並攻擊的所有格子<br>= 「路徑成本 ≤ (AP上限 − 攻擊成本) 且滿足 MOVE 上限的可達格」再向外擴 **射程** 格 | SPEC v0.1 §3.3 | `STABLE`（定義）／值依賴 `CONFLICT-01`、`OD-01`、`OD-04` | 給定測試地圖與單位，逐格比對預期集合 |
| **R-THR-02** | 威脅範圍的計算必須與實際移動使用**同一套可達性演算法**（Dijkstra flood fill） | SPEC v0.1 §6.5 | `STABLE` | 不存在第二份可達性實作；威脅範圍與移動範圍的可達格集合在同參數下一致 |

### 1.1 推導範例（依賴未決事項）

小耗攻擊後剩 3 AP（8 − 5），採**地形成本**讀法時：

| 路徑地形 | 小耗威脅範圍 |
|---|---|
| 全道路 | **4**（移 3 + 射程 1） |
| 經過一格森林／碎石 | **2** |
| 經過高地 | **2** |

> **一格森林等於把小耗的威脅圈從 4 砍到 2。**
> 這是地形在 Stage 01 的真正價值：讓玩家一次只拉一隻。
>
> ⚠️ **若 [CONFLICT-01](../CONFLICTS.md#conflict-01) 裁決為平坦成本（固定 1 AP/格），
> 這張表全部失效**，所有地形的威脅圈都是 4。

---

## 2. 啟動

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-THR-03** | 敵人在玩家進入其威脅範圍前**不啟動** | SPEC v0.1 §3.3 | `STABLE` | 玩家停在威脅範圍外時，敵方單位不移動、不攻擊 |
| **R-THR-04** | 啟動由玩家的位置變化觸發，並產生 `FactionActivated(factionId, triggeredByUnitId)` Effect | SPEC v0.1 §6.3 | `STABLE` | Effect 出現在觸發它的 `UnitMoved` 之後（因果順序） |
| **R-THR-06** | 啟動的粒度（單隻／整組／整個陣營） | SPEC v0.1 §6.3 用 `FactionActivated`（陣營層級） | `DERIVED` | Effect 名稱暗示**整個陣營一起啟動**。Stage 01 有 4 隻分散的小耗，陣營層級啟動代表**踩到一隻等於拉全部**，與「一次只拉一隻」的設計意圖矛盾 |

> 🔴 **R-THR-06 是本輪發現的規格矛盾。**
>
> - [02-design/battle-experience.md](../02-design/battle-experience.md) 與
>   SPEC v0.1 §3.3 的設計意圖是**「讓玩家一次只拉一隻」**
> - 但 SPEC v0.1 §6.3 的 Effect 是 `FactionActivated`（**陣營**層級）
>
> 兩者不相容：如果啟動是陣營層級，地形削減威脅圈就沒有意義，
> 因為踩到任何一隻的威脅範圍都會拉起全部 4 隻。
>
> **這需要企劃裁決**，且它會改變 Effect 的設計。
> 已登錄為 [OD-10](../OPEN-DECISIONS.md#od-10) 的一部分（AI 行為規格）。
> **在裁決前不要實作啟動邏輯。**

| ID | Statement | Source | Status |
|---|---|---|---|
| **R-THR-07** | 未啟動的敵人在其陣營回合做什麼 | — | `OPEN → OD-10` |
| **R-THR-08** | 已啟動的敵人是否會再度「脫離戰鬥」 | — | `OPEN → OD-10`（文件從未提及，推測為否） |

---

## 3. 可見性（UI 需求）

**這一節是需求規格，不是 UI 設計規格。**

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-THR-05** | 威脅範圍必須對玩家可見，且能**一鍵顯示全體危險區** | SPEC v0.1 §3.3 | `STABLE`（需求）／形式 `OPEN → OD-14` | 存在一個操作能同時顯示所有存活敵人的威脅範圍聯集 |
| **R-THR-09** | 滑鼠停在任何一格時，必須顯示該格的 **Exposure** | SPEC v0.1 §2.3 | `STABLE`（需求）／形式 `OPEN → OD-14` | 對地圖上任一格 hover 都能取得數值 |
| **R-THR-10** | 滑鼠停在任何一格時，必須顯示**目前有幾隻活著的敵人的威脅範圍涵蓋這格** | SPEC v0.1 §2.3 | `STABLE`（需求）／形式 `OPEN → OD-14` | 死亡敵人不計入；數值隨戰況即時更新 |

> **R-THR-09 與 R-THR-10 是兩個不同的量**，見
> [SPEC-grid-terrain.md §3.2](SPEC-grid-terrain.md)（靜態 Exposure vs 有效 Exposure）。
> **兩個都要顯示**，不能只顯示一個。

### 3.1 查詢服務的架構要求

以上三條都是**查詢**（read-only），不是狀態變更。

| ID | Statement | Status |
|---|---|---|
| **R-THR-11** | 威脅範圍與 Exposure 的查詢必須是規則層提供的純函式查詢，**不修改 state** | `STABLE` |
| **R-THR-12** | 表現層透過查詢服務取得這些值，**不得自行重算** | `STABLE` |

> **R-THR-12 是 A7 的一個例外情境，需要小心。**
> [test-strategy A7](../06-validation/test-strategy.md) 說「表現層組件不得反向引用 Core 的可變狀態（只能讀 EffectLog）」。
> 但 UI 要顯示 hover 格的 Exposure，這**無法只靠 EffectLog** ——
> Effect 是事件流，不是空間查詢介面。
>
> 解法：規則層額外提供一組**唯讀查詢介面**，表現層可以呼叫，
> 但拿到的是值（value）而不是可變狀態的參考。
> 見 [04-architecture/overview.md](../04-architecture/overview.md) 的「兩條線」一節。
> **這是本輪發現的架構細節，SPEC v0.1 §6.9 沒有涵蓋。**

---

## 4. 已知風險

| 風險 | 內容 | 處理 |
|---|---|---|
| 逐一釣怪 | 「進入威脅範圍才啟動」的著名失敗模式是「逐一釣怪變成唯一最佳解」（XCOM pod 的長年批評，RESEARCH §2） | **Stage 01 作為教學關，逐一釣怪就是我們要教的東西**，本關不處理。記在後續關卡的風險清單上，用時間壓力或目標設計去破 |

這不是待決事項，是**已接受的取捨**。不要在 Stage 01 加時間壓力來「修正」它。
