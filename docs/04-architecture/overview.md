# Architecture — Overview

| | |
|---|---|
| **Purpose** | 定義系統責任怎麼分、依賴往哪流、誰擁有什麼資料 |
| **Audience** | 程式 |
| **Source of Truth** | 本檔（源自 SPEC v0.1 §6.2、§6.9） |
| **Dependencies** | [03-spec/](../03-spec/) 全部 |
| **Related** | [simulation-core.md](simulation-core.md)、[determinism.md](determinism.md)、[extension-points.md](extension-points.md)、[07-adr/](../07-adr/) |

> **本檔描述責任分工，不含 implementation code。**
> 出現在這裡的型別簽章只是為了定義**邊界**，不是要照抄的實作。

---

## 1. 一句話

> **規則層是資產，表現層是可拋棄品。**

這句話直接來自 [prototype-charter §5](../01-vision/prototype-charter.md)：
之後要換 2D／2.5D／HD-2D **甚至換引擎**，都等 Prototype 驗證玩法後再說。

因此規則層與 UnityEngine 的分離**不能只是紀律，必須是編譯期強制**。
理由見 [ADR-0001](../07-adr/ADR-0001-core-unity-assembly-split.md)。

---

## 2. 分層

```
┌─────────────────────────────────────────────────┐
│  Ediki.Unity  （表現層 / 輸入 / 場景）             │
│  ─ 可拋棄品。換維度、換引擎時整層重寫              │
│  ─ 播放 EffectLog、呼叫唯讀查詢、送出 Command      │
└────────────────────┬────────────────────────────┘
                     │  單向依賴 ↓
┌────────────────────┴────────────────────────────┐
│  Ediki.Core   （規則層）                          │
│  ─ 資產。零 UnityEngine 依賴（編譯期強制）          │
│  ─ BattleState / BattleSimulator / Command       │
│  ─ Effect / IGridTopology / IRandomSource        │
└─────────────────────────────────────────────────┘
```

### 2.1 Assembly 佈局

```
Assets/Scripts/
├── Core/            Ediki.Core.asmdef        ← 勾選 No Engine References
├── Unity/           Ediki.Unity.asmdef       ← 引用 Core
└── Editor/          Ediki.Editor.asmdef
Assets/_Project/Tests/
└── EditMode/        Ediki.Tests.EditMode.asmdef
```

`Ediki.Core.asmdef` 的 **No Engine References** 選項讓「Core 零 UnityEngine」
變成**編譯期強制** —— 誤 `using UnityEngine` 直接編譯失敗。
這比用測試掃引用更硬。

