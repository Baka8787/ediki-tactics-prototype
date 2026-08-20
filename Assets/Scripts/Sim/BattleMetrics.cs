using System.Collections.Generic;
using System.Text;
using Ediki.Core;

namespace Ediki.Sim
{
    /// <summary>
    /// Every action a player unit can spend AP on, as M1 counts it.
    ///
    /// The skill entries were added 2026-08-16. Until then this stopped at Wait,
    /// so Taunt/Slow/Push/Purify were classified as "not an action" and never
    /// reached M1 at all — which made "the control kit is a net loss" and "the
    /// control kit is never used" indistinguishable in the telemetry, and left
    /// every skill conclusion (§17.3 / §20 / §21) resting on half its evidence.
    ///
    /// The five original values keep their numbers on purpose: composition keys
    /// print in enum order, so a battle that issues no skills produces exactly
    /// the string it produced before and every recorded M1 number stays
    /// comparable.
    /// </summary>
    public enum ActionKind
    {
        Move = 0, Attack = 1, Guard = 2, Rest = 3, Wait = 4,
        Taunt = 5, Slow = 6, Push = 7, Purify = 8, ArmorBreak = 9
    }

    /// <summary>
    /// Everything one simulated battle contributes to M1-M5.
    /// Collected by observing commands and state; it never changes either.
    /// </summary>
    public sealed class BattleResult
    {
        public string Map;
        public string Strategy;
        public int Seed;

        public BattleOutcome Outcome;
        public int Turns;                       // M3
        public bool HitRoundCap;                // safety net tripped, not a rule

        /// <summary>
        /// Rounds actually stepped: one player phase plus one enemy phase each.
        ///
        /// This is the round counter, not a second metric — the runner increments
        /// it once per iteration of its own loop, so PlayerTurns == rounds played.
        /// Turns above is state.TurnIndex, which the RULE layer advances, and the
        /// two are not interchangeable when a battle ends mid-round.
        /// </summary>
        public int PlayerTurns;

        /// <summary>
        /// The harness safety net this run was given (SimulationConfig.MaxRounds).
        ///
        /// Carried on the result so a reader can tell "stopped at 60 of 60" from
        /// "stopped at 12 of 60" without the config that produced it. NOT a rule:
        /// R-WIN-04 says the rules have no turn limit, and an encounter's own
        /// `objective turns=N` is a different clock that produces a real outcome.
        /// </summary>
        public int RoundCap;

        /// <summary>
        /// Sum of MaxHp over the player units that fight, dead ones included.
        ///
        /// The denominator has to keep counting the dead or "we lost with 80% of
        /// our HP" would be true of a wipe with one survivor. Objective props are
        /// excluded for the same reason FinalPlayerHp excludes them: they never
        /// act and their HP is not the party's staying power.
        /// </summary>
        public int PlayerMaxHp;

        /// <summary>
        /// The run stopped because BattleSimulator refused a command the harness
        /// issued, not because the round cap ran out.
        ///
        /// Both leave the battle InProgress and therefore both set HitRoundCap,
        /// which is why this flag exists: one is a timeout and the other is an
        /// engine fault, and the Unresolved bucket cannot tell them apart.
        /// </summary>
        public bool EndedByRejectedCommand;

        /// <summary>
        /// Skill commands the STRATEGY chose, and skill commands the 15% sampling
        /// noise chose, counted separately.
        ///
        /// They have to be separate. LegalCommands.For lists every affordable
        /// skill, and the noise draws from that list uniformly, so a strategy with
        /// no skill logic at all still emits skills — measured at 1-2% of
        /// unit-turns for corridor-hold on gym-opening. A skill-use check that
        /// could not tell the two apart would be reporting the sampling device.
        /// </summary>
        public int StrategySkillActions;
        public int NoiseSkillActions;

        /// <summary>
        /// Whether the caller declared this run's strategy should issue no skills.
        ///
        /// Carried from the config so the detector never has to guess from a
        /// strategy's name. Default false = the question was not asked, and
        /// UNEXPECTED_SKILL_USAGE can never be raised.
        /// </summary>
        public bool ExpectedNoStrategySkillUse;

