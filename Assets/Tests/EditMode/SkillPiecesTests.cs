using System;
using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules;
using HeroHQ.Rules.Data;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static HeroHQ.Tests.TestFights;

namespace HeroHQ.Tests
{
    // P1-10 (03 Architecture 4.12): SkillResolver part 2. The other four pieces (Heal, Apply effect, Clear, Turn
    // meter), the six piece conditions, the Special's cooldown and the ultimate's charge.
    // 02 GDD 11: test 6 (meter), test 7 (cooldown 2), test 8 (charge), test 10 (the effects part).
    //
    // The set-up is SkillDamageTests': p0 (Speed 200) acts at ticks 5, 10, 15 and 20, everyone else (Speed 50)
    // first at tick 20, after p0. So at p0's first turn every other meter is 250. Test heroes: Damage 200, Armor
    // 100, Crit Chance 10, Health 1,000,000, Special cooldown 2. Tuning: meter_full 1000, charge_basic 25,
    // charge_special 10, charge_per_hurt 1, armor_k 1000. A 100 % hit with no crit does 182.
    public class SkillPiecesTests
    {
        static Fight Duel(Action<string, JObject> edit = null, int seed = 1) => NewFight(
            new[] { ("p0", 200), ("p1", 50), ("p2", 50), ("p3", 50), ("p4", 50) },
            new[] { ("e0", 50), ("e1", 50), ("e2", 50), ("e3", 50), ("e4", 50) }, seed, edit: edit);

        /// <summary>Replaces a skill's pieces. Pieces are written with single quotes: "{ do: 'heal', target: 'ally', percent: 20 }".</summary>
        static void Pieces(JObject hero, string button, params string[] pieces) =>
            hero[button]["pieces"] = JArray.Parse("[" + string.Join(",", pieces) + "]");

        static void Stat(JObject hero, string stat, double value) => hero["stats"][stat] = value;

        static void Traits(JObject hero, params string[] traits) => hero["traits"] = new JArray(traits);

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

        /// <summary>The next 20 rolls of the fight match a fresh generator with this seed: nothing rolled so far.</summary>
        static void NothingRolled(Fight fight, int seed)
        {
            var expected = new System.Random(seed);
            for (var i = 0; i < 20; i++)
                Assert.AreEqual(expected.NextDouble() * 100 < 50, fight.Roll(50), $"roll {i}: nothing may roll before it");
        }

        // ---- Heal ---------------------------------------------------------------------------------------

        [Test]
        public void Heal_GivesBackAPercentOfTheTargetsMaxHealth_NeverAboveIt_WithNoRoll()
        {
            var fight = Duel((id, h) =>
            {
                if (!IsEnemy(id)) Stat(h, "health", 1000);
                if (id == "p0") Pieces(h, "ultimate", "{ do: 'heal', target: 'all_allies', percent: 20 }");
            });
            var p0 = UntilP0(fight);
            var p = fight.Players;
            p[0].Health = 900; p[1].Health = 500; p[3].Health = 100; p[4].Health = 950;   // p2 is full
            p0.Charge = 100;

            var events = fight.Act(new Choice(Button.Ultimate, null));

            var healed = Of(events, FightEventKind.Healed);
            CollectionAssert.AreEqual(p, healed.Select(e => e.Who), "every ally, the hero included, in slot order");
            CollectionAssert.AreEqual(new[] { 100, 200, 0, 200, 50 }, healed.Select(e => e.Amount),
                "20 % of 1,000 = 200, capped at the Health each one is missing");
            CollectionAssert.AreEqual(new[] { 1000, 700, 1000, 300, 1000 }, p.Select(f => f.Health));
            Assert.AreEqual("Healed: Player 1 p1 (200)", healed[1].ToString());
            NothingRolled(fight, 1);
        }

        // ---- Apply effect -------------------------------------------------------------------------------

