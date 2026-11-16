using System;
using System.Linq;
using HeroHQ.Rules;
using NUnit.Framework;
using static HeroHQ.Tests.TestFights;

namespace HeroHQ.Tests
{
    // P1-08 (03 Architecture 4.12): who a skill may or must hit. 02 GDD 11 test 15 (targeting part) and
    // 02 GDD 10.6 questions 9 and 10. Slots are 0..4: front row first (top to bottom), then the back row.
    public class TargetingTests
    {
        // ---- set-up helpers -----------------------------------------------------------------------------

        /// <summary>Everyone slow and equal, so nobody acts unless a test asks. Players p0..p4, enemies e0..e4.</summary>
        static Fight NewTestFight(string playerFormation = "3-2", string enemyFormation = "3-2") => NewFight(
            new[] { ("p0", 100), ("p1", 100), ("p2", 100), ("p3", 100), ("p4", 100) },
            new[] { ("e0", 100), ("e1", 100), ("e2", 100), ("e3", 100), ("e4", 100) },
            playerFormation: playerFormation, enemyFormation: enemyFormation);

        /// <summary>The slot numbers of a list of fighters, as a plain array (so a test reads "slots 0, 1, 2").</summary>
        static int[] Slots(System.Collections.Generic.IEnumerable<Fighter> fighters) => fighters.Select(f => f.Slot).ToArray();

        static Fighter Knock(Fighter f)
        {
            f.KnockedOut = true;
            f.Health = 0;
            return f;
        }

        // ---- rows ---------------------------------------------------------------------------------------

        [TestCase("3-2", new[] { Row.Front, Row.Front, Row.Front, Row.Back, Row.Back })]
        [TestCase("2-3", new[] { Row.Front, Row.Front, Row.Back, Row.Back, Row.Back })]
        [TestCase("4-1", new[] { Row.Front, Row.Front, Row.Front, Row.Front, Row.Back })]
        [TestCase("1-4", new[] { Row.Front, Row.Back, Row.Back, Row.Back, Row.Back })]
        public void Create_PutsTheFirstSlotsInFront_AsTheFormationSays(string formation, Row[] expected)
        {
            var fight = NewTestFight(playerFormation: formation, enemyFormation: formation);

            Assert.AreEqual(expected, fight.Players.Select(f => f.Row).ToArray());
            Assert.AreEqual(expected, fight.Enemies.Select(f => f.Row).ToArray());
        }

        [Test]
        public void Create_GivesEveryFighterTheirMaxHealth_AndStartsFull()
        {
            var fight = NewTestFight();

            Assert.IsTrue(fight.Players.Concat(fight.Enemies).All(f => f.MaxHealth == 1000000 && f.Health == f.MaxHealth));
        }

        [Test]
        public void Copy_KeepsRowAndMaxHealth()
        {
            var fight = NewTestFight("1-4", "1-4");

            var copy = fight.Copy();

            Assert.AreEqual(Row.Back, copy.Enemies[3].Row);
            Assert.AreEqual(1000000, copy.Enemies[3].MaxHealth);
        }

        // ---- a chosen enemy: front row first (02 GDD 2, 7.2) -------------------------------------------

        [Test]
        public void Choosable_FrontRowFirst_WhileAFrontRowHeroStands()
        {
            var fight = NewTestFight();

            Assert.AreEqual(new[] { 0, 1, 2 }, Slots(Targeting.Choosable(fight, fight.Players[0], reachesBackRow: false)));
        }

        [Test]
        public void Choosable_WorksTheSameForTheEnemySide_AgainstTheirOpponents()
        {
            var fight = NewTestFight("3-2", "2-3");

            // an enemy acting aims at the players' 3-2: slots 0, 1, 2
            Assert.AreEqual(new[] { 0, 1, 2 }, Slots(Targeting.Choosable(fight, fight.Enemies[0], false)));
            // a player acting aims at the enemies' 2-3: slots 0, 1
            Assert.AreEqual(new[] { 0, 1 }, Slots(Targeting.Choosable(fight, fight.Players[0], false)));
        }

        [Test]
        public void Choosable_OneStandingFrontHero_IsTheOnlyChoice()
        {
            var fight = NewTestFight();
            Knock(fight.Enemies[0]);
            Knock(fight.Enemies[2]);

            Assert.AreEqual(new[] { 1 }, Slots(Targeting.Choosable(fight, fight.Players[0], false)));
        }

