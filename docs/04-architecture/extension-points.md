# Architecture — Extension Points（刻意留軟的接縫）

| | |
|---|---|
| **Purpose** | 定義哪些地方刻意留了介面、為什麼留、以及**什麼時候不該留** |
| **Audience** | 程式 |
| **Source of Truth** | 本檔（源自 SPEC v0.1 §6.4） |
| **Dependencies** | [overview.md](overview.md)、[simulation-core.md](simulation-core.md) |
| **Related** | [ADR-0001](../07-adr/ADR-0001-core-unity-assembly-split.md)、[ADR-0005](../07-adr/ADR-0005-grayblock-3d-prototype-shell.md) |

---

## 判準：什麼值得留介面

留一個介面不是免費的 —— 它增加間接層、增加測試面、增加閱讀成本。

**本專案的判準**：只有滿足以下**其中一項**才留：

1. **已知有第二個實作要跑**（不是「未來也許」，是「這輪要跑」）
2. **它是一個未決事項的技術落實**（讓我們不必現在決定）
3. **它是換維度／換引擎時的切割線**

不滿足以上任一項的介面 = 過度設計，**應該刪掉**。

---

## 1. `IGridTopology`

```csharp
public interface IGridTopology
{
    IEnumerable<Coord> Neighbors(Coord c);   // 回傳順序必須固定
    int  Distance(Coord a, Coord b);
    bool Contains(Coord c);
    IEnumerable<Coord> AllCoords();
}
```

| 判準 | 符合？ |
|---|---|
| 已知有第二個實作要跑 | ❌ 只實作 `SquareGrid4` |
| 未決事項的技術落實 | ❌ |
| 換維度／換引擎的切割線 | ✅ **是** |

**保留理由**：SPEC v0.1 §1.2 明確寫「不做六角格（**但介面要留**）」，
且 §6.4 明確寫「**六角格不要實作**，留介面就好（YAGNI）」。

> 🔴 **這個介面附帶一條禁令**（[R-GRID-01](../03-spec/SPEC-grid-terrain.md)）：
> **不准實作第二個拓撲。**
> 它存在的目的是防止未來換拓撲時要動整份規則層，
> **不是**為了現在支援兩種格子。
>
> 未來真要換：cube 座標給演算法、axial 給儲存。

---

## 2. `IRandomSource`

```csharp
public interface IRandomSource
{
    int NextInt(int exclusiveMax);   // 整數，不用 float
}
```

| 判準 | 符合？ |
|---|---|
| 已知有第二個實作要跑 | ✅ **是** — `AlwaysHitSource`（封包 2）與 `SeededPcgSource`（封包 1） |
| 未決事項的技術落實 | ✅ **是** — [OD-05](../OPEN-DECISIONS.md#od-05) 未拍板 |
| 換維度／換引擎的切割線 | ❌ |

**這是本專案最有價值的一個介面。**
它是「不自行決定 OD-05」這條紀律的技術落實 ——
有了它，命中模型從一個**架構決策**降級成一個**設定值**。

| 規則 | 內容 |
|---|---|
| 規則層不得直接呼叫任何 RNG | [R-COMBAT-11](../03-spec/SPEC-combat.md) |
| 必須是整數 RNG | [R-COMBAT-12](../03-spec/SPEC-combat.md)、[determinism 戒一](determinism.md) |
| 切換封包 = 換這個實作 + 換資料表 | [R-DATA-02](../03-spec/SPEC-unit-data.md) |

---

## 3. 移動成本模型

SPEC v0.1 沒有明確給這個介面，**但本輪分析認為它是必要的**。

| 判準 | 符合？ |
|---|---|
| 已知有第二個實作要跑 | ✅ **是** — 平坦成本 vs 地形成本（[CONFLICT-01](../CONFLICTS.md#conflict-01)） |
| 未決事項的技術落實 | ✅ **是** |
| 換維度／換引擎的切割線 | ❌ |

**理由**：[CONFLICT-01](../CONFLICTS.md#conflict-01) 是 BLOCKER 且**兩種讀法行為完全不同**。
沒有這個接縫，裁決結果會逼迫改寫 Dijkstra 的呼叫端。

有了它，兩種模型都是資料 ＋ 一個小實作，裁決當天就能切換。

> **這是本輪新增的架構元素**，來源是對 CONFLICT-01 的風險應對，
> 不是既有文件的要求。

---

## 4. `ITurnOrder` — ⚠️ 建議刪除

```csharp
public interface ITurnOrder { /* 陣營輪流；未來可換個體行動序 */ }
```

| 判準 | 符合？ |
|---|---|
| 已知有第二個實作要跑 | ❌ Stage 01 只有陣營輪流 |
| 未決事項的技術落實 | ❌ 沒有相關的 Open Decision |
| 換維度／換引擎的切割線 | ❌ 回合制與維度無關 |

**三項全不符合。**

SPEC v0.1 §6.4 列出這個介面，理由是「未來可換個體行動序」。
但「未來也許會換」不符合本檔的判準，也不符合 SPEC v0.1 自己
對 `IGridTopology` 說的 YAGNI 標準。

> **建議：不要建立 `ITurnOrder`。**
> 直接把陣營輪流寫成具體邏輯。真的要換行動序時再抽介面，
> 那時你會更清楚介面該長什麼樣。
>
> **這是一個架構建議，不是既有文件的決定。**
> 已登錄在 [DOCUMENT-MAP §3.3](../DOCUMENT-MAP.md#33-有-architecture-但沒有對應需求的部分)
> 的「過度設計候選」清單。若企劃／程式認為應該保留，請說明理由並更新本檔。

---

## 5. 表現層邊界（最大的一條接縫）

**這不是一個介面，是一整個 assembly 邊界。**

| 判準 | 符合？ |
|---|---|
| 換維度／換引擎的切割線 | ✅ **這就是那條線** |

[prototype-charter §5](../01-vision/prototype-charter.md)：
之後要換 2D／2.5D／HD-2D 甚至換引擎，等 Prototype 驗證玩法後再說。

因此：

> **`Ediki.Unity` 整層被視為可拋棄品。**
> 換維度時它會被重寫，`Ediki.Core` 一行不動。

強制手段：`Ediki.Core.asmdef` 的 **No Engine References**（編譯期）＋ 架構測試 **A1**。

見 [ADR-0001](../07-adr/ADR-0001-core-unity-assembly-split.md) 與
[ADR-0005](../07-adr/ADR-0005-grayblock-3d-prototype-shell.md)。

---

## 6. 明確**不留**的接縫

記在這裡，避免有人好心加回來。

| 不留 | 為什麼 |
|---|---|
| 序列化框架抽象層（MemoryPack 等） | SPEC v0.1 §6.7 明確「prototype 階段先手寫」。**目前沒有第二個實作** |
| 存檔系統介面 | Prototype 非目標 |
| 技能／狀態效果系統 | Stage 01 沒有技能也沒有狀態（[R-COMBAT-20..22](../03-spec/SPEC-combat.md)） |
| 污染系統的擴充點 | 明確非目標。**GDD 有這個系統不代表 Prototype 要為它預留位置** |
| 多人／網路 | 從未被提及 |
| 音效／本地化 | 明確非目標 |

> **「GDD 裡有」不是「架構要預留」的理由。**
> 這是本專案最容易犯的錯：GDD 描述的是一款 20 關、4 角色、
> 有污染系統與四魔將的完整遊戲。**Prototype 只驗證一個假說。**
> 為 GDD 的完整內容預留架構 = 為一個不存在的需求付出成本。
