using System.Collections.Generic;
using System.Text;
using Ediki.Core;
using Ediki.Core.Ai;
using Ediki.Core.Data;

namespace Ediki.Sim
{
    public sealed class SimulationConfig
    {
        public string MapName = "map";
        public string EncounterText;
        public IPlayerStrategy Strategy;
        public int Runs = 100;
        public int BaseSeed = 1;

        /// <summary>
        /// Chance per decision that the strategy takes a random legal command
        /// instead of its preferred one.
        ///
        /// This is a SAMPLING device, not a gameplay rule: with zero noise a
        /// deterministic strategy against a deterministic AI replays the same
        /// battle every time and the distributions would all have zero variance.
        /// </summary>
        public int NoisePercent = 15;

        /// <summary>
        /// Safety net so a stuck battle cannot hang the batch. NOT a gameplay
        /// rule — R-WIN-04 still says the rules have no turn limit. Runs that
        /// trip it are reported separately as "unresolved".
        /// </summary>
        public int MaxRounds = 60;

        public int MaxCommandsPerUnitTurn = 24;

        /// <summary>
        /// M6: the row the dividing wall sits on. The first time a player unit
        /// steps onto that row, the x it used identifies the route it took.
        ///
        /// This is a property of the MAP, not of the rules — a map with no
        /// dividing wall has no routes to count, and passes -1 to disable it.
        /// </summary>
        public int RouteProbeRow = 5;

        /// <summary>
        /// M7: sample each enemy's release round (first round it can reach a
        /// player). Costs one threat-range flood fill per living enemy per round,
        /// so it is switchable for anyone who only wants M1-M5 quickly.
        /// </summary>
        public bool MeasureContact = true;

        /// <summary>
        /// Optional read-only tap for replay. Null in a batch — the notifications
        /// are the only cost, and an observer cannot change a decision.
        /// </summary>
        public IBattleObserver Observer;

        /// <summary>
        /// The CALLER's declaration that this strategy should not issue skills.
        ///
        /// Deliberately a config flag rather than something inferred from the
        /// strategy: IPlayerStrategy exposes only Name and DecideNext, so the only
        /// way to answer it from the strategy itself would be to hardcode a list
        /// of names, and a name is not a policy. Default false means the question
        /// is never asked and UNEXPECTED_SKILL_USAGE can never be raised.
        /// </summary>
        public bool ExpectNoStrategySkillUse;
    }

    /// <summary>
    /// Runs batches of scripted battles and collects M1-M5.
    ///
    /// Reads state and issues Commands; changes no rules. Every run is fully
    /// reproducible from (encounter text, strategy, seed, noise).
    /// </summary>
    public sealed class SimulationRunner
    {
        private readonly TerrainCatalog _terrain;
        private readonly UnitCatalog _units;
        private readonly AiProfileCatalog _aiProfiles;

        public SimulationRunner(string terrainData, string unitData, string aiProfileData)
        {
            _terrain = TerrainLoader.Parse(terrainData);
            _units = UnitLoader.Parse(unitData);
            _aiProfiles = AiProfileLoader.Parse(aiProfileData);
        }

        /// <summary>
        /// The derived-metrics table for one encounter. No battles are run — this
        /// is algebra over the data, and it is meant to be read BEFORE the outcome
        /// metrics, because it says whether they can mean anything.
        /// </summary>
        public string DescribeProfile(string encounterText)
        {
            EncounterDef encounter = EncounterLoader.Parse(encounterText, _terrain);
            return EncounterProfile.Describe(encounter, _units);
        }

        public List<BattleResult> RunBatch(SimulationConfig config)
        {
            List<BattleResult> results = new List<BattleResult>(config.Runs);
            for (int i = 0; i < config.Runs; i++)
                results.Add(RunOne(config, config.BaseSeed + i));
            return results;
        }

