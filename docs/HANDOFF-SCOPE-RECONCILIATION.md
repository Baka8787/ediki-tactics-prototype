# 交接：Scope Reconciliation（Prototype / Stage 01 / Gym / GDD）

**建立於 2026-08-16。本輪是 READ-ONLY 稽核，沒有修改任何既有檔案、程式或 OD。**
本檔的用途是讓下一個會話拿到一張可信的 Scope Map，並知道**專案負責人還沒裁決什麼**。

> ⚠️ **本檔不是裁決。** 所有「建議」都要等專案負責人拍板。
> 沿用 [HANDOFF-NEXT-SESSION §0.0](HANDOFF-NEXT-SESSION.md) 的資訊分級：
> `[A]` repo 已確認　`[B]` 提出未核對　`[C]` 推論／假說

---

## 0. 三十秒版本

`[A]` **Charter §4 的非目標表宣稱有否決權，但其中四列已經被實作、被測試、而且被 OD 解除過。**
`[A]` **`stage01.encounter.txt` 本身完全乾淨**：單人桃太郎、4 隻同質小耗、rout、無技能、無污染。
`[A]` **真正的問題不在 Stage 01，而在 Charter 用「Stage 01 交付內容」推導出「整個 Prototype 的研究禁區」。**

具體那一段是 [prototype-charter.md 第 90–98 行](01-vision/prototype-charter.md)：

| 文件原文 | 它實際做的事 |
|---|---|
| 「正守／玄真／影丸／仙子 —— **Stage 01 只有桃太郎與小耗**」 | 用交付內容禁掉整個 Prototype 的角色研究 |
| 「多單位隊伍管理 —— **玩家方只有一個單位**」 | 同上 |
| 「GDD 的狀態列表 —— **Stage 01 沒有任何東西會施加狀態**」 | 同上 |

**這正是專案負責人懷疑的那個反轉，而且有明確出處。**

---

## 1. 現況 Scope Map

### 1.1 五個 scope 目前各自的定義

| Scope | 定義在哪 | 實際內容 | 狀態 |
|---|---|---|---|
| **Prototype** | [charter §2](01-vision/prototype-charter.md) | 只靠底層機制（移動／攻擊／防禦／休息／AP／地形／佔格）證明有意義的決策 | `[A]` **已被 OD-30／OD-31 部分繞過，charter 用警告框標記，未改寫** |
| **Stage 01 delivery** | `stage01.encounter.txt` | 單人桃太郎 ＋ 4× kohaku（同質）＋ rout ＋ 無技能 ＋ 無污染 | `[A]` **乾淨，一個字都沒被改過** |
| **Stage 01 acceptance** | [playtest-metrics Q1–Q5](06-validation/playtest-metrics.md) ＋ [test-strategy A1–A7](06-validation/test-strategy.md) | Q1–Q5 五個驗收問題；A1–A7 架構測試 | `[A]` Q2／Q4 需真人，**一次都沒做過** |
| **Gym experiment** | **沒有任何治理文件定義** | 75 張 `gym-*.encounter` | `[A]` 只有 [experiment-playbook §5.4](05-development/experiment-playbook.md) 說「可以整批刪掉」 |
| **GDD** | `00-source/` | 全遊戲 | — |

### 1.2 實作現況（repository 事實，不是文件宣稱）

`[A]`

| 項目 | 數量 | 位置 |
|---|---|---|
| `units.txt` 單位總數 | **61** | 其中**只有 2 個**（`momotaro`、`kohaku`）屬於 Stage 01 交付 |
| encounter 總數 | **77** | `stage01*` **2** 張、`gym-*` **75** 張 |
| 內容層動詞 | 4 | `TauntCommand` / `SlowCommand` / `PushCommand` / `PurifyCommand`，**全部在 `Ediki.Core`** |
| 污染系統 | — | `BattleState`（6 處）＋ `BattleSimulator`（12 處），**在 `Ediki.Core`** |
| EditMode 測試 | **317** | 其中 `ControlSkillTests` 16 個專測技能 |

`[A]` **四個 GDD 角色的實作狀況**：

| 角色 | 存在？ | 被誰用 |
|---|---|---|
| 正守 | ✅ `zhengshou` ＋ 7 個變體 | `ControlSkillTests`、`DataTests`（**GDD 數值鎖定測試**）、`gym-opening*`、`gym-ctr-*`、E4 |
| 玄真 | ✅ `genjin` ＋ 2 個變體 | `gym-opening*`、E1 |
| 影丸 | ✅ `kagemaru` ＋ 8 個變體 | `gym-opening*`、E1／E3 |
| 晦氣／淨化 | ✅ `huiqi`、`momotaro_pure` | `SimulationTests` 污染測試、`gym-contam*` |

---

## 2. 發現的衝突（逐項）

### 2.1 🔴 Charter §4 宣稱否決權，但四列已被實作

`[A]` [charter §4](01-vision/prototype-charter.md) 原文：
> 「**這張表有否決權** —— 任何人（包含 Claude Code）提議做這裡的東西，預設答案是「不做」。」

