using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace HeroHQ.Screens.Editor
{
    // Makes Assets/Scenes/Battle.unity from nothing (03 Architecture 7.2, 7.4). The scene is never edited by hand:
    // change this file and run it again, from the menu "Hero HQ > Make the Battle scene" or with tools/make-scene.sh.
    public static class BattleSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Battle.unity";
        public static readonly string[] DataFiles = { "heroes", "classes", "traits", "tuning", "teams", "formations" };
        /// <summary>The cut-in pictures (03 Architecture 8.5); the data names them by file name.</summary>
        public const string CutInArtFolder = "Assets/Art/CutIns";

        [MenuItem("Hero HQ/Make the Battle scene")]
        static void BuildFromMenu()
        {
            // Building replaces the open scene, so ask first if it has unsaved changes.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        // Called by tools/make-scene.sh: Unity.exe -batchmode -quit -executeMethod HeroHQ.Screens.Editor.BattleSceneBuilder.Build
        // An exception here makes Unity exit with an error code, so the script notices.
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // A camera only to clear the background: every box is on a Screen Space Overlay canvas (BattleView).
            var camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(0x1E, 0x24, 0x30, 0xFF);
            camera.transform.position = new Vector3(0, 0, -10);

            // Taps reach the canvas through the Input System (the project's only input handler). The module fills
            // in Unity's default UI actions itself when the scene starts.
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var screen = new GameObject("Battle", typeof(BattleScreen)).GetComponent<BattleScreen>();
            screen.heroes = Data("heroes");
            screen.classes = Data("classes");
            screen.traits = Data("traits");
            screen.tuning = Data("tuning");
            screen.teams = Data("teams");
            screen.formations = Data("formations");
            screen.cutInArt = CutInArt();

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException($"Could not save {ScenePath}");
            Debug.Log($"BattleSceneBuilder: made {ScenePath}");
        }

        // Every picture in the cut-in folder, as sprites. Unity packs a sprite the scene refers to into the build, so the
        // cut-in finds it by name on a phone too (8.5). A picture that did not import as a sprite stops the build.
        public static Sprite[] CutInArt()
        {
            var paths = Directory.GetFiles(CutInArtFolder, "*.png").Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToArray();
            return paths.Select(path =>
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) throw new InvalidDataException($"{path} did not import as a sprite");
                return sprite;
            }).ToArray();
        }

        static TextAsset Data(string name)
        {
            var path = $"Assets/Data/{name}.json";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null) throw new FileNotFoundException($"{path} is missing or not imported as a TextAsset", path);
            return asset;
        }
    }
}