        public BattleResult RunOne(SimulationConfig config, int seed)
        {
            EncounterDef encounter = EncounterLoader.Parse(config.EncounterText, _terrain);
            BattleSetup setup = EncounterLoader.CreateBattle(encounter, _units, _aiProfiles);

            BattleState state = BattleSimulator.Begin(setup.State).State;
            DeterministicRandom rng = new DeterministicRandom(seed);

            BattleResult result = new BattleResult
            {
                Map = config.MapName,
                Strategy = config.Strategy.Name,
                Seed = seed,
                RouteProbeRow = config.RouteProbeRow,
                RoundCap = config.MaxRounds,
                ExpectedNoStrategySkillUse = config.ExpectNoStrategySkillUse
            };

            // The denominator for "lost with the party still intact" has to be
            // fixed before anyone dies, and it counts the fallen: measured against
            // the survivors only, a wipe with one healthy unit left would read as
            // full strength.
            foreach (UnitState u in state.Units)
                if (u.Faction == Faction.Player && !u.MustSurvive) result.PlayerMaxHp += u.Def.MaxHp;

            int rounds = 0;
            int roundsObserved = 0;

            while (state.Outcome == BattleOutcome.InProgress && rounds < config.MaxRounds)
            {
                rounds++;

                // M7 is sampled BEFORE the player acts, so round 1 reports the
                // encounter as the designer laid it out rather than as the
                // strategy rearranged it.
                if (config.MeasureContact) SampleReleases(state, result, rounds);

                if (config.Observer != null) config.Observer.RoundStarted(rounds, state);

                BattleState beforePhase = state;
                state = RunPlayerTurn(state, config, rng, result, rounds);
                if (state.Outcome != BattleOutcome.InProgress) break;

                ExecuteResult endPlayer = BattleSimulator.Execute(state, new EndTurnCommand(Faction.Player));
                if (!endPlayer.Ok) { result.EndedByRejectedCommand = true; break; }
                // Enemy reinforcements arrive on this transition, so this log matters.
                Tally(endPlayer.Log, endPlayer.State, result, config.RouteProbeRow, rounds);
                beforePhase = state;
                state = endPlayer.State;
                if (config.Observer != null)
                    config.Observer.PhaseResolved(rounds, Faction.Player, beforePhase, endPlayer.Log, state);
                if (state.Outcome != BattleOutcome.InProgress) break;

                EffectLog enemyLog = new EffectLog();
                beforePhase = state;
                state = setup.Ai.RunFactionTurn(state, Faction.Enemy, enemyLog,
                                                config.MaxCommandsPerUnitTurn).State;
                Tally(enemyLog, state, result, config.RouteProbeRow, rounds);
                if (config.Observer != null)
                    config.Observer.PhaseResolved(rounds, Faction.Enemy, beforePhase, enemyLog, state);
                if (state.Outcome != BattleOutcome.InProgress) break;

                ExecuteResult endEnemy = BattleSimulator.Execute(state, new EndTurnCommand(Faction.Enemy));
                if (!endEnemy.Ok) { result.EndedByRejectedCommand = true; break; }
                Tally(endEnemy.Log, endEnemy.State, result, config.RouteProbeRow, rounds);
                beforePhase = state;
                state = endEnemy.State;
                // This log was already tallied but never shown to an observer, so
                // anything it emits — reinforcements, a timed objective resolving —
                // was invisible to a transcript or a heatmap.
                if (config.Observer != null)
                    config.Observer.PhaseResolved(rounds, Faction.Enemy, beforePhase, endEnemy.Log, state);

                ObserveRoundEnd(config, state, rounds, ref roundsObserved);
            }

            // The round the battle ended in exits through a break, so its end is
            // reported here instead. Guarded on the round number, so a round that
            // completed normally is not counted twice.
            ObserveRoundEnd(config, state, rounds, ref roundsObserved);

            FinishContact(state, result, rounds);

            result.Outcome = state.Outcome;
            result.Turns = state.TurnIndex;
            result.HitRoundCap = state.Outcome == BattleOutcome.InProgress;
            result.EnemiesRemaining = state.CountLiving(Faction.Enemy);
            result.FinalStateHash = StateHasher.Hash(state);

            foreach (UnitState u in state.LivingUnitsOf(Faction.Player))
                if (!u.MustSurvive) result.FinalPlayerHp += u.Hp;

            if (config.Observer != null) config.Observer.BattleFinished(result, state);

            return result;
        }

