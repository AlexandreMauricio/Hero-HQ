using System.IO;
using System.Linq;
using HeroHQ.Rules;
using HeroHQ.Rules.Data;
using HeroHQ.Screens;
using NUnit.Framework;
using UnityEngine;
using static HeroHQ.Tests.TestFights;

namespace HeroHQ.Tests
{
    // P2b-03 (03 Architecture 8.10): the hero card and the press-and-hold (02 GDD 17.1, 17.4, 17.5). The card's content is
    // plain C#, tested against the fighters; the holds are driven without a mouse or a clock through Hold.Press, Tick and
    // Release (the click a Button sends when a finger lifts is sent here by hand: Tap.onClick.Invoke()). Built in EditMode
    // under a throwaway object; nothing is played, and nothing here has been looked at on a screen.
    public class HeroCardTests
    {
        GameObject root;
        BattleScreen screen;
        BattleView View => screen.View;

        [SetUp]
        public void BuildScreen()
        {
            root = new GameObject("HeroCardTests");
            screen = root.AddComponent<BattleScreen>();
            screen.BuildView();
        }

        [TearDown]
        public void DestroyScreen() => Object.DestroyImmediate(root);

        static GameData real;
        static GameData Real()
        {
            string Read(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "Data", file));
            return real ?? (real = GameData.Load(Read("heroes.json"), Read("classes.json"), Read("traits.json"),
                                                 Read("tuning.json"), Read("teams.json"), Read("formations.json")));
        }

        static Fight RealFight(string players = "greek_strike", string enemies = "enemy_crew") =>
            Fight.Create(Real(), Real().Teams[players], Real().Teams[enemies], 1);

        // Both sides 3-2, p0 the fastest so the fight opens on p0's turn; every test hero has the same stats and bare skills.
        Fight AtP0()
        {
            var fight = NewFight(
                new[] { ("p0", 130), ("p1", 100), ("p2", 90), ("p3", 80), ("p4", 70) },
                new[] { ("e0", 110), ("e1", 60), ("e2", 50), ("e3", 40), ("e4", 30) });
            View.ShowFight(fight, fight.Data.Formations["3-2"], fight.Data.Formations["3-2"]);
            fight.AdvanceToChoice();
            Assert.AreSame(fight.Players[0], fight.Acting, "set-up: p0 acts first");
            return fight;
        }

        static void HoldFor(Hold hold, float seconds = Screens.Hold.Seconds)
        {
            hold.Press();
            hold.Tick(seconds);
        }

        // ---- the card's content equals the fighter ---------------------------------------------------------------------

        [Test]
        public void TheCard_EqualsTheFighter_OnBothSides_ForAllTenHeroesOfARealFight()
        {
            var fight = RealFight();
            var data = Real();
            var differs = 0;
            foreach (var who in fight.Players.Concat(fight.Enemies))
            {
                var card = HeroCard.Of(data, who, false);
                var hero = who.Hero;
                Assert.AreEqual(hero.Name, card.Name, "the full name, not the tag's short one");
                Assert.AreEqual(who.Side, card.Side);
                Assert.AreEqual(data.Classes[hero.Class].Name, card.ClassName);
                CollectionAssert.AreEqual(hero.Traits.Select(t => data.Traits[t].Name), card.Traits, $"{hero.Name}: the three traits");
                Assert.AreEqual(3, card.Traits.Count);

                // the eight stats the fight uses (final, after the leader's and passives' bonuses)
                CollectionAssert.AreEqual(
                    new[] { "Health", "Armor", "Damage", "Speed", "Accuracy", "Dodge", "Crit chance", "Crit damage" },
                    card.Stats.Select(s => s.Label));
                CollectionAssert.AreEqual(
                    new[]
                    {
                        who.MaxHealth.ToString(), who.Armor.ToString(), who.Damage.ToString(), who.Speed.ToString(),
                        who.Accuracy + " %", who.DodgeChance + " %", who.CritChance + " %", who.CritDamage + " %",
                    }, card.Stats.Select(s => s.Value), $"{hero.Name}: stats as the fight uses them");
                if (who.Damage != (int)hero.Stats["damage"] || who.Armor != (int)hero.Stats["armor"]
                    || who.MaxHealth != (int)hero.Stats["health"] || who.CritChance != (int)hero.Stats["crit_chance"]
                    || who.DodgeChance != (int)hero.Stats["dodge_chance"]) differs++;

                CollectionAssert.AreEqual(
                    new[] { "Basic", "Special", "Ultimate", "Passive" }, card.Lines.Select(l => l.Label));
                Assert.AreEqual(SkillText.Sentence(hero.Basic), card.Lines[0].Text);
                Assert.AreEqual(SkillText.Sentence(hero.Special), card.Lines[1].Text);
                Assert.AreEqual(SkillText.Sentence(hero.Ultimate), card.Lines[2].Text);
                Assert.AreEqual(SkillText.PassiveSentence(hero.Passive), card.Lines[3].Text);
                Assert.IsEmpty(card.ActiveEffects, "a fight's first moment: no effects");
            }
            Assert.Greater(differs, 0, "set-up: some heroes carry bonuses, so the card shows final numbers, not the data's");
        }

