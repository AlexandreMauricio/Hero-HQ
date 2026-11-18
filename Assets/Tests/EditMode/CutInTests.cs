using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroHQ.Rules;
using HeroHQ.Rules.Data;
using HeroHQ.Screens;
using HeroHQ.Screens.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using static HeroHQ.Tests.TestFights;

namespace HeroHQ.Tests
{
    // P2b-01 (03 Architecture 8.10; 8.1 A1, 8.5): the Unity cut-in. CutInTiming's timetable as plain C#; the cut-in's
    // layers built in EditMode under a throwaway object and moved by hand with Step (nothing is played); BattleScreen's
    // choice of full, short or off with "seen it" per hero id. 01 Vision's acceptance 1-3; 02 GDD 17.7.
    public class CutInTests
    {
        const float Frame = 1f / 60;

        // ---- the timetable (acceptance 1) ---------------------------------------------------------------------

        [Test]
        public void TheFullVersion_LastsTwoToThreeSeconds_AndTheShortOneUnderOne()
        {
            Assert.That(CutInTiming.FullSeconds, Is.InRange(2f, 3f));
            Assert.That(CutInTiming.ShortSeconds, Is.LessThan(1f));
            foreach (var (version, seconds) in new[] { (CutInVersion.Full, CutInTiming.FullSeconds), (CutInVersion.Short, CutInTiming.ShortSeconds) })
            {
                var clock = new CutInTiming.Clock(version);
                var played = 0f;
                while (!clock.Finished)
                {
                    clock.Advance(Frame);
                    played += Frame;
                    Assert.Less(played, 5, "the cut-in never ends");
                }
                Assert.AreEqual(seconds, played, Frame, $"{version} plays for its whole length, flash-out included");
            }
        }

        [Test]
        public void TheBeats_FollowEachOther_FromZeroToTheEnd_AsInTheMockUp()
        {
            foreach (var (beats, seconds) in new[] { (CutInTiming.FullBeats, CutInTiming.FullSeconds), (CutInTiming.ShortBeats, CutInTiming.ShortSeconds) })
            {
                Assert.AreEqual(0f, beats[0].from);
                for (int i = 1; i < beats.Length; i++) Assert.AreEqual(beats[i - 1].to, beats[i].from, beats[i].name);
                Assert.AreEqual(seconds, beats[beats.Length - 1].to);
            }
            Assert.AreEqual(new[] { "Freeze", "Slash", "Entry", "Hold and shout", "Release", "Back" }, CutInTiming.FullBeats.Select(b => b.name));
            Assert.AreEqual(new[] { "Freeze + slash", "Shout and hold", "Flash out" }, CutInTiming.ShortBeats.Select(b => b.name));
        }

        [Test]
        public void TheFullVersion_ShowsEachLayerInItsBeat()
        {
            CutInState At(float t) => CutInTiming.At(CutInVersion.Full, t);
            // Freeze: the dim and the bars come, nothing else yet.
            Assert.Greater(At(0.1f).Dim, 0);
            Assert.Greater(At(0.1f).Bars, 0);
            Assert.AreEqual(0f, At(0.1f).Band);
            Assert.IsFalse(At(0.1f).HeroVisible);
            // Slash: the band opens with a flash.
            Assert.Greater(At(0.25f).Band, 0);
            Assert.Greater(At(0.2f).Flash, 0);
            Assert.IsFalse(At(0.3f).HeroVisible);
            // Entry: the hero slides in and the behind-layer rises; no shout yet.
            Assert.IsTrue(At(0.4f).HeroVisible);
            Assert.Less(At(0.4f).HeroIn, 1);
            Assert.IsFalse(At(0.6f).ShoutVisible);
            // Hold and shout: the shout slams in big and settles, with a shake; the camera pushes in.
            Assert.IsTrue(At(0.65f).ShoutVisible);
            Assert.Greater(At(0.66f).ShoutScale, 2);
            Assert.AreEqual(1f, At(1f).ShoutScale, 1e-4);
            Assert.AreNotEqual(0f, At(0.8f).ShakeX);
            Assert.AreEqual(1f, At(1f).HeroIn, 1e-4);
            Assert.Greater(At(1.8f).Push, At(1f).Push);
            // Release: the white flash grows; Back: the battle shows under the fading flash.
            Assert.Greater(At(2.15f).Flash, 0.5f);
            Assert.IsTrue(At(2.19f).ShowingCutIn);
            Assert.IsFalse(At(2.2f).ShowingCutIn);
            Assert.Greater(At(2.25f).Flash, 0);
            Assert.AreEqual(0f, At(2.5f).Flash, 1e-4);
        }