        [Test]
        public void ApplyEffect_PutsTheEffectOnEachTarget_AndABarrierHoldsAPercentOfTheHoldersMaxHealth()
        {
            var fight = Duel((id, h) =>
            {
                if (id == "p0") Stat(h, "health", 1000);
                if (id == "p1") Stat(h, "health", 1002);
                if (id == "p0") Pieces(h, "special", "{ do: 'effect', target: 'all_allies', effect: 'barrier', turns: 2, percent: 25 }",
                                                     "{ do: 'effect', target: 'self', effect: 'taunt', turns: 1 }");
            });
            var p0 = UntilP0(fight);

            var events = fight.Act(new Choice(Button.Special, null));

            var p = fight.Players;
            CollectionAssert.AreEqual(new[] { 250, 251, 250000, 250000, 250000 },
                p.Select(f => f.Effects.Single(e => e.Kind == "barrier").Amount),
                "25 % of each holder's own max Health; 250.5 rounds up to 251 (halves up)");
            Assert.AreEqual(p0.TurnsStarted + 1, p0.Effects.Single(e => e.Kind == "taunt").EndsAfterTurn, "1 turn: through p0's next turn");
            Assert.AreEqual(6, Of(events, FightEventKind.EffectApplied).Count);
            NothingRolled(fight, 1);
        }

        [Test]
        public void Test10_AMiss_CancelsTheSkillsEffectsAndMeterDrainOnThatTargetOnly()
        {
            bool sawMiss = false, sawHit = false;
            for (var seed = 1; seed <= 40 && !(sawMiss && sawHit); seed++)
            {
                var fight = Duel((id, h) =>
                {
                    if (id == "p0") Pieces(h, "basic", "{ do: 'damage', target: 'enemy_and_neighbours', power: 10 }",
                                                       "{ do: 'effect', target: 'enemy_and_neighbours', effect: 'offense_down', turns: 2 }",
                                                       "{ do: 'meter', target: 'enemy_and_neighbours', percent: -10 }");
                    if (id == "e1") Stat(h, "dodge_chance", 30);
                }, seed);
                UntilP0(fight);
                var e = fight.Enemies;

                var events = fight.Act(new Choice(Button.Basic, e[1]));

                foreach (var other in new[] { e[0], e[2] })
                {
                    Assert.IsTrue(other.Effects.Any(x => x.Kind == "offense_down"), $"seed {seed}: {other} was hit");
                    Assert.AreEqual(150, other.Meter, "250 − 10 % of a full bar");
                }
                if (Of(events, FightEventKind.Missed, e[1]).Count == 1)
                {
                    CollectionAssert.IsEmpty(e[1].Effects, $"seed {seed}: no effect on the missed target");
                    Assert.AreEqual(250, e[1].Meter, "no drain either");
                    CollectionAssert.IsEmpty(events.Where(x => x.Who == e[1] && x.Kind != FightEventKind.Missed));
                    sawMiss = true;
                }
                else
                {
                    Assert.IsTrue(e[1].Effects.Any(x => x.Kind == "offense_down"));
                    Assert.AreEqual(150, e[1].Meter);
                    sawHit = true;
                }
            }
            Assert.IsTrue(sawMiss && sawHit, "the seeds cover a miss and a hit on e1");
        }

        // ---- Clear --------------------------------------------------------------------------------------

        [Test]
        public void Clear_TakesBadEffectsNewestFirst_UpToItsCount_AndLeavesBuffs()
        {
            var fight = Duel((id, h) =>
            {
                if (id == "p0") Pieces(h, "special", "{ do: 'clear', target: 'ally', count: 2 }");
            });
            UntilP0(fight);
            var p1 = fight.Players[1];
            Effects.Apply(p1, "defense_down", 3);
            Effects.Apply(p1, "taunt", 3);
            Effects.Apply(p1, "offense_down", 3);
            Effects.Apply(p1, "stun", 0);

            var cleared = Of(fight.Act(new Choice(Button.Special, p1)), FightEventKind.EffectCleared);

            CollectionAssert.AreEqual(new[] { "stun", "offense_down" }, cleared.Select(e => e.Effect), "newest first, two of them");
            CollectionAssert.AreEqual(new[] { "defense_down", "taunt" }, p1.Effects.Select(e => e.Kind));
            Assert.AreEqual("EffectCleared: Player 1 p1 (stun)", cleared[0].ToString());
        }

