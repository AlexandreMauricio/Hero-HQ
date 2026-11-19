using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroHQ.Rules;
using HeroHQ.Rules.Data;
using HeroHQ.Screens;
using HeroHQ.Screens.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using static HeroHQ.Tests.TestFights;
using Button = HeroHQ.Rules.Button;

namespace HeroHQ.Tests
{
    // P2b-05 (03 Architecture 8.10; 02 GDD 17.8): the gear, the settings panel and the pause. The screen is built in
    // EditMode under a throwaway object and the taps are the screen's plain methods or the buttons' own onClick.
    // The editor's PlayerPrefs are the real ones on this PC: the setting and every "seen" flag a test may touch are kept
    // first and put back afterwards, so these tests start from "nobody seen, default setting" and leave no key behind.
    public class SettingsTests
    {
        GameObject root;
        BattleScreen screen;
        BattleView View => screen.View;
        readonly Dictionary<string, int?> saved = new Dictionary<string, int?>();

        static GameData real;
        static GameData Real()
        {
            string Read(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "Data", file));
            return real ?? (real = GameData.Load(Read("heroes.json"), Read("classes.json"), Read("traits.json"),
                                                 Read("tuning.json"), Read("teams.json"), Read("formations.json")));
        }

        static IEnumerable<string> Keys() => Real().Heroes.Keys.Concat(TestData.HeroIds)
            .Concat(new[] { "p0", "p1", "p2", "p3", "p4", "e0", "e1", "e2", "e3", "e4" })
            .Distinct().Select(id => BattleScreen.SeenKeyPrefix + id)
            .Append(BattleScreen.CutInSettingKey);

