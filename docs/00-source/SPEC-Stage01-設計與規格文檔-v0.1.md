# 《穢土紀》Stage 01 Prototype — 設計與規格文檔

**版本** v0.1（草案，含待決清單）
**範圍** Unity 6 戰棋 prototype，只做 Stage 01
**目的** 驗證 AP 系統手感。不是做遊戲，不是做關卡，是做一台可測量的實驗機器。

---

## 0. 這份文件怎麼讀

| 讀者 | 必讀 | 可略過 |
|---|---|---|
| **企劃** | 第 1、2、3、4、5、8 章 | 第 6、7 章（技術） |
| **程式** | 全部 | — |
| **兩邊一起看** | 第 1 章（目標）、第 8 章（待決清單） | — |

第 8 章的每一項都需要有人拍板。**沒拍板之前程式不能開工的項目已標記 `[BLOCKER]`。**

---

## 1. 目標與非目標

### 1.1 我們要驗證的事

> 「AP 8、攻擊 5、移動 1/格」這組規則，能不能讓玩家每回合都在做**有意義的空間決策**？

具體來說，Stage 01 要證明一件事：

> **玩家該煩惱的不是「我這回合打誰」，而是「我這回合結束時站在哪裡、有幾個敵人打得到我」。**

### 1.2 非目標（明確不做）

- 不做四魔將、不做 20 關、不做裝備演進、不做飾品、不做污染系統
- 不做美術。灰盒方塊、純色、文字標籤
- 不做存檔 UI、不做設定選單、不做音效
- 不做六角格（但介面要留）

### 1.3 成功的定義

Prototype 成功 = 我們能用數據回答第 7 章那五個問題。不是「好玩」，是「可測量」。

---

## 2. 核心設計命題：Exposure（接觸面）

這是整份文件的軸心，企劃和程式都要理解同一個定義。

### 2.1 定義

> **Exposure（暴露度）= 你所站的格子，有幾個「可通行且敵人能站上去」的相鄰格。**

方格四鄰接、近戰射程 1，所以 Exposure 直接等於**同一回合最多有幾隻敵人能打到你**。

| Exposure | 地形情境 | 同時被打 |
|---|---|---|
| 4 | 開闊地 | 4 |
| 3 | 靠一面牆 | 3 |
| 2 | 凹角 | 2 |
| 1 | 死巷、1 寬走廊 | 1 |

### 2.2 為什麼是這個

戰棋難度的真正槓桿是「每回合有多少敵人能實際攻擊到你」，不是地圖上敵人總數。這一點在兩份獨立研究裡都指向同一結論——瓶頸讓少數單位守住通道，強迫敵人通過瓶頸永遠對玩家有利。Into the Breach 的設計師也講過同一件事的反面：多一個敵人就能把一場戰鬥從「有趣挑戰」變成「完全不可能」，因為 8x8 的小地圖把接觸面壓到極小，邊際影響被放大。

對我們的意義很直接：**Stage 01 的難度旋鈕不是敵人數量，是地圖幾何。**

### 2.3 Exposure 是一級概念

不是分析用的比喻，是要進系統的東西：

- **UI 必須顯示**：滑鼠停在任何一格，顯示該格 Exposure 與「目前有幾隻活著的敵人的威脅範圍涵蓋這格」
- **AI 必須評估**：敵方 AI 選擇站位時要能讀這個值
- **測試必須統計**：自動化跑分要記錄「玩家每回合結束時的 Exposure 分布」

---

## 3. 戰鬥規則規格

> 本章不含程式碼，企劃可直接讀。所有數字都是**資料**，不寫死在程式裡。

### 3.1 回合與 AP

- 陣營輪流制：玩家全體行動 → 敵方全體行動 → 下一回合
- 每個單位每回合 **8 AP**，回合開始時重設（**不跨回合保留**，見 8.4）
- 玩家可自由決定移動與行動的順序、可交錯（移動 → 攻擊 → 移動）
- 敵方 AI 固定「先移動後行動」

| 動作 | AP 成本 |
|---|---|
| 移動 | 該格地形成本（見 3.2） |
| 攻擊 | 5 |
| 防禦 | 3 |
| 道具 | 2 |

