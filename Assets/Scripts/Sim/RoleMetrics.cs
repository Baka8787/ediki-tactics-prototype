using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ediki.Core;

namespace Ediki.Sim
{
    /// <summary>
    /// Where a player turn's AP actually went.
    ///
    /// The question this exists for: raising an attack from 4 AP to 5 does not
    /// create a reserve by itself — it creates 3 spare AP, and something else may
    /// still spend them. Guard costs 3. So the interesting number is not "did the
    /// residue appear" but "what consumed it", and that needs the spend broken out
    /// by action rather than a single waste percentage.
    ///
    /// Every figure is read from ApSpent / ApReset effects the rule layer already
    /// emits. Nothing here re-derives a cost.
    /// </summary>
    public sealed class ApResidue
    {
        public long OnAttack;
        public long OnMove;
        public long OnSkill;
        public long OnGuard;
        public long OnRest;

        /// <summary>Spent during the ENEMY phase to pay for a counter-attack.</summary>
        public long OnCounter;

        /// <summary>
        /// AP still unspent when the player ended its turn, summed over rounds.
        ///
        /// This is the reserve that survives into the enemy phase — AP resets only
        /// for the INCOMING faction, so what the player holds back is still there
        /// when it is attacked.
        /// </summary>
        public long ReservedIntoEnemyPhase;

        /// <summary>Regen that spilled over the cap and was lost.</summary>
        public long RegenWasted;

        public long TotalSpent => OnAttack + OnMove + OnSkill + OnGuard + OnRest + OnCounter;

        public void Merge(ApResidue other)
        {
            OnAttack += other.OnAttack;
            OnMove += other.OnMove;
            OnSkill += other.OnSkill;
            OnGuard += other.OnGuard;
            OnRest += other.OnRest;
            OnCounter += other.OnCounter;
            ReservedIntoEnemyPhase += other.ReservedIntoEnemyPhase;
            RegenWasted += other.RegenWasted;
        }
    }

    /// <summary>
    /// Role-specific tallies for one batch cell.
    ///
    /// All of it comes from the existing effect stream. Anything that would need
    /// a rule the project does not have is absent rather than approximated — see
    /// the armour-break note in the experiment write-up.
    /// </summary>
    public sealed class RoleMetrics
    {
        public int Battles;
        public int PlayerRounds;

        public readonly ApResidue Ap = new ApResidue();

        /// <summary>Commands the simulator refused. Never counted as an action.</summary>
        public int RejectedCommands;

        // --- what the player side did -------------------------------------
        public int PlayerAttacks;
        public int PlayerKills;
        public int Pushes;
        public int Slows;
        public int Taunts;
        public long DamageDealt;

        // --- what happened to it ------------------------------------------
        public int EnemyAttacksOnPlayers;
        public long DamageReceived;

        // --- counter ------------------------------------------------------

        /// <summary>
        /// Attacks on a counter-capable player that were in reach and not already
        /// answered this round — i.e. everything the rule checks EXCEPT the AP.
        ///
        /// Defined that way on purpose: it isolates the AP condition, which is the
        /// whole question. A ranged attacker produces no opportunity at all,
        /// because a counter needs the attacker inside the defender's own range.
        /// </summary>
        public int CounterOpportunities;

        public int CounterActivations;
        public long CounterDamage;

        /// <summary>Activations per opportunity, in percent. -1 when there were none.</summary>
        public int CounterActivationPercent =>
            CounterOpportunities == 0 ? -1 : CounterActivations * 100 / CounterOpportunities;

        /// <summary>
        /// Total cells enemies were displaced by Push, summed.
        ///
        /// Reported next to the count because the two are not the same claim, and
        /// in this rule layer displacement is ALWAYS 1 per push — so this is a
        /// structural constant, not a measurement. It is printed anyway so nobody
        /// has to rediscover that.
        /// </summary>
        public int PushDisplacement;

