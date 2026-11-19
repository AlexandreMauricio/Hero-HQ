using System;
using HeroHQ.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace HeroHQ.Screens
{
    /// <summary>
    /// The result screen (P2b-06; 02 GDD 17.8; 03 Architecture 8.1 A6), drawn by code like every other box. When the last
    /// event has played, <b>VICTORY</b> or <b>DEFEAT</b> slams in (it starts big and faint and lands at its size in
    /// <see cref="SlamSeconds"/>) in the cut-in's lettering (<see cref="CutIn.Lettering"/>), and stays; after
    /// <see cref="LetteringSeconds"/> a panel appears below it: "Fight took N turns · M of 5 heroes standing" and one
    /// <b>Play again</b> button. A dark shade over the safe area takes every tap, so before the panel and anywhere outside
    /// the button a tap does nothing (17.8: no tap-anywhere).
    ///
    /// It is a MonoBehaviour (a script Unity runs on an object each frame: see the Glossary) because something has to count
    /// the time. It counts <c>Time.deltaTime</c>, so the pause stops it too (reading R33); <see cref="Advance"/> is public so
    /// a test can move time without a frame. It is a plain child of the safe area, with no sorting of its own, so the gear
    /// and the settings panel (which have theirs) stay above it.
    /// </summary>
    public sealed class ResultPanel : MonoBehaviour
    {
        /// <summary>The word lands in 0.25 s and the whole slam, before the panel, lasts 1.5 s (17.8: "about 1.5 s").</summary>
        public const float SlamSeconds = 0.25f, LetteringSeconds = 1.5f;
        /// <summary>The word starts this many times its size. A press is at least 144 px (17.1).</summary>
        public const float SlamStartScale = 3f;
        public const int TitleSize = 240;
        public const float ButtonWidth = 560, ButtonHeight = 170, MinPress = 144;
        const float BoxWidth = 1500, BoxHeight = 460;

        public Text Title { get; private set; }
        public RectTransform TitleRect { get; private set; }
        /// <summary>The panel: the line and the button. Off until the word has had its 1.5 s.</summary>
        public GameObject Box { get; private set; }
        public Text Line { get; private set; }
        public UnityEngine.UI.Button PlayAgain { get; private set; }
        public RectTransform PlayAgainRect { get; private set; }
        public Text PlayAgainText { get; private set; }
        /// <summary>The dark shade over the whole safe area: it takes every tap, so nothing under the panel can be pressed.</summary>
        public Image Shade { get; private set; }

        /// <summary>Seconds of game time since <see cref="Show"/>.</summary>
        public float Age { get; private set; }
        /// <summary>The panel (the line and Play again) is up: the word has had its 1.5 s.</summary>
        public bool PanelShown => Box != null && Box.activeSelf;

        /// <summary>Makes the (hidden) result screen under <paramref name="parent"/>.</summary>
        public static ResultPanel Build(Transform parent)
        {
            var root = BattleView.NewRect("End", parent);
            root.Inset(0);
            var panel = root.gameObject.AddComponent<ResultPanel>();
            panel.Shade = root.gameObject.AddComponent<Image>();
            panel.Shade.color = new Color(0, 0, 0, 0.75f);
            panel.Shade.raycastTarget = true;
            panel.BuildParts(root);
            root.gameObject.SetActive(false);
            return panel;
        }

        void BuildParts(RectTransform root)
        {
            Title = CutIn.Lettering("Title", root, "", TitleSize, BattleView.Gold, null);
            TitleRect = Title.rectTransform;
            TitleRect.anchorMin = TitleRect.anchorMax = new Vector2(0.5f, 1);
            TitleRect.anchoredPosition = new Vector2(0, -330);   // the word's middle, 330 px down from the top of the safe area

            Box = BattleView.NewRect("Result panel", root).gameObject;
            var box = (RectTransform)Box.transform;
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 1);
            box.pivot = new Vector2(0.5f, 1);
            box.sizeDelta = new Vector2(BoxWidth, BoxHeight);
            box.anchoredPosition = new Vector2(0, -520);
            BattleView.Panel("Edge", box, BattleView.Grey);
            BattleView.Panel("Body", box, BattleView.Body).rectTransform.Inset(6);

            Line = BattleView.Label("Line", box, "", 56, Color.white, TextAnchor.MiddleCenter);
            Line.rectTransform.Band(40, 190, left: 40, right: 40);
            Line.horizontalOverflow = HorizontalWrapMode.Wrap;
            Line.resizeTextForBestFit = true;   // a long line shrinks a little, never under 40 px
            Line.resizeTextMinSize = 40;
            Line.resizeTextMaxSize = 56;

            var press = BattleView.NewRect("Play again", box);
            press.anchorMin = press.anchorMax = new Vector2(0.5f, 0);
            press.pivot = new Vector2(0.5f, 0);
            press.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
            press.anchoredPosition = new Vector2(0, 50);
            PlayAgainRect = press;
            var face = press.gameObject.AddComponent<Image>();
            face.sprite = BattleView.Rounded;
            face.type = Image.Type.Sliced;
            face.color = BattleView.Blue;
            PlayAgain = press.gameObject.AddComponent<UnityEngine.UI.Button>();
            PlayAgain.targetGraphic = face;
            PlayAgain.transition = Selectable.Transition.None;
            PlayAgainText = BattleView.Label("Text", press, "Play again", 72, Color.white, TextAnchor.MiddleCenter);
            PlayAgainText.fontStyle = FontStyle.Bold;

            Box.SetActive(false);
        }

        /// <summary>
        /// Starts the result: the shade comes on, the word slams in, and a press of Play again calls
        /// <paramref name="onPlayAgain"/>. <paramref name="standing"/> is how many of <b>your</b> heroes are on their feet
        /// (also on a defeat: 0 of 5, R34) out of <paramref name="of"/>.
        /// </summary>
        public void Show(Outcome outcome, int turns, int standing, int of, Action onPlayAgain)
        {
            if (outcome == Outcome.Going) throw new ArgumentException("The fight is still going.", nameof(outcome));
            Title.text = outcome == Outcome.Won ? "VICTORY" : "DEFEAT";
            Title.color = outcome == Outcome.Won ? BattleView.Gold : BattleView.Red;
            Line.text = $"Fight took {turns} turns · {standing} of {of} heroes standing";
            PlayAgain.onClick.RemoveAllListeners();
            PlayAgain.onClick.AddListener(() => onPlayAgain?.Invoke());
            Box.SetActive(false);
            Age = 0;
            gameObject.SetActive(true);
            Apply();
        }

        public void Hide() => gameObject.SetActive(false);

        void Update() => Advance(Time.deltaTime);

        /// <summary>Moves the result's clock on by <paramref name="seconds"/> of game time.</summary>
        public void Advance(float seconds)
        {
            Age += seconds;
            Apply();
        }

        void Apply()
        {
            // The slam: from SlamStartScale times its size, faint, to its own size and solid, easing out (fast, then settling).
            var t = Mathf.Clamp01(Age / SlamSeconds);
            var eased = 1 - (1 - t) * (1 - t);
            var scale = Mathf.Lerp(SlamStartScale, 1f, eased);
            TitleRect.localScale = new Vector3(scale, scale, 1);
            var colour = Title.color;
            colour.a = Mathf.Clamp01(Age / (SlamSeconds * 0.4f));
            Title.color = colour;
            if (Age >= LetteringSeconds && !Box.activeSelf) Box.SetActive(true);
        }
    }
}