**AP 8 的組合空間（攻擊 5 的情況下）：**

| 組合 | AP | 備註 |
|---|---|---|
| 攻擊 + 移動 3 格（道路） | 5+3 | 主流回合 |
| 攻擊 + 防禦 | 5+3 | 守線回合，剛好用完 |
| 攻擊 + 道具 + 移動 1 格 | 5+2+1 | |
| 純移動 4 格 | 4 | 受 MOVE 上限，浪費 4 AP |
| 攻擊 × 2 | 10 | **做不到** |

**這是一個已知風險。** 攻擊佔 8 AP 的 62%，多數回合會塌成「移動 + 攻擊」——這是 AP 制的典型失敗模式。AP 制要產生決策空間需要三個條件：(a) 多個成本相近、彼此競爭的動作；(b) 沒用完的 AP 有意義；(c) 單一動作不吃掉超過半數預算。**我們目前三條都不滿足。**

處理方式見 8.1（攻擊成本要不要降到 4）。

### 3.2 移動與地形

**`[BLOCKER]` 規格矛盾必須先解決。** 原始規格同時寫了「移動 1 AP/格（受 MOVE 上限）」和「地形成本 道路 1 / 碎石 2 / 森林 2 / 高地 3」，兩者互相打架。

**本文件採用讀法 (A)：進入一格消耗該格的地形成本 AP；MOVE 是該回合可移動的「格數」上限。** 兩個限制同時生效，取較嚴格者。

| 地形 | AP 成本 | 備註 |
|---|---|---|
| 道路 | 1 | |
| 碎石 | 2 | |
| 森林 | 2 | |
| 高地 | 3 | |
| **阻擋** | **不可進入** | **新增，見 8.2** |

**推導出來的實際移動力：**

| 情境 | 桃太郎（MOVE 4） | 小耗（MOVE 3） |
|---|---|---|
| 攻擊回合（剩 3 AP）走道路 | 3 格 | 3 格 |
| 攻擊回合走碎石／森林 | 1 格（浪費 1 AP） | 1 格 |
| 攻擊回合走高地 | 1 格 | 1 格 |
| 純移動回合走道路 | 4 格（MOVE 上限，浪費 4 AP） | 3 格（MOVE 上限） |
| 純移動回合走森林 | 4 格（8 AP 剛好） | 3 格（6 AP） |
| 純移動回合走高地 | 2 格（6 AP） | 2 格（6 AP） |

**兩個要注意的結果：**

1. **桃太郎的 MOVE 4 在任何攻擊回合都用不到**（5 + 4 = 9 > 8）。攻擊回合他和小耗一樣只能走 3 格，所以**他無法風箏**。MOVE 4 只在純移動回合有差別。
2. **任何非道路地形都把攻擊回合的移動壓成 1 格**，雙方一樣。目前地形只會「拖慢」，不會製造雙方的不對稱。

### 3.3 威脅範圍與敵人啟動

- **威脅範圍** = 敵人能在同一回合內移動到並攻擊的所有格子 = 「路徑成本 ≤ (8 − 攻擊成本) 且格數 ≤ MOVE 的可達格」再向外擴 1 格射程
- **敵人在玩家進入其威脅範圍前不啟動**
- **威脅範圍必須對玩家可見**（一鍵顯示全體危險區，Fire Emblem 的 danger zone 做法）

小耗攻擊後剩 3 AP，所以：

| 路徑地形 | 小耗威脅範圍 |
|---|---|
| 全道路 | **4**（移 3 + 射程 1） |
| 經過一格森林／碎石 | **2** |
| 經過高地 | **2** |

> **一格森林等於把小耗的威脅圈從 4 砍到 2。** 這是地形在 Stage 01 的真正價值：讓玩家一次只拉一隻。

**已知風險：** 「進入威脅範圍才啟動」的著名失敗模式是「逐一釣怪變成唯一最佳解」（XCOM pod 的長年批評）。Stage 01 作為教學關，逐一釣怪**就是我們要教的東西**，所以這關不處理；但要記在後續關卡的風險清單上，用時間壓力或目標設計去破。

