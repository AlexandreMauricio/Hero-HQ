using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroHQ.Rules;
using HeroHQ.Rules.Data;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using static HeroHQ.Tests.TestFights;

namespace HeroHQ.Tests
{
    // P2a-06 (03 Architecture 7.7, reading R19; 02 GDD 17.5 and 17.10 item 1): SkillResolver.WillTouch says which
    // heroes a skill will touch for a chosen hero, so the aiming outlines come from the rules and can't disagree with
    // them. It asks Targeting.Targets for each piece, so these tests cover joining, order and repeats, plus one case per
    // target kind. Players are p0..p4 and enemies e0..e4, both standing 3-2 (slots 0-2 front, 3-4 back).
    public class WillTouchTests
    {
        // ---- set-up helpers -----------------------------------------------------------------------------------

        /// <summary>A fight where p0's Special is made of the given pieces (JSON), so a test names the skill it needs.</summary>
        static Fight WithSpecial(string pieces, (string, int)[] enemies = null) => NewFight(
            new[] { ("p0", 100), ("p1", 100), ("p2", 100), ("p3", 100), ("p4", 100) },
            enemies ?? new[] { ("e0", 100), ("e1", 100), ("e2", 100), ("e3", 100), ("e4", 100) },
            edit: (id, hero) => { if (id == "p0") hero["special"]["pieces"] = JArray.Parse(pieces); });

        static IReadOnlyList<Fighter> Touch(Fight fight, Fighter chosen = null) =>
            SkillResolver.WillTouch(fight, fight.Players[0], Button.Special, chosen);

        static string Ids(IEnumerable<Fighter> heroes) => string.Join(",", heroes.Select(f => f.Hero.Id));

        static string Effect(string target) => $"{{ 'do': 'effect', 'target': '{target}', 'effect': 'taunt', 'turns': 1 }}".Replace('\'', '"');
        static string Damage(string target) => $"{{ 'do': 'damage', 'target': '{target}', 'power': 100 }}".Replace('\'', '"');
        static string Pieces(params string[] pieces) => "[" + string.Join(",", pieces) + "]";

        static Fighter Knock(Fighter f)
        {
            f.KnockedOut = true;
            f.Health = 0;
            return f;
        }

        // ---- one case per target kind (02 GDD 7.2) ------------------------------------------------------------

        [Test]
        public void Enemy_IsTheChosenHeroOnly()
        {
            var fight = WithSpecial(Pieces(Damage("enemy")));
            Assert.AreEqual("e1", Ids(Touch(fight, fight.Enemies[1])));
        }

        [TestCase(1, "e0,e1,e2")]    // the middle of the front row: the hero above and the one below
        [TestCase(0, "e0,e1")]       // the top of the front row: only the one below
        [TestCase(2, "e1,e2")]       // the bottom of the front row: only the one above
        [TestCase(3, "e3,e4")]       // a back-row hero: their row only, never across rows
        public void EnemyAndNeighbours_IsTheChosenHeroAndTheOnesNextToThemInTheirRow(int chosen, string expected)
        {
            var fight = WithSpecial(Pieces(Damage("enemy_and_neighbours")));
            Assert.AreEqual(expected, Ids(Touch(fight, fight.Enemies[chosen])));
        }

        [TestCase(0)]
        [TestCase(2)]
        public void EnemyAndNeighbours_ReachAcrossAGap(int chosen)
        {
            // e1 has fallen and left a gap (02 GDD 2): e0 and e2 are neighbours now, as Targeting.Neighbours says.
            var fight = WithSpecial(Pieces(Damage("enemy_and_neighbours")));
            Knock(fight.Enemies[1]);
            Assert.AreEqual("e0,e2", Ids(Touch(fight, fight.Enemies[chosen])));
        }

