using HeroHQ.Rules;
using NUnit.Framework;

namespace HeroHQ.Tests
{
    // P1-04 (03 Architecture 4.12): the pure formulas of 02 GDD 6 — miss chance, damage, heal — with round numbers.
    // Tests 11 (damage) and the math part of 9 (miss chance) of 02 GDD 11. The rolls themselves come in P1-09.
    public class DamageMathTests
    {
        static readonly double[] None = new double[0];
        const double K = 1000;   // armor_k in the tests

        static int Dmg(double damage, double power, double[] bonuses = null, double armor = 0,
                       double[] taken = null, bool crit = false, double critDamage = 150)
            => DamageMath.Damage(damage, power, bonuses ?? None, armor, K, taken ?? None, crit, critDamage);

        // ---- miss chance (02 GDD 6, test 9 math part) ---------------------------------------------------

        [Test]
        public void MissChance_IsDodgeMinusAccuracy()
        {
            Assert.AreEqual(10, DamageMath.MissChance(10, 0, 30));
            Assert.AreEqual(5, DamageMath.MissChance(20, 15, 30));
        }

        [Test]
        public void MissChance_IsZero_WhenAccuracyReachesDodge([Values(10, 11, 25, 100)] double accuracy)
        {
            Assert.AreEqual(0, DamageMath.MissChance(10, accuracy, 30));
        }

        [Test]
        public void MissChance_NeverExceedsMaxDodge()
        {
            Assert.AreEqual(30, DamageMath.MissChance(50, 0, 30), "dodge 50 is capped at max_dodge");
            Assert.AreEqual(30, DamageMath.MissChance(50, 10, 30), "a net 40 is capped at max_dodge");
            Assert.AreEqual(30, DamageMath.MissChance(30, 0, 30), "exactly max_dodge is allowed");
            Assert.AreEqual(25, DamageMath.MissChance(35, 10, 30), "a net 25 is below the cap and stays 25");
        }

        [Test]
        public void MissChance_IsZero_ForACantMissSkill([Values(0, 10, 30, 50)] double dodge)
        {
            Assert.AreEqual(0, DamageMath.MissChance(dodge, 0, 30, cantMiss: true));
        }

        // ---- damage (02 GDD 6, test 11) -----------------------------------------------------------------

        [Test]
        public void Damage_IsAttackerDamageTimesPower()
        {
            Assert.AreEqual(200, Dmg(200, 100));
            Assert.AreEqual(300, Dmg(200, 150));
            Assert.AreEqual(900, Dmg(300, 300));
        }

        [Test]
        public void Armor_CutsAShareThatGrowsButNeverReachesAll()
        {
            Assert.AreEqual(150, Dmg(300, 100, armor: 1000), "armor = armor_k cuts half");
            Assert.AreEqual(200, Dmg(300, 100, armor: 500), "armor = half of armor_k cuts a third");
            Assert.AreEqual(1, Dmg(300, 100, armor: 1e9), "a huge armor still leaves at least 1");
        }

        [Test]
        public void AttackerBonuses_AreAddedTogether_NotMultiplied()
        {
            // leader +20 % and passive +10 %: +30 % -> 1300, not 1.2 x 1.1 = 1320 (02 GDD 8)
            Assert.AreEqual(1300, Dmg(1000, 100, bonuses: new double[] { 20, 10 }));
            // Offense Up +25 and a leader +20: 1450
            Assert.AreEqual(1450, Dmg(1000, 100, bonuses: new double[] { 25, 20 }));
        }

        [Test]
        public void OffenseDownCountsAsANegativeBonus()
        {
            Assert.AreEqual(750, Dmg(1000, 100, bonuses: new double[] { -25 }));
            Assert.AreEqual(1000, Dmg(1000, 100, bonuses: new double[] { 25, -25 }));
        }

        [Test]
        public void TargetChanges_AreAddedTogether_AndApplyOnce()
        {
            Assert.AreEqual(750, Dmg(1000, 100, taken: new double[] { -25 }), "Defense Up cuts 25 %");
            Assert.AreEqual(1250, Dmg(1000, 100, taken: new double[] { 25 }), "Defense Down adds 25 %");
            Assert.AreEqual(1150, Dmg(1000, 100, taken: new double[] { 25, -10 }), "changes add: +25 and -10 = +15 %");
        }

        [Test]
        public void Crit_MultipliesByCritDamage()
        {
            Assert.AreEqual(1500, Dmg(1000, 100, crit: true, critDamage: 150));
            Assert.AreEqual(1750, Dmg(1000, 100, crit: true, critDamage: 175));
            Assert.AreEqual(1000, Dmg(1000, 100, crit: false, critDamage: 175), "no crit, no multiplier");
        }

        [Test]
        public void Damage_FollowsTheGddOrder_AndRoundsOnlyAtTheEnd()
        {
            // 400 x 150 % = 600; +20 +10 % = 780; armor 500 cuts a third = 520; target +25 % = 650; crit x1.5 = 975
            var result = Dmg(400, 150, bonuses: new double[] { 20, 10 }, armor: 500, taken: new double[] { 25 },
                             crit: true, critDamage: 150);
            Assert.AreEqual(975, result);
        }

        [Test]
        public void Damage_IsAtLeastOne([Values(0, 1, 5)] double damage)
        {
            Assert.AreEqual(1, Dmg(damage, 1));
            Assert.AreEqual(1, Dmg(1000, 100, bonuses: new double[] { -200 }), "even with bonuses below -100 %");
        }

        [Test]
        public void Damage_RoundsAHalfUp()
        {
            // C#'s plain Math.Round would send 2.5 to 2: the GDD wants halves up (03 Architecture 4.12 checklist)
            Assert.AreEqual(2, Dmg(3, 50), "1.5 -> 2");
            Assert.AreEqual(3, Dmg(5, 50), "2.5 -> 3");
            Assert.AreEqual(4, Dmg(7, 50), "3.5 -> 4");
            Assert.AreEqual(2, Dmg(24, 10), "2.4 -> 2");
            Assert.AreEqual(3, Dmg(26, 10), "2.6 -> 3");
        }

        // ---- heal ---------------------------------------------------------------------------------------

        [Test]
        public void Heal_IsAPercentOfTheTargetsMaxHealth()
        {
            Assert.AreEqual(600, DamageMath.Heal(20, None, 3000, 1000));
            Assert.AreEqual(300, DamageMath.Heal(10, None, 3000, 1000));
        }

        [Test]
        public void HealingBonuses_AddThenMultiplyTheHeal()
        {
            // 02 GDD 10.2: Patch Job's 20 % with Doc Nefer's +20 % on an Outlaw heals 24 %
            Assert.AreEqual(720, DamageMath.Heal(20, new double[] { 20 }, 3000, 100));
            Assert.AreEqual(780, DamageMath.Heal(20, new double[] { 20, 10 }, 3000, 100), "bonuses add: +30 % of 20 % = 26 %");
        }

        [Test]
        public void Heal_NeverGoesAboveMaxHealth()
        {
            Assert.AreEqual(100, DamageMath.Heal(20, None, 3000, 2900), "only 100 is missing");
            Assert.AreEqual(0, DamageMath.Heal(20, None, 3000, 3000), "already full");
        }

        [Test]
        public void Heal_RoundsAHalfUp()
        {
            Assert.AreEqual(3, DamageMath.Heal(25, None, 10, 0), "2.5 -> 3");
            Assert.AreEqual(2, DamageMath.Heal(24, None, 10, 0), "2.4 -> 2");
        }
    }
}
