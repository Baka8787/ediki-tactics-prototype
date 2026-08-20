using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ediki.Core;

namespace Ediki.Sim
{
    /// <summary>
    /// Why a battle was flagged for triage. Strings rather than an enum because
    /// they are written into anomalies.json and read by things outside this
    /// project — renaming one is a breaking change to the file format, and a
    /// constant makes that visible at the call site.
    /// </summary>
    public static class FailureReason
    {
        /// <summary>
        /// The battle never reached an outcome: it was still InProgress when the
        /// harness stopped stepping it.
        ///
        /// NOT the same thing as losing to an encounter's own clock. An encounter
        /// with `objective type=rout turns=20` produces a perfectly ordinary
        /// Defeat when TurnIndex passes 20 — the rule layer decided that, and the
        /// run is resolved. This reason means nobody decided anything.
        /// </summary>
        public const string TimeoutUnresolved = "TIMEOUT_UNRESOLVED";

        /// <summary>
        /// Lost while still holding most of the party's HP — the signature of
        /// running out of time rather than being killed (playtest-metrics: 550 of
        /// 1150 HP left and still losing 71% of the time).
        /// </summary>
        public const string DefeatWithHighRemainingHp = "DEFEAT_WITH_HIGH_REMAINING_HP";

        /// <summary>
        /// The strategy issued skill commands on a run whose caller declared it
        /// should not. Only ever raised when the caller opted in — see
        /// SimulationConfig.ExpectNoStrategySkillUse.
        /// </summary>
        public const string UnexpectedSkillUsage = "UNEXPECTED_SKILL_USAGE";

        /// <summary>
        /// The run stopped because BattleSimulator rejected a command the harness
        /// issued, not because the clock ran out.
        ///
        /// This exists because HitRoundCap alone cannot tell the two apart: it is
        /// set from "outcome is still InProgress", and the runner also breaks out
        /// of the loop when an EndTurnCommand is refused. Both land in the same
        /// Unresolved bucket, and only one of them is a timeout — the other is an
        /// engine-level fault worth looking at on its own.
        /// </summary>
        public const string AbortedByRejectedCommand = "ABORTED_BY_REJECTED_COMMAND";
    }

    /// <summary>Tunable cut-offs for the detector. Data, so a run can say what it used.</summary>
    public sealed class AnomalyThresholds
    {
        /// <summary>
        /// A Defeat holding MORE than this share of the party's maximum HP is
        /// flagged. Percent rather than a float so the comparison is exact
        /// integer arithmetic and cannot drift between platforms.
        /// </summary>
        public int HighHpDefeatPercent = 40;

        public static AnomalyThresholds Default => new AnomalyThresholds();
    }

    /// <summary>
    /// One flagged battle, with everything needed to reproduce it.
    ///
    /// Reasons is a LIST: a run that timed out while still holding most of its HP
    /// is both things at once, and collapsing that to one label throws away the
    /// half that explains the other.
    /// </summary>
    public sealed class BattleAnomaly
    {
        public string Encounter;
        public string Strategy;
        public int Seed;

        public readonly List<string> Reasons = new List<string>();

        public int RemainingHp;
        public int MaxHp;

        /// <summary>
        /// RemainingHp / MaxHp in thousandths. Integer so the JSON is byte-stable;
        /// the float is never computed, let alone formatted by a culture.
        /// </summary>
        public int RemainingHpPerMille;

        /// <summary>Rounds actually played. One player phase + one enemy phase each.</summary>
        public int Rounds;

        /// <summary>The harness safety net this run was given, for context on Rounds.</summary>
        public int RoundCap;

        /// <summary>Victory / Defeat / InProgress. InProgress means unresolved.</summary>
        public string Result;

        public bool Has(string reason) => Reasons.Contains(reason);