| 非目標原文 | repo 現況 | 解除依據 |
|---|---|---|
| **污染系統** | 已實作在 Core，有測試 | ❌ **沒有任何 OD 解除過它**。OD-29 只裁決「強度與定價」，**預設了它存在** |
| 正守／玄真／影丸／仙子 | 三個已實作 | ✅ OD-31（作為驗證工具） |
| 多單位隊伍管理 | 四人隊已實作 | ✅ OD-31（上限 4 人） |
| 桃太郎的四個技能 | 淨化已實作 | ❌ 沒有 OD 解除 |

> `[A]` **污染是最嚴重的一列**：charter §4 明寫「污染是全遊戲的差異化核心，但**不在 Prototype 範圍**」，
> 而它已經在 `Ediki.Core` 裡跑了好幾輪，並且有一個 High 優先度的 OD-29 在討論它的定價。
> **文件說不做，程式已經做了，而且沒有任何一份文件記錄這個轉折。**

### 2.2 🔴 「Shipped」測試把實驗內容鎖成正式規格

`[A]` `DataTests.cs:307` `ShippedZhengshou_MatchesTheGddStatBlock`：

```csharp
UnitDef z = UnitLoader.Parse(Read("units")).Get("zhengshou");
Assert.AreEqual(435, z.MaxHp);   // 鎖定 GDD 數值
```

> **正守是 charter §4 的非目標，卻有一個名叫 `Shipped…` 的測試把他的數值鎖死在 GDD 上。**
> 這條測試現在的效果是：**把一個實驗角色當成正式交付內容來保護。**
> 同樣的問題也在 `ShippedRoster_IsStillPerfectlyDivisible`（第 324 行），
> 它斷言整個 roster 的性質，而那個 roster 現在有 ~30 個實驗變體。

### 2.3 內容層動詞住在「要留下來的資產」層

`[A]` charter §5 寫「表現層是可拋棄品，**規則層是要留下來的資產**」。
`[A]` 而 Taunt／Slow／Push／Purify／污染**全部實作在 `Ediki.Core`**。

> `[C]` 目前**沒有任何機制**可以把一個 Core 機制標記為「實驗性、未來可能移除」。
> 一個為了跑實驗而加的動詞，和一個正式規格動詞，在程式碼裡長得一模一樣。

### 2.4 Gym 沒有治理層定義

`[A]` 75 張 `gym-*` 佔了 encounter 總數的 97%，但**沒有任何治理文件說它們在 acceptance scope 之外**。
唯一的說明在 [experiment-playbook §5.4](05-development/experiment-playbook.md)（開發流程文件，不是治理文件）。

### 2.5 Q1 的前提已經過期

`[A]` [playtest-metrics](06-validation/playtest-metrics.md) 的 **Q1 寫「攻擊 5 AP 下」**，
而 OD-01 裁決的基線是 **4 AP**。**驗收問題本身帶著一個過期的數字。**

---

## 3. 「正式內容」與「實驗內容」混淆的具體位置

`[A]` 依嚴重度排序：

| # | 位置 | 混淆型態 |
|---|---|---|
| 1 | [charter §4 第 90–98 行](01-vision/prototype-charter.md) | **交付事實 → 研究禁令**（Stage 01 只有桃太郎 ⇒ 整個 Prototype 不准有別的角色） |
| 2 | `DataTests.cs:307`、`:324` | **實驗內容 → 正式規格**（`Shipped*` 測試鎖定實驗角色與實驗 roster） |
| 3 | charter §4 污染那一列 | **文件禁止 → 程式已做**，且無任何轉折紀錄 |
| 4 | `Ediki.Core` 的四個技能指令 ＋ 污染 | **實驗機制 → 永久資產層**，無標記機制 |
| 5 | charter 檔名／專案資料夾名 | **`Stage 01 Prototype`** 這個名字本身就把兩者綁在一起 |

---

## 4. 4 × 2 方向與 Charter 的關係（最小必要修改）

`[C]` **不做裁決，只分類。**

| 項目 | 分類 | 理由 |
|---|---|---|
| 四個角色存在於實驗中 | **只需補 Experimental Scope** | OD-31 已解除，charter §4 只是沒同步 |
| 每角色 2 種定位（4×2） | **只需補 Experimental Scope** | 定位＝資料層變體，沒有新規則 |
| Mode 1／Mode 2 分層 | **需要新章節（無現有歸屬）** | 目前沒有任何文件有「實驗模式」這個概念 |
| 污染留在實驗範圍 | **需要修改 Charter §4** | 那一列目前是無條件禁止，且無 OD 覆蓋 |
| Taunt／Slow／Push 留在 Core | **需要新增一條治理規則** | 否則實驗動詞會被誤讀成正式規格 |
| 玄真 B（Armor Break） | **GDD hypothesis / experimental track** | `[A]` RULE-GAP，需要新 Command＋UnitState＋StateHasher，**A4 會變** |
| 4×2 成為正式 GDD 規格 | **延後到正式 GDD 裁決** | 本輪明確不做 |

