using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroHQ.Rules;
using HeroHQ.Rules.Data;
using HeroHQ.Screens;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using static HeroHQ.Tests.TestFights;
using Look = HeroHQ.Screens.BattleView.ButtonLook;

namespace HeroHQ.Tests
{
    // P2a-09 (03 Architecture 7.7): your turn. The taps are BattleScreen's plain methods (TapButton, TapHero, TapEmpty),
    // called here without a mouse; the screen is built in EditMode under a throwaway object and nothing is played.
    // 02 GDD 17.5 and 17.9: S7 (no single tap commits early), S6 (hit chips), S8 (glow, dim, shield, the reason), S9 (buttons).
    public class BattleTurnTests
    {
        GameObject root;
        BattleScreen screen;
        BattleView View => screen.View;

        [SetUp]
        public void BuildScreen()
        {
            root = new GameObject("BattleTurnTests");
            screen = root.AddComponent<BattleScreen>();
            screen.BuildView();
        }

        [TearDown]
        public void DestroyScreen() => UnityEngine.Object.DestroyImmediate(root);

        static void Pieces(JObject hero, string button, params string[] pieces) =>
            hero[button]["pieces"] = JArray.Parse("[" + string.Join(",", pieces) + "]");

        // The test heroes (TestData): Basic = one chosen enemy, Special = a chosen enemy and their neighbours (cooldown 2),
        // Ultimate = all enemies (no choice). Both sides 3-2: e0, e1, e2 in front; e3 behind e0 + e1, e4 behind e1 + e2.
        // p0 is the fastest, so the fight opens on p0's turn. Nobody dodges unless a test says so: no chips by default.
        Fight AtP0(Action<string, JObject> edit = null)
        {
            var fight = NewFight(
                new[] { ("p0", 130), ("p1", 100), ("p2", 90), ("p3", 80), ("p4", 70) },
                new[] { ("e0", 110), ("e1", 60), ("e2", 50), ("e3", 40), ("e4", 30) }, edit: edit);
            View.ShowFight(fight, fight.Data.Formations["3-2"], fight.Data.Formations["3-2"]);
            fight.AdvanceToChoice();
            Assert.AreSame(fight.Players[0], fight.Acting, "set-up: p0 acts first");
            return fight;
        }

        HeroFigure F(Fighter hero) => View.Figures[hero];
        Look LookOf(Button b) => View.Buttons[(int)b].Look;

        static IEnumerable<Fighter> All(Fight fight) => fight.Players.Concat(fight.Enemies);

        void AssertGlow(Fight fight, params Fighter[] glowing)
        {
            foreach (var hero in All(fight))
                Assert.AreEqual(glowing.Contains(hero), F(hero).Glow.activeSelf, $"{hero}'s glow");
        }

        void AssertDim(Fight fight, params Fighter[] dimmed)
        {
            foreach (var hero in All(fight))
                Assert.AreEqual(dimmed.Contains(hero), F(hero).Dimmed, $"{hero}'s dim");
        }

        void AssertOutlined(Fight fight, params Fighter[] outlined)
        {
            foreach (var hero in All(fight))
                Assert.AreEqual(outlined.Contains(hero), F(hero).AimOutline.activeSelf, $"{hero}'s aiming outline");
        }

        void AssertNothingShowing(Fight fight)
        {
            AssertGlow(fight);
            AssertDim(fight);
            AssertOutlined(fight);
            foreach (var hero in All(fight)) Assert.IsFalse(F(hero).HitChip.activeSelf, $"{hero}'s hit chip");
        }

        // ---- S7: arm, then commit; no single tap commits early ---------------------------------------------------

        [Test]
        public void OneHeroSkill_ArmingCommitsNothing_GlowsWhoMayBeChosen_AndTheFirstTapOnOneCommits()
        {
            var fight = AtP0();
            var e = fight.Enemies;
            screen.BeginYourTurn(fight);
            AssertNothingShowing(fight);

            screen.TapButton(Button.Basic);
            Assert.AreEqual(Button.Basic, screen.Armed);
            Assert.IsNull(screen.Committed, "arming alone commits nothing");
            AssertGlow(fight, e[0], e[1], e[2]);
            AssertDim(fight, e[3], e[4]);   // only the side the skill picks from is dimmed (reading R27)
            AssertOutlined(fight);
            Assert.AreEqual(SkillText.Sentence(fight.Acting.Hero.Basic), View.InfoStrip.text);

            screen.TapHero(e[1]);
            Assert.AreEqual(Button.Basic, screen.Committed.Value.Button);
            Assert.AreSame(e[1], screen.Committed.Value.Target);
            AssertNothingShowing(fight);
            foreach (var b in View.Buttons) Assert.AreEqual(Look.Hidden, b.Look, $"{b.Kind} hidden while it plays back (P2b-04)");
            Assert.IsFalse(View.ButtonsShown);
            Assert.IsFalse(View.ButtonsTakeTaps, "no tap reaches them from the moment you commit");
            Assert.AreEqual(new Vector2(0, -BattleView.ButtonHideDrop), View.ButtonsBox.GetComponent<Mover>().Home, "sliding out");
        }