        /// <summary>The ratio as a fixed 3-decimal string, invariant culture.</summary>
        public string RemainingHpRatioText =>
            (RemainingHpPerMille / 1000).ToString(CultureInfo.InvariantCulture) + "." +
            (RemainingHpPerMille % 1000).ToString("000", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads finished battles and decides which ones a human should look at.
    ///
    /// Pure observation over BattleResult. It runs no battles, holds no state and
    /// touches no rules — every number it reads was already collected by
    /// SimulationRunner, which is the point: a triage layer that re-simulated
    /// anything could disagree with the batch it is triaging.
    /// </summary>
    public static class AnomalyDetector
    {
        /// <summary>The anomaly for this battle, or null when nothing is wrong with it.</summary>
        public static BattleAnomaly Detect(BattleResult result, AnomalyThresholds thresholds = null)
        {
            if (result == null) return null;
            AnomalyThresholds limits = thresholds ?? AnomalyThresholds.Default;

            BattleAnomaly anomaly = new BattleAnomaly
            {
                Encounter = result.Map,
                Strategy = result.Strategy,
                Seed = result.Seed,
                RemainingHp = result.FinalPlayerHp,
                MaxHp = result.PlayerMaxHp,
                RemainingHpPerMille = PerMille(result.FinalPlayerHp, result.PlayerMaxHp),
                Rounds = result.PlayerTurns,
                RoundCap = result.RoundCap,
                Result = result.Outcome.ToString()
            };

            // A. Never reached an outcome.
            if (result.HitRoundCap) anomaly.Reasons.Add(FailureReason.TimeoutUnresolved);

            // A'. ...and if it was a refused command rather than the clock, say so.
            // Deliberately additive: the run is still unresolved, and dropping that
            // would hide it from anyone counting timeouts.
            if (result.EndedByRejectedCommand) anomaly.Reasons.Add(FailureReason.AbortedByRejectedCommand);

            // B. Lost with the party largely intact.
            if (result.Outcome == BattleOutcome.Defeat && IsAbove(result.FinalPlayerHp,
                                                                 result.PlayerMaxHp,
                                                                 limits.HighHpDefeatPercent))
            {
                anomaly.Reasons.Add(FailureReason.DefeatWithHighRemainingHp);
            }

            // C. Skills issued by a strategy the caller said would not issue any.
            //
            // Gated on the caller's declaration, never on the strategy's name, and
            // counts only STRATEGY-issued skills. The 15% sampling noise draws
            // uniformly from LegalCommands, which includes skills, so it fires them
            // for strategies that have no skill logic at all — measured at 1-2% of
            // unit-turns for corridor-hold. Counting those would flag every run.
            if (result.ExpectedNoStrategySkillUse && result.StrategySkillActions > 0)
                anomaly.Reasons.Add(FailureReason.UnexpectedSkillUsage);

            return anomaly.Reasons.Count == 0 ? null : anomaly;
        }

        /// <summary>Every flagged battle in a batch, in a stable order.</summary>
        public static List<BattleAnomaly> DetectAll(IList<BattleResult> results,
                                                    AnomalyThresholds thresholds = null)
        {
            List<BattleAnomaly> found = new List<BattleAnomaly>();
            if (results == null) return found;

            for (int i = 0; i < results.Count; i++)
            {
                BattleAnomaly anomaly = Detect(results[i], thresholds);
                if (anomaly != null) found.Add(anomaly);
            }

            Sort(found);
            return found;
        }

        /// <summary>
        /// Encounter, then strategy, then seed — ordinal throughout.
        ///
        /// The batch already produces results in a fixed order, but sorting here
        /// means the file does not change shape when someone reorders the map or
        /// strategy list, which is the whole value of a diffable diagnostic.
        /// </summary>
        public static void Sort(List<BattleAnomaly> anomalies)
        {
            anomalies.Sort((a, b) =>
            {
                int byMap = string.CompareOrdinal(a.Encounter, b.Encounter);
                if (byMap != 0) return byMap;
                int byStrategy = string.CompareOrdinal(a.Strategy, b.Strategy);
                if (byStrategy != 0) return byStrategy;
                return a.Seed.CompareTo(b.Seed);
            });
        }

        /// <summary>remaining/max &gt; percent, in exact integer arithmetic.</summary>
        private static bool IsAbove(int remaining, int max, int percent)
        {
            if (max <= 0) return false;
            return (long)remaining * 100 > (long)max * percent;
        }

        private static int PerMille(int remaining, int max)
        {
            if (max <= 0) return 0;
            return (int)((long)remaining * 1000 / max);
        }
    }

    /// <summary>
    /// Writes anomalies.json.
    ///
    /// Hand-rolled rather than JsonUtility because that lives in UnityEngine and
    /// Ediki.Sim is engine-free (A1). Hand-rolled also buys the thing the format
    /// actually needs: field order is the order written here, so the file is
    /// byte-identical for the same input instead of depending on how a serialiser
    /// happened to walk the type.
    /// </summary>
    public static class AnomalyReport
    {
        /// <summary>
        /// Bump this when a field is added, removed or given a new meaning, so a
        /// reader can tell an old file from a new one.
        /// </summary>
        public const int FormatVersion = 1;

        public static string ToJson(IList<BattleAnomaly> anomalies, AnomalyThresholds thresholds = null)
        {
            AnomalyThresholds limits = thresholds ?? AnomalyThresholds.Default;
            StringBuilder sb = new StringBuilder();

            sb.Append("{\n");
            sb.Append("  \"format_version\": ").Append(Int(FormatVersion)).Append(",\n");
            sb.Append("  \"high_hp_defeat_percent\": ").Append(Int(limits.HighHpDefeatPercent)).Append(",\n");
            sb.Append("  \"count\": ").Append(Int(anomalies == null ? 0 : anomalies.Count)).Append(",\n");
            sb.Append("  \"anomalies\": [");

            if (anomalies == null || anomalies.Count == 0)
            {
                sb.Append("]\n}\n");
                return sb.ToString();
            }

            sb.Append('\n');
            for (int i = 0; i < anomalies.Count; i++)
            {
                BattleAnomaly a = anomalies[i];
                sb.Append("    {\n");
                sb.Append("      \"encounter\": ").Append(Str(a.Encounter)).Append(",\n");
                sb.Append("      \"strategy\": ").Append(Str(a.Strategy)).Append(",\n");
                sb.Append("      \"seed\": ").Append(Int(a.Seed)).Append(",\n");
                sb.Append("      \"failure_reasons\": [");
                for (int r = 0; r < a.Reasons.Count; r++)
                {
                    if (r > 0) sb.Append(", ");
                    sb.Append(Str(a.Reasons[r]));
                }
                sb.Append("],\n");
                sb.Append("      \"result\": ").Append(Str(a.Result)).Append(",\n");
                sb.Append("      \"rounds\": ").Append(Int(a.Rounds)).Append(",\n");
                sb.Append("      \"round_cap\": ").Append(Int(a.RoundCap)).Append(",\n");
                sb.Append("      \"remaining_hp\": ").Append(Int(a.RemainingHp)).Append(",\n");
                sb.Append("      \"max_hp\": ").Append(Int(a.MaxHp)).Append(",\n");
                sb.Append("      \"remaining_hp_ratio\": ").Append(a.RemainingHpRatioText).Append('\n');
                sb.Append("    }").Append(i == anomalies.Count - 1 ? "\n" : ",\n");
            }

            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Str(string value)
        {
            if (value == null) return "null";

            StringBuilder sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