        /// <summary>
        /// Per-player-unit damage, so "who actually got hit" is answerable.
        ///
        /// The protection question needs this split: a taunt that works moves
        /// damage from the fragile unit onto the tank, and a battle-level total
        /// cannot see that at all.
        /// </summary>
        public readonly List<UnitTally> ByUnit = new List<UnitTally>();

        public sealed class UnitTally
        {
            public int UnitId;
            public string Name;
            public int MaxHp;
            public long DamageReceived;
            public int AttacksReceived;

            /// <summary>Final HP summed over battles; divide by Battles for the mean.</summary>
            public long FinalHpTotal;
        }

        public UnitTally Unit(int unitId, string name = null, int maxHp = 0)
        {
            for (int i = 0; i < ByUnit.Count; i++)
                if (ByUnit[i].UnitId == unitId) return ByUnit[i];

            UnitTally tally = new UnitTally { UnitId = unitId, Name = name, MaxHp = maxHp };
            ByUnit.Add(tally);
            return tally;
        }

        public void Merge(RoleMetrics other)
        {
            if (other == null) return;
            PushDisplacement += other.PushDisplacement;

            Battles += other.Battles;
            PlayerRounds += other.PlayerRounds;
            Ap.Merge(other.Ap);
            RejectedCommands += other.RejectedCommands;

            PlayerAttacks += other.PlayerAttacks;
            PlayerKills += other.PlayerKills;
            Pushes += other.Pushes;
            Slows += other.Slows;
            Taunts += other.Taunts;
            DamageDealt += other.DamageDealt;

            EnemyAttacksOnPlayers += other.EnemyAttacksOnPlayers;
            DamageReceived += other.DamageReceived;

            CounterOpportunities += other.CounterOpportunities;
            CounterActivations += other.CounterActivations;
            CounterDamage += other.CounterDamage;
        }

        public string Describe()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("  role metrics     : ").Append(N(Battles)).Append(" battle(s), ")
              .Append(N(PlayerRounds)).AppendLine(" player round(s)");
            sb.Append("    player         : ").Append(N(PlayerAttacks)).Append(" attacks, ")
              .Append(N(PlayerKills)).Append(" kills, ").Append(Big(DamageDealt)).Append(" damage dealt")
              .Append("   skills: push ").Append(N(Pushes)).Append(" / slow ").Append(N(Slows))
              .Append(" / taunt ").Append(N(Taunts)).AppendLine();
            sb.Append("    taken          : ").Append(N(EnemyAttacksOnPlayers)).Append(" enemy attacks, ")
              .Append(Big(DamageReceived)).AppendLine(" damage received");
            sb.Append("    counter        : ").Append(N(CounterActivations)).Append(" fired of ")
              .Append(N(CounterOpportunities)).Append(" opportunities  (")
              .Append(CounterActivationPercent < 0 ? "n/a" : N(CounterActivationPercent) + "%")
              .Append(")   damage ").Append(Big(CounterDamage)).AppendLine();
            sb.Append("    AP spent       : attack ").Append(Big(Ap.OnAttack))
              .Append("  move ").Append(Big(Ap.OnMove))
              .Append("  skill ").Append(Big(Ap.OnSkill))
              .Append("  guard ").Append(Big(Ap.OnGuard))
              .Append("  rest ").Append(Big(Ap.OnRest))
              .Append("  counter ").Append(Big(Ap.OnCounter)).AppendLine();
            sb.Append("    AP reserved    : ").Append(Big(Ap.ReservedIntoEnemyPhase))
              .Append(" carried into enemy phases  (mean ")
              .Append(PlayerRounds == 0 ? "n/a" : Fixed(Ap.ReservedIntoEnemyPhase * 100 / PlayerRounds))
              .Append(" per round)   regen wasted ").Append(Big(Ap.RegenWasted))
              .Append("   rejected commands ").Append(N(RejectedCommands)).AppendLine();

            if (Pushes > 0)
            {
                sb.Append("    push           : ").Append(N(Pushes)).Append(" pushes, ")
                  .Append(N(PushDisplacement)).AppendLine(" cells of displacement (1 per push by rule)");
            }