        [Test]
        public void TheShortVersion_HasTheHeroAtOnce_AndTheShoutAtFifteenHundredths()
        {
            CutInState At(float t) => CutInTiming.At(CutInVersion.Short, t);
            Assert.IsTrue(At(0).HeroVisible);
            Assert.AreEqual(1f, At(0).HeroIn);
            Assert.IsFalse(At(0.14f).ShoutVisible);
            Assert.IsTrue(At(0.15f).ShoutVisible);
            Assert.IsTrue(At(0.67f).ShowingCutIn);
            Assert.IsFalse(At(0.68f).ShowingCutIn);
        }

        [Test]
        public void ATap_AtAnyMoment_EndsTheCutInWithinATenthOfASecond()
        {
            foreach (var version in new[] { CutInVersion.Full, CutInVersion.Short })
                for (var at = 0f; at < CutInTiming.Seconds(version); at += 0.01f)
                {
                    var clock = new CutInTiming.Clock(version);
                    clock.Advance(at);
                    var showing = !clock.BattleShows;
                    clock.Skip();
                    clock.Advance(CutInTiming.SkipSeconds + 1e-4f);
                    Assert.IsTrue(clock.BattleShows, $"{version} tapped at {at:0.00} s: the battle shows");
                    // A tap during the last flash-out (the battle already shows) changes nothing, as in the mock-up.
                    if (showing) Assert.IsTrue(clock.Finished, $"{version} tapped at {at:0.00} s: nothing is left after 0.1 s");
                }
        }

        [Test]
        public void ATap_FreezesTheFrameUnderARisingFlash_ThenTheBattleShows()
        {
            var clock = new CutInTiming.Clock(CutInVersion.Full);
            clock.Advance(1f);
            var before = clock.State;
            clock.Skip();
            clock.Advance(0.03f);
            Assert.IsTrue(clock.State.ShowingCutIn, "the frame is held while the flash rises");
            Assert.AreEqual(before.Push, clock.State.Push, "frozen");
            Assert.AreEqual(0.6f, clock.State.Flash, 1e-3);
            clock.Advance(0.03f);
            Assert.IsTrue(clock.BattleShows);
            Assert.IsFalse(clock.Finished, "the flash is still fading");
        }

        [TestCase(CutInSetting.FirstTimeFullThenShort, false, CutInVersion.Full)]
        [TestCase(CutInSetting.FirstTimeFullThenShort, true, CutInVersion.Short)]
        [TestCase(CutInSetting.AlwaysFull, false, CutInVersion.Full)]
        [TestCase(CutInSetting.AlwaysFull, true, CutInVersion.Full)]
        [TestCase(CutInSetting.Off, false, CutInVersion.Off)]
        [TestCase(CutInSetting.Off, true, CutInVersion.Off)]
        public void TheSetting_AndWhetherTheHeroWasSeen_PickTheVersion(CutInSetting setting, bool seen, CutInVersion expected) =>
            Assert.AreEqual(expected, CutInTiming.Pick(setting, seen));

