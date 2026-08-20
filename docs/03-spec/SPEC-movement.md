# SPEC — Movement（可達性、路徑、成本）

> ## ✅ PROTOTYPE BASELINE — 2026-08-13
>
> [OD-03](../OPEN-DECISIONS.md#od-03)：**單位佔格、互相阻擋**（敵我皆同；不做 push/pull/phasing）。
> [OD-04](../OPEN-DECISIONS.md#od-04)：**採地形成本**，4-directional。[CONFLICT-01](../CONFLICTS.md#conflict-01) 已解決。
> R-MOVE-01/03/04/05/07/12 一律視為 `BASELINE`。
>
> ✅ **`MOVE` 已生效**（2026-08-13 追加裁決）：
> `MOVE = 單次 Move Action 可移動的最大 Grid Cell 數`（桃太郎 4、小耗 3）。
> 可達性同時受 **AP ／ 地形成本 ／ 單位佔格 ／ MOVE maxSteps** 四者限制。
> R-MOVE-04 因此為 `BASELINE`。
>
> ⚠️ **MOVE 只限制單次動作，可以串接多次 Move Action 繞過** → [OD-17](../OPEN-DECISIONS.md#od-17)。
> 這代表 **Q5 的驗收條件仍未完全成立**。
>
> ⚠️ **R-MOVE-09（AI 用 A*）未實作** —— 120 格的網格上 flood fill 已足夠，
> 第二套尋路只會與 R-THR-02 要求的「共用同一套可達性」互相牴觸。這是刻意的簡化。
>
> 實作：`Assets/Scripts/Core/Movement.cs`

| | |
|---|---|
| **Purpose** | 定義單位如何移動、移動花多少、哪裡去得了 |
| **Audience** | 程式（實作與測試） |
| **Source of Truth** | 本檔 |
| **Dependencies** | [SPEC-grid-terrain.md](SPEC-grid-terrain.md)、[SPEC-battle-flow.md](SPEC-battle-flow.md) |
| **Related** | [SPEC-threat-activation.md](SPEC-threat-activation.md)（威脅範圍用同一套可達性） |

> 🔴 **本檔是全套規格中被阻擋最嚴重的一份。**
> 核心的成本模型（[CONFLICT-01](../CONFLICTS.md#conflict-01)）與單位阻擋
> （[OD-03](../OPEN-DECISIONS.md#od-03)）都未裁決。
> **可以實作的是「結構」，不能實作的是「規則本身」。**

---

## 1. 成本模型

| ID | Statement | Source | Status |
|---|---|---|---|
| **R-MOVE-01** | 移動的 AP 成本模型 | GDD 三.1 ／ SPEC v0.1 §3.2 | `CONFLICT → CONFLICT-01` |
| **R-MOVE-02** | 成本一律透過可替換的成本模型查詢，**程式碼不得出現任何成本字面值** | SPEC v0.1 §6.4 | `STABLE` | 架構測試 **A5** |

### 1.1 兩種互斥的讀法

| 讀法 | 內容 | 來源 |
|---|---|---|
| **平坦成本** | 進入任一格固定 1 AP；`MOVE` 是格數上限 | **GDD**（三.1「移動：1 AP」、Stage 01「消耗 1 AP / 格」） |
| **地形成本** | 進入一格消耗該格的地形成本；`MOVE` 是格數上限；兩個限制同時生效取較嚴 | **SPEC v0.1 §3.2 讀法 (A)**（自行採用，未經企劃拍板） |

> ⚠️ SPEC v0.1 §3.2 所引用的「原始規格」地形成本表在封存的 GDD 中**不存在**。
> 這不只是「兩份文件不同」，而是**其中一方沒有來源**。

### 1.2 兩種讀法的實際差異

若採地形成本讀法，SPEC v0.1 §3.2 的推導：

| 情境 | 桃太郎（MOVE 4） | 小耗（MOVE 3） |
|---|---|---|
| 攻擊回合（剩 3 AP）走道路 | 3 格 | 3 格 |
| 攻擊回合走碎石／森林 | 1 格（浪費 1 AP） | 1 格 |
| 攻擊回合走高地 | 1 格 | 1 格 |
| 純移動回合走道路 | 4 格（MOVE 上限，浪費 4 AP） | 3 格（MOVE 上限） |
| 純移動回合走森林 | 4 格（8 AP 剛好） | 3 格（6 AP） |
| 純移動回合走高地 | 2 格（6 AP） | 2 格（6 AP） |

兩個必須讓企劃知道的後果：

1. **桃太郎的 MOVE 4 在任何攻擊回合都用不到**（5 + 4 = 9 > 8）。
   攻擊回合他和小耗一樣只能走 3 格，**他無法風箏**。→ 驗收問題 **Q5**
2. **任何非道路地形都把攻擊回合的移動壓成 1 格**，雙方一樣。
   地形只會「拖慢」，不會製造雙方的不對稱。

若採平坦成本讀法，上表全部不適用，且[02-design/battle-experience.md](../02-design/battle-experience.md)
的「一格森林把小耗威脅圈從 4 砍到 2」也不成立。

---

## 2. MOVE 上限

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-MOVE-03** | 每個單位有 `MOVE` 屬性，來自資料 | GDD（桃太郎 MOVE 4、小耗 MOVE 3） | `STABLE` | 單位資料含 `move` 欄位 |
| **R-MOVE-04** | `MOVE` 的語意（格數上限／AP 上限／兩者取嚴） | SPEC v0.1 §3.2 | `OPEN → OD-04` | 三種語意行為不同，必須明確擇一 |
| **R-MOVE-05** | `MOVE` 的計量在該單位的一個回合內累計（不是單次 `MoveCommand`） | `DERIVED` ← R-ACT-01（可交錯移動） | `DERIVED` | 兩次 `MoveCommand` 各走 2 格，第二次在 MOVE=3 時必須被部分拒絕 |

> **R-MOVE-05 很容易被忽略。** 因為 R-ACT-01 允許「移動 → 攻擊 → 移動」，
> MOVE 上限必須是**回合累計**，不能每次 `MoveCommand` 各自重算，否則玩家可以
> 用多次 Command 繞過上限。這條在文件中沒有明說，是從 R-ACT-01 推導出來的，
> **建議請企劃確認**。

---

## 3. 可達性與路徑

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-MOVE-06** | `MoveCommand` 帶完整 path；驗證階段必須檢查路徑合法性，**不得信任呼叫端** | SPEC v0.1 §6.3 | `STABLE` | 送入非連續／穿牆／成本超支的 path，`Execute` 回傳 `Ok=false` 且 state 不變 |
| **R-MOVE-07** | 路徑合法 = 起點為單位當前位置 ∧ 每一步為相鄰格 ∧ 每一格可進入 ∧ 總成本 ≤ 剩餘 AP ∧ 滿足 MOVE 上限 ∧ 終點未被佔據 | `DERIVED` | `DERIVED`（各項依賴 OD-03, OD-04, CONFLICT-01） | 每個條件各一條反例測試 |
| **R-MOVE-08** | 移動範圍（玩家可走到哪）用 **Dijkstra flood fill** 計算 | SPEC v0.1 §6.5 | `STABLE` | 需要「所有可達格 + 成本」，目標未知 |
| **R-MOVE-09** | AI 點對點路徑用 **A\*** | SPEC v0.1 §6.5 | `STABLE` | 目標已知，有啟發函數 |
| **R-MOVE-10** | 地形成本進 edge weight，**不寫死在拓撲裡** | SPEC v0.1 §6.5 | `STABLE` | 更換成本模型不需要改 `IGridTopology` 實作 |
| **R-MOVE-11** | Dijkstra 的展開順序必須確定（同輸入同輸出，含 tie-break） | SPEC v0.1 §6.5、§6.6 | `STABLE` | 架構測試 **A4**；同一格集合的走訪順序在重複執行間一致 |

> **R-MOVE-11 是確定性的實際落腳點之一。**
> 相同成本的格子在優先佇列中的先後順序若不確定，
> 「所有可達格」的集合相同但**路徑**可能不同 → 影響 `UnitMoved.path` → 影響狀態雜湊。
> Tie-break 必須明確定義（例如按 `Coord` 的字典序），不能依賴容器的預設行為。

---

## 4. 單位阻擋

| ID | Statement | Source | Status |
|---|---|---|---|
| **R-MOVE-12** | 單位是否阻擋移動 | SPEC v0.1 §8.2 | `OPEN → OD-03` |
| **R-MOVE-13** | 兩個單位不得同時佔據同一格 | `DERIVED`（所有戰棋的基本前提；GDD 與 SPEC 皆未明寫） | `DERIVED` | 任何導致重疊的指令必須被拒絕 |

> **R-MOVE-13 沒有書面來源。** 它是從「Exposure = 相鄰格數 = 最多幾隻敵人能打到你」
> 推導出來的必要前提（若可重疊，Exposure 的推導立刻失效）。
> 標為 `DERIVED` 而非 `STABLE`，是為了提醒它是推論而非引用。

> ⚠️ [OD-03](../OPEN-DECISIONS.md#od-03) 未裁決之前，
> **「走廊」這個概念不成立** —— SPEC v0.1 §5.4 直言「沒有這條，走廊擋不住任何東西」。
> 這代表 Stage 01 地圖設計與 Exposure 的核心命題**都卡在這一項**。