        [Test]
        public void AreaSkill_TheFirstTapAims_AnotherReAims_TheSameHeroAgainCommits()
        {
            var fight = AtP0();
            var e = fight.Enemies;
            screen.BeginYourTurn(fight);

            screen.TapButton(Button.Special);
            AssertGlow(fight, e[0], e[1], e[2]);
            AssertOutlined(fight);

            screen.TapHero(e[1]);
            Assert.IsNull(screen.Committed, "the first tap only aims");
            Assert.AreSame(e[1], screen.Aimed);
            AssertOutlined(fight, e[0], e[1], e[2]);   // e1 and its neighbours in the front row
            AssertGlow(fight, e[0], e[1], e[2]);       // still glowing: another tap re-aims

            screen.TapHero(e[0]);
            Assert.IsNull(screen.Committed, "tapping another hero re-aims");
            Assert.AreSame(e[0], screen.Aimed);
            AssertOutlined(fight, e[0], e[1]);

            screen.TapHero(e[0]);
            Assert.AreEqual(Button.Special, screen.Committed.Value.Button);
            Assert.AreSame(e[0], screen.Committed.Value.Target);
        }

        [Test]
        public void AreaSkill_AimedThenTheArmedButton_Commits()
        {
            var fight = AtP0();
            screen.BeginYourTurn(fight);
            screen.TapButton(Button.Special);
            screen.TapHero(fight.Enemies[2]);
            Assert.IsNull(screen.Committed);
            screen.TapButton(Button.Special);
            Assert.AreEqual(Button.Special, screen.Committed.Value.Button);
            Assert.AreSame(fight.Enemies[2], screen.Committed.Value.Target);
        }

        [Test]
        public void NoChoiceSkill_ArmingOutlinesEveryoneItTouches_NothingDims_ATapOnAnyOutlinedHeroCommits()
        {
            var fight = AtP0();
            fight.Players[0].Charge = 100;
            screen.BeginYourTurn(fight);

            screen.TapButton(Button.Ultimate);
            Assert.IsNull(screen.Committed);
            AssertOutlined(fight, fight.Enemies.ToArray());
            AssertGlow(fight);
            AssertDim(fight);

            screen.TapHero(fight.Players[1]);
            Assert.IsNull(screen.Committed, "a hero the skill doesn't touch does nothing");
            Assert.AreEqual(Button.Ultimate, screen.Armed);

            // OWNER 2026-11-18 (2a feedback): the tap on an outlined hero is the committing tap.
            screen.TapHero(fight.Enemies[3]);
            Assert.AreEqual(Button.Ultimate, screen.Committed.Value.Button);
            Assert.IsNull(screen.Committed.Value.Target);
            AssertNothingShowing(fight);
        }

        [Test]
        public void NoChoiceSkill_TheArmedButtonAgainStillCommits()
        {
            var fight = AtP0();
            fight.Players[0].Charge = 100;
            screen.BeginYourTurn(fight);
            screen.TapButton(Button.Ultimate);
            Assert.IsNull(screen.Committed, "arming alone commits nothing");
            screen.TapButton(Button.Ultimate);
            Assert.AreEqual(Button.Ultimate, screen.Committed.Value.Button);
            Assert.IsNull(screen.Committed.Value.Target);
        }

        [Test]
        public void NoChoiceSkill_OnYourOwnSide_AnAllyTapCommits_AHeroItDoesntTouchDoesNot()
        {
            var fight = AtP0((id, h) =>
            {
                if (id == "p0") Pieces(h, "special", "{ do: 'effect', target: 'self', effect: 'offense_up', turns: 1 }");
            });
            screen.BeginYourTurn(fight);
            screen.TapButton(Button.Special);
            screen.TapHero(fight.Enemies[0]);
            screen.TapHero(fight.Players[2]);
            Assert.IsNull(screen.Committed, "only the outlined hero (self) counts");
            screen.TapHero(fight.Players[0]);
            Assert.AreEqual(Button.Special, screen.Committed.Value.Button);
        }

        [Test]
        public void AnotherButtonReArms_TheArmedOneAgainTakesItBack_AndSoDoesTheEmptyField()
        {
            var fight = AtP0();
            screen.BeginYourTurn(fight);

            screen.TapHero(fight.Enemies[0]);
            Assert.IsNull(screen.Armed, "a hero's tap with nothing armed does nothing");
            Assert.IsNull(screen.Committed);

            screen.TapButton(Button.Basic);
            screen.TapButton(Button.Special);
            Assert.AreEqual(Button.Special, screen.Armed, "another button re-arms");
            screen.TapButton(Button.Special);
            Assert.IsNull(screen.Armed, "the armed button again takes it back (nothing aimed yet)");
            AssertNothingShowing(fight);

            screen.TapButton(Button.Basic);
            screen.TapEmpty();
            Assert.IsNull(screen.Armed, "the empty field takes it back");

            screen.TapButton(Button.Special);
            screen.TapHero(fight.Enemies[1]);
            screen.TapEmpty();
            Assert.IsNull(screen.Armed);
            Assert.IsNull(screen.Aimed);
            Assert.IsNull(screen.Committed);
            AssertNothingShowing(fight);
            Assert.AreEqual("", View.InfoStrip.text);
        }

