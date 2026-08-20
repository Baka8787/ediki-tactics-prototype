using System;
using System.Collections.Generic;
using Ediki.Core;
using Ediki.Core.Ai;
using Ediki.Core.Data;

namespace Ediki.Editor.Prototype
{
    public enum IssueLevel { Warning = 0, Error = 1 }

    public sealed class EncounterIssue
    {
        public readonly IssueLevel Level;
        public readonly string Message;

        /// <summary>Spawn this is about, or -1. Lets the UI jump the selection to it.</summary>
        public readonly int SpawnIndex;

        /// <summary>Cell this is about, or null. Lets the map view ring it in red.</summary>
        public readonly Coord? Cell;

        public EncounterIssue(IssueLevel level, string message, int spawnIndex = -1, Coord? cell = null)
        {
            Level = level;
            Message = message;
            SpawnIndex = spawnIndex;
            Cell = cell;
        }

        public override string ToString() => (Level == IssueLevel.Error ? "ERROR  " : "WARNING") + "  " + Message;
    }

    /// <summary>
    /// Everything the editor can tell a planner BEFORE the canonical loader gets
    /// a say, phrased as something a person can act on.
    ///
    /// Two layers on purpose:
    ///   Check() is advisory and reports EVERYTHING it finds at once, with the
    ///          cell and the spawn attached, so the map view can point at it.
    ///   Gate()  is the authority. It runs EncounterLoader.Parse and
    ///          CreateBattle over the text that would actually be written, so
    ///          nothing can ever be saved or played that the runtime and
    ///          Ediki.Sim would then refuse.
    ///
    /// The rules Check() knows are the loader's rules restated in Chinese, not
    /// new ones. When the two disagree, Gate() wins and says so — that is the
    /// signal that this file has drifted from EncounterLoader and needs fixing.
    /// </summary>
    public static class EncounterValidation
    {
        public static List<EncounterIssue> Check(EncounterDocument doc, TerrainCatalog terrain,
                                                 UnitCatalog units, AiProfileCatalog aiProfiles,
                                                 EditorRoster roster = null)
        {
            List<EncounterIssue> issues = new List<EncounterIssue>();

            CheckMap(doc, terrain, issues);
            CheckSpawns(doc, terrain, units, aiProfiles, issues);
            CheckObjective(doc, terrain, issues);
            CheckReachability(doc, terrain, issues);
            CheckRoster(doc, roster, issues);

            return issues;
        }

        /// <summary>
        /// Side and identity rules, and the only checks that need the roster.
        ///
        /// Skipped entirely for units the roster does not mention: measurement
        /// fixtures like e4_backline are spawned on whichever side the experiment
        /// needs, and a tool has no business calling that an error.
        /// </summary>
        private static void CheckRoster(EncounterDocument doc, EditorRoster roster, List<EncounterIssue> issues)
        {
            if (roster == null || roster.IsEmpty) return;

            for (int i = 0; i < doc.Spawns.Count; i++)
            {
                SpawnEntry s = doc.Spawns[i];
                RosterSide? side = roster.SideOf(s.UnitId);
                if (!side.HasValue) continue;

                if (side.Value == RosterSide.Enemy && s.Faction == Faction.Player)
                    issues.Add(new EncounterIssue(IssueLevel.Error,
                        doc.TagOf(s) + "「" + roster.CharacterOf(s.UnitId).Name
                        + "」是敵方角色，不能放在我方。", i, s.Position));

                if (side.Value != RosterSide.Enemy && s.Faction == Faction.Enemy)
                    issues.Add(new EncounterIssue(IssueLevel.Error,
                        doc.TagOf(s) + "「" + roster.CharacterOf(s.UnitId).Name
                        + "」是我方角色，不能放在敵方。", i, s.Position));

                if (side.Value == RosterSide.Objective && s.Faction == Faction.Player && !s.Protect)
                    issues.Add(new EncounterIssue(IssueLevel.Warning,
                        doc.TagOf(s) + " 是守護目標類的單位，通常要勾「是要保護的目標」。", i, s.Position));
            }

            // Duplicates by CHARACTER, not by unit id: Momotaro_A and Momotaro_B
            // are two builds of 桃太郎, and you cannot bring both of him.
            List<int> party = PartySlots(doc);
            for (int a = 0; a < party.Count; a++)
                for (int b = a + 1; b < party.Count; b++)
                {
                    RosterCharacter first = roster.CharacterOf(doc.Spawns[party[a]].UnitId);
                    RosterCharacter second = roster.CharacterOf(doc.Spawns[party[b]].UnitId);
                    if (first == null || second == null || first != second) continue;

                    // The identical-id case is already reported by CheckPartyLimits.
                    if (string.Equals(doc.Spawns[party[a]].UnitId, doc.Spawns[party[b]].UnitId,
                                      StringComparison.OrdinalIgnoreCase)) continue;

                    issues.Add(new EncounterIssue(IssueLevel.Error,
                        doc.TagOf(doc.Spawns[party[b]]) + " 和 " + doc.TagOf(doc.Spawns[party[a]])
                        + " 都是「" + first.Name + "」的不同定位，同一個角色只能帶一個。",
                        party[b], doc.Spawns[party[b]].Position));
                }
        }