> **後果**：Core 內不能用 `Vector2Int`，必須自訂 `Coord`（見
> [SPEC-grid-terrain R-GRID-03](../03-spec/SPEC-grid-terrain.md)）。
> 也不能用 `ScriptableObject`（見 [OD-11](../OPEN-DECISIONS.md#od-11)）。

### 2.2 依賴方向

| From | To | 允許？ |
|---|---|---|
| Unity → Core | ✅ | 唯一允許的方向 |
| Core → Unity | ❌ | 編譯期擋掉 |
| Core → UnityEngine | ❌ | 編譯期擋掉 |
| Editor → Core / Unity | ✅ | |
| Tests → Core | ✅ | |
| Tests → Unity | ⚠️ | 盡量避免。架構回歸測試應該只需要 Core |

**沒有循環依賴的可能性** —— asmdef 本身就不允許。

---

## 3. 規則層與表現層之間的**兩條線**

SPEC v0.1 §6.9 只描述了一條線（EffectLog）。
本輪分析發現需要**兩條**，因為 UI 需求（[R-THR-09/10](../03-spec/SPEC-threat-activation.md)）
無法只靠事件流滿足。

```
                  ┌──────────────── Ediki.Unity ─────────────────┐
                  │                                              │
   Command  ──────┤  輸入 → 建 Command → 送進 Simulator            │
      ↓           │                                              │
 ┌────┴────────┐  │  ① EffectLog ──→ 攤成時間軸播放（動畫、音效）    │
 │ Ediki.Core  │  │                                              │
 │ Simulator   ├──┤  ② 唯讀查詢 ──→ hover 顯示 Exposure / 危險區    │
 └─────────────┘  │                                              │
                  └──────────────────────────────────────────────┘
```

### 線 ①：EffectLog（事件流，推）

- 規則層瞬間算完 → 回傳有序的 `EffectLog`
- 表現層攤成時間軸播放
- **表現層不查詢規則層、不做任何判斷，只按序播**
- 這條線由架構測試 **A7** 守住

### 線 ②：唯讀查詢（拉）

- UI 要顯示「hover 這格的 Exposure」「全體危險區」
- 這是**空間查詢**，事件流無法提供
- 規則層額外提供純函式查詢介面，回傳**值**（value），不是可變狀態的參考

| 規則 | 內容 |
|---|---|
| 查詢必須是純函式，不改變 state | [R-THR-11](../03-spec/SPEC-threat-activation.md) |
| 表現層不得自行重算這些值 | [R-THR-12](../03-spec/SPEC-threat-activation.md) |
| 表現層拿到的是 value，不是 `BattleState` 的參考 | 本檔 |

> **為什麼要區分「值」和「參考」**：A7 的意圖是「表現層不能反向依賴 Core 的**可變狀態**」。
> 給值不違反這個意圖，給 `BattleState` 的參考會。
>
> **這是本輪新增的架構規則，SPEC v0.1 沒有涵蓋。**
> 若不區分，實作者會很自然地把 `BattleState` 傳給 UI，然後 A7 形同虛設。

---

## 4. Data Ownership

| 資料 | 擁有者 | 生命週期 | 誰能改 |
|---|---|---|---|
| `BattleState`（單位位置、HP、AP、回合） | **Ediki.Core** | 一場戰鬥 | 只有 `BattleSimulator.Execute` |
| `EffectLog` | Core 產生 → Unity 消費 | 一個 Command | 產生後**不可變** |
| 單位定義資料（HP/ATK/DEF/MOVE/成本） | 資料檔（格式見 [OD-11](../OPEN-DECISIONS.md#od-11)） | 整個專案 | 企劃（改資料，不改程式） |
| 地圖資料 | 資料檔 | 整個專案 | 企劃 |
| 場景 GameObject / Prefab | **Ediki.Unity** | 一個場景 | 表現層 |
| 輸入狀態（滑鼠位置、選取中的單位） | **Ediki.Unity** | 一幀 | 表現層。**不進 `BattleState`** |
| 動畫進度 | **Ediki.Unity** | 播放期間 | 表現層 |

> **「選取中的單位」不屬於 `BattleState`。**
> 這是很容易做錯的地方 —— 選取是 UI 狀態，不是遊戲狀態。
> 放進 `BattleState` 會污染狀態雜湊，讓 A4（同指令串 → 同雜湊）失效。

---

## 5. 模組責任

| 模組 | 責任 | **不**負責 |
|---|---|---|
| `BattleSimulator` | 驗證 Command、產生 Effect、套用狀態變更 | 動畫、UI、輸入、AI 決策 |
| `BattleState` | 持有戰鬥的完整狀態；提供 `Clone()` 與正規化序列化 | 任何規則邏輯 |
| `IGridTopology` | 鄰接、距離、界內判定、全格列舉 | 地形成本、單位佔用 |
| 移動成本模型 | 每格／每邊的成本查詢 | 拓撲 |
| 可達性計算 | Dijkstra flood fill（移動範圍、威脅範圍共用） | 決定要走哪條路 |
| 尋路 | A\*（AI 點對點） | 移動範圍 |
| `IRandomSource` | 提供整數亂數 | 決定何時需要亂數 |
| AI | 決定敵方要下什麼 Command | 直接改狀態（**AI 必須走同一個漏斗**） |
| 表現層 | 播 EffectLog、顯示查詢結果、蒐集輸入 | 任何規則判斷 |

> **AI 必須走同一個漏斗**（[R-CMD-01](../03-spec/SPEC-battle-flow.md)）。
> 這讓 AI 推演可以直接用 `Clone()` + `Execute()`，不需要第二套規則實作。
> 兩套規則實作 = 兩套 bug。

---

## 6. Runtime Flow

一次玩家操作的完整流程：

```
1. 玩家點擊格子
        ↓  (Ediki.Unity)
2. 表現層建立 MoveCommand(unitId, path)
        ↓
3. BattleSimulator.Execute(state, command)      ← 純函式，瞬間完成
        ↓  (Ediki.Core)
4. Validate → 不合法就回 Ok=false，state 原封不動
        ↓
5. 合法 → 產生有序 Effect[]，套用出新的 BattleState
        ↓
6. 回傳 ExecuteResult { State, Log, Ok, RejectReason }
        ↓  (Ediki.Unity)
7. 表現層替換持有的 state 參考
        ↓
8. 表現層把 Log 攤成時間軸，逐一播放
        ↓
9. 播放期間 UI 用「線 ②」查詢新 state 的 Exposure 等資訊
```

**第 3 步到第 6 步是瞬間完成的**，沒有時間概念。
時間只存在於第 8 步。這是規則層與表現層分離的核心：
**規則層沒有「動畫播到一半」這種狀態。**

### 6.1 自動化跑分的流程

Prototype 的成功標準是「可測量」，所以這條流程和上面一樣重要：

```
1. 載入資料 → 建初始 BattleState
2. 迴圈：策略函式(state) → Command → Execute → 新 state
3. 直到 BattleEnded 或超過 timeout
4. 蒐集指標（見 06-validation/playtest-metrics.md）
5. 重複 N 次（不同 seed / 不同策略 / 不同資料封包）
```

**第 2 步完全不需要 Unity。** 這是 [ADR-0001](../07-adr/ADR-0001-core-unity-assembly-split.md)
帶來的最大實際回報：1000 場跑分不用開 Editor。

---

## 7. 環境事實

| 項目 | 值 | 來源 |
|---|---|---|
| Unity | **6000.5.1f1** | `ProjectSettings/ProjectVersion.txt`（實際查證） |
| 渲染管線 | URP 17.5.0 | `Packages/manifest.json` |
| Test Framework | 1.7.0 | `Packages/manifest.json` |
| Newtonsoft JSON | **未安裝** | `Packages/manifest.json` |

> 🔴 SPEC v0.1 §6.1 記載 Unity 6.0 / 6.3（6000.0 / 6000.3）與 C# 9.0，
> **與實際專案不符** → [CONFLICT-06](../CONFLICTS.md#conflict-06)。
> C# 語言版本與可用語言特性**必須在 6000.5.1f1 上實測後**才能寫進
> [coding-guidelines.md](../05-development/coding-guidelines.md)。
>
> 這是一個**查證題，不是討論題**。

### 7.1 Unity 外測試（社群做法，非官方）

因為 Core 零引擎依賴，可以另外維護一份 .NET Standard 2.1 的 `.csproj`，
用 `dotnet test` 在 Unity 外跑確定性測試，CI 秒級回饋，不用開 Editor。

**代價**：需要維護兩套建置。
**注意**：SPEC v0.1 附錄 B 自述這是社群通用做法，**非 Unity 官方文件明載**。

這個選項與 [OD-11](../OPEN-DECISIONS.md#od-11)（資料格式）強耦合 ——
選 ScriptableObject 作為正本就等於放棄它。
