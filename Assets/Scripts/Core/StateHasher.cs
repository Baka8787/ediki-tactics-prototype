namespace Ediki.Core
{
    /// <summary>
    /// Canonical world-state hash (determinism rule 3 / ADR-0003).
    ///
    /// Never uses GetHashCode(): .NET randomises string hashing per process, so
    /// a GetHashCode-based hash compares equal within one run and differs across
    /// runs — exactly the failure that only shows up in CI.
    ///
    /// Only game state goes in. UI state (selection, camera, animation progress)
    /// must never reach here.
    /// </summary>
    public static class StateHasher
    {
        private const uint FnvOffsetBasis = 2166136261;
        private const uint FnvPrime = 16777619;

        public static uint Hash(BattleState state)
        {
            uint h = FnvOffsetBasis;

            h = Mix(h, state.TurnIndex);
            h = Mix(h, (int)state.CurrentFaction);
            h = Mix(h, (int)state.Outcome);
            h = Mix(h, state.Map.Width);
            h = Mix(h, state.Map.Height);

            // Units are stored id-ordered and never reordered, so iteration is canonical.
            for (int i = 0; i < state.Units.Count; i++)
            {
                UnitState u = state.Units[i];
                h = Mix(h, u.Id);
                h = MixString(h, u.Def.Id);
                h = Mix(h, (int)u.Faction);
                h = Mix(h, u.Position.X);
                h = Mix(h, u.Position.Y);
                h = Mix(h, u.Hp);
                h = Mix(h, u.Ap);
                h = Mix(h, u.IsGuarding ? 1 : 0);
                h = Mix(h, u.HasEndedTurn ? 1 : 0);
                h = Mix(h, u.IsActivated ? 1 : 0);
                h = Mix(h, u.HasCounteredThisRound ? 1 : 0);
                h = Mix(h, u.MoveUsedThisTurn);
                h = Mix(h, u.MustSurvive ? 1 : 0);
            }

            // Declaration order, never reordered.
            for (int i = 0; i < state.Reinforcements.Count; i++)
            {
                PendingReinforcement r = state.Reinforcements[i];
                h = Mix(h, r.Turn);
                h = Mix(h, (int)r.Faction);
                h = MixString(h, r.Def.Id);
                h = Mix(h, r.Position.X);
                h = Mix(h, r.Position.Y);
                h = Mix(h, r.Spawned ? 1 : 0);
            }

            // Mutable terrain. Folded in only when it exists, so a battle that
            // never contaminates anything hashes exactly as it did before terrain
            // could change at all — which keeps the golden constants meaningful
            // across the change instead of forcing a rubber-stamp update.
            if (state.HasContamination)
            {
                for (int y = 0; y < state.Map.Height; y++)
                    for (int x = 0; x < state.Map.Width; x++)
                    {
                        int level = state.ContaminationAt(new Coord(x, y));
                        if (level == 0) continue;
                        h = Mix(h, x);
                        h = Mix(h, y);
                        h = Mix(h, level);
                    }
            }

            // Control statuses. Same gating as contamination and kill targets: a
            // battle where nobody was ever slowed or taunting hashes exactly as it
            // did before the control kit existed, so the golden constants keep
            // meaning something across the change.
            if (state.HasControlStatus)
            {
                for (int i = 0; i < state.Units.Count; i++)
                {
                    UnitState u = state.Units[i];
                    h = Mix(h, u.SlowedUntilTurn);
                    h = Mix(h, u.TauntingUntilTurn);
                }
            }

            // Armour breaks. Gated separately from the control statuses above, not
            // folded into them: a battle that taunts but never breaks armour must
            // keep hashing the way it did before 破甲 existed, and so must one that
            // does neither. Both fields go in — the stamp alone would let two
            // states with different DEF reductions collide.
            if (state.HasArmorBreak)
            {
                for (int i = 0; i < state.Units.Count; i++)
                {
                    UnitState u = state.Units[i];
                    h = Mix(h, u.ArmorBrokenUntilTurn);
                    h = Mix(h, u.ArmorBrokenAmount);
                }
            }

            if (state.HasStatuses)
            {
                for (int i = 0; i < state.Units.Count; i++)
                {
                    UnitState u = state.Units[i];
                    if (u.Statuses == null) continue;
                    ActiveStatus[] ordered = u.Statuses.ToArray();
                    System.Array.Sort(ordered, (a, b) =>
                    {
                        int c = ((int)a.Kind).CompareTo((int)b.Kind);
                        if (c != 0) return c;
                        c = a.RemainingPhases.CompareTo(b.RemainingPhases);
                        return c != 0 ? c : a.Magnitude.CompareTo(b.Magnitude);
                    });
                    h = Mix(h, u.Id);
                    h = Mix(h, ordered.Length);
                    for (int s = 0; s < ordered.Length; s++)
                    {
                        h = Mix(h, (int)ordered[s].Kind);
                        h = Mix(h, ordered[s].RemainingPhases);
                        h = Mix(h, ordered[s].Magnitude);
                    }
                }
            }

            // Per-round action counters. Gated on any unit HAVING a cap, not on the
            // counters being non-zero: an uncapped roster never reads them, so a
            // battle without caps keeps hashing the way it always did.
            if (state.HasPerRoundCaps)
            {
                for (int i = 0; i < state.Units.Count; i++)
                {
                    UnitState u = state.Units[i];
                    h = Mix(h, u.AttacksThisRound);
                    h = Mix(h, u.SkillUsesThisRound);
                }
            }

            // Kill targets. Folded in only when one exists, for the same reason as
            // contamination above: an encounter that marks nobody hashes exactly as
            // it did before Kill objectives existed, so the golden constants keep
            // meaning something instead of being rubber-stamped.
            if (state.HasObjectiveTarget)
            {
                for (int i = 0; i < state.Units.Count; i++)
                    h = Mix(h, state.Units[i].IsObjectiveTarget ? 1 : 0);
            }

            h = Mix(h, (int)state.Objective.Kind);
            h = Mix(h, state.Objective.TurnLimit);
            h = Mix(h, state.Objective.Target.X);
            h = Mix(h, state.Objective.Target.Y);

            return h;
        }

        private static uint Mix(uint h, int value)
        {
            unchecked
            {
                uint v = (uint)value;
                h = (h ^ (v & 0xFF)) * FnvPrime;
                h = (h ^ ((v >> 8) & 0xFF)) * FnvPrime;
                h = (h ^ ((v >> 16) & 0xFF)) * FnvPrime;
                h = (h ^ ((v >> 24) & 0xFF)) * FnvPrime;
                return h;
            }
        }

        private static uint MixString(uint h, string s)
        {
            unchecked
            {
                if (s == null) return Mix(h, -1);
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    h = (h ^ (uint)(c & 0xFF)) * FnvPrime;
                    h = (h ^ (uint)((c >> 8) & 0xFF)) * FnvPrime;
                }
                return h;
            }
        }
    }
}