        // ------------------------------------------------------------------ map

        private static void CheckMap(EncounterDocument doc, TerrainCatalog terrain, List<EncounterIssue> issues)
        {
            if (doc.Width < EncounterDocument.MinSize || doc.Height < EncounterDocument.MinSize)
                issues.Add(new EncounterIssue(IssueLevel.Error,
                    "地圖太小（" + doc.Width + "x" + doc.Height + "），最小是 "
                    + EncounterDocument.MinSize + "x" + EncounterDocument.MinSize + "。"));

            if (doc.Width > EncounterDocument.MaxSize || doc.Height > EncounterDocument.MaxSize)
                issues.Add(new EncounterIssue(IssueLevel.Error,
                    "地圖太大（" + doc.Width + "x" + doc.Height + "），最大是 "
                    + EncounterDocument.MaxSize + "x" + EncounterDocument.MaxSize + "。"));

            // 不存在的 Tile Definition: a cell pointing past the end of terrain.txt.
            // Reachable by editing terrain.txt down after a map was painted.
            for (int y = 0; y < doc.Height; y++)
                for (int x = 0; x < doc.Width; x++)
                {
                    int idx = doc.TerrainAt(x, y);
                    if (idx >= 0 && idx < terrain.Count) continue;
                    issues.Add(new EncounterIssue(IssueLevel.Error,
                        "格子 (" + x + "," + y + ") 用的地形已經不存在了，請重新塗一次。", -1, new Coord(x, y)));
                    return;   // one is enough; the whole map is suspect
                }

            bool anyPassable = false;
            for (int y = 0; y < doc.Height && !anyPassable; y++)
                for (int x = 0; x < doc.Width && !anyPassable; x++)
                {
                    int idx = doc.TerrainAt(x, y);
                    if (idx >= 0 && idx < terrain.Count && !terrain[idx].BlocksMovement) anyPassable = true;
                }

            if (!anyPassable)
                issues.Add(new EncounterIssue(IssueLevel.Error, "整張地圖都是障礙物，沒有任何可以站的格子。"));
        }

        // --------------------------------------------------------------- spawns