        [Test]
        public void UnusableButtons_ArmNothing()
        {
            var fight = AtP0();
            fight.Players[0].Cooldown = 2;
            fight.Players[0].Charge = 50;
            screen.BeginYourTurn(fight);
            screen.TapButton(Button.Special);
            Assert.IsNull(screen.Armed, "the Special is on cooldown");
            screen.TapButton(Button.Ultimate);
            Assert.IsNull(screen.Armed, "the Ultimate is under 100");
        }

        // ---- S8: who may be chosen, the shield and the reason ------------------------------------------------------

        [Test]
        public void ACoveredHerosTap_CommitsNothing_AndTheInfoStripSaysFrontRowFirst()
        {
            var fight = AtP0();
            screen.BeginYourTurn(fight);
            screen.TapButton(Button.Basic);

            screen.TapHero(fight.Enemies[3]);
            Assert.IsNull(screen.Committed);
            Assert.AreEqual(Button.Basic, screen.Armed, "still armed");
            Assert.AreEqual(BattleScreen.FrontRowFirst, screen.Reason);
            Assert.AreEqual("Front row first.", View.InfoStrip.text);

            screen.TapHero(fight.Players[1]);
            Assert.IsNull(screen.Committed, "your own hero can't be chosen for a Basic either");
            Assert.IsNull(screen.Reason, "no reason for the other side");
            Assert.AreEqual(SkillText.Sentence(fight.Acting.Hero.Basic), View.InfoStrip.text);
        }

        [Test]
        public void UnderTaunt_OnlyTheTauntingHeroGlows_AnotherTapSaysWhy_AndTheTaunterCommits()
        {
            var fight = AtP0();
            var e = fight.Enemies;
            Effects.Apply(e[2], "taunt", 1);
            screen.BeginYourTurn(fight);
            screen.TapButton(Button.Basic);
            AssertGlow(fight, e[2]);
            AssertDim(fight, e[0], e[1], e[3], e[4]);

            screen.TapHero(e[0]);
            Assert.IsNull(screen.Committed);
            Assert.AreEqual("E2 has Taunt: pick them.", View.InfoStrip.text);
            screen.TapHero(e[3]);
            Assert.AreEqual("E2 has Taunt: pick them.", View.InfoStrip.text, "Taunt wins over the lane rule");

            screen.TapHero(e[2]);
            Assert.AreSame(e[2], screen.Committed.Value.Target);
        }

        [Test]
        public void TwoTaunters_TheReasonNamesBoth()
        {
            var fight = AtP0();
            Effects.Apply(fight.Enemies[0], "taunt", 1);
            Effects.Apply(fight.Enemies[2], "taunt", 1);
            screen.BeginYourTurn(fight);
            screen.TapButton(Button.Basic);
            screen.TapHero(fight.Enemies[1]);
            Assert.AreEqual("E0 and E2 have Taunt: pick them.", View.InfoStrip.text);
        }

        [Test]
        public void Shields_EqualTargetingCovered_OnBothSides_AndGoWhenTheLastCoverFalls()
        {
            var fight = AtP0();
            foreach (var hero in All(fight))
                Assert.AreEqual(Targeting.Covered(fight, hero), F(hero).Shield.activeSelf, $"{hero}'s shield at the start");
            Assert.IsTrue(F(fight.Enemies[3]).Shield.activeSelf && F(fight.Players[4]).Shield.activeSelf, "set-up");

            foreach (var down in new[] { fight.Enemies[0], fight.Enemies[1] })
            {
                down.KnockedOut = true;
                down.Health = 0;
            }
            View.SyncAfterAction(fight, new Playback(fight));
            Assert.IsFalse(F(fight.Enemies[3]).Shield.activeSelf, "e3's lane (e0, e1) is open");
            Assert.IsTrue(F(fight.Enemies[4]).Shield.activeSelf, "e4 is still covered by e2");
            foreach (var hero in All(fight).Where(h => !h.KnockedOut))
                Assert.AreEqual(Targeting.Covered(fight, hero), F(hero).Shield.activeSelf, $"{hero}'s shield after the knockouts");
        }

