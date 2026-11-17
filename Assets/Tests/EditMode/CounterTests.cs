using System;
using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static HeroHQ.Tests.TestFights;

namespace HeroHQ.Tests
{
    // P1-11 (03 Architecture 4.12): counter-attacks (02 GDD 7.4, 10.6 questions 3 and 4).
    // 02 GDD 11: test 12 (counter part: fires once, after the whole skill, no chain, none from or at a knocked-out
    // hero) and test 8 (counter part: the counter-attacker gets no action charge; the attacker's charge from
    // counter damage counts).
    //
    // The set-up is SkillPiecesTests': p0 (Speed 200) acts first, everyone else (Speed 50) after it. Test heroes:
    // Damage 200, Armor 100, Health 1,000,000, no Dodge and Accuracy 0 (so nothing misses unless a test gives a
    // Dodge). A 100 % hit with no crit does 182. Crit Chance is 10, so a test that reads a damage number sets it to 0.
    public class CounterTests
    {
        static Fight Duel(Action<string, JObject> edit = null, int seed = 1) => NewFight(
            new[] { ("p0", 200), ("p1", 50), ("p2", 50), ("p3", 50), ("p4", 50) },
            new[] { ("e0", 50), ("e1", 50), ("e2", 50), ("e3", 50), ("e4", 50) }, seed,
            edit: (id, h) => { h["stats"]["crit_chance"] = 0; edit?.Invoke(id, h); });

        static void Pieces(JObject hero, string button, params string[] pieces) =>
            hero[button]["pieces"] = JArray.Parse("[" + string.Join(",", pieces) + "]");

        static void Stat(JObject hero, string stat, double value) => hero["stats"][stat] = value;

        /// <summary>Plays turns (everyone else uses Basic) until p0 must choose.</summary>
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

        static bool HasCounter(Fighter f) => f.Effects.Any(e => e.Kind == "counter");

        [Test]
        public void Counter_FiresOnce_AfterTheWholeSkill_AsTheHoldersBasicAtTheAttacker()
        {
            var fight = Duel((id, h) =>
            {
                if (id == "p0") Pieces(h, "basic", "{ do: 'damage', target: 'enemy', power: 100, hits: 3 }");
            });
            var p0 = UntilP0(fight);
            var e0 = fight.Enemies[0];
            Effects.Apply(e0, "counter", 2);

            var events = fight.Act(new Choice(Button.Basic, e0));

            // the charge events (a hit's, the Basic's) are left out: they are not what this test is about
            var kinds = events.Where(e => e.Kind != FightEventKind.ChargeChanged).Select(e => e.Kind).ToList();
            CollectionAssert.AreEqual(new[]
            {
                FightEventKind.SkillUsed,
                FightEventKind.Damaged, FightEventKind.Damaged, FightEventKind.Damaged,   // all three hits first
                FightEventKind.CounterAttack, FightEventKind.EffectUsedUp,                // then the counter, spent
                FightEventKind.Damaged,                                                   // the Basic, at p0
            }, kinds, "one counter, after the skill's last hit");
            Assert.AreEqual(1, Of(events, FightEventKind.CounterAttack).Count);
            Assert.AreEqual("CounterAttack: Enemy 0 e0", Of(events, FightEventKind.CounterAttack)[0].ToString());
            Assert.AreEqual("EffectUsedUp: Enemy 0 e0 (counter)", Of(events, FightEventKind.EffectUsedUp)[0].ToString());
            Assert.AreEqual(p0, Of(events, FightEventKind.Damaged).Last().Who);
            Assert.AreEqual(182, Of(events, FightEventKind.Damaged).Last().Amount, "e0's Basic, whole");
            Assert.IsFalse(HasCounter(e0), "used once");
        }

