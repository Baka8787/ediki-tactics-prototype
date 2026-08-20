# SPEC — Status Effects（狀態效果）

| | |
|---|---|
| **Purpose** | `ActiveStatus` 的完整規則語意。PR-1 的前置規格 |
| **Audience** | 程式、企劃、Codex |
| **Source of Truth** | 本檔（規則層）／[GDD](../00-source/GDD-穢土紀企畫書-暫定.extracted.txt)（設計意圖） |
| **Dependencies** | [NOTE-2026-08-20 規劃與工單](../NOTE-2026-08-20-狀態系統與地塊效果-規劃與工單.md)、[OD-35](../OPEN-DECISIONS.md#od-35) |

> ## ✅ 已裁決 — 2026-08-20（專案負責人）
>
> §3–§4 共 **14 條** 規則，**13 條 `STABLE`**、**1 條仍 `OPEN`**
> （R-STATUS-12b 清除優先序，依裁決另定）。
> 裁決紀錄見 [OD-35](../OPEN-DECISIONS.md#od-35)。

---

## 0. Prototype 偏離登記簿

**專案負責人 2026-08-20 指示：`必中` 僅為 Prototype 實驗規則，不修改 Production GDD 的最終語意。**

**GDD 是設計正本；本檔只描述 Prototype 規則層的行為。** 兩者不同處一律登記於此並附回收條件
—— 否則三個月後沒有人分得出「這是刻意的實驗簡化」還是「這是實作錯誤」。

| # | GDD 語意 | Prototype 實驗語意 | 為什麼偏離 | 回收條件 |
|---|---|---|---|---|
| **D-01** | 狀態附加帶機率（「30% 暈眩」「50% 機率免疫」「隨機賦予」） | **必中**，不做機率 | 規則層是決定論的（[ADR-0003](../07-adr/ADR-0003-deterministic-rule-layer.md)、determinism rule 1、[OD-05](../OPEN-DECISIONS.md#od-05) 已裁掉命中／閃避骰）。加入 RNG 需要 `BattleState` 帶種子、`StateHasher` 納入 RNG 狀態、A4 三個常數重算、replay 與 SquadMatrix 的可重現性重驗 | 「規則層要不要有 RNG」單獨開 OD 並裁決為「要」時。**在那之前，GDD 的機率數值不視為已實作** |
| **D-02** | 緩速 SLOW＝移動成本 × 2 | 既有 `SlowCommand`＝每格 **+1 AP** | 既有機制已有量測基線 | 本輪不碰 |
| **D-03** | 虛弱 FRAIL＝DEF × 0.70 | 既有破甲＝DEF **− N**，與虛弱**並存**為兩種工具 | 破甲的價值在於跨過刀數階梯，而該階梯是懸崖（2 刀 68%／3 刀 1%，見 `units.txt` 註解）。換成乘法會改變它跨不跨得過去 | — |
| **D-04** | 狀態來源含裝備、被動、多種技能 | 本輪只有 **地形** 與 **攻擊附帶** | 裝備已排除；技能施放狀態需新 Command，屬「新增系統」流程 | 裝備系統進入範圍時 |
| **D-05** | 持續時間以「回合」計 | 以**受影響單位的 phase** 計（R-STATUS-04） | GDD 的「回合」未定義是 round 還是 phase。以 phase 計可讓「持續 2 回合」永遠等於「作用 2 次」，與施加時機無關 | — |

> **維護規則**：偏離被回收時**不要刪除該列** —— 改標 `RESOLVED` 並註明日期。
> 理由同 [CONFLICTS.md](../CONFLICTS.md)：未來需要知道規則為什麼曾經長那樣。

---

## 1. GDD 實際規定了什麼

### 1.1 有明確數值的

| 狀態 | GDD 原文 | 行號 |
|---|---|---|
| 中毒 POISON | 每回合失去 10% 生命值 | 112 |
| 中毒（鬼牙墜） | 每回合失去**最大 HP 10%**，**持續 2 回合** | 439 |
| 流血 BLEED | 每回合失去**最大生命值 5%**，**持續 2 回合** | 115 |
| 強壯 MIGHT | 攻擊力 × **1.30** | 106 |
| 脫力 WEAKEN | 攻擊力 × **0.70** | 107 |
| 固元 FORTIFY | 防禦力 × **1.30** | 108 |
| 虛弱 FRAIL | 防禦力 × **0.70** | 109 |

> 行 112 的「10% 生命值」有歧義，行 439 同一狀態寫「**最大 HP** 10%」。
> **採最大 HP**（裁決 D14）：取較具體者，且取現有 HP 會讓中毒永遠殺不死人，
> 與「持續傷害型」的定位矛盾。

### 1.2 GDD 提到但沒給規則的

驅散存在（狂暴「DISPEL: false」反面推得，119）、「清除 **1 個**負面狀態」但沒說挑哪個（247）、
清除全部（272／386）、持續時間可被 +1（644）、疊加只出現在裝備專屬機制（331／322）。

### 1.3 GDD 完全沒有提到的 —— §3 的全部內容

結算時機、同名共存、重複施加、來源記錄、整數取捨、修飾套用順序、能否致死、雜湊順序。

> 這不是 GDD 的缺失。它是設計文件，不是規則規格。

---

## 2. Core 既有前例

| 前例 | 內容 | 位置 |
|---|---|---|
| 回合戳記 | `SlowedUntilTurn` 等，`TurnIndex <= stamp` 生效，**不需遞減** | `BattleState.cs:423` |
| 重複施加＝拒絕 | Slow／破甲／引誘三個都 `Reject`。理由：防止把 AP 倒進「看起來有生產力」的動作而污染量測 | `BattleSimulator.cs:324, 365, 412` |
| 雜湊 gating | `HasContamination` / `HasControlStatus` / `HasArmorBreak` / 每回合上限，四次 | `BattleState.cs:350`、`StateHasher.cs` |
| 每回合 tick 位置 | `ExecuteEndTurn` 內 `SpreadContamination` | `BattleSimulator.cs:634` |
| 傷害事件無來源 | `HpChanged(unitId, delta, newHp)` | `Effects.cs:82` |

> ### ⚠️ 本規格**刻意偏離**第一項前例
>
> 既有狀態用**回合戳記**（不需遞減、自己過期）。本規格改用 **`RemainingPhases` 倒數**
> （裁決 D1），因為戳記以 `TurnIndex`（round）為單位，而 R-STATUS-04 要求以
> **phase** 計數 —— 用 round 戳記表達 phase 語意需要一組不對稱修正，
> 那正是 `StatusExpiryTurnFor` 現在在做的事，而它只支援「1 個 phase」。
>
> **代價**：倒數需要一個遞減點（R-STATUS-13）。
> **既有四個狀態不受影響、不改寫。**

---

## 3. 規則條目

### 3.1 表示法與雜湊

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-STATUS-01** | 一個生效中的狀態以 `(Kind, RemainingPhases, Magnitude)` 表示。`RemainingPhases > 0` 即為生效中 | OD-35 D1 | `STABLE` | 施加 `RemainingPhases=2` 後，該狀態在受影響單位的 2 個 phase 內生效，第 2 個 phase 結束時消失 |
| **R-STATUS-02** | `BattleState.HasStatuses` 為假時，`StateHasher` 不折入任何狀態資料 | `DERIVED`（四次前例） | `STABLE` | **無狀態的戰鬥雜湊逐位元不變；A4 三個常數 `3080245196`／`3711821134`／`1561619701` 不動** |
| **R-STATUS-03** | 狀態折入雜湊前依 `(Kind, RemainingPhases, Magnitude)` 遞增排序 | OD-35 D3 | `STABLE` | 同一組狀態以不同**加入順序**產生，雜湊必須相同 |

> ### 📌 修正：D3／D7 **不會**讓既有 A4 失效
>
> 本檔前一版寫「事後重排列舉會讓 A4 與所有存檔失效」。**那是錯的，已修正。**
>
> 只要 R-STATUS-02 的 gate 正確，**沒有任何單位帶狀態的戰鬥就不折入狀態資料**，
> 雜湊與現在逐位元相同。A4 的三個常數建立在 `TestWorld` 上，那裡沒有狀態，
> **不論列舉值或排序怎麼改，A4 都不會動** —— 這正是 gating 設計的全部目的。
>
> D3／D7 真正影響的範圍是**帶有狀態的狀態雜湊**：
> 未來為狀態新增的 golden 常數、跨版本 replay 比對、任何被記錄下來的含狀態雜湊。
> 這仍是「值得一次定好」的理由，但**不是「會炸掉 A4」**。
>
> **反過來說**：如果哪天改了列舉值而 A4 竟然變了，那代表 **gate 壞了**，
> 要修的是 gate，不是常數。

---

### 3.2 持續時間與遞減

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-STATUS-04** | 「持續 N 回合」的 N 以**受影響單位的 phase** 計數，與施加時機無關 | OD-35 D4 | `STABLE` | 敵方 phase 施加的 `N=2` 中毒與我方 phase 施加的 `N=2` 中毒，都恰好作用 2 次 |
| **R-STATUS-13** | `RemainingPhases` 在**受影響陣營的 phase 結束時**遞減 1，歸零者移除 | `DERIVED`（D1＋D4＋D8 的唯一自洽解） | 🟡 `STABLE`（**推導，請確認**） | `N=2` 的強壯在該單位的 2 個 phase 內都提供 ×1.30，第 3 個 phase 不再提供 |

> **R-STATUS-13 為什麼在 phase 結束**
>
> D8 已裁定 tick 在 **phase 開始**。若遞減也放 phase 開始，`N=2` 的乘法狀態
> 只會在第 1 個 phase 生效（第 2 個 phase 一開始就歸零移除）——
> 與 D4「作用 2 次」矛盾。**phase 結束是唯一能同時滿足 D4 與 D8 的位置。**
>
> **已知不對稱（刻意，需知悉）**：在受影響單位**自己的 phase 中途**被施加的狀態
> （例如走上毒沼），該 phase 的開始已經過去，所以**當個 phase 不 tick**，
> 但在該 phase 剩餘時間內生效，並於該 phase 結束時遞減一次。
> 對地形來源無影響 —— 只要還站著，下個 phase 會依 R-STATUS-08 刷新。

---

### 3.3 結算時機

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-STATUS-05** | 持續傷害在**受影響陣營的 phase 開始時**結算，依 `UnitState.Id` 遞增順序，**一個 phase 最多一次** | OD-35 D8 | `STABLE` | 一個 phase 內每個中毒單位恰好扣血一次 |
| **R-STATUS-06** | 持續傷害**可以致死**，走既有死亡判定與 `UnitDied`，並重新評估勝負 | OD-35 D9 | `STABLE` | 中毒把 HP 打到 0 時單位死亡、`UnitDied` 發出一次、`CheckBattleEnd` 被呼叫 |

> **實作位置**：`BattleSimulator.ExecuteEndTurn` 內既有的「incoming faction 的 phase 開始」
> 迴圈（解除格擋、AP regen 的那一段）。
>
> ⚠️ **DoT 致死時，該單位必須跳過同一迴圈裡的 AP reset 與旗標清除。**
> 這是本規格最容易寫錯的地方：死亡發生在一個原本假設「本迴圈的單位都活著」的位置。

---

### 3.4 疊加與重複施加

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-STATUS-07** | 同一單位身上同一 `StatusKind` **最多一份** | OD-35 D5 | `STABLE` | 對已中毒目標再次施加中毒，狀態數量仍為 1 |
| **R-STATUS-08** | 重複施加**刷新持續時間**：`RemainingPhases = max(現有, 新)`。**`Magnitude` 不累加**，取 `max(現有, 新)` | OD-35 D10 | `STABLE` | 對剩 1 phase 的 10% 中毒施加 2 phase 的 5% 中毒 → 結果為 `RemainingPhases=2, Magnitude=10` |

> **與既有前例相反，這是刻意的。** Slow／破甲／引誘拒絕重複施加，理由是防止玩家把 AP
> 倒進看起來有生產力的動作而污染「玩家有沒有事做」的量測 ——
> **那條理由對地形完全不適用**（站著不花 AP）。照抄前例會讓毒沼在第 2 個 phase 起失效。

---

### 3.5 數值與取捨

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-STATUS-09** | 修飾依**下方明列的固定順序**逐一套用，每步整數截斷。**此順序與 `StatusKind` 列舉值無關** | OD-35 D11 | `STABLE` | 見下方常數斷言 |
| **R-STATUS-10** | 持續傷害 = `MaxHp × Magnitude / 100`，整數截斷，**最少 1** | OD-35 D12 ＋ `DERIVED`（沿用 `RestHealAmount`） | `STABLE` | `MaxHp=40` 的 5% 每次扣 **2**；`MaxHp=15` 的 5% 扣 **1**（不是 0） |

**R-STATUS-09：修飾套用順序（規範性）**

```
EffectiveAtk：
  1. base  = UnitDef.AtkOnRound(turnIndex)     ← 既有成長
  2. × Might   (%)                             ← 增益先
  3. × Weaken  (%)                             ← 減益後

EffectiveDef：
  1. base  = UnitDef.Def
  2. × Fortify (%)                             ← 增益先
  3. × Frail   (%)                             ← 減益後
  4. − ArmorBreakAmount                        ← 減法最後（既有行為）
  5. clamp 至 >= 0
```

**規範原則**：增益先於減益；乘法先於減法；**每一步都截斷**。

> **為什麼不用列舉順序**（裁決 D11）：列舉值是雜湊的一部分。
> 把計算順序綁在它上面，會讓「調整列舉」與「調整算式」變成同一件事 ——
> 兩個本應各自可改的決定被焊死。上表是**獨立的**規範。
>
> **給 Codex**：請把這個順序寫成一個具名的明確序列（例如
> `AtkModifierOrder` / `DefModifierOrder` 常數陣列），**不要**用
> `foreach (status in statuses.OrderBy(s => s.Kind))`。
> 後者能跑，但它讓下一個人以為順序是列舉決定的。

**常數斷言（測試必須逐字包含）**

| 情境 | 逐步計算 | 結果 |
|---|---|---|
| `ATK=100`，強壯130＋脫力70 | `100×130/100=130` → `130×70/100=91` | **91** |
| `ATK=3`，強壯130＋脫力70 | `3×130/100=3` → `3×70/100=2` | **2** |
| `DEF=50`，固元130＋虛弱70＋破甲20 | `50×130/100=65` → `65×70/100=45` → `45−20=25` | **25** |
| `DEF=10`，虛弱70＋破甲20 | `10×70/100=7` → `7−20=−13` → clamp | **0** |

---

### 3.6 來源與移除

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-STATUS-11** | `ActiveStatus` **不記錄施加者** | OD-35 D6 | `STABLE` | `ActiveStatus` 恰有三個欄位 |
| **R-STATUS-12a** | 狀態的移除只有三條路：`RemainingPhases` 歸零、單位死亡、明確的移除效果 | OD-35 | `STABLE` | 離開毒沼不會立刻解毒（毒自然過期） |
| **R-STATUS-12b** | 「清除 1 個負面狀態」時挑哪一個 | — | 🔴 **`OPEN`** | — |

> ### R-STATUS-12b：清除優先序 —— **刻意留空**
>
> 裁決 D7 明確要求：**`StatusKind` 列舉順序不得決定 cleanse priority，清除規則另定。**
>
> 本檔前一版曾建議「取列舉值最小者」。**該建議已撤回** —— 它會把一個雜湊穩定性需求
> （列舉值不可重排）和一個玩法設計需求（先解哪個 debuff）綁成同一個決定。
>
> **本輪不實作任何清除效果**（來源只有地形與攻擊附帶，見偏離簿 D-04），
> 所以這條不擋 PR-1～PR-5。等有清除效果的來源進入範圍時再開 OD。
>
> **給 Codex**：不要寫任何依賴 `StatusKind` 大小關係的清除邏輯。

**增益／減益分類**（供未來的清除效果使用，本輪僅登記）

| 增益 | 減益 |
|---|---|
| `Might`、`Fortify` | `Poison`、`Bleed`、`Weaken`、`Frail` |

> 此分類寫在 `StatusKind` 的定義旁，**不要散落在呼叫端**。

---

## 4. 事件模型：`HpChanged` 加來源分類（裁決 D13＝c）

### 4.1 波及範圍調查 —— **不需要回報阻塞**

裁決 D13 要求「若會造成超出本輪的大規模事件模型改造，先回報」。**調查結果：不會。**

| | 數量 | 位置 |
|---|---|---|
| `new HpChanged(` 建構點 | **4** | 全在 `BattleSimulator.cs`：270 攻擊、463 致命地形、583 休息回復、762 反擊 |
| 讀取點 | **6 ＋ 1 測試** | `BattleHeatmap` 400、`BattleTranscript` 217、`PositionSolver` 338、`RoleMetrics` 384/435/458、`SpatialHeatmapTests` 276 |

**`HpChanged` 不進 `StateHasher`**（雜湊只涵蓋 state，不涵蓋 effect），
所以**這個改動對 A4 零風險**。真正需要改的只有要分辨來源的 `BattleHeatmap` 與 `RoleMetrics`。

### 4.2 規範

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-STATUS-14** | `HpChanged` 增加 `HpChangeCause Cause`。四個既有建構點必須明確指定 | OD-35 D13 | `STABLE` | 每個 `new HpChanged` 都帶明確 Cause，無一依賴預設值 |

```csharp
public enum HpChangeCause
{
    Attack  = 0,   // 既有 270：普通攻擊
    Counter = 1,   // 既有 762：反擊
    Terrain = 2,   // 既有 463：致命地形
    Rest    = 3,   // 既有 583：休息回復
    Status  = 4    // 新增：中毒／流血
}
```

> **建議不給預設值**，強迫四個既有建構點明確標註 —— 那正是這個改動的價值。
> 讀取端的相容性由「不解構 Cause 的既有讀取自然不受影響」保證。

### 4.3 順帶發現：一個既有的量測缺陷

`BattleHeatmap.CountDamage` 的傷害訊號是「`HpChanged` 且 `Delta < 0`」（`BattleHeatmap.cs:400`），
而**致命地形也發負的 `HpChanged`**（`BattleSimulator.cs:463`，扣掉該單位全部剩餘 HP）。

**也就是說：一個被推進深坑的單位，目前在熱圖上登記為那一格的一筆大額傷害。**
這是既有缺陷，與狀態無關，`gym-crucible-chasm` 的熱圖直接受影響。

> ⚠️ **這是發現，不是本輪的授權範圍。**
> 修正它會改變已發表的熱圖數字，屬於量測變更，應另開決議。
> R-STATUS-14 只負責**讓它變得可以分辨**；要不要據以過濾是下一個決定。

### 4.4 量測交接

**PR-2 必須在 `playtest-metrics.md` 增加一行**，說明 `Cause=Status` 的扣血如何計入 M1／M2。
沒有那一行，下一輪的數字就不可與本輪比較。

---

## 5. PR-1 檢查表

| 要做 | 對應規則 |
|---|---|
| `StatusKind` 列舉（值固定）＋增益／減益分類 | R-STATUS-12b 附表 |
| `ActiveStatus`：`(Kind, RemainingPhases, Magnitude)` 三欄 | R-STATUS-01、11 |
| `UnitState.Statuses`（無狀態時為 `null`）＋ `Clone()` | `ADR-0004`、測試 A3 |
| `BattleState.HasStatuses` gate | R-STATUS-02 |
| `StateHasher` gated fold ＋ 排序 | R-STATUS-02、03 |

```csharp
public enum StatusKind
{
    None = 0, Poison = 1, Bleed = 2, Might = 3, Weaken = 4, Fortify = 5, Frail = 6
}
```

> **列舉值固定不得重排**（裁決 D7）：它是雜湊內容的一部分。
> 但它**不決定任何玩法順序** —— 修飾順序見 R-STATUS-09，清除順序見 R-STATUS-12b。

**PR-1 驗收**：A4 三個常數未變、A3（Clone 隔離）通過、排序決定論測試通過、
**遊戲行為逐位元不變**（此時沒有任何來源會施加狀態）。

---

## 6. 待辦

- [x] 裁決並登錄 [OD-35](../OPEN-DECISIONS.md#od-35)
- [x] 更新 `prototype-charter.md §4`
- [ ] 確認 **R-STATUS-13**（遞減在 phase 結束）—— 由 D1＋D4＋D8 推導，非直接裁決
- [ ] 確認 **R-STATUS-08** 的 `Magnitude` 取 `max()` —— 由 D10「不疊」＋原預設推導
- [ ] R-STATUS-12b 清除優先序：有清除來源時另開 OD
- [ ] §4.3 熱圖缺陷：另開量測決議
- [ ] 更新 `DOCUMENT-MAP.md` Traceability 表（R-STATUS-01..14）
- [ ] 記入 `CHANGELOG.md`
