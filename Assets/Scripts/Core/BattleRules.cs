namespace Ediki.Core
{
    /// <summary>
    /// How DEF turns into damage reduction. Different tactics games answer this
    /// differently and the answer changes what a defensive unit is for, so it is
    /// a per-battle setting the prototype can measure rather than a constant.
    /// </summary>
    public enum DamageModel
    {
        /// <summary>max(1, ATK - DEF). OD-05 baseline.</summary>
        Subtractive = 0,

        /// <summary>ATK x (100 - DEF)%, DEF capped at MaxPercentageReduction.</summary>
        Percentage = 1
    }

    /// <summary>
    /// The rule variants one battle runs under.
    ///
    /// Immutable and shared across clones, exactly like BattleMap and
    /// ObjectiveDef. It is configuration rather than world state, so it is NOT
    /// part of the state hash — two battles under different rule sets are told
    /// apart by their outcomes, not by hashing their settings.
    ///
    /// Everything here defaults to the decided baseline, so a battle that does
    /// not mention rules behaves exactly as it did before this type existed.
    /// </summary>
    public sealed class RuleSet
    {
        public static readonly RuleSet Default = new RuleSet(DamageModel.Subtractive);

        public readonly DamageModel Damage;

        public RuleSet(DamageModel damage)
        {
            Damage = damage;
        }

        public string Describe() => "damage=" + (Damage == DamageModel.Percentage ? "percent" : "subtractive");
    }

    /// <summary>
    /// The rule constants that are NOT per-unit.
    ///
    /// Kept deliberately tiny and in ONE place so the prototype baseline is easy
    /// to change (OD-05 / OD-06 explicitly ask for this).
    /// Do not grow this into a damage framework.
    /// </summary>
    public static class BattleRules
    {
        /// <summary>Guard multiplier, expressed as a percentage (OD-06: x0.5).</summary>
        public const int GuardDamagePercent = 50;

        /// <summary>
        /// OD-05 prototype baseline: Damage = max(1, ATK - DEF), deterministic.
        /// Guard is applied afterwards as a straight multiplier, never as a DEF bonus.
        /// Integer maths only (determinism rule 1).
        /// </summary>
        public static int ComputeDamage(int attackerAtk, int targetDef, bool targetIsGuarding) =>
            ComputeDamage(attackerAtk, targetDef, targetIsGuarding, DamageModel.Subtractive);

        /// <summary>
        /// Same, under a chosen damage model. The model is per-battle data
        /// (RuleSet), never a global — a mutable global here would make the rule
        /// layer non-reentrant and quietly break determinism rule 1.
        ///
        /// Subtractive and Percentage are NOT interchangeable balance-wise:
        /// subtraction makes a point of DEF worth a flat point of damage, so high
        /// ATK overwhelms it; a percentage makes each point of DEF worth more as
        /// ATK rises. Which one a game picks changes what "a tank" means.
        /// Baseline stays Subtractive (OD-05) and nothing below changes it.
        /// </summary>
        public static int ComputeDamage(int attackerAtk, int targetDef, bool targetIsGuarding,
                                        DamageModel model)
        {
            int raw;

            if (model == DamageModel.Percentage)
            {
                // DEF reads as "percent of damage prevented", capped so nothing is
                // ever fully immune. Integer maths, truncating (determinism rule 1).
                int reduction = targetDef;
                if (reduction < 0) reduction = 0;
                if (reduction > MaxPercentageReduction) reduction = MaxPercentageReduction;
                raw = attackerAtk * (100 - reduction) / 100;
            }
            else
            {
                raw = attackerAtk - targetDef;
            }

            if (raw < 1) raw = 1;

            if (targetIsGuarding)
            {
                raw = raw * GuardDamagePercent / 100;
                if (raw < 1) raw = 1;
            }

            return raw;
        }

        /// <summary>Ceiling on percentage DEF, so no unit becomes unkillable.</summary>
        public const int MaxPercentageReduction = 90;
    }
}