        [Test]
        public void Choosable_FrontRowDown_OpensTheBackRow()
        {
            var fight = NewTestFight();
            foreach (var f in fight.Enemies.Take(3)) Knock(f);

            Assert.AreEqual(new[] { 3, 4 }, Slots(Targeting.Choosable(fight, fight.Players[0], false)));
        }

        [Test]
        public void Choosable_ReachesTheBackRow_MeansEveryStandingEnemy()
        {
            var fight = NewTestFight();
            Knock(fight.Enemies[1]);

            Assert.AreEqual(new[] { 0, 2, 3, 4 }, Slots(Targeting.Choosable(fight, fight.Players[0], reachesBackRow: true)));
        }

        [Test]
        public void Choosable_LeavesOutKnockedOutHeroes()
        {
            var fight = NewTestFight();
            Knock(fight.Enemies[0]);

            Assert.AreEqual(new[] { 1, 2 }, Slots(Targeting.Choosable(fight, fight.Players[0], false)));
        }

        // ---- Taunt comes before the front row (02 GDD 7.2) ----------------------------------------------

        [Test]
        public void Choosable_ATauntingBackRowHero_MustBePicked()
        {
            var fight = NewTestFight();
            Effects.Apply(fight.Enemies[4], "taunt", 1);

            Assert.AreEqual(new[] { 4 }, Slots(Targeting.Choosable(fight, fight.Players[0], false)));
        }

        [Test]
        public void Choosable_Taunt_AlsoOverridesReachesTheBackRow()
        {
            var fight = NewTestFight();
            Effects.Apply(fight.Enemies[0], "taunt", 1);

            Assert.AreEqual(new[] { 0 }, Slots(Targeting.Choosable(fight, fight.Players[0], reachesBackRow: true)));
        }

        [Test]
        public void Choosable_TwoTaunters_AreBothChoices_InAnyRow()
        {
            var fight = NewTestFight();
            Effects.Apply(fight.Enemies[1], "taunt", 1);
            Effects.Apply(fight.Enemies[3], "taunt", 1);

            Assert.AreEqual(new[] { 1, 3 }, Slots(Targeting.Choosable(fight, fight.Players[0], false)));
        }

        [Test]
        public void Choosable_TauntOnTheActorsOwnSide_ChangesNothing()
        {
            var fight = NewTestFight();
            Effects.Apply(fight.Players[4], "taunt", 1);

            Assert.AreEqual(new[] { 0, 1, 2 }, Slots(Targeting.Choosable(fight, fight.Players[0], false)));
        }

        [Test]
        public void Choosable_AKnockedOutTaunter_NoLongerForcesAnything()
        {
            var fight = NewTestFight();
            Effects.Apply(fight.Enemies[4], "taunt", 1);
            Knock(fight.Enemies[4]);
            Effects.LoseAll(fight.Enemies[4]);

            Assert.AreEqual(new[] { 0, 1, 2 }, Slots(Targeting.Choosable(fight, fight.Players[0], false)));
        }

        // ---- neighbours: the same row, the row closes up (02 GDD 2) -------------------------------------

        [TestCase(0, new[] { 1 })]
        [TestCase(1, new[] { 0, 2 })]
        [TestCase(2, new[] { 1 })]        // slot 3 is next in the list but stands in the other row
        [TestCase(3, new[] { 4 })]
        [TestCase(4, new[] { 3 })]
        public void Neighbours_AreNextToThemInTheSameRow(int slot, int[] expected)
        {
            var fight = NewTestFight();

            Assert.AreEqual(expected, Slots(Targeting.Neighbours(fight, fight.Enemies[slot])));
        }

        [Test]
        public void Neighbours_SkipKnockedOutHeroes_TheRowClosesUp()
        {
            var fight = NewTestFight();
            Knock(fight.Enemies[1]);

            Assert.AreEqual(new[] { 2 }, Slots(Targeting.Neighbours(fight, fight.Enemies[0])));
            Assert.AreEqual(new[] { 0 }, Slots(Targeting.Neighbours(fight, fight.Enemies[2])));
        }

        [Test]
        public void Neighbours_AloneInARow_HasNone()
        {
            var fight = NewTestFight("1-4", "1-4");

            Assert.IsEmpty(Targeting.Neighbours(fight, fight.Enemies[0]));
        }

        [Test]
        public void Neighbours_NeverCrossTheRows_WhateverTheFormation()
        {
            var fight = NewTestFight("2-3", "2-3");

            Assert.AreEqual(new[] { 0 }, Slots(Targeting.Neighbours(fight, fight.Enemies[1])));     // front row of two
            Assert.AreEqual(new[] { 3 }, Slots(Targeting.Neighbours(fight, fight.Enemies[2])));     // back row starts at slot 2
            Assert.AreEqual(new[] { 2, 4 }, Slots(Targeting.Neighbours(fight, fight.Enemies[3])));
        }

