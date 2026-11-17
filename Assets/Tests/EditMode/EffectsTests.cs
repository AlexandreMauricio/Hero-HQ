using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules;
using NUnit.Framework;
using static HeroHQ.Tests.TestFights;

namespace HeroHQ.Tests
{
    // P1-07 (03 Architecture 4.12): effects and Stun. 02 GDD 11 test 12 (effects part) and test 5 (Stun).
    //
    // Durations are counted in the HOLDER's own turns (02 GDD 7.4): "N turns" = until the end of the holder's
    // Nth turn that starts after the effect was applied. A turn the holder only skips because of a Stun still
    // counts as one of their turns.
    public class EffectsTests
    {
        const int BarLength = 8;     // turn_bar_length in TestData.Tuning

        // ---- set-up helpers -----------------------------------------------------------------------------

        /// <summary>Distinct Speeds, all different, so who acts first never depends on the tie rule: p1 first.</summary>
        static Fight NewTestFight() => NewFight(
            new[] { ("p0", 100), ("p1", 120), ("p2", 90), ("p3", 80), ("p4", 70) },
            new[] { ("e0", 110), ("e1", 60), ("e2", 50), ("e3", 40), ("e4", 30) });

        static Fighter Bare() => new Fighter(Side.Player, 0, 100);

        static IEnumerable<string> Kinds(Fighter f) => f.Effects.Select(e => e.Kind);

        static Effect Find(Fighter f, string kind) => f.Effects.FirstOrDefault(e => e.Kind == kind);

        /// <summary>Plays turns (everyone uses Basic) until <paramref name="who"/> is the one choosing. Returns all events on the way.</summary>
        static List<FightEvent> PlayUntilItsTurn(Fight fight, Fighter who)
        {
            var events = new List<FightEvent>();
            for (var guard = 0; guard < 200; guard++)
            {
                events.AddRange(fight.AdvanceToChoice());
                if (fight.Acting == who) return events;
                events.AddRange(fight.Act(Basic(fight)));
            }
            Assert.Fail($"{who} never got a turn");
            return events;
        }

        /// <summary>The holder's whole turn: they choose (Basic) and the turn ends. Returns the events of the end of the turn.</summary>
        static IReadOnlyList<FightEvent> FinishTurn(Fight fight) => fight.Act(Basic(fight));

        static List<FightEventKind> EventKinds(IEnumerable<FightEvent> events) => events.Select(e => e.Kind).ToList();

        // ---- applying -----------------------------------------------------------------------------------

        [Test]
        public void Apply_PutsTheEffectOnTheHero_AndSaysSo()
        {
            var hero = Bare();

            var events = Effects.Apply(hero, "offense_up", 2);

            Assert.AreEqual(1, hero.Effects.Count);
            Assert.AreEqual("offense_up", hero.Effects[0].Kind);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(FightEventKind.EffectApplied, events[0].Kind);
            Assert.AreSame(hero, events[0].Who);
            Assert.AreEqual("offense_up", events[0].Effect);
        }

        [Test]
        public void Effects_RememberTheOrderTheyWerePutOn_SoClearCanTakeTheNewestFirst()
        {
            var hero = Bare();
            Effects.Apply(hero, "defense_down", 2);
            Effects.Apply(hero, "taunt", 1);
            Effects.Apply(hero, "offense_down", 3);
            Effects.Apply(hero, "defense_down", 3);      // a second copy is not a new entry: it keeps its place

            CollectionAssert.AreEqual(new[] { "defense_down", "taunt", "offense_down" }, Kinds(hero));
        }

        [Test]
        public void IsBad_IsTrueForTheThreeBadEffectsOnly()
        {
            foreach (var bad in new[] { "offense_down", "defense_down", "stun" }) Assert.IsTrue(Effects.IsBad(bad), bad);
            foreach (var good in new[] { "offense_up", "defense_up", "taunt", "barrier", "ward", "counter", "momentum" })
                Assert.IsFalse(Effects.IsBad(good), good);
        }

        // ---- durations ----------------------------------------------------------------------------------