        [Test]
        public void TheLeaderSkillLine_IsOnlyOnTheLeader()
        {
            var fight = RealFight();
            var who = fight.Players[0];
            var leading = HeroCard.Of(Real(), who, true);
            var following = HeroCard.Of(Real(), who, false);

            Assert.IsTrue(leading.IsLeader);
            Assert.AreEqual("Leader skill", leading.Lines.Last().Label);
            Assert.AreEqual(SkillText.LeaderSentence(who.Hero.Leader), leading.Lines.Last().Text);
            Assert.AreEqual(5, leading.Lines.Count);
            Assert.IsFalse(following.IsLeader);
            Assert.AreEqual(4, following.Lines.Count);
            Assert.IsFalse(following.Lines.Any(l => l.Label == "Leader skill"));
        }

        [Test]
        public void EveryEffectOnTheHero_IsInWordsWithItsTurnsLeft_AndTheCountsTheIconUses()
        {
            var fight = NewFight(new[] { ("p0", 130), ("p1", 100), ("p2", 90), ("p3", 80), ("p4", 70) },
                                 new[] { ("e0", 110), ("e1", 60), ("e2", 50), ("e3", 40), ("e4", 30) });
            var who = fight.Players[1];
            Effects.Apply(who, "offense_up", 2);
            Effects.Apply(who, "defense_down", 1);
            Effects.Apply(who, "barrier", 3, 120);
            Effects.Apply(who, "stun", 0);
            Effects.Apply(who, "momentum", 0, 5);
            Effects.Apply(who, "momentum", 0, 5);

            CollectionAssert.AreEqual(new[]
            {
                "Offense Up: 2 turns left", "Defense Down: 1 turn left", "Barrier (120 left): 3 turns left",
                "Stun: next turn skipped", "Momentum (2 stacks)",
            }, HeroCard.Of(fight.Data, who, false).ActiveEffects, "in the order they were put on");

            who.TurnsStarted = 1;   // one of the holder's own turns has started: one fewer turn left, as the icon's number says
            Assert.AreEqual("Offense Up: 1 turn left", HeroCard.Of(fight.Data, who, false).ActiveEffects[0]);

            // the same on the other side
            var enemy = fight.Enemies[0];
            Effects.Apply(enemy, "taunt", 2);
            Assert.AreEqual(new[] { "Taunt: 2 turns left" }, HeroCard.Of(fight.Data, enemy, false).ActiveEffects);
            Assert.AreEqual(Side.Enemy, HeroCard.Of(fight.Data, enemy, false).Side);
        }