        [Test]
        public void ASkillThatReachesTheBackRow_CoveredHeroesGlow_AndKeepTheirShield()
        {
            var fight = AtP0((id, h) =>
            {
                if (id == "p0") Pieces(h, "basic", "{ do: 'damage', target: 'enemy', power: 100, reaches_back_row: true }");
            });
            screen.BeginYourTurn(fight);
            screen.TapButton(Button.Basic);
            AssertGlow(fight, fight.Enemies.ToArray());
            AssertDim(fight);
            Assert.IsTrue(F(fight.Enemies[3]).Shield.activeSelf);
            screen.TapHero(fight.Enemies[4]);
            Assert.AreSame(fight.Enemies[4], screen.Committed.Value.Target);
        }

        [Test]
        public void AnAllySkill_GlowsYourWholeSide_AndCommitsOnTheFirstTap()
        {
            var fight = AtP0((id, h) =>
            {
                if (id == "p0") Pieces(h, "special", "{ do: 'heal', target: 'ally', percent: 20 }");
            });
            screen.BeginYourTurn(fight);
            screen.TapButton(Button.Special);
            AssertGlow(fight, fight.Players.ToArray());
            AssertDim(fight);
            screen.TapHero(fight.Players[3]);
            Assert.AreSame(fight.Players[3], screen.Committed.Value.Target);
        }

        // ---- S6: the hit chips -------------------------------------------------------------------------------------

        // Every figure's chip, against Options: shown only on the heroes listed, and then exactly "Hit (100 - miss) %".
        void AssertChips(Fight fight, Button button, params Fighter[] chipped)
        {
            var miss = fight.Options[(int)button].MissChance;
            foreach (var hero in All(fight))
            {
                var shown = F(hero).HitChip.activeSelf;
                Assert.AreEqual(chipped.Contains(hero), shown, $"{hero}'s chip");
                if (shown) Assert.AreEqual(HeroFigure.HitWords(100 - miss[hero]), F(hero).HitText.text, $"{hero}'s chip text");
            }
        }

        static void Dodge(Fight fight, params (int enemy, int dodge)[] dodges)
        {
            foreach (var (enemy, dodge) in dodges) fight.Enemies[enemy].DodgeChance = dodge;
        }

        [Test]
        public void HitChips_OneHeroSkill_OnEveryGlowingEnemy_Hit100Included_TheNumberFromOptions()
        {
            var fight = AtP0();
            Dodge(fight, (0, 15), (2, 22), (3, 25));   // e3 dodges too, but is covered: no glow, no chip
            screen.BeginYourTurn(fight);
            screen.TapButton(Button.Basic);
            AssertChips(fight, Button.Basic, fight.Enemies[0], fight.Enemies[1], fight.Enemies[2]);
            Assert.AreEqual("Hit 85 %", F(fight.Enemies[0]).HitText.text);
            Assert.AreEqual("Hit 100 %", F(fight.Enemies[1]).HitText.text, "OWNER 2026-11-17: no dodge still shows its chip");
            Assert.AreEqual("Hit 78 %", F(fight.Enemies[2]).HitText.text);
        }

        [Test]
        public void HitChips_AreaSkill_OnTheOutlinedOnesOnceAimed_NoChoiceSkill_OnEveryEnemyItTouches_Hit100Included()
        {
            var fight = AtP0();
            fight.Players[0].Charge = 100;
            Dodge(fight, (0, 10), (1, 20), (3, 30), (4, 5));
            screen.BeginYourTurn(fight);

            screen.TapButton(Button.Special);
            AssertChips(fight, Button.Special);                                   // nothing aimed yet: glow only
            screen.TapHero(fight.Enemies[2]);
            AssertChips(fight, Button.Special, fight.Enemies[1], fight.Enemies[2]);   // e1, e2 outlined; e2 doesn't dodge: "Hit 100 %"
            Assert.AreEqual("Hit 100 %", F(fight.Enemies[2]).HitText.text);
            screen.TapHero(fight.Enemies[2]);                                    // the same hero again commits
            Assert.IsTrue(screen.Committed.HasValue, "set-up");
            AssertChips(fight, Button.Special);                                   // chips go with the commit

            screen.BeginYourTurn(fight);
            screen.TapButton(Button.Ultimate);
            AssertChips(fight, Button.Ultimate, fight.Enemies.ToArray());
        }

        [Test]
        public void NoChips_OnCantMissSkills_HealsOrBuffs()
        {
            var fight = AtP0((id, h) =>
            {
                if (id != "p0") return;
                Pieces(h, "basic", "{ do: 'damage', target: 'enemy', power: 100, cant_miss: true }");
                Pieces(h, "special", "{ do: 'heal', target: 'ally', percent: 20 }");
                Pieces(h, "ultimate", "{ do: 'effect', target: 'self', effect: 'offense_up', turns: 1 }");
            });
            fight.Players[0].Charge = 100;
            Dodge(fight, (0, 20), (1, 20), (2, 20), (3, 20), (4, 20));
            screen.BeginYourTurn(fight);

            foreach (var b in new[] { Button.Basic, Button.Special, Button.Ultimate })
            {
                screen.TapEmpty();
                screen.TapButton(b);
                Assert.AreEqual(b, screen.Armed, "set-up");
                AssertChips(fight, b);
            }
            AssertOutlined(fight, fight.Players[0]);   // the self buff outlines p0
        }