        private static void CheckSpawns(EncounterDocument doc, TerrainCatalog terrain, UnitCatalog units,
                                        AiProfileCatalog aiProfiles, List<EncounterIssue> issues)
        {
            if (doc.Spawns.Count == 0)
            {
                issues.Add(new EncounterIssue(IssueLevel.Error, "地圖上還沒有放任何單位。"));
                return;
            }

            Dictionary<Coord, int> usedAtStart = new Dictionary<Coord, int>();
            bool hasCombatant = false;
            bool hasEnemy = false;
            int protectedCount = 0;
            int targetCount = 0;

            for (int i = 0; i < doc.Spawns.Count; i++)
            {
                SpawnEntry s = doc.Spawns[i];
                string tag = doc.TagOf(s);

                if (string.IsNullOrEmpty(s.UnitId))
                {
                    issues.Add(new EncounterIssue(IssueLevel.Error, tag + " 還沒有選角色。", i, s.Position));
                }
                else if (!s.HasPendingStats)
                {
                    // A spawn with pending edits will be given a brand-new id at
                    // save time, so the catalog cannot know about it yet.
                    UnitDef def;
                    if (!units.TryGet(s.UnitId, out def))
                        issues.Add(new EncounterIssue(IssueLevel.Error,
                            tag + " 用的角色「" + s.UnitId + "」在角色資料裡找不到。", i, s.Position));
                }

                if (!doc.Contains(s.Position))
                {
                    issues.Add(new EncounterIssue(IssueLevel.Error,
                        tag + " 的位置 (" + s.Position.X + "," + s.Position.Y + ") 在地圖外面。", i, s.Position));
                }
                else
                {
                    int idx = doc.TerrainAt(s.Position);
                    if (idx >= 0 && idx < terrain.Count)
                    {
                        TerrainDef t = terrain[idx];
                        if (t.BlocksMovement)
                            issues.Add(new EncounterIssue(IssueLevel.Error,
                                tag + " 站在障礙物上（" + t.Name + "），沒有單位能站在那裡。", i, s.Position));
                        else if (t.IsLethal)
                            issues.Add(new EncounterIssue(IssueLevel.Warning,
                                tag + " 站在致命地形上（" + t.Name + "），一開場就會死。", i, s.Position));
                    }

                    if (!s.IsReinforcement)
                    {
                        int other;
                        if (usedAtStart.TryGetValue(s.Position, out other))
                            issues.Add(new EncounterIssue(IssueLevel.Error,
                                tag + " 和 " + doc.TagOf(doc.Spawns[other]) + " 重疊在同一格 ("
                                + s.Position.X + "," + s.Position.Y + ")。", i, s.Position));
                        else usedAtStart.Add(s.Position, i);
                    }
                }

                if (s.ArrivesOnTurn < 0)
                    issues.Add(new EncounterIssue(IssueLevel.Error, tag + " 的登場回合不能是負數。", i, s.Position));

                if (s.Protect)
                {
                    protectedCount++;
                    if (s.Faction != Faction.Player)
                        issues.Add(new EncounterIssue(IssueLevel.Error,
                            tag + " 是敵人，不能設成「要保護的目標」。", i, s.Position));
                    if (s.IsReinforcement)
                        issues.Add(new EncounterIssue(IssueLevel.Error,
                            tag + " 是要保護的目標，不能設成中途登場。", i, s.Position));
                }

                if (s.IsObjectiveTarget)
                {
                    targetCount++;
                    if (s.Faction != Faction.Enemy)
                        issues.Add(new EncounterIssue(IssueLevel.Error,
                            tag + " 是我方，不能設成「要擊殺的目標」。", i, s.Position));
                    if (s.IsReinforcement)
                        issues.Add(new EncounterIssue(IssueLevel.Error,
                            tag + " 是要擊殺的目標，不能設成中途登場。", i, s.Position));
                }

                if (s.Faction == Faction.Enemy && !string.IsNullOrEmpty(s.AiProfileId))
                {
                    AiProfile profile;
                    if (!aiProfiles.TryGet(s.AiProfileId, out profile))
                        issues.Add(new EncounterIssue(IssueLevel.Warning,
                            tag + " 指定的行為「" + s.AiProfileId + "」不存在，會改用預設行為。", i, s.Position));
                }

                if (s.Faction == Faction.Player && !s.Protect) hasCombatant = true;
                if (s.Faction == Faction.Enemy) hasEnemy = true;
            }

            if (!hasCombatant)
                issues.Add(new EncounterIssue(IssueLevel.Error,
                    "沒有任何能戰鬥的我方單位（只放要保護的目標，第一回合就會輸）。"));
            if (!hasEnemy)
                issues.Add(new EncounterIssue(IssueLevel.Error, "沒有放任何敵人。"));

            CheckPartyLimits(doc, issues);

            if (targetCount > 1)
                issues.Add(new EncounterIssue(IssueLevel.Warning,
                    "有 " + targetCount + " 個敵人被設成擊殺目標，只有第一個會被計算。"));
            if (protectedCount == 0 && doc.ObjectiveKind == ObjectiveKind.Defend)
                issues.Add(new EncounterIssue(IssueLevel.Error, "任務是「守住目標」，但沒有任何單位被設成要保護的目標。"));
        }