        [Test]
        public void Clear_WithNoCount_ClearsEveryBadEffect()
        {
            var fight = Duel((id, h) =>
            {
                if (id == "p0") Pieces(h, "ultimate", "{ do: 'clear', target: 'all_allies' }");
            });
            var p0 = UntilP0(fight);
            var p = fight.Players;
            Effects.Apply(p[1], "offense_down", 3);
            Effects.Apply(p[1], "defense_down", 3);
            Effects.Apply(p[1], "ward", 3);
            Effects.Apply(p[2], "stun", 0);
            p0.Charge = 100;

            fight.Act(new Choice(Button.Ultimate, null));

            CollectionAssert.AreEqual(new[] { "ward" }, p[1].Effects.Select(e => e.Kind), "the buff stays");
            CollectionAssert.IsEmpty(p[2].Effects);
        }

        // ---- Turn meter: test 6 -------------------------------------------------------------------------

        [Test]
        public void Test6_Drain_IsAPercentOfAFullBar_RoundedDown_AndNeverTakesAMeterBelow0()
        {
            var fight = Duel((id, h) =>
            {
                if (id == "p0")
                {
                    Pieces(h, "basic", "{ do: 'damage', target: 'enemy', power: 10 }", "{ do: 'meter', target: 'enemy', percent: -12.34 }");
                    Pieces(h, "special", "{ do: 'meter', target: 'enemy', percent: -50 }");
                }
            });
            UntilP0(fight);
            var e0 = fight.Enemies[0];

            var first = Of(fight.Act(new Choice(Button.Basic, e0)), FightEventKind.MeterChanged).Single();
            Assert.AreEqual(-123, first.Amount, "12.34 % of 1,000 = 123.4, rounded down");
            Assert.AreEqual(127, e0.Meter);
            Assert.AreEqual("MeterChanged: Enemy 0 e0 (-123)", first.ToString());

            UntilP0(fight);                                       // e0 has filled 250 more: 377
            var second = Of(fight.Act(new Choice(Button.Special, e0)), FightEventKind.MeterChanged).Single();
            Assert.AreEqual(-377, second.Amount, "500 asked, only 377 there: the event says what really changed");
            Assert.AreEqual(0, e0.Meter);
        }

        [Test]
        public void Test6_Fill_RoundedDown_CanMakeAHeroActAtOnce()
        {
            var fight = Duel((id, h) =>
            {
                if (id == "p0") Pieces(h, "special", "{ do: 'meter', target: 'ally', percent: 75.05 }");
            });
            UntilP0(fight);
            var p1 = fight.Players[1];

            var filled = Of(fight.Act(new Choice(Button.Special, p1)), FightEventKind.MeterChanged).Single();
            Assert.AreEqual(750, filled.Amount, "750.5 rounded down");

            fight.AdvanceToChoice();
            Assert.AreSame(p1, fight.Acting, "250 + 750 = a full bar: p1 acts at once");
            Assert.AreEqual(1000, p1.Meter, "no tick in between");
            Assert.AreEqual(250, fight.Enemies[0].Meter);
        }

        // ---- Cooldown: test 7 ---------------------------------------------------------------------------

        [Test]
        public void Test7_Cooldown2_SpecialGreyedOnTheNextTwoTurns_ReadyOnTheThird()
        {
            var fight = Duel();
            var p0 = UntilP0(fight);

            fight.Act(new Choice(Button.Special, fight.Enemies[1]));
            Assert.AreEqual(2, p0.Cooldown, "the button shows the turns left");

            UntilP0(fight);
            Assert.IsFalse(fight.Options[1].Usable, "next turn: greyed");
            Assert.AreEqual(2, p0.Cooldown);
            fight.Act(Basic(fight));

            UntilP0(fight);
            Assert.IsFalse(fight.Options[1].Usable, "the turn after: greyed");
            Assert.AreEqual(1, p0.Cooldown);
            fight.Act(Basic(fight));

            UntilP0(fight);
            Assert.IsTrue(fight.Options[1].Usable, "third turn: ready");
            Assert.AreEqual(0, p0.Cooldown);
        }

        [Test]
        public void Cooldown_ASkippedTurnCountsDownToo()
        {
            // reading R10 (03 Architecture 4.12): a stunned turn is one of the hero's turns, as for effects
            var fight = Duel();
            var p0 = UntilP0(fight);
            fight.Act(new Choice(Button.Special, fight.Enemies[1]));
            Effects.Apply(p0, "stun", 0);

            UntilP0(fight);                                       // turn 2 is skipped on the way

            Assert.AreEqual(3, p0.TurnsStarted);
            Assert.AreEqual(1, p0.Cooldown);
            Assert.IsFalse(fight.Options[1].Usable);
        }

