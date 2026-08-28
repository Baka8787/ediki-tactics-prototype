# Ediki — 戰棋原型與自製設計工具鏈

**一個可遊玩的回合制戰棋原型，附一套自製工具：關卡編輯器、即時驗證、批次戰鬥模擬與指標分析。**

用純文字定義關卡，用 GUI 或記事本都能編輯而不互相破壞；改完立刻知道有沒有壞，
再用批次模擬回答「這張圖到底有不有趣」。

`Unity 6` · `C#` · 434 個 EditMode 測試 · 86 份關卡 · 68 個單位

![Ediki 關卡編輯器](docs/images/encounter-editor.png)

> 上圖：關卡編輯器。左側地形筆刷與單位放置、中央可旋轉的格子地圖、右側單位數值，
> 底部是**即時驗證** —— 「我方最多 4 個出戰單位，目前有 5 個」在按下試玩之前就出現。

---

## 這個專案在解決什麼

戰棋關卡的低級錯誤（單位出界、兩隻站同一格、不在 roster、出戰數超標）通常要開遊戲才會發現；
而平衡問題更慢 —— 得反覆手玩很多場才看得出傾向。結果就是**不敢做實驗**。

所以這裡的重點不只是遊戲本身，而是把「改一張圖 → 知道它壞沒壞 → 知道它有不有趣」這條路縮到最短。

## 三個設計決定

**1. 關卡是純文字，不是二進位資產。**
ASCII 地圖 ＋ spawn 行 ＋ 人寫的設計註解，全部在同一個 `.encounter.txt` 裡：

```text
# GYM LEVEL - not the narrative Stage 01.
# Lever: enemy heterogeneity only. Objective stays rout.
# Question: does threat ordering alone break the monotony?
encounter id=gym-a-hetero name=A-hetero-rout
objective type=rout
map
############
#.ff....ff.#
#####.######
#....^.....#
############
endmap
spawn faction=player unit=momotaro   x=3  y=8
spawn faction=enemy  unit=kohaku_bow x=10 y=2 ai=cautious
```

地圖可以直接用 ASCII 畫、設計意圖跟資料放在一起、git diff 看得懂。

**2. GUI 與文字檔雙向，開存不失真。**
`EveryShippedEncounter_SurvivesOpenAndSaveUnchanged` 會把 86 份關卡全部開啟再儲存並斷言逐字不變，
`KeepsItsCommentsThroughOpenAndSave` 再確認人寫的註解沒有被吃掉。
**設計者因此不需要選邊站** —— 想用 GUI 就用，想在編輯器裡批次改就直接改。

**3. 驗證分兩層：結構性錯誤靠測試，設計品質靠模擬。**
編輯器擋掉「這張圖壞了」；擋不掉「這張圖無聊」。同一份檔案可以丟進 `Ediki.Sim` 跑批次：

```text
gym-d1-routes.encounter / corridor-hold  (60 runs)
  M5 win rate      : 100%  (60W / 0L / 0unresolved)
  M1 variety       : 91 distinct mixes, top-3 = 35%
  M1b skill use    : 0% of unit-turns issued a skill
```

勝率 100% 加上技能使用率 0% 一起讀，說的是同一件事：**這張圖不需要做選擇也能贏。**
那是設計問題，而它是被量出來的，不是被感覺出來的。

## 架構

```
Ediki.Core    規則與資料模型（不依賴 Unity）
Ediki.Sim     批次模擬、指標、異常偵測、決定性隨機（不依賴 Unity 執行期）
Ediki.Unity   呈現與輸入
Ediki.Editor  關卡編輯器、Replay 視窗
```

四層各自獨立 assembly，27 個測試檔共 434 個 EditMode 測試。
`Ediki.Sim` 不依賴 Unity 執行期，所以模擬可以脫離編輯器跑，且同 seed 同結果。

## 怎麼跑

按 Unity 的 **Play** 就能玩，開場載入 `gym-big-split`。
關卡編輯器在 Unity 選單開啟，Replay 視窗可重播模擬產生的戰鬥。

## 開發文件

這個 repo 有一半的重量在 `docs/` —— 設計願景、規格、架構、ADR、驗證方法論。
完整索引與依角色的閱讀路徑見 **[docs/README.md](docs/README.md)**。

---

*這是一個進行中的原型。目前狀態、未決事項與已知矛盾記錄在 `docs/OPEN-DECISIONS.md` 與 `docs/CONFLICTS.md`。*