        [Test]
        public void NoChips_OnACantMissNoChoiceSkill_ThoughEveryEnemyIsOutlined()
        {
            var fight = AtP0((id, h) =>
            {
                if (id == "p0") Pieces(h, "ultimate", "{ do: 'damage', target: 'all_enemies', power: 100, cant_miss: true }");
            });
            fight.Players[0].Charge = 100;
            screen.BeginYourTurn(fight);
            screen.TapButton(Button.Ultimate);
            AssertOutlined(fight, fight.Enemies.ToArray());
            AssertChips(fight, Button.Ultimate);   // OWNER 2026-11-18: "can't miss" shows no chip
        }

        // ---- S9: the buttons -------------------------------------------------------------------------------------

        [Test]
        public void Buttons_ShowTheSkill_AndTheirState_FromOptionsAndTheActingHero()
        {
            var fight = AtP0();
            View.ShowActing(fight.Acting);
            Assert.AreEqual("P0's turn", View.TurnLine.text);
            screen.BeginYourTurn(fight);
            Assert.AreEqual("P0 Strike", View.Buttons[0].Name.text);
            Assert.AreEqual(Look.Ready, LookOf(Button.Basic));
            Assert.AreEqual(Look.Ready, LookOf(Button.Special));
            Assert.AreEqual(Look.Charging, LookOf(Button.Ultimate));
            Assert.AreEqual("0", View.Buttons[2].State.text);

            fight.Players[0].Cooldown = 2;
            fight.Players[0].Charge = 62.7;
            screen.BeginYourTurn(fight);
            Assert.AreEqual(Look.Waiting, LookOf(Button.Special));
            Assert.AreEqual("2", View.Buttons[1].State.text, "the turns left");
            Assert.AreEqual(Look.Charging, LookOf(Button.Ultimate));
            Assert.AreEqual("62", View.Buttons[2].State.text, "the charge, rounded down");

            fight.Players[0].Charge = 100;
            screen.BeginYourTurn(fight);
            Assert.AreEqual(Look.UltimateReady, LookOf(Button.Ultimate));
            Assert.AreEqual("READY", View.Buttons[2].State.text);

            screen.TapButton(Button.Basic);
            Assert.AreEqual(Look.Armed, LookOf(Button.Basic));
            Assert.IsTrue(View.Buttons[0].Outline.activeSelf);
            Assert.Greater(View.Buttons[0].Rect.anchoredPosition.y, View.Buttons[1].Rect.anchoredPosition.y, "armed is raised");

            foreach (var b in View.Buttons)
                Assert.GreaterOrEqual(Mathf.Min(b.Rect.sizeDelta.x, b.Rect.sizeDelta.y), 144, "17.1: a must-press button is at least 144 px");
        }

        // ---- P2b-02: the symbols and the target mark on a button (02 GDD 17.5) -------------------------------------

        [Test]
        public void Buttons_ShowASymbolPerKindOfPiece_AndTheTargetMark_OneDotThreeDotsAllDots()
        {
            var fight = AtP0((id, h) =>
            {
                if (id != "p0") return;
                Pieces(h, "basic", "{ do: 'damage', target: 'enemy', power: 100 }");
                Pieces(h, "special", "{ do: 'damage', target: 'enemy_and_neighbours', power: 100 }",
                                     "{ do: 'effect', target: 'enemy', effect: 'stun' }");
                Pieces(h, "ultimate", "{ do: 'damage', target: 'all_enemies', power: 100 }",
                                      "{ do: 'effect', target: 'self', effect: 'offense_up', turns: 2 }");
            });
            screen.BeginYourTurn(fight);
            var (basic, special, ultimate) = (View.Buttons[0], View.Buttons[1], View.Buttons[2]);

            CollectionAssert.AreEqual(new[] { EffectLook.Symbol.Damage }, basic.Symbols);
            Assert.AreEqual(EffectLook.Reach.One, basic.Reach);
            Assert.AreEqual(1, basic.Dots.Count, "one dot");

            CollectionAssert.AreEqual(new[] { EffectLook.Symbol.Damage, EffectLook.Symbol.BadEffect }, special.Symbols);
            Assert.AreEqual(EffectLook.Reach.Three, special.Reach);
            Assert.AreEqual(3, special.Dots.Count, "three dots");

            CollectionAssert.AreEqual(new[] { EffectLook.Symbol.Damage, EffectLook.Symbol.Buff }, ultimate.Symbols);
            Assert.AreEqual(EffectLook.Reach.All, ultimate.Reach);
            Assert.AreEqual(5, ultimate.Dots.Count, "all dots");
            Assert.AreEqual(2, basic.SymbolRow.childCount, "one symbol and one dot, nothing else in the row");
        }

