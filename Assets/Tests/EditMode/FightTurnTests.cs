using System;
using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules;
using NUnit.Framework;
using static HeroHQ.Tests.TestFights;

namespace HeroHQ.Tests
{
    // P1-06 (03 Architecture 4.12): the fight's clock. Create, AdvanceToChoice, Act (end of turn and the win
    // check; the skill itself does nothing until P1-09), Copy and PredictTurns. 02 GDD 11 tests 3 and 4.
    //
    // The test heroes have a huge Health so nobody is knocked out once Basics really hit (P1-09): test 3 then
    // keeps proving the bar against real Basics, as 02 GDD 11 asks ("every hero only uses Basic").
    public class FightTurnTests
    {
        const int Full = 1000;       // meter_full in TestData.Tuning
        const int BarLength = 8;     // turn_bar_length in TestData.Tuning

        // ---- set-up helpers (NewFight and Basic are shared, see TestFights) --------------------------------

        /// <summary>Speeds with ties inside a side and across sides, so the tie rule is used along the way.</summary>
        static Fight MixedFight() => NewFight(
            new[] { ("p0", 100), ("p1", 120), ("p2", 90), ("p3", 100), ("p4", 75) },
            new[] { ("e0", 100), ("e1", 110), ("e2", 60), ("e3", 120), ("e4", 95) });

        /// <summary>The fighters of a predicted bar, without the skipped marks (those are tested in EffectsTests).</summary>
        static IReadOnlyList<Fighter> Who(IReadOnlyList<PredictedTurn> bar) => bar.Select(t => t.Who).ToList();

        /// <summary>One whole turn: run the clock to the next hero, who uses Basic. Returns who acted.</summary>
        static Fighter PlayOneTurn(Fight fight)
        {
            fight.AdvanceToChoice();
            var who = fight.Acting;
            fight.Act(Basic(fight));
            return who;
        }

        // ---- Create ------------------------------------------------------------------------------------

        [Test]
        public void Create_PutsEachTeamInSlotOrder_WithStatsFromTheData_AndEveryMeterAtZero()
        {
            var fight = MixedFight();

            CollectionAssert.AreEqual(new[] { "p0", "p1", "p2", "p3", "p4" }, fight.Players.Select(f => f.Hero.Id));
            CollectionAssert.AreEqual(new[] { "e0", "e1", "e2", "e3", "e4" }, fight.Enemies.Select(f => f.Hero.Id));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, fight.Players.Select(f => f.Slot));
            Assert.IsTrue(fight.Players.All(f => f.Side == Side.Player));
            Assert.IsTrue(fight.Enemies.All(f => f.Side == Side.Enemy));
            Assert.AreEqual(120, fight.Players[1].Speed);
            Assert.AreEqual(1000000, fight.Players[1].Health);
            Assert.IsTrue(fight.Players.Concat(fight.Enemies).All(f => f.Meter == 0 && !f.KnockedOut),
                "02 GDD 3: every meter starts at 0");
            Assert.AreEqual(Outcome.Going, fight.Outcome);
            Assert.IsNull(fight.Acting, "nobody acts until the clock runs");
        }

        // ---- AdvanceToChoice and Act -------------------------------------------------------------------

        [Test]
        public void AdvanceToChoice_RunsTheClockUntilAMeterIsFull_AndSaysWhoseTurnStarted()
        {
            var fight = MixedFight();

            var events = fight.AdvanceToChoice();

            // Speed 120: p1 and e3 are both full at tick 9 with 1080; your side wins the tie (step 3)
            Assert.AreSame(fight.Players[1], fight.Acting);
            Assert.AreEqual(1080, fight.Acting.Meter, "the meter is still full while the hero chooses");
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(FightEventKind.TurnStarted, events[0].Kind);
            Assert.AreSame(fight.Players[1], events[0].Who);
        }

        [Test]
        public void AdvanceToChoice_WhileAHeroIsChoosing_DoesNothing()
        {
            var fight = MixedFight();
            fight.AdvanceToChoice();
            var acting = fight.Acting;
            var meters = fight.Players.Concat(fight.Enemies).Select(f => f.Meter).ToList();

            CollectionAssert.IsEmpty(fight.AdvanceToChoice());
            Assert.AreSame(acting, fight.Acting);
            CollectionAssert.AreEqual(meters, fight.Players.Concat(fight.Enemies).Select(f => f.Meter));
        }