        [Test]
        public void AllEnemies_IsEveryStandingEnemyInSlotOrder()
        {
            var fight = WithSpecial(Pieces(Damage("all_enemies")));
            Assert.AreEqual("e0,e1,e2,e3,e4", Ids(Touch(fight)));

            Knock(fight.Enemies[2]);
            Assert.AreEqual("e0,e1,e3,e4", Ids(Touch(fight)), "a fallen hero is not touched");
        }

        [Test]
        public void Self_IsTheActing_Hero()
        {
            var fight = WithSpecial(Pieces(Effect("self")));
            Assert.AreEqual("p0", Ids(Touch(fight)));
        }

        [TestCase(2, "p2")]
        [TestCase(0, "p0")]          // the actor may choose themselves (02 GDD 10.6 q. 9)
        public void Ally_IsTheChosenHeroOnly(int chosen, string expected)
        {
            var fight = WithSpecial(Pieces(Effect("ally")));
            Assert.AreEqual(expected, Ids(Touch(fight, fight.Players[chosen])));
        }

        [Test]
        public void AllAllies_IsTheWholeSideIncludingTheActor()
        {
            var fight = WithSpecial(Pieces(Effect("all_allies")));
            Assert.AreEqual("p0,p1,p2,p3,p4", Ids(Touch(fight)));
        }

        [Test]
        public void MostHurtAlly_IsTheLowestHealthPercent()
        {
            var fight = WithSpecial(Pieces(Effect("most_hurt_ally")));
            fight.Players[3].Health = 10;
            Assert.AreEqual("p3", Ids(Touch(fight)));
        }

        [Test]
        public void NextEnemy_IsTheFirstEnemyOnTheTurnBar()
        {
            var fight = WithSpecial(Pieces(Damage("next_enemy")),
                                    enemies: new[] { ("e0", 100), ("e1", 100), ("e2", 100), ("e3", 100), ("e4", 300) });
            Assert.AreEqual("e4", Ids(Touch(fight)));
            Assert.AreSame(fight.PredictTurns(1)[0].Who, Touch(fight).Single(), "the outline is the bar's first portrait");
        }

        // ---- joining the pieces -------------------------------------------------------------------------------

        [Test]
        public void SeveralPieces_AreJoined_ThePlayersSideFirstThenTheEnemyInSlotOrder()
        {
            // Overheat's shape: the chosen enemy and neighbours, then a buff on self. Written enemy-first, listed player-first.
            var fight = WithSpecial(Pieces(Damage("enemy_and_neighbours"), Effect("self")));
            Assert.AreEqual("p0,e0,e1,e2", Ids(Touch(fight, fight.Enemies[1])));
        }

        [Test]
        public void AHeroTwoPiecesAimAt_IsListedOnce()
        {
            var fight = WithSpecial(Pieces(Damage("enemy"), Effect("enemy"), Effect("self"), Effect("all_allies")));
            Assert.AreEqual("p0,p1,p2,p3,p4,e1", Ids(Touch(fight, fight.Enemies[1])));
        }

        [Test]
        public void ForAnEnemyActing_TheOpponentsAreThePlayersAndTheListIsStillPlayersFirst()
        {
            // the test heroes' own skills: Special = chosen hero and neighbours, Ultimate = all opponents
            var fight = WithSpecial(Pieces(Damage("enemy")));
            var enemy = fight.Enemies[0];

            Assert.AreEqual("p0,p1,p2", Ids(SkillResolver.WillTouch(fight, enemy, Button.Special, fight.Players[1])));
            Assert.AreEqual("p0,p1,p2,p3,p4", Ids(SkillResolver.WillTouch(fight, enemy, Button.Ultimate, null)));
        }

        [Test]
        public void APiecesCondition_IsNotLookedAt_ItIsWhoTheSkillIsAimedAt()
        {
            // reading R19: e1 has no Defense Down, so the piece would skip them, but the outline still shows them
            var fight = WithSpecial(Pieces(
                "{ \"do\": \"damage\", \"target\": \"enemy\", \"power\": 100, \"if\": { \"test\": \"target_has_effect\", \"effect\": \"defense_down\" } }"));
            Assert.AreEqual("e1", Ids(Touch(fight, fight.Enemies[1])));
        }

