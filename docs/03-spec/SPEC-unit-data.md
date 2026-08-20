# SPEC — Unit Data（單位數值 schema 與 Stage 01 名單）

| | |
|---|---|
| **Purpose** | 定義單位有哪些屬性、資料從哪裡來、Stage 01 有誰 |
| **Audience** | 程式、企劃 |
| **Source of Truth** | **schema** 的 SoT 是本檔；**數值** 的 SoT 是 GDD ＋ 尚未選定的封包（[OD-05](../OPEN-DECISIONS.md#od-05)） |
| **Dependencies** | 無 |
| **Related** | [SPEC-combat.md](SPEC-combat.md)、[SPEC-movement.md](SPEC-movement.md)、[04-architecture/data-flow 在 overview.md](../04-architecture/overview.md) |

> **schema 穩定，數值不穩定。** 這個分佈決定了實作順序：
> 先把資料驅動的骨架做出來，數值最後填。

---

## 1. 資料驅動要求

| ID | Statement | Source | Status | Acceptance |
|---|---|---|---|---|
| **R-DATA-01** | AP 成本、地形成本、單位數值**全部進資料**；Core 只讀資料 | SPEC v0.1 §6.4 | `STABLE` | 架構測試 **A5**：程式碼中不得出現這些字面值 |
| **R-DATA-02** | 切換數值封包只需要換資料 ＋ 換 `IRandomSource` 實作，**不需要改規則邏輯** | SPEC v0.1 §4.3、§6.4 | `STABLE` | 兩個封包都能跑通同一套測試（結果不同，但都不崩） |
| **R-DATA-03** | 資料的儲存格式 | SPEC v0.1 §6.4（「ScriptableObject / JSON」兩者都提） | `OPEN → OD-11` | 見下方 §4 |
| **R-DATA-04** | Core 收到的是**引擎中立的純資料型別**，不是 `ScriptableObject` | `DERIVED` ← [ADR-0001](../07-adr/ADR-0001-core-unity-assembly-split.md) | `DERIVED` | Core 的資料型別不引用 UnityEngine |

---

## 2. 單位屬性 schema

| 欄位 | 型別 | 意義 | 來源 | 備註 |
|---|---|---|---|---|
| `id` | string | 唯一識別 | — | |
| `factionId` | enum/int | 陣營 | SPEC v0.1 §6.3 | Stage 01：Player / Enemy |
| `maxHp` | int | 生命上限 | GDD | |
| `atk` | int | 攻擊力 | GDD | |
| `def` | int | 防禦力 | GDD | 用於 [R-COMBAT-05](SPEC-combat.md) |
| `move` | int | 移動力 | GDD | 語意未定 → [OD-04](../OPEN-DECISIONS.md#od-04) |
| `maxAp` | int | AP 上限 | GDD（8） | |
| `hit` | int | 命中（整數化，見下） | GDD | 僅封包 1 使用 |
| `eva` | int | 迴避（整數化） | GDD | 僅封包 1 使用 |
| `attackRange` | int | 攻擊射程 | 推導（[R-COMBAT-01](SPEC-combat.md)） | Stage 01 = 1，**建議企劃確認** |
| `actionCosts` | map | 動作 → AP 成本（可 per-unit 覆寫） | SPEC v0.1 §6.4、§8.1 路線 C | 支援 [OD-01](../OPEN-DECISIONS.md#od-01) 路線 C |
| `atkGrowth` | int | 每完成一回合增加的 ATK | **無 GDD 來源**（2026-08-15 機制實驗） | 預設 0。`0` 的單位行為與此欄位存在之前完全相同 → [OD-28](../OPEN-DECISIONS.md#od-28) |

| ID | Statement | Status |
|---|---|---|
| **R-DATA-05** | `hit` / `eva` 在資料中以**整數**儲存（例如百分比 ×100 或 0–100），**不得用 float** | `STABLE`（[確定性戒一](../04-architecture/determinism.md)） |
| **R-DATA-08** | `atkGrowth` 是**回合數的純函數**（`ATK + growth × (回合−1)`），不得存成單位狀態 | `BASELINE`（gym 專用）｜ Acceptance：`AtkGrowth_IsAPureFunctionOfTheRoundAndZeroByDefault`；成長單位不進 `Clone` 也不進狀態雜湊，A4 不受影響 |

> **R-DATA-05 有一個轉換責任問題**：GDD 寫的是 `HIT 0.80` / `EVA 0.20`（小數）。
> 誰負責轉成整數？建議在資料載入邊界一次轉換完成，
> Core 內部只看到整數。**這是實作細節，不是規格**，記在此處只是提醒不要漏掉。

---

## 3. Stage 01 名單

### 3.1 桃太郎

| 屬性 | GDD | 封包 1 | 封包 2 | 狀態 |
|---|---|---|---|---|
| HP | 300 | 300 | 300 | ✅ 三方一致 |
| ATK | 60 | 60 | 60 | ✅ 三方一致 |
| DEF | 50 | 50 | 50 | ✅ 三方一致 |
| MOVE | 4 | 4 | 4 | ✅ 三方一致 |
| AP | 8 | 8 | 8 | ✅ 三方一致 |
| HIT | 80% | 0.80 | — | 封包 2 不使用 |
| EVA | 20% | 0.20 | — | 封包 2 不使用 |

**桃太郎的數值沒有衝突。** 這是全套規格中最穩定的一組資料。

> ⚠️ GDD Stage 01 另外指定裝備：桃木刀（命中 +10%）、行腳鎧（防禦 +1）。
> **Prototype 不做裝備**（[R-COMBAT-23](SPEC-combat.md)），
> 但「HIT 0.80 是否已含桃木刀的 +10%」文件沒說 → 見 R-COMBAT-23 的註記。

### 3.2 小耗

| 屬性 | GDD | 封包 1 | 封包 2 | 狀態 |
|---|---|---|---|---|
| HP | 80 | **50** | 80 | 🔴 [CONFLICT-03](../CONFLICTS.md#conflict-03) |
| ATK | 100 | 100 | **50** | 🔴 [CONFLICT-03](../CONFLICTS.md#conflict-03) |
| DEF | **未給** | 20 | 20 | 🔴 GDD 無此數值 |
| MOVE | 3 | 3 | 3 | ✅ 一致 |
| AP | 8 | 8 | 8 | ✅（GDD：AP 8 為系統共通規則） |
| HIT | **未給** | 0.60 | — | 🔴 GDD 無此數值 |
| EVA | **未給** | 0.12 | — | 🔴 GDD 無此數值 |

> 🔴 **`DEF 20` / `HIT 0.60` / `EVA 0.12` 在 GDD 中不存在**，是 SPEC v0.1 引入的新數值，
> 沒有來源。裁決 [OD-05](../OPEN-DECISIONS.md#od-05) 時必須一併確認這三個值的出處。

### 3.2b 正守（護行僧）—— gym 用，2026-08-14 加入

| 屬性 | GDD | Prototype | 狀態 |
|---|---|---|---|
| HP | 435 | 435 | ✅ 一致 |
| ATK | 33 | 33 | ✅ 一致 |
| DEF | 70 | 70 | ✅ 一致 |
| MOVE | 3 | 3 | ✅ 一致 |
| AP | 8 | **10 / 恢復 8** | 依 [OD-21](../OPEN-DECISIONS.md#od-21) 的 AP 經濟，與桃太郎相同 |
| 射程 | 未給 | 1 | 專案負責人指定（暫定） |
| HIT / EVA | 0.68 / 0.10 | — | 封包 2 不使用（[OD-05](../OPEN-DECISIONS.md#od-05) 確定性命中） |
| 地形特化 | 碎石強化 | **未實作** | Prototype 沒有碎石地形，也沒有單位地形加成 |

**來源**：GDD《討鬼團核心角色設定報告書：正守》「三、基礎數值 (Ver. RuleLock)」。
**所有數值皆為暫定**，之後看情況調整。

> **正守不在 Stage 01 的編制內**（GDD：他在 Stage 02 加入）。
> 加進 `units.txt` 是為了量「第二個玩家單位」對指標的影響，
> 目前只出現在 `gym-lanes-pair` 這類 gym 地圖。
> 量測結果見 [playtest-metrics §9.5](../06-validation/playtest-metrics.md)：
> **他會把 M5 的策略分離度從 23 個百分點壓到 3 個** —— 這是需要企劃知道的事。

### 3.3 編制

| ID | Statement | Source | Status |
|---|---|---|---|
| **R-DATA-06** | Stage 01：玩家方桃太郎 × 1，敵方小耗 × 4 | GDD Stage 01 三 ＋ SPEC v0.1 §5.2 | `STABLE`（兩份文件一致） |
| **R-DATA-07** | 敵方起始位置分散配置，**確保玩家需要移動才能觸敵** | GDD Stage 01 三 | `STABLE`（意圖）／實際座標 `CONFLICT → CONFLICT-02` |

---

## 4. 資料格式（未定）

見 [OD-11](../OPEN-DECISIONS.md#od-11)。核心張力：

| 約束 | 後果 |
|---|---|
| Core 必須零 UnityEngine 依賴（[ADR-0001](../07-adr/ADR-0001-core-unity-assembly-split.md)） | `ScriptableObject` 只能存在於 Unity 層 |
| SPEC v0.1 §6.2 提到 Core 可用 `dotnet test` 在 Unity 外跑 | Unity 外**讀不到 ScriptableObject** |

若要保留「Unity 外秒級測試」這個紅利，資料正本必須是引擎中立格式。

**附帶事實**：`Packages/manifest.json` 目前**沒有安裝**
`com.unity.nuget.newtonsoft-json`。若選 JSON 且不想手寫 parser，需先加套件。
（SPEC v0.1 §6.7 明確說 `JsonUtility` 不能用：不支援 Dictionary、不支援多型／介面。）

---

## 5. 動作對照（推定）

GDD 給桃太郎四個技能，**沒有「通用攻擊」**。
SPEC v0.1 的 `AttackCommand` 是抽象動作。

| Prototype 動作 | GDD 最接近的對應 | 狀態 |
|---|---|---|
| 攻擊 | 「斬 (Slash)：基礎近戰物理攻擊，提供穩定輸出」 | **推定** → [ODD-04](../DOCUMENT-MAP.md#odd-04) |
| 防禦 | GDD 只給成本「防禦：3 AP」，無對應技能 | 效果 `OPEN → OD-06` |
| 道具 | GDD 只給成本「道具：2 AP」，Stage 01 無道具 | `OPEN → OD-09` |
| 移動 | 移動 | `CONFLICT → CONFLICT-01` |

> GDD 三.1 寫的是「**攻擊 / 技能**：5 AP」——
> 把攻擊與技能放在同一個成本，暗示「斬」也是 5 AP。
> 但這是推測，**建議企劃確認**（低風險確認題）。