        [SetUp]
        public void BuildScreen()
        {
            saved.Clear();
            foreach (var key in Keys())
            {
                saved[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
                PlayerPrefs.DeleteKey(key);
            }
            Time.timeScale = 1;
            NewScreen();
        }

        void NewScreen()
        {
            root = new GameObject("SettingsTests");
            screen = root.AddComponent<BattleScreen>();
            screen.cutInArt = BattleSceneBuilder.CutInArt();
            screen.BuildView();
        }

        [TearDown]
        public void PutBack()
        {
            Time.timeScale = 1;
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            foreach (var pair in saved)
                if (pair.Value.HasValue) PlayerPrefs.SetInt(pair.Key, pair.Value.Value);
                else PlayerPrefs.DeleteKey(pair.Key);
            PlayerPrefs.Save();
        }

        Fight RealFight(int seed = 1)
        {
            var data = Real();
            var fight = Fight.Create(data, data.Teams["greek_strike"], data.Teams["enemy_crew"], seed);
            View.ShowFight(fight, data.Formations[data.Teams["greek_strike"].Formation], data.Formations[data.Teams["enemy_crew"].Formation]);
            return fight;
        }

        static Fighter Find(IEnumerable<Fighter> team, string id) => team.Single(f => f.Hero.Id == id);

        static FightEvent UltimateUsed(Fighter who)
        {
            var e = new FightEvent(FightEventKind.SkillUsed, who);
            typeof(FightEvent).GetProperty("Button").SetValue(e, Button.Ultimate);
            return e;
        }

        // The test heroes (TestData), as BattleTurnTests uses them: p0 acts first; Basic = one chosen enemy.
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

        // ---- the setting: full, short or off (17.8), per hero, both sides, with the "seen" flags (P2b-01) --------------

        [Test]
        public void FirstTimeFullThenShort_IsTheDefault_AndGivesFullOnceThenShort_PerHero_OnEitherSide()
        {
            Assert.AreEqual(CutInSetting.FirstTimeFullThenShort, screen.CutInSetting, "the default");
            var fight = RealFight();
            var playback = new Playback(fight);

            screen.Show(fight, playback, UltimateUsed(Find(fight.Enemies, "kal")), out var first);
            Assert.AreEqual(CutInVersion.Full, first.Clock.Version, "nobody has seen Kal");
            screen.Show(fight, playback, UltimateUsed(Find(fight.Players, "kal")), out var second);
            Assert.AreEqual(CutInVersion.Short, second.Clock.Version, "seen on the other side counts");
            screen.Show(fight, playback, UltimateUsed(Find(fight.Players, "strix")), out var third);
            Assert.AreEqual(CutInVersion.Full, third.Clock.Version, "another hero is new");
        }

        [Test]
        public void AlwaysFull_GivesTheFullOne_EveryTime_ForEveryHero_OnEitherSide()
        {
            screen.CutInSetting = CutInSetting.AlwaysFull;
            var fight = RealFight();
            var playback = new Playback(fight);
            foreach (var who in new[] { Find(fight.Players, "kal"), Find(fight.Enemies, "kal"), Find(fight.Enemies, "strix"), Find(fight.Players, "strix") })
                for (int i = 0; i < 2; i++)
                {
                    screen.Show(fight, playback, UltimateUsed(who), out var cutIn);
                    Assert.AreEqual(CutInVersion.Full, cutIn.Clock.Version, $"{who.Hero.Id} on {who.Side}, time {i + 1}");
                }
        }

        [Test]
        public void Off_GivesNoCutIn_OnlyTheOffLook_ForEveryHero_AndMarksNobodySeen()
        {
            screen.CutInSetting = CutInSetting.Off;
            var fight = RealFight();
            var playback = new Playback(fight);
            var looks = 0;
            foreach (var who in new[] { Find(fight.Players, "kal"), Find(fight.Enemies, "kal"), Find(fight.Enemies, "strix") })
            {
                var hold = screen.Show(fight, playback, UltimateUsed(who), out var cutIn);
                Assert.IsNull(cutIn, $"{who.Hero.Id} on {who.Side}");
                Assert.AreEqual(BattleView.OffLookSeconds, hold);
                Assert.AreEqual(++looks, View.OffLooks);
            }
            Assert.IsFalse(BattleScreen.HasSeen("kal"), "off never starts a full cut-in, so nobody is seen");
        }

        // ---- the choice is kept between runs --------------------------------------------------------------------------

        [Test]
        public void TheChoice_SurvivesAFreshBattleScreen_AndASillyStoredNumberReadsAsTheDefault()
        {
            screen.ChooseCutIn(CutInSetting.Off);
            Assert.AreEqual(CutInSetting.Off, screen.CutInSetting);
            Assert.AreEqual((int)CutInSetting.Off, PlayerPrefs.GetInt(BattleScreen.CutInSettingKey), "kept in PlayerPrefs");

            UnityEngine.Object.DestroyImmediate(root);
            NewScreen();
            Assert.AreEqual(CutInSetting.Off, screen.CutInSetting, "a fresh screen reads it back");

            screen.OpenSettings();
            Assert.AreEqual(CutInSetting.Off, View.Settings.Shown, "and the panel lights it");

            PlayerPrefs.SetInt(BattleScreen.CutInSettingKey, 42);
            Assert.AreEqual(CutInSetting.FirstTimeFullThenShort, screen.CutInSetting);
        }

        [Test]
        public void ThePanelsThreeButtons_ChooseTheSetting_AndLightOnlyTheChosenOne()
        {
            screen.OpenSettings();
            var panel = View.Settings;
            Assert.AreEqual(CutInSetting.FirstTimeFullThenShort, panel.Shown, "lit at the default");
            for (int i = 0; i < 3; i++)
            {
                panel.CutInButtons[i].onClick.Invoke();
                Assert.AreEqual((CutInSetting)i, screen.CutInSetting);
                Assert.AreEqual((CutInSetting)i, panel.Shown);
            }
            Assert.IsTrue(screen.SettingsOpen, "choosing doesn't close the panel");
        }

        // ---- the pause (R33) ------------------------------------------------------------------------------------------

        [Test]
        public void OpeningThePanel_PausesTheGame_AndResumeCarriesOn()
        {
            Assert.IsFalse(View.Settings.IsOpen);
            Assert.AreEqual(1f, Time.timeScale);

            screen.OpenSettings();
            Assert.IsTrue(screen.SettingsOpen);
            Assert.IsTrue(View.Settings.IsOpen);
            Assert.AreEqual(0f, Time.timeScale, "the game's clock stops: every wait, the cut-in and the slides stand still");

            screen.OpenSettings();   // the gear again changes nothing
            Assert.AreEqual(0f, Time.timeScale);

            screen.Resume();
            Assert.IsFalse(screen.SettingsOpen);
            Assert.IsFalse(View.Settings.IsOpen);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [Test]
        public void TheGearAndTheResumeButton_DoTheSame_AsTheMethods()
        {
            View.Settings.Gear.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Assert.IsTrue(screen.SettingsOpen);
            Assert.AreEqual(0f, Time.timeScale);
            View.Settings.ResumeButton.onClick.Invoke();
            Assert.IsFalse(screen.SettingsOpen);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [Test]
        public void WhileThePanelIsOpen_NoTapReachesTheFieldOrTheButtons_AndAfterResumeTheyWorkAgain()
        {
            var fight = AtP0();
            screen.BeginYourTurn(fight);
            screen.TapButton(Button.Basic);
            Assert.AreEqual(Button.Basic, screen.Armed);

            screen.OpenSettings();
            screen.TapEmpty();
            Assert.AreEqual(Button.Basic, screen.Armed, "the field's bare ground doesn't take the skill back");
            screen.TapHero(fight.Enemies[1]);
            Assert.IsNull(screen.Committed, "a hero doesn't commit");
            screen.TapButton(Button.Basic);
            Assert.AreEqual(Button.Basic, screen.Armed, "the armed button isn't tapped again");
            screen.TapButton(Button.Special);
            Assert.AreEqual(Button.Basic, screen.Armed, "another button doesn't arm");
            screen.HoldButton(Button.Ultimate);
            Assert.AreNotEqual(SkillText.Sentence(fight.Acting.Hero.Ultimate), View.InfoStrip.text, "no button hold reads its sentence");

            // And the real taps: the shade over the whole screen catches every one of them.
            Assert.IsTrue(View.Settings.Shade.raycastTarget);
            var shade = View.Settings.Shade.rectTransform;
            Assert.AreEqual(Vector2.zero, shade.anchorMin);
            Assert.AreEqual(Vector2.one, shade.anchorMax);
            Assert.AreEqual(Vector2.zero, shade.offsetMin);
            Assert.AreEqual(Vector2.zero, shade.offsetMax);

            screen.Resume();
            screen.TapHero(fight.Enemies[1]);
            Assert.AreEqual(Button.Basic, screen.Committed.Value.Button, "after Resume the tap commits as before");
            Assert.AreSame(fight.Enemies[1], screen.Committed.Value.Target);
        }

        [Test]
        public void OpeningThePanelDuringYourChoice_LeavesTheButtonsWhereTheyAre_AndResumeDoesNotSlideThemAgain()
        {
            var fight = AtP0();
            screen.BeginYourTurn(fight);
            var mover = View.ButtonsBox.GetComponent<Mover>();
            mover.Advance(1);   // they have arrived
            Assert.IsTrue(View.ButtonsShown);
            Assert.AreEqual(Vector2.zero, mover.Home);

            screen.OpenSettings();
            Assert.IsTrue(View.ButtonsShown, "still there, under the panel");
            Assert.AreEqual(Vector2.zero, mover.Home);
            Assert.IsFalse(mover.Moving);

            screen.Resume();
            Assert.IsTrue(View.ButtonsShown);
            Assert.AreEqual(Vector2.zero, mover.Home);
            Assert.IsFalse(mover.Moving, "Resume starts no slide");
            Assert.AreEqual(Vector2.zero, View.ButtonsBox.anchoredPosition);
        }

        [Test]
        public void OpeningThePanel_ClosesAHeroCardThatIsOpen()
        {
            var fight = RealFight();
            screen.HoldHero(fight.Players[0], fromBar: false);
            Assert.IsNotNull(screen.CardOf);
            screen.OpenSettings();
            Assert.IsNull(screen.CardOf);
            screen.Resume();
        }

        // ---- Quit fight (17.8) ----------------------------------------------------------------------------------------

        [Test]
        public void QuitFight_MidChoice_EndsWithNoResult_PutsTheButtonsAway_AndLeavesTheClockRunning()
        {
            var fight = AtP0();
            screen.BeginYourTurn(fight);
            screen.TapButton(Button.Basic);
            screen.OpenSettings();
            Assert.AreEqual(0f, Time.timeScale);

            View.Settings.QuitButton.onClick.Invoke();

            Assert.AreEqual(1f, Time.timeScale, "the clock runs again");
            Assert.IsFalse(screen.SettingsOpen);
            Assert.IsFalse(View.Settings.IsOpen);
            Assert.IsNull(screen.Armed);
            Assert.IsNull(screen.Committed);
            Assert.IsFalse(View.End.activeSelf, "no result panel");
            Assert.IsFalse(View.ButtonsShown, "the buttons start hidden until the next fight's first choice");
            Assert.IsFalse(View.ButtonsTakeTaps);
            Assert.AreEqual(new Vector2(0, -BattleView.ButtonHideDrop), View.ButtonsBox.anchoredPosition, "away at once, not sliding");

            screen.TapButton(Button.Basic);
            Assert.IsNull(screen.Armed, "with no fight in hand nothing can be tapped");
        }

        [Test]
        public void QuitFight_MidCutIn_RemovesTheCutIn_StartsANewFight_AndLeavesTheClockRunning()
        {
            screen.Data = Real();
            var oldFight = RealFight();
            var playback = new Playback(oldFight);
            screen.Show(oldFight, playback, UltimateUsed(Find(oldFight.Players, "kal")), out var cutIn);
            Assert.IsNotNull(cutIn);
            Assert.IsNotNull(View.Canvas.transform.Find("Cut-in"), "set-up: the cut-in is on screen");
            View.ShowBanner("Strix uses Mark");
            screen.OpenSettings();

            screen.QuitFight();

            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsNull(View.Canvas.transform.Find("Cut-in"), "no cut-in left behind");
            Assert.AreEqual(0, View.Banner.childCount, "the banner is gone");
            Assert.IsFalse(screen.SettingsOpen);
            Assert.IsFalse(View.End.activeSelf);
            Assert.AreNotSame(oldFight, View.Fight, "a new fight is on the field");
            Assert.IsFalse(View.ButtonsShown);
            Assert.AreEqual(new Vector2(0, -BattleView.ButtonHideDrop), View.ButtonsBox.anchoredPosition);
        }

        // ---- the gear and the panel as drawn (17.8; S12) --------------------------------------------------------------

        [Test]
        public void TheGear_IsInTheTopStripsRightCorner_120Px_ClearOfTheTurnBar_AndDrawsAboveACutIn()
        {
            var gear = View.Settings.Gear;
            Assert.AreSame(View.TopStrip, gear.parent, "in the top strip");
            Assert.AreEqual(new Vector2(SettingsPanel.GearSize, SettingsPanel.GearSize), gear.sizeDelta);
            Assert.AreEqual(120f, SettingsPanel.GearSize);
            Assert.AreEqual(Vector2.one, gear.anchorMin, "right corner, flush with the strip's right edge");
            Assert.AreEqual(Vector2.one, gear.anchorMax);
            Assert.AreEqual(0f, gear.anchoredPosition.x);
            Assert.GreaterOrEqual(gear.anchoredPosition.y, -146 + SettingsPanel.GearSize - 1, "inside the 146 px strip: top edge at or under the strip's top");
            Assert.LessOrEqual(gear.anchoredPosition.y - SettingsPanel.GearSize, 0f);
            // 120 px is what 02 GDD 17.3 and 03 A5 set (the strip is 146 px tall), under 17.1's 144 px for a must-press button: reading R43.

            // The strip is 1740 wide (1920 less 90 px each side), so its right half runs 870 from the middle; the bar is
            // centred, and its eighth entry, or one arriving from the right, ends well short of the gear's left edge.
            const float stripHalf = (1920 - 2 * 90) / 2f, entry = 120, gap = 12;
            var gearLeft = stripHalf - SettingsPanel.GearSize;
            var barRight = BattleView.BarX(8, 7) + entry / 2;
            var arrivingRight = BattleView.BarX(8, 7) + entry + gap + entry / 2;
            Assert.Less(barRight, gearLeft);
            Assert.Less(arrivingRight, gearLeft, "even an entry sliding in from the right");

            // A cut-in is made later and covers the whole canvas and takes every tap; the gear and the panel carry their
            // own sorting so the gear stays pressable and the panel draws over a cut-in already playing.
            var gearCanvas = gear.GetComponent<Canvas>();
            Assert.IsTrue(gearCanvas.overrideSorting);
            Assert.Greater(gearCanvas.sortingOrder, 0);
            Assert.IsNotNull(gear.GetComponent<GraphicRaycaster>());
            var panelCanvas = View.Settings.Panel.GetComponent<Canvas>();
            Assert.IsTrue(panelCanvas.overrideSorting);
            Assert.Greater(panelCanvas.sortingOrder, gearCanvas.sortingOrder, "the panel is over the gear");
            Assert.AreSame(View.Canvas.transform, View.Settings.Panel.transform.parent, "under the canvas, not the safe area, so it covers the notch too");
        }

        [Test]
        public void ThePanel_HasTheThreeChoices_ResumeAndQuit_EveryPressAtLeast144Px_AndEveryTextAtLeast40Px()
        {
            var panel = View.Settings;
            var presses = panel.CutInButtons.Concat(new[] { panel.ResumeButton, panel.QuitButton }).ToList();
            Assert.AreEqual(5, presses.Count);
            foreach (var b in presses)
            {
                var size = ((RectTransform)b.transform).sizeDelta;
                Assert.GreaterOrEqual(size.x, SettingsPanel.MinPress, b.name);
                Assert.GreaterOrEqual(size.y, SettingsPanel.MinPress, b.name);
            }
            var texts = panel.CutInTexts.Concat(new[] { panel.Title, panel.CutInsLabel, panel.PausedNote, panel.ResumeText, panel.QuitText });
            foreach (var t in texts) Assert.GreaterOrEqual(t.fontSize, 40, t.text);

            Assert.AreEqual("Resume", panel.ResumeText.text);
            Assert.AreEqual("Quit fight", panel.QuitText.text);
            Assert.AreEqual("Cut-ins", panel.CutInsLabel.text);
            StringAssert.Contains("First time full", panel.CutInTexts[0].text);
            Assert.AreEqual("Always full", panel.CutInTexts[1].text);
            Assert.AreEqual("Off", panel.CutInTexts[2].text);

            // Nothing sticks out of the box, and the choices don't overlap each other.
            var box = panel.Box.sizeDelta;
            var rects = presses.Select(b => (RectTransform)b.transform).ToList();
            foreach (var r in rects)
            {
                Assert.GreaterOrEqual(r.anchoredPosition.x, 0);
                Assert.LessOrEqual(r.anchoredPosition.x + r.sizeDelta.x, box.x);
                Assert.LessOrEqual(-r.anchoredPosition.y + r.sizeDelta.y, box.y);
            }
            for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 5; j++)
                {
                    var a = rects[i];
                    var c = rects[j];
                    var apart = a.anchoredPosition.x + a.sizeDelta.x <= c.anchoredPosition.x || c.anchoredPosition.x + c.sizeDelta.x <= a.anchoredPosition.x
                             || -a.anchoredPosition.y + a.sizeDelta.y <= -c.anchoredPosition.y || -c.anchoredPosition.y + c.sizeDelta.y <= -a.anchoredPosition.y;
                    Assert.IsTrue(apart, $"{a.name} and {c.name}");
                }
        }
    }
}
