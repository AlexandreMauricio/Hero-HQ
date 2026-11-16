using System;
using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static HeroHQ.Tests.TestFights;

namespace HeroHQ.Tests
{
    // P1-09 (03 Architecture 4.12): SkillResolver part 1 and the fight's Options and Act. Damage pieces, hit and
    // crit (once per target per skill), knockouts, the Barrier, and what the three buttons offer.
    // 02 GDD 11: test 9 (the roll part), test 10 (the damage part), test 12 (the knockout part).
    //
    // The set-up: p0 (Speed 200) acts first and four times as often as anyone else (all Speed 50), so a test can
    // play p0's turns over and over. Test heroes have Damage 200, Armor 100, Crit Chance 10, Crit Damage 150,
    // Accuracy 0, Dodge 0 and Health 1,000,000 unless a test changes them. Tuning: armor_k 1000, max_dodge 30,
    // offense_change and defense_change 25. So a 100 % hit does 200 × (1 − 100/1100) = 181.8 → 182.
    public class SkillDamageTests
    {
        static Fight Duel(Action<string, JObject> edit = null, int seed = 1) => NewFight(
            new[] { ("p0", 200), ("p1", 50), ("p2", 50), ("p3", 50), ("p4", 50) },
            new[] { ("e0", 50), ("e1", 50), ("e2", 50), ("e3", 50), ("e4", 50) }, seed, edit: edit);

        /// <summary>Replaces a skill's pieces. Pieces are written with single quotes: "{ do: 'damage', target: 'enemy', power: 100 }".</summary>
        static void Pieces(JObject hero, string button, params string[] pieces) =>
            hero[button]["pieces"] = JArray.Parse("[" + string.Join(",", pieces) + "]");

        static void Stat(JObject hero, string stat, double value) => hero["stats"][stat] = value;

        static bool IsEnemy(string id) => id.StartsWith("e");

        /// <summary>Plays turns (everyone else uses Basic) until p0 must choose; returns p0.</summary>
        static Fighter UntilP0(Fight fight)
        {
            for (var guard = 0; guard < 100; guard++)
            {
                fight.AdvanceToChoice();
                if (fight.Acting == fight.Players[0]) return fight.Acting;
                fight.Act(Basic(fight));
            }
            Assert.Fail("p0 never got a turn");
            return null;
        }

        static List<FightEvent> Of(IEnumerable<FightEvent> events, FightEventKind kind, Fighter who = null) =>
            events.Where(e => e.Kind == kind && (who == null || e.Who == who)).ToList();

        /// <summary>Plays p0's turn <paramref name="times"/> times with a skill that needs no choice; returns only p0's events.</summary>
        static List<FightEvent> P0Acts(Fight fight, Button button, int times)
        {
            var events = new List<FightEvent>();
            for (var i = 0; i < times; i++)
            {
                UntilP0(fight);
                events.AddRange(fight.Act(new Choice(button, null)));
            }
            return events;
        }

        // ---- test 9: hit or miss ------------------------------------------------------------------------

        [Test]
        public void Test9_AccuracyAtLeastDodge_NeverMisses_Over1000Rolls()
        {
            var dodge = new Dictionary<string, int> { ["e0"] = 20, ["e1"] = 20, ["e2"] = 10, ["e3"] = 0, ["e4"] = 15 };
            var fight = Duel((id, h) =>
            {
                if (id == "p0")
                {
                    Stat(h, "accuracy", 20);
                    Pieces(h, "basic", "{ do: 'damage', target: 'all_enemies', power: 10 }");
                }
                if (IsEnemy(id)) Stat(h, "dodge_chance", dodge[id]);
            });

            var events = P0Acts(fight, Button.Basic, 200);     // 200 turns × 5 enemies = 1,000 hit rolls

            Assert.AreEqual(1000, Of(events, FightEventKind.Damaged).Count);
            CollectionAssert.IsEmpty(Of(events, FightEventKind.Missed));
        }