        // ---- Ultimate charge: test 8 --------------------------------------------------------------------

        [Test]
        public void Test8_ABasicCharges_EvenWhenItMisses()
        {
            for (var seed = 1; seed <= 40; seed++)
            {
                var fight = Duel((id, h) => { if (id == "e0") Stat(h, "dodge_chance", 30); }, seed);
                var p0 = UntilP0(fight);

                var events = fight.Act(new Choice(Button.Basic, fight.Enemies[0]));
                if (Of(events, FightEventKind.Missed).Count == 0) continue;

                Assert.AreEqual(25, p0.Charge, "charge_basic, even on a miss: a miss is never punished twice");
                var charged = Of(events, FightEventKind.ChargeChanged, p0).Single();
                Assert.AreEqual(25, charged.Charge);
                Assert.AreEqual("ChargeChanged: Player 0 p0 (25)", charged.ToString());
                return;
            }
            Assert.Fail("no seed in 40 missed");
        }

        [Test]
        public void Test8_ASpecialCharges_charge_special_AndTheUltimateEmptiesTheChargeFirst_AndGivesNone()
        {
            var fight = Duel();
            var p0 = UntilP0(fight);

            fight.Act(new Choice(Button.Special, fight.Enemies[1]));
            Assert.AreEqual(10, p0.Charge);

            UntilP0(fight);
            p0.Charge = 100;
            var events = fight.Act(new Choice(Button.Ultimate, null));

            Assert.AreEqual(0, p0.Charge);
            Assert.AreEqual(FightEventKind.ChargeChanged, events[1].Kind, "right after SkillUsed, before the hits");
            Assert.AreSame(p0, events[1].Who);
            Assert.AreEqual(0, events[1].Charge);
            Assert.AreEqual(1, Of(events, FightEventKind.ChargeChanged, p0).Count, "the ultimate gives no charge back");
        }

        [Test]
        public void Test8_DamageTakenCharges_PerOnePercentOfMaxHealthLost_ButNotWhatTheBarrierSoaked()
        {
            var fight = Duel((id, h) =>
            {
                if (IsEnemy(id)) Stat(h, "health", 1000);
                if (id == "p0")
                {
                    Stat(h, "crit_chance", 0);
                    Pieces(h, "basic", "{ do: 'damage', target: 'all_enemies', power: 100 }");
                }
            });
            UntilP0(fight);
            var e = fight.Enemies;
            Effects.Apply(e[0], "barrier", 2, amount: 100);
            Effects.Apply(e[1], "barrier", 2, amount: 500);

            var events = fight.Act(new Choice(Button.Basic, null)).ToList();

            Assert.AreEqual(8.2, e[0].Charge, 1e-9, "182 − 100 soaked = 82 lost = 8.2 % of 1,000: fractions kept");
            Assert.AreEqual(0, e[1].Charge, "all soaked: nothing lost, no charge");
            CollectionAssert.IsEmpty(Of(events, FightEventKind.ChargeChanged, e[1]));
            Assert.AreEqual(18.2, e[2].Charge, 1e-9);
            var damaged = events.IndexOf(Of(events, FightEventKind.Damaged, e[2]).Single());
            Assert.AreEqual(FightEventKind.ChargeChanged, events[damaged + 1].Kind, "the charge right after the hit");
        }

        [Test]
        public void Test8_Charge_IsCappedAt100()
        {
            var fight = Duel((id, h) =>
            {
                if (id == "e0") Stat(h, "health", 1000);
                if (id == "p0") Stat(h, "crit_chance", 0);
            });
            var p0 = UntilP0(fight);
            var e0 = fight.Enemies[0];
            p0.Charge = 90;
            e0.Charge = 99;

            var events = fight.Act(new Choice(Button.Basic, e0));

            Assert.AreEqual(100, p0.Charge);
            Assert.AreEqual(100, e0.Charge, "99 + 18.2 stops at 100");
            Assert.AreEqual(100, Of(events, FightEventKind.ChargeChanged, e0).Single().Charge);
        }

