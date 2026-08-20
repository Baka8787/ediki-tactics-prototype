# ADR-0001 — Core / Unity assembly 分離，Core 勾選 No Engine References

**Status**：`Accepted`
**Date**：2026-08-13
**來源**：SPEC v0.1 §6.2

---

## Context

[prototype-charter §5](../01-vision/prototype-charter.md) 記載了專案負責人的環境指示：

> 環境暫定 UDP 3D，之後要換 2D、2.5D、HD-2D 甚至換引擎都是等 Prototype 驗證玩法後再說。

同時 [prototype-charter §3](../01-vision/prototype-charter.md) 定義成功標準是
「能用數據回答五個問題」，這推導出必須能自動化跑 1000 場。

兩件事指向同一個結構需求：**戰鬥規則必須能離開 Unity 獨立存在。**

## Problem

「規則層不要引用 UnityEngine」如果只是一條**紀律**，它一定會被破壞：

- 有人為了方便用 `Vector2Int`
- 有人為了 log 用 `Debug.Log`
- 有人為了資料用 `ScriptableObject`
- 有人為了隨機用 `UnityEngine.Random`

每一次破壞單獨看都很小，但累積起來會讓「換引擎」與「Unity 外跑測試」
從「重寫表現層」變成「重寫整個專案」。

## Options

### A. 靠紀律 + Code Review
成本最低，但**必定失效**。兩人團隊、有 AI 協作，review 覆蓋不到每一行。

### B. 靠測試掃描引用
寫一條測試檢查 Core 的型別不引用 UnityEngine。
比紀律硬，但**回饋太晚**（要跑測試才知道），而且測試本身可能被繞過或被停用。

### C. asmdef 分離 + Core 勾選 No Engine References
Unity 的 Assembly Definition 提供 **No Engine References** 選項，
勾選後該 assembly 完全無法引用 UnityEngine。

## Decision

**採 C，並保留 B 作為第二層防護。**

```
Assets/Scripts/
├── Core/            Ediki.Core.asmdef        ← 勾選 No Engine References
├── Unity/           Ediki.Unity.asmdef       ← 引用 Core
└── Editor/          Ediki.Editor.asmdef
Assets/_Project/Tests/
└── EditMode/        Ediki.Tests.EditMode.asmdef
```

依賴方向：`Unity → Core`，**單向**。

## Rationale

**No Engine References 讓「Core 零 UnityEngine」變成編譯期強制，而不只是紀律。**
誤 `using UnityEngine` 直接編譯失敗 —— 這比用測試掃引用更硬，回饋也更即時。

保留架構測試 **A1** 作為第二層，是因為 asmdef 設定可能被誤改，
測試能在設定漂移時發出警報。

## Consequences

### 正面

- **換引擎時 Core 一行不動。** 這是 [OD-15](../OPEN-DECISIONS.md#od-15) 保留選項的技術前提
- **可以在 Unity 外用 `dotnet test` 跑確定性測試**，CI 秒級回饋，1000 場跑分不用開 Editor
  （社群通用做法，非 Unity 官方文件明載；代價是要維護兩套建置）
- 表現層可以被整層丟棄重寫，不影響規則正確性

### 負面 / 代價

- **Core 內不能用 `Vector2Int`**，必須自訂 `Coord`（[R-GRID-03](../03-spec/SPEC-grid-terrain.md)）
- **Core 內不能用 `ScriptableObject`** → 直接影響 [OD-11](../OPEN-DECISIONS.md#od-11)：
  若資料正本是 ScriptableObject，Core 就只能收轉換後的純資料，
  而且「Unity 外跑測試」的紅利會消失
- **Core 內不能用 `Debug.Log`** —— 除錯輸出需要自己的抽象
- asmdef 增加編譯單元，小專案下編譯時間影響可忽略

### 對其他決策的影響

- [ADR-0005](ADR-0005-grayblock-3d-prototype-shell.md) 依賴本 ADR
- [OD-11](../OPEN-DECISIONS.md#od-11)（資料格式）的張力**完全來自本 ADR**
- 架構測試 A1、A5、A7 都在守這條線
