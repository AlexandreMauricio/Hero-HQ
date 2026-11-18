using System;
using System.Linq;
using HeroHQ.Rules.Data;
using HeroHQ.Screens;
using HeroHQ.Screens.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HeroHQ.Tests
{
    // P2a-04 (03 Architecture 7.7): the Battle scene that BattleSceneBuilder makes (run tools/make-scene.sh before
    // these tests when the builder changes), and the empty battle screen BattleView builds (02 GDD 17.1, 17.3).
    public class BattleSceneTests
    {
        static void WithBattleScene(Action<Scene> check)
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(BattleSceneBuilder.ScenePath),
                $"{BattleSceneBuilder.ScenePath} is missing: run tools/make-scene.sh");
            // Opened beside whatever is open, then closed, so the test leaves the editor as it found it.
            var scene = EditorSceneManager.OpenScene(BattleSceneBuilder.ScenePath, OpenSceneMode.Additive);
            try { check(scene); }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        static T[] All<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();

        // ---- the scene -----------------------------------------------------------------------------------------

        [Test]
        public void TheBattleScene_HoldsOneBattleScreen_WithTheSixDataFiles_ThatLoadWithNoProblem()
        {
            WithBattleScene(scene =>
            {
                var screens = All<BattleScreen>(scene);
                Assert.AreEqual(1, screens.Length, "one BattleScreen, the scene's only script (7.2)");
                var s = screens[0];
                var files = new[] { s.heroes, s.classes, s.traits, s.tuning, s.teams, s.formations };
                for (int i = 0; i < files.Length; i++)
                {
                    var expected = $"Assets/Data/{BattleSceneBuilder.DataFiles[i]}.json";
                    Assert.IsNotNull(files[i], $"{expected} is not set");
                    Assert.AreEqual(expected, AssetDatabase.GetAssetPath(files[i]));
                }
                Assert.DoesNotThrow(() => GameData.Load(s.heroes.text, s.classes.text, s.traits.text,
                                                        s.tuning.text, s.teams.text, s.formations.text));
                Assert.AreEqual(0.4f, s.beatSeconds, 1e-6, "a beat is 0.4 s (02 GDD 17.6)");
            });
        }

        [Test]
        public void TheBattleScene_HoldsEveryCutInPicture_TheDataNames()
        {
            // P2b-01 (03 Architecture 8.5): the builder fills BattleScreen's sprite list from Assets/Art/CutIns, so the
            // cut-in finds each drawing and behind-layer by name, in the editor and in a build.
            WithBattleScene(scene =>
            {
                var s = All<BattleScreen>(scene)[0];
                var data = GameData.Load(s.heroes.text, s.classes.text, s.traits.text, s.tuning.text, s.teams.text, s.formations.text);
                var named = data.Heroes.Values.SelectMany(h => new[] { h.Cutin.Drawing, h.Cutin.Behind }).Where(n => n != null).ToList();
                Assert.IsNotEmpty(named);
                foreach (var name in named)
                {
                    var sprite = s.Art(name);
                    Assert.IsNotNull(sprite, name + " is in the scene");
                    Assert.AreEqual(BattleSceneBuilder.CutInArtFolder + "/" + name + ".png", AssetDatabase.GetAssetPath(sprite));
                }
            });
        }

        [Test]
        public void TheBattleScene_HasOneCamera_AndAnEventSystemOnTheInputSystem()
        {
            WithBattleScene(scene =>
            {
                Assert.AreEqual(1, All<Camera>(scene).Length);
                var systems = All<EventSystem>(scene);
                Assert.AreEqual(1, systems.Length);
                Assert.IsNotNull(systems[0].GetComponent<InputSystemUIInputModule>(),
                    "the project's only input handler is the Input System, so the old StandaloneInputModule would fail");
                Assert.AreEqual(0, All<Canvas>(scene).Length, "the canvas is built by code when the scene plays, not saved in it");
            });
        }

        // ---- the empty screen ----------------------------------------------------------------------------------

        static void WithView(Action<BattleView> check)
        {
            var root = new GameObject("BattleSceneTests root");
            try
            {
                var view = new BattleView();
                view.Build(root.transform);
                check(view);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // y measured down from the top of the 1080 reference screen, as 02 GDD 17.3 writes it.
        static void AssertZone(RectTransform zone, float top, float bottom, string name)
        {
            Assert.IsNotNull(zone, name);
            Assert.AreEqual(name, zone.name);
            Assert.AreEqual(1 - bottom / 1080f, zone.anchorMin.y, 1e-5, $"{name}: bottom edge at y {bottom}");
            Assert.AreEqual(1 - top / 1080f, zone.anchorMax.y, 1e-5, $"{name}: top edge at y {top}");
            Assert.AreEqual(0f, zone.anchorMin.x);
            Assert.AreEqual(1f, zone.anchorMax.x);
            Assert.AreEqual(new Vector2(90, 0), zone.offsetMin, $"{name}: 90 px from the left edge (17.1)");
            Assert.AreEqual(new Vector2(-90, 0), zone.offsetMax, $"{name}: 90 px from the right edge (17.1)");
        }

        [Test]
        public void TheEmptyScreen_IsACanvasScaledFrom1920x1080_ThatKeepsItsHeight()
        {
            WithView(view =>
            {
                Assert.AreEqual(RenderMode.ScreenSpaceOverlay, view.Canvas.renderMode);
                var scaler = view.Canvas.GetComponent<CanvasScaler>();
                Assert.IsNotNull(scaler);
                Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
                Assert.AreEqual(new Vector2(1920, 1080), scaler.referenceResolution);
                Assert.AreEqual(1f, scaler.matchWidthOrHeight,
                    "match the height, so a wider phone (20:9) only gets more margin at the sides (17.1)");
                Assert.IsNotNull(view.Canvas.GetComponent<GraphicRaycaster>(), "taps reach the boxes");
            });
        }

        [Test]
        public void TheEmptyScreen_HasTheFourZonesOf17_3_InsideTheSafeArea()
        {
            WithView(view =>
            {
                Assert.AreEqual(view.Canvas.transform, view.SafeArea?.parent, "the safe-area panel sits on the canvas");
                AssertZone(view.TopStrip, 24, 170, "Top strip");
                AssertZone(view.Banner, 170, 240, "Banner");
                AssertZone(view.Field, 190, 780, "Field");
                AssertZone(view.BottomBand, 800, 1050, "Bottom band");
                foreach (var zone in new[] { view.TopStrip, view.Banner, view.Field, view.BottomBand })
                    Assert.AreEqual(view.SafeArea, zone.parent, $"{zone.name} is inside the safe area");
                Assert.Greater(view.Banner.GetSiblingIndex(), view.Field.GetSiblingIndex(),
                    "the banner is drawn after the field, so it sits on top where they overlap");
                Assert.LessOrEqual(view.BottomBand.anchorMax.y, view.Field.anchorMin.y, "the buttons never cover the field");
            });
        }

        [Test]
        public void FitSafeArea_PutsThePanelOnUnitysSafeArea()
        {
            WithView(view =>
            {
                // A 2400 x 1080 phone with a 120 px notch on the left and a 60 px rounded corner on the right.
                view.FitSafeArea(new Rect(120, 0, 2400 - 120 - 60, 1080), new Vector2(2400, 1080));
                Assert.AreEqual(120 / 2400f, view.SafeArea.anchorMin.x, 1e-6);
                Assert.AreEqual(0f, view.SafeArea.anchorMin.y, 1e-6);
                Assert.AreEqual((2400 - 60) / 2400f, view.SafeArea.anchorMax.x, 1e-6);
                Assert.AreEqual(1f, view.SafeArea.anchorMax.y, 1e-6);
                Assert.AreEqual(Vector2.zero, view.SafeArea.offsetMin);
                Assert.AreEqual(Vector2.zero, view.SafeArea.offsetMax);

                // Turned to the other landscape side: the notch is now on the right.
                view.FitSafeArea(new Rect(60, 0, 2400 - 120 - 60, 1080), new Vector2(2400, 1080));
                Assert.AreEqual(60 / 2400f, view.SafeArea.anchorMin.x, 1e-6);
                Assert.AreEqual((2400 - 120) / 2400f, view.SafeArea.anchorMax.x, 1e-6);
            });
        }
    }
}