        [Test]
        public void EveryEffectKind_HasWords_NoRawDataName()
        {
            foreach (var kind in Effects.Kinds)
            {
                // a fresh fight each time: an opposite effect would cancel the one before, a Ward would stop a bad one
                var fight = NewFight(new[] { ("p0", 130), ("p1", 100), ("p2", 90), ("p3", 80), ("p4", 70) },
                                     new[] { ("e0", 110), ("e1", 60), ("e2", 50), ("e3", 40), ("e4", 30) });
                var who = fight.Players[2];
                Effects.Apply(who, kind, 2, 100);
                var words = HeroCard.Of(fight.Data, who, false).ActiveEffects.Last();
                Assert.IsFalse(words.Contains("_"), $"{kind}: '{words}' shows a raw data name");
                Assert.IsTrue(words.StartsWith(char.ToUpper(kind[0]).ToString()), $"{kind}: '{words}'");
            }
        }

        // ---- the card's box ---------------------------------------------------------------------------------------------

        [Test]
        public void TheCardBox_ShowsTheContent_ThenHides_AndTheLeaderTagIsOnlyForTheLeader()
        {
            var fight = RealFight();
            var data = Real();
            Assert.IsFalse(View.Card.Visible, "closed until a hold");

            View.ShowCard(HeroCard.Of(data, fight.Players[0], true));
            Assert.IsTrue(View.Card.Visible);
            Assert.AreEqual(fight.Players[0].Hero.Name, View.Card.Title.text);
            Assert.IsTrue(View.Card.LeaderTag.activeSelf);
            StringAssert.Contains(data.Classes[fight.Players[0].Hero.Class].Name, View.Card.Subtitle.text);
            StringAssert.Contains("none", View.Card.EffectsText.text);

            View.ShowCard(HeroCard.Of(data, fight.Enemies[1], false));
            Assert.AreEqual(fight.Enemies[1].Hero.Name, View.Card.Title.text, "drawn fresh each time");
            Assert.IsFalse(View.Card.LeaderTag.activeSelf);

            View.HideHold();
            Assert.IsFalse(View.Card.Visible);
        }

        [Test]
        public void TheCardBox_FitsInsideTheSafeArea_AtTheReferenceSize()
        {
            // 17.1: nothing the player reads sits closer than 90 px to the sides or 24 px to the top and bottom of 1920 x 1080.
            Assert.LessOrEqual(HeroCardView.Width, 1920 - 2 * 90);
            Assert.LessOrEqual(HeroCardView.Height, 1080 - 2 * 24);
        }

        // ---- holds on a figure --------------------------------------------------------------------------------------------

        [Test]
        public void AHoldIsFourTenthsOfASecond()
        {
            Assert.AreEqual(0.4f, Screens.Hold.Seconds);
        }

        [Test]
        public void AShortPress_IsStillATap_AndOpensNoCard()
        {
            var fight = AtP0();
            var taps = 0;
            View.HeroTapped = _ => taps++;
            var figure = View.Figures[fight.Enemies[0]];

            figure.Hold.Press();
            figure.Hold.Tick(0.39f);
            figure.Hold.Release();
            figure.Tap.onClick.Invoke();   // what a Button sends when the finger lifts

            Assert.IsFalse(View.Card.Visible, "0.39 s is not yet a hold");
            Assert.AreEqual(1, taps, "a short press is a tap");
        }

        [Test]
        public void AHold_OpensTheHeroCard_ReleaseClosesIt_AndTheClickThatEndsItIsNotATap()
        {
            var fight = AtP0();
            screen.PlayerLeaderId = "p0";
            var taps = 0;
            View.HeroTapped = _ => taps++;
            var figure = View.Figures[fight.Players[0]];

            HoldFor(figure.Hold);
            Assert.IsTrue(View.Card.Visible);
            Assert.AreEqual("P0", View.Card.Title.text);
            Assert.AreSame(fight.Players[0], screen.CardOf);
            Assert.IsTrue(View.Card.LeaderTag.activeSelf, "p0 leads");
            Assert.IsFalse(View.Bar.Any(e => e.Lit), "a hold on a figure lights no bar entries");

            figure.Hold.Release();
            Assert.IsFalse(View.Card.Visible, "release closes the card");
            Assert.IsNull(screen.CardOf);
            figure.Tap.onClick.Invoke();
            Assert.AreEqual(0, taps, "the click after a hold is part of the hold");

            // and the next short press is a tap again
            figure.Hold.Press();
            figure.Hold.Release();
            figure.Tap.onClick.Invoke();
            Assert.AreEqual(1, taps);
        }

