namespace Ediki.Sim
{
    /// <summary>
    /// Seeded integer PRNG for the SAMPLING layer only.
    ///
    /// This deliberately lives in Ediki.Sim, not Ediki.Core. The rule layer stays
    /// RNG-free (OD-05 deterministic hit); randomness here only decides which
    /// legal play a sampled strategy makes, so the same seed always replays the
    /// same battle and the rule layer's determinism guarantees are untouched.
    ///
    /// xorshift32: integer-only, no float, identical on every platform.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private uint _state;

        public readonly int Seed;

        public DeterministicRandom(int seed)
        {
            Seed = seed;
            // 0 is a fixed point of xorshift; nudge it off.
            _state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);
        }

        public uint NextUInt()
        {
            unchecked
            {
                uint x = _state;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                _state = x;
                return x;
            }
        }

        /// <summary>Uniform in [0, exclusiveMax). Returns 0 when exclusiveMax &lt;= 0.</summary>
        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 0) return 0;
            return (int)(NextUInt() % (uint)exclusiveMax);
        }

        /// <summary>True with the given percentage chance.</summary>
        public bool Chance(int percent)
        {
            if (percent <= 0) return false;
            if (percent >= 100) return true;
            return NextInt(100) < percent;
        }
    }
}