        private static BattleState RunPlayerTurn(BattleState state, SimulationConfig config,
                                                 DeterministicRandom rng, BattleResult result,
                                                 int round)
        {
            result.PlayerTurns++;
            List<ActionKind> actions = new List<ActionKind>();

            List<int> unitIds = new List<int>();
            foreach (UnitState u in state.LivingUnitsOf(Faction.Player))
            {
                if (u.MustSurvive) continue;   // objective props never act; they would skew M2
                unitIds.Add(u.Id);
                result.ApGranted += u.Def.ApRegen;
            }

            for (int i = 0; i < unitIds.Count; i++)
            {
                List<ActionKind> unitActions = new List<ActionKind>();

                for (int step = 0; step < config.MaxCommandsPerUnitTurn; step++)
                {
                    if (state.Outcome != BattleOutcome.InProgress) break;

                    UnitState unit = state.FindUnit(unitIds[i]);
                    if (unit == null || !unit.IsAlive || unit.HasEndedTurn) break;

                    bool fromNoise;
                    ICommand command = Decide(state, unit, config, rng, out fromNoise);
                    if (command == null) break;

                    ExecuteResult r = BattleSimulator.Execute(state, command);

                    // Notified before the Ok check: a refused command is a fact
                    // about this battle, and it is the one a triage reader most
                    // wants to see. It still ends the unit's activation.
                    if (config.Observer != null) config.Observer.PlayerCommand(round, state, command, r);
                    if (!r.Ok) break;

                    state = r.State;
                    Tally(r.Log, r.State, result, config.RouteProbeRow, result.PlayerTurns);
                    ActionKind kind;
                    if (TryClassify(command, out kind))
                    {
                        actions.Add(kind);
                        unitActions.Add(kind);

                        // Who chose this skill decides whether it means anything.
                        // The noise samples LegalCommands uniformly and that list
                        // contains skills, so it emits them for strategies with no
                        // skill logic — attributing those to the strategy would
                        // make every batch look like it used the control kit.
                        if (IsSkill(kind))
                        {
                            if (fromNoise) result.NoiseSkillActions++;
                            else result.StrategySkillActions++;
                        }
                    }
                    if (command is WaitCommand || command is RestCommand) break;
                }

                result.UnitActionCompositions.Add(BattleResult.ComposeKey(unitActions));
            }

            result.ActionCompositions.Add(BattleResult.ComposeKey(actions));

            foreach (UnitState u in state.LivingUnitsOf(Faction.Player))
            {
                if (u.MustSurvive) continue;
                result.EndOfTurnExposure.Add(BattleQueries.EffectiveExposure(state, u.Position, Faction.Player));
            }

            return state;
        }

        // ---------------------------------------------------------------- M7

        /// <summary>
        /// Records the first round each enemy could actually reach a player unit.
        ///
        /// "Could reach", not "did attack": an enemy that walks into range and is
        /// killed before it swings still constrained where the player could stand,
        /// and that constraint is the thing being measured.
        /// </summary>
        private static void SampleReleases(BattleState state, BattleResult result, int round)
        {
            foreach (UnitState enemy in state.LivingUnitsOf(Faction.Enemy))
            {
                if (HasEntryFor(result.EnemyReleases, enemy.Id)) continue;

                HashSet<Coord> threatened = BattleQueries.ThreatRange(state, enemy);
                foreach (UnitState player in state.LivingUnitsOf(Faction.Player))
                {
                    if (player.MustSurvive) continue;          // props are not what the enemy is chasing
                    if (!threatened.Contains(player.Position)) continue;

                    result.EnemyReleases.Add(new KeyValuePair<int, int>(enemy.Id, round));
                    break;
                }
            }
        }

        /// <summary>
        /// Turns the raw release/death rounds into the two numbers worth reading:
        /// how long the player actually stood inside someone's reach, and how many
        /// enemies never got there at all.
        /// </summary>
        private static void FinishContact(BattleState state, BattleResult result, int lastRound)
        {
            int enemiesSeen = 0;
            foreach (UnitState enemy in state.Units)
                if (enemy.Faction == Faction.Enemy) enemiesSeen++;

            for (int i = 0; i < result.EnemyReleases.Count; i++)
            {
                KeyValuePair<int, int> release = result.EnemyReleases[i];
                if (release.Value == 1) result.EnemiesInContactOnRound1++;

                // An enemy still alive at the end was in contact right to the end.
                int end = lastRound;
                for (int d = 0; d < result.EnemyDeaths.Count; d++)
                    if (result.EnemyDeaths[d].Key == release.Key) { end = result.EnemyDeaths[d].Value; break; }

                int turns = end - release.Value;
                if (turns > 0) result.ContactTurns += turns;
            }

            result.EnemiesNeverInContact = enemiesSeen - result.EnemyReleases.Count;
            if (result.EnemiesNeverInContact < 0) result.EnemiesNeverInContact = 0;
        }