        [Test]
        public void Test9_CantMiss_NeverMisses_EvenAtMaxDodge_Over1000Rolls()
        {
            var fight = Duel((id, h) =>
            {
                if (id == "p0") Pieces(h, "basic", "{ do: 'damage', target: 'all_enemies', power: 10, cant_miss: true }");
                if (IsEnemy(id)) Stat(h, "dodge_chance", 50);
            });

            var events = P0Acts(fight, Button.Basic, 200);

            Assert.AreEqual(1000, Of(events, FightEventKind.Damaged).Count);
            CollectionAssert.IsEmpty(Of(events, FightEventKind.Missed));
        }

        [Test]
        public void Test9_DodgeAboveAccuracy_Misses_AboutAsOftenAsTheMissChance_NeverAboveMaxDodge()
        {
            // Dodge 50 is capped at max_dodge 30: about 300 misses in 1,000 (a fixed seed, so always the same count)
            var fight = Duel((id, h) =>
            {
                if (id == "p0") Pieces(h, "basic", "{ do: 'damage', target: 'all_enemies', power: 10 }");
                if (IsEnemy(id)) Stat(h, "dodge_chance", 50);
            });

            var events = P0Acts(fight, Button.Basic, 200);

            var misses = Of(events, FightEventKind.Missed).Count;
            Assert.That(misses, Is.InRange(230, 370), "30 %, not 50 %");
            Assert.AreEqual(1000 - misses, Of(events, FightEventKind.Damaged).Count, "every roll is a hit or a miss");
        }

        // ---- the rolls: per target per skill, hit then crit, in target order --------------------------

        [Test]
        public void Rolls_PerTarget_OneHitRollThenOneCritRoll_InTargetOrder_OnTheFightsGenerator()
        {
            bool sawMiss = false, sawCrit = false, sawPlain = false;
            for (var seed = 1; seed <= 20; seed++)
            {
                var fight = Duel((id, h) =>
                {
                    if (id == "p0")
                    {
                        Stat(h, "crit_chance", 50);
                        Pieces(h, "basic", "{ do: 'damage', target: 'all_enemies', power: 100 }",
                                           "{ do: 'damage', target: 'all_enemies', power: 50 }");
                    }
                    if (IsEnemy(id)) Stat(h, "dodge_chance", 30);
                }, seed);
                fight.AdvanceToChoice();                          // nothing rolls before the first skill
                Assert.AreSame(fight.Players[0], fight.Acting);

                var events = fight.Act(new Choice(Button.Basic, null));

                // the same seed in C#'s own generator, rolled the way 02 GDD 6 says
                var expected = new System.Random(seed);
                foreach (var enemy in fight.Enemies)              // slot order is the target order
                {
                    if (expected.NextDouble() * 100 < 30)
                    {
                        Assert.AreEqual(1, Of(events, FightEventKind.Missed, enemy).Count, $"seed {seed}: {enemy} missed");
                        CollectionAssert.IsEmpty(Of(events, FightEventKind.Damaged, enemy), "a miss cancels both pieces on that target");
                        sawMiss = true;
                        continue;
                    }
                    var crit = expected.NextDouble() * 100 < 50;
                    var hits = Of(events, FightEventKind.Damaged, enemy);
                    Assert.AreEqual(2, hits.Count, $"seed {seed}: both pieces land on {enemy}");
                    Assert.IsTrue(hits.All(h => h.Crit == crit), $"seed {seed}: the crit roll is shared by both pieces on {enemy}");
                    if (crit) sawCrit = true; else sawPlain = true;
                }

                // the generator is exactly where it should be: the second piece rolled nothing
                for (var i = 0; i < 20; i++)
                    Assert.AreEqual(expected.NextDouble() * 100 < 50, fight.Roll(50), $"seed {seed}: roll {i} after the skill");
            }
            Assert.IsTrue(sawMiss && sawCrit && sawPlain, "the seeds cover a miss, a crit and a plain hit");
        }