### 3.4 傷害與命中

**傷害公式**（確定）：

```
傷害 = ATK × 100 / (100 + DEF)，無條件捨去
```

這是除法減傷，沒有「懸崖」、每點 DEF 邊際遞減、永遠不會把傷害降到 0。DEF 的實際減傷率：

| DEF | 減傷 |
|---|---|
| 20 | 16.7% |
| 50 | 33.3% |
| 100 | 50% |
| 200 | 66.7% |

**這條公式有一個必須知道的副作用**：因為 DEF 是遞減的，**「防禦」動作絕對不能定義成 DEF buff**。把 DEF 從 50 加倍到 100，傷害只從 66 掉到 50（−24%）；要把傷害砍半得把 DEF 加到 203。所以：

> **防禦（3 AP）必須定義成「本回合受到的傷害 × 係數」的直接乘數，不是 DEF 加成。** 見 8.3。

**命中公式**（待決）：

```
命中率 = HIT × (1 − EVA)
```

這是乘法式，命中率恆在 [0, HIT] 之間，不會爆表或變負。但**這是整份規格最需要拍板的地方**，見 8.5 與第 4 章。

### 3.5 勝敗條件

- **勝**：全滅敵人（4 隻小耗）
- **負**：桃太郎 HP 歸零

---

## 4. 數值封包

**現行 GDD 數值直接拿來跑，Stage 01 是打不贏的。** 這不是玩家技術問題，是數值問題，先講清楚：

- 桃太郎每擊 50 傷，小耗 80 HP → 需 2 次命中，命中率 0.704 → **平均 2.84 回合殺一隻，清場 11.4 回合**
- 小耗每擊 66 傷，命中率 0.48 → **每隻每回合期望 31.7 傷**
- 就算站在完美的 1 寬走廊（Exposure 1）：11.4 × 31.7 = **360 傷 > 300 HP，還是輸**

所以必須改數值。下面是兩個**內部自洽**的封包，各自對應一種設計哲學。**選一個，不要混。**

### 4.1 封包 1：隨機命中（貼近現行 GDD）

保留 HIT/EVA，只動一個數字。

| 單位 | HP | ATK | DEF | MOVE | HIT | EVA |
|---|---|---|---|---|---|---|
| 桃太郎 | 300 | 60 | 50 | 4 | 0.80 | 0.20 |
| 小耗 | **50**（原 80） | 100 | 20 | 3 | 0.60 | 0.12 |

- 桃 → 小：50 傷 → **一擊必殺**
- 小 → 桃：66 傷
- 桃命中 0.704 / 小命中 0.48
- 清場期望 **5.7 回合**

| Exposure | 累計承傷 | 結果 |
|---|---|---|
| 1 | 180 | **勝，剩 120 HP（40%）** |
| 2 | 315 | 一線之差落敗 |
| 3 | 405 | 明確落敗 |
| 4 | 450 | 約第 2.4 回合倒 |

**優點**：只改一個數字，貼近 GDD。一擊必殺讓傷害預覽變成二元、好讀。
**缺點**：命中骰帶來的挫折感。一次 miss 就可能翻盤，而 prototype 樣本小、玩家會歸因到運氣而非設計。

### 4.2 封包 2：完全資訊（攻擊必中）

移除命中骰，把不確定性完全拿掉。

| 單位 | HP | ATK | DEF | MOVE |
|---|---|---|---|---|
| 桃太郎 | 300 | 60 | 50 | 4 |
| 小耗 | 80 | **50**（原 100） | 20 | 3 |

- 桃 → 小：50 傷 → 80 HP 需 **2 刀**
- 小 → 桃：**33 傷** → 300 HP 撐 **9 刀**
- 清場**剛好 8 回合**（全部可心算）

| Exposure | 每回合承傷 | 結果 |
|---|---|---|
| 1 | 33 | **勝，剩 36 HP（12%）** |
| 2 | 66 | 第 5 回合倒 |
| 3 | 99 | 第 3–4 回合倒 |
| 4 | 132 | 第 3 回合倒 |

