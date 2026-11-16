using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroHQ.Rules
{
    /// <summary>
    /// The pure formulas of 02 GDD section 6: miss chance, damage, heal. No state and no dice: the caller
    /// rolls (SkillResolver, P1-09) and passes the result in, so every formula is easy to test.
    ///
    /// Percentages are as written in the data (25 means 25 %). Every bonus list is SUMMED here and applied
    /// once, so the "bonuses add, they don't multiply" rule lives in exactly one place (02 GDD 6 and 8).
    /// </summary>
    public static class DamageMath
    {
        /// <summary>
        /// The chance, in percent, that an attack on this target misses: its Dodge minus the attacker's
        /// Accuracy, never below 0 and never above <paramref name="maxDodge"/>. A "can't miss" skill is 0.
        /// </summary>
        public static double MissChance(double targetDodge, double attackerAccuracy, double maxDodge, bool cantMiss = false)
        {
            if (cantMiss) return 0;
            return Math.Min(maxDodge, Math.Max(0, targetDodge - attackerAccuracy));
        }

        /// <summary>
        /// The damage of ONE hit on ONE target, as a whole number of at least 1 (02 GDD 6). The caller decides
        /// whether it is a critical hit.
        /// </summary>
        /// <param name="attackerDamage">The attacker's Damage stat.</param>
        /// <param name="powerPercent">The piece's power, a % of that stat.</param>
        /// <param name="attackerBonuses">Every % bonus to the attacker's damage (Offense Up/Down, leader, passives, Momentum…); negative for a cut.</param>
        /// <param name="targetArmor">The target's Armor stat.</param>
        /// <param name="armorK">The tuning value armor_k: an Armor of this size cuts half the damage.</param>
        /// <param name="targetChanges">Every % change to the damage the target takes (Defense Up is negative, Defense Down positive).</param>
        /// <param name="crit">True if this skill crits on this target.</param>
        /// <param name="critDamagePercent">The attacker's Crit Damage stat (150 = 1.5 times); only used on a crit.</param>
        public static int Damage(double attackerDamage, double powerPercent, IEnumerable<double> attackerBonuses,
                                 double targetArmor, double armorK, IEnumerable<double> targetChanges,
                                 bool crit, double critDamagePercent)
        {
            var baseDamage = attackerDamage * powerPercent / 100;
            var boosted = baseDamage * (1 + attackerBonuses.Sum() / 100);
            var armored = boosted * (1 - targetArmor / (targetArmor + armorK));
            var taken = armored * (1 + targetChanges.Sum() / 100);
            if (crit) taken *= critDamagePercent / 100;
            return Math.Max(1, RoundHalfUp(taken));
        }

        /// <summary>
        /// How much a heal gives back: a % of the TARGET's max Health, times the summed healing bonuses,
        /// rounded, and never more than the Health the target is missing (02 GDD 6).
        /// </summary>
        public static int Heal(double healPercent, IEnumerable<double> healingBonuses, int maxHealth, int health)
        {
            var amount = maxHealth * healPercent / 100 * (1 + healingBonuses.Sum() / 100);
            return Math.Max(0, Math.Min(RoundHalfUp(amount), maxHealth - health));
        }

        /// <summary>
        /// Rounds to the nearest whole number with a half going UP (2.5 -> 3). C#'s plain Math.Round sends a
        /// half to the even number (2.5 -> 2), which 02 GDD 10.6 question 2 doesn't want.
        /// </summary>
        static int RoundHalfUp(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