        [Test]
        public void SeveralHits_ShareOneHitRollAndOneCritRoll_EachHitDealsItsOwnDamage()
        {
            bool sawMiss = false, sawCrit = false, sawPlain = false;
            for (var seed = 1; seed <= 30; seed++)
            {
                var fight = Duel((id, h) =>
                {
                    if (id == "p0")
                    {
                        Stat(h, "crit_chance", 50);
                        Pieces(h, "basic", "{ do: 'damage', target: 'enemy', power: 50, hits: 3 }");
                    }
                    if (id == "e0") Stat(h, "dodge_chance", 30);
                }, seed);
                UntilP0(fight);

                var hits = Of(fight.Act(new Choice(Button.Basic, fight.Enemies[0])), FightEventKind.Damaged, fight.Enemies[0]);

                if (hits.Count == 0) { sawMiss = true; continue; }
                Assert.AreEqual(3, hits.Count, $"seed {seed}: all three hits or none");
                Assert.AreEqual(1, hits.Select(h => (h.Crit, h.Amount)).Distinct().Count(), $"seed {seed}: one crit roll, the same number each hit");
                if (hits[0].Crit) sawCrit = true; else sawPlain = true;
            }
            Assert.IsTrue(sawMiss && sawCrit && sawPlain, "the seeds cover a miss, a crit and a plain hit");
        }

        [Test]
        public void AlwaysCrits_EveryHitCrits_WithoutACritRoll()
        {
            var fight = Duel((id, h) =>
            {
                if (id != "p0") return;
                Stat(h, "crit_chance", 0);
                Pieces(h, "basic", "{ do: 'damage', target: 'enemy', power: 100, always_crits: true }",
                                   "{ do: 'damage', target: 'enemy', power: 100 }");
            });
            fight.AdvanceToChoice();

            var hits = Of(fight.Act(new Choice(Button.Basic, fight.Enemies[0])), FightEventKind.Damaged);

            Assert.AreEqual(2, hits.Count);
            Assert.IsTrue(hits.All(h => h.Crit), "the skill always crits, its second piece too (the crit is per target per skill)");
            Assert.AreEqual(273, hits[0].Amount, "182 before rounding was 181.8; × Crit Damage 150 % = 272.7 → 273");

            var expected = new System.Random(1);
            expected.NextDouble();                                // the hit roll; no crit roll
            for (var i = 0; i < 20; i++)
                Assert.AreEqual(expected.NextDouble() * 100 < 50, fight.Roll(50), $"roll {i} after the skill");
        }

        // ---- test 10: a miss, and the damage itself ------------------------------------------------------

        [Test]
        public void Test10_AMiss_CancelsTheSkillsDamageOnThatTargetOnly()
        {
            bool sawMiss = false, sawHit = false;
            for (var seed = 1; seed <= 40 && !(sawMiss && sawHit); seed++)
            {
                var fight = Duel((id, h) =>
                {
                    if (id == "p0") Pieces(h, "basic", "{ do: 'damage', target: 'enemy_and_neighbours', power: 100 }",
                                                       "{ do: 'damage', target: 'enemy', power: 100 }");
                    if (id == "e1") Stat(h, "dodge_chance", 30);
                }, seed);
                UntilP0(fight);
                var e = fight.Enemies;

                var events = fight.Act(new Choice(Button.Basic, e[1]));    // e1 and its neighbours e0, e2

                Assert.AreEqual(1, Of(events, FightEventKind.Damaged, e[0]).Count);
                Assert.AreEqual(1, Of(events, FightEventKind.Damaged, e[2]).Count);
                Assert.IsEmpty(Of(events, FightEventKind.Damaged, e[3]).Concat(Of(events, FightEventKind.Damaged, e[4])), "the back row isn't a neighbour");
                if (Of(events, FightEventKind.Missed, e[1]).Count == 1)
                {
                    CollectionAssert.IsEmpty(Of(events, FightEventKind.Damaged, e[1]), "neither piece touches the missed target");
                    Assert.AreEqual(1000000, e[1].Health);
                    sawMiss = true;
                }
                else
                {
                    Assert.AreEqual(2, Of(events, FightEventKind.Damaged, e[1]).Count, "hit: both pieces land on the chosen enemy");
                    sawHit = true;
                }
            }
            Assert.IsTrue(sawMiss && sawHit, "the seeds cover a miss and a hit on e1");
        }

