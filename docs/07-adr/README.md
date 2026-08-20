# Architecture Decision Records

**ADR 回答一個問題：Why did we choose this?**

不是「是什麼」（那是 Architecture），不是「必須怎樣」（那是 Specification）。

---

## 什麼時候建立 ADR

**只有**滿足以下條件時：

- 影響整體架構
- 跨多個系統
- 難以逆轉
- 存在明顯 trade-off
- **未來工程師需要知道決策原因**（不然他會想「為什麼不用 X？」）

**不要為普通 implementation detail 建立 ADR。**

判斷準則：如果新工程師看到程式碼會問「為什麼不用更簡單的 X？」，
而答案不是顯而易見的 —— 那值得一個 ADR。

## 必要欄位

每份 ADR 必須包含：

`Status` / `Context` / `Problem` / `Options` / `Decision` / `Rationale` / `Consequences`

### Status 值

| Status | 意思 |
|---|---|
| `Proposed` | 提出但未拍板 |
| `Accepted` | 已採用 |
| `Superseded by ADR-xxxx` | 被取代（**不刪除舊的**） |
| `Deprecated` | 不再適用 |
| `Blocked by OD-xx` | 等待 Open Decision |

## 編號

`ADR-nnnn-<kebab-title>.md`，四位數，**不重用**。

---

## 目前的 ADR

| # | 標題 | Status | 為什麼值得存在 |
|---|---|---|---|
| [0001](ADR-0001-core-unity-assembly-split.md) | Core/Unity assembly 分離，Core 勾選 No Engine References | `Accepted` | 這是**換引擎能力**的技術基礎。不知道理由的人會覺得「多此一舉」而破壞它 |
| [0002](ADR-0002-single-command-funnel.md) | 所有狀態變更走單一漏斗，`Execute` 為純函式 | `Accepted` | 限制很強（AI 也不能走捷徑），沒有理由說明會被繞過 |
| [0003](ADR-0003-deterministic-rule-layer.md) | 規則層只用整數 ＋ 自訂 canonical 雜湊 | `Accepted` | 「為什麼不能用 float？為什麼不能用 `GetHashCode()`？」是必然會被問的問題 |
| [0004](ADR-0004-hand-written-clone.md) | 手寫 `Clone()`，不用 record `with`、不用序列化往返 | `Accepted` | 「為什麼不用 record？」是 C# 工程師的第一直覺 |
| [0005](ADR-0005-grayblock-3d-prototype-shell.md) | Prototype 用 URP 3D 灰盒，維度／引擎路線延後 | `Accepted` | 說明**為什麼表現層被當成可拋棄品**，以及這對架構的要求 |

## 建議建立、但**現在不建立**的 ADR

以下都是真正的架構級決策，但**依賴未決事項**。
在 Open Decision 裁決之前建立它們，等於自行決定 TBD。

| 建議編號 | 主題 | 阻擋於 | 為什麼值得一個 ADR |
|---|---|---|---|
| ADR-0006 | 命中模型：完全資訊 vs 隨機命中 | [OD-05](../OPEN-DECISIONS.md#od-05) | 跨系統（規則、測試、UI、AI）、難逆轉、trade-off 極明顯、與 GDD 的相容性有長期後果 |
| ADR-0007 | 資料格式：ScriptableObject vs JSON | [OD-11](../OPEN-DECISIONS.md#od-11) | 決定「Core 能不能在 Unity 外測試」，影響整個 CI 策略 |
| ADR-0008 | 移動成本模型 | [CONFLICT-01](../CONFLICTS.md#conflict-01) | 影響 Exposure、威脅範圍、地圖設計 —— 幾乎整個 Prototype |
| ADR-0009 | AI 架構（utility 評分 vs 固定策略腳本） | [OD-10](../OPEN-DECISIONS.md#od-10) | 決定跑分數據的可信度；AI 若太強或太笨，Exposure 的相關性測不出來 |

## 明確**不需要** ADR 的事

避免 ADR 通膨。以下都是實作細節，寫在程式碼註解或 Architecture 文件即可：

- 用哪個優先佇列實作 Dijkstra
- FNV-1a vs SHA-256（兩者都符合戒三，選哪個無長期後果）
- 檔案／命名空間的組織方式
- 測試命名慣例
- 「不用 `JsonUtility`」（這是事實限制，不是選擇 —— 它不支援 Dictionary 與多型）