        // ---- the eight targets of 02 GDD 7.2 ------------------------------------------------------------

        [Test]
        public void Targets_Enemy_IsTheChosenHeroAlone()
        {
            var fight = NewTestFight();
            var actor = fight.Players[0];

            var hit = Targeting.Targets(fight, actor, "enemy", fight.Enemies[1]);

            Assert.AreEqual(new[] { 1 }, Slots(hit));
        }

        [Test]
        public void Targets_EnemyAndNeighbours_IsTheChosenHeroAndThoseNextToThem_InSlotOrder()
        {
            var fight = NewTestFight();

            var hit = Targeting.Targets(fight, fight.Players[0], "enemy_and_neighbours", fight.Enemies[1]);

            Assert.AreEqual(new[] { 0, 1, 2 }, Slots(hit));
        }

        [Test]
        public void Targets_EnemyAndNeighbours_ChosenAtTheEdge_HasOneNeighbour()
        {
            var fight = NewTestFight();

            var hit = Targeting.Targets(fight, fight.Players[0], "enemy_and_neighbours", fight.Enemies[0]);

            Assert.AreEqual(new[] { 0, 1 }, Slots(hit));
        }

        [Test]
        public void Targets_AllEnemies_IgnoreRowsAndTaunt_AndSkipKnockedOutHeroes()
        {
            var fight = NewTestFight();
            Effects.Apply(fight.Enemies[0], "taunt", 1);
            Knock(fight.Enemies[2]);

            var hit = Targeting.Targets(fight, fight.Players[0], "all_enemies", null);

            Assert.AreEqual(new[] { 0, 1, 3, 4 }, Slots(hit));
            Assert.IsTrue(hit.All(f => f.Side == Side.Enemy));
        }

        [Test]
        public void Targets_AllEnemies_ForAnEnemyActing_IsThePlayersSide()
        {
            var fight = NewTestFight();

            var hit = Targeting.Targets(fight, fight.Enemies[0], "all_enemies", null);

            Assert.AreEqual(new[] { 0, 1, 2, 3, 4 }, Slots(hit));
            Assert.IsTrue(hit.All(f => f.Side == Side.Player));
        }

        [Test]
        public void Targets_Self_IsTheActor()
        {
            var fight = NewTestFight();
            var actor = fight.Players[3];

            Assert.AreSame(actor, Targeting.Targets(fight, actor, "self", null).Single());
        }

        // ---- the enemy who acts next: the turn bar, a skipped turn included (10.6 q. 10) ----------------

        /// <summary>The enemy in the back row (slot 4) is much faster than the rest, so it is first on the bar.</summary>
        static Fight FastBackRowEnemy() => NewFight(
            new[] { ("p0", 50), ("p1", 50), ("p2", 50), ("p3", 50), ("p4", 50) },
            new[] { ("e0", 100), ("e1", 100), ("e2", 100), ("e3", 100), ("e4", 300) });

        [Test]
        public void Targets_NextEnemy_IsTheFirstEnemyOnTheBar_BackRowOrNot()
        {
            var fight = FastBackRowEnemy();

            var hit = Targeting.Targets(fight, fight.Players[0], "next_enemy", null);

            Assert.AreSame(fight.Enemies[4], hit.Single());
            Assert.AreSame(fight.Enemies[4], fight.PredictTurns(1)[0].Who, "it is the bar's first portrait");
        }

        [Test]
        public void Targets_NextEnemy_IgnoresTaunt()
        {
            var fight = FastBackRowEnemy();
            Effects.Apply(fight.Enemies[0], "taunt", 1);

            var hit = Targeting.Targets(fight, fight.Players[0], "next_enemy", null);

            Assert.AreSame(fight.Enemies[4], hit.Single());
        }

        [Test]
        public void Targets_NextEnemy_CountsATurnThatWillBeSkipped()
        {
            var fight = FastBackRowEnemy();
            Effects.Apply(fight.Enemies[4], "stun", 0);
            Assert.IsTrue(fight.PredictTurns(1)[0].Skipped, "set-up: the bar marks the stunned turn as skipped");

            var hit = Targeting.Targets(fight, fight.Players[0], "next_enemy", null);

            Assert.AreSame(fight.Enemies[4], hit.Single(), "what the bar shows is what the skill hits");
        }