**優點**：跟《穢土紀》原本就有的確定性設計（污染擴散、瀕死 15% 閾值）哲學一致。玩家失敗時無法怪黑箱，失敗焦點從「我沒猜對」變成「我的策略不夠好」。所有數字可心算 → 完全資訊 → 每場戰鬥變成謎題。而且**規則層完全不需要亂數**，確定性測試變得極簡。
**缺點**：偏離 GDD 的 HIT/EVA 設定；小耗 ATK 從 100 砍到 50 是大改。

### 4.3 建議

**Prototype 先做封包 2，把封包 1 當 A/B 變體。**

理由：prototype 的目的是驗證 AP 手感，命中骰是干擾變數——玩家分不清「這回合難受」是 AP 設計爛還是骰子爛。先在無雜訊環境下量 AP 手感，確認之後再把隨機性加回去測第二次。

架構上兩個封包都要能跑（見 6.4 的 `IRandomSource`），切換只改資料。

### 4.4 平衡方法：用 TTK 不用單發傷害

回合制平衡要看「幾次攻擊能擊殺」（TTK），不是單發傷害。乘法 buff 在除法公式下的實際加成會隨目標 DEF 浮動——`ATK × 1.3` 不等於最終傷害 +30%。**所有數值調整都要用 TTK 表複驗。**

---

## 5. Stage 01 地圖規格

### 5.1 設計意圖

一張圖同時提供三種 Exposure 情境，讓玩家自己撞出結論：

| 區域 | Exposure | 教什麼 |
|---|---|---|
| 隘口內側 | 1 | 正解 |
| 高地誘餌 | 4 | 「視野好」不等於「安全」 |
| 隘口北側開闊地 | 4 | 衝動的代價 |

### 5.2 灰盒地圖（12 × 10，起始版本）

```
       0   1   2   3   4   5   6   7   8   9  10  11
  0    #   #   #   #   #   #   #   #   #   #   #   #
  1    #   .   .   .   1   .   .   2   .   .   .   #
  2    #   .   f   f   .   .   .   .   f   f   3   #
  3    #   .   f   .   .   .   .   .   .   f   4   #
  4    #   #   #   #   #   ▓   #   #   #   #   #   #
  5    #   .   .   r   #   ▓   #   r   .   .   .   #
  6    #   .   f   f   .   .   .   f   f   .   .   #
  7    #   .   .   .   .   ^   .   .   .   .   .   #
  8    #   .   .   P   .   .   .   .   .   .   .   #
  9    #   #   #   #   #   #   #   #   #   #   #   #

  #  阻擋      .  道路(1)    f  森林(2)
  r  碎石(2)   ^  高地(3)    ▓  隘口（道路）
  P  桃太郎起點            1-4  小耗起點
```

### 5.3 關鍵格位

| 格 | 說明 |
|---|---|
| **(5,5) 黃金格** | 唯一的敵方鄰格是 (5,4)。**有效 Exposure = 1。這是唯一穩定的勝利解。** |
| (5,4) 隘口 | 敵人必經。1 寬。 |
| (5,7) 高地 | 誘餌。四面通行 → Exposure 4。走上去要 3 AP。 |
| (3,8) 起點 | 到黃金格 7 格 / 7 AP → 需 2 個純移動回合 |

### 5.4 這張圖依賴的規則

- **`[BLOCKER]` 單位阻擋移動**：敵人不能穿過己方或敵方單位。沒有這條，走廊擋不住任何東西。見 8.2。
- **阻擋地形存在**：高成本地形只能「拖慢」，不能「限制鄰接數」。只有不可通行的幾何才能把 Exposure 壓到 4 以下。

### 5.5 地形戰鬥修正（可選，預設關閉）

目前地形只影響移動成本，不影響戰鬥。業界慣例是**修命中而不是修防禦**（Fire Emblem 森林給 +1 DEF 與最多 +20 迴避；Engage 直接給樹林 +30 迴避；Battle Brothers 從高處攻擊 +10% 命中、從低處往上打每級 −10%）。

若要加，建議這組，全部進資料、預設關：

| 地形 | 效果 |
|---|---|
| 高地 | 站在此格：攻擊者 HIT −10% |
| 森林 | 站在此格：EVA +10% |

