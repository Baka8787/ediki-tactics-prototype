using System;
using System.Collections.Generic;

namespace Ediki.Core.Ai
{
    public enum TargetPreference
    {
        Nearest = 0,
        LowestHp = 1,
        LowestDefence = 2
    }

    /// <summary>
    /// Data-driven knobs for one enemy archetype (OD-10).
    ///
    /// Deliberately a flat bag of numbers, not a behaviour tree. Designers tune
    /// enemies by editing ai-profiles.txt; no code changes, no editor tooling.
    /// </summary>
    public sealed class AiProfile
    {
        public readonly string Id;

        /// <summary>Who to go for.</summary>
        public readonly TargetPreference TargetPreference;

        /// <summary>Distance the unit tries to hold. 1 = melee.</summary>
        public readonly int PreferredDistance;

        /// <summary>0 = ignores its own safety entirely, 100 = never trades position for a hit.</summary>
        public readonly int Aggression;

        /// <summary>Below this HP%, the unit backs off instead of closing in. 0 = never retreats.</summary>
        public readonly int RetreatHpPercent;

        /// <summary>Below this HP%, the unit guards when it cannot attack. 0 = never guards.</summary>
        public readonly int GuardHpPercent;

        public AiProfile(string id, TargetPreference targetPreference, int preferredDistance,
                         int aggression, int retreatHpPercent, int guardHpPercent)
        {
            Id = id;
            TargetPreference = targetPreference;
            PreferredDistance = preferredDistance;
            Aggression = aggression;
            RetreatHpPercent = retreatHpPercent;
            GuardHpPercent = guardHpPercent;
        }

        public static readonly AiProfile Default =
            new AiProfile("default", TargetPreference.Nearest, 1, 70, 0, 0);

        public override string ToString() => Id;
    }

    public sealed class AiProfileCatalog
    {
        private readonly Dictionary<string, AiProfile> _byId;

        public AiProfileCatalog(IEnumerable<AiProfile> profiles)
        {
            _byId = new Dictionary<string, AiProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (AiProfile p in profiles)
            {
                if (_byId.ContainsKey(p.Id)) throw new ArgumentException("Duplicate ai profile '" + p.Id + "'.");
                _byId.Add(p.Id, p);
            }
        }

        public bool TryGet(string id, out AiProfile profile) => _byId.TryGetValue(id, out profile);

        public AiProfile GetOrDefault(string id)
        {
            AiProfile p;
            if (id != null && _byId.TryGetValue(id, out p)) return p;
            return AiProfile.Default;
        }
    }
}
