using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ediki.Sim
{
    /// <summary>
    /// The one place a strategy name maps to a strategy.
    ///
    /// It exists because two callers now need that map — the metrics batch and
    /// replay — and a second copy would drift: a strategy added to one list and
    /// not the other produces a batch and a replay that quietly disagree about
    /// what "corridor-hold" means.
    ///
    /// Order is the order the batch runs them in, and Create returns a FRESH
    /// instance each call: strategies are stateless today, and handing out a
    /// shared one would make that an assumption rather than a fact.
    /// </summary>
    public static class StrategyCatalog
    {
        /// <summary>Every name Create accepts, in batch order.</summary>
        public static readonly string[] Names =
        {
            "corridor-hold",
            "tpa-order",
            "residue-aware",
            "decapitate",
            "control-hold",
            "sustain-hold",
            "shield-wall",
            "purify-hold",
            "charge",

            // Experiment instruments, NOT part of the standard batch. Registered
            // here so a flagged battle can still be replayed by name; the metrics
            // menu deliberately does not include any of them.
            "counter-reserve",
            "attack-only",
            "push-instrument",
            "slow-instrument",
            "taunt-instrument",

            // 1-ply evaluator. An INSTRUMENT for "can a one-step lookahead use the
            // signals the State already carries", not a player — see the class
            // comment before reading anything into its win rate.
            "one-ply"
        };

        public static bool IsKnown(string name) => Create(name) != null;

        /// <summary>The strategy with this name, or null. Never throws.</summary>
        public static IPlayerStrategy Create(string name)
        {
            switch (name)
            {
                case "corridor-hold": return new CorridorHoldStrategy();
                case "tpa-order": return new ThreatPriorityStrategy();
                case "residue-aware": return new ResidueAwareStrategy();
                case "decapitate": return new DecapitateStrategy();
                case "control-hold": return new ControlHoldStrategy();
                case "sustain-hold": return new SustainHoldStrategy();
                case "shield-wall": return new ShieldWallStrategy();
                case "purify-hold": return new PurifyingHoldStrategy();
                case "charge": return new AggressiveStrategy();
                case "counter-reserve": return new CounterReserveStrategy();
                case "attack-only": return new AttackOnlyStrategy();
                case "push-instrument": return new PushInstrumentStrategy();
                case "slow-instrument": return new SlowInstrumentStrategy();
                case "taunt-instrument": return new TauntInstrumentStrategy();
                case "one-ply": return new OnePlyTacticalStrategy();
                default: return null;
            }
        }

        public static string NameList() => string.Join(", ", Names);
    }

    /// <summary>
    /// A parsed request to replay one battle: which encounter, which seed, which
    /// strategy.
    ///
    /// Parsing lives here rather than in the Editor so it can be tested without
    /// Unity, and so the error messages are the same wherever the request comes
    /// from. It resolves nothing and runs nothing — it turns text into three
    /// validated values, or into a sentence explaining why it could not.
    /// </summary>
    public sealed class ReplayRequest
    {
        public const string Flag = "--replay";
        public const string Usage = "--replay <encounter> <seed> <strategy>";

        public string Encounter;
        public int Seed;
        public string Strategy;

        /// <summary>
        /// Parses "--replay &lt;encounter&gt; &lt;seed&gt; &lt;strategy&gt;".
        ///
        /// <paramref name="knownEncounters"/> may be null, which skips the
        /// existence check — the caller owns the files, and a parser that had to
        /// touch the filesystem could not be tested headlessly. Pass the list
        /// when you have it, so a typo is caught here with the alternatives
        /// spelled out rather than as a null further down.
        /// </summary>
        public static bool TryParse(IList<string> args, IList<string> knownEncounters,
                                    out ReplayRequest request, out string error)
        {
            request = null;
            error = null;

            if (args == null || args.Count == 0)
            {
                error = "Nothing to parse. Usage: " + Usage;
                return false;
            }

            int at = 0;
            if (args[0] == Flag) at = 1;

            int provided = args.Count - at;
            if (provided != 3)
            {
                error = "Replay needs exactly 3 values but got " + provided
                        + ". Usage: " + Usage;
                return false;
            }

            string encounter = Trim(args[at]);
            string seedText = Trim(args[at + 1]);
            string strategy = Trim(args[at + 2]);

            if (encounter.Length == 0) { error = "Encounter name is empty. Usage: " + Usage; return false; }
            if (strategy.Length == 0) { error = "Strategy name is empty. Usage: " + Usage; return false; }

            int seed;
            if (!int.TryParse(seedText, NumberStyles.AllowLeadingSign,
                              CultureInfo.InvariantCulture, out seed))
            {
                error = "Seed must be a whole number, but was \"" + seedText + "\".";
                return false;
            }

            if (!StrategyCatalog.IsKnown(strategy))
            {
                error = "Unknown strategy \"" + strategy + "\". Known strategies: "
                        + StrategyCatalog.NameList();
                return false;
            }

            if (knownEncounters != null && !Contains(knownEncounters, encounter))
            {
                error = "Unknown encounter \"" + encounter + "\". " + Nearby(knownEncounters, encounter);
                return false;
            }

            request = new ReplayRequest { Encounter = encounter, Seed = seed, Strategy = strategy };
            return true;
        }

        /// <summary>Convenience for a single typed line rather than an argv array.</summary>
        public static bool TryParseLine(string line, IList<string> knownEncounters,
                                        out ReplayRequest request, out string error)
        {
            List<string> parts = new List<string>();
            if (line != null)
            {
                string[] raw = line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < raw.Length; i++) parts.Add(raw[i]);
            }
            return TryParse(parts, knownEncounters, out request, out error);
        }

        public override string ToString() =>
            Flag + " " + Encounter + " " + Seed.ToString(CultureInfo.InvariantCulture) + " " + Strategy;

        private static string Trim(string s) => s == null ? "" : s.Trim();

        private static bool Contains(IList<string> names, string wanted)
        {
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], wanted, System.StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Names sharing a prefix with what was typed, so the usual mistake — a
        /// missing ".encounter" suffix — answers itself.
        /// </summary>
        private static string Nearby(IList<string> names, string wanted)
        {
            StringBuilder close = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] == null || names[i].IndexOf(wanted, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (close.Length > 0) close.Append(", ");
                close.Append(names[i]);
            }

            if (close.Length > 0) return "Did you mean: " + close + "?";
            return names.Count == 0
                ? "No encounters are available."
                : "Available: " + string.Join(", ", ToArray(names));
        }

        private static string[] ToArray(IList<string> names)
        {
            string[] copy = new string[names.Count];
            for (int i = 0; i < names.Count; i++) copy[i] = names[i];
            return copy;
        }
    }
}