        [Test]
        public void Counter_IsTheWholeBasic_AimedAtTheAttacker_TauntAndFrontRowIgnored()
        {
            // p4 (Speed 200) stands in the back row of a 4–1 team, behind four front-row heroes; p1 taunts
            var fight = NewFight(
                new[] { ("p0", 50), ("p1", 50), ("p2", 50), ("p3", 50), ("p4", 200) },
                new[] { ("e0", 50), ("e1", 50), ("e2", 50), ("e3", 50), ("e4", 50) }, playerFormation: "4-1",
                edit: (id, h) =>
                {
                    h["stats"]["crit_chance"] = 0;
                    if (id == "e0") Pieces(h, "basic", "{ do: 'damage', target: 'enemy', power: 100 }",
                                                       "{ do: 'effect', target: 'enemy', effect: 'offense_down', turns: 2 }");
                });
            fight.AdvanceToChoice();
            var p4 = fight.Acting;
            Assert.AreEqual(fight.Players[4], p4);
            Assert.AreEqual(Row.Back, p4.Row);
            var e0 = fight.Enemies[0];
            Effects.Apply(e0, "counter", 2);
            Effects.Apply(fight.Players[1], "taunt", 3);

            var events = fight.Act(new Choice(Button.Basic, e0));

            var afterCounter = events.SkipWhile(e => e.Kind != FightEventKind.CounterAttack)
                                     .Where(e => e.Kind != FightEventKind.ChargeChanged).ToList();
            CollectionAssert.AreEqual(new[] { FightEventKind.CounterAttack, FightEventKind.EffectUsedUp, FightEventKind.Damaged, FightEventKind.EffectApplied },
                                      afterCounter.Select(e => e.Kind), "every piece of the Basic applies");
            Assert.AreEqual(p4, afterCounter[2].Who, "the back-row attacker, not the taunting front-row hero");
            Assert.IsTrue(p4.Effects.Any(e => e.Kind == "offense_down"));
            Assert.AreEqual(0, Of(events, FightEventKind.Damaged, fight.Players[1]).Count);
        }

        [Test]
        public void Counter_IsAnAttack_ButNotATurn()
        {
            var fight = Duel();
            UntilP0(fight);
            var e0 = fight.Enemies[0];
            Effects.Apply(e0, "counter", 2);

            fight.Act(new Choice(Button.Basic, e0));

            Assert.IsTrue(e0.Attacked, "counts as an attack for Strix's first attack (R4, 10.6 q. 3)");
            Assert.IsFalse(e0.Acted, "not a turn: still hasn't acted yet (10.6 q. 4)");
            Assert.AreEqual(0, e0.TurnsStarted);
        }

        [Test]
        public void Counter_DoesNotChain_ACounterAttackNeverTriggersAnotherCounter()
        {
            var fight = Duel();
            var p0 = UntilP0(fight);
            var e0 = fight.Enemies[0];
            Effects.Apply(e0, "counter", 2);
            Effects.Apply(p0, "counter", 2);

            var events = fight.Act(new Choice(Button.Basic, e0));

            Assert.AreEqual(1, Of(events, FightEventKind.CounterAttack).Count, "only e0 counters");
            Assert.IsTrue(HasCounter(p0), "p0's own Counter wasn't triggered by the counter-attack, so it is still there");
            Assert.IsFalse(HasCounter(e0));
        }

        [Test]
        public void Counter_NotFromAHeroTheSkillKnockedOut()
        {
            var fight = Duel((id, h) => { if (id == "e0") Stat(h, "health", 100); });
            var p0 = UntilP0(fight);
            var e0 = fight.Enemies[0];
            Effects.Apply(e0, "counter", 2);

            var events = fight.Act(new Choice(Button.Basic, e0));

            Assert.IsTrue(e0.KnockedOut);
            CollectionAssert.IsEmpty(Of(events, FightEventKind.CounterAttack), "knocked out at once: no counter-attack");
            CollectionAssert.IsEmpty(Of(events, FightEventKind.Damaged, p0));
        }

        [Test]
        public void Counter_NotAtAnAttackerAlreadyKnockedOutByAnEarlierCounter_AndTheLaterCounterIsKept()
        {
            var fight = Duel((id, h) =>
            {
                if (id == "p0") Pieces(h, "basic", "{ do: 'damage', target: 'enemy_and_neighbours', power: 100 }");
                if (id == "p0") Stat(h, "health", 100);
            });
            var p0 = UntilP0(fight);
            var e = fight.Enemies;
            Effects.Apply(e[0], "counter", 2);
            Effects.Apply(e[1], "counter", 2);

            var events = fight.Act(new Choice(Button.Basic, e[0]));      // e0 and its neighbour e1

            Assert.IsTrue(p0.KnockedOut, "e0's counter, first in slot order, knocks p0 out");
            CollectionAssert.AreEqual(new[] { e[0] }, Of(events, FightEventKind.CounterAttack).Select(x => x.Who));
            Assert.IsTrue(HasCounter(e[1]), "e1's counter had nobody to hit, so it isn't used up");
        }