        [Test]
        public void Duration_PutOnYourselfInYourTurn_LastsThroughYourNextTurn()
        {
            var fight = NewTestFight();
            var hero = fight.Players[1];
            PlayUntilItsTurn(fight, hero);

            Effects.Apply(hero, "offense_up", 1);        // Kal's self-buff: "for 1 turn", used in his own turn
            var endOfThisTurn = FinishTurn(fight);
            Assert.IsNotNull(Find(hero, "offense_up"), "the turn it was put on doesn't count (02 GDD 7.4)");
            CollectionAssert.DoesNotContain(EventKinds(endOfThisTurn), FightEventKind.EffectExpired);

            PlayUntilItsTurn(fight, hero);
            Assert.IsNotNull(Find(hero, "offense_up"), "still there while the hero chooses their next attack");

            var endOfNextTurn = FinishTurn(fight);
            Assert.IsNull(Find(hero, "offense_up"), "gone at the end of the next turn");
            var expired = endOfNextTurn.Single(e => e.Kind == FightEventKind.EffectExpired);
            Assert.AreSame(hero, expired.Who);
            Assert.AreEqual("offense_up", expired.Effect);
        }

        [Test]
        public void Duration_TwoTurns_LastsThroughTheSecondTurnAfter()
        {
            var fight = NewTestFight();
            var hero = fight.Players[1];
            PlayUntilItsTurn(fight, hero);
            Effects.Apply(hero, "defense_up", 2);
            FinishTurn(fight);

            PlayUntilItsTurn(fight, hero);
            FinishTurn(fight);
            Assert.IsNotNull(Find(hero, "defense_up"), "one of its two turns is over");

            PlayUntilItsTurn(fight, hero);
            FinishTurn(fight);
            Assert.IsNull(Find(hero, "defense_up"), "the second turn is over");
        }

        [Test]
        public void Duration_PutOnAnEnemyInYourTurn_CountsTheEnemysVeryNextTurn()
        {
            var fight = NewTestFight();
            var mine = fight.Players[1];
            var enemy = fight.Enemies[0];                // Speed 110: acts after p1 (120)
            PlayUntilItsTurn(fight, mine);
            Effects.Apply(enemy, "defense_down", 1);
            FinishTurn(fight);

            PlayUntilItsTurn(fight, enemy);
            Assert.IsNotNull(Find(enemy, "defense_down"), "it is on them while they act");
            FinishTurn(fight);
            Assert.IsNull(Find(enemy, "defense_down"), "and ends with that very turn");
        }

        [Test]
        public void Duration_PutOnAgainAtYourNextTurn_ReachesToTheTurnAfterThat()
        {
            // Halvard's Taunt: used every other turn, it must keep covering the enemy turns in between.
            var fight = NewTestFight();
            var hero = fight.Players[1];
            PlayUntilItsTurn(fight, hero);
            Effects.Apply(hero, "taunt", 1);
            FinishTurn(fight);

            PlayUntilItsTurn(fight, hero);
            Effects.Apply(hero, "taunt", 1);             // still on him: put on again in this turn
            FinishTurn(fight);
            Assert.IsNotNull(Find(hero, "taunt"), "the second Taunt is for his NEXT turn, so it is still there");

            PlayUntilItsTurn(fight, hero);
            FinishTurn(fight);
            Assert.IsNull(Find(hero, "taunt"));
        }

        [Test]
        public void Duration_ASkippedStunnedTurn_StillCountsAsATurn()
        {
            var fight = NewTestFight();
            var hero = fight.Enemies[0];
            Effects.Apply(hero, "defense_down", 1);
            Effects.Apply(hero, "stun", 0);

            PlayUntilItsTurn(fight, fight.Players[1]);                   // p1 acts first; e0's turn comes after
            Assert.IsNotNull(Find(hero, "defense_down"));
            FinishTurn(fight);
            var events = PlayUntilItsTurn(fight, fight.Players[0]);      // e0 is skipped on the way

            Assert.IsNull(Find(hero, "defense_down"), "its 1 turn was the skipped one");
            Assert.AreEqual(1, events.Count(e => e.Kind == FightEventKind.TurnSkipped));
        }

        [Test]
        public void Expiring_ManyAtOnce_ComesOutInTheOrderPutOn()
        {
            var fight = NewTestFight();
            var hero = fight.Players[1];
            Effects.Apply(hero, "taunt", 1);
            Effects.Apply(hero, "offense_up", 1);
            Effects.Apply(hero, "defense_up", 1);
            PlayUntilItsTurn(fight, hero);

            var events = FinishTurn(fight);

            CollectionAssert.AreEqual(new[] { "taunt", "offense_up", "defense_up" },
                events.Where(e => e.Kind == FightEventKind.EffectExpired).Select(e => e.Effect));
        }

        [Test]
        public void Barrier_AlsoEndsAfterItsTurns()
        {
            var fight = NewTestFight();
            var hero = fight.Players[1];
            Effects.Apply(hero, "barrier", 1, amount: 300);
            PlayUntilItsTurn(fight, hero);

            FinishTurn(fight);

            Assert.IsNull(Find(hero, "barrier"));
        }