### 最小必要文件變更（`[C]` 建議，等裁決）

1. **charter §4 拆成兩張表**：`Delivery 非目標`（美術／存檔／音效／六角格）與 `Research 非目標`（目前應該接近空的）
2. **charter 新增一節「Experimental Scope」**，收容 Mode 1／Mode 2 與 4×2
3. **正式登錄 OD-33**（目前 OPEN-DECISIONS.md 最高只到 **OD-31**，OD-32／OD-33 都不存在）
4. **重新命名或重新註解 `Shipped*` 測試**，區分「Stage 01 交付資料」與「實驗資料」
5. **在治理層寫下 `gym-*` 不在 acceptance scope**

> ⚠️ **以上五條都不要在沒有裁決的情況下執行。** 第 1 條會動到全專案最高層的範圍文件。

---

## 5. 應維持 Experimental 的內容

`[C]`

- 全部 75 張 `gym-*`
- 61 個單位裡的 ~59 個（除 `momotaro`／`kohaku`）
- Taunt／Slow／Push／Purify 四個動詞的**設計地位**（實作可留，但不等於正式規格）
- 污染系統
- 斬首（`kill`）目標
- 全部 instrument 策略（`attack-only`／`push-instrument`／`slow-instrument`／`taunt-instrument`／`counter-reserve`）
- 4×2 角色定位

## 6. 已可視為 Prototype 核心研究範圍

`[C]`

- **Exposure 主命題**（charter §2 軸心，未被否證）
- **可測量性**（charter §3：能自動化跑分是及格線）—— 已由 317 個測試 ＋ 決定性 batch／replay 支撐
- **「同一行動的價值是否隨局面改變」** —— 這是專案負責人本輪重述的研究目標，
  `[A]` 而目前已有完整儀器（heatmap／role metrics／AP residue／persona matrix／dominance heuristic）
- **角色定位分化**（4×2）作為**研究載體**，不是交付內容

---

## 7. 下一輪需要專案負責人裁決的事項

| # | 事項 | 為什麼擋著 |
|---|---|---|
| 1 | **charter §4 是否拆成 Delivery／Research 兩張非目標表** | 這是本輪所有混淆的根源 |
| 2 | **污染系統的地位** | 文件禁止、程式已實作、無 OD 覆蓋 —— 目前是三方矛盾 |
| 3 | **OD-31 主命題的最終處置**（宣告已回答／退場／改寫） | Highest，charter §2 的地位懸而未決 |
| 4 | **OD-33 正式登錄**（Option A ＋ Mode 1／Mode 2） | 目前只是裁決意向，OPEN-DECISIONS.md 沒有 OD-32／OD-33 |
| 5 | **`Shipped*` 測試要不要改名／重新分類** | 它們現在把實驗內容鎖成正式規格 |
| 6 | **Core 內的實驗機制要不要標記** | 否則下一輪還會再問一次「這是正式的嗎」 |
| 7 | **Q1 的「5 AP」前提要不要更新成 4 AP** | 驗收問題帶著過期數字 |
| 8 | 玄真 B Armor Break 是否進 GDD hypothesis track | `[A]` RULE-GAP，動 Core 會改 A4 |

---

## 8. 本輪沒有做的事

- **沒有修改任何既有檔案**（程式、資料、OD、charter 全部未動）
- **沒有登錄 OD-33**
- **沒有刪除任何角色、污染或 gym 實驗**
- **沒有替專案負責人做任何裁決**
- **沒有把 4×2 當成已裁決規格**

## 9. 上一輪（Prompt 5）的實驗狀態，供接續

`[A]` 四張 test-paper 已建好並跑完 N=50 pilot：
`gym-e1-damagerace` / `gym-e2-pushdelay` / `gym-e3-slowcontrol` / `gym-e4-protection`。

| Encounter | Discrimination | N=200 建議 |
|---|---|---|
| E1 | PASS（Momotaro／Genjin 列） | ✅ 可跑 |
| E2 | PASS | ✅ 可跑 |
| E3 | **PARTIAL**（A/B 軸 100%/100% 飽和） | ⚠️ 需先調敵人威脅結構 |
| E4 | **PARTIAL**（勝率貼地板 2%/20%/0%/0%） | ⚠️ 需先降敵人致命度 |

`[A]` **三個關鍵負面結果**（不要重測，除非改設計）：
- Push 沒有延後接觸：release 3.43 → 3.38
- Slow 沒有延後接觸：release **2.50 → 2.51**（248 次遲滯）
- Taunt 是唯一正收益：勝率 2% → 20%，後排存活 0% → 20%

`[A]` **測試基線**：317 全綠、A1–A7 全過、A4 常數 `3080245196 / 3711821134 / 1561619701` 未變、
`Ediki.Core` 零修改、CSV schema 未變、batch／replay 決定性已驗證。