        [Test]
        public void Test8_ASkippedTurnGivesNoCharge()
        {
            var fight = Duel();
            var p0 = fight.Players[0];
            Effects.Apply(p0, "stun", 0);

            var events = fight.AdvanceToChoice();                 // p0's first turn is skipped, then p0 again at tick 10

            Assert.AreEqual(1, Of(events, FightEventKind.TurnSkipped, p0).Count);
            Assert.AreSame(p0, fight.Acting);
            Assert.AreEqual(0, p0.Charge);
            CollectionAssert.IsEmpty(Of(events, FightEventKind.ChargeChanged));
        }

        // ---- the six piece conditions (02 GDD 7.3) ------------------------------------------------------

        /// <summary>p0's Basic: a plain 100 % hit, then a second 100 % hit only if <paramref name="condition"/> holds.</summary>
        static void SecondHitIf(JObject p0, string condition) =>
            Pieces(p0, "basic", "{ do: 'damage', target: 'enemy', power: 100 }",
                                "{ do: 'damage', target: 'enemy', power: 100, if: " + condition + " }");

        [Test]
        public void Condition_AlliesWith_CountsTheOtherFour_KnockedOutAlliesIncluded()
        {
            int Hits(string[] norse, bool knockOutP2)
            {
                var fight = Duel((id, h) =>
                {
                    if (norse.Contains(id)) Traits(h, "hero", "norse", "human");
                    if (id == "p0") SecondHitIf(h, "{ test: 'allies_with', count: 2, trait: 'norse' }");
                });
                UntilP0(fight);
                if (knockOutP2) fight.Players[2].KnockedOut = true;
                return Of(fight.Act(new Choice(Button.Basic, fight.Enemies[0])), FightEventKind.Damaged).Count;
            }

            Assert.AreEqual(2, Hits(new[] { "p1", "p2" }, false), "two Norse allies");
            Assert.AreEqual(2, Hits(new[] { "p1", "p2" }, true), "a knocked-out ally still counts (02 GDD 8)");
            Assert.AreEqual(1, Hits(new[] { "p0", "p1" }, false), "the hero isn't one of their own allies");
        }

        [Test]
        public void Condition_PerAllyWith_ScalesThePiecesNumber_AndNoMatchingAllySkipsIt()
        {
            // reading R8 (03 Architecture 4.12): the piece's number is multiplied by the count
            List<FightEvent> Play(int casters)
            {
                var fight = Duel((id, h) =>
                {
                    if (id != "p0" && !IsEnemy(id) && int.Parse(id.Substring(1)) <= casters) h["class"] = "caster";
                    if (id == "p0")
                    {
                        Stat(h, "crit_chance", 0);
                        Pieces(h, "basic", "{ do: 'damage', target: 'enemy', power: 50, if: { test: 'per_ally_with', class: 'caster' } }",
                                           "{ do: 'heal', target: 'self', percent: 1, if: { test: 'per_ally_with', class: 'caster' } }");
                    }
                });
                var p0 = UntilP0(fight);
                p0.Health = 900000;
                return fight.Act(new Choice(Button.Basic, fight.Enemies[0])).ToList();
            }

            var two = Play(2);
            Assert.AreEqual(182, Of(two, FightEventKind.Damaged).Single().Amount, "50 % × 2 casters = 100 %");
            Assert.AreEqual(20000, Of(two, FightEventKind.Healed).Single().Amount, "1 % × 2 of 1,000,000");

            var none = Play(0);
            CollectionAssert.IsEmpty(Of(none, FightEventKind.Damaged));
            CollectionAssert.IsEmpty(Of(none, FightEventKind.Healed));
            CollectionAssert.IsEmpty(Of(none, FightEventKind.Missed), "a skipped piece touches nobody");
        }

        [Test]
        public void Condition_TargetHasEffect_IsCheckedPerTarget_WhenThePieceRuns()
        {
            var fight = Duel((id, h) =>
            {
                if (id == "p0") Pieces(h, "basic", "{ do: 'effect', target: 'enemy', effect: 'defense_down', turns: 2 }",
                                                   "{ do: 'damage', target: 'all_enemies', power: 100, if: { test: 'target_has_effect', effect: 'defense_down' } }");
            });
            UntilP0(fight);
            var e = fight.Enemies;
            Effects.Apply(e[3], "defense_down", 2);

            var events = fight.Act(new Choice(Button.Basic, e[1]));

            CollectionAssert.AreEqual(new[] { e[1], e[3] }, Of(events, FightEventKind.Damaged).Select(x => x.Who),
                "e1, marked by the first piece, and e3, marked before");
        }

