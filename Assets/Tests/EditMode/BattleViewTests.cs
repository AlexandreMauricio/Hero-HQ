using System;
using System.Collections.Generic;
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
    // P2a-07 (03 Architecture 7.7): the field and the turn bar BattleView draws while the fight plays itself
    // (02 GDD 17.3, 17.4, 17.6). Everything is built in EditMode under a throwaway object; nothing is played.
    public class BattleViewTests
    {
        GameObject root;
        BattleView view;

        [SetUp]
        public void BuildView()
        {
            root = new GameObject("BattleViewTests");
            view = new BattleView();
            view.Build(root.transform);
        }

        [TearDown]
        public void DestroyView() => UnityEngine.Object.DestroyImmediate(root);

        static GameData real;
        static GameData Real()
        {
            string Read(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "Data", file));
            return real ?? (real = GameData.Load(Read("heroes.json"), Read("classes.json"), Read("traits.json"),
                                                 Read("tuning.json"), Read("teams.json"), Read("formations.json")));
        }

        static Fight TestFight(string playerFormation = "3-2", string enemyFormation = "3-2") => NewFight(
            new[] { ("p0", 100), ("p1", 120), ("p2", 90), ("p3", 80), ("p4", 70) },
            new[] { ("e0", 110), ("e1", 60), ("e2", 50), ("e3", 40), ("e4", 30) },
            playerFormation: playerFormation, enemyFormation: enemyFormation);

        void Show(Fight fight, string playerFormation, string enemyFormation) =>
            view.ShowFight(fight, fight.Data.Formations[playerFormation], fight.Data.Formations[enemyFormation]);

        // ---- the field (17.3, 17.6) ----------------------------------------------------------------------------

        [TestCase("4-1", "1-4")]   // 17.3's worst case
        [TestCase("1-4", "4-1")]
        [TestCase("3-2", "2-3")]
        [TestCase("2-3", "3-2")]
        public void TenFigures_StandAtTheirFormationsAt_YoursOnTheLeft_TheRivalsMirrored(string mine, string theirs)
        {
            var fight = TestFight(mine, theirs);
            Show(fight, mine, theirs);

            Assert.AreEqual(10, view.Figures.Count);
            foreach (var (team, formation, sign) in new[] { (fight.Players, mine, -1), (fight.Enemies, theirs, +1) })
                foreach (var hero in team)
                {
                    var figure = view.Figures[hero];
                    var at = fight.Data.Formations[formation].Slots[hero.Slot].At;
                    Assert.AreSame(view.Field, figure.Root.parent, "figures stand in the field zone");
                    Assert.AreEqual(new Vector2(sign * (float)at[0], -(float)at[1]), figure.Root.anchoredPosition,
                                    $"{hero}: x from the middle outward, y down from the field's top (7.1)");
                    Assert.AreEqual(hero.Hero.Name, figure.Name.text, "the name in full (short names are 2b)");
                }
        }

        [Test]
        public void TheRealFormations_EveryPair_NoTwoFiguresOverlap_AndAllStayInTheFieldClearOfTheBanner()
        {
            // S11's automated part at the 1920 x 1080 reference: a figure's footprint is its box plus the chip row above.
            const float Middle = 960, FieldTop = 190, FieldBottom = 780, BannerBottom = 240, Side = 90;
            var formations = Real().Formations.Values.ToList();
            Rect Footprint(FormationSlotDef slot, int sign)
            {
                float x = Middle + sign * (float)slot.At[0], y = FieldTop + (float)slot.At[1];
                float top = y - HeroFigure.BoxHeight / 2 - HeroFigure.ChipsGap - HeroFigure.ChipsHeight;
                return new Rect(x - HeroFigure.Width / 2, top, HeroFigure.Width, y + HeroFigure.BoxHeight / 2 - top);
            }

            foreach (var mine in formations)
                foreach (var theirs in formations)
                {
                    var rects = mine.Slots.Select(s => Footprint(s, -1)).Concat(theirs.Slots.Select(s => Footprint(s, +1))).ToList();
                    var pair = $"{mine.Id} against {theirs.Id}";
                    foreach (var r in rects)
                    {
                        Assert.GreaterOrEqual(r.yMin, BannerBottom, $"{pair}: a figure under the banner (R20) {r}");
                        Assert.LessOrEqual(r.yMax, FieldBottom, $"{pair}: a figure over the bottom band {r}");
                        Assert.GreaterOrEqual(r.xMin, Side, $"{pair}: a figure in the left margin {r}");
                        Assert.LessOrEqual(r.xMax, 1920 - Side, $"{pair}: a figure in the right margin {r}");
                    }
                    for (int i = 0; i < rects.Count; i++)
                        for (int j = i + 1; j < rects.Count; j++)
                            Assert.IsFalse(rects[i].Overlaps(rects[j]), $"{pair}: figures {i + 1} and {j + 1} overlap");
                }
        }

        [Test]
        public void AFigure_ShowsHealthFromPlayback_AndAKnockedOutOne_GreysAndItsSlotStaysEmpty_OverARealFight()
        {
            // The Greek strike against the crew, as the screen will play it: Health and charge drawn from Playback, the
            // effects from the fighters, after every batch (7.2, finding 3).
            var data = Real();
            var players = data.Teams["greek_strike"];
            var enemies = data.Teams["enemy_crew"];
            Playback playback = null;
            var at = new Dictionary<Fighter, Vector2>();
            var knockedOut = new HashSet<Fighter>();

            Play(data, players, enemies, seed: 1, after: (fight, batch) =>
            {
                if (playback == null)
                {
                    playback = new Playback(fight);
                    view.ShowFight(fight, data.Formations[players.Formation], data.Formations[enemies.Formation]);
                    foreach (var f in view.Figures) at[f.Key] = f.Value.Root.anchoredPosition;
                }
                playback.Apply(batch);
                view.SyncAfterAction(fight, playback);

                foreach (var (hero, figure) in view.Figures.Select(p => (p.Key, p.Value)))
                {
                    Assert.AreEqual(at[hero], figure.Root.anchoredPosition, $"{hero}: nobody moves (a fallen hero leaves a gap)");
                    var down = playback.Health(hero) == 0;
                    Assert.AreEqual(down, figure.KnockedOut, $"{hero}: knocked out exactly when the shown Health is 0 (R22)");
                    Assert.AreEqual(!down, figure.Box.activeSelf, $"{hero}: the box goes when the hero falls");
                    Assert.IsTrue(figure.EmptySlot.activeSelf, $"{hero}: the slot's grey frame always stays");
                    if (down) { knockedOut.Add(hero); continue; }
                    Assert.AreEqual(playback.Health(hero).ToString(), figure.HealthNumber.text, $"{hero}: Health from Playback");
                    Assert.AreEqual(playback.Charge(hero) / 100, figure.ChargeFill.anchorMax.x, 1e-4, $"{hero}: charge from Playback");
                }
            });

            Assert.GreaterOrEqual(knockedOut.Count, 5, "one whole side fell");
        }

        [Test]
        public void TheHealthBar_ShowsTheNumber_AndABarrierAsALightBlueStretchAtItsEnd()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var figure = view.Figures[fight.Players[0]];

            figure.ShowHealth(600, 1000, 0);
            Assert.AreEqual("600", figure.HealthNumber.text);
            Assert.AreEqual(0.6f, figure.HealthFill.anchorMax.x, 1e-4);
            Assert.IsFalse(figure.BarrierFill.gameObject.activeSelf, "no Barrier, no stretch");

            figure.ShowHealth(1000, 1000, 250);   // full Health plus a Barrier: the bar counts both, 1250
            Assert.AreEqual(0.8f, figure.HealthFill.anchorMax.x, 1e-4);
            Assert.IsTrue(figure.BarrierFill.gameObject.activeSelf);
            Assert.AreEqual(0.8f, figure.BarrierFill.anchorMin.x, 1e-4, "the stretch starts where Health ends");
            Assert.AreEqual(1f, figure.BarrierFill.anchorMax.x, 1e-4);
        }

        [Test]
        public void TheChargeBar_IsGold_AndCarriesUltAt100()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            foreach (var figure in new[] { view.Figures[fight.Players[0]], view.Figures[fight.Enemies[0]] })   // both sides
            {
                figure.ShowCharge(62.5);
                Assert.AreEqual(0.625f, figure.ChargeFill.anchorMax.x, 1e-4);
                Assert.IsFalse(figure.UltTag.activeSelf);
                figure.ShowCharge(100);
                Assert.AreEqual(1f, figure.ChargeFill.anchorMax.x, 1e-4);
                Assert.IsTrue(figure.UltTag.activeSelf, "a rival ultimate you can see coming (17.6)");
            }
        }

        [Test]
        public void EffectChips_SayTheEffectAndTurnsLeft_BuffsGreen_BadOnesRed_FourThenPlusN()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var hero = fight.Players[0];
            Effects.Apply(hero, "offense_up", 2);
            Effects.Apply(hero, "defense_down", 1);
            Effects.Apply(hero, "stun", 1);                       // no turn count: it ends with the hero's next turn
            view.SyncAfterAction(fight, new Playback(fight));
            var figure = view.Figures[hero];

            CollectionAssert.AreEqual(new[] { "Offense Up 2", "Defense Down 1", "Stun" }, figure.Chips.Select(c => c.Label.text));
            CollectionAssert.AreEqual(new[] { false, true, true }, figure.Chips.Select(c => c.Bad));
            Assert.AreEqual(BattleView.Green, figure.Chips[0].Frame.color, "buff: green frame");
            Assert.IsNotNull(figure.Chips[0].Frame.sprite, "buff: the rounded frame");
            Assert.AreEqual(BattleView.Red, figure.Chips[1].Frame.color, "bad effect: red frame");
            Assert.IsNull(figure.Chips[1].Frame.sprite, "bad effect: a square frame, a different shape");

            Effects.Apply(hero, "taunt", 3);
            Effects.Apply(hero, "counter", 2);
            Effects.Apply(hero, "momentum", 0, amount: 1);
            view.SyncAfterAction(fight, new Playback(fight));
            CollectionAssert.AreEqual(new[] { "Offense Up 2", "Defense Down 1", "Stun", "Taunt 3", "+2" },
                                      figure.Chips.Select(c => c.Label.text), "up to four, then +N (17.6)");
        }

        [Test]
        public void TheActingRing_IsOnTheActingHeroOnly()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            fight.AdvanceToChoice();
            view.ShowActing(fight.Acting);
            foreach (var f in view.Figures)
                Assert.AreEqual(f.Key == fight.Acting, f.Value.Ring.activeSelf, f.Key.ToString());
            view.ShowActing(null);
            Assert.IsTrue(view.Figures.Values.All(f => !f.Ring.activeSelf));
        }

        // ---- the turn bar (17.4) -------------------------------------------------------------------------------

        [Test]
        public void TheBar_ShowsPredictTurnsInOrder_NowFirstAndLarger_AStunnedTurnDimmedAndStruckThrough()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            fight.AdvanceToChoice();
            Effects.Apply(fight.Enemies[0], "stun", 1);          // e0 (Speed 110) acts soon: its turn is skipped
            var length = (int)fight.Data.Tuning["turn_bar_length"];
            var predicted = fight.PredictTurns(length);
            Assume.That(predicted.Any(t => t.Skipped), "the test needs a skipped turn in the bar");

            view.ShowBar(predicted);
            view.ShowBar(predicted);                              // redrawn after every action: the old entries go

            Assert.AreEqual(length, view.Bar.Count);
            Assert.AreEqual(length, view.BarRow.childCount, "no entries left over");
            for (int i = 0; i < length; i++)
            {
                var entry = view.Bar[i];
                Assert.AreSame(predicted[i].Who, entry.Who, $"entry {i + 1}");
                Assert.AreEqual(predicted[i].Skipped, entry.Skipped, $"entry {i + 1}");
                Assert.AreEqual(predicted[i].Skipped, entry.Strike.activeSelf, $"entry {i + 1}: struck through when skipped");
                Assert.AreEqual(predicted[i].Skipped, entry.Dimmed, $"entry {i + 1}: dimmed when skipped");
                Assert.AreEqual(i == 0, entry.Now, $"entry {i + 1}: NOW is the first");
                Assert.AreEqual(i == 0 ? 140f : 120f, entry.Rect.sizeDelta.x, $"entry {i + 1}: NOW larger");
                Assert.AreEqual(entry.Who.Side == Side.Enemy, entry.Diamond, $"entry {i + 1}: blue round or red diamond badge");
                Assert.AreEqual(entry.Who.Hero.Name.Substring(0, 2), entry.Initials.text);
            }
            for (int i = 1; i < length; i++)
                Assert.Greater(view.Bar[i].Rect.anchoredPosition.x, view.Bar[i - 1].Rect.anchoredPosition.x, "next to act on the left");
        }

        // ---- the seed and the end ------------------------------------------------------------------------------

        [Test]
        public void TheSeed_IsPrintedSmallInACorner()
        {
            view.ShowSeed(123456);
            Assert.AreEqual("Seed 123456", view.Seed.text);
        }

        [TestCase(Outcome.Won, "VICTORY")]
        [TestCase(Outcome.Lost, "DEFEAT")]
        public void TheEnd_SaysWhoWon_AndTheTurns_AndATapStartsANewFightInPlace(Outcome outcome, string title)
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            Assert.IsFalse(view.End.activeSelf, "no end text while the fight goes on");

            var tapped = false;
            view.ShowEnd(outcome, 57, () => tapped = true);
            Assert.IsTrue(view.End.activeSelf);
            Assert.AreEqual(title, view.EndTitle.text);
            Assert.AreEqual("Fight took 57 turns", view.EndLine.text);
            view.EndButton.onClick.Invoke();
            Assert.IsTrue(tapped);

            var again = TestFight();
            Show(again, "3-2", "3-2");
            Assert.IsFalse(view.End.activeSelf, "a new fight hides the end text");
            Assert.AreEqual(10, view.Field.childCount, "the old figures are gone");
            Assert.IsTrue(view.Figures.Keys.All(f => again.Players.Contains(f) || again.Enemies.Contains(f)));
        }

        // ---- the show (P2a-08; 02 GDD 17.6, 17.7) --------------------------------------------------------------

        // FightEvent's detail fields have internal setters (only the rules write them), so a hand-made event sets them by reflection.
        static FightEvent Event(FightEventKind kind, Fighter who, string effect = null, int amount = 0, int soaked = 0,
                                bool crit = false, HeroHQ.Rules.Button? button = null)
        {
            var e = new FightEvent(kind, who, effect);
            void Set(string name, object value) => typeof(FightEvent).GetProperty(name).SetValue(e, value);
            Set("Amount", amount);
            Set("Soaked", soaked);
            Set("Crit", crit);
            if (button != null) Set("Button", button);
            return e;
        }

        const float Beat = 0.4f;

        static void AssertNumber(FightEvent e, string text, Color color, int size, string why)
        {
            var number = BattleView.NumberFor(e);
            Assert.IsTrue(number.HasValue, why);
            Assert.AreEqual(text, number.Value.Text, why);
            Assert.AreEqual(color, number.Value.Color, why);
            Assert.AreEqual(size, number.Value.Size, why);
        }

        [Test]
        public void TheFloatingNumbers_EachKindSaysItsWordsInItsColour()
        {
            var who = TestFight().Players[0];
            // 17.6: white 64 damage; CRIT! yellow and bigger; MISS grey; + green; Barrier soak blue; WARDED; COUNTER!.
            AssertNumber(Event(FightEventKind.Damaged, who, amount: 182), "182", Color.white, 64, "a hit");
            AssertNumber(Event(FightEventKind.Damaged, who, amount: 182, crit: true), "CRIT!\n182", BattleView.Yellow, 88, "a critical hit");
            AssertNumber(Event(FightEventKind.Damaged, who, amount: 100, soaked: 100), "100", BattleView.LightBlue, 64, "all of it soaked by a Barrier");
            AssertNumber(Event(FightEventKind.Damaged, who, amount: 150, soaked: 100), "150", Color.white, 64, "partly soaked: the Health bar shows the rest");
            AssertNumber(Event(FightEventKind.Missed, who), "MISS", BattleView.MissGrey, 64, "a miss");
            AssertNumber(Event(FightEventKind.Healed, who, amount: 120), "+120", BattleView.Green, 64, "a heal");
            AssertNumber(Event(FightEventKind.EffectBlocked, who, "stun"), "WARDED", Color.white, 56, "a Ward stopping an effect");
            AssertNumber(Event(FightEventKind.CounterAttack, who), "COUNTER!", Color.white, 56, "a counter-attack");
            AssertNumber(Event(FightEventKind.EffectApplied, who, "offense_up"), "Offense Up", BattleView.Green, 44, "a buff put on (reading R26)");
            AssertNumber(Event(FightEventKind.EffectApplied, who, "defense_down"), "Defense Down", BattleView.Red, 44, "a bad effect put on (reading R26)");
        }

        [Test]
        public void EveryOtherKind_HasNoFloatingNumber()
        {
            var who = TestFight().Players[0];
            var shown = new[] { FightEventKind.Damaged, FightEventKind.Missed, FightEventKind.Healed, FightEventKind.EffectBlocked,
                                FightEventKind.CounterAttack, FightEventKind.EffectApplied };
            foreach (FightEventKind kind in Enum.GetValues(typeof(FightEventKind)))
                if (!shown.Contains(kind))
                    Assert.IsFalse(BattleView.NumberFor(Event(kind, who, "stun")).HasValue, $"{kind} shows no number over the hero");
        }

        [Test]
        public void AHit_PutsItsNumberOverTheHero_MovesTheHealthBarFromPlayback_AndHoldsOneBeat()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var playback = new Playback(fight);
            var hero = fight.Players[1];
            var figure = view.Figures[hero];

            var hold = view.ShowEvent(Event(FightEventKind.Damaged, hero, amount: 40), playback, Beat);

            Assert.AreEqual(Beat, hold, "a hit is one beat");
            Assert.AreEqual("40", view.LastNumber.text);
            Assert.AreSame(view.Field, view.LastNumber.rectTransform.parent, "the number lives in the field, so a new fight clears it");
            Assert.AreEqual(figure.Root.anchoredPosition.x, view.LastNumber.rectTransform.anchoredPosition.x, 0.01f, "over the hero");
            Assert.AreEqual((hero.MaxHealth - 40).ToString(), figure.HealthNumber.text, "Health from Playback, not from the fighter");
        }

        [Test]
        public void ABookkeepingEvent_ChangesTheBarAtOnce_HoldsNothing_AndShowsNoNumber()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var playback = new Playback(fight);
            var hero = fight.Enemies[2];

            var charged = new FightEvent(FightEventKind.ChargeChanged, hero);
            typeof(FightEvent).GetProperty("Charge").SetValue(charged, 55.0);
            Assert.AreEqual(0f, view.ShowEvent(charged, playback, Beat), "nothing to wait for");
            Assert.AreEqual(0.55f, view.Figures[hero].ChargeFill.anchorMax.x, 1e-4, "the charge bar moved now");
            Assert.IsNull(view.LastNumber, "no number for bookkeeping");
        }

        [Test]
        public void ALethalHit_EmptiesTheBarFirst_AndTheBoxGoesOnTheKnockoutBeat()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var playback = new Playback(fight);
            var hero = fight.Enemies[0];
            var figure = view.Figures[hero];

            view.ShowEvent(Event(FightEventKind.Damaged, hero, amount: hero.MaxHealth), playback, Beat);
            Assert.AreEqual("0", figure.HealthNumber.text);
            Assert.IsTrue(figure.Box.activeSelf, "still standing on the hit's own beat");

            var hold = view.ShowEvent(Event(FightEventKind.KnockedOut, hero), playback, Beat);
            Assert.AreEqual(Beat, hold);
            Assert.IsFalse(figure.Box.activeSelf, "the box goes on the knockout beat");
            Assert.IsTrue(figure.EmptySlot.activeSelf, "its slot stays");
        }

        [Test]
        public void TheBanner_SaysWhatAnEnemyDoes_AndAStunnedTurnIsSkippedForEitherSide()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var playback = new Playback(fight);

            view.ShowBanner("Strix uses Mark");
            Assert.AreEqual("Strix uses Mark", view.BannerText.text);
            Assert.IsTrue(view.BannerText.transform.IsChildOf(view.Banner), "in the banner zone (R20)");

            foreach (var hero in new[] { fight.Players[0], fight.Enemies[0] })
            {
                var hold = view.ShowEvent(Event(FightEventKind.TurnSkipped, hero), playback, Beat);
                Assert.AreEqual(BattleView.BannerSeconds, hold, "0.8 s");
                Assert.AreEqual($"{hero.Hero.Name} is stunned: turn skipped", view.BannerText.text);
            }
        }

        [Test]
        public void AnUltimate_ShowsTheOffLook_WithTheHeroesShout_ForThreeTenthsOfASecond_AndOtherSkillsDoNot()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var playback = new Playback(fight);
            var hero = fight.Players[0];

            Assert.AreEqual(0f, view.ShowEvent(Event(FightEventKind.SkillUsed, hero, button: HeroHQ.Rules.Button.Basic), playback, Beat));
            Assert.AreEqual(0f, view.ShowEvent(Event(FightEventKind.SkillUsed, hero, button: HeroHQ.Rules.Button.Special), playback, Beat));
            Assert.AreEqual(0, view.OffLooks, "only an ultimate gets the look");

            var hold = view.ShowEvent(Event(FightEventKind.SkillUsed, hero, button: HeroHQ.Rules.Button.Ultimate), playback, Beat);
            Assert.AreEqual(BattleView.OffLookSeconds, hold, "0.1 s flash then the band, 0.3 s in all");
            Assert.AreEqual(1, view.OffLooks);
            Assert.AreEqual(hero.Hero.Cutin.Shout, view.LastShout);
            Assert.AreEqual(0.1f, BattleView.FlashSeconds);
        }

        [Test]
        public void OverRealFights_TheOffLookComesBeforeTheUltimatesFirstHit_AndEveryEventHoldsTheRightTime()
        {
            var data = Real();
            var players = data.Teams["greek_strike"];
            var enemies = data.Teams["enemy_crew"];
            var ultimates = 0;
            var expectedLooks = 0;

            foreach (var seed in new[] { 1, 2, 3, 4, 5 })
            {
                Playback playback = null;
                Play(data, players, enemies, seed, after: (fight, batch) =>
                {
                    if (playback == null)
                    {
                        playback = new Playback(fight);
                        view.ShowFight(fight, data.Formations[players.Formation], data.Formations[enemies.Formation]);
                    }
                    var ultimateAction = batch.Count > 0 && batch[0].Kind == FightEventKind.SkillUsed && batch[0].Button == HeroHQ.Rules.Button.Ultimate;
                    foreach (var e in batch)
                    {
                        var hold = view.ShowEvent(e, playback, Beat);
                        if (e.Kind == FightEventKind.SkillUsed && e.Button == HeroHQ.Rules.Button.Ultimate)
                        {
                            expectedLooks++;
                            ultimates++;
                            Assert.AreEqual(BattleView.OffLookSeconds, hold, e.ToString());
                        }
                        else if (e.Kind == FightEventKind.TurnSkipped) Assert.AreEqual(BattleView.BannerSeconds, hold, e.ToString());
                        else Assert.AreEqual(Playback.GetsBeat(e.Kind) ? Beat : 0f, hold, e.ToString());

                        if (ultimateAction && (e.Kind == FightEventKind.Damaged || e.Kind == FightEventKind.Missed))
                            Assert.AreEqual(expectedLooks, view.OffLooks, $"the off look is already on screen at this hit: {e}");
                    }
                });
                // Every event shown: the Health on screen equals the fighters' own for everyone still standing (finding 3, end of fight).
                foreach (var pair in view.Figures.Where(p => !p.Key.KnockedOut))
                    Assert.AreEqual(pair.Key.Health.ToString(), pair.Value.HealthNumber.text, $"seed {seed}: {pair.Key}");
            }
            Assert.Greater(ultimates, 0, "the five fights fire at least one ultimate");
            Assert.AreEqual(expectedLooks, view.OffLooks);
        }

        [Test]
        public void AnEnemyAction_ShowsTheBannerAndOutlinesItsTarget_AndASkillWithNoTapOutlinesNobody()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var enemy = fight.Enemies[0];
            var target = fight.Players[2];

            view.ShowEnemyAction(enemy, new Choice(HeroHQ.Rules.Button.Special, target));
            Assert.AreEqual($"{enemy.Hero.Name} uses {enemy.Hero.Special.Name}", view.BannerText.text);
            foreach (var pair in view.Figures)
                Assert.AreEqual(pair.Key == target, pair.Value.TargetRing.activeSelf, pair.Key.ToString());

            view.ClearAim();
            Assert.IsTrue(view.Figures.Values.All(f => !f.TargetRing.activeSelf));

            view.ShowEnemyAction(enemy, new Choice(HeroHQ.Rules.Button.Ultimate, null));
            Assert.AreEqual($"{enemy.Hero.Name} uses {enemy.Hero.Ultimate.Name}", view.BannerText.text);
            Assert.IsTrue(view.Figures.Values.All(f => !f.TargetRing.activeSelf), "nothing to aim at");
        }

        [Test]
        public void TheEnemysTarget_GetsARedOutline_ThatGoesAgain()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var figure = view.Figures[fight.Players[0]];
            Assert.IsFalse(figure.TargetRing.activeSelf, "off to start with");
            figure.ShowTargeted(true);
            Assert.IsTrue(figure.TargetRing.activeSelf);
            Assert.AreEqual(BattleView.Red, figure.TargetRing.GetComponent<UnityEngine.UI.Image>().color);
            figure.ShowTargeted(false);
            Assert.IsFalse(figure.TargetRing.activeSelf);
        }
    }
}