        private static bool HasEntryFor(List<KeyValuePair<int, int>> entries, int unitId)
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Key == unitId) return true;
            return false;
        }

        /// <summary>
        /// The next command, and whether the sampling noise picked it rather than
        /// the strategy.
        ///
        /// The rng calls are untouched and in the same order, so this reports on
        /// the existing decision — it does not add a draw or change one.
        /// </summary>
        private static ICommand Decide(BattleState state, UnitState unit,
                                       SimulationConfig config, DeterministicRandom rng,
                                       out bool fromNoise)
        {
            fromNoise = false;

            if (rng.Chance(config.NoisePercent))
            {
                List<ICommand> legal = LegalCommands.For(state, unit);
                if (legal.Count > 0)
                {
                    fromNoise = true;
                    return legal[rng.NextInt(legal.Count)];
                }
            }

            return config.Strategy.DecideNext(state, unit, rng);
        }

        /// <summary>Skills, as opposed to the five basic actions every unit has.</summary>
        private static bool IsSkill(ActionKind kind) => kind >= ActionKind.Taunt;

        /// <summary>
        /// Reports the end of a round at most once.
        ///
        /// Idempotent because the loop has several exits and the alternative —
        /// a call before every break — is the kind of thing that stays correct
        /// until someone adds a seventh break.
        /// </summary>
        private static void ObserveRoundEnd(SimulationConfig config, BattleState state,
                                            int round, ref int roundsObserved)
        {
            if (config.Observer == null || round <= roundsObserved || round == 0) return;
            roundsObserved = round;
            config.Observer.RoundEnded(round, state);
        }

        private static void Tally(EffectLog log, BattleState state, BattleResult result,
                                  int routeProbeRow, int round)
        {
            for (int i = 0; i < log.Count; i++)
            {
                if (log[i] is CounterAttacked) result.CountersMade++;
                else if (log[i] is UnitSpawned) result.ReinforcementsArrived++;
                else if (log[i] is UnitRested rested) { if (IsPlayer(state, rested.UnitId)) result.RestsTaken++; }
                else if (log[i] is UnitMoved moved) RecordCrossing(moved, state, result, routeProbeRow);
                else if (log[i] is UnitDied died)
                {
                    // M7 needs the round an enemy stopped threatening anyone.
                    if (died.Faction == Faction.Enemy)
                        result.EnemyDeaths.Add(new KeyValuePair<int, int>(died.UnitId, round));
                }
                else if (log[i] is ApReset reset)
                {
                    // Only the player side feeds M2; enemy AP is not the metric.
                    if (IsPlayer(state, reset.UnitId)) result.ApUnused += reset.Wasted;
                }
            }
        }

        /// <summary>
        /// M6: which gap in the dividing wall a player unit used.
        ///
        /// The whole path has to be scanned. A unit with MOVE 4 walks over the
        /// wall row and lands past it in one command, so its To is already on the
        /// far side — looking only at From/To would report "never crossed" for a
        /// unit that plainly did.
        /// </summary>
        private static void RecordCrossing(UnitMoved moved, BattleState state, BattleResult result, int routeProbeRow)
        {
            if (routeProbeRow < 0 || moved.Path == null || moved.Path.Length == 0) return;
            if (!IsPlayer(state, moved.UnitId)) return;

            // Only the FIRST crossing per unit counts: a unit that walks back and
            // forth over the wall would otherwise vote several times.
            for (int i = 0; i < result.UnitCrossings.Count; i++)
                if (result.UnitCrossings[i].Key == moved.UnitId) return;

            for (int i = 0; i < moved.Path.Length; i++)
            {
                if (moved.Path[i].Y != routeProbeRow) continue;

                result.UnitCrossings.Add(new KeyValuePair<int, int>(moved.UnitId, moved.Path[i].X));
                if (result.FirstCrossingX < 0) result.FirstCrossingX = moved.Path[i].X;
                return;
            }
        }

        private static bool IsPlayer(BattleState state, int unitId)
        {
            UnitState u = state.FindUnit(unitId);
            return u != null && u.Faction == Faction.Player && !u.MustSurvive;
        }

        /// <summary>
        /// Which action M1 should count this command as.
        ///
        /// The four skills were missing here until 2026-08-16, so every skill a
        /// strategy issued was silently dropped from the action mix. That made
        /// the two readings of a negative result — "the kit is used and loses"
        /// versus "the kit is never used" — impossible to tell apart, and the
        /// price sweep in §21 could not distinguish them either.
        ///
        /// EndTurnCommand still returns false, and should: it is a phase change,
        /// not something a unit spends its turn on.
        /// </summary>
        private static bool TryClassify(ICommand command, out ActionKind kind)
        {
            if (command is MoveCommand) { kind = ActionKind.Move; return true; }
            if (command is AttackCommand) { kind = ActionKind.Attack; return true; }
            if (command is GuardCommand) { kind = ActionKind.Guard; return true; }
            if (command is RestCommand) { kind = ActionKind.Rest; return true; }
            if (command is WaitCommand) { kind = ActionKind.Wait; return true; }
            if (command is TauntCommand) { kind = ActionKind.Taunt; return true; }
            if (command is SlowCommand) { kind = ActionKind.Slow; return true; }
            if (command is PushCommand) { kind = ActionKind.Push; return true; }
            if (command is PurifyCommand) { kind = ActionKind.Purify; return true; }
            // Sits above Taunt in the enum, so IsSkill picks it up without a change.
            if (command is ArmorBreakCommand) { kind = ActionKind.ArmorBreak; return true; }
            kind = ActionKind.Wait;
            return false;
        }

        // ------------------------------------------------------------ reporting

        public static SimulationSummary Summarise(List<BattleResult> results)
        {
            SimulationSummary s = new SimulationSummary();
            if (results.Count == 0) return s;

            s.Map = results[0].Map;
            s.Strategy = results[0].Strategy;
            s.Runs = results.Count;
            s.RouteProbeRow = results[0].RouteProbeRow;

            Dictionary<string, int> compositions = new Dictionary<string, int>();
            Dictionary<string, int> unitCompositions = new Dictionary<string, int>();
            int totalCompositions = 0;
            int totalUnitCompositions = 0;
            int unitCompositionsWithSkill = 0;
            long releaseSum = 0, contactTurnsSum = 0, contactRound1Sum = 0;
            int releaseCount = 0;
            int resolved = 0;
            long apGranted = 0, apUnused = 0;
            long turnSum = 0;
            long exposureSum = 0;
            int exposureCount = 0;
            long hpSum = 0;
            long hpMaxSum = 0;

            s.MinTurns = int.MaxValue;

            for (int i = 0; i < results.Count; i++)
            {
                BattleResult r = results[i];

                if (r.HitRoundCap) s.Unresolved++;
                else if (r.IsWin) s.Wins++;
                else s.Losses++;

                // M3 only counts battles that actually finished. A run that tripped
                // the safety net has no meaningful length, and averaging the cap
                // value in would quietly invent a number.
                if (!r.HitRoundCap)
                {
                    resolved++;
                    turnSum += r.Turns;
                    if (r.Turns < s.MinTurns) s.MinTurns = r.Turns;
                    if (r.Turns > s.MaxTurns) s.MaxTurns = r.Turns;
                }

                apGranted += r.ApGranted;
                apUnused += r.ApUnused;
                hpSum += r.FinalPlayerHp;
                hpMaxSum += r.PlayerMaxHp;
                s.StrategySkillActions += r.StrategySkillActions;
                s.NoiseSkillActions += r.NoiseSkillActions;

                for (int c = 0; c < r.ActionCompositions.Count; c++)
                {
                    string key = r.ActionCompositions[c];
                    int n;
                    compositions[key] = compositions.TryGetValue(key, out n) ? n + 1 : 1;
                    totalCompositions++;
                }

                for (int c = 0; c < r.UnitActionCompositions.Count; c++)
                {
                    string key = r.UnitActionCompositions[c];
                    int n;
                    unitCompositions[key] = unitCompositions.TryGetValue(key, out n) ? n + 1 : 1;
                    totalUnitCompositions++;
                    if (BattleResult.ContainsSkill(key)) unitCompositionsWithSkill++;
                }

                // M7
                for (int e = 0; e < r.EnemyReleases.Count; e++)
                {
                    int round = r.EnemyReleases[e].Value;
                    releaseSum += round;
                    releaseCount++;
                    int n;
                    s.ReleaseHistogram[round] = s.ReleaseHistogram.TryGetValue(round, out n) ? n + 1 : 1;
                }

                contactTurnsSum += r.ContactTurns;
                contactRound1Sum += r.EnemiesInContactOnRound1;
                s.EnemiesNeverInContact += r.EnemiesNeverInContact;

                for (int e = 0; e < r.EndOfTurnExposure.Count; e++)
                {
                    int exposure = r.EndOfTurnExposure[e];
                    exposureSum += exposure;
                    exposureCount++;
                    int n;
                    s.ExposureHistogram[exposure] = s.ExposureHistogram.TryGetValue(exposure, out n) ? n + 1 : 1;
                }

                // M6. A disabled probe measured nothing, so it must not fill the
                // "never crossed" bucket — that would read as a result.
                if (r.RouteProbeRow >= 0)
                {
                    if (r.FirstCrossingX < 0)
                    {
                        s.RunsWithoutCrossing++;
                    }
                    else
                    {
                        int n;
                        s.RouteHistogram[r.FirstCrossingX] =
                            s.RouteHistogram.TryGetValue(r.FirstCrossingX, out n) ? n + 1 : 1;
                    }
                }

                for (int c = 0; c < r.UnitCrossings.Count; c++)
                {
                    int x = r.UnitCrossings[c].Value;
                    int n;
                    s.UnitRouteHistogram[x] = s.UnitRouteHistogram.TryGetValue(x, out n) ? n + 1 : 1;
                }
            }

            // Sorted so ties resolve the same way every run (determinism rule 2).
            List<int> routeXs = new List<int>(s.RouteHistogram.Keys);
            routeXs.Sort();
            int topRouteCount = 0;
            int crossedRuns = results.Count - s.RunsWithoutCrossing;
            for (int i = 0; i < routeXs.Count; i++)
            {
                if (s.RouteHistogram[routeXs[i]] <= topRouteCount) continue;
                topRouteCount = s.RouteHistogram[routeXs[i]];
                s.TopRouteX = routeXs[i];
            }
            s.TopRoutePercent = crossedRuns == 0 ? 0 : topRouteCount * 100 / crossedRuns;

            s.MeanReleaseRoundX100 = releaseCount == 0 ? 0 : (int)(releaseSum * 100 / releaseCount);
            s.MeanContactTurnsX100 = (int)(contactTurnsSum * 100 / results.Count);
            s.MeanContactOnRound1X100 = (int)(contactRound1Sum * 100 / results.Count);

            s.WinRatePercent = (int)(s.Wins * 100L / results.Count);
            s.ApWastePercent = apGranted == 0 ? 0 : (int)(apUnused * 100 / apGranted);
            s.MeanTurnsX100 = resolved == 0 ? 0 : (int)(turnSum * 100 / resolved);
            s.MeanExposureX100 = exposureCount == 0 ? 0 : (int)(exposureSum * 100 / exposureCount);
            s.MeanFinalPlayerHp = (int)(hpSum / results.Count);
            s.ResolvedRuns = resolved;

            // -1 rather than 0: an encounter with no fighting HP has no ratio,
            // and 0 would read as "the party was wiped".
            s.MeanRemainingHpPerMille = hpMaxSum <= 0 ? -1 : (int)(hpSum * 1000 / hpMaxSum);

            if (s.MinTurns == int.MaxValue) s.MinTurns = 0;

            // Sorted so the report is stable run to run (determinism rule 2).
            List<string> keys = new List<string>(compositions.Keys);
            keys.Sort();
            string top = null;
            int topCount = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                s.CompositionCounts.Add(new KeyValuePair<string, int>(keys[i], compositions[keys[i]]));
                if (compositions[keys[i]] > topCount) { topCount = compositions[keys[i]]; top = keys[i]; }
            }
            s.TopComposition = top ?? "(none)";
            s.TopCompositionPercent = totalCompositions == 0 ? 0 : topCount * 100 / totalCompositions;
            s.DistinctCompositions = keys.Count;

            // Top-3 share: a low top-1 share can still hide a very short tail.
            List<KeyValuePair<string, int>> ranked = new List<KeyValuePair<string, int>>(s.CompositionCounts);
            ranked.Sort((a, b) =>
            {
                int byCount = b.Value.CompareTo(a.Value);
                return byCount != 0 ? byCount : string.CompareOrdinal(a.Key, b.Key);
            });

            int top3 = 0;
            for (int i = 0; i < ranked.Count && i < 3; i++)
            {
                s.TopCompositions.Add(ranked[i]);
                top3 += ranked[i].Value;
            }
            s.Top3CompositionPercent = totalCompositions == 0 ? 0 : top3 * 100 / totalCompositions;

            // M1b — same counting, per unit instead of per turn.
            List<string> unitKeys = new List<string>(unitCompositions.Keys);
            unitKeys.Sort();
            s.DistinctUnitCompositions = unitKeys.Count;

            List<KeyValuePair<string, int>> rankedUnits = new List<KeyValuePair<string, int>>();
            for (int i = 0; i < unitKeys.Count; i++)
                rankedUnits.Add(new KeyValuePair<string, int>(unitKeys[i], unitCompositions[unitKeys[i]]));
            rankedUnits.Sort((a, b) =>
            {
                int byCount = b.Value.CompareTo(a.Value);
                return byCount != 0 ? byCount : string.CompareOrdinal(a.Key, b.Key);
            });

            int unitTop3 = 0;
            for (int i = 0; i < rankedUnits.Count && i < 3; i++) unitTop3 += rankedUnits[i].Value;
            s.TopUnitCompositionPercent = totalUnitCompositions == 0 || rankedUnits.Count == 0
                ? 0 : rankedUnits[0].Value * 100 / totalUnitCompositions;
            s.Top3UnitCompositionPercent = totalUnitCompositions == 0
                ? 0 : unitTop3 * 100 / totalUnitCompositions;
            s.UnitTurnsWithSkillPercent = totalUnitCompositions == 0
                ? 0 : unitCompositionsWithSkill * 100 / totalUnitCompositions;

            return s;
        }

        /// <summary>One row per battle. Raw export for offline analysis.</summary>
        public static string ToCsv(List<BattleResult> results)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("map,strategy,seed,outcome,turns,hit_round_cap,player_turns,ap_granted,ap_unused," +
                          "final_player_hp,enemies_remaining,mean_exposure_x100,first_crossing_x," +
                          "unit_crossings,contact_round1,contact_turns,never_in_contact,state_hash");

            for (int i = 0; i < results.Count; i++)
            {
                BattleResult r = results[i];
                long exposureSum = 0;
                for (int e = 0; e < r.EndOfTurnExposure.Count; e++) exposureSum += r.EndOfTurnExposure[e];
                int meanExposure = r.EndOfTurnExposure.Count == 0
                    ? 0 : (int)(exposureSum * 100 / r.EndOfTurnExposure.Count);

                sb.Append(r.Map).Append(',').Append(r.Strategy).Append(',').Append(r.Seed).Append(',')
                  .Append(r.Outcome).Append(',').Append(r.Turns).Append(',').Append(r.HitRoundCap ? 1 : 0).Append(',')
                  .Append(r.PlayerTurns).Append(',').Append(r.ApGranted).Append(',').Append(r.ApUnused).Append(',')
                  .Append(r.FinalPlayerHp).Append(',').Append(r.EnemiesRemaining).Append(',')
                  .Append(meanExposure).Append(',').Append(r.FirstCrossingX).Append(',');

                // "unit:x" pairs, so a two-unit battle can be re-analysed offline
                // without re-running it. Pipe-separated to stay inside one field.
                for (int c = 0; c < r.UnitCrossings.Count; c++)
                {
                    if (c > 0) sb.Append('|');
                    sb.Append(r.UnitCrossings[c].Key).Append(':').Append(r.UnitCrossings[c].Value);
                }

                sb.Append(',').Append(r.EnemiesInContactOnRound1).Append(',').Append(r.ContactTurns)
                  .Append(',').Append(r.EnemiesNeverInContact)
                  .Append(',').Append(r.FinalStateHash).AppendLine();
            }

            return sb.ToString();
        }
    }
}