        [TestCase("offense_up", "defense_down", 426, TestName = "Test10_Damage_IsTheFormula_OffenseUpAndDefenseDown")]
        [TestCase("offense_down", "defense_up", 153, TestName = "Test10_Damage_IsTheFormula_OffenseDownAndDefenseUp")]
        public void Test10_Damage_IsTheFormula(string onAttacker, string onTarget, int expected)
        {
            // 200 × 150 % = 300; × (1 ± 25 %); × (1 − 100/1100); × (1 ± 25 %); rounded:
            // 300 × 1.25 × 0.9091 × 1.25 = 426.1 → 426,   300 × 0.75 × 0.9091 × 0.75 = 153.4 → 153
            var fight = Duel((id, h) =>
            {
                if (id != "p0") return;
                Stat(h, "crit_chance", 0);
                Pieces(h, "basic", "{ do: 'damage', target: 'enemy', power: 150 }");
            });
            var p0 = UntilP0(fight);
            var e0 = fight.Enemies[0];
            Effects.Apply(p0, onAttacker, 2);
            Effects.Apply(e0, onTarget, 2);

            var hit = Of(fight.Act(new Choice(Button.Basic, e0)), FightEventKind.Damaged).Single();

            Assert.AreEqual(expected, hit.Amount);
            Assert.IsFalse(hit.Crit);
            Assert.AreEqual(1000000 - expected, e0.Health);
        }

        [Test]
        public void Barrier_SoaksFirst_ThenHealthDrops_AndIsUsedUpWhenEmpty()
        {
            var fight = Duel((id, h) => { if (id == "p0") Stat(h, "crit_chance", 0); });
            UntilP0(fight);
            var e0 = fight.Enemies[0];
            Effects.Apply(e0, "barrier", 2, amount: 100);

            var events = fight.Act(new Choice(Button.Basic, e0)).ToList();

            var hit = Of(events, FightEventKind.Damaged).Single();
            Assert.AreEqual(182, hit.Amount, "the number shown is the whole hit");
            Assert.AreEqual(100, hit.Soaked);
            Assert.AreEqual(1000000 - 82, e0.Health, "only what the Barrier didn't soak comes off Health");
            Assert.IsFalse(e0.Effects.Any(x => x.Kind == "barrier"));
            var usedUp = events.FindIndex(x => x.Kind == FightEventKind.EffectUsedUp && x.Effect == "barrier");
            Assert.AreEqual(events.IndexOf(hit) + 1, usedUp, "the Barrier breaks right after the hit that empties it");
            Assert.AreEqual("Damaged: Enemy 0 e0 (182, 100 soaked)", hit.ToString());
        }

        [Test]
        public void Barrier_BiggerThanTheHit_SoaksItAll_AndKeepsTheRest()
        {
            var fight = Duel((id, h) => { if (id == "p0") Stat(h, "crit_chance", 0); });
            UntilP0(fight);
            var e0 = fight.Enemies[0];
            Effects.Apply(e0, "barrier", 2, amount: 500);

            var events = fight.Act(new Choice(Button.Basic, e0));

            Assert.AreEqual(182, Of(events, FightEventKind.Damaged).Single().Soaked);
            Assert.AreEqual(1000000, e0.Health);
            Assert.AreEqual(318, e0.Effects.Single(x => x.Kind == "barrier").Amount);
            CollectionAssert.IsEmpty(Of(events, FightEventKind.EffectUsedUp));
        }

        // ---- test 12: knockouts -------------------------------------------------------------------------

        [Test]
        public void Test12_AHeroKnockedOutByTheFirstPiece_IsSkippedByTheRestOfTheSkill_AndLeavesTheFight()
        {
            var fight = Duel((id, h) =>
            {
                if (id == "p0")
                {
                    Stat(h, "crit_chance", 0);
                    Pieces(h, "basic", "{ do: 'damage', target: 'enemy_and_neighbours', power: 100 }",
                                       "{ do: 'damage', target: 'all_enemies', power: 100, hits: 2 }");
                }
                if (id == "e0") Stat(h, "health", 100);
            });
            UntilP0(fight);
            var e = fight.Enemies;
            Effects.Apply(e[0], "defense_up", 3);       // 182 × 0.75 = 136: still more than 100

            var events = fight.Act(new Choice(Button.Basic, e[1])).ToList();

            Assert.AreEqual(1, Of(events, FightEventKind.Damaged, e[0]).Count, "only the first piece reaches e0");
            Assert.AreEqual(1, Of(events, FightEventKind.KnockedOut, e[0]).Count);
            Assert.AreEqual(events.IndexOf(Of(events, FightEventKind.Damaged, e[0])[0]) + 1,
                            events.IndexOf(Of(events, FightEventKind.KnockedOut, e[0])[0]), "knocked out the moment Health hits 0");
            Assert.AreEqual(0, e[0].Health);
            Assert.IsTrue(e[0].KnockedOut);
            CollectionAssert.IsEmpty(e[0].Effects, "a knocked-out hero loses every effect");
            Assert.AreEqual(3, Of(events, FightEventKind.Damaged, e[1]).Count, "piece 1, then two hits of piece 2");
            Assert.AreEqual(3, Of(events, FightEventKind.Damaged, e[2]).Count);
            Assert.AreEqual(2, Of(events, FightEventKind.Damaged, e[3]).Count);

            // out of the fight: can't be chosen or hit again, and gone from the turn bar
            CollectionAssert.DoesNotContain(Targeting.Opponents(fight, fight.Players[0]), e[0]);
            CollectionAssert.DoesNotContain(fight.PredictTurns(20).Select(t => t.Who), e[0]);
        }