        [Test]
        public void StunAndMomentum_HaveNoTurnCount_TheyNeverRunOut()
        {
            var hero = Bare();
            Effects.Apply(hero, "momentum", 0, 5);   // the amount of Momentum is its most stacks (P1-14)
            Effects.Apply(hero, "stun", 0);
            hero.TurnsStarted = 50;

            var events = Effects.EndTurn(hero);

            CollectionAssert.IsEmpty(events);
            CollectionAssert.AreEqual(new[] { "momentum", "stun" }, Kinds(hero));
        }

        // ---- one copy each ------------------------------------------------------------------------------

        [Test]
        public void OneCopy_AppliedAgainWithALongerTime_ResetsToTheLongerTime()
        {
            var fight = NewTestFight();
            var hero = fight.Players[1];
            Effects.Apply(hero, "offense_up", 1);
            Effects.Apply(hero, "offense_up", 3);
            Assert.AreEqual(1, hero.Effects.Count, "one copy each");

            for (var turn = 1; turn <= 2; turn++)
            {
                PlayUntilItsTurn(fight, hero);
                FinishTurn(fight);
                Assert.IsNotNull(Find(hero, "offense_up"), $"still on after turn {turn} of 3");
            }
            PlayUntilItsTurn(fight, hero);
            FinishTurn(fight);
            Assert.IsNull(Find(hero, "offense_up"), "gone after turn 3");
        }

        [Test]
        public void OneCopy_AppliedAgainWithAShorterTime_KeepsTheLongerTime()
        {
            var fight = NewTestFight();
            var hero = fight.Players[1];
            Effects.Apply(hero, "offense_up", 3);
            Effects.Apply(hero, "offense_up", 1);
            Assert.AreEqual(1, hero.Effects.Count);

            for (var turn = 1; turn <= 2; turn++)
            {
                PlayUntilItsTurn(fight, hero);
                FinishTurn(fight);
            }
            Assert.IsNotNull(Find(hero, "offense_up"), "still on after turn 2: the 3 turns were kept");
        }

        [Test]
        public void OneCopy_AppliedAgain_IsStillReportedAsApplied()
        {
            var hero = Bare();
            Effects.Apply(hero, "taunt", 2);

            var events = Effects.Apply(hero, "taunt", 1);

            Assert.AreEqual(new[] { FightEventKind.EffectApplied }, EventKinds(events));
        }

        [Test]
        public void Barrier_AppliedAgain_KeepsTheBiggerAmount()
        {
            var hero = Bare();
            Effects.Apply(hero, "barrier", 2, amount: 100);
            Effects.Apply(hero, "barrier", 1, amount: 250);
            Assert.AreEqual(1, hero.Effects.Count);
            Assert.AreEqual(250, Find(hero, "barrier").Amount, "the bigger amount wins");

            Effects.Apply(hero, "barrier", 1, amount: 40);
            Assert.AreEqual(250, Find(hero, "barrier").Amount, "a smaller one never lowers it");
        }

        // ---- opposites cancel ---------------------------------------------------------------------------

        [TestCase("offense_up", "offense_down")]
        [TestCase("offense_down", "offense_up")]
        [TestCase("defense_up", "defense_down")]
        [TestCase("defense_down", "defense_up")]
        public void Opposites_RemoveEachOtherInsteadOfStacking(string first, string second)
        {
            var hero = Bare();
            Effects.Apply(hero, first, 2);

            var events = Effects.Apply(hero, second, 2);

            CollectionAssert.IsEmpty(hero.Effects, "neither is left: the new one just cancels the old");
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(FightEventKind.EffectCancelled, events[0].Kind);
            Assert.AreEqual(first, events[0].Effect, "the event names what was removed");
        }

        [Test]
        public void Opposites_DoNotTouchTheOtherPair()
        {
            var hero = Bare();
            Effects.Apply(hero, "offense_up", 2);

            Effects.Apply(hero, "defense_down", 2);

            CollectionAssert.AreEqual(new[] { "offense_up", "defense_down" }, Kinds(hero));
        }

        // ---- Ward ---------------------------------------------------------------------------------------