        [Test]
        public void Buttons_ASelfSkill_ShowsARingNotADot()
        {
            var fight = AtP0((id, h) =>
            {
                if (id == "p0") Pieces(h, "basic", "{ do: 'effect', target: 'self', effect: 'defense_up', turns: 2 }");
            });
            screen.BeginYourTurn(fight);
            var basic = View.Buttons[0];

            CollectionAssert.AreEqual(new[] { EffectLook.Symbol.Buff }, basic.Symbols);
            Assert.AreEqual(EffectLook.Reach.Self, basic.Reach);
            Assert.AreEqual(1, basic.Dots.Count);
            StringAssert.Contains("Ring", basic.Dots[0].sprite.texture.name, "self is a hollow ring, not a dot");
            StringAssert.Contains("Rounded", View.Buttons[1].Dots[0].sprite.texture.name, "a target is a filled dot");
        }

        [Test]
        public void Buttons_HiddenWhenNotChoosing_KeepTheirFaceWhileTheyLeave_AndComeBackWithTheNextChoice()
        {
            // P2b-04 (17.3.1): hidden, not greyed. They keep what they showed while they slide out (a blank grey button
            // sliding away would look wrong); the next choice redraws them.
            var fight = AtP0();
            screen.BeginYourTurn(fight);
            Assert.IsTrue(View.Buttons.All(b => b.Symbols.Count > 0 && b.Dots.Count > 0), "set-up: every button has them");

            View.HideButtons();
            Assert.IsTrue(View.Buttons.All(b => b.Look == Look.Hidden));
            Assert.IsTrue(View.Buttons.All(b => b.Symbols.Count > 0), "still drawn while they leave");

            screen.BeginYourTurn(fight);
            Assert.IsTrue(View.ButtonsShown);
            Assert.IsTrue(View.Buttons.All(b => b.Look != Look.Hidden && b.Symbols.Count > 0 && b.Dots.Count > 0), "back with the next choice");
        }

        [Test]
        public void TheButtons_AreOnScreenOnlyWhileYourHeroChooses_SlidingInAndOutIn15Hundredths()
        {
            var fight = AtP0();
            var box = View.ButtonsBox.GetComponent<Mover>();
            var away = new Vector2(0, -BattleView.ButtonHideDrop);
            Assert.IsFalse(View.ButtonsShown, "a fight starts with them away");
            Assert.IsFalse(View.ButtonsTakeTaps);
            Assert.AreEqual(away, View.ButtonsBox.anchoredPosition, "put away at once, not slid");
            Assert.AreEqual(0.15f, BattleView.ButtonSlideSeconds);

            screen.BeginYourTurn(fight);
            Assert.IsTrue(View.ButtonsShown);
            Assert.IsTrue(View.ButtonsTakeTaps);
            Assert.AreEqual(Vector2.zero, box.Home, "sliding in");
            box.Advance(0.075f);
            Assert.Greater(View.ButtonsBox.anchoredPosition.y, away.y, "on the way");
            Assert.Less(View.ButtonsBox.anchoredPosition.y, 0, "not there yet");
            box.Advance(0.1f);
            Assert.AreEqual(Vector2.zero, View.ButtonsBox.anchoredPosition, "in");
            screen.TapButton(Button.Basic);                       // arming redraws them; nothing slides again
            Assert.IsFalse(box.Moving);

            screen.TapHero(fight.Enemies[0]);                     // commits
            Assert.IsFalse(View.ButtonsShown);
            Assert.IsFalse(View.ButtonsTakeTaps, "no tap reaches them from the moment you commit");
            box.Advance(0.2f);
            Assert.AreEqual(away, View.ButtonsBox.anchoredPosition, "out");
        }

        [Test]
        public void WhileTheButtonsAreAway_TheCornerShowsTheActingHeroAndTheirTurn_RedForARival_AndIsEmptyWhenNobodyActs()
        {
            var fight = AtP0();
            Assert.IsFalse(View.Corner.gameObject.activeSelf, "nobody acting");

            View.ShowActing(fight.Acting);                        // p0, before the choice opens
            Assert.IsTrue(View.Corner.gameObject.activeSelf, "the buttons are away, somebody acts");
            Assert.AreEqual($"{fight.Acting.Hero.ShortName}'s turn", View.CornerText.text);
            Assert.AreEqual(BattleView.Blue, View.CornerText.color);
            Assert.AreEqual(fight.Acting.Hero.Name.Substring(0, 2), View.CornerInitials.text, "the initials, as on the bar");

            screen.BeginYourTurn(fight);
            Assert.IsFalse(View.Corner.gameObject.activeSelf, "the buttons are in: their place is taken");
            screen.TapButton(Button.Basic);
            screen.TapHero(fight.Enemies[0]);
            Assert.IsTrue(View.Corner.gameObject.activeSelf, "after the commit the corner shows who is acting while it plays");

            View.ShowActing(fight.Enemies[0]);
            Assert.AreEqual(BattleView.Red, View.CornerText.color, "a rival's turn is red");
            Assert.AreEqual($"{fight.Enemies[0].Hero.ShortName}'s turn", View.CornerText.text);

            View.ShowActing(null);
            Assert.IsFalse(View.Corner.gameObject.activeSelf, "at the result nobody is acting");
        }

