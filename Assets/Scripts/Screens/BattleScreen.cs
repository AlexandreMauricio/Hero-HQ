using HeroHQ.Rules.Data;
using UnityEngine;

namespace HeroHQ.Screens
{
    // The one script in the Battle scene (03 Architecture 7.2). BattleSceneBuilder fills in the six data files;
    // nothing here is set by hand in the editor. P2a-04: load the data and build the empty screen. The fight loop
    // comes in P2a-07.
    public class BattleScreen : MonoBehaviour
    {
        [Header("The six data files (set by BattleSceneBuilder)")]
        public TextAsset heroes;
        public TextAsset classes;
        public TextAsset traits;
        public TextAsset tuning;
        public TextAsset teams;
        public TextAsset formations;

        [Tooltip("How long one beat lasts, in seconds (02 GDD 17.6). Change it while playing to feel the pace.")]
        public float beatSeconds = 0.4f;

        public GameData Data { get; private set; }
        public BattleView View { get; private set; }

        void Start()
        {
            // GameData.Load collects every problem in the files and throws once with the whole list,
            // which Unity shows in the Console.
            Data = GameData.Load(heroes.text, classes.text, traits.text, tuning.text, teams.text, formations.text);
            View = new BattleView();
            View.Build(transform);
        }

        void Update()
        {
            // The phone turns between its two landscape sides, which moves the notch: keep the boxes clear of it.
            View?.FitSafeArea();
        }
    }
}