        [Test]
        public void Counter_EachHolderTheSkillHit_CountersOnce_InSlotOrder_OthersDont()
        {
            var fight = Duel((id, h) =>
            {
                if (id == "p0") Pieces(h, "basic", "{ do: 'damage', target: 'all_enemies', power: 10 }");
            });
            UntilP0(fight);
            var e = fight.Enemies;
            Effects.Apply(e[3], "counter", 2);
            Effects.Apply(e[0], "counter", 2);
            Effects.Apply(e[2], "counter", 2);

            var events = fight.Act(new Choice(Button.Basic, null));

            CollectionAssert.AreEqual(new[] { e[0], e[2], e[3] }, Of(events, FightEventKind.CounterAttack).Select(x => x.Who), "slot order");
            Assert.IsFalse(events.SkipWhile(x => x.Kind != FightEventKind.CounterAttack).Any(x => x.Kind == FightEventKind.Damaged && x.Who.Side == Side.Enemy),
                           "every enemy was hit before the first counter");
        }

        [Test]
        public void Counter_NeedsTheSkillToHitTheHolder_AMissLeavesItBe()
        {
            bool sawMiss = false, sawHit = false;
            for (var seed = 1; seed <= 40 && !(sawMiss && sawHit); seed++)
            {
                var fight = Duel((id, h) => { if (id == "e0") Stat(h, "dodge_chance", 30); }, seed);
                UntilP0(fight);
                var e0 = fight.Enemies[0];
                Effects.Apply(e0, "counter", 2);

                var events = fight.Act(new Choice(Button.Basic, e0));

                if (Of(events, FightEventKind.Missed, e0).Count == 1)
                {
                    CollectionAssert.IsEmpty(Of(events, FightEventKind.CounterAttack), $"seed {seed}: a missed skill doesn't hit the holder");
                    Assert.IsTrue(HasCounter(e0), "and the Counter is kept");
                    sawMiss = true;
                }
                else
                {
                    Assert.AreEqual(1, Of(events, FightEventKind.CounterAttack).Count, $"seed {seed}");
                    sawHit = true;
                }
            }
            Assert.IsTrue(sawMiss && sawHit, "the seeds cover a miss and a hit on e0");
        }

        [Test]
        public void Counter_OnlyAnEnemysSkillTriggersIt()
        {
            var fight = Duel((id, h) => { if (id == "p0") Pieces(h, "special", "{ do: 'heal', target: 'ally', percent: 10 }"); });
            UntilP0(fight);
            var p1 = fight.Players[1];
            Effects.Apply(p1, "counter", 2);

            var events = fight.Act(new Choice(Button.Special, p1));

            CollectionAssert.IsEmpty(Of(events, FightEventKind.CounterAttack), "a heal from an ally is not an enemy's skill");
            Assert.IsTrue(HasCounter(p1));
        }

        [Test]
        public void Test8_Counter_GivesTheCounterAttackerNoActionCharge_ButTheAttackersHurtChargeCounts()
        {
            var fight = Duel((id, h) =>
            {
                if (id == "p0" || id == "e0") Stat(h, "health", 1000);
            });
            var p0 = UntilP0(fight);
            var e0 = fight.Enemies[0];
            Effects.Apply(e0, "counter", 2);

            var events = fight.Act(new Choice(Button.Basic, e0));

            Assert.AreEqual(18.2, e0.Charge, 1e-9, "only the 182 it lost to p0's hit: no charge_basic for the counter");
            Assert.AreEqual(25 + 18.2, p0.Charge, 1e-9, "p0's own Basic charge, then 182 lost to the counter = 18.2 % of 1,000");
            Assert.AreEqual(1, Of(events, FightEventKind.ChargeChanged, e0).Count, "e0's charge changed once, from the hit");
        }

        [Test]
        public void Counter_ABasicAimedAtAnAllyIsRefused()
        {
            var fight = Duel((id, h) =>
            {
                if (id == "e0") Pieces(h, "basic", "{ do: 'heal', target: 'ally', percent: 10 }");
            });
            UntilP0(fight);
            var e0 = fight.Enemies[0];
            Effects.Apply(e0, "counter", 2);

            Assert.Throws<NotSupportedException>(() => fight.Act(new Choice(Button.Basic, e0)),
                "a counter has no tapped ally to aim at (nothing in the kits does this)");
        }
    }
}