        /// <summary>
        /// M2 under the carry-over economy (OD-21): the denominator is AP REGEN,
        /// and the numerator is regen that spilled over the cap. Leftover AP is
        /// banked, not wasted, so counting it as waste would be wrong.
        /// </summary>
        public int ApGranted;
        public int ApUnused;

        public int RestsTaken;

        /// <summary>M1: one entry per player turn, e.g. "Attack x2" or "Move,Attack".</summary>
        public readonly List<string> ActionCompositions = new List<string>();

        /// <summary>
        /// M1b: one entry per unit per turn.
        ///
        /// The combined key above concatenates every unit's actions, so adding a
        /// second unit multiplies the number of distinct mixes without any new
        /// decision existing. Counting per unit is the only version that stays
        /// comparable across party sizes (OD-26).
        /// </summary>
        public readonly List<string> UnitActionCompositions = new List<string>();

        /// <summary>M4: player's effective exposure at the end of each player turn.</summary>
        public readonly List<int> EndOfTurnExposure = new List<int>();

        /// <summary>
        /// M6: x of the first player step onto the route probe row.
        /// -1 = the wall was never crossed (or the probe was off — see RouteProbeRow).
        /// </summary>
        public int FirstCrossingX = -1;

        /// <summary>
        /// Which row M6 watched, carried through so the summary can tell
        /// "nobody crossed" apart from "nothing was being measured".
        /// -1 = the probe was off.
        /// </summary>
        public int RouteProbeRow = -1;

        /// <summary>
        /// M6 per unit — (unitId, x) in crossing order, first crossing only.
        ///
        /// Two units that both take the west gap is a different finding from two
        /// units that split up, and the battle-level number cannot tell them
        /// apart. Kept per unit so a second player unit does not silently average
        /// the route distribution away.
        /// </summary>
        public readonly List<KeyValuePair<int, int>> UnitCrossings = new List<KeyValuePair<int, int>>();

        /// <summary>
        /// M7: (enemyId, round) for the first round each enemy could actually
        /// reach a player unit — the "release time" of the scheduling model.
        ///
        /// Why it is worth its own metric: with every enemy released on round 1
        /// the battle is a pure ordering problem and the spatial layer does
        /// nothing. Release is what makes the greedy ordering wrong, so it is the
        /// thing the whole "position matters" claim rests on.
        /// </summary>
        public readonly List<KeyValuePair<int, int>> EnemyReleases = new List<KeyValuePair<int, int>>();

        /// <summary>(enemyId, round) for enemies that died, so contact length is computable.</summary>
        public readonly List<KeyValuePair<int, int>> EnemyDeaths = new List<KeyValuePair<int, int>>();

        /// <summary>
        /// Sum over enemies of (death round − release round): how many enemy turns
        /// the player actually spent inside someone's reach.
        ///
        /// This is the honest unit of threat. An enemy with huge damage that never
        /// closes contributes zero, and no per-turn damage number can say that.
        /// </summary>
        public int ContactTurns;

        /// <summary>Enemies already able to reach a player on round 1.</summary>
        public int EnemiesInContactOnRound1;

        /// <summary>Enemies that never reached anyone before the battle ended.</summary>
        public int EnemiesNeverInContact;

        public int FinalPlayerHp;
        public int EnemiesRemaining;
        public uint FinalStateHash;

        /// <summary>How often the reserve-AP counter actually fired. Zero means the
        /// mechanic never engaged, which is a different finding from "it did not help".</summary>
        public int CountersMade;
        public int ReinforcementsArrived;

        public bool IsWin => Outcome == BattleOutcome.Victory;

        /// <summary>
        /// Names in ActionKind order. The array length is what sizes the tally
        /// below, so a new ActionKind that nobody names here throws on its first
        /// use instead of being dropped — the failure mode that hid the whole
        /// control kit from M1 for two rounds of measurement.
        /// </summary>
        private static readonly string[] ActionNames =
            { "Move", "Attack", "Guard", "Rest", "Wait", "Taunt", "Slow", "Push", "Purify", "ArmorBreak" };