        [Test]
        public void Targets_NextEnemy_PlayersAreFirstOnTheBar_StillFindsTheFirstEnemy()
        {
            // Players are much faster: the bar starts with players, so the search goes past them.
            var fight = NewFight(
                new[] { ("p0", 300), ("p1", 280), ("p2", 260), ("p3", 240), ("p4", 220) },
                new[] { ("e0", 100), ("e1", 90), ("e2", 80), ("e3", 70), ("e4", 60) });

            var hit = Targeting.Targets(fight, fight.Players[0], "next_enemy", null);

            var firstEnemyOnTheBar = fight.PredictTurns(40).First(t => t.Who.Side == Side.Enemy).Who;
            Assert.AreSame(firstEnemyOnTheBar, hit.Single());
            Assert.AreEqual(0, hit.Single().Slot);
        }

        [Test]
        public void Targets_NextEnemy_ForAnEnemyActing_IsTheFirstPlayerOnTheBar()
        {
            var fight = NewFight(
                new[] { ("p0", 50), ("p1", 50), ("p2", 50), ("p3", 300), ("p4", 50) },
                new[] { ("e0", 100), ("e1", 100), ("e2", 100), ("e3", 100), ("e4", 100) });

            var hit = Targeting.Targets(fight, fight.Enemies[0], "next_enemy", null);

            Assert.AreSame(fight.Players[3], hit.Single());
        }

        [Test]
        public void Targets_NextEnemy_WhileAHeroIsChoosing_SkipsThemAndTheirSide()
        {
            // p0 is first and is choosing now; the first ENEMY on the bar is e2 (the faster of the two enemies).
            var fight = NewFight(
                new[] { ("p0", 300), ("p1", 50), ("p2", 50), ("p3", 50), ("p4", 50) },
                new[] { ("e0", 100), ("e1", 100), ("e2", 150), ("e3", 100), ("e4", 100) });
            fight.AdvanceToChoice();
            Assert.AreSame(fight.Players[0], fight.Acting, "set-up");

            var hit = Targeting.Targets(fight, fight.Acting, "next_enemy", null);

            Assert.AreSame(fight.Enemies[2], hit.Single());
        }

        [Test]
        public void Targets_NextEnemy_DoesNotChangeTheFight()
        {
            var fight = FastBackRowEnemy();
            var metersBefore = fight.Players.Concat(fight.Enemies).Select(f => f.Meter).ToArray();

            Targeting.Targets(fight, fight.Players[0], "next_enemy", null);

            Assert.AreEqual(metersBefore, fight.Players.Concat(fight.Enemies).Select(f => f.Meter).ToArray());
        }

        // ---- allies: "ally" includes the hero in a skill, the other four in a trigger (10.6 q. 9) -------

        [Test]
        public void Allies_InASkill_IncludeTheHero_StandingOnly()
        {
            var fight = NewTestFight();
            Knock(fight.Players[1]);

            Assert.AreEqual(new[] { 0, 2, 3, 4 }, Slots(Targeting.Allies(fight, fight.Players[0], includeSelf: true)));
        }

        [Test]
        public void Allies_InATrigger_AreTheOtherFour()
        {
            var fight = NewTestFight();

            Assert.AreEqual(new[] { 0, 1, 3, 4 }, Slots(Targeting.Allies(fight, fight.Players[2], includeSelf: false)));
        }

        [Test]
        public void Targets_Ally_IsTheChosenAlly_AndMayBeTheHeroThemselves()
        {
            var fight = NewTestFight();
            var actor = fight.Players[0];

            Assert.AreSame(actor, Targeting.Targets(fight, actor, "ally", actor).Single());
            Assert.AreSame(fight.Players[3], Targeting.Targets(fight, actor, "ally", fight.Players[3]).Single());
        }

        [Test]
        public void Targets_AllAllies_InASkill_IncludeTheHero_InSlotOrder()
        {
            var fight = NewTestFight();

            var hit = Targeting.Targets(fight, fight.Players[2], "all_allies", null);

            Assert.AreEqual(new[] { 0, 1, 2, 3, 4 }, Slots(hit));
        }

        [Test]
        public void Targets_AllAllies_InATrigger_AreTheOtherFour_AndNotTheKnockedOut()
        {
            var fight = NewTestFight();
            Knock(fight.Players[4]);

            var hit = Targeting.Targets(fight, fight.Players[2], "all_allies", null, inTrigger: true);

            Assert.AreEqual(new[] { 0, 1, 3 }, Slots(hit));
        }

        [Test]
        public void Targets_AllAllies_ForAnEnemyActing_IsTheEnemySide()
        {
            var fight = NewTestFight();

            var hit = Targeting.Targets(fight, fight.Enemies[0], "all_allies", null);

            Assert.IsTrue(hit.All(f => f.Side == Side.Enemy));
            Assert.AreEqual(5, hit.Count);
        }