        // ---- edges --------------------------------------------------------------------------------------------

        [Test]
        public void ASkillThatNeedsAChosenHero_AskedWithNobodyChosen_IsRefused()
        {
            var fight = WithSpecial(Pieces(Damage("enemy")));
            Assert.Throws<ArgumentException>(() => Touch(fight, null));
        }

        [Test]
        public void AChosenHeroWhoIsKnockedOut_IsNotTouched()
        {
            var fight = WithSpecial(Pieces(Damage("enemy")));
            Knock(fight.Enemies[1]);
            Assert.IsEmpty(Touch(fight, fight.Enemies[1]));
        }

        [Test]
        public void ItChangesNothing_NoRollNoHealthNoMeter()
        {
            var fight = WithSpecial(Pieces(Damage("enemy_and_neighbours"), Effect("self")));
            var all = fight.Players.Concat(fight.Enemies).ToList();
            string Snapshot() => string.Join("|", all.Select(f => $"{f.Health}/{f.Meter}/{f.Charge}/{f.Effects.Count()}/{f.Cooldown}"));
            var before = Snapshot();
            var untouched = WithSpecial(Pieces(Damage("enemy_and_neighbours"), Effect("self")));   // same seed, never asked

            Touch(fight, fight.Enemies[1]);

            Assert.AreEqual(before, Snapshot());
            // the generator was not used: the next rolls are the ones the fight that never asked gets
            for (var i = 0; i < 5; i++) Assert.AreEqual(untouched.Roll(50), fight.Roll(50), $"roll {i}");
        }

        // ---- the real heroes ----------------------------------------------------------------------------------

        [Test]
        public void EveryRealSkillOfEveryRealHero_HasAnOutlineAtTheStartOfTheFight()
        {
            string Read(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "Data", file));
            var data = GameData.Load(Read("heroes.json"), Read("classes.json"), Read("traits.json"), Read("tuning.json"), Read("teams.json"), Read("formations.json"));
            var fight = Fight.Create(data, data.Teams["greek_strike"], data.Teams["enemy_crew"], seed: 1);

            foreach (var actor in fight.Players.Concat(fight.Enemies))
                foreach (var button in new[] { Button.Basic, Button.Special, Button.Ultimate })
                {
                    var option = SkillResolver.Option(fight, actor, button);
                    var chosen = option.NeedsChoice ? option.Choosable[0] : null;      // the first hero a tap may pick
                    var touched = SkillResolver.WillTouch(fight, actor, button, chosen);
                    var label = $"{actor.Hero.Id}'s {button}";

                    Assert.IsNotEmpty(touched, label);
                    Assert.IsTrue(touched.All(f => !f.KnockedOut), label);
                    Assert.AreEqual(touched.Count, touched.Distinct().Count(), $"{label} lists a hero twice");
                    CollectionAssert.AreEqual(touched.OrderBy(f => f.Side == Side.Player ? 0 : 1).ThenBy(f => f.Slot).ToList(), touched.ToList(), $"{label} is not in order");
                    if (chosen != null) CollectionAssert.Contains(touched, chosen, label);
                }
        }

        [Test]
        public void RealKalsUltimate_OutlinesAllFiveOfTheCrew()
        {
            string Read(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "Data", file));
            var data = GameData.Load(Read("heroes.json"), Read("classes.json"), Read("traits.json"), Read("tuning.json"), Read("teams.json"), Read("formations.json"));
            var fight = Fight.Create(data, data.Teams["greek_strike"], data.Teams["enemy_crew"], seed: 1);
            var kal = fight.Players.Single(f => f.Hero.Id == "kal");

            Assert.AreEqual(5, SkillResolver.WillTouch(fight, kal, Button.Ultimate, null).Count);
        }
    }
}