        [Test]
        public void AHold_OnAnEnemyFigure_ShowsTheEnemysCard_WithNoLeaderSkillUnlessTheyLead()
        {
            var fight = AtP0();
            screen.EnemyLeaderId = "e0";
            HoldFor(View.Figures[fight.Enemies[1]].Hold);
            Assert.AreEqual("E1", View.Card.Title.text);
            Assert.IsFalse(View.Card.LeaderTag.activeSelf);
            View.Figures[fight.Enemies[1]].Hold.Release();

            HoldFor(View.Figures[fight.Enemies[0]].Hold);
            Assert.IsTrue(View.Card.LeaderTag.activeSelf);
        }

        [Test]
        public void AHold_NeverArmsCommitsOrReAims_WhateverIsArmed()
        {
            var fight = AtP0();
            var e = fight.Enemies;
            screen.BeginYourTurn(fight);
            screen.TapButton(Button.Special);          // an area skill: the first tap on an enemy aims, the second commits
            screen.TapHero(e[1]);
            Assert.AreEqual(Button.Special, screen.Armed);
            Assert.AreSame(e[1], screen.Aimed);

            // hold another enemy, and a friend, and a button: the click after each is dropped
            foreach (var hero in new[] { e[0], e[1], fight.Players[1] })
            {
                var figure = View.Figures[hero];
                HoldFor(figure.Hold);
                figure.Hold.Release();
                figure.Tap.onClick.Invoke();
                Assert.AreEqual(Button.Special, screen.Armed, $"holding {hero} armed or took back something");
                Assert.AreSame(e[1], screen.Aimed, $"holding {hero} re-aimed");
                Assert.IsNull(screen.Committed, $"holding {hero} committed");
            }
            var basic = View.Buttons[(int)Button.Basic];
            HoldFor(basic.Hold);
            basic.Hold.Release();
            basic.Ui.onClick.Invoke();
            Assert.AreEqual(Button.Special, screen.Armed, "a hold on the Basic button did not arm it");
            Assert.IsNull(screen.Committed);
        }

        // ---- holds on the turn-bar entries (17.4) ----------------------------------------------------------------------------

        [Test]
        public void AHold_OnABarEntry_OpensThatHerosCard_LightsEveryEntryOfTheirs_AndRingsTheirFigure()
        {
            var fight = AtP0();
            View.ShowBar(fight.PredictTurns(8));
            var hero = fight.Players[0];
            var entries = View.Bar.Where(b => b.Who == hero).ToList();
            Assert.GreaterOrEqual(entries.Count, 2, "set-up: the fastest hero has more than one entry in eight (a double turn)");

            HoldFor(entries[1].Hold);
            Assert.IsTrue(View.Card.Visible);
            Assert.AreEqual("P0", View.Card.Title.text);
            foreach (var entry in View.Bar) Assert.AreEqual(entry.Who == hero, entry.Lit, $"entry of {entry.Who}");
            foreach (var pair in View.Figures) Assert.AreEqual(pair.Key == hero, pair.Value.SpotRing.activeSelf, $"{pair.Key}'s ring");

            entries[1].Hold.Release();
            Assert.IsFalse(View.Card.Visible);
            Assert.IsFalse(View.Bar.Any(b => b.Lit), "every light is out");
            Assert.IsFalse(View.Figures.Values.Any(f => f.SpotRing.activeSelf));
        }

        [Test]
        public void ABarEntry_IsNotATapTarget_AShortPressDoesNothing()
        {
            var fight = AtP0();
            View.ShowBar(fight.PredictTurns(8));
            var entry = View.Bar[1];
            entry.Hold.Press();
            entry.Hold.Tick(0.2f);
            entry.Hold.Release();
            Assert.IsFalse(View.Card.Visible);
            Assert.IsFalse(View.Bar.Any(b => b.Lit));
        }