        [Test]
        public void Test12_SeveralHits_StopTheMomentTheTargetIsKnockedOut()
        {
            var fight = Duel((id, h) =>
            {
                if (id == "p0")
                {
                    Stat(h, "crit_chance", 0);
                    Pieces(h, "basic", "{ do: 'damage', target: 'enemy', power: 100, hits: 3 }");
                }
                if (id == "e0") Stat(h, "health", 300);
            });
            UntilP0(fight);

            var events = fight.Act(new Choice(Button.Basic, fight.Enemies[0]));

            Assert.AreEqual(2, Of(events, FightEventKind.Damaged).Count, "182, then 118 left: the second hit knocks out, the third never comes");
            Assert.AreEqual(1, Of(events, FightEventKind.KnockedOut).Count);
            var knockout = events.ToList().FindIndex(e => e.Kind == FightEventKind.KnockedOut);
            CollectionAssert.AreEqual(new[] { FightEventKind.ChargeChanged }, events.Skip(knockout + 1).Select(e => e.Kind),
                "nothing after it but the attacker's Basic charge (P1-10)");
        }

        // ---- Options: the three buttons -----------------------------------------------------------------

        [Test]
        public void Options_NobodyChoosing_IsEmpty()
        {
            CollectionAssert.IsEmpty(Duel().Options);
        }

        [Test]
        public void Options_BasicAlwaysUsable_SpecialAtCooldown0_UltimateAtFullCharge()
        {
            var fight = Duel();
            var p0 = UntilP0(fight);

            CollectionAssert.AreEqual(new[] { Button.Basic, Button.Special, Button.Ultimate }, fight.Options.Select(o => o.Button));
            Assert.AreSame(p0.Hero.Special, fight.Options[1].Skill);
            CollectionAssert.AreEqual(new[] { true, true, false }, fight.Options.Select(o => o.Usable),
                "every Special is ready at fight start; charge starts at 0 (02 GDD 4)");

            p0.Cooldown = 1;
            p0.Charge = 99.5;
            CollectionAssert.AreEqual(new[] { true, false, false }, fight.Options.Select(o => o.Usable));

            p0.Cooldown = 0;
            p0.Charge = 100;
            CollectionAssert.AreEqual(new[] { true, true, true }, fight.Options.Select(o => o.Usable));
        }

        [Test]
        public void Options_WhoMayBeChosen_FollowsTheSkillsShape()
        {
            // default test skills: Basic a chosen enemy, Special a chosen enemy and neighbours, Ultimate all enemies
            var fight = Duel();
            UntilP0(fight);
            var e = fight.Enemies;
            var options = fight.Options;

            Assert.IsTrue(options[0].NeedsChoice);
            CollectionAssert.AreEqual(new[] { e[0], e[1], e[2] }, options[0].Choosable, "front row first");
            Assert.IsTrue(options[1].NeedsChoice);
            CollectionAssert.AreEqual(new[] { e[0], e[1], e[2] }, options[1].Choosable, "the centre is chosen like a chosen enemy");
            Assert.IsFalse(options[2].NeedsChoice);
            CollectionAssert.IsEmpty(options[2].Choosable, "no choice: the screen arms the button and commits");

            Effects.Apply(e[4], "taunt", 2);
            CollectionAssert.AreEqual(new[] { e[4] }, fight.Options[0].Choosable, "Taunt first, even in the back row");
        }