        [Test]
        public void Condition_TargetHas_ATraitOrAClass()
        {
            var fight = Duel((id, h) =>
            {
                if (id == "e2") Traits(h, "hero", "norse", "human");
                if (id == "e4") h["class"] = "caster";
                if (id == "p0") Pieces(h, "basic", "{ do: 'damage', target: 'all_enemies', power: 100, if: { test: 'target_has', trait: 'norse' } }",
                                                   "{ do: 'damage', target: 'all_enemies', power: 100, if: { test: 'target_has', class: 'caster' } }");
            });
            UntilP0(fight);

            var events = fight.Act(new Choice(Button.Basic, null));

            var touched = events.Where(x => x.Kind == FightEventKind.Damaged || x.Kind == FightEventKind.Missed).Select(x => x.Who);
            CollectionAssert.AreEqual(new[] { fight.Enemies[2], fight.Enemies[4] }, touched,
                "only the matching enemies are touched at all (no roll, no event for the rest)");
        }

        [Test]
        public void Condition_TargetNotActed_ASkippedTurnIsNotActing()
        {
            var fight = Duel((id, h) => { if (id == "p0") SecondHitIf(h, "{ test: 'target_not_acted' }"); });
            UntilP0(fight);
            var e = fight.Enemies;

            Assert.AreEqual(2, Of(fight.Act(new Choice(Button.Basic, e[0])), FightEventKind.Damaged).Count, "nobody has acted yet");
            Effects.Apply(e[1], "stun", 0);
            for (var turn = 2; turn <= 4; turn++)
            {
                UntilP0(fight);
                fight.Act(new Choice(Button.Basic, e[2]));
            }
            UntilP0(fight);                                       // every enemy has had a turn at tick 20; e1's was skipped

            Assert.IsTrue(e[0].Acted);
            Assert.IsFalse(e[1].Acted, "a stunned, skipped turn is not acting (02 GDD 10.6 q. 4)");
            Assert.AreEqual(2, Of(fight.Act(new Choice(Button.Basic, e[1])), FightEventKind.Damaged).Count);
            UntilP0(fight);
            Assert.AreEqual(1, Of(fight.Act(new Choice(Button.Basic, e[0])), FightEventKind.Damaged).Count, "e0 has acted");
        }

        [Test]
        public void Condition_FirstAttack_IsTheFirstSkillAimedAtAnEnemy()
        {
            var fight = Duel((id, h) =>
            {
                if (id != "p0") return;
                SecondHitIf(h, "{ test: 'first_attack' }");
                Pieces(h, "special", "{ do: 'heal', target: 'self', percent: 10 }");
            });
            var p0 = UntilP0(fight);

            fight.Act(new Choice(Button.Special, null));
            Assert.IsFalse(p0.Attacked, "a heal is not an attack");

            UntilP0(fight);
            Assert.AreEqual(2, Of(fight.Act(new Choice(Button.Basic, fight.Enemies[0])), FightEventKind.Damaged).Count, "the first attack");
            Assert.IsTrue(p0.Attacked);

            UntilP0(fight);
            Assert.AreEqual(1, Of(fight.Act(new Choice(Button.Basic, fight.Enemies[0])), FightEventKind.Damaged).Count, "the second isn't");
        }

        // ---- load check (03 Architecture 3.4: a piece missing a setting its kind needs) -----------------

        [Test]
        public void Load_ABarrierPieceWithoutPercent_IsAProblem()
        {
            var heroes = TestData.HeroesWith("rowan", h => h["special"]["pieces"] =
                JArray.Parse("[{ do: 'effect', target: 'self', effect: 'barrier', turns: 2 }]"));

            var problems = Assert.Throws<GameDataException>(() => TestData.Load(heroes: heroes)).Problems;

            Assert.AreEqual(1, problems.Count, string.Join(" | ", problems));
            StringAssert.Contains("rowan", problems[0]);
            StringAssert.Contains("special.pieces[0].percent", problems[0]);
            StringAssert.Contains("missing", problems[0]);
        }
    }
}