**採用封包 2（必中）時這組自動失效**，屆時改用「站高地：受到傷害 −20%」之類的乘數版本。

### 5.6 明確不做的：圍攻加成

Battle Brothers 的做法是第一隻之後每多一隻相鄰敵人給攻擊方 +5% 命中。**Stage 01 不加。** 我們只有 4 隻敵人、Exposure 4 已經是必死，再加只是死更快，教學價值為零。留到後續關卡。

---

## 6. 技術架構

> 本章給程式。企劃可跳到第 7 章。

### 6.1 環境事實（已核對官方文件）

| 項目 | 值 |
|---|---|
| Unity | 6.0 或 6.3 LTS（6000.0 / 6000.3） |
| C# 語言版本 | **9.0**（Unity 6.0–6.3 全系列皆為 C# 9.0） |
| Scripting runtime | Mono / IL2CPP |
| API 相容層 | .NET Standard 2.1 |

**不可用的語言特性**（會編譯失敗）：

- `record` — 需手動宣告 `IsExternalInit`，且 Unity 序列化系統不支援 record
- `init` setter、`required` members、file-scoped namespace、collection expressions、global using

CoreCLR / .NET 10 / C# 14 是 Unity 6.8 的計劃，**現在不能依賴**。

> **結論：`BattleState` 用一般 class，不要用 record。**

### 6.2 Assembly 分層

```
Assets/Scripts/
├── Core/            Ediki.Core.asmdef        ← 勾選 No Engine References
├── Unity/           Ediki.Unity.asmdef       ← 引用 Core
└── Editor/          Ediki.Editor.asmdef
Assets/_Project/Tests/
└── EditMode/        Ediki.Tests.EditMode.asmdef
```

`Ediki.Core.asmdef` 的 **No Engine References** 選項會讓「Core 零 UnityEngine」變成**編譯期強制**，而不只是紀律——誤 `using UnityEngine` 直接編譯失敗。這比用測試掃引用更硬。

Core 內不能用 `Vector2Int`，自訂：

```csharp
public readonly struct Coord { public readonly int X, Y; }
```

**額外紅利**：因為 Core 零引擎依賴，可以另外維護一份 .NET Standard 2.1 的 `.csproj`，用 `dotnet test` 在 Unity 外跑確定性測試，CI 秒級回饋，不用開 Editor。（此為社群通用做法，非 Unity 官方文件明載，需維護兩套建置。）

### 6.3 單一漏斗

```
Command  →  Validate  →  Effect[]  →  Apply
```

```csharp
public readonly struct ExecuteResult
{
    public readonly BattleState State;
    public readonly EffectLog   Log;
    public readonly bool        Ok;
    public readonly string      RejectReason;
}

public static class BattleSimulator
{
    // 純函式：同輸入同輸出、無副作用、不改動傳入的 state
    public static ExecuteResult Execute(BattleState state, ICommand command);
}
```

**Command 清單（Stage 01）**

| Command | 參數 |
|---|---|
| `MoveCommand` | unitId, path |
| `AttackCommand` | attackerId, targetId |
| `DefendCommand` | unitId |
| `UseItemCommand` | unitId, itemId, targetId |
| `EndTurnCommand` | factionId |

**Effect 清單（Stage 01）**

| Effect | 欄位 |
|---|---|
| `ApSpent` | unitId, amount, remaining |
| `UnitMoved` | unitId, from, to, path |
| `AttackResolved` | attackerId, targetId, hit, roll |
| `HpChanged` | unitId, delta, newHp |
| `UnitDied` | unitId |
| `DefendApplied` | unitId, multiplier, expiresAtTurn |
| `FactionActivated` | factionId, triggeredByUnitId |
| `TurnStarted` / `TurnEnded` | turnIndex, factionId |
| `BattleEnded` | outcome |

**粒度原則**：一個 Effect = 表現層可獨立播放的一個原子事件。一個 Command 展開成有序的多個 Effect，順序即因果順序。規則層在瞬間求值時就決定好完整 log，**表現層不做任何判斷，只按序播**。

**攻擊 Command 的展開範例：**
```
[ ApSpent(5), AttackResolved(hit=true), HpChanged(-50), UnitDied ]
```

