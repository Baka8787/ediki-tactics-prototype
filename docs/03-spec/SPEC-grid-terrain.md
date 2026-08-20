# SPEC — Grid & Terrain（座標、鄰接、地形、Exposure）

> ## ✅ PROTOTYPE BASELINE — 2026-08-13
>
> [OD-02](../OPEN-DECISIONS.md#od-02) 已裁決：**Blocking Terrain 存在**。
> 地形集合 `Open(1) / Road(1) / Forest(2) / Highland(2) / Blocking(impassable)`，資料驅動。
> [CONFLICT-02](../CONFLICTS.md#conflict-02) 已解決（採含隘口的地圖）。
> R-TERR-03/04 與 R-GRID-05/06/08 一律視為 `BASELINE`。
>
> 實作：`Assets/Scripts/Core/Terrain.cs`、`BattleMap.cs`、`ThreatAndExposure.cs`、
> `Assets/_Project/Resources/Data/terrain.txt`

| | |
|---|---|
| **Purpose** | 定義戰場的空間結構，以及 Exposure 的精確計算方式 |
| **Audience** | 程式（實作與測試） |
| **Source of Truth** | 本檔（**地圖資料本身**的 Source of Truth 是資料檔，見 [ODD-03](../DOCUMENT-MAP.md#odd-03)） |
| **Dependencies** | 無 |
| **Related** | [02-design/exposure.md](../02-design/exposure.md)（意圖）、[SPEC-movement.md](SPEC-movement.md)、[04-architecture/extension-points.md](../04-architecture/extension-points.md) |

---

## 1. 拓撲

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-GRID-01** | 格子拓撲透過 `IGridTopology` 介面存取。Stage 01 **只實作 `SquareGrid4`**（方格四鄰接）。**六角格不實作，只留介面** | SPEC v0.1 §6.4、§1.2 | `STABLE` | 存在且僅存在一個 `IGridTopology` 實作 |
| **R-GRID-02** | `Neighbors(c)` 的回傳順序在同輸入下必須固定 | SPEC v0.1 §6.4、§6.6 | `STABLE` | 架構測試 **A6** |
| **R-GRID-03** | 座標型別為 Core 自訂的 `Coord`（`int X, Y`），**不得使用 `Vector2Int`** | SPEC v0.1 §6.2 | `STABLE` | 架構測試 **A1**（Core 零 UnityEngine 引用）自然涵蓋 |
| **R-GRID-04** | 地圖為有限矩形，`Contains(c)` 判定格子是否在圖內 | SPEC v0.1 §6.4 | `STABLE` | 界外座標一律 `Contains == false`，且不得出現在 `Neighbors` 結果中 |

> **R-GRID-01 是一條「刻意不做」的規格。**
> 它存在的目的是**防止有人好心把六角格實作出來**。留介面是為了未來，不是為了現在。
> 見 [extension-points.md](../04-architecture/extension-points.md) 的 YAGNI 檢查。

---

## 2. 地形

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-TERR-01** | 每一格恰有一種地形類型 | `DERIVED` | `DERIVED` | 地圖資料中每格有且僅有一個 terrain 欄位 |
| **R-TERR-02** | 地形類型與其屬性由**資料**定義，程式碼不得列舉硬編碼的成本 | SPEC v0.1 §6.4 | `STABLE` | 架構測試 **A5** |
| **R-TERR-03** | 地形種類清單 | GDD 三.3 ／ SPEC v0.1 §3.2、§5.2 | `CONFLICT → CONFLICT-02` | 見下表 |
| **R-TERR-04** | 存在「阻擋（Blocked）」地形，不可進入 | SPEC v0.1 §5.4、§8.2 | `OPEN → OD-02` | GDD 沒有這個地形類型 |

### 2.1 地形清單（**未定案**）

| 地形 | GDD | SPEC v0.1 §5.2 地圖 | 移動成本 | 狀態 |
|---|---|---|---|---|
| 道路 | ✅ | ✅ `.` | 1 | `CONFLICT-01` |
| 森林 | ✅ | ✅ `f` | 2？ | `CONFLICT-01` |
| 碎石 | ✅ | ✅ `r` | 2？ | `CONFLICT-01` ＋ `CONFLICT-02`（GDD 說 Stage 01 沒有） |
| 高地 | ✅ | ✅ `^` | 3？ | `CONFLICT-01` ＋ `CONFLICT-02`（同上） |
| 阻擋 | ❌ **GDD 無此地形** | ✅ `#` | 不可進入 | `OPEN → OD-02` |

> ⚠️ **`道路1 / 碎石2 / 森林2 / 高地3` 這組成本沒有可查證的來源文件。**
> GDD 只寫「移動：1 AP」。詳見 [CONFLICT-01](../CONFLICTS.md#conflict-01)。

| ID | Statement | Source | Status |
|---|---|---|---|
| **R-TERR-05** | 地形是否影響戰鬥（命中／閃避／減傷） | SPEC v0.1 §5.5 | `OPEN → OD-08`（預設不做） |
| **R-TERR-06** | Stage 01 **不實作**圍攻加成（相鄰敵人數影響命中） | SPEC v0.1 §5.6 | `STABLE`（負面規格） |

---

## 2.1 即死地塊（Lethal Terrain）

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-TERR-07** | 地形可標記 `lethal=true`。**任何單位在移動或位移結束後停在該格上，立即死亡**（HP 歸零，產生 `UnitFellIntoHazard` ＋ `UnitDied`） | `DERIVED` ← [OD-34](../OPEN-DECISIONS.md#od-34) | `BASELINE`（**未裁決**） | `PushIntoLethalTerrain_KillsOutright` |
| **R-TERR-08** | 即死地塊是**可通行**的（`blocks=false`）。載入器拒絕 `blocks=true lethal=true` 的組合 | `DERIVED` ← [OD-34](../OPEN-DECISIONS.md#od-34) | `BASELINE` | `LethalTerrainIsStillPassableSoAPushCanReachIt`；`BlockingTerrainCannotAlsoBeLethal` |
| **R-TERR-09** | 🔴 **尋路不得將即死地塊列為可達目的地。** 單位永遠不會自願走進去；只有**非自願位移**（擊退）能把單位放進去 | `DERIVED` ← [OD-34](../OPEN-DECISIONS.md#od-34) | `BASELINE` | `NothingWalksIntoAHazardOnItsOwn` |
| **R-TERR-10** | 即死結算**不經過傷害公式**：不受 DEF、防禦（Guard）或任何減傷影響 | `DERIVED` ← [OD-34](../OPEN-DECISIONS.md#od-34) | `BASELINE` | `PushIntoLethalTerrain_IgnoresGuard` |

> 🔴 **R-TERR-09 是承重規則，不是最佳化。**
> `LegalCommands` 由尋路結果列舉，而跑分工具的 15% 雜訊是**均勻抽樣**。
> 若可達集合含即死格，雜訊會在任何有危害的地圖上隨機讓單位自殺，
> **該地圖上的所有量測都會失效**。這條規則存在的理由是量測有效性，不是玩家體驗。

> **R-TERR-08 的設計語意**：阻擋地形決定單位能**去**哪，即死地形決定單位能被**放**到哪。
> 進不去的危害只是一面牆 —— 所以位移（擊退）才是碰得到它的動詞。
>
> ⚠️ **判定點是「停下來的那一格」，不是路徑經過的每一格。**
> 規則層的移動是一次性換位（路徑僅用於計價），逐格判定會憑空引入一個
> 其他規則都沒有的分步模型。

---

## 3. Exposure

**這是本檔的核心。** Exposure 是可計算的量，不是形容詞。

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-GRID-05** | `Exposure(c)` = `c` 的相鄰格中，**可通行且敵方單位能站上去**的格子數量 | SPEC v0.1 §2.1 | `STABLE`（定義）／值 `OPEN → OD-02, OD-03` | 給定測試地圖，開闊地 = 4、靠牆 = 3、凹角 = 2、1 寬走廊 = 1 |
| **R-GRID-06** | 由於方格四鄰接且近戰射程 = 1，**`Exposure(c)` 等於同一回合最多有幾隻敵人能攻擊站在 `c` 的單位** | SPEC v0.1 §2.1 | `DERIVED` ← R-GRID-05 ＋ R-COMBAT-01 | 反證測試：無法構造出「Exposure = n 但 n+1 隻敵人同時攻擊到」的情況 |
| **R-GRID-07** | 系統必須能查詢**任意格**的 Exposure，不只是單位所在格 | SPEC v0.1 §2.3（UI 需求） | `STABLE` | 對地圖上每一格呼叫 `Exposure(c)` 都得到有效值 |

### 3.1 「可通行且敵人能站上去」的精確定義

R-GRID-05 的「可通行且敵方單位能站上去」目前**無法精確化**，因為它依賴：

| 依賴 | 影響 |
|---|---|
| [OD-02](../OPEN-DECISIONS.md#od-02) 阻擋地形 | 沒有阻擋地形 → Exposure 恆為 4（地圖邊緣除外） |
| [OD-03](../OPEN-DECISIONS.md#od-03) 單位阻擋 | 若單位不阻擋，被己方單位佔據的格仍算 Exposure |

**裁決後必須回來把這一節寫成一個明確的謂詞**，例如：

```
CanEnemyOccupy(c) := Contains(c)
                  && Terrain(c) != Blocked
                  && (單位阻擋規則的判定)
```

> **在此之前不要實作 Exposure 計算。**
> 這不是「先寫個版本之後再改」的東西 —— 它是主假說的量測工具，
> 算錯會讓整個 Prototype 的數據無效。

### 3.2 有效 Exposure vs 靜態 Exposure

SPEC v0.1 §5.3 提到「**有效** Exposure = 1」，措辭與 §2.1 的靜態定義不同。

| 概念 | 定義 | 用途 |
|---|---|---|
| **靜態 Exposure** | 該格的可通行相鄰格數（R-GRID-05） | 地圖幾何的固有性質，與敵人位置無關 |
| **有效 Exposure** | 考慮敵人**實際能否到達**那些相鄰格之後的數量 | 玩家真正關心的東西 |

**兩者不同。** 例如黃金格 (5,5) 的相鄰格中有些被牆佔據，靜態 Exposure 已經是 1；
但若某格雖然可通行、敵人這回合卻到不了，有效 Exposure 會更低。

| ID | Statement | Status |
|---|---|---|
| **R-GRID-08** | 系統必須同時能查詢靜態 Exposure 與有效 Exposure，兩者是不同的查詢 | `STABLE`（結構）／有效值依賴 [SPEC-threat-activation](SPEC-threat-activation.md) |

> **UI 顯示哪一個？** SPEC v0.1 §2.3 要求顯示「該格 Exposure」與
> 「目前有幾隻活著的敵人的威脅範圍涵蓋這格」—— 後者其實就是有效 Exposure 的另一種說法。
> **兩個都要顯示。** 呈現形式見 [OD-14](../OPEN-DECISIONS.md#od-14)。

---

## 4. 地圖資料

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-MAP-01** | 地圖（尺寸、每格地形、單位起始位置）全部來自資料，**不得寫死在場景或程式碼中** | SPEC v0.1 §6.4 | `STABLE` | 換一張地圖不需要重新編譯 |
| **R-MAP-02** | 地圖必須完全連通：不存在玩家永遠無法接觸的敵方單位 | [02-design/stage-01.md](../02-design/stage-01.md) P5 | `STABLE` | 對每張地圖資料執行連通性測試 |
| **R-MAP-03** | Stage 01 的地圖內容 | — | `CONFLICT → CONFLICT-02` | 兩種定位互斥，**裁決前不建立正式地圖資料** |

> **R-MAP-02 是硬性正確性條件**，適用於任何裁決結果。
> 它應該在地圖資料被載入時就驗證，而不是等到跑分跑出無限迴圈才發現。