        [TestCase("offense_down")]
        [TestCase("defense_down")]
        [TestCase("stun")]
        public void Ward_StopsTheNextBadEffect_AndIsUsedUp(string bad)
        {
            var hero = Bare();
            Effects.Apply(hero, "ward", 3);

            var events = Effects.Apply(hero, bad, 2);

            CollectionAssert.IsEmpty(hero.Effects, "the bad effect never landed and the Ward is gone");
            CollectionAssert.AreEqual(new[] { FightEventKind.EffectBlocked, FightEventKind.EffectUsedUp }, EventKinds(events));
            Assert.AreEqual(bad, events[0].Effect, "blocked: the effect that was stopped");
            Assert.AreEqual("ward", events[1].Effect, "used up: the Ward");
        }

        [Test]
        public void Ward_IsUsedOnce_TheSecondBadEffectLands()
        {
            var hero = Bare();
            Effects.Apply(hero, "ward", 3);
            Effects.Apply(hero, "offense_down", 2);

            Effects.Apply(hero, "defense_down", 2);

            CollectionAssert.AreEqual(new[] { "defense_down" }, Kinds(hero));
        }

        [Test]
        public void Ward_DoesNotStopGoodEffects()
        {
            var hero = Bare();
            Effects.Apply(hero, "ward", 3);

            Effects.Apply(hero, "taunt", 1);
            Effects.Apply(hero, "offense_up", 1);

            CollectionAssert.AreEqual(new[] { "ward", "taunt", "offense_up" }, Kinds(hero));
        }

        [Test]
        public void Ward_ComesBeforeTheCancel_TheOffenseUpStays()      // 02 GDD 10.6 q. 6
        {
            var hero = Bare();
            Effects.Apply(hero, "offense_up", 2);
            Effects.Apply(hero, "ward", 2);

            var events = Effects.Apply(hero, "offense_down", 2);

            CollectionAssert.AreEqual(new[] { "offense_up" }, Kinds(hero), "Ward used up, Offense Up untouched, no Offense Down");
            CollectionAssert.AreEqual(new[] { FightEventKind.EffectBlocked, FightEventKind.EffectUsedUp }, EventKinds(events));
        }

        [Test]
        public void Ward_AlsoEndsAfterItsTurns()
        {
            var fight = NewTestFight();
            var hero = fight.Players[1];
            Effects.Apply(hero, "ward", 1);
            PlayUntilItsTurn(fight, hero);

            FinishTurn(fight);

            Assert.IsNull(Find(hero, "ward"));
        }

        // ---- Stun: applying -----------------------------------------------------------------------------

        [Test]
        public void Stun_ASecondStunOnAStunnedHero_DoesNothing()
        {
            var hero = Bare();
            Effects.Apply(hero, "stun", 0);

            var events = Effects.Apply(hero, "stun", 0);

            Assert.AreEqual(1, hero.Effects.Count);
            CollectionAssert.IsEmpty(events, "no second Stun, and nothing to report");
            Assert.IsTrue(Effects.IsStunned(hero));
        }

        [Test]
        public void Stun_ASecondStunDoesNotSpendAWard_BecauseItWouldNotHaveLanded()
        {
            var hero = Bare();
            Effects.Apply(hero, "stun", 0);
            Effects.Apply(hero, "ward", 2);

            var events = Effects.Apply(hero, "stun", 0);

            CollectionAssert.IsEmpty(events);
            CollectionAssert.AreEqual(new[] { "stun", "ward" }, Kinds(hero));
        }

        // ---- Knockouts ----------------------------------------------------------------------------------

        [Test]
        public void KnockedOut_LosesEveryEffect()
        {
            var hero = Bare();
            Effects.Apply(hero, "offense_up", 2);
            Effects.Apply(hero, "stun", 0);
            Effects.Apply(hero, "barrier", 2, amount: 50);

            Effects.LoseAll(hero);

            CollectionAssert.IsEmpty(hero.Effects);
            Assert.IsFalse(Effects.IsStunned(hero));
        }

        [Test]
        public void KnockedOut_CantGetNewEffects()
        {
            var hero = Bare();
            hero.KnockedOut = true;

            var events = Effects.Apply(hero, "defense_up", 2);

            CollectionAssert.IsEmpty(events);
            CollectionAssert.IsEmpty(hero.Effects);
        }

        // ---- test 5: Stun in the fight ------------------------------------------------------------------

