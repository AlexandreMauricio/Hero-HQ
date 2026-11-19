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
    // P2b-06 (03 Architecture 8.10; 02 GDD 17.8; R34): the result screen and Play again. The screen is built in EditMode
    // under a throwaway object; time moves by ResultPanel.Advance, the taps are the buttons' own onClick. P2a-07's one
    // end-text test (BattleViewTests.TheEnd_...) was changed on purpose for the new ShowEnd and line; the two
    // SettingsTests lines that read View.End still pass as written.
    public class ResultTests
    {
        GameObject root;
        BattleScreen screen;
        BattleView View => screen.View;

        static GameData real;
        static GameData Real()
        {
            string Read(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "Data", file));
            return real ?? (real = GameData.Load(Read("heroes.json"), Read("classes.json"), Read("traits.json"),
                                                 Read("tuning.json"), Read("teams.json"), Read("formations.json")));
        }

        [SetUp]
        public void BuildScreen()
        {
            root = new GameObject("ResultTests");
            screen = root.AddComponent<BattleScreen>();
            screen.cutInArt = BattleSceneBuilder.CutInArt();
            screen.BuildView();
        }

        [TearDown]
        public void Destroy() => Object.DestroyImmediate(root);

        // Your heroes p0..p4 against e0..e4; p0 is choosing, so the screen holds the fight.
        Fight AtP0()
        {
            var fight = NewFight(
                new[] { ("p0", 130), ("p1", 100), ("p2", 90), ("p3", 80), ("p4", 70) },
                new[] { ("e0", 110), ("e1", 60), ("e2", 50), ("e3", 40), ("e4", 30) });
            View.ShowFight(fight, fight.Data.Formations["3-2"], fight.Data.Formations["3-2"]);
            fight.AdvanceToChoice();
            screen.BeginYourTurn(fight);
            return fight;
        }

        static void Decide(Fight fight, Outcome outcome) =>
            typeof(Fight).GetProperty("Outcome").SetValue(fight, outcome);

        // ---- the slam, in the cut-in's lettering ------------------------------------------------------------------------

        [TestCase(Outcome.Won, "VICTORY")]
        [TestCase(Outcome.Lost, "DEFEAT")]
        public void TheWord_SlamsInFromBigAndFaint_LandsInAQuarterSecond_AndStays_InTheCutInsLettering(Outcome outcome, string word)
        {
            var result = View.Result;
            result.Hide();
            View.ShowEnd(outcome, 40, 2, 5, () => { });

            Assert.AreEqual(word, result.Title.text);
            var want = outcome == Outcome.Won ? BattleView.Gold : BattleView.Red;
            Assert.AreEqual(want.r, result.Title.color.r, 0.01f, "gold for a victory, red for a defeat");
            Assert.AreEqual(want.g, result.Title.color.g, 0.01f);
            Assert.AreEqual(want.b, result.Title.color.b, 0.01f);
            Assert.Greater(result.TitleRect.localScale.x, 2.5f, "starts big");
            Assert.Less(result.Title.color.a, 0.1f, "and faint");

            result.Advance(ResultPanel.SlamSeconds / 2);
            Assert.Less(result.TitleRect.localScale.x, ResultPanel.SlamStartScale);
            Assert.Greater(result.TitleRect.localScale.x, 1f);

            result.Advance(ResultPanel.SlamSeconds);
            Assert.AreEqual(1f, result.TitleRect.localScale.x, 1e-4f, "landed");
            Assert.AreEqual(1f, result.Title.color.a, 1e-4f);

            result.Advance(ResultPanel.LetteringSeconds);
            Assert.AreEqual(1f, result.TitleRect.localScale.x, 1e-4f, "stays at its size");
            Assert.AreEqual(word, result.Title.text);

            // The same helper the cut-in's shout uses: bold italic with an outline (so the Bangers swap reaches it).
            Assert.AreEqual(FontStyle.BoldAndItalic, result.Title.fontStyle);
            Assert.IsNotNull(result.Title.GetComponent<Outline>());
        }

        [Test]
        public void ThePanel_ComesAfterAboutOneAndAHalfSeconds_NotBefore()
        {
            View.ShowEnd(Outcome.Won, 40, 2, 5, () => { });
            var result = View.Result;
            Assert.IsFalse(result.PanelShown, "only the word at first");
            Assert.AreEqual(1.5f, ResultPanel.LetteringSeconds, "17.8: about 1.5 s");
            result.Advance(ResultPanel.LetteringSeconds - 0.01f);
            Assert.IsFalse(result.PanelShown);
            result.Advance(0.02f);
            Assert.IsTrue(result.PanelShown);
            Assert.IsTrue(result.PlayAgain.gameObject.activeInHierarchy);
        }

        // ---- the line: N turns as the report counts them, M of 5 standing (R34) -------------------------------------------

        [Test]
        public void TheLine_CountsYourStandingHeroes_AndAllFiveFallenIsZeroOfFive_OnADefeat()
        {
            var fight = AtP0();
            fight.Players[1].KnockedOut = true;
            fight.Players[3].KnockedOut = true;
            Decide(fight, Outcome.Won);
            screen.ShowResult();
            Assert.AreEqual("Fight took 0 turns · 3 of 5 heroes standing", View.Result.Line.text, "the enemy's fallen do not count");

            foreach (var hero in fight.Players) hero.KnockedOut = true;
            Decide(fight, Outcome.Lost);
            screen.ShowResult();
            Assert.AreEqual("DEFEAT", View.Result.Title.text);
            StringAssert.EndsWith("0 of 5 heroes standing", View.Result.Line.text);
        }

        [Test]
        public void TheTurnCount_IsTheReportsCount_OverWholeFights([Values(1, 2, 3, 4)] int seed)
        {
            var data = Real();
            var counted = 0;
            var played = Play(data, data.Teams["greek_strike"], data.Teams["enemy_crew"], seed,
                              after: (_, batch) => counted += BattleScreen.TurnsIn(batch));
            Assert.Greater(played.Turns, 10, "set-up: a real fight");
            Assert.AreEqual(played.Turns, counted, "what the screen adds up is what the report counts, a skipped turn included");
        }

        // ---- what is cleared behind the panel -------------------------------------------------------------------------

        [Test]
        public void TheTurnBarIsEmpty_NobodyIsActing_AndTheButtonsAreAway_UnderThePanel()
        {
            var fight = AtP0();
            View.ShowBar(fight.PredictTurns(8));
            View.ShowActing(fight.Acting);
            Assert.Greater(View.Bar.Count, 0, "set-up: a bar is showing");
            Assert.IsTrue(View.ButtonsShown, "set-up: your hero is choosing");

            Decide(fight, Outcome.Won);
            screen.ShowResult();

            Assert.AreEqual(0, View.Bar.Count, "the bar is empty");
            Assert.AreEqual(0, View.BarRow.childCount, "and nothing of it is left to see");
            Assert.IsFalse(View.Corner.gameObject.activeSelf, "no face and no name in the corner");
            Assert.AreEqual("", View.TurnLine.text);
            Assert.IsFalse(View.ButtonsShown);
            Assert.IsFalse(View.ButtonsTakeTaps);
        }

        // ---- taps ---------------------------------------------------------------------------------------------------

        [Test]
        public void OnlyPlayAgainIsPressable_TheRestOfTheScreenTakesTheTapAndDoesNothing()
        {
            var fight = AtP0();
            Decide(fight, Outcome.Won);
            screen.ShowResult();
            var result = View.Result;

            Assert.IsNull(result.GetComponent<UnityEngine.UI.Button>(), "no tap-anywhere box");
            Assert.IsTrue(result.Shade.raycastTarget, "the shade takes the taps");
            var shade = (RectTransform)result.transform;
            Assert.AreEqual(Vector2.zero, shade.anchorMin);
            Assert.AreEqual(Vector2.one, shade.anchorMax, "over the whole safe area");
            var presses = result.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            Assert.AreEqual(1, presses.Length, "one button");
            Assert.AreSame(result.PlayAgain, presses[0]);
            Assert.IsFalse(result.PanelShown, "and until the panel is up it can't be pressed either");

            screen.PlayAgain();   // (the button's own onClick calls this; here nothing presses it)
            screen.ShowResult();
            Assert.IsFalse(screen.PlayAgainPressed, "a new result starts with no press");
        }

        [Test]
        public void PlayAgain_IsAtLeast144Px_ItsTextAtLeast40_AndInsideThePanel_AndPressingItAsksForANewFight()
        {
            var fight = AtP0();
            Decide(fight, Outcome.Lost);
            screen.ShowResult();
            var result = View.Result;
            Assert.GreaterOrEqual(result.PlayAgainRect.sizeDelta.x, 144f);
            Assert.GreaterOrEqual(result.PlayAgainRect.sizeDelta.y, 144f);
            Assert.GreaterOrEqual(ResultPanel.MinPress, 144f);
            Assert.GreaterOrEqual(result.PlayAgainText.fontSize, 40);
            Assert.AreEqual("Play again", result.PlayAgainText.text);
            Assert.GreaterOrEqual(result.Line.resizeTextMinSize, 40, "the line never shrinks under 40 px");

            var box = ((RectTransform)result.Box.transform).sizeDelta;
            var press = result.PlayAgainRect;
            Assert.LessOrEqual(press.sizeDelta.x, box.x);
            Assert.LessOrEqual(press.anchoredPosition.y + press.sizeDelta.y, box.y);

            Assert.IsFalse(screen.PlayAgainPressed);
            result.PlayAgain.onClick.Invoke();
            Assert.IsTrue(screen.PlayAgainPressed, "the loop will start a new fight");
        }

        [Test]
        public void TheResultSitsUnderTheGearAndThePanel_APlainChildOfTheSafeArea_AndLast()
        {
            var fight = AtP0();
            Decide(fight, Outcome.Won);
            screen.ShowResult();
            var result = View.Result;

            Assert.IsNull(result.GetComponent<Canvas>(), "no sorting of its own");
            Assert.AreSame(View.SafeArea, result.transform.parent);
            Assert.AreEqual(View.SafeArea.childCount - 1, result.transform.GetSiblingIndex(), "the last box in the safe area, over the card");
            Assert.IsTrue(View.Settings.Gear.GetComponent<Canvas>().overrideSorting, "the gear has its own, so it stays above");
            Assert.IsTrue(View.Settings.Panel.GetComponent<Canvas>().overrideSorting);
        }

        [Test]
        public void QuitFight_WhileTheResultShows_TakesTheResultAway()
        {
            var fight = AtP0();
            Decide(fight, Outcome.Won);
            screen.ShowResult();
            Assert.IsTrue(View.End.activeSelf, "set-up");

            screen.QuitFight();

            Assert.IsFalse(View.End.activeSelf);
        }
    }
}
