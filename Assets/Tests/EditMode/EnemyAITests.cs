using System;
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
    // P1-15 (03 Architecture 4.12): EnemyAI, 02 GDD 9. 02 GDD 11 test 14 (a charged, useful ultimate is always used;
    // lowest-Health target), the three "would it do something" cases of 02 GDD 9, the target table with reading R1,
    // either side, never rolls.
    //
    // Set-up: p0 (Speed 200) acts first, everyone else (Speed 50) after it; both teams stand 3–2, so slots 0–2 are the
    // front row and 3–4 the back row. Test heroes: Health 1,000,000, Accuracy 0, no Dodge. The AI is asked for p0's
    // choice (it works for whichever side is acting) unless a test says otherwise.
    public class EnemyAITests
    {
        static Fight Duel(Action<string, JObject> edit = null, int seed = 1) => NewFight(
            new[] { ("p0", 200), ("p1", 50), ("p2", 50), ("p3", 50), ("p4", 50) },
            new[] { ("e0", 50), ("e1", 50), ("e2", 50), ("e3", 50), ("e4", 50) }, seed, edit: edit);

        static void Pieces(JObject hero, string button, params string[] pieces) =>
            hero[button]["pieces"] = JArray.Parse("[" + string.Join(",", pieces) + "]");

        const string DamageOne = "{ do: 'damage', target: 'enemy', power: 100 }";

        /// <summary>The fight at p0's turn.</summary>
        static Fight AtP0(Action<string, JObject> edit = null, int seed = 1)
        {
            var fight = Duel(edit, seed);
            fight.AdvanceToChoice();
            Assert.AreEqual(fight.Players[0], fight.Acting, "set-up: p0 acts first");
            return fight;
        }

        // ---- ultimate, special, basic (02 GDD 9 rules 1–3) ------------------------------------------------------

        [Test]
        public void ChargedUsefulUltimate_IsAlwaysUsed_EvenWithTheSpecialReady()
        {
            var fight = AtP0();
            fight.Players[0].Charge = 100;
            Assert.AreEqual(0, fight.Players[0].Cooldown, "the Special is ready too");

            Assert.AreEqual(Button.Ultimate, EnemyAI.Choose(fight).Button);
        }

        [Test]
        public void UltimateNotCharged_SpecialReady_UsesTheSpecial()
        {
            var fight = AtP0();
            fight.Players[0].Charge = 99.5;
            Assert.AreEqual(Button.Special, EnemyAI.Choose(fight).Button);
        }

        [Test]
        public void SpecialOnCooldown_UsesTheBasic()
        {
            var fight = AtP0();
            fight.Players[0].Cooldown = 1;
            Assert.AreEqual(Button.Basic, EnemyAI.Choose(fight).Button);
        }

        [Test]
        public void ChargedButUselessUltimate_FallsToTheSpecial_AndWhenThatIsUselessToo_ToTheBasic()
        {
            // a heal-only Ultimate and Special while everyone stands at full Health: neither would do anything
            var fight = AtP0((id, h) =>
            {
                if (id != "p0") return;
                Pieces(h, "ultimate", "{ do: 'heal', target: 'all_allies', percent: 30 }");
                Pieces(h, "special", "{ do: 'heal', target: 'ally', percent: 30 }");
            });
            fight.Players[0].Charge = 100;
            Assert.AreEqual(Button.Basic, EnemyAI.Choose(fight).Button, "nothing to heal: both skipped");

            fight.Players[3].Health = 1;                  // now someone is hurt: the charged Ultimate would do something
            Assert.AreEqual(Button.Ultimate, EnemyAI.Choose(fight).Button);

            fight.Players[0].Charge = 0;                  // not charged: the Special would heal too
            Assert.AreEqual(Button.Special, EnemyAI.Choose(fight).Button);
        }

        // ---- "would it do something": the three cases of 02 GDD 9 -----------------------------------------------

        [Test]
        public void Heal_IsNotUseful_WhenEveryAllyIsAtFullHealth()
        {
            var fight = AtP0((id, h) => { if (id == "p0") Pieces(h, "special", "{ do: 'heal', target: 'most_hurt_ally', percent: 30 }"); });
            Assert.AreEqual(Button.Basic, EnemyAI.Choose(fight).Button);
            fight.Players[4].Health -= 1;                 // one point down is enough
            Assert.AreEqual(Button.Special, EnemyAI.Choose(fight).Button);
        }

        [Test]
        public void Clear_IsNotUseful_WhenNobodyOnTheSideHasABadEffect()
        {
            var fight = AtP0((id, h) => { if (id == "p0") Pieces(h, "special", "{ do: 'clear', target: 'all_allies' }"); });
            Effects.Apply(fight.Players[2], "offense_up", 2);   // a buff is not a bad effect
            Assert.AreEqual(Button.Basic, EnemyAI.Choose(fight).Button);
            Effects.Apply(fight.Players[3], "defense_down", 2);
            Assert.AreEqual(Button.Special, EnemyAI.Choose(fight).Button);
        }

        [Test]
        public void Stun_IsNotUseful_WhenEveryTargetIsAlreadyStunned()
        {
            // a Stun-only Special at one chosen enemy: the front row (e0–e2) are the only heroes it may pick
            var fight = AtP0((id, h) => { if (id == "p0") Pieces(h, "special", "{ do: 'effect', target: 'enemy', effect: 'stun' }"); });
            foreach (var e in fight.Enemies.Take(3)) Effects.Apply(e, "stun", 0);
            Assert.AreEqual(Button.Basic, EnemyAI.Choose(fight).Button, "all the heroes it may pick are stunned");
            Effects.UseUp(fight.Enemies[1], "stun");        // one front-row hero can be stunned again
            Assert.AreEqual(Button.Special, EnemyAI.Choose(fight).Button);
        }

        [Test]
        public void SkillWithADamagePiece_IsAlwaysUseful_EvenWhenItsStunCannotLand()
        {
            var fight = AtP0((id, h) =>
            {
                if (id == "p0") Pieces(h, "special", DamageOne, "{ do: 'effect', target: 'enemy', effect: 'stun' }");
            });
            foreach (var e in fight.Enemies) Effects.Apply(e, "stun", 0);
            Assert.AreEqual(Button.Special, EnemyAI.Choose(fight).Button);
        }

        // ---- the target table (02 GDD 9) ------------------------------------------------------------------------

        [Test]
        public void Damage_PicksTheLowestCurrentHealth_NotTheLowestPercent()
        {
            var fight = AtP0();
            fight.Players[0].Cooldown = 1;
            fight.Enemies[0].Health = 400;
            fight.Enemies[1].Health = 300;                // the lowest number of Health points
            fight.Enemies[1].MaxHealth = 301;             // and nearly full, so the lowest Health % is somebody else
            fight.Enemies[2].Health = 500;
            Assert.AreEqual(fight.Enemies[1], EnemyAI.Choose(fight).Target);
        }

        [Test]
        public void Damage_TiesGoToTheLowestSlot()
        {
            var fight = AtP0();
            fight.Players[0].Cooldown = 1;
            foreach (var e in fight.Enemies) e.Health = 1000;
            Assert.AreEqual(fight.Enemies[0], EnemyAI.Choose(fight).Target);
            fight.Enemies[0].Health = 2000;
            Assert.AreEqual(fight.Enemies[1], EnemyAI.Choose(fight).Target);
        }

        [Test]
        public void Damage_PicksOnlyAmongTheHeroesItMayChoose_FrontRowThenTaunt()
        {
            var fight = AtP0();
            fight.Players[0].Cooldown = 1;
            fight.Enemies[4].Health = 1;                  // the weakest, but in the back row behind three front heroes
            fight.Enemies[2].Health = 900000;
            fight.Enemies[0].Health = 800000;
            fight.Enemies[1].Health = 950000;
            Assert.AreEqual(fight.Enemies[0], EnemyAI.Choose(fight).Target, "the front row's lowest");

            Effects.Apply(fight.Enemies[3], "taunt", 2);  // a taunting back-row hero must be picked, whatever their Health
            Assert.AreEqual(fight.Enemies[3], EnemyAI.Choose(fight).Target);
        }

        [Test]
        public void Damage_TheLowestHealthFocus_TakesABackRowHeroOnceTheirLaneIsOpen()
        {
            // 3-2, F1 and F2 down: B4 is open, B5 is still covered by F3 (P2a-02, 02 GDD 2)
            var fight = AtP0();
            fight.Players[0].Cooldown = 1;
            fight.Enemies[0].Health = 0; fight.Enemies[0].KnockedOut = true;
            fight.Enemies[1].Health = 0; fight.Enemies[1].KnockedOut = true;
            fight.Enemies[2].Health = 900000;
            fight.Enemies[3].Health = 2;                  // open and the lowest of those who can be picked
            fight.Enemies[4].Health = 1;                  // lower still, but covered by e2
            Assert.AreEqual(fight.Enemies[3], EnemyAI.Choose(fight).Target);

            fight.Enemies[2].Health = 0; fight.Enemies[2].KnockedOut = true;
            Assert.AreEqual(fight.Enemies[4], EnemyAI.Choose(fight).Target, "with F3 down, B5 is open too");
        }

        // ---- Threshold on the real data (P2a-02, A4; settles R17 (b)) -------------------------------------------

        static GameData RealData()
        {
            string Read(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "Data", file));
            return GameData.Load(Read("heroes.json"), Read("classes.json"), Read("traits.json"), Read("tuning.json"),
                                 Read("teams.json"), Read("formations.json"));
        }

        /// <summary>The Healing wall against the crew (2-3), at Ibis's first turn, with the AI's choice ready to read.</summary>
        static Fight AtIbis(GameData data)
        {
            var fight = Fight.Create(data, data.Teams["healing_wall"], data.Teams["enemy_crew"], 1);
            for (var i = 0; i < 100; i++)
            {
                fight.AdvanceToChoice();
                Assert.AreEqual(Outcome.Going, fight.Outcome, "set-up: the fight is still on");
                if (fight.Acting.Hero.Id == "ibis") return fight;
                fight.Act(EnemyAI.Choose(fight));
            }
            Assert.Fail("set-up: Ibis never got a turn");
            return null;
        }

        [Test]
        public void RealIbis_Threshold_DrainsTheEnemyWhoActsNext_EvenInTheBackRow()
        {
            var data = RealData();
            var fight = AtIbis(data);
            Assert.AreEqual(Button.Special, EnemyAI.Choose(fight).Button, "set-up: Threshold is her choice");

            // the crew stands 2-3: Kal and Hori in front, Strix (slot 2) behind Kal, covered while Kal stands
            var strix = fight.Enemies.Single(e => e.Hero.Id == "strix");
            Assert.AreEqual(Row.Back, strix.Row);
            Assert.IsTrue(Targeting.Covered(fight, strix), "set-up: Kal still stands in front of Strix");
            foreach (var e in fight.Enemies) e.Meter = 0;
            strix.Meter = 990;
            Assert.AreEqual(strix, fight.PredictTurns(8).First(t => t.Who.Side == Side.Enemy).Who, "set-up: Strix acts next");

            Assert.AreEqual(strix, EnemyAI.Choose(fight).Target);
        }

        [Test]
        public void RealIbis_Threshold_UnderTaunt_HitsTheTauntingHero()
        {
            var data = RealData();
            var fight = AtIbis(data);
            var halvard = fight.Enemies.Single(e => e.Hero.Id == "halvard");
            Assert.AreEqual(Row.Back, halvard.Row);
            Effects.Apply(halvard, "taunt", 2);
            foreach (var e in fight.Enemies) e.Meter = 0;
            fight.Enemies.First(e => e != halvard).Meter = 990;

            Assert.AreEqual(Button.Special, EnemyAI.Choose(fight).Button);
            Assert.AreEqual(halvard, EnemyAI.Choose(fight).Target);
        }

        [Test]
        public void Damage_AReachingSkillMayPickTheBackRow()
        {
            var fight = AtP0((id, h) =>
            {
                if (id == "p0") Pieces(h, "basic", "{ do: 'damage', target: 'enemy', power: 100, reaches_back_row: true }");
            });
            fight.Players[0].Cooldown = 1;
            fight.Enemies[4].Health = 1;
            Assert.AreEqual(fight.Enemies[4], EnemyAI.Choose(fight).Target);
        }

        [Test]
        public void SkillThatAimsNowhere_NeedsNoTarget()
        {
            var fight = AtP0();                           // the test Ultimate hits all enemies
            fight.Players[0].Charge = 100;
            var choice = EnemyAI.Choose(fight);
            Assert.AreEqual(Button.Ultimate, choice.Button);
            Assert.IsNull(choice.Target);
        }

        [Test]
        public void BadEffect_PicksTheLowestHealthEnemyWithoutIt_OrTheLowestIfAllHaveIt()
        {
            var fight = AtP0((id, h) =>
            {
                if (id == "p0") Pieces(h, "basic", "{ do: 'effect', target: 'enemy', effect: 'defense_down', turns: 2 }");
            });
            fight.Players[0].Cooldown = 1;
            fight.Enemies[0].Health = 100;                // the lowest, but already marked
            fight.Enemies[1].Health = 300;
            fight.Enemies[2].Health = 200;
            Effects.Apply(fight.Enemies[0], "defense_down", 2);
            Assert.AreEqual(fight.Enemies[2], EnemyAI.Choose(fight).Target, "the lowest of those without it");

            Effects.Apply(fight.Enemies[1], "defense_down", 2);
            Effects.Apply(fight.Enemies[2], "defense_down", 2);
            Assert.AreEqual(fight.Enemies[0], EnemyAI.Choose(fight).Target, "all have it: the lowest Health");
        }

        [Test]
        public void R1_DamageAndBadEffect_TheBadEffectRowWins()
        {
            // Mark's shape: damage, then Defense Down on the same chosen enemy
            var fight = AtP0((id, h) =>
            {
                if (id == "p0") Pieces(h, "basic", DamageOne, "{ do: 'effect', target: 'enemy', effect: 'defense_down', turns: 2 }");
            });
            fight.Players[0].Cooldown = 1;
            fight.Enemies[0].Health = 100;
            fight.Enemies[1].Health = 300;
            fight.Enemies[2].Health = 200;
            Effects.Apply(fight.Enemies[0], "defense_down", 2);
            Assert.AreEqual(fight.Enemies[2], EnemyAI.Choose(fight).Target, "not the lowest-Health enemy, who is already marked");
        }

        [Test]
        public void R1_DamageAndDrain_TheEnemyWhoActsNextWins()
        {
            // Threshold's shape: damage, then drain the turn meter of the same chosen enemy
            var fight = AtP0((id, h) =>
            {
                if (id == "p0") Pieces(h, "basic", DamageOne, "{ do: 'meter', target: 'enemy', percent: -50 }");
            });
            fight.Players[0].Cooldown = 1;
            foreach (var e in fight.Enemies) e.Health = 1000;
            fight.Enemies[0].Health = 1;                  // the lowest Health
            fight.Enemies[2].Meter = 400;                 // and e2 is the nearest to a full bar
            var next = fight.PredictTurns(8).First(t => t.Who.Side == Side.Enemy).Who;
            Assert.AreNotEqual(fight.Enemies[0], next, "set-up: the weakest is not the one who acts next");
            Assert.AreEqual(next, EnemyAI.Choose(fight).Target);
        }

        [Test]
        public void R1_Drain_WhenTheNextEnemyCannotBeChosen_TakesTheFirstChoosableOnTheBar()
        {
            // the enemy who acts next stands in the back row, out of reach: the first hero on the bar that may be picked
            var fight = AtP0((id, h) =>
            {
                if (id == "p0") Pieces(h, "basic", DamageOne, "{ do: 'meter', target: 'enemy', percent: -50 }");
            });
            fight.Players[0].Cooldown = 1;
            fight.Enemies[4].Meter = 500;                 // the back-row hero is nearest to a full bar
            fight.Enemies[1].Meter = 400;                 // then the front-row e1
            var bar = fight.PredictTurns(8);
            Assert.AreEqual(fight.Enemies[4], bar.First(t => t.Who.Side == Side.Enemy).Who, "set-up: e4 acts next");
            var firstFront = bar.First(t => t.Who.Side == Side.Enemy && t.Who.Row == Row.Front).Who;
            Assert.AreEqual(firstFront, EnemyAI.Choose(fight).Target);
        }

        [Test]
        public void HealOrBuff_PicksTheAllyWithTheLowestHealthPercent_SelfIncluded_TiesLowestSlot()
        {
            var fight = AtP0((id, h) =>
            {
                if (id == "p0") Pieces(h, "basic", "{ do: 'heal', target: 'ally', percent: 30 }");
            });
            fight.Players[0].Cooldown = 1;
            fight.Players[1].Health = 500000;             // 50 %
            fight.Players[2].Health = 300000;             // 30 %
            fight.Players[3].MaxHealth = 100;             // 30 of 100 is 30 % too: the same percent as p2, in a later slot
            fight.Players[3].Health = 30;
            Assert.AreEqual(fight.Players[2], EnemyAI.Choose(fight).Target, "a tie on 30 %: the lower slot");

            fight.Players[0].Health = 100000;             // the actor themselves, now the most hurt
            Assert.AreEqual(fight.Players[0], EnemyAI.Choose(fight).Target);
        }

        // ---- either side, no luck, a legal choice ---------------------------------------------------------------

        [Test]
        public void WorksForEitherSide_TheEnemyActingTargetsYourHeroes()
        {
            var fight = NewFight(
                new[] { ("p0", 50), ("p1", 50), ("p2", 50), ("p3", 50), ("p4", 50) },
                new[] { ("e0", 200), ("e1", 50), ("e2", 50), ("e3", 50), ("e4", 50) });
            fight.AdvanceToChoice();
            Assert.AreEqual(fight.Enemies[0], fight.Acting, "set-up: e0 acts first");
            fight.Players[0].Health = 700;
            fight.Players[1].Health = 600;
            fight.Players[2].Health = 900;

            var choice = EnemyAI.Choose(fight);
            Assert.AreEqual(Button.Special, choice.Button);
            Assert.AreEqual(fight.Players[1], choice.Target, "the lowest-Health hero of the front row");
        }

        [Test]
        public void NeverRolls_ItWorksOnACopy_WhichThrowsOnAnyRoll()
        {
            var fight = AtP0();
            fight.Players[0].Charge = 100;
            var copy = fight.Copy();
            Assert.DoesNotThrow(() => EnemyAI.Choose(copy));
            Assert.Throws<InvalidOperationException>(() => copy.Roll(50), "set-up: a roll on a copy would throw");
        }

        [Test]
        public void ChangesNothing_AndTheChoiceIsOneTheFightAccepts()
        {
            var fight = AtP0();
            var before = string.Join(",", fight.Players.Concat(fight.Enemies).Select(f => f.Health + "/" + f.Meter + "/" + f.Charge));
            var choice = EnemyAI.Choose(fight);
            var after = string.Join(",", fight.Players.Concat(fight.Enemies).Select(f => f.Health + "/" + f.Meter + "/" + f.Charge));
            Assert.AreEqual(before, after);
            Assert.AreEqual(fight.Players[0], fight.Acting);
            Assert.DoesNotThrow(() => fight.Act(choice));
        }

        [Test]
        public void SameFight_SameChoice()
        {
            Choice Pick(int seed) { var f = AtP0(seed: seed); f.Players[0].Charge = 100; return EnemyAI.Choose(f); }
            var a = Pick(7);
            var b = Pick(7);
            Assert.AreEqual(a.Button, b.Button);
            Assert.AreEqual(a.Target?.ToString(), b.Target?.ToString());
        }

        [Test]
        public void NobodyChoosing_IsAnError()
        {
            var fight = Duel();                           // AdvanceToChoice not called: nobody is acting
            Assert.Throws<InvalidOperationException>(() => EnemyAI.Choose(fight));
        }
    }
}