### 6.4 刻意留軟的接縫

```csharp
public interface IGridTopology
{
    IEnumerable<Coord> Neighbors(Coord c);   // 回傳順序必須固定
    int  Distance(Coord a, Coord b);
    bool Contains(Coord c);
    IEnumerable<Coord> AllCoords();
}

public interface ITurnOrder { /* 陣營輪流；未來可換個體行動序 */ }

public interface IRandomSource
{
    int NextInt(int exclusiveMax);   // 整數，不用 float
}
```

- **`IGridTopology`**：現在只實作 `SquareGrid4`。**六角格不要實作**，留介面就好（YAGNI）。未來要換：cube 座標給演算法、axial 給儲存。
- **`IRandomSource`**：兩個實作 — `AlwaysHitSource`（封包 2，回傳固定值）與 `SeededPcgSource`（封包 1）。**切換數值封包只換這個實作 + 資料表。**
- **AP 成本、地形成本、單位數值全部進 ScriptableObject / JSON**，Core 只讀資料。

### 6.5 移動範圍 vs 尋路（不要搞混）

| 用途 | 演算法 | 理由 |
|---|---|---|
| 玩家移動範圍、威脅範圍 | **Dijkstra flood fill** | 要「所有可達格 + 成本」，目標未知 |
| AI 點對點路徑 | **A\*** | 目標已知，有啟發函數，較快 |

地形成本進 edge weight，不寫死在拓撲裡。**Dijkstra 的展開順序必須確定**（見 6.6）。

### 6.6 確定性三戒

| 戒 | 內容 | 為什麼 |
|---|---|---|
| **一** | 規則層只用整數。命中用整數 RNG（0–99）比較整數化命中率 | float 跨平台/跨編譯設定不保證位元一致 |
| **二** | 任何影響模擬或雜湊的迭代，不得依賴 `Dictionary` / `HashSet` 的列舉順序。改用 `List` 或先排序 key | .NET 不保證 hashmap 列舉順序 |
| **三** | 世界狀態雜湊**絕不用**內建 `GetHashCode()`。序列化成 canonical bytes 後算 FNV-1a 或 SHA-256 | .NET Core 的 `string.GetHashCode()` 有 randomization，同字串跨 process 不同值 |

**採用封包 2（必中）時，Core 根本不需要亂數**，戒一自動滿足，確定性測試變成純粹的「同指令串 → 同雜湊」。這是選封包 2 的隱藏技術紅利。

### 6.7 深複製與序列化

| 用途 | 做法 |
|---|---|
| **深複製**（AI 推演、傷害預覽、undo） | 手寫 `Clone()`，配一條「Clone 隔離」測試 |
| **狀態雜湊** | 手寫 canonical `BinaryWriter` → FNV-1a |
| **存檔 / 除錯 dump**（可延後） | Newtonsoft（`com.unity.nuget.newtonsoft-json`） |

- **不要用 `JsonUtility`**：不支援 Dictionary、不支援多型／介面。
- **不要用 record + `with`**：`with` 只做淺複製，巢狀 `List<Unit>` 會被兩個 state 共用，AI 推演會污染真實狀態。加上 Unity 6 的 record 限制，直接排除。
- **框架選項（不急）**：若手寫 Clone 太容易漏，MemoryPack（Cysharp，source generator、無反射、IL2CPP 友善）往返可當深複製，官方宣稱 Unity 下比 JsonUtility 快 3–10 倍。**但 prototype 階段先手寫。**

### 6.8 白送的兩個功能

單一漏斗 + 可深複製狀態一做完，這兩個是免費的，**建議做**：

- **Undo**：戰棋的老痛點（誤點格子、忘記某次攻擊會暴露自己）。存 state 快照或反向重跑指令串即可。
- **Replay**：只記錄 seed + 指令串就能重跑整場，是除錯 AI 與確定性 bug 最便宜的工具。

### 6.9 表現層

規則層瞬間算完 → 回傳 `EffectLog` → 表現層攤成時間軸播放。表現層**只讀 EffectLog**，不查詢規則層、不做判斷。這條線就是 Core / Unity 的 assembly 邊界，由 6.2 的 asmdef 強制。