            // Who took the hits. This is the protection measurement; without the
            // split, a taunt that redirected everything looks identical to one
            // that did nothing.
            if (ByUnit.Count > 0)
            {
                long total = 0;
                for (int i = 0; i < ByUnit.Count; i++) total += ByUnit[i].DamageReceived;

                sb.AppendLine("    per player unit:");
                for (int i = 0; i < ByUnit.Count; i++)
                {
                    UnitTally u = ByUnit[i];
                    sb.Append("      ").Append((u.Name ?? ("#" + N(u.UnitId))).PadRight(16))
                      .Append("dmg ").Append(Big(u.DamageReceived).PadLeft(7))
                      .Append("  share ").Append(total == 0 ? "n/a" : (u.DamageReceived * 100 / total) + "%")
                      .Append("   attacks taken ").Append(N(u.AttacksReceived))
                      .Append("   mean final HP ")
                      .Append(Battles == 0 ? "n/a" : Big(u.FinalHpTotal / Battles) + "/" + N(u.MaxHp))
                      .Append(Battles == 0 || u.MaxHp == 0
                          ? "" : "  (" + (u.FinalHpTotal * 100 / Battles / u.MaxHp) + "%)")
                      .AppendLine();
                }
            }

            return sb.ToString();
        }

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
        private static string Big(long v) => v.ToString(CultureInfo.InvariantCulture);
        private static string Fixed(long x100) =>
            (x100 / 100).ToString(CultureInfo.InvariantCulture) + "." +
            (x100 % 100).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Collects RoleMetrics by watching a battle.
    ///
    /// Read-only, like every other observer here: it consumes the effect stream
    /// and the states the runner already had, issues nothing and changes nothing.
    /// One instance can watch a whole batch cell.
    /// </summary>
    public sealed class RoleMetricsObserver : IBattleObserver
    {
        private readonly RoleMetrics _metrics = new RoleMetrics();
        private readonly UnitPositionTracker _positions = new UnitPositionTracker();

        /// <summary>
        /// Players that have already answered an attack this round.
        ///
        /// The rule layer clears HasCounteredThisRound at the start of the unit's
        /// own phase, so a unit counters at most once per round. An attack arriving
        /// after that is not an opportunity the AP could have taken.
        /// </summary>
        private bool[] _counteredThisRound = new bool[16];

        public RoleMetrics Metrics => _metrics;

        public void RoundStarted(int round, BattleState state)
        {
            _positions.Grow(state);
            Grow(state);
            for (int i = 0; i < _counteredThisRound.Length; i++) _counteredThisRound[i] = false;
            _metrics.PlayerRounds++;
        }

        public void PlayerCommand(int round, BattleState before, ICommand command, ExecuteResult result)
        {
            if (!result.Ok)
            {
                // A refused command spent nothing and did nothing. Counted so the
                // residue table adds up rather than quietly losing turns.
                _metrics.RejectedCommands++;
                return;
            }

            Consume(before, result.Log, ApBucketFor(command));
        }

        public void PhaseResolved(int round, Faction phase, BattleState before, EffectLog log, BattleState after)
        {
            // The player's own end-of-turn state is the last moment its AP is
            // visible before the enemy acts, and that AP is what a counter spends.
            if (phase == Faction.Player) RecordReserve(before);

            Consume(before, log, ApBucket.Counter);
        }

        public void RoundEnded(int round, BattleState state) { }

        public void BattleFinished(BattleResult result, BattleState finalState)
        {
            _metrics.Battles++;

            // Final HP per unit, so "the backline survived" is a number rather
            // than an impression. Dead units contribute 0, which is correct.
            foreach (UnitState u in finalState.Units)
            {
                if (u.Faction != Faction.Player || u.MustSurvive) continue;
                RoleMetrics.UnitTally tally = _metrics.Unit(u.Id, u.Def.DisplayName, u.Def.MaxHp);
                tally.FinalHpTotal += u.IsAlive ? u.Hp : 0;
            }
        }

