using System;
using System.Collections.Generic;
using Ediki.Core;
using Ediki.Unity;
using UnityEditor;
using UnityEngine;

namespace Ediki.Editor.Prototype
{
    public sealed partial class PrototypeEditorWindow
    {
        private bool _foldEncounter = true;
        private bool _foldObjective = true;
        private bool _foldUnit = true;
        private bool _foldSkills = true;
        private bool _foldSchedule;

        private int _pendingWidth;
        private int _pendingHeight;
        private bool _sizeInitialised;

        /// <summary>
        /// A foldout that restarts the GUI pass when it is toggled. Opening a
        /// section adds controls the cached layout does not know about, which is
        /// the same hazard every conditional field in this window avoids.
        /// </summary>
        private static bool Fold(bool value, string label, bool header = true)
        {
            bool next = EditorGUILayout.Foldout(value, label, true,
                                                header ? EditorStyles.foldoutHeader : EditorStyles.foldout);
            if (next != value) RestartLayout();
            return next;
        }

        private void DrawInspector(Rect rect)
        {
            GUILayout.BeginArea(rect, EditorStyles.helpBox);
            _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);

            // Greyed out rather than hidden while the game runs: the planner is
            // playtesting precisely to read these numbers, so they must stay
            // legible — they just must not be typeable into a document the
            // running battle was already built from.
            using (new EditorGUI.DisabledScope(EditingLocked))
            {
                DrawEncounterSection();
                EditorGUILayout.Space(6f);
                DrawObjectiveSection();
                EditorGUILayout.Space(6f);
                DrawUnitSection();
                EditorGUILayout.Space(6f);
                DrawScheduleSection();
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------ encounter

        private void DrawEncounterSection()
        {
            _foldEncounter = Fold(_foldEncounter, "關卡設定");
            if (!_foldEncounter) return;

            EditorGUI.indentLevel++;

            if (!_sizeInitialised) { _pendingWidth = _doc.Width; _pendingHeight = _doc.Height; _sizeInitialised = true; }

            EditorGUI.BeginChangeCheck();
            string id = EditorGUILayout.DelayedTextField("關卡代號", _doc.Id);
            string name = EditorGUILayout.DelayedTextField("關卡名稱", _doc.DisplayName);
            if (EditorGUI.EndChangeCheck())
            {
                _history.Push(_doc, "改關卡名稱");
                _doc.Id = EncounterDocumentIO.Token(id, "encounter");
                _doc.DisplayName = EncounterDocumentIO.Token(name, _doc.Id);
                MarkDirty();
            }

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("地圖大小　目前 " + _doc.Width + " x " + _doc.Height,
                                       EditorStyles.miniBoldLabel);

            // One field per row, full width.
            //
            // These used to share a row inside a BeginHorizontal. Unity gives the
            // LABEL a fixed share of each row, so two labelled fields in a narrow
            // panel left input boxes a few pixels wide — you could scrub the
            // label to raise the number but never click in to type a smaller one,
            // which is exactly the "grows but will not shrink back" symptom.
            _pendingWidth = Mathf.Clamp(EditorGUILayout.DelayedIntField("寬（格）", _pendingWidth),
                                        EncounterDocument.MinSize, EncounterDocument.MaxSize);
            _pendingHeight = Mathf.Clamp(EditorGUILayout.DelayedIntField("高（格）", _pendingHeight),
                                         EncounterDocument.MinSize, EncounterDocument.MaxSize);

            bool changed = _pendingWidth != _doc.Width || _pendingHeight != _doc.Height;
            bool shrinking = _pendingWidth < _doc.Width || _pendingHeight < _doc.Height;

            int wouldFallOutside = 0;
            if (changed)
            {
                for (int i = 0; i < _doc.Spawns.Count; i++)
                {
                    Coord p = _doc.Spawns[i].Position;
                    if (p.X >= _pendingWidth || p.Y >= _pendingHeight) wouldFallOutside++;
                }
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!changed))
            {
                if (GUILayout.Button("套用大小"))
                {
                    _history.Push(_doc, "改地圖大小");
                    _doc.Resize(_pendingWidth, _pendingHeight, EncounterDocumentIO.DefaultTerrainIndex(_data.Terrain));
                    _pendingWidth = _doc.Width;
                    _pendingHeight = _doc.Height;
                    _camera.FocusMap(_doc.Width, _doc.Height);
                    MarkDirty();
                }
                if (GUILayout.Button("取消", GUILayout.Width(50f)))
                {
                    _pendingWidth = _doc.Width;
                    _pendingHeight = _doc.Height;
                }
            }
            EditorGUILayout.EndHorizontal();

            string sizeHint;
            if (!changed) sizeHint = "重疊的格子會保留，縮小和放大都可以。";
            else if (wouldFallOutside > 0)
                sizeHint = "⚠ 有 " + wouldFallOutside + " 個單位會落在地圖外，套用後要把它們搬回來。";
            else sizeHint = shrinking ? "會裁掉邊緣的格子，中間的保留。" : "新格子會填成預設地面。";

            EditorGUILayout.LabelField(" ", sizeHint, EditorStyles.wordWrappedMiniLabel);

            if (!_plannerMode)
            {
                EditorGUILayout.Space(2f);
                EditorGUI.BeginChangeCheck();
                DamageModel model = (DamageModel)EditorGUILayout.EnumPopup("Damage model", _doc.Damage);
                if (EditorGUI.EndChangeCheck())
                {
                    _history.Push(_doc, "改傷害模型");
                    _doc.Damage = model;
                    MarkDirty();
                }
                EditorGUILayout.LabelField(" ", PlannerVocabulary.DamageModelHint(_doc.Damage), EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.Space(2f);
                int index = _doc.Damage == DamageModel.Percentage ? 1 : 0;
                EditorGUI.BeginChangeCheck();
                int picked = EditorGUILayout.Popup("傷害計算", index, new[] { "固定扣減", "百分比減傷" });
                if (EditorGUI.EndChangeCheck())
                {
                    _history.Push(_doc, "改傷害計算");
                    _doc.Damage = picked == 1 ? DamageModel.Percentage : DamageModel.Subtractive;
                    MarkDirty();
                }
                EditorGUILayout.LabelField(" ", PlannerVocabulary.DamageModelHint(_doc.Damage), EditorStyles.miniLabel);
            }

            EditorGUI.indentLevel--;
        }

        // ------------------------------------------------------------ objective

        private void DrawObjectiveSection()
        {
            _foldObjective = Fold(_foldObjective, "任務目標");
            if (!_foldObjective) return;

            EditorGUI.indentLevel++;

            ObjectiveKind[] kinds = PlannerVocabulary.AllObjectives;
            string[] labels = new string[kinds.Length];
            int current = 0;
            for (int i = 0; i < kinds.Length; i++)
            {
                labels[i] = PlannerVocabulary.ObjectiveName(kinds[i]);
                if (kinds[i] == _doc.ObjectiveKind) current = i;
            }

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup("類型", current, labels);
            if (EditorGUI.EndChangeCheck())
            {
                _history.Push(_doc, "改任務類型");
                _doc.ObjectiveKind = kinds[picked];

                // Survive and Defend cannot resolve without a clock, so give them
                // one rather than letting the planner hit an error for a field
                // they have not been shown yet.
                if ((_doc.ObjectiveKind == ObjectiveKind.Survive || _doc.ObjectiveKind == ObjectiveKind.Defend)
                    && _doc.TurnLimit <= 0)
                    _doc.TurnLimit = 8;

                MarkDirty();
            }

            EditorGUILayout.LabelField(" ", PlannerVocabulary.ObjectiveHint(_doc.ObjectiveKind),
                                       EditorStyles.wordWrappedMiniLabel);

            EditorGUI.BeginChangeCheck();
            int limit = Mathf.Max(0, EditorGUILayout.DelayedIntField("回合上限", _doc.TurnLimit));
            if (EditorGUI.EndChangeCheck())
            {
                _history.Push(_doc, "改回合上限");
                _doc.TurnLimit = limit;
                MarkDirty();
            }
            EditorGUILayout.LabelField(" ", _doc.TurnLimit == 0 ? "0 = 沒有時間限制" : "超過就算失敗",
                                       EditorStyles.miniLabel);

            // Always emitted, greyed out when the objective does not read them.
            // Showing and hiding fields between passes is what makes IMGUI throw
            // "Getting control N's position in a group with only M controls", so
            // every conditional field in this window is disabled rather than dropped.
            bool usesTarget = _doc.ObjectiveKind == ObjectiveKind.Reach;
            using (new EditorGUI.DisabledScope(!usesTarget))
            {
                EditorGUI.BeginChangeCheck();
                int x = EditorGUILayout.DelayedIntField("目標 X", _doc.ObjectiveTarget.X);
                int y = EditorGUILayout.DelayedIntField("目標 Y", _doc.ObjectiveTarget.Y);
                if (EditorGUI.EndChangeCheck() && usesTarget)
                {
                    _history.Push(_doc, "改任務地點");
                    _doc.ObjectiveTarget = new Coord(x, y);
                    MarkDirty();
                }
            }
            EditorGUILayout.LabelField(" ", usesTarget
                ? "也可以用左邊的「任務地點」工具直接在地圖上點。"
                : "這個任務類型不會用到地點。", EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(4f);
            DrawObjectiveUnits();

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// Which units the objective is ABOUT, chosen here rather than one unit
        /// at a time.
        ///
        /// These two flags were on the unit inspector, which made setting up a
        /// "kill the marked enemy" battle a matter of selecting units until you
        /// found the right one and remembering that only one may carry the mark.
        /// They belong to the objective — they only mean anything relative to it,
        /// and the loader rejects a mark the objective never reads.
        /// </summary>
        private void DrawObjectiveUnits()
        {
            bool wantsKill = _doc.ObjectiveKind == ObjectiveKind.Kill;
            bool wantsProtect = _doc.ObjectiveKind == ObjectiveKind.Defend;

            EditorGUILayout.LabelField(
                wantsKill ? "要擊殺的敵人" : wantsProtect ? "要保護的單位" : "任務單位",
                EditorStyles.miniBoldLabel);

            if (!wantsKill && !wantsProtect)
            {
                EditorGUILayout.LabelField(" ", "這個任務類型不需要指定單位。", EditorStyles.miniLabel);
                ClearStrayMarks();
                return;
            }

            List<int> candidates = new List<int>();
            for (int i = 0; i < _doc.Spawns.Count; i++)
            {
                SpawnEntry s = _doc.Spawns[i];
                bool eligible = wantsKill
                    ? s.Faction == Faction.Enemy && !s.IsReinforcement
                    : s.Faction == Faction.Player;
                if (eligible) candidates.Add(i);
            }

            if (candidates.Count == 0)
            {
                EditorGUILayout.HelpBox(wantsKill
                    ? "地圖上沒有可以當擊殺目標的敵人（中途登場的敵人不能當目標）。"
                    : "地圖上沒有我方單位可以保護。", MessageType.Warning);
                return;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                int index = candidates[i];
                SpawnEntry s = _doc.Spawns[index];
                bool on = wantsKill ? s.IsObjectiveTarget : s.Protect;

                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                bool next = EditorGUILayout.Toggle(on, GUILayout.Width(18f));
                EditorGUILayout.LabelField(_doc.TagOf(s) + "  " + DisplayNameOf(s));
                EditorGUILayout.EndHorizontal();

                if (!EditorGUI.EndChangeCheck()) continue;

                _history.Push(_doc, wantsKill ? "改擊殺目標" : "改保護目標");

                if (wantsKill)
                {
                    // Exactly one. The loader takes the first mark and the rest do
                    // nothing but perturb the state hash, so picking a second one
                    // has to MOVE the mark rather than add one.
                    for (int j = 0; j < _doc.Spawns.Count; j++) _doc.Spawns[j].IsObjectiveTarget = false;
                    s.IsObjectiveTarget = next;
                }
                else
                {
                    s.Protect = next;   // any number may be defended at once
                }

                MarkDirty();
                RestartLayout();
            }

            EditorGUILayout.LabelField(" ", wantsKill
                ? "只能選一個。選了另一個，原本的就會取消。"
                : "可以選多個，全部都要活到時限。", EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>
        /// Drops marks the current objective would never read.
        ///
        /// The loader REFUSES an encounter carrying one, so leaving it set would
        /// block save and play with an error about a checkbox that is no longer
        /// on screen anywhere.
        /// </summary>
        private void ClearStrayMarks()
        {
            bool any = false;
            for (int i = 0; i < _doc.Spawns.Count; i++)
                if (_doc.Spawns[i].IsObjectiveTarget) { any = true; break; }

            if (!any) return;

            _history.Push(_doc, "清掉用不到的擊殺目標");
            for (int i = 0; i < _doc.Spawns.Count; i++) _doc.Spawns[i].IsObjectiveTarget = false;
            MarkDirty();
            SetStatus("任務類型改了，原本的擊殺目標標記已經清掉 —— 這個類型不會用到它。", MessageType.Info);
        }

        // ----------------------------------------------------------------- unit

        private void DrawUnitSection()
        {
            _foldUnit = Fold(_foldUnit, "選取的單位");
            if (!_foldUnit) return;

            if (_selectedSpawn < 0 || _selectedSpawn >= _doc.Spawns.Count)
            {
                EditorGUILayout.HelpBox("在地圖上點一個單位就會顯示它的資料。", MessageType.None);
                return;
            }

            SpawnEntry spawn = _doc.Spawns[_selectedSpawn];
            EditorGUI.indentLevel++;

            DrawIdentity(spawn);

            UnitDef def;
            bool known = _data.Units.TryGet(spawn.UnitId, out def);
            if (!known && !spawn.HasPendingStats)
            {
                EditorGUILayout.HelpBox("找不到角色「" + spawn.UnitId + "」。請在下面重新選一個。", MessageType.Error);
                EditorGUI.indentLevel--;
                return;
            }

            UnitStatBlock view = spawn.HasPendingStats ? spawn.PendingStats : UnitStatBlock.From(def);
            UnitStatBlock edited = view.Clone();

            EditorGUILayout.Space(4f);
            DrawCoreStats(edited);
            EditorGUILayout.Space(4f);
            DrawCombat(edited);
            EditorGUILayout.Space(4f);
            DrawSkills(edited);

            CommitStats(spawn, view, edited, known ? def : null);

            EditorGUI.indentLevel--;
        }

        private void DrawIdentity(SpawnEntry spawn)
        {
            EditorGUILayout.LabelField("身分", EditorStyles.miniBoldLabel);

            EditorGUILayout.LabelField("編號", _doc.TagOf(spawn));

            // Character, then A/B. Switching the variant is the same gesture as
            // switching the character, which is what makes "try him as B" a
            // one-click experiment rather than a hunt through the unit list.
            string nextId = CharacterAndVariant(spawn.UnitId, spawn.Faction, false);
            if (!string.Equals(nextId, spawn.UnitId, StringComparison.Ordinal))
            {
                _history.Push(_doc, "更換角色或變體");
                spawn.UnitId = nextId;

                // A different build is not an edit of the previous one — its
                // pending numbers would be meaningless against the new base.
                spawn.PendingStats = null;
                MarkDirty();

                // An unknown id draws an error box instead of the stat fields, so
                // becoming known changes what the rest of this pass would emit.
                RestartLayout();
            }

            EditorGUILayout.LabelField(_plannerMode ? "資料代號" : "Unit ID", spawn.UnitId);

            EditorGUI.BeginChangeCheck();
            int faction = EditorGUILayout.Popup("陣營", spawn.Faction == Faction.Player ? 0 : 1,
                                                new[] { "我方", "敵方" });
            if (EditorGUI.EndChangeCheck())
            {
                _history.Push(_doc, "改陣營");
                spawn.Faction = faction == 0 ? Faction.Player : Faction.Enemy;
                if (spawn.Faction == Faction.Player) { spawn.IsObjectiveTarget = false; spawn.AiProfileId = null; }
                else { spawn.Protect = false; if (string.IsNullOrEmpty(spawn.AiProfileId)) spawn.AiProfileId = _brushAi; }
                MarkDirty();
            }

            UnitDef def;
            bool inCatalog = _data.Units.TryGet(spawn.UnitId, out def);
            string role = "—";
            if (spawn.HasPendingStats)
                role = PrototypeVisuals.PlannerNameOf(PrototypeVisuals.ArchetypeOf(spawn.PendingStats.ToDef("preview")));
            else if (inCatalog)
                role = PrototypeVisuals.PlannerNameOf(PrototypeVisuals.ArchetypeOf(def));
            EditorGUILayout.LabelField("定位", role + "（由數值判定，決定它在地圖上的形狀）");

            EditorGUI.BeginChangeCheck();
            int x = EditorGUILayout.DelayedIntField("位置 X", spawn.Position.X);
            int y = EditorGUILayout.DelayedIntField("位置 Y", spawn.Position.Y);
            int turn = Mathf.Max(0, EditorGUILayout.DelayedIntField("登場回合", spawn.ArrivesOnTurn));
            if (EditorGUI.EndChangeCheck())
            {
                _history.Push(_doc, "改位置或登場回合");
                spawn.Position = new Coord(x, y);
                spawn.ArrivesOnTurn = turn;
                MarkDirty();
            }
            EditorGUILayout.LabelField(" ", spawn.ArrivesOnTurn == 0 ? "0 = 一開始就在場上" : "半透明顯示 = 還沒登場",
                                       EditorStyles.miniLabel);

            // Behaviour stays here — it is a property of THIS enemy, not of the
            // objective. Kill target and protect target have moved to the
            // objective panel: they are not attributes of a unit, they are the
            // answer to "what is this battle about", and having them here meant
            // configuring one objective by visiting several units in turn.
            bool isEnemy = spawn.Faction == Faction.Enemy;

            using (new EditorGUI.DisabledScope(!isEnemy))
            {
                int aiIndex = _data.AiIds.IndexOf(spawn.AiProfileId);
                EditorGUI.BeginChangeCheck();
                int pickedAi = EditorGUILayout.Popup("行為", Mathf.Max(0, aiIndex), AiMenuLabels());
                if (EditorGUI.EndChangeCheck() && isEnemy && pickedAi >= 0 && pickedAi < _data.AiIds.Count)
                {
                    _history.Push(_doc, "改敵人行為");
                    spawn.AiProfileId = _data.AiIds[pickedAi];
                    MarkDirty();
                }
            }

            string objectiveRole = spawn.IsObjectiveTarget ? "要擊殺的目標"
                        : spawn.Protect ? "要保護的目標" : "—";
            EditorGUILayout.LabelField("任務角色", objectiveRole + "（在上面的「任務目標」裡設定）");
        }

        private void DrawCoreStats(UnitStatBlock s)
        {
            EditorGUILayout.LabelField(_plannerMode ? "基本" : "Stats", EditorStyles.miniBoldLabel);

            s.DisplayName = EditorGUILayout.DelayedTextField(_plannerMode ? "顯示名稱" : "DisplayName", s.DisplayName);

            s.MaxHp = Row(PlannerVocabulary.Hp, "MaxHp", s.MaxHp);
            s.MaxAp = Row(PlannerVocabulary.Ap, "MaxAp", s.MaxAp);
            s.ApRegen = Row(PlannerVocabulary.ApRegen, "ApRegen", s.ApRegen);
            s.Move = Row(PlannerVocabulary.Move, "Move", s.Move);
            s.Def = Row(PlannerVocabulary.Def, "Def", s.Def);

            if (!_plannerMode) s.AtkGrowth = Row(PlannerVocabulary.AtkGrowth, "AtkGrowth", s.AtkGrowth);
        }

        private void DrawCombat(UnitStatBlock s)
        {
            EditorGUILayout.LabelField(_plannerMode ? "攻擊" : "Attack", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;
            s.Atk = Row(PlannerVocabulary.AttackDamageHint, "Atk", s.Atk);
            s.AttackApCost = Row(PlannerVocabulary.AttackCost, "AttackApCost", s.AttackApCost);
            s.AttackRange = Row(PlannerVocabulary.Range, "AttackRange", s.AttackRange);
            if (!_plannerMode)
                s.AttacksPerRound = Row(PlannerVocabulary.AttacksPerRound, "AttacksPerRound", s.AttacksPerRound);
            EditorGUI.indentLevel--;

            EditorGUILayout.LabelField(_plannerMode ? "格擋 / 反擊 / 休息" : "Guard / Counter / Rest",
                                       EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;
            s.GuardApCost = Row(PlannerVocabulary.GuardCost, "GuardApCost", s.GuardApCost);
            s.CounterApCost = Row(PlannerVocabulary.CounterCost, "CounterApCost", s.CounterApCost);
            if (s.CounterApCost == 0)
                EditorGUILayout.LabelField(" ", "0 = 不會反擊", EditorStyles.miniLabel);
            s.RestApCost = Row(PlannerVocabulary.RestCost, "RestApCost", s.RestApCost);
            using (new EditorGUI.DisabledScope(s.RestApCost <= 0))
                s.RestHealPercent = Row(PlannerVocabulary.RestHeal + "（%）", "RestHealPercent", s.RestHealPercent);
            EditorGUI.indentLevel--;
        }

        private void DrawSkills(UnitStatBlock s)
        {
            _foldSkills = Fold(_foldSkills, _plannerMode ? "技能" : "Skills", false);
            if (!_foldSkills) return;

            EditorGUI.indentLevel++;

            s.PushApCost = Skill(PlannerVocabulary.SkillPush, s.PushApCost, 1,
                                 PlannerVocabulary.SkillDistance, ref s.PushRange, 1);
            s.SlowApCost = Skill(PlannerVocabulary.SkillSlow, s.SlowApCost, 1,
                                 PlannerVocabulary.SkillRange, ref s.SlowRange, 3);
            s.TauntApCost = Skill(PlannerVocabulary.SkillTaunt, s.TauntApCost, 3,
                                  PlannerVocabulary.SkillRadius, ref s.TauntRadius, 3);

            s.ArmorBreakApCost = Skill(PlannerVocabulary.SkillArmorBreak, s.ArmorBreakApCost, 3,
                                       PlannerVocabulary.SkillRange, ref s.ArmorBreakRange, 1);
            EditorGUI.indentLevel++;
            using (new EditorGUI.DisabledScope(s.ArmorBreakApCost <= 0))
                s.ArmorBreakAmount = Row(PlannerVocabulary.SkillAmount + "（降低防禦）", "ArmorBreakAmount",
                                         s.ArmorBreakAmount);
            EditorGUI.indentLevel--;

            s.PurifyApCost = Skill(PlannerVocabulary.SkillPurify, s.PurifyApCost, 4,
                                   PlannerVocabulary.SkillRadius, ref s.PurifyRadius, 2);
            s.ContaminatePerTurn = Skill(PlannerVocabulary.SkillContaminate, s.ContaminatePerTurn, 1,
                                         PlannerVocabulary.SkillRadius, ref s.ContaminateRadius, 1,
                                         "每回合污染量");

            EditorGUILayout.Space(2f);
            s.ImmuneToPush = EditorGUILayout.Toggle(PlannerVocabulary.ImmuneToPush, s.ImmuneToPush);
            if (!_plannerMode)
                s.SkillUsesPerRound = Row(PlannerVocabulary.SkillUsesPerRound, "SkillUsesPerRound", s.SkillUsesPerRound);

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// One skill: a toggle that turns it on at a sensible cost, then its two
        /// numbers. Cost 0 IS "the unit does not have this skill" in the data
        /// model, so the toggle is not a separate field — it writes the cost.
        /// </summary>
        private int Skill(string label, int cost, int defaultCost, string secondLabel,
                          ref int secondValue, int defaultSecond, string costLabel = null)
        {
            bool on = cost > 0;
            bool next = EditorGUILayout.Toggle(label, on);

            if (next != on)
            {
                if (next) { cost = defaultCost; if (secondValue <= 0) secondValue = defaultSecond; }
                else cost = 0;
            }

            EditorGUI.indentLevel++;
            using (new EditorGUI.DisabledScope(cost <= 0))
            {
                int typedCost = Row(costLabel ?? PlannerVocabulary.SkillCost, "ApCost", cost);
                int typedSecond = Row(secondLabel, "Range", secondValue);
                if (cost > 0) { cost = typedCost; secondValue = typedSecond; }
            }
            EditorGUI.indentLevel--;

            return cost;
        }

        /// <summary>Planner label in planner mode, engineering field name otherwise.</summary>
        private int Row(string plannerLabel, string engineeringLabel, int value)
        {
            return EditorGUILayout.DelayedIntField(_plannerMode ? plannerLabel : engineeringLabel, value);
        }

        /// <summary>
        /// Writes the edited block back, but only when it actually differs — and
        /// drops the pending edit entirely when the planner types the original
        /// numbers back in, so no variant is ever minted for a round trip.
        /// </summary>
        private void CommitStats(SpawnEntry spawn, UnitStatBlock view, UnitStatBlock edited, UnitDef catalogDef)
        {
            if (edited.MatchesStatsOf(view.ToDef("_"))) return;

            _history.Push(_doc, "修改數值");

            if (catalogDef != null && edited.MatchesStatsOf(catalogDef))
            {
                spawn.PendingStats = null;
                SetStatus("數值和原本一樣，不會另外建立角色。", MessageType.Info);
            }
            else
            {
                spawn.PendingStats = edited;
                SetStatus("數值已改。存檔或試玩時會自動建立新角色，原本的角色不受影響。", MessageType.Info);
            }

            MarkDirty();
        }

        // ------------------------------------------------------------- schedule

        private void DrawScheduleSection()
        {
            _foldSchedule = Fold(_foldSchedule, "生成排程（" + _doc.Spawns.Count + " 個單位）");
            if (!_foldSchedule) return;

            EditorGUI.indentLevel++;

            int removeAt = -1;
            for (int i = 0; i < _doc.Spawns.Count; i++)
            {
                SpawnEntry s = _doc.Spawns[i];

                EditorGUILayout.BeginHorizontal();

                bool selected = i == _selectedSpawn;
                bool pick = GUILayout.Toggle(selected, _doc.TagOf(s), EditorStyles.miniButton, GUILayout.Width(38f));
                if (pick != selected && pick)
                {
                    _selectedSpawn = i;
                    _selectedCell = s.Position;
                    _camera.FocusCell(s.Position);

                    // The unit section is drawn ABOVE this list, so it already
                    // ran with the old selection this pass — consistent with the
                    // cached layout. The next Layout picks the new one up.
                    Repaint();
                }

                EditorGUILayout.LabelField(DisplayNameOf(s), GUILayout.Width(96f));

                EditorGUI.BeginChangeCheck();
                int turn = Mathf.Max(0, EditorGUILayout.DelayedIntField(s.ArrivesOnTurn, GUILayout.Width(30f)));
                int x = EditorGUILayout.DelayedIntField(s.Position.X, GUILayout.Width(30f));
                int y = EditorGUILayout.DelayedIntField(s.Position.Y, GUILayout.Width(30f));
                if (EditorGUI.EndChangeCheck())
                {
                    _history.Push(_doc, "改生成排程");
                    s.ArrivesOnTurn = turn;
                    s.Position = new Coord(x, y);
                    MarkDirty();
                }

                if (GUILayout.Button("刪", EditorStyles.miniButton, GUILayout.Width(26f))) removeAt = i;

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.LabelField(" ", "欄位依序是：登場回合 / X / Y", EditorStyles.miniLabel);

            if (removeAt >= 0)
            {
                _history.Push(_doc, "刪除單位");
                RemoveSpawn(removeAt);
            }

            EditorGUILayout.Space(4f);
            if (GUILayout.Button("在地圖中央新增一個" + PlannerVocabulary.FactionName(_brushFaction) + "單位"))
            {
                _history.Push(_doc, "新增單位");
                PlaceUnitAtFreeCell();
            }

            EditorGUI.indentLevel--;
        }

        private void PlaceUnitAtFreeCell()
        {
            int cx = _doc.Width / 2;
            int cy = _doc.Height / 2;

            for (int radius = 0; radius < Mathf.Max(_doc.Width, _doc.Height); radius++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        Coord c = new Coord(cx + dx, cy + dy);
                        if (!_doc.Contains(c)) continue;
                        if (_doc.SpawnAt(c) != null) continue;

                        int idx = _doc.TerrainAt(c);
                        if (idx < 0 || idx >= _data.Terrain.Count) continue;
                        if (_data.Terrain[idx].BlocksMovement) continue;

                        _doc.Spawns.Add(NewSpawnAt(c));
                        _selectedSpawn = _doc.Spawns.Count - 1;
                        _selectedCell = c;
                        MarkDirty();
                        return;
                    }
            }

            SetStatus("地圖上找不到空的可通行格子。", MessageType.Warning);
        }
    }
}