        /// <summary>
        /// The party is at most four, and nobody brings two of themselves.
        ///
        /// Four is the 討鬼團: the GDD names 桃太郎, 正守, 玄真 and 影丸 and no
        /// fifth member, SquadMatrix.Characters is a four-element array, and every
        /// crucible map is built with exactly four combat slots. Confirmed as the
        /// current cap by the project owner 2026-08-18.
        ///
        /// A protected spawn does NOT count. SquadMatrix skips those for the same
        /// reason: a shrine is scenery the objective points at, not a squad slot.
        /// </summary>
        public const int MaxPartySize = 4;

        /// <summary>Player spawns that occupy a party slot. Protected props do not.</summary>
        private static List<int> PartySlots(EncounterDocument doc)
        {
            List<int> party = new List<int>();
            for (int i = 0; i < doc.Spawns.Count; i++)
            {
                SpawnEntry s = doc.Spawns[i];
                if (s.Faction != Faction.Player || s.Protect) continue;
                party.Add(i);
            }
            return party;
        }

        private static void CheckPartyLimits(EncounterDocument doc, List<EncounterIssue> issues)
        {
            List<int> party = PartySlots(doc);

            if (party.Count > MaxPartySize)
            {
                int extra = party[MaxPartySize];
                issues.Add(new EncounterIssue(IssueLevel.Error,
                    "我方最多 " + MaxPartySize + " 個出戰單位，目前有 " + party.Count
                    + " 個。（要保護的目標不算在內。）",
                    extra, doc.Spawns[extra].Position));
            }

            for (int a = 0; a < party.Count; a++)
                for (int b = a + 1; b < party.Count; b++)
                {
                    SpawnEntry first = doc.Spawns[party[a]];
                    SpawnEntry second = doc.Spawns[party[b]];
                    if (!string.Equals(first.UnitId, second.UnitId, StringComparison.OrdinalIgnoreCase)) continue;

                    issues.Add(new EncounterIssue(IssueLevel.Error,
                        doc.TagOf(second) + " 和 " + doc.TagOf(first) + " 是同一個角色（"
                        + second.UnitId + "），我方不能重複帶同一人。",
                        party[b], second.Position));
                }
        }

        // ------------------------------------------------------------ objective

        private static void CheckObjective(EncounterDocument doc, TerrainCatalog terrain, List<EncounterIssue> issues)
        {
            switch (doc.ObjectiveKind)
            {
                case ObjectiveKind.Reach:
                    if (!doc.Contains(doc.ObjectiveTarget))
                    {
                        issues.Add(new EncounterIssue(IssueLevel.Error,
                            "任務目標地點 (" + doc.ObjectiveTarget.X + "," + doc.ObjectiveTarget.Y + ") 在地圖外面。",
                            -1, doc.ObjectiveTarget));
                    }
                    else
                    {
                        int idx = doc.TerrainAt(doc.ObjectiveTarget);
                        if (idx >= 0 && idx < terrain.Count && terrain[idx].BlocksMovement)
                            issues.Add(new EncounterIssue(IssueLevel.Error,
                                "任務目標地點是障礙物，沒有單位能走到那裡。", -1, doc.ObjectiveTarget));
                    }
                    break;

                case ObjectiveKind.Survive:
                case ObjectiveKind.Defend:
                    if (doc.TurnLimit <= 0)
                        issues.Add(new EncounterIssue(IssueLevel.Error,
                            "這個任務類型一定要設回合上限，否則永遠不會結束。"));
                    break;

                case ObjectiveKind.Kill:
                    bool marked = false;
                    for (int i = 0; i < doc.Spawns.Count && !marked; i++) marked = doc.Spawns[i].IsObjectiveTarget;
                    if (!marked)
                        issues.Add(new EncounterIssue(IssueLevel.Error,
                            "任務是「擊殺指定敵人」，但沒有任何敵人被標成目標。"));
                    break;
            }

            // A mark the objective never reads would do nothing except perturb
            // the state hash — the loader rejects it outright, so warn early.
            if (doc.ObjectiveKind != ObjectiveKind.Kill)
            {
                for (int i = 0; i < doc.Spawns.Count; i++)
                {
                    if (!doc.Spawns[i].IsObjectiveTarget) continue;
                    issues.Add(new EncounterIssue(IssueLevel.Error,
                        doc.TagOf(doc.Spawns[i]) + " 被標成擊殺目標，但目前的任務類型不會用到它。",
                        i, doc.Spawns[i].Position));
                }
            }

            if (doc.ObjectiveKind != ObjectiveKind.Defend)
            {
                for (int i = 0; i < doc.Spawns.Count; i++)
                {
                    if (!doc.Spawns[i].Protect) continue;
                    issues.Add(new EncounterIssue(IssueLevel.Warning,
                        doc.TagOf(doc.Spawns[i]) + " 設成了要保護的目標，但任務類型不是「守住目標」——"
                        + "它死掉一樣會判定失敗。", i, doc.Spawns[i].Position));
                }
            }

            if (doc.TurnLimit < 0)
                issues.Add(new EncounterIssue(IssueLevel.Error, "回合上限不能是負數。"));
        }

