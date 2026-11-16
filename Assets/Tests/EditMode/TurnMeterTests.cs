using System.Collections.Generic;
using HeroHQ.Rules;
using NUnit.Framework;

namespace HeroHQ.Tests
{
    // P1-05 (03 Architecture 4.12): the turn meter of 02 GDD 3 — ticks, carry-over, the four-step tie rule.
    // 02 GDD 11 tests 1 and 2. The tests use bare fighters (side, slot, Speed); the fight itself is P1-06.
    public class TurnMeterTests
    {
        const int Full = 1000;   // meter_full in the tests

        static Fighter F(Side side, int slot, int speed, int meter = 0) => new Fighter(side, slot, speed) { Meter = meter };

        // ---- test 1: Speed ratio ------------------------------------------------------------------------

        [Test]
        public void SpeedRatio_FastHeroActsExactlyTwiceAsOften_OverThirtyTurns()
        {
            var fast = F(Side.Player, 0, 120);
            var slow = F(Side.Enemy, 0, 60);
            var all = new List<Fighter> { fast, slow };
            int fastTurns = 0, slowTurns = 0;

            // the loop of 02 GDD 3: while anyone is full, the first one acts; then one tick
            while (slowTurns < 30)
            {
                Fighter next;
                while ((next = TurnMeter.Next(all, Full)) != null)
                {
                    if (next == fast) fastTurns++; else slowTurns++;
                    TurnMeter.EndTurn(next, Full);
                    if (slowTurns == 30) break;
                }
                TurnMeter.Tick(all);
            }

            Assert.AreEqual(30, slowTurns);
            Assert.AreEqual(60, fastTurns, "carry-over keeps the ratio exact: 120 vs 60 is exactly twice");
        }

        [Test]
        public void Tick_AddsSpeedToEveryLivingMeter_AndSkipsKnockedOutHeroes()
        {
            var a = F(Side.Player, 0, 100);
            var b = F(Side.Enemy, 0, 70, meter: 30);
            var down = F(Side.Enemy, 1, 90);
            down.KnockedOut = true;

            TurnMeter.Tick(new[] { a, b, down });

            Assert.AreEqual(100, a.Meter);
            Assert.AreEqual(100, b.Meter);
            Assert.AreEqual(0, down.Meter, "a knocked-out hero is out of the order");
        }

        [Test]
        public void EndTurn_DropsTheMeterByOneFullBar_KeepingTheOverflow()
        {
            var hero = F(Side.Player, 0, 120, meter: 1080);
            TurnMeter.EndTurn(hero, Full);
            Assert.AreEqual(80, hero.Meter, "02 GDD 3: Skipjack acts at 1080 and keeps 80");
        }

        [Test]
        public void Next_IsNull_WhenNobodyIsFull_AndIgnoresKnockedOutHeroes()
        {
            var notFull = F(Side.Player, 0, 100, meter: 999);
            var down = F(Side.Enemy, 0, 100, meter: 1500);
            down.KnockedOut = true;

            Assert.IsNull(TurnMeter.Next(new[] { notFull, down }, Full));
        }

        // ---- test 2: the tie rule, each step decides one case -------------------------------------------

        [Test]
        public void Tie_Step1_TheFullerMeterGoesFirst_EvenIfTheOtherIsFaster()
        {
            var fuller = F(Side.Enemy, 4, 80, meter: 1100);      // later slot, enemy side, slower: step 1 alone decides
            var faster = F(Side.Player, 0, 120, meter: 1050);
            Assert.AreSame(fuller, TurnMeter.Next(new[] { faster, fuller }, Full));
        }

        [Test]
        public void Tie_Step2_TheHigherSpeedGoesFirst_WhenMetersAreEqual()
        {
            var faster = F(Side.Enemy, 4, 100, meter: 1000);     // enemy side and later slot: step 2 alone decides
            var slower = F(Side.Player, 0, 90, meter: 1000);
            Assert.AreSame(faster, TurnMeter.Next(new[] { slower, faster }, Full));
        }

        [Test]
        public void Tie_Step3_YourSideGoesBeforeTheEnemys_WhenMetersAndSpeedAreEqual()
        {
            var player = F(Side.Player, 4, 100, meter: 1000);    // a LATER slot, so only step 3 can decide
            var enemy = F(Side.Enemy, 0, 100, meter: 1000);
            Assert.AreSame(player, TurnMeter.Next(new[] { enemy, player }, Full));
        }

        [Test]
        public void Tie_Step4_TheLowerSlotGoesFirst_WhenEverythingElseIsEqual()
        {
            var slot1 = F(Side.Player, 1, 100, meter: 1000);
            var slot3 = F(Side.Player, 3, 100, meter: 1000);
            Assert.AreSame(slot1, TurnMeter.Next(new[] { slot3, slot1 }, Full));
        }

        [Test]
        public void SeveralFullHeroes_ActOneAfterAnother_InTieOrder()
        {
            var a = F(Side.Player, 2, 100, meter: 1000);
            var b = F(Side.Player, 0, 100, meter: 1000);
            var c = F(Side.Enemy, 0, 100, meter: 1000);
            var d = F(Side.Enemy, 1, 120, meter: 1000);
            var all = new List<Fighter> { a, b, c, d };

            var order = new List<Fighter>();
            Fighter next;
            while ((next = TurnMeter.Next(all, Full)) != null)
            {
                order.Add(next);
                TurnMeter.EndTurn(next, Full);
            }

            // d (faster), then the player's heroes by slot (b, a), then the enemy c
            CollectionAssert.AreEqual(new[] { d, b, a, c }, order);
        }

        // ---- 02 GDD 3's worked example ------------------------------------------------------------------

        [Test]
        public void Example_Skipjack120_ActsTwiceBeforeHalvard60_AtTick17()
        {
            var skipjack = F(Side.Player, 0, 120);
            var halvard = F(Side.Player, 1, 60);
            var all = new List<Fighter> { skipjack, halvard };
            var actions = new List<(int tick, Fighter who)>();

            for (var tick = 1; tick <= 17; tick++)
            {
                TurnMeter.Tick(all);
                Fighter next;
                while ((next = TurnMeter.Next(all, Full)) != null)
                {
                    actions.Add((tick, next));
                    TurnMeter.EndTurn(next, Full);
                }
            }

            // tick 9: Skipjack at 1080 acts, keeps 80. Tick 17: Skipjack 1040 and Halvard 1020, the fuller goes first.
            Assert.AreEqual((9, skipjack), actions[0]);
            Assert.AreEqual((17, skipjack), actions[1]);
            Assert.AreEqual((17, halvard), actions[2]);
            Assert.AreEqual(3, actions.Count);
        }
    }
}
