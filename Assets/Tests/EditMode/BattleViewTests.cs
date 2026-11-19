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
                    Assert.AreEqual(hero.Hero.ShortName, figure.Name.text, "the name tag says the short name (P2b-02), which is the name when the data has no short_name");
                }
        }

        [Test]
        public void TheRealFormations_EveryPair_NoTwoFiguresOverlap_AndAllStayInTheFieldClearOfTheBanner()
        {
            // S11's automated part at the 1920 x 1080 reference: a figure's footprint is its box, the icon row above it and
            // the hit chip under it (P2b-02, 17.3.1: 40 + 2 + 100 + 38 = 180 px tall).
            // P2b-02: the footprint is now 180 px tall (17.3.1's icons and hit chip), so this runs on the formations the real
            // teams use (3-2 and 2-3, rows 180 apart); 4-1 and 1-4 (rows 135 apart) go with the formations in P2b-07, and
            // R36 said the layout test runs on the real teams only until the board drawing (P2b-09). The bottom limit is the
            // bottom band's top edge (800): the hit chip may hang into the 20 px gap under the field zone (780), as it did
            // not before, but never into the band.
            const float Middle = 960, FieldTop = 190, FieldBottom = 800, BannerBottom = 240, Side = 90;
            var real = Real();
            var inUse = real.Teams.Values.Select(t => t.Formation).Distinct().ToList();
            CollectionAssert.AreEquivalent(new[] { "3-2", "2-3" }, inUse, "set-up: the real teams use these two");
            var formations = real.Formations.Values.Where(f => inUse.Contains(f.Id)).ToList();
            Rect Footprint(FormationSlotDef slot, int sign)
            {
                float x = Middle + sign * (float)slot.At[0], y = FieldTop + (float)slot.At[1];
                float top = y - HeroFigure.BoxHeight / 2 - HeroFigure.IconsRowGap - HeroFigure.IconsHeight;
                float bottom = y + HeroFigure.BoxHeight / 2 + HeroFigure.HitChipHeight;
                return new Rect(x - HeroFigure.Width / 2, top, HeroFigure.Width, bottom - top);
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

        // ---- the effect icons and the hit chip (P2b-02; 02 GDD 17.3.1, 17.6; D2: placeholders drawn by code) -------

        // A hero with these effects put on, drawn after an action as the screen draws it.
        HeroFigure WithEffects(Fight fight, params (string kind, int turns)[] effects)
        {
            var hero = fight.Players[0];
            foreach (var (kind, turns) in effects) Effects.Apply(hero, kind, turns, kind == "momentum" ? 3 : 0);
            view.SyncAfterAction(fight, new Playback(fight));
            return view.Figures[hero];
        }

        static string Shape(HeroFigure.Icon icon) => icon.Frame.sprite.texture.name;

        // A box's place in its figure's own space (x from the figure's centre, y up from it), whatever the canvas scale.
        static Rect InFigure(HeroFigure figure, RectTransform box)
        {
            var corners = new Vector3[4];
            box.GetLocalCorners(corners);
            var low = (Vector2)figure.Root.InverseTransformPoint(box.TransformPoint(corners[0]));
            var high = (Vector2)figure.Root.InverseTransformPoint(box.TransformPoint(corners[2]));
            return Rect.MinMaxRect(low.x, low.y, high.x, high.y);
        }

        [Test]
        public void EffectIcons_ShowTheMarkAndTurnsLeft_BuffsRoundGreen_BadOnesSpikedRed()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var figure = WithEffects(fight, ("offense_up", 2), ("defense_down", 1), ("stun", 1));   // stun has no turn count

            CollectionAssert.AreEqual(new[] { "offense_up", "defense_down", "stun" }, figure.Icons.Select(i => i.Kind));
            CollectionAssert.AreEqual(new[] { false, true, true }, figure.Icons.Select(i => i.Bad));
            CollectionAssert.AreEqual(new[] { "A+", "D-", "S" }, figure.Icons.Select(i => i.Mark.text), "the short marks of EffectLook");
            CollectionAssert.AreEqual(new[] { "2", "1", "" }, figure.Icons.Select(i => i.Turns.text), "turns left; none for Stun");
            Assert.AreEqual(BattleView.Green, figure.Icons[0].Frame.color, "buff: green");
            StringAssert.Contains("Rounded", Shape(figure.Icons[0]), "buff: a rounded frame");
            Assert.AreEqual(BattleView.Red, figure.Icons[1].Frame.color, "bad effect: red");
            StringAssert.Contains("Spiked", Shape(figure.Icons[1]), "bad effect: a spiked frame, a different shape");
            Assert.AreEqual(BattleView.Red, figure.Icons[2].Frame.color);
            StringAssert.Contains("Spiked", Shape(figure.Icons[2]));
            Assert.IsFalse(figure.Icons.Any(i => i.IsMore));
        }

        [Test]
        public void EffectIcons_TheTurnsLeftCountDown_AsTheHolderTakesTurns()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var hero = fight.Players[0];
            Effects.Apply(hero, "ward", 3);
            var figure = view.Figures[hero];
            figure.ShowEffects(hero.Effects, turnsStarted: 0);
            Assert.AreEqual("3", figure.Icons[0].Turns.text);
            figure.ShowEffects(hero.Effects, turnsStarted: 2);
            Assert.AreEqual("1", figure.Icons[0].Turns.text, "one more own turn to go");
        }

        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(4, 4)]     // four fit: four icons, no "+N"
        [TestCase(5, 4)]     // five: three icons and "+2" in the fourth slot
        [TestCase(8, 4)]
        public void EffectIcons_FourSlots_TheFourthReadsPlusN_WhenThereAreMore(int requested, int slots)
        {
            var effects = requested;
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var all = new[] { "offense_up", "defense_up", "taunt", "ward", "counter", "barrier", "momentum", "stun" };
            var figure = WithEffects(fight, all.Take(effects).Select(k => (k, 2)).ToArray());
            effects = fight.Players[0].Effects.Count;                    // what the rules really put on (a Barrier of 0 isn't one)

            Assert.AreEqual(slots, figure.Icons.Count);
            if (effects <= HeroFigure.MaxIcons)
            {
                Assert.IsFalse(figure.Icons.Any(i => i.IsMore));
                return;
            }
            var more = figure.Icons[HeroFigure.MaxIcons - 1];
            Assert.IsTrue(more.IsMore, "the fourth slot is the +N slot");
            Assert.AreEqual("+" + (effects - (HeroFigure.MaxIcons - 1)), more.Mark.text, "N = the effects not shown");
            Assert.AreEqual(effects, figure.Icons.Count(i => !i.IsMore) + int.Parse(more.Mark.text.Substring(1)), "icons + N = all of them");
        }

        [Test]
        public void EffectIcons_AreFortyPixelsTwoApart_AndNeverOverlapEachOther_TheNameOrTheHitChip()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var figure = WithEffects(fight, ("offense_up", 2), ("defense_up", 2), ("taunt", 2), ("ward", 2), ("counter", 2), ("stun", 1));
            Assert.AreEqual(4, figure.Icons.Count, "set-up: the longest row");

            var icons = figure.Icons.Select(i => InFigure(figure, i.Rect)).ToList();
            var name = InFigure(figure, figure.Name.rectTransform);
            var chip = InFigure(figure, figure.HitChip.GetComponent<RectTransform>());   // measured though it is hidden now

            foreach (var icon in icons) Assert.AreEqual(new Vector2(40, 40), icon.size, "17.3.1: 40 x 40");
            for (int i = 1; i < icons.Count; i++)
                Assert.AreEqual(2f, icons[i].xMin - icons[i - 1].xMax, 1e-3, "2 px apart");
            Assert.AreEqual(0f, (icons[0].xMin + icons[3].xMax) / 2, 1e-3, "the row is centred over the box");

            for (int i = 0; i < icons.Count; i++)
            {
                for (int j = i + 1; j < icons.Count; j++) Assert.IsFalse(icons[i].Overlaps(icons[j]), $"icons {i} and {j}");
                Assert.IsFalse(icons[i].Overlaps(name), $"icon {i} over the name");
                Assert.IsFalse(icons[i].Overlaps(chip), $"icon {i} over the hit chip");
                Assert.AreEqual(HeroFigure.BoxHeight / 2 + HeroFigure.IconsRowGap, icons[i].yMin, 1e-3, "the icons stand just over the box");
            }
            Assert.IsFalse(name.Overlaps(chip), "the hit chip covers no name");
            Assert.AreEqual(-HeroFigure.BoxHeight / 2, chip.yMax, 1e-3, "the chip hangs from the box's bottom edge");

            // 40 + 2 + 100 + 38: the figure's whole footprint, which the layout test counts.
            Assert.AreEqual(180f, icons.Max(i => i.yMax) - chip.yMin, 1e-3);
        }

        [Test]
        public void TheHitChip_IsUnderTheBox_156By38_WithThirtyTwoPixelText()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var figure = view.Figures[fight.Enemies[0]];
            figure.ShowChoosing(true, false, false, 85);
            var chip = figure.HitChip.GetComponent<RectTransform>();

            Assert.AreEqual(new Vector2(156, 38), chip.sizeDelta);
            Assert.AreEqual(new Vector2(0.5f, 0), chip.anchorMin, "hung from the box's bottom edge");
            Assert.AreEqual(new Vector2(0.5f, 1), chip.pivot, "its top edge on the box's bottom edge, so it hangs below");
            Assert.AreEqual(Vector2.zero, chip.anchoredPosition);
            Assert.AreEqual(32, figure.HitText.fontSize, "17.3.1: text 32 px");
            Assert.AreEqual("Hit 85 %", figure.HitText.text);
        }

        [Test]
        public void AKnockedOutHero_TakesTheIconRowWithTheBox()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var figure = WithEffects(fight, ("offense_up", 2));
            figure.ShowKnockedOut();
            Assert.IsFalse(figure.Box.activeSelf, "the icon row is part of the box and goes with it");
        }

        // ---- short names (P2b-02, 02 GDD 17.6, 17.3.1) ------------------------------------------------------------

        [Test]
        public void KalAndTalos_WearTheShortNameKal_OnTheTagAndTheTurnLine_EveryoneElseTheirName()
        {
            var data = Real();
            var greek = data.Teams["greek_strike"];
            var crew = data.Teams["enemy_crew"];
            var fight = Fight.Create(data, greek, crew, seed: 1);
            view.ShowFight(fight, data.Formations[greek.Formation], data.Formations[crew.Formation]);

            var kal = fight.Players.Concat(fight.Enemies).First(f => f.Hero.Id == "kal");
            Assert.AreEqual("Kal & Talos", kal.Hero.Name, "the full name stays in the data (hero card, cut-in)");
            Assert.AreEqual("Kal", view.Figures[kal].Name.text, "the tag");
            view.ShowActing(kal);
            Assert.AreEqual("Kal's turn", view.TurnLine.text, "the turn line");
            foreach (var hero in fight.Players.Concat(fight.Enemies).Where(f => f.Hero.Id != "kal"))
                Assert.AreEqual(hero.Hero.Name, view.Figures[hero].Name.text, hero.Hero.Id);
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

        // P2b-06 changes this test on purpose (03 8.10 names P2a-07's end-text tests): ShowEnd now also takes how many of
        // your heroes stand, the line says so, and the whole-screen "tap anywhere" box is gone: only the Play again button
        // starts a new fight (ResultTests holds the rest of the result's checks).
        [TestCase(Outcome.Won, "VICTORY")]
        [TestCase(Outcome.Lost, "DEFEAT")]
        public void TheEnd_SaysWhoWon_TheTurnsAndWhoStands_AndPlayAgainStartsANewFightInPlace(Outcome outcome, string title)
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            Assert.IsFalse(view.End.activeSelf, "no end text while the fight goes on");

            var tapped = false;
            view.ShowEnd(outcome, 57, 3, 5, () => tapped = true);
            Assert.IsTrue(view.End.activeSelf);
            Assert.AreEqual(title, view.Result.Title.text);
            Assert.AreEqual("Fight took 57 turns · 3 of 5 heroes standing", view.Result.Line.text);
            view.Result.PlayAgain.onClick.Invoke();
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

        // ---- the movements (P2b-04; 02 GDD 17.4, 17.6; 03 Architecture 8.1 A4) ----------------------------------------------
        //
        // Nothing here waits for a frame: a Mover is told how much time has passed (Advance), as Hold is told with Tick.

        static void Finish(Transform under)
        {
            foreach (var m in under.GetComponentsInChildren<Mover>(true).ToArray())
                if (m != null) m.Advance(5);
        }

        static Mover MoverOn(string name, out RectTransform rect)
        {
            var go = new GameObject(name, typeof(RectTransform));
            rect = (RectTransform)go.transform;
            return go.AddComponent<Mover>();
        }

        [Test]
        public void AMover_SlidesToItsPlace_EasedAtBothEnds_AndStopsThere()
        {
            var mover = MoverOn("slide", out var rect);
            try
            {
                mover.MoveTo(new Vector2(100, 0), 0.3f);
                Assert.IsTrue(mover.Moving);
                Assert.AreEqual(Vector2.zero, rect.anchoredPosition, "nothing moves until time passes");
                mover.Advance(0.15f);
                Assert.AreEqual(50, rect.anchoredPosition.x, 0.01f, "half way at half the time");
                mover.Advance(0.05f);
                Assert.Less(rect.anchoredPosition.x, 100);
                mover.Advance(0.1f);
                Assert.AreEqual(new Vector2(100, 0), rect.anchoredPosition);
                Assert.IsFalse(mover.Moving, "then it stops");
            }
            finally { UnityEngine.Object.DestroyImmediate(rect.gameObject); }
        }

        [Test]
        public void AMover_ANewMoveOnABoxStillMoving_StartsWhereItIs_AndEndsAtTheNewestPlace()
        {
            var mover = MoverOn("restart", out var rect);
            try
            {
                mover.MoveTo(new Vector2(100, 0), 0.3f);
                mover.Advance(0.15f);
                var mid = rect.anchoredPosition;
                mover.MoveTo(new Vector2(-40, 10), 0.3f);
                mover.Advance(0);
                Assert.AreEqual(mid, rect.anchoredPosition, "no jump: the new slide starts from where the box is");
                mover.Advance(1);
                Assert.AreEqual(new Vector2(-40, 10), rect.anchoredPosition);
                Assert.AreEqual(new Vector2(-40, 10), mover.Home);
            }
            finally { UnityEngine.Object.DestroyImmediate(rect.gameObject); }
        }

        [Test]
        public void AMover_AShakeWobblesAroundThePlace_ThenIsOver_AndAShakeDuringASlideStillEndsAtTheSlidesPlace()
        {
            var mover = MoverOn("shake", out var rect);
            try
            {
                mover.Shake(0.3f, 14);
                mover.Advance(0.025f);
                Assert.AreNotEqual(0, rect.anchoredPosition.x, "off its place mid-shake");
                Assert.AreEqual(0, rect.anchoredPosition.y, "left and right only");
                mover.Advance(1);
                Assert.AreEqual(Vector2.zero, rect.anchoredPosition, "back on its place");
                Assert.IsFalse(mover.Moving);

                mover.MoveTo(new Vector2(60, 0), 0.3f);
                mover.Shake(0.3f, 14);
                mover.Advance(1);
                Assert.AreEqual(new Vector2(60, 0), rect.anchoredPosition, "both over: the slide's place");
            }
            finally { UnityEngine.Object.DestroyImmediate(rect.gameObject); }
        }

        [Test]
        public void AMover_ThatVanishes_FadesAndRemovesItsBox_WhenTheSlideEnds()
        {
            var parent = new GameObject("parent", typeof(RectTransform));
            var mover = MoverOn("leaving", out var rect);
            rect.SetParent(parent.transform, false);
            try
            {
                mover.MoveTo(new Vector2(-50, 0), 0.4f, vanish: true);
                mover.Advance(0.2f);
                Assert.AreEqual(0.5f, rect.GetComponent<CanvasGroup>().alpha, 0.01f, "half faded at half the time");
                mover.Advance(0.3f);
                Assert.AreEqual(0, parent.transform.childCount, "gone");
            }
            finally { UnityEngine.Object.DestroyImmediate(parent); }
        }

        // ---- the bar slides (17.4) ----

        static PredictedTurn Turn(Fighter who, bool skipped = false) => new PredictedTurn(who, skipped);

        [Test]
        public void TheBar_WhenTheOrderChanges_SlidesEntriesFromWhereTheyWere_TheFirstLeaves_ANewOneArrivesOnTheRight()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var p = fight.Players;
            var e = fight.Enemies;
            view.ShowBar(new[] { Turn(p[0]), Turn(p[1]), Turn(e[0]), Turn(p[2]), Turn(e[1]) });
            Finish(view.BarRow);
            var oldX = view.Bar.ToDictionary(b => b.Who, b => b.Rect.anchoredPosition.x);
            var first = view.Bar[0].Rect.gameObject;

            // p0 acted: it leaves; the others move up one place; p3 arrives on the right.
            var after = new[] { Turn(p[1]), Turn(e[0]), Turn(p[2]), Turn(e[1]), Turn(p[3]) };
            view.ShowBar(after);

            Assert.AreEqual(5, view.Bar.Count);
            Assert.IsTrue(first != null, "the entry that acted is still on screen, on its way out");
            Assert.AreEqual(oldX[p[1]], view.Bar[0].Rect.anchoredPosition.x, "p1's entry starts where it stood");
            Assert.AreEqual(oldX[e[0]], view.Bar[1].Rect.anchoredPosition.x, "e0's entry starts where it stood");
            Assert.Greater(view.Bar[4].Rect.anchoredPosition.x, BattleView.BarX(5, 4), "p3 starts to the right of its place");
            Assert.AreEqual(6, view.BarRow.childCount, "five new entries and the one that is leaving");

            foreach (var m in view.BarRow.GetComponentsInChildren<Mover>()) Assert.IsTrue(m.Moving);
            Finish(view.BarRow);

            for (int i = 0; i < after.Length; i++)
                Assert.AreEqual(BattleView.BarX(after.Length, i), view.Bar[i].Rect.anchoredPosition.x, 0.01f, $"entry {i + 1} ends where a plain redraw puts it");
            Assert.AreEqual(5, view.BarRow.childCount, "the first one is gone and nothing else is left over");
            Assert.IsTrue(first == null);
        }

        [Test]
        public void TheBar_AKnockedOutHerosEntries_VanishWhereTheyStood_AndTheOthersCloseUp()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var p = fight.Players;
            var e = fight.Enemies;
            view.ShowBar(new[] { Turn(p[0]), Turn(e[0]), Turn(p[1]), Turn(e[0]), Turn(p[2]) });
            Finish(view.BarRow);
            var standing = view.Bar[1].Rect.gameObject;

            view.ShowBar(new[] { Turn(p[1]), Turn(p[2]), Turn(p[0]) });   // e0 fell: both its entries are gone
            Assert.IsTrue(standing != null, "its entries are still fading");
            Finish(view.BarRow);
            Assert.IsTrue(standing == null);
            Assert.AreEqual(3, view.BarRow.childCount);
            Assert.IsFalse(view.Bar.Any(b => b.Who == e[0]));
            for (int i = 0; i < 3; i++) Assert.AreEqual(BattleView.BarX(3, i), view.Bar[i].Rect.anchoredPosition.x, 0.01f);
        }

        [Test]
        public void TheBar_RedrawnWithTheSameOrder_MovesNothing()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var order = new[] { Turn(fight.Players[0]), Turn(fight.Enemies[0]), Turn(fight.Players[1]) };
            view.ShowBar(order);
            view.ShowBar(order);
            Assert.AreEqual(3, view.BarRow.childCount);
            Assert.AreEqual(0, view.BarRow.GetComponentsInChildren<Mover>().Length, "nothing to slide");
            for (int i = 0; i < 3; i++) Assert.AreEqual(BattleView.BarX(3, i), view.Bar[i].Rect.anchoredPosition.x, 0.01f);
        }

        [Test]
        public void TheBar_OverARealFight_EveryActionSlidesAndEndsExactlyWhereAPlainRedrawPutsIt()
        {
            var data = Real();
            var players = data.Teams["greek_strike"];
            var enemies = data.Teams["enemy_crew"];
            var length = (int)data.Tuning["turn_bar_length"];
            var shown = 0;
            var started = false;
            Play(data, players, enemies, seed: 1, after: (fight, batch) =>
            {
                if (!started)
                {
                    started = true;
                    view.ShowFight(fight, data.Formations[players.Formation], data.Formations[enemies.Formation]);
                }
                if (fight.Outcome != Outcome.Going) return;
                var turns = fight.PredictTurns(length);
                view.ShowBar(turns);
                Finish(view.BarRow);
                shown++;
                Assert.AreEqual(turns.Count, view.BarRow.childCount, $"after action {shown}: nothing left over");
                for (int i = 0; i < turns.Count; i++)
                {
                    Assert.AreSame(turns[i].Who, view.Bar[i].Who);
                    Assert.AreEqual(BattleView.BarX(turns.Count, i), view.Bar[i].Rect.anchoredPosition.x, 0.01f, $"after action {shown}, entry {i + 1}");
                }
            });
            Assert.Greater(shown, 20, "a real fight's worth of redraws");
        }

        // ---- the acting hero steps forward (17.6) ----

        [Test]
        public void TheActingHero_StepsThirtyPixelsTowardTheMiddle_WithTheRing_AndBackAfter_EitherSide()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            foreach (var (hero, toward) in new[] { (fight.Players[0], +1f), (fight.Enemies[0], -1f) })
            {
                var figure = view.Figures[hero];
                var rest = figure.Root.anchoredPosition;
                view.ShowActing(hero);
                Assert.IsTrue(figure.Ring.activeSelf);
                Assert.AreEqual(rest, figure.Root.anchoredPosition, "the step takes time; the ring is on at once");
                Finish(view.Field);
                Assert.AreEqual(rest + new Vector2(30 * toward, 0), figure.Root.anchoredPosition, $"{hero}: 30 px toward the middle");

                view.ShowActing(null);
                Finish(view.Field);
                Assert.AreEqual(rest, figure.Root.anchoredPosition, $"{hero}: back on its square");
                Assert.AreEqual(figure.Rest, figure.Root.anchoredPosition);
            }
        }

        [Test]
        public void TheActingHero_WhoFallsOnItsOwnTurn_GoesBackToItsSquareToo()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var hero = fight.Players[0];
            var figure = view.Figures[hero];
            view.ShowActing(hero);
            Finish(view.Field);
            figure.ShowKnockedOut();
            Finish(view.Field);
            Assert.AreEqual(figure.Rest, figure.Root.anchoredPosition);
            Assert.IsTrue(figure.EmptySlot.activeSelf);
        }

        // ---- a knockout greys and slides off (17.6) ----

        [Test]
        public void AKnockout_SlidesAGreyCopyOfTheBoxOffTowardItsOwnEdge_InItsOwnBeat_AndLeavesTheEmptySquare()
        {
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            foreach (var (hero, outward) in new[] { (fight.Players[2], -1f), (fight.Enemies[2], +1f) })
            {
                var figure = view.Figures[hero];
                figure.ShowKnockedOut();
                Assert.IsFalse(figure.Box.activeSelf, "the real box is off at once");
                var copy = figure.Root.Find("Falling box");
                Assert.IsTrue(copy != null, $"{hero}: a copy takes over the leaving");
                Assert.AreEqual(1, figure.Root.Cast<Transform>().Count(c => c.name == "Falling box"));

                figure.ShowKnockedOut();                                  // the screen calls it again after every action
                Assert.AreEqual(1, figure.Root.Cast<Transform>().Count(c => c.name == "Falling box"), "once only");

                var mover = copy.GetComponent<Mover>();
                mover.Advance(HeroFigure.FallSeconds / 2);
                var x = ((RectTransform)copy).anchoredPosition.x;
                Assert.AreEqual(outward, Mathf.Sign(x), "toward its own side's edge");
                Assert.Less(copy.GetComponent<CanvasGroup>().alpha, 1, "fading");
                mover.Advance(HeroFigure.FallSeconds);
                Assert.IsTrue(figure.Root.Find("Falling box") == null, "gone after its beat");
                Assert.IsTrue(figure.EmptySlot.activeSelf, "the square stays");
                Assert.IsFalse(figure.Box.activeSelf);
                Assert.AreEqual(figure.Rest, figure.Root.anchoredPosition, "nobody moves into it");
            }
        }

        [Test]
        public void TheKnockoutsSlide_FitsInItsBeat_AndNothingTheScreenWaitsForGrew()
        {
            Assert.LessOrEqual(HeroFigure.FallSeconds, Beat, "the slide rides the knockout's own beat (R32)");
            Assert.LessOrEqual(BattleView.BarSlideSeconds, 2 * Beat, "the bar's slide ends within the next action's beats");
            var fight = TestFight();
            Show(fight, "3-2", "3-2");
            var playback = new Playback(fight);
            var hero = fight.Enemies[0];
            view.ShowEvent(Event(FightEventKind.Damaged, hero, amount: hero.MaxHealth), playback, Beat);
            Assert.AreEqual(Beat, view.ShowEvent(Event(FightEventKind.KnockedOut, hero), playback, Beat), "the knockout still holds one beat, no more");
            Assert.IsTrue(view.Figures[hero].Root.Find("Falling box") != null);
        }
    }
}