        [Test]
        public void ANewFight_StartsWithTheButtonsAway_EvenIfTheLastOneWasQuitMidSlide()
        {
            var fight = AtP0();
            screen.BeginYourTurn(fight);                          // sliding in, never finished
            View.ShowFight(fight, fight.Data.Formations["3-2"], fight.Data.Formations["3-2"]);
            Assert.IsFalse(View.ButtonsShown);
            Assert.IsFalse(View.ButtonsBox.GetComponent<Mover>().Moving);
            Assert.AreEqual(new Vector2(0, -BattleView.ButtonHideDrop), View.ButtonsBox.anchoredPosition);
        }

        [Test]
        public void ATapOnAHeroYouCantPick_ShakesItAndDoesNothingElse_AndThePickableOnesDontShake()
        {
            var fight = AtP0();
            var e = fight.Enemies;
            screen.BeginYourTurn(fight);
            screen.TapButton(Button.Basic);                       // e3 and e4 are covered by the front row
            var covered = F(e[3]);
            Assert.IsFalse(covered.Root.TryGetComponent<Mover>(out _), "nothing has moved it yet");

            screen.TapHero(e[3]);
            Assert.AreEqual(BattleScreen.FrontRowFirst, screen.Reason);
            Assert.IsNull(screen.Committed, "nothing committed");
            var mover = covered.Root.GetComponent<Mover>();
            Assert.IsTrue(mover.Moving, "it shakes");
            mover.Advance(0.025f);
            Assert.AreNotEqual(covered.Rest, covered.Root.anchoredPosition, "off its square mid-shake");
            mover.Advance(1);
            Assert.AreEqual(covered.Rest, covered.Root.anchoredPosition, "back on its square");
            Assert.IsFalse(F(e[1]).Root.TryGetComponent<Mover>(out _), "nobody else moved");
        }

        [Test]
        public void ButtonSymbols_ReDrawOnlyWhenTheSkillChanges()
        {
            var fight = AtP0();
            screen.BeginYourTurn(fight);
            var before = View.Buttons[0].SymbolRow.GetChild(0);
            screen.TapButton(Button.Basic);                     // arming redraws the button's look, not its symbols
            Assert.AreSame(before, View.Buttons[0].SymbolRow.GetChild(0));
        }

        [Test]
        public void ButtonSymbols_TheWidestRowFitsTheButton_AndStandsClearOfTheNameAndTheState()
        {
            // Four symbols and the five-dot mark is the widest row there can be (17.5 lists five kinds; none of the
            // real skills uses more than four, EffectLookTests).
            const float widest = 4 * BattleView.SymbolSize + 3 * BattleView.SymbolGap + 5 * BattleView.DotSize + 4 * BattleView.DotGap;
            const float bodyInset = 2 * 6;
            Assert.LessOrEqual(widest, BattleView.ButtonWidth - bodyInset - 2 * BattleView.SymbolSide, "the narrowest button");

            var fight = AtP0();
            screen.BeginYourTurn(fight);
            foreach (var b in View.Buttons)
            {
                var row = Within(b, b.SymbolRow);
                Assert.IsFalse(row.Overlaps(Within(b, b.Name.rectTransform)), $"{b.Kind}: the row over the skill name");
                Assert.IsFalse(row.Overlaps(Within(b, b.State.rectTransform)), $"{b.Kind}: the row over the big state line");
                Assert.GreaterOrEqual(row.xMin, 0, $"{b.Kind}: inside the button");
                Assert.LessOrEqual(row.xMax, b.Rect.sizeDelta.x, $"{b.Kind}: inside the button");
            }
        }

        // A box's place inside its button (x from the left, y up from the bottom), whatever the canvas scale.
        static Rect Within(BattleView.SkillButton b, RectTransform box)
        {
            var corners = new Vector3[4];
            box.GetLocalCorners(corners);
            var low = (Vector2)b.Rect.InverseTransformPoint(box.TransformPoint(corners[0]));
            var high = (Vector2)b.Rect.InverseTransformPoint(box.TransformPoint(corners[2]));
            var origin = -Vector2.Scale(b.Rect.pivot, b.Rect.sizeDelta);   // the button's left-bottom corner in its own space
            return Rect.MinMaxRect(low.x - origin.x, low.y - origin.y, high.x - origin.x, high.y - origin.y);
        }