---

## 7. 測試與驗收

### 7.1 架構回歸測試（EditMode）

| 編號 | 斷言 |
|---|---|
| A1 | `Ediki.Core` 的任何型別不引用 `UnityEngine` / `UnityEditor` |
| A2 | `Execute(state, cmd)` 呼叫前後，傳入的 `state` 物件雜湊不變（純函式） |
| A3 | `state.Clone()` 後改動副本，原本不受影響（深複製隔離；含所有巢狀集合） |
| A4 | 同 seed + 同指令串 → 同世界狀態雜湊（golden hash，常數比對） |
| A5 | AP 成本、地形成本、單位數值皆從資料讀取；程式碼中不得出現這些字面值 |
| A6 | `IGridTopology.Neighbors` 的回傳順序在同輸入下固定 |
| A7 | 表現層組件不得反向引用 Core 的可變狀態（只能讀 EffectLog） |

### 7.2 手感驗收指標

因為 `Execute` 是純函式可 seed，這些**全部可以自動化跑 1000 場統計**，不用手動 playtest。這是強架構在這個專案的最大回報。

| 指標 | 目標 | 觸發改設計的門檻 |
|---|---|---|
| **同一動作組合佔比** | < 70% | ≥ 70% 表示決策空間塌陷，必須調 AP 成本 |
| **殘 AP 浪費率** | < 15% | 持續 > 25% 表示 AP 粒度或成本錯了 |
| **平均戰鬥回合數** | 6–10 | < 4 太短沒決策空間，> 15 拖沓 |
| **回合結束 Exposure 分布** | 熟練玩法應收斂到 1–2 | 若 Exposure 與勝負無相關，核心命題失敗 |
| **勝率（固定策略）** | 走廊策略 > 80%，衝鋒策略 < 20% | 兩者接近表示地圖沒教到東西 |

### 7.3 五個要回答的問題

Prototype 結束時要能回答：

1. 攻擊 5 AP 下，玩家每回合真的有 ≥ 3 個有意義的選項嗎？
2. 玩家會不會自己發現「站進走廊」這件事？花幾回合發現？
3. Exposure 是不是勝負的主要解釋變數？
4. 命中骰（封包 1）有沒有讓玩家把失敗歸因到運氣？
5. 桃太郎 MOVE 4 有沒有存在感？（目前推算是沒有）

---

## 8. 待決清單

> **每一項都需要拍板。標記 `[BLOCKER]` 的沒定案程式不能開工。**

### 8.1 攻擊成本要不要從 5 降到 4？`[BLOCKER]`

| 路線 | 內容 | 影響 |
|---|---|---|
| **A 維持 5** | 1 攻擊 + 3 點移動／防禦 | 決策空間窄，但焦點清晰：「這回合站哪」。與本文件的 Exposure 主題最一致 |
| **B 全體降 4** | 雙方都能一回合 2 攻擊 | 決策空間變寬，但雙方輸出翻倍、戰鬥變短變刺、平衡難度上升 |
| **C 不對稱** | 桃太郎 4、小耗 5 | 玩家有 combo 空間，敵人維持單發。per-unit AP 成本本來就進資料，架構免費 |

**建議：先做 A 當基準線，把 B/C 當變體跑同一套自動化指標比較。** 這件事本身就是 prototype 要驗證的東西，不該在文檔階段猜答案。

### 8.2 三個規則缺口 `[BLOCKER]`

| 缺口 | 建議 |
|---|---|
| 移動成本讀法矛盾 | 採 3.2 的讀法 (A) |
| 沒有阻擋地形 | 新增 `Blocked`（不可進入）。**沒有這個做不出走廊** |
| 沒定義單位阻擋 | 單位阻擋移動，不可穿越。**沒有這個走廊擋不住人** |

### 8.3 防禦（3 AP）的效果是什麼？`[BLOCKER]`

規格完全沒定義。**必須是傷害乘數，不能是 DEF 加成**（理由見 3.4）。

建議：`本回合受到的傷害 × 0.5，持續到下個己方回合開始`。