        // --------------------------------------------------------- connectivity

        /// <summary>
        /// R-MAP-02 restated: an enemy the player can never walk to makes "defeat
        /// all enemies" unwinnable. Terrain only — units move, walls do not.
        /// </summary>
        private static void CheckReachability(EncounterDocument doc, TerrainCatalog terrain, List<EncounterIssue> issues)
        {
            Coord start = default;
            bool found = false;
            for (int i = 0; i < doc.Spawns.Count && !found; i++)
            {
                if (doc.Spawns[i].Faction != Faction.Player) continue;
                if (!doc.Contains(doc.Spawns[i].Position)) continue;
                start = doc.Spawns[i].Position;
                found = true;
            }
            if (!found) return;

            HashSet<Coord> seen = new HashSet<Coord> { start };
            Queue<Coord> queue = new Queue<Coord>();
            queue.Enqueue(start);

            int[] dx = { 1, -1, 0, 0 };
            int[] dy = { 0, 0, 1, -1 };

            while (queue.Count > 0)
            {
                Coord cur = queue.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    Coord n = new Coord(cur.X + dx[d], cur.Y + dy[d]);
                    if (!doc.Contains(n)) continue;
                    int idx = doc.TerrainAt(n);
                    if (idx < 0 || idx >= terrain.Count || terrain[idx].BlocksMovement) continue;
                    if (!seen.Add(n)) continue;
                    queue.Enqueue(n);
                }
            }

            for (int i = 0; i < doc.Spawns.Count; i++)
            {
                SpawnEntry s = doc.Spawns[i];
                if (s.Faction != Faction.Enemy) continue;
                if (!doc.Contains(s.Position)) continue;
                if (seen.Contains(s.Position)) continue;

                issues.Add(new EncounterIssue(IssueLevel.Error,
                    doc.TagOf(s) + " 被牆完全圍住，我方走不到它，這場戰鬥永遠打不完。", i, s.Position));
            }
        }

        // ------------------------------------------------------------ authority

        public sealed class GateResult
        {
            public bool Ok;

            /// <summary>The exact text that would be written. Valid whenever Ok.</summary>
            public string Text;

            /// <summary>Why the canonical loader refused. Null when Ok.</summary>
            public string LoaderError;

            public List<EncounterIssue> Issues;

            public int ErrorCount
            {
                get
                {
                    int n = 0;
                    for (int i = 0; i < Issues.Count; i++) if (Issues[i].Level == IssueLevel.Error) n++;
                    return n;
                }
            }
        }

        /// <summary>
        /// The last word before anything is written or played.
        ///
        /// Advisory checks first (so the planner gets the readable message), then
        /// the REAL loader over the REAL text. Both must pass. If Check() is
        /// clean and the loader still refuses, the loader's message is shown
        /// verbatim — a mismatch there is a bug in this file, not in the data,
        /// and hiding it would let the editor emit encounters the game cannot open.
        /// </summary>
        public static GateResult Gate(EncounterDocument doc, TerrainCatalog terrain,
                                      UnitCatalog units, AiProfileCatalog aiProfiles,
                                      EditorRoster roster = null)
        {
            GateResult result = new GateResult { Issues = Check(doc, terrain, units, aiProfiles, roster) };

            if (result.ErrorCount > 0) { result.Ok = false; return result; }

            string text = EncounterDocumentIO.ToText(doc, terrain);
            try
            {
                EncounterDef def = EncounterLoader.Parse(text, terrain);
                EncounterLoader.CreateBattle(def, units, aiProfiles);
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.LoaderError = ex.Message;
                return result;
            }

            result.Ok = true;
            result.Text = text;
            return result;
        }
    }
}