        // ------------------------------------------------------------ internals

        private enum ApBucket { Attack, Move, Skill, Guard, Rest, Counter }

        private static ApBucket ApBucketFor(ICommand command)
        {
            if (command is AttackCommand) return ApBucket.Attack;
            if (command is MoveCommand) return ApBucket.Move;
            if (command is GuardCommand) return ApBucket.Guard;
            if (command is RestCommand) return ApBucket.Rest;
            // Taunt / Slow / Push / Purify.
            return ApBucket.Skill;
        }

        private void Add(ApBucket bucket, int amount)
        {
            switch (bucket)
            {
                case ApBucket.Attack: _metrics.Ap.OnAttack += amount; break;
                case ApBucket.Move: _metrics.Ap.OnMove += amount; break;
                case ApBucket.Skill: _metrics.Ap.OnSkill += amount; break;
                case ApBucket.Guard: _metrics.Ap.OnGuard += amount; break;
                case ApBucket.Rest: _metrics.Ap.OnRest += amount; break;
                default: _metrics.Ap.OnCounter += amount; break;
            }
        }

        /// <summary>
        /// Walks one log: AP, damage, kills, skills and the counter bookkeeping.
        ///
        /// <paramref name="bucket"/> is where AP spent by a PLAYER unit is filed.
        /// Inside a phase log that is always a counter, because the only way a
        /// player spends AP outside its own command is by answering an attack.
        /// </summary>
        private void Consume(BattleState reference, EffectLog log, ApBucket bucket)
        {
            if (log == null || log.Count == 0) return;

            _positions.Seed(reference);
            Grow(reference);

            for (int i = 0; i < log.Count; i++)
            {
                Effect e = log[i];

                // Deliberately NOT "continue": a movement effect updates the
                // position AND may still need counting. UnitPushed is both a
                // relocation and a skill landing, and swallowing it here lost
                // every push from the tally.
                _positions.Follow(e);

                if (e is ApSpent spent)
                {
                    if (IsPlayer(reference, spent.UnitId)) Add(bucket, spent.Amount);
                    continue;
                }

                if (e is ApReset reset)
                {
                    if (IsPlayer(reference, reset.UnitId)) _metrics.Ap.RegenWasted += reset.Wasted;
                    continue;
                }

                if (e is AttackResolved attack) { RecordAttack(reference, attack); continue; }

                if (e is CounterAttacked counter)
                {
                    if (IsPlayer(reference, counter.DefenderId))
                    {
                        _metrics.CounterActivations++;
                        Mark(counter.DefenderId);
                    }
                    continue;
                }

                if (e is HpChanged hp) { RecordHp(reference, hp, log, i); continue; }

                if (e is UnitDied died)
                {
                    if (died.Faction == Faction.Enemy) _metrics.PlayerKills++;
                    continue;
                }

                if (e is UnitPushed pushed)
                {
                    _metrics.Pushes++;
                    _metrics.PushDisplacement +=
                        reference.Map.Topology.Distance(pushed.From, pushed.To);
                    continue;
                }
                if (e is SlowApplied) { _metrics.Slows++; continue; }
                if (e is TauntApplied) { _metrics.Taunts++; continue; }
            }
        }

        private void RecordAttack(BattleState reference, AttackResolved attack)
        {
            if (IsPlayer(reference, attack.AttackerId))
            {
                _metrics.PlayerAttacks++;
                return;
            }

            _metrics.EnemyAttacksOnPlayers++;

            UnitState defender = reference.FindUnit(attack.TargetId);
            if (defender == null || defender.Faction != Faction.Player) return;

            _metrics.Unit(defender.Id, defender.Def.DisplayName, defender.Def.MaxHp).AttacksReceived++;

            // A counter OPPORTUNITY: everything the rule checks except the AP.
            if (!defender.Def.CanCounter) return;
            if (attack.TargetId < _counteredThisRound.Length && _counteredThisRound[attack.TargetId]) return;

            Coord attackerAt, defenderAt;
            if (!_positions.TryPosition(attack.AttackerId, out attackerAt)) return;
            if (!_positions.TryPosition(attack.TargetId, out defenderAt)) return;
            if (reference.Map.Topology.Distance(attackerAt, defenderAt) > defender.Def.AttackRange) return;

            _metrics.CounterOpportunities++;
        }