        [Test]
        public void TheBarRedrawnMidHold_ClosesTheCard_ItsFingerUpWillNeverCome()
        {
            var fight = AtP0();
            View.ShowBar(fight.PredictTurns(8));
            HoldFor(View.Bar[2].Hold);
            Assert.IsTrue(View.Card.Visible);

            View.ShowBar(fight.PredictTurns(8));   // the entry under the finger is destroyed
            Assert.IsFalse(View.Card.Visible, "a card never stays open with nothing holding it");
            Assert.IsFalse(View.Bar.Any(b => b.Lit));
        }

        // ---- holds on a button (17.5) ---------------------------------------------------------------------------------------

        [Test]
        public void AHold_OnAButton_ReadsItsSentenceInTheInfoStrip_ArmsNothing_AndReleasePutsTheStripBack()
        {
            var fight = AtP0();
            screen.BeginYourTurn(fight);
            var special = View.Buttons[(int)Button.Special];
            Assert.AreEqual("", View.InfoStrip.text, "set-up: nothing armed");

            HoldFor(special.Hold);
            Assert.AreEqual(SkillText.Sentence(fight.Acting.Hero.Special), View.InfoStrip.text);
            Assert.IsNull(screen.Armed, "a hold arms nothing");

            special.Hold.Release();
            special.Ui.onClick.Invoke();
            Assert.IsNull(screen.Armed, "the click that ends a hold does not arm either");
            Assert.AreEqual("", View.InfoStrip.text, "back to what it said");

            // an armed Basic keeps its sentence after a hold on the Ultimate
            screen.TapButton(Button.Basic);
            var ultimate = View.Buttons[(int)Button.Ultimate];
            HoldFor(ultimate.Hold);
            Assert.AreEqual(SkillText.Sentence(fight.Acting.Hero.Ultimate), View.InfoStrip.text, "even a grey button can be read");
            ultimate.Hold.Release();
            Assert.AreEqual(Button.Basic, screen.Armed);
            Assert.AreEqual(SkillText.Sentence(fight.Acting.Hero.Basic), View.InfoStrip.text);
        }

        [Test]
        public void AShortPress_OnAButton_IsStillATap()
        {
            var fight = AtP0();
            screen.BeginYourTurn(fight);
            var basic = View.Buttons[(int)Button.Basic];
            basic.Hold.Press();
            basic.Hold.Tick(0.1f);
            basic.Hold.Release();
            basic.Ui.onClick.Invoke();
            Assert.AreEqual(Button.Basic, screen.Armed);
        }

        [Test]
        public void AHold_OnAButton_OnAnEnemysTurn_ReadsNothing()
        {
            var fight = AtP0();   // p0 acts, but the screen is not waiting for taps (no BeginYourTurn): the buttons are locked
            var special = View.Buttons[(int)Button.Special];
            HoldFor(special.Hold);
            Assert.AreEqual("", View.InfoStrip.text);
            special.Hold.Release();
        }

        // ---- the hold itself -------------------------------------------------------------------------------------------------------

        [Test]
        public void SlidingOffBeforeFourTenths_IsNoHold_ButOnceOpenTheCardStaysUntilTheFingerLifts()
        {
            var go = new GameObject("hold test");
            try
            {
                var hold = go.AddComponent<Hold>();
                var started = 0;
                var ended = 0;
                hold.Started = () => started++;
                hold.Ended = () => ended++;

                hold.Press();
                hold.Tick(0.2f);
                hold.OnPointerExit(null);
                hold.Tick(1f);
                Assert.AreEqual(0, started, "the finger slid off at 0.2 s");

                hold.Press();
                hold.Tick(0.4f);
                Assert.AreEqual(1, started);
                hold.Tick(5f);
                Assert.AreEqual(1, started, "started once");
                hold.OnPointerExit(null);
                Assert.AreEqual(0, ended, "a thumb drifting a little does not close the card");
                hold.Release();
                Assert.AreEqual(1, ended);
                hold.Release();
                Assert.AreEqual(1, ended, "a second release does nothing");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