        [Test]
        public void Act_EndsTheTurn_TheMeterDropsByOneFullBarAndKeepsTheOverflow()
        {
            var fight = MixedFight();
            fight.AdvanceToChoice();
            var hero = fight.Acting;

            var events = fight.Act(Basic(fight));

            Assert.AreEqual(80, hero.Meter, "1080 - 1000: the overflow is kept (02 GDD 3)");
            Assert.IsNull(fight.Acting);
            CollectionAssert.IsEmpty(events, "skills do nothing yet (P1-09) and nobody won");

            // the next turn is the other Speed-120 hero, still full from the same tick
            fight.AdvanceToChoice();
            Assert.AreSame(fight.Enemies[3], fight.Acting);
        }

        [Test]
        public void Act_WithNobodyChoosing_IsAnError()
        {
            var fight = MixedFight();
            Assert.Throws<InvalidOperationException>(() => fight.Act(new Choice(Button.Basic, fight.Enemies[0])));
        }

        // ---- test 3: the bar's prediction is what happens -----------------------------------------------

        [Test]
        public void Test3_PredictedTurns_AreTheTurnsThatHappen_EveryHeroUsingBasic()
        {
            var fight = MixedFight();
            const int turns = 40;
            var predicted = new List<IReadOnlyList<Fighter>>();
            var happened = new List<Fighter>();

            for (var t = 0; t < turns; t++)
            {
                fight.AdvanceToChoice();
                predicted.Add(Who(fight.PredictTurns(BarLength)));     // asked while the hero chooses, as the screen does
                happened.Add(fight.Acting);
                fight.Act(Basic(fight));
            }

            Assert.IsTrue(fight.Players.Concat(fight.Enemies).All(f => !f.KnockedOut), "test 3 is with nobody knocked out");
            for (var t = 0; t + BarLength <= turns; t++)
                CollectionAssert.AreEqual(happened.Skip(t).Take(BarLength), predicted[t],
                    $"the bar shown at turn {t + 1} (the acting hero first) must be the next {BarLength} turns");
        }

        [Test]
        public void PredictTurns_BeforeTheFirstTurn_IsTheFirstTurns()
        {
            var fight = MixedFight();
            var bar = Who(fight.PredictTurns(BarLength));

            var happened = Enumerable.Range(0, BarLength).Select(_ => PlayOneTurn(fight));
            CollectionAssert.AreEqual(happened, bar);
        }

        [Test]
        public void PredictTurns_ChangesNothingInTheFight()
        {
            var fight = MixedFight();
            PlayOneTurn(fight);
            fight.AdvanceToChoice();
            var acting = fight.Acting;
            var meters = fight.Players.Concat(fight.Enemies).Select(f => f.Meter).ToList();

            fight.PredictTurns(BarLength);

            Assert.AreSame(acting, fight.Acting);
            CollectionAssert.AreEqual(meters, fight.Players.Concat(fight.Enemies).Select(f => f.Meter));
        }

        [Test]
        public void PredictTurns_LeavesKnockedOutHeroesOut()
        {
            var fight = MixedFight();
            var down = fight.Players[1];          // the fastest player hero
            down.KnockedOut = true;

            var bar = Who(fight.PredictTurns(20));

            Assert.AreEqual(20, bar.Count);
            CollectionAssert.DoesNotContain(bar, down, "02 GDD 2: a knocked-out hero leaves the turn bar");
        }

        // ---- test 4: 02 GDD 3's example in the bar ------------------------------------------------------

        [Test]
        public void Test4_TheBarShowsSkipjackTwiceBeforeHalvard()
        {
            // 02 GDD 3's example numbers: Skipjack 120, Halvard 60. Everyone else is slower (50), so the
            // example's ticks 9 and 17 come out exactly as in the GDD's table.
            var fight = NewFight(
                new[] { ("skipjack", 120), ("halvard", 60), ("p2", 50), ("p3", 50), ("p4", 50) },
                new[] { ("e0", 50), ("e1", 50), ("e2", 50), ("e3", 50), ("e4", 50) });
            var skipjack = fight.Players[0];
            var halvard = fight.Players[1];

            var bar = Who(fight.PredictTurns(BarLength));

            Assert.AreSame(skipjack, bar[0], "tick 9: Skipjack at 1080");
            Assert.AreSame(skipjack, bar[1], "tick 17: Skipjack at 1040 is fuller than Halvard at 1020");
            Assert.AreSame(halvard, bar[2]);
            Assert.AreEqual(BarLength, bar.Count);

            // and the fight itself plays it that way
            Assert.AreSame(skipjack, PlayOneTurn(fight));
            Assert.AreSame(skipjack, PlayOneTurn(fight));
            Assert.AreSame(halvard, PlayOneTurn(fight));
        }

        // ---- Copy and the random generator (03 Architecture 4.2) ----------------------------------------