        // ---- the most hurt ally: lowest Health %, ties to the lowest slot (10.6 q. 9) -------------------

        static void SetHealth(Fighter f, int health, int max)
        {
            f.MaxHealth = max;
            f.Health = health;
        }

        [Test]
        public void MostHurtAlly_IsTheLowestHealthPercent_NotTheLowestHealth()
        {
            var fight = NewTestFight();
            SetHealth(fight.Players[0], 400, 400);        // 100 %
            SetHealth(fight.Players[1], 100, 200);        // 50 %: the most hurt, though 100 Health is not the lowest number
            SetHealth(fight.Players[2], 60, 100);         // 60 %
            SetHealth(fight.Players[3], 900, 1000);       // 90 %
            SetHealth(fight.Players[4], 70, 100);         // 70 %

            var hit = Targeting.Targets(fight, fight.Players[0], "most_hurt_ally", null);

            Assert.AreEqual(new[] { 1 }, Slots(hit));
        }

        [Test]
        public void MostHurtAlly_Tie_GoesToTheLowestSlot_EvenWithDifferentMaxHealth()
        {
            var fight = NewTestFight();
            SetHealth(fight.Players[0], 1000, 1000);
            SetHealth(fight.Players[1], 1000, 1000);
            SetHealth(fight.Players[2], 500, 1000);       // 50 %
            SetHealth(fight.Players[3], 250, 500);        // 50 %
            SetHealth(fight.Players[4], 100, 200);        // 50 %

            var hit = Targeting.Targets(fight, fight.Players[0], "most_hurt_ally", null);

            Assert.AreEqual(new[] { 2 }, Slots(hit));
        }

        [Test]
        public void MostHurtAlly_InASkill_CanBeTheHeroThemselves()
        {
            var fight = NewTestFight();
            SetHealth(fight.Players[3], 100, 1000);

            var hit = Targeting.Targets(fight, fight.Players[3], "most_hurt_ally", null);

            Assert.AreEqual(new[] { 3 }, Slots(hit));
        }

        [Test]
        public void MostHurtAlly_InATrigger_IsAnotherHero_EvenWhenTheHeroIsTheMostHurt()
        {
            var fight = NewTestFight();
            SetHealth(fight.Players[3], 100, 1000);
            SetHealth(fight.Players[1], 600, 1000);

            var hit = Targeting.Targets(fight, fight.Players[3], "most_hurt_ally", null, inTrigger: true);

            Assert.AreEqual(new[] { 1 }, Slots(hit));
        }

        [Test]
        public void MostHurtAlly_AtFightStart_InATrigger_IsTheOtherAllyInTheLowestSlot()
        {
            // Ibis's Ward at fight start (02 GDD 10.4, 10.6 q. 5): everyone is at full Health, so the tie rule decides.
            var fight = NewTestFight();

            Assert.AreEqual(new[] { 1 }, Slots(Targeting.Targets(fight, fight.Players[0], "most_hurt_ally", null, inTrigger: true)));
            Assert.AreEqual(new[] { 0 }, Slots(Targeting.Targets(fight, fight.Players[3], "most_hurt_ally", null, inTrigger: true)));
        }

        [Test]
        public void MostHurtAlly_IgnoresKnockedOutHeroes()
        {
            var fight = NewTestFight();
            SetHealth(fight.Players[1], 0, 1000);
            Knock(fight.Players[1]);
            SetHealth(fight.Players[2], 900, 1000);

            var hit = Targeting.Targets(fight, fight.Players[0], "most_hurt_ally", null);

            Assert.AreEqual(new[] { 2 }, Slots(hit));
        }

        // ---- wrong use ----------------------------------------------------------------------------------

        [Test]
        public void Targets_ChosenTargetMissing_Throws()
        {
            var fight = NewTestFight();

            Assert.Throws<ArgumentException>(() => Targeting.Targets(fight, fight.Players[0], "enemy", null));
            Assert.Throws<ArgumentException>(() => Targeting.Targets(fight, fight.Players[0], "ally", null));
            Assert.Throws<ArgumentException>(() => Targeting.Targets(fight, fight.Players[0], "enemy_and_neighbours", null));
        }

        [Test]
        public void Targets_UnknownTarget_Throws()
        {
            var fight = NewTestFight();

            Assert.Throws<ArgumentException>(() => Targeting.Targets(fight, fight.Players[0], "everyone", null));
        }
    }
}