        [Test]
        public void Test5_AStunnedHerosTurnIsSkippedOnce_TheMeterStillResets_AndTheStunEnds()
        {
            var fight = NewTestFight();
            var stunned = fight.Players[1];              // the fastest: would act first
            Effects.Apply(stunned, "stun", 0);

            var events = fight.AdvanceToChoice();

            Assert.AreNotSame(stunned, fight.Acting, "the stunned hero never gets to choose");
            Assert.AreEqual(1, stunned.TurnsStarted, "it was their turn, so it counts as one");
            CollectionAssert.AreEqual(
                new[] { FightEventKind.TurnStarted, FightEventKind.TurnSkipped, FightEventKind.EffectUsedUp, FightEventKind.TurnStarted },
                EventKinds(events));
            Assert.AreSame(stunned, events[0].Who);
            Assert.AreSame(stunned, events[1].Who);
            Assert.AreEqual("stun", events[2].Effect);
            Assert.IsFalse(Effects.IsStunned(stunned), "the Stun ended when the turn was skipped");
            Assert.Less(stunned.Meter, 1000, "its meter dropped by one full bar even though it did nothing");
        }

        [Test]
        public void Test5_TheTurnAfterTheSkippedOne_IsAnOrdinaryTurn()
        {
            var fight = NewTestFight();
            var stunned = fight.Players[1];
            Effects.Apply(stunned, "stun", 0);

            PlayUntilItsTurn(fight, stunned);

            Assert.AreSame(stunned, fight.Acting, "it gets its next turn as normal");
            Assert.AreEqual(2, stunned.TurnsStarted);
        }

        [Test]
        public void Test5_TheBarMarksTheStunnedTurnAsSkipped_OnlyTheFirstOne()
        {
            var fight = NewTestFight();
            var stunned = fight.Players[1];
            Effects.Apply(stunned, "stun", 0);

            var bar = fight.PredictTurns(BarLength);

            Assert.AreSame(stunned, bar[0].Who);
            Assert.IsTrue(bar[0].Skipped);
            var later = bar.Skip(1).Where(t => t.Who == stunned).ToList();
            Assert.IsNotEmpty(later, "it comes round again inside the bar");
            Assert.IsTrue(later.All(t => !t.Skipped), "only one turn is lost");
            Assert.IsTrue(bar.Where(t => t.Who != stunned).All(t => !t.Skipped));
        }

        [Test]
        public void Test5_TheBarIsExactWithAStun_SkippedMarksIncluded()
        {
            var fight = NewTestFight();
            Effects.Apply(fight.Players[1], "stun", 0);
            Effects.Apply(fight.Enemies[0], "stun", 0);
            var predicted = fight.PredictTurns(BarLength).Select(t => (t.Who, t.Skipped)).ToList();

            // what really happens: every TurnStarted event, skipped when a TurnSkipped follows it at once
            var happened = new List<(Fighter, bool)>();
            while (happened.Count < BarLength)
            {
                var events = fight.AdvanceToChoice();
                for (var i = 0; i < events.Count; i++)
                    if (events[i].Kind == FightEventKind.TurnStarted)
                        happened.Add((events[i].Who, i + 1 < events.Count && events[i + 1].Kind == FightEventKind.TurnSkipped));
                if (fight.Acting != null) fight.Act(Basic(fight));
            }

            CollectionAssert.AreEqual(predicted, happened.Take(BarLength).ToList());
        }

        [Test]
        public void Test5_ThePredictionDoesNotUseUpTheRealStun()
        {
            var fight = NewTestFight();
            var stunned = fight.Players[1];
            Effects.Apply(stunned, "stun", 0);

            fight.PredictTurns(BarLength);

            Assert.IsTrue(Effects.IsStunned(stunned), "the prediction ran on a copy");
            Assert.AreEqual(0, stunned.TurnsStarted);
        }

        // ---- Copy ---------------------------------------------------------------------------------------

        [Test]
        public void Copy_CopiesTheEffects_AndAChangeToTheCopyNeverReachesTheFight()
        {
            var fight = NewTestFight();
            var hero = fight.Players[1];
            Effects.Apply(hero, "taunt", 2);
            Effects.Apply(hero, "barrier", 2, amount: 100);
            var endsAfter = Find(hero, "taunt").EndsAfterTurn;

            var copy = fight.Copy();
            var copyHero = copy.Players[1];
            CollectionAssert.AreEqual(Kinds(hero), Kinds(copyHero), "the copy starts with the same effects");
            Assert.AreNotSame(hero.Effects[0], copyHero.Effects[0], "but its own records");

            Effects.Apply(copyHero, "stun", 0);
            Effects.Apply(copyHero, "barrier", 2, amount: 900);
            Effects.Apply(copyHero, "taunt", 9);
            Effects.LoseAll(copyHero);

            CollectionAssert.AreEqual(new[] { "taunt", "barrier" }, Kinds(hero));
            Assert.AreEqual(100, Find(hero, "barrier").Amount);
            Assert.AreEqual(endsAfter, Find(hero, "taunt").EndsAfterTurn);
        }
    }
}