        /// <summary>
        /// Damage, split by who took it — and counter damage separated out by
        /// looking back for the CounterAttacked that caused it.
        /// </summary>
        private void RecordHp(BattleState reference, HpChanged hp, EffectLog log, int index)
        {
            if (hp.Delta >= 0) return;                    // healing is not damage
            int amount = -hp.Delta;

            if (IsPlayer(reference, hp.UnitId))
            {
                _metrics.DamageReceived += amount;

                UnitState hurt = reference.FindUnit(hp.UnitId);
                if (hurt != null)
                    _metrics.Unit(hurt.Id, hurt.Def.DisplayName, hurt.Def.MaxHp).DamageReceived += amount;
                return;
            }

            _metrics.DamageDealt += amount;

            // The rule layer emits CounterAttacked immediately before the HpChanged
            // it causes, so the preceding effect identifies counter damage without
            // recomputing anything.
            for (int back = index - 1; back >= 0; back--)
            {
                if (log[back] is CounterAttacked) { _metrics.CounterDamage += amount; return; }
                if (log[back] is HpChanged || log[back] is AttackResolved) return;
            }
        }

        private void RecordReserve(BattleState endOfPlayerTurn)
        {
            foreach (UnitState unit in endOfPlayerTurn.LivingUnitsOf(Faction.Player))
            {
                if (unit.MustSurvive) continue;
                _metrics.Ap.ReservedIntoEnemyPhase += unit.Ap;
            }
        }

        private static bool IsPlayer(BattleState state, int unitId)
        {
            UnitState u = state.FindUnit(unitId);
            return u != null && u.Faction == Faction.Player;
        }

        private void Mark(int unitId)
        {
            if (unitId >= 0 && unitId < _counteredThisRound.Length) _counteredThisRound[unitId] = true;
        }

        private void Grow(BattleState state)
        {
            int highest = 0;
            for (int i = 0; i < state.Units.Count; i++)
                if (state.Units[i].Id > highest) highest = state.Units[i].Id;

            if (highest < _counteredThisRound.Length) return;

            int size = _counteredThisRound.Length;
            while (size <= highest) size *= 2;
            System.Array.Resize(ref _counteredThisRound, size);
        }
    }

    /// <summary>
    /// Fans one battle out to several observers.
    ///
    /// The runner holds a single Observer, and a batch now wants both the heatmap
    /// and the role metrics. Composing them here keeps that a reporting concern
    /// instead of another field on SimulationConfig.
    /// </summary>
    public sealed class CompositeObserver : IBattleObserver
    {
        private readonly IBattleObserver[] _observers;

        public CompositeObserver(params IBattleObserver[] observers)
        {
            _observers = observers ?? new IBattleObserver[0];
        }

        public void RoundStarted(int round, BattleState state)
        {
            for (int i = 0; i < _observers.Length; i++) _observers[i].RoundStarted(round, state);
        }

        public void PlayerCommand(int round, BattleState before, ICommand command, ExecuteResult result)
        {
            for (int i = 0; i < _observers.Length; i++)
                _observers[i].PlayerCommand(round, before, command, result);
        }

        public void PhaseResolved(int round, Faction phase, BattleState before, EffectLog log, BattleState after)
        {
            for (int i = 0; i < _observers.Length; i++)
                _observers[i].PhaseResolved(round, phase, before, log, after);
        }

        public void RoundEnded(int round, BattleState state)
        {
            for (int i = 0; i < _observers.Length; i++) _observers[i].RoundEnded(round, state);
        }

        public void BattleFinished(BattleResult result, BattleState finalState)
        {
            for (int i = 0; i < _observers.Length; i++) _observers[i].BattleFinished(result, finalState);
        }
    }
}