        public static string ComposeKey(List<ActionKind> actions)
        {
            if (actions.Count == 0) return "(nothing)";

            int[] counts = new int[ActionNames.Length];
            for (int i = 0; i < actions.Count; i++) counts[(int)actions[i]]++;

            StringBuilder sb = new StringBuilder();
            for (int k = 0; k < ActionNames.Length; k++)
            {
                if (counts[k] == 0) continue;
                if (sb.Length > 0) sb.Append('+');
                sb.Append(ActionNames[k]);
                if (counts[k] > 1) sb.Append('x').Append(counts[k]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Does this composition key contain at least one skill?
        ///
        /// Reads the key rather than adding a counter, so it stays an aggregation
        /// of telemetry that already exists. It is here because Describe only
        /// prints the top three mixes: a skill used in 4% of turns would sit in
        /// CompositionCounts and never appear in the report, which is close enough
        /// to invisible to repeat the defect this whole change is fixing.
        /// </summary>
        public static bool ContainsSkill(string composition)
        {
            if (string.IsNullOrEmpty(composition)) return false;
            for (int k = (int)ActionKind.Taunt; k < ActionNames.Length; k++)
                if (composition.Contains(ActionNames[k])) return true;
            return false;
        }
    }

    /// <summary>Aggregate over one batch: a single (map, strategy) cell of the matrix.</summary>
    public sealed class SimulationSummary
    {
        public string Map;
        public string Strategy;
        public int Runs;

        // M5
        public int Wins;
        public int Losses;
        public int Unresolved;
        public int WinRatePercent;

        // M1
        public string TopComposition;
        public int TopCompositionPercent;

        /// <summary>
        /// How many different action mixes ever showed up, and how much of the play
        /// the three most common ones account for. This is the direct read on
        /// "is it monotonous" — top-share alone can look fine while the tail is empty.
        /// </summary>
        public int DistinctCompositions;
        public int Top3CompositionPercent;
        public readonly List<KeyValuePair<string, int>> TopCompositions = new List<KeyValuePair<string, int>>();

        // M1b — the same three numbers counted per unit instead of per turn.
        // Compare THESE across encounters with different party sizes.
        public int DistinctUnitCompositions;
        public int TopUnitCompositionPercent;
        public int Top3UnitCompositionPercent;

        /// <summary>
        /// Share of unit-turns that contained at least one skill (taunt / slow /
        /// push / purify).
        ///
        /// Not a new measurement — it is M1b's own keys, summed. It is printed
        /// because a batch reporting "the control kit costs 12 points" and a batch
        /// reporting "the control kit was never issued" look identical in every
        /// other number, and until 2026-08-16 this project could not tell them
        /// apart at all.
        /// </summary>
        public int UnitTurnsWithSkillPercent;

        public readonly List<KeyValuePair<string, int>> CompositionCounts = new List<KeyValuePair<string, int>>();

        // M2
        public int ApWastePercent;

        // M3
        public int MinTurns;
        public int MaxTurns;
        public int MeanTurnsX100;

        // M4
        public readonly Dictionary<int, int> ExposureHistogram = new Dictionary<int, int>();
        public int MeanExposureX100;

        // M6 — route utilisation. Keyed by x on the probe row; the caller groups
        // the x values into named routes, because only the map knows where its
        // gaps are.
        public readonly Dictionary<int, int> RouteHistogram = new Dictionary<int, int>();

        /// <summary>Per-unit crossings, for maps with more than one player unit.</summary>
        public readonly Dictionary<int, int> UnitRouteHistogram = new Dictionary<int, int>();

        /// <summary>Runs where no player unit ever reached the probe row.</summary>
        public int RunsWithoutCrossing;

        /// <summary>The row M6 watched. -1 = the probe was off, so M6 says nothing.</summary>
        public int RouteProbeRow = -1;

        /// <summary>
        /// Share of the most-used route, as a percentage of the runs that crossed
        /// at all. Runs that never crossed used no route, so counting them in the
        /// denominator would make a dead route look less dominant than it is.
        /// </summary>
        public int TopRoutePercent;
        public int TopRouteX = -1;

        // M7 — release / contact. Keyed by round; round 1 means "in reach before
        // the player has moved at all".
        public readonly Dictionary<int, int> ReleaseHistogram = new Dictionary<int, int>();
        public int MeanReleaseRoundX100;

        /// <summary>Mean number of enemies already in reach on round 1 (H1's variable).</summary>
        public int MeanContactOnRound1X100;

        /// <summary>Mean enemy-turns the player spent inside someone's reach.</summary>
        public int MeanContactTurnsX100;

        /// <summary>Enemies that never reached anyone, summed over the batch.</summary>
        public int EnemiesNeverInContact;

        public int MeanFinalPlayerHp;

        /// <summary>
        /// Final player HP as a share of the party's maximum, in thousandths.
        /// -1 = the encounter had no fighting HP to measure, which is not zero.
        ///
        /// The absolute figure above cannot be compared across encounters with
        /// different rosters — 550 HP left is most of a party or a scrap of one.
        /// Computed as sum(final) / sum(max) over the batch, both of which the
        /// per-battle results already carry.
        /// </summary>
        public int MeanRemainingHpPerMille = -1;

        /// <summary>
        /// Skill commands over the whole batch, split by who chose them.
        ///
        /// UnitTurnsWithSkillPercent above cannot make this split: it reads
        /// composition keys, which record that a skill happened and not who
        /// decided on it. The 15% sampling noise draws from a legal set that
        /// includes skills, so a strategy with no skill logic still shows up
        /// there — these two are what tell the cases apart.
        /// </summary>
        public int StrategySkillActions;
        public int NoiseSkillActions;

        /// <summary>
        /// Battles that reached an outcome. Mean turns is undefined when this is
        /// zero, and MeanTurnsX100 reports 0 in that case — which reads as a very
        /// fast battle unless you check here first.
        /// </summary>
        public int ResolvedRuns;

        /// <summary>
        /// Where this cell's battles were fought, or null when nobody watched.
        ///
        /// Attached by the caller rather than computed in Summarise: it is
        /// accumulated by an observer during the runs, not derived from the
        /// BattleResults afterwards, and building it here would mean carrying a
        /// grid on every one of the 200 results in a cell.
        ///
        /// Diagnostic only. It is rendered into the text report and stays out of
        /// the CSV, which keeps every existing column and its meaning untouched.
        /// </summary>
        public BattleHeatmap Heatmap;

        /// <summary>
        /// Role-specific tallies for this cell, or null when nobody watched.
        ///
        /// Attached by the caller for the same reason Heatmap is: it is collected
        /// by an observer during the runs, not derived from the results after.
        /// Diagnostic only — it stays out of the CSV.
        /// </summary>
        public RoleMetrics Roles;

        public string Describe()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Map).Append(" / ").Append(Strategy).Append("  (").Append(Runs).AppendLine(" runs)");
            sb.Append("  M5 win rate      : ").Append(WinRatePercent).Append("%  (")
              .Append(Wins).Append("W / ").Append(Losses).Append("L / ").Append(Unresolved).AppendLine("unresolved)");
            sb.Append("  M1 top action mix: ").Append(TopComposition).Append("  ")
              .Append(TopCompositionPercent).AppendLine("% of player turns");
            sb.Append("  M1 variety       : ").Append(DistinctCompositions)
              .Append(" distinct mixes, top-3 = ").Append(Top3CompositionPercent).AppendLine("%");
            sb.Append("                     ");
            for (int i = 0; i < TopCompositions.Count; i++)
                sb.Append(TopCompositions[i].Key).Append(' ').Append(TopCompositions[i].Value).Append("   ");
            sb.AppendLine();
            sb.Append("  M1b per unit     : ").Append(DistinctUnitCompositions)
              .Append(" distinct, top ").Append(TopUnitCompositionPercent)
              .Append("%, top-3 = ").Append(Top3UnitCompositionPercent)
              .AppendLine("%   <- compare THIS across party sizes");
            sb.Append("  M1b skill use    : ").Append(UnitTurnsWithSkillPercent)
              .AppendLine("% of unit-turns issued a skill");
            sb.Append("  M2 AP waste      : ").Append(ApWastePercent).AppendLine("%");
            sb.Append("  M3 turns         : mean ").Append(MeanTurnsX100 / 100)
              .Append('.').Append((MeanTurnsX100 % 100).ToString("00"))
              .Append("  min ").Append(MinTurns).Append("  max ").AppendLine(MaxTurns.ToString());
            sb.Append("  M4 exposure      : mean ").Append(MeanExposureX100 / 100).Append('.')
              .Append((MeanExposureX100 % 100).ToString("00")).Append("   ");

            List<int> keys = new List<int>(ExposureHistogram.Keys);
            keys.Sort();
            for (int i = 0; i < keys.Count; i++)
                sb.Append('[').Append(keys[i]).Append(']').Append(ExposureHistogram[keys[i]]).Append(' ');
            sb.AppendLine();

            AppendContact(sb);
            AppendRoutes(sb);

            sb.Append("  final player HP  : mean ").Append(MeanFinalPlayerHp).AppendLine();

            if (Roles != null) sb.Append(Roles.Describe());

            // Last, and only when someone watched: it is the tallest thing in the
            // block and the scalar metrics above are what a reader scans first.
            if (Heatmap != null) sb.Append(Heatmap.Render());

            return sb.ToString();
        }

        /// <summary>
        /// M7. Round 1 contact count is the headline: when every enemy is in
        /// reach immediately, the battle has no spatial layer left to measure.
        /// </summary>
        private void AppendContact(StringBuilder sb)
        {
            sb.Append("  M7 contact       : round-1 reach ").Append(Fixed(MeanContactOnRound1X100))
              .Append(" enemies   mean release round ").Append(Fixed(MeanReleaseRoundX100))
              .Append("   contact turns ").AppendLine(Fixed(MeanContactTurnsX100));
            sb.Append("                     ");

            List<int> rounds = new List<int>(ReleaseHistogram.Keys);
            rounds.Sort();
            for (int i = 0; i < rounds.Count; i++)
                sb.Append('r').Append(rounds[i]).Append(':').Append(ReleaseHistogram[rounds[i]]).Append("  ");
            if (EnemiesNeverInContact > 0) sb.Append("never:").Append(EnemiesNeverInContact);
            sb.AppendLine();
        }

        private static string Fixed(int hundredths) =>
            (hundredths / 100) + "." + (hundredths % 100).ToString("00");

        /// <summary>
        /// M6. Silent when the probe is off — a map with no dividing wall has no
        /// routes, and printing an empty histogram there would read like a result.
        /// </summary>
        private void AppendRoutes(StringBuilder sb)
        {
            if (RouteProbeRow < 0) return;

            int crossed = Runs - RunsWithoutCrossing;
            sb.Append("  M6 route use     : ").Append(crossed).Append('/').Append(Runs)
              .Append(" runs crossed, top route x=").Append(TopRouteX)
              .Append(' ').Append(TopRoutePercent).AppendLine("%");
            sb.Append("                     ");

            List<int> xs = new List<int>(RouteHistogram.Keys);
            xs.Sort();
            for (int i = 0; i < xs.Count; i++)
                sb.Append("x=").Append(xs[i]).Append(':').Append(RouteHistogram[xs[i]]).Append("  ");
            if (RunsWithoutCrossing > 0) sb.Append("never:").Append(RunsWithoutCrossing);
            sb.AppendLine();

            // Only worth printing when a second unit exists to disagree with the first.
            int unitTotal = 0;
            foreach (KeyValuePair<int, int> kv in UnitRouteHistogram) unitTotal += kv.Value;
            if (unitTotal <= crossed) return;

            sb.Append("                     per unit: ");
            List<int> unitXs = new List<int>(UnitRouteHistogram.Keys);
            unitXs.Sort();
            for (int i = 0; i < unitXs.Count; i++)
                sb.Append("x=").Append(unitXs[i]).Append(':').Append(UnitRouteHistogram[unitXs[i]]).Append("  ");
            sb.AppendLine();
        }
    }
}