        [Test]
        public void Copy_IsIndependent_AChangeToTheCopyNeverReachesTheFight()
        {
            var fight = MixedFight();
            PlayOneTurn(fight);
            fight.AdvanceToChoice();
            var acting = fight.Acting;
            var before = fight.Players.Concat(fight.Enemies).Select(f => (f.Meter, f.Health, f.KnockedOut)).ToList();

            var copy = fight.Copy();
            Assert.AreEqual(acting.Hero.Id, copy.Acting.Hero.Id, "the copy starts where the fight is");
            Assert.AreNotSame(acting, copy.Acting);
            Assert.AreSame(fight.Data, copy.Data, "the read-only data is shared");

            foreach (var f in copy.Players.Concat(copy.Enemies))
            {
                f.Meter += 500;
                f.Health -= 1;
                f.KnockedOut = true;
            }
            copy.Enemies[0].KnockedOut = false;     // so the copy can still play a turn
            copy.Act(new Choice(Button.Basic, copy.Enemies[0]));
            copy.AdvanceToChoice();

            Assert.AreSame(acting, fight.Acting);
            Assert.AreEqual(Outcome.Going, fight.Outcome);
            CollectionAssert.AreEqual(before, fight.Players.Concat(fight.Enemies).Select(f => (f.Meter, f.Health, f.KnockedOut)));
        }

        [Test]
        public void Roll_OnACopy_IsAnError_SoThePredictionCanNeverUseLuck()
        {
            var fight = MixedFight();
            var copy = fight.Copy();

            Assert.Throws<InvalidOperationException>(() => copy.Roll(50));
            Assert.DoesNotThrow(() => fight.Roll(50), "the real fight has its generator");
        }

        [Test]
        public void Roll_ZeroNeverHappens_HundredAlwaysDoes_AndTheSameSeedRollsTheSame()
        {
            var fight = MixedFight();
            for (var i = 0; i < 200; i++)
            {
                Assert.IsFalse(fight.Roll(0));
                Assert.IsTrue(fight.Roll(100));
            }

            // 03 Architecture 4.9: the seed fixes the sequence, so a fight replays the same way
            var a = MixedFight();
            var b = MixedFight();
            var rollsA = Enumerable.Range(0, 200).Select(_ => a.Roll(50)).ToList();
            var rollsB = Enumerable.Range(0, 200).Select(_ => b.Roll(50)).ToList();
            CollectionAssert.AreEqual(rollsA, rollsB);
            Assert.IsTrue(rollsA.Contains(true) && rollsA.Contains(false), "50 % gives both over 200 rolls");
        }

        // ---- the win check (02 GDD 2, step 6) -----------------------------------------------------------

        [Test]
        public void WinCheck_AllFiveEnemiesDown_IsAWin_AtTheEndOfTheTurn()
        {
            var fight = MixedFight();
            fight.AdvanceToChoice();
            foreach (var enemy in fight.Enemies) enemy.KnockedOut = true;
            Assert.AreEqual(Outcome.Going, fight.Outcome, "checked at the end of the turn, not before");

            var events = fight.Act(new Choice(Button.Basic, fight.Enemies[0]));

            Assert.AreEqual(Outcome.Won, fight.Outcome);
            Assert.AreEqual(FightEventKind.FightWon, events.Last().Kind);
        }

        [Test]
        public void WinCheck_AllFiveOfYoursDown_IsALoss()
        {
            var fight = MixedFight();
            fight.AdvanceToChoice();
            foreach (var hero in fight.Players) hero.KnockedOut = true;

            var events = fight.Act(new Choice(Button.Basic, fight.Enemies[0]));

            Assert.AreEqual(Outcome.Lost, fight.Outcome);
            Assert.AreEqual(FightEventKind.FightLost, events.Last().Kind);
        }

        [Test]
        public void WinCheck_FourOfFiveDown_TheFightGoesOn()
        {
            var fight = MixedFight();
            fight.AdvanceToChoice();
            foreach (var enemy in fight.Enemies.Skip(1)) enemy.KnockedOut = true;

            var events = fight.Act(Basic(fight));

            Assert.AreEqual(Outcome.Going, fight.Outcome);
            CollectionAssert.IsEmpty(events);
        }

        [Test]
        public void AfterTheFight_AdvanceDoesNothing_AndActIsAnError()
        {
            var fight = MixedFight();
            fight.AdvanceToChoice();
            foreach (var enemy in fight.Enemies) enemy.KnockedOut = true;
            fight.Act(new Choice(Button.Basic, fight.Enemies[0]));

            // 03 Architecture 4.3's loop calls AdvanceToChoice once more after the winning Act
            CollectionAssert.IsEmpty(fight.AdvanceToChoice());
            Assert.IsNull(fight.Acting);
            Assert.Throws<InvalidOperationException>(() => fight.Act(new Choice(Button.Basic, fight.Enemies[0])));
        }
    }
}