        [TestCase("TALOS, WAKE UP!", new[] { "TALOS,", "WAKE UP!" })]
        [TestCase("FROM ABOVE.", new[] { "FROM ABOVE." })]
        [TestCase("BY THE WORD OF THOTH!", new[] { "BY THE WORD", "OF THOTH!" })]
        [TestCase("SPECIAL DELIVERY!", new[] { "SPECIAL", "DELIVERY!" })]
        public void ALongShout_IsSplitInTwo_WhereTheLongerLineIsShortest(string shout, string[] lines) =>
            Assert.AreEqual(lines, CutIn.ShoutLinesOf(shout));

        // ---- on screen ----------------------------------------------------------------------------------------

        GameObject root;
        BattleScreen screen;
        readonly Dictionary<string, int?> savedSeen = new Dictionary<string, int?>();

        static GameData real;
        static GameData Real()
        {
            string Read(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "Data", file));
            return real ?? (real = GameData.Load(Read("heroes.json"), Read("classes.json"), Read("traits.json"),
                                                 Read("tuning.json"), Read("teams.json"), Read("formations.json")));
        }

        // The editor's PlayerPrefs are the real ones on this PC (03 8.10): every "seen" flag a test may touch is kept
        // here first and put back afterwards, so the tests start from "nobody seen" and leave no key behind.
        static IEnumerable<string> HeroIdsTestsUse() => Real().Heroes.Keys.Concat(TestData.HeroIds)
            .Concat(new[] { "p0", "p1", "p2", "p3", "p4", "e0", "e1", "e2", "e3", "e4" });

        [SetUp]
        public void BuildScreen()
        {
            savedSeen.Clear();
            foreach (var id in HeroIdsTestsUse().Distinct())
            {
                var key = BattleScreen.SeenKeyPrefix + id;
                savedSeen[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
                PlayerPrefs.DeleteKey(key);
            }
            root = new GameObject("CutInTests");
            screen = root.AddComponent<BattleScreen>();
            screen.cutInArt = BattleSceneBuilder.CutInArt();
            screen.BuildView();
        }

        [TearDown]
        public void PutBack()
        {
            UnityEngine.Object.DestroyImmediate(root);
            foreach (var pair in savedSeen)
                if (pair.Value.HasValue) PlayerPrefs.SetInt(pair.Key, pair.Value.Value);
                else PlayerPrefs.DeleteKey(pair.Key);
            PlayerPrefs.Save();
        }

        Fight RealFight(int seed = 1)
        {
            var data = Real();
            var fight = Fight.Create(data, data.Teams["greek_strike"], data.Teams["enemy_crew"], seed);
            screen.View.ShowFight(fight, data.Formations[data.Teams["greek_strike"].Formation], data.Formations[data.Teams["enemy_crew"].Formation]);
            return fight;
        }

        static Fighter Find(IEnumerable<Fighter> team, string id) => team.Single(f => f.Hero.Id == id);

        CutIn ShowCutIn(Fight fight, Fighter who, CutInVersion version)
        {
            var (background, glow) = BattleScreen.Colours(fight.Data, who.Hero);
            return CutIn.Show(screen.View.Canvas.transform, who, version, background, glow,
                              screen.Art(who.Hero.Cutin.Drawing), screen.Art(who.Hero.Cutin.Behind));
        }

        static FightEvent UltimateUsed(Fighter who)
        {
            var e = new FightEvent(FightEventKind.SkillUsed, who);
            typeof(FightEvent).GetProperty("Button").SetValue(e, HeroHQ.Rules.Button.Ultimate);
            return e;
        }

        [Test]
        public void EveryDrawingAndBehindLayer_NamedInHeroesJson_HasItsSprite()
        {
            var named = Real().Heroes.Values.SelectMany(h => new[] { h.Cutin.Drawing, h.Cutin.Behind }).Where(n => n != null).ToList();
            Assert.IsNotEmpty(named, "Kal & Talos has a drawing and a behind-layer");
            foreach (var name in named)
            {
                var path = $"{BattleSceneBuilder.CutInArtFolder}/{name}.png";
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Sprite>(path), $"{path} is missing or not a sprite");
                Assert.IsNotNull(screen.Art(name), $"the builder's sprite list has {name}");
            }
        }