        [Test]
        public void Options_ReachesTheBackRow_IfAnyChosenEnemyPieceHasTheFlag_AndAnAllySkillOffersEveryAllySelfIncluded()
        {
            var fight = Duel((id, h) =>
            {
                if (id != "p0") return;
                Pieces(h, "special", "{ do: 'damage', target: 'enemy', power: 100 }",
                                     "{ do: 'damage', target: 'enemy', power: 100, reaches_back_row: true }");
                Pieces(h, "ultimate", "{ do: 'heal', target: 'ally', percent: 20 }");
            });
            UntilP0(fight);

            CollectionAssert.AreEqual(fight.Enemies, fight.Options[1].Choosable, "reading R2: the skill reaches if any of its pieces does");
            Assert.IsTrue(fight.Options[2].NeedsChoice);
            CollectionAssert.AreEqual(fight.Players, fight.Options[2].Choosable, "in a skill, \"ally\" includes the hero (10.6 q. 9)");
            CollectionAssert.IsEmpty(fight.Options[2].MissChance, "heals never miss: no chip");
        }

        [Test]
        public void Options_MissChance_ForEachEnemy_IsDodgeMinusAccuracy_CappedAtMaxDodge_AndZeroIfItCantMiss()
        {
            var dodge = new Dictionary<string, int> { ["e0"] = 0, ["e1"] = 10, ["e2"] = 25, ["e3"] = 50, ["e4"] = 5 };
            var fight = Duel((id, h) =>
            {
                if (id == "p0")
                {
                    Stat(h, "accuracy", 10);
                    Pieces(h, "ultimate", "{ do: 'damage', target: 'all_enemies', power: 100, cant_miss: true }");
                }
                if (IsEnemy(id)) Stat(h, "dodge_chance", dodge[id]);
            });
            UntilP0(fight);
            var basic = fight.Options[0].MissChance;

            CollectionAssert.AreEqual(new[] { 0.0, 0, 15, 30, 0 }, fight.Enemies.Select(e => basic[e]),
                "below 0 is 0; 50 − 10 = 40 is capped at max_dodge 30");
            CollectionAssert.AreEqual(new[] { 0.0, 0, 0, 0, 0 }, fight.Enemies.Select(e => fight.Options[2].MissChance[e]), "can't miss");
        }

        // ---- Act: checks the choice, then the skill ------------------------------------------------------

        [Test]
        public void Act_RejectsAButtonThatIsntUsable_OrAHeroWhoCantBeChosen_AndChangesNothing()
        {
            var fight = Duel();
            var p0 = UntilP0(fight);
            var before = fight.Players.Concat(fight.Enemies).Select(f => f.Health).ToList();

            Assert.Throws<ArgumentException>(() => fight.Act(new Choice(Button.Ultimate, null)), "charge 0");
            Assert.Throws<ArgumentException>(() => fight.Act(new Choice(Button.Basic, fight.Enemies[3])), "back row behind a standing front row");
            Assert.Throws<ArgumentException>(() => fight.Act(new Choice(Button.Basic, fight.Players[1])), "an ally for an enemy skill");
            Assert.Throws<ArgumentException>(() => fight.Act(new Choice(Button.Basic, null)), "Basic needs a tapped hero");

            Assert.AreSame(p0, fight.Acting, "still p0's choice");
            CollectionAssert.AreEqual(before, fight.Players.Concat(fight.Enemies).Select(f => f.Health));
        }

        [Test]
        public void Ultimate_SkillUsedIsTheFirstEventOfTheAction_SoTheCutInPlaysBeforeTheHits()
        {
            var fight = Duel();
            var p0 = UntilP0(fight);
            p0.Charge = 100;

            var events = fight.Act(new Choice(Button.Ultimate, null));

            Assert.AreEqual(FightEventKind.SkillUsed, events[0].Kind);
            Assert.AreSame(p0, events[0].Who);
            Assert.AreEqual(Button.Ultimate, events[0].Button);
            Assert.AreEqual("SkillUsed: Player 0 p0 (Ultimate)", events[0].ToString());
            Assert.AreEqual(5, Of(events.Skip(1), FightEventKind.Damaged).Count, "then the hits: all five enemies");
        }
    }
}