        [Test]
        public void TheTurnLine_SaysTheShortName_WhenTheHeroHasOne()
        {
            var fight = AtP0((id, h) =>
            {
                if (id == "p0") { h["name"] = "Pelagia Stormborn"; h["short_name"] = "Pelagia"; }
            });
            View.ShowActing(fight.Acting);
            Assert.AreEqual("Pelagia's turn", View.TurnLine.text);
        }

        [Test]
        public void OnAnEnemysTurn_TheButtonsAreHidden_AndNoTapDoesAnything()
        {
            var fight = AtP0();
            screen.BeginYourTurn(fight);
            screen.TapButton(Button.Basic);
            screen.TapHero(fight.Enemies[0]);
            var first = screen.Committed.Value;
            fight.Act(first);
            fight.AdvanceToChoice();
            Assert.AreEqual(Side.Enemy, fight.Acting.Side, "set-up: e0 is next");

            Assert.Throws<InvalidOperationException>(() => screen.BeginYourTurn(fight));
            screen.TapButton(Button.Basic);
            screen.TapHero(fight.Players[0]);
            screen.TapEmpty();
            Assert.IsNull(screen.Armed);
            Assert.AreSame(first.Target, screen.Committed.Value.Target, "nothing new committed");
            foreach (var b in View.Buttons) Assert.AreEqual(Look.Hidden, b.Look, "hidden, not greyed (P2b-04)");
            Assert.IsFalse(View.ButtonsShown);
            Assert.IsFalse(View.ButtonsTakeTaps, "no tap reaches a hidden button");
        }

        // ---- the real fight, played through the taps ---------------------------------------------------------------

        static GameData real;
        static GameData Real()
        {
            string Read(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "Data", file));
            return real ?? (real = GameData.Load(Read("heroes.json"), Read("classes.json"), Read("traits.json"),
                                                 Read("tuning.json"), Read("teams.json"), Read("formations.json")));
        }

        [Test]
        public void TheGreekStrike_PlayedThroughTheTaps_EveryShape_TheTapsGiveTheChoiceTheyWereAimedAt()
        {
            // Your side taps what EnemyAI would pick, through the screen, with exactly 17.5's taps for each shape; the
            // enemy side plays by EnemyAI. Each commit must equal the choice aimed at, no earlier tap may commit, and the
            // glow must equal Options' choosable list every time (S8). Five seeds, so all three shapes come up.
            var data = Real();
            var players = data.Teams["greek_strike"];
            var enemies = data.Teams["enemy_crew"];
            var shapes = new HashSet<string>();

            for (var seed = 1; seed <= 5; seed++)
            {
                var fight = Fight.Create(data, players, enemies, seed);
                var playback = new Playback(fight);
                View.ShowFight(fight, data.Formations[players.Formation], data.Formations[enemies.Formation]);
                playback.Apply(fight.StartEvents);
                for (var turn = 0; turn < MaxTurns && fight.Outcome == Outcome.Going; turn++)
                {
                    playback.Apply(fight.AdvanceToChoice());
                    if (fight.Acting == null) break;
                    var choice = EnemyAI.Choose(fight);
                    if (fight.Acting.Side == Side.Player)
                    {
                        screen.BeginYourTurn(fight);
                        var option = fight.Options[(int)choice.Button];
                        screen.TapButton(choice.Button);
                        Assert.IsNull(screen.Committed, $"seed {seed}: arming committed");
                        foreach (var hero in All(fight).Where(h => !h.KnockedOut))
                            Assert.AreEqual(option.Choosable.Contains(hero), F(hero).Glow.activeSelf, $"seed {seed}: {hero}'s glow");

                        var area = option.Skill.Pieces.Any(p => p.Target == "enemy_and_neighbours");
                        if (!option.NeedsChoice)
                        {
                            shapes.Add("no choice");
                            screen.TapButton(choice.Button);
                        }
                        else
                        {
                            shapes.Add(area ? "area" : "one hero");
                            screen.TapHero(choice.Target);
                            if (area)
                            {
                                Assert.IsNull(screen.Committed, $"seed {seed}: the area skill's first tap committed");
                                screen.TapHero(choice.Target);
                            }
                        }
                        Assert.IsTrue(screen.Committed.HasValue, $"seed {seed}: {fight.Acting}'s {choice.Button} never committed");
                        Assert.AreEqual(choice.Button, screen.Committed.Value.Button);
                        Assert.AreSame(option.NeedsChoice ? choice.Target : null, screen.Committed.Value.Target);
                        choice = screen.Committed.Value;
                    }
                    playback.Apply(fight.Act(choice));
                    View.SyncAfterAction(fight, playback);
                }
                Assert.AreNotEqual(Outcome.Going, fight.Outcome, $"seed {seed}: the fight should end");
            }
            CollectionAssert.AreEquivalent(new[] { "one hero", "area", "no choice" }, shapes, "all three shapes played");
        }
    }
}