參考：封包 2、Exposure 1 下，防禦讓每回合承傷從 33 降到 17，8 回合總傷 132，玩家能剩 56% HP 過關——這會讓「攻擊 + 防禦」成為走廊裡的標準回合，剛好用滿 8 AP。

### 8.4 剩餘 AP 要不要跨回合保留？

目前規格是「不保留」。AP 制產生深度的條件之一是「沒用完的 AP 有意義」。DOS2 的收斂做法是每回合恢復 4、上限 6、最多帶 2 點到下回合。

**建議 Stage 01 先不保留**（少一個變數），但把它列為 8.1 之後的第二個實驗。

### 8.5 命中：封包 1 還是封包 2？`[BLOCKER]`

見第 4 章。**建議先做封包 2（必中）**，理由是命中骰會污染 AP 手感的測量。

補充一個心理學面向：損失趨避的估計係數約 λ≈2.25（Tversky & Kahneman 1992），錯過一次高命中攻擊的痛感遠大於閃過一次低命中攻擊的爽感。Fire Emblem 的 2RN 就是為此存在的「善意謊言」，XCOM 則是在 Legend 以外的難度偷偷幫玩家。**如果最後決定保留隨機命中，就要一併決定要不要做這種補正**——但那又和「確定性核心」的哲學打架。這是一組要一起下的決定，不能分開。

### 8.6 地形戰鬥修正要不要做？

見 5.5。**建議先不做**，先確認純幾何（Exposure）能不能撐起手感。地形修正是第二層調味，加太早會混淆歸因。

---

## 附錄 A：名詞對照

| 本文用語 | 別稱 | 定義 |
|---|---|---|
| Exposure | 接觸面 / engagement surface | 所站格子的可通行相鄰格數 = 同時能打到你的敵人數上限 |
| 威脅範圍 | danger zone / threat range / ZOC | 敵人能在同一回合移動到並攻擊的範圍 |
| EffectLog | combat log / event stream | 規則層算完後回傳給表現層的有序事件串 |
| 黃金格 | — | Exposure 1 且在攻擊距離內的格子 |
| TTK | time to kill | 幾次攻擊能擊殺，回合制平衡的正確單位 |

---

## 附錄 B：主要參考來源

**設計**
- Matthew Davis, *Into the Breach Design Postmortem*, GDC 2019（完全資訊、8x8 手工地圖、難度懸崖、用約束來設計）
- *Road to the IGF: Subset Games' Into the Breach*, Game Developer（Justin Ma 訪談）
- *The Metrics of Space: Tactical Level Design*, Game Developer（瓶頸與接觸面）
- Fire Emblem Wiki — True hit（2RN）、Terrain（森林 +1 DEF / +20 迴避）
- Battle Brothers Wiki — Combat Mechanics（圍攻 +5%/隻、高低差 ±10%）
- Divinity Wiki — Original Sin 2 Action Points（4 起手 / 上限 6 / 帶 2）
- Ian Schreiber & Brenda Romero, *Game Balance*, CRC Press 2021
- Tversky & Kahneman, *Advances in Prospect Theory*, 1992（λ≈2.25）

**技術**
- Unity Manual 6000.0 / 6000.3 — C# compiler and language version reference（C# 9.0）
- Unity Manual — Assembly Definition properties（No Engine References）
- Unity Manual — Edit mode and Play mode tests
- Robert Nystrom, *Game Programming Patterns* — Command（undo / replay / 網路）
- The Liquid Fire — Tactics RPG 系列（state machine、path finding：flood fill vs A\*）
- Red Blob Games — Hexagonal Grids（cube 給演算法、axial 給儲存）
- Rads and Relics — Command Systems in Games
- Cysharp/MemoryPack README

**限制說明**
- 第 4 章的回合數與承傷推算，基於「每回合各單位攻擊一次」的簡化模型，未計入移動、卡位與變異數。實際數字要用 7.2 的自動化跑分複驗。
- 「Core 用 `dotnet test` 在 Unity 外跑」為社群通用做法，非 Unity 官方文件明載。
- Unity CoreCLR / C# 14 的時程為官方路線圖之計劃，非已交付事實。