        [Test]
        public void KalsPictures_ImportAsSprites_TheBehindLayerAtTheDefault2048Cap()
        {
            // 03 Architecture 8.5's two INFERENCEs: PNGs import as sprites in 2D mode, and the 2508 px behind-layer is
            // read at Unity's default 2048 px cap (it is drawn about 990 px wide, so nothing is lost).
            foreach (var name in new[] { "kal", "kal_behind" })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath($"{BattleSceneBuilder.CutInArtFolder}/{name}.png");
                Assert.AreEqual(TextureImporterType.Sprite, importer.textureType, name);
                Assert.AreEqual(SpriteImportMode.Single, importer.spriteImportMode, name + ": one whole picture, named after its file (2D mode would cut it up)");
                Assert.AreEqual(2048, importer.maxTextureSize, name);
            }
            Assert.AreEqual(2048, screen.Art("kal_behind").texture.width);
            Assert.AreEqual(1776, screen.Art("kal").texture.width, "kal.png is under the cap: kept whole");
        }

        [Test]
        public void KalsCutIn_UsesHisDrawingAndBehindLayer_AtTheMockUpsPlacement()
        {
            var fight = RealFight();
            var kal = Find(fight.Players, "kal");
            var cutIn = ShowCutIn(fight, kal, CutInVersion.Full);
            cutIn.Step(1f);                                           // the hold: the hero in place

            Assert.IsFalse(cutIn.Placeholder);
            Assert.AreSame(screen.Art("kal"), cutIn.Hero.sprite);
            Assert.AreSame(screen.Art("kal_behind"), cutIn.Behind.sprite);
            // make_mockup_test.py: the whole PNG 738 px tall, its top at y 173.5, centred on the hips (720, 560).
            var hero = cutIn.Hero.rectTransform;
            Assert.AreEqual(738, hero.sizeDelta.y, 0.01f);
            Assert.AreEqual(738 * 1776f / 1920, hero.sizeDelta.x, 0.01f);
            Assert.AreEqual(new Vector2(720 - 960, 540 - 560), cutIn.HeroCamera.anchoredPosition, "the hips, from the screen's middle");
            Assert.AreEqual(new Vector2(-738 * 1776f / 1920 / 2, 560 - 173.5f), hero.anchoredPosition);
            Assert.AreEqual(990.66f, cutIn.Behind.rectTransform.sizeDelta.x, 0.01f, "2508 px x 0.395");
            Assert.AreEqual(TALOSTopLeft, cutIn.Behind.rectTransform.anchoredPosition, "risen into place by the hold");
        }

        static readonly Vector2 TALOSTopLeft = new Vector2(376 - 720, 560 - 75);

        [Test]
        public void AnotherHero_ChangesOnlyThePicture_TheBehindLayer_TheShoutAndTheColours()
        {
            // Acceptance 2: one template. Kal (Greek, drawing and behind-layer) against Halvard (Norse, placeholder).
            var fight = RealFight();
            var kal = ShowCutIn(fight, Find(fight.Players, "kal"), CutInVersion.Full);
            var halvard = ShowCutIn(fight, Find(fight.Players, "halvard"), CutInVersion.Full);
            foreach (var t in new[] { 0f, 0.2f, 0.3f, 0.5f, 0.8f, 1.2f, 1.9f, 2.1f })
            {
                kal.Step(t - kal.Clock.Time);
                halvard.Step(t - halvard.Clock.Time);
                var at = $"at {t} s";
                Assert.AreEqual(kal.Dim.color, halvard.Dim.color, at);
                Assert.AreEqual(kal.Flash.color, halvard.Flash.color, at);
                Assert.AreEqual(kal.Band.sizeDelta, halvard.Band.sizeDelta, at);
                Assert.AreEqual(kal.Band.localRotation, halvard.Band.localRotation, at);
                Assert.AreEqual(kal.TopBar.sizeDelta, halvard.TopBar.sizeDelta, at);
                Assert.AreEqual(kal.HeroCamera.gameObject.activeSelf, halvard.HeroCamera.gameObject.activeSelf, at);
                Assert.AreEqual(kal.HeroCamera.localScale, halvard.HeroCamera.localScale, at);
                Assert.AreEqual(kal.HeroOffset.anchoredPosition, halvard.HeroOffset.anchoredPosition, at);
                Assert.AreEqual(kal.Shout.gameObject.activeSelf, halvard.Shout.gameObject.activeSelf, at);
                Assert.AreEqual(kal.Shout.anchoredPosition, halvard.Shout.anchoredPosition, at);
                Assert.AreEqual(kal.Shout.localScale, halvard.Shout.localScale, at);
                Assert.AreEqual(kal.Shout.localRotation, halvard.Shout.localRotation, at);
            }
            // What does change: the picture, the behind-layer, the shout and the pantheon colours (traits.json).
            Assert.IsTrue(halvard.Placeholder);
            Assert.IsNull(halvard.Behind);
            Assert.IsNotNull(kal.Behind);
            Assert.AreEqual(new[] { "I GAVE", "MY WORD!" }, halvard.ShoutLines.Select(l => l.text));
            Assert.AreEqual("TALOS,", kal.ShoutLines[0].text);
            Assert.AreEqual("— HALVARD —", halvard.NameLine.text);
            Assert.AreEqual(Hex("#d7263d"), halvard.BandColour.color);
            Assert.AreEqual(Hex("#1b6fd1"), kal.BandColour.color);
            // The glow is mixed into the band's colour by code (one opaque picture): the glow colour in the middle,
            // the band's colour at the edge.
            AssertColour(Hex("#a8e6ff"), halvard.Glow.sprite.texture.GetPixel(64, 64));
            AssertColour(Hex("#d7263d"), halvard.Glow.sprite.texture.GetPixel(0, 0));
            AssertColour(Hex("#ffd23f"), kal.Glow.sprite.texture.GetPixel(64, 64));
            AssertColour(Hex("#1b6fd1"), kal.Glow.sprite.texture.GetPixel(0, 0));
        }

        static void AssertColour(Color expected, Color actual)
        {
            foreach (var (e, a) in new[] { (expected.r, actual.r), (expected.g, actual.g), (expected.b, actual.b), (1f, actual.a) })
                Assert.AreEqual(e, a, 1.5f / 255, $"{expected} against {actual}");
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var colour);
            return colour;
        }

        [Test]
        public void TheRivalTag_ShowsOnlyOnTheEnemySide()
        {
            var fight = RealFight();
            var yours = ShowCutIn(fight, Find(fight.Players, "kal"), CutInVersion.Full);
            var theirs = ShowCutIn(fight, Find(fight.Enemies, "kal"), CutInVersion.Full);
            Assert.IsFalse(yours.Rival);
            Assert.IsNull(yours.RivalTag);
            Assert.IsTrue(theirs.Rival);
            Assert.AreEqual("RIVAL", theirs.RivalTag.text);
            Assert.Greater(theirs.RivalTag.rectTransform.anchoredPosition.x, 0, "beside the name, to its right");
            Assert.AreEqual(theirs.NameLine.rectTransform.anchoredPosition.y, theirs.RivalTag.rectTransform.anchoredPosition.y);
            Assert.AreSame(yours.Hero.sprite, theirs.Hero.sprite, "the drawing is not recoloured for the rival");
        }

        [Test]
        public void AHeroWithNoDrawing_GetsThePlaceholderFigure_InADarkShadeOfThePantheonColour_WithGlowingEyes()
        {
            var fight = RealFight();
            var strix = Find(fight.Players, "strix");
            Assert.IsNull(strix.Hero.Cutin.Drawing, "set-up: Strix has no drawing yet");
            var cutIn = ShowCutIn(fight, strix, CutInVersion.Short);

            Assert.IsTrue(cutIn.Placeholder);
            Assert.IsNotNull(cutIn.Hero.sprite, "the figure drawn by code");
            var greek = Hex("#1b6fd1");
            Assert.AreEqual(Color.Lerp(Color.black, greek, 0.3f), cutIn.Hero.color);
            var eyes = cutIn.HeroOffset.GetComponentsInChildren<UnityEngine.UI.Image>().Where(i => i.name == "Eye").ToList();
            Assert.AreEqual(2, eyes.Count);
            Assert.IsTrue(eyes.All(e => e.color == Hex("#ffd23f")), "in the glow colour");
            Assert.AreEqual(new[] { "FROM ABOVE." }, cutIn.ShoutLines.Select(l => l.text));
        }

        [Test]
        public void ATapOnTheCutIn_SkipsIt_AndItRemovesItself()
        {
            var fight = RealFight();
            var cutIn = ShowCutIn(fight, Find(fight.Players, "kal"), CutInVersion.Full);
            cutIn.Step(0.5f);
            Assert.IsFalse(cutIn.BattleShows);
            Assert.IsTrue(cutIn.GetComponent<UnityEngine.UI.Image>().raycastTarget, "the whole screen takes the tap");
            cutIn.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();   // as a real tap does
            cutIn.Step(0.06f);
            Assert.IsTrue(cutIn.BattleShows);
            Assert.IsFalse(cutIn.GetComponent<UnityEngine.UI.Image>().raycastTarget, "taps reach the battle again");
            cutIn.Step(0.05f);
            Assert.IsTrue(cutIn == null, "destroyed once the flash has gone");
        }

        [Test]
        public void ACutInLeftAlone_RemovesItselfAtItsEnd()
        {
            var fight = RealFight();
            var cutIn = ShowCutIn(fight, Find(fight.Players, "kal"), CutInVersion.Short);
            cutIn.Step(0.7f);
            Assert.IsTrue(cutIn != null && cutIn.BattleShows);
            cutIn.Step(0.11f);
            Assert.IsTrue(cutIn == null);
        }

        // ---- BattleScreen: full, short or off; "seen" per hero id (02 GDD 17.7; reading R30) ----------------------

        [Test]
        public void TheFirstTime_AHerosCutInIsFull_ThenShort_OnEitherSide_RememberedByHeroId()
        {
            var fight = RealFight();
            var playback = new Playback(fight);
            var yourKal = Find(fight.Players, "kal");
            var theirKal = Find(fight.Enemies, "kal");
            var strix = Find(fight.Enemies, "strix");

            Assert.IsFalse(BattleScreen.HasSeen("kal"));
            Assert.AreEqual(0f, screen.Show(fight, playback, UltimateUsed(theirKal), out var first));
            Assert.AreEqual(CutInVersion.Full, first.Clock.Version, "nobody has seen Kal yet");
            Assert.IsTrue(BattleScreen.HasSeen("kal"), "seen as soon as the full one starts (R30)");
            first.Tap();
            first.Step(0.2f);

            screen.Show(fight, playback, UltimateUsed(yourKal), out var second);
            Assert.AreEqual(CutInVersion.Short, second.Clock.Version, "the enemy's Kal counts for yours: per hero, not per side");
            screen.Show(fight, playback, UltimateUsed(strix), out var third);
            Assert.AreEqual(CutInVersion.Full, third.Clock.Version, "another hero is still new");
            Assert.AreEqual(1, PlayerPrefs.GetInt(BattleScreen.SeenKeyPrefix + "kal"), "kept by id in PlayerPrefs");
        }

        [Test]
        public void TheOtherSettings_AlwaysFull_AndOff_WhichStillGivesTheOffLook()
        {
            var fight = RealFight();
            var playback = new Playback(fight);
            var kal = Find(fight.Players, "kal");
            BattleScreen.MarkSeen("kal");

            screen.CutInSetting = CutInSetting.AlwaysFull;
            screen.Show(fight, playback, UltimateUsed(kal), out var full);
            Assert.AreEqual(CutInVersion.Full, full.Clock.Version);

            screen.CutInSetting = CutInSetting.Off;
            var hold = screen.Show(fight, playback, UltimateUsed(kal), out var none);
            Assert.IsNull(none);
            Assert.AreEqual(BattleView.OffLookSeconds, hold);
            Assert.AreEqual(1, screen.View.OffLooks);
            Assert.AreEqual(kal.Hero.Cutin.Shout, screen.View.LastShout);
        }

        [Test]
        public void OnlyAnUltimate_StartsACutIn()
        {
            var fight = RealFight();
            var playback = new Playback(fight);
            var kal = Find(fight.Players, "kal");
            foreach (var button in new[] { HeroHQ.Rules.Button.Basic, HeroHQ.Rules.Button.Special })
            {
                var e = new FightEvent(FightEventKind.SkillUsed, kal);
                typeof(FightEvent).GetProperty("Button").SetValue(e, button);
                Assert.AreEqual(0f, screen.Show(fight, playback, e, out var cutIn));
                Assert.IsNull(cutIn, button.ToString());
            }
            Assert.IsFalse(BattleScreen.HasSeen("kal"));
        }

        [Test]
        public void OverFiveRealFights_TheCutInIsOnScreenBeforeEveryUltimatesFirstHit_AndHandsBackBeforeIt()
        {
            // Acceptance 3 (P2a-08's off-look test, extended): the hits and numbers come after the cut-in, never during.
            var data = Real();
            var players = data.Teams["greek_strike"];
            var enemies = data.Teams["enemy_crew"];
            var cutIns = 0;
            var full = 0;

            foreach (var seed in new[] { 1, 2, 3, 4, 5 })
            {
                Playback playback = null;
                Play(data, players, enemies, seed, after: (fight, batch) =>
                {
                    if (playback == null)
                    {
                        playback = new Playback(fight);
                        screen.View.ShowFight(fight, data.Formations[players.Formation], data.Formations[enemies.Formation]);
                    }
                    var ultimateAction = batch.Count > 0 && batch[0].Kind == FightEventKind.SkillUsed && batch[0].Button == HeroHQ.Rules.Button.Ultimate;
                    var cutInPlayed = false;   // a bool: a destroyed cut-in reads as null in Unity
                    foreach (var e in batch)
                    {
                        if (ultimateAction && (e.Kind == FightEventKind.Damaged || e.Kind == FightEventKind.Missed))
                            Assert.IsTrue(cutInPlayed, $"seed {seed}: a cut-in was on screen before this hit: {e}");
                        screen.Show(fight, playback, e, out var cutIn);
                        if (cutIn == null) continue;
                        cutIns++;
                        if (cutIn.Clock.Version == CutInVersion.Full) full++;
                        Assert.AreSame(e.Who, cutIn.Who);
                        Assert.IsFalse(cutIn.BattleShows, "the cut-in covers the battle when it starts");
                        // As BattleScreen.Play waits: frame by frame until the battle shows again; only then the next event.
                        var waited = 0f;
                        while (!cutIn.BattleShows) { cutIn.Step(Frame); waited += Frame; }
                        Assert.LessOrEqual(waited, CutInTiming.FullBattleAt + Frame);
                        cutInPlayed = true;
                        cutIn.Step(1f);                                // the flash fades; the cut-in removes itself
                    }
                });
            }
            Assert.Greater(cutIns, 0, "the five fights fire at least one ultimate");
            Assert.LessOrEqual(full, data.Heroes.Count, "full only the first time per hero");
            Assert.AreEqual(0, screen.View.OffLooks, "the default setting never shows the off look");
        }
    }
}
