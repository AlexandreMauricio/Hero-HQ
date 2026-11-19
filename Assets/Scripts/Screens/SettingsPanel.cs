using System;
using UnityEngine;
using UnityEngine.UI;

namespace HeroHQ.Screens
{
    /// <summary>
    /// The gear and the settings panel (P2b-05; 02 GDD 17.8; 03 Architecture 8.1 A5), drawn by code like every other box.
    /// The <b>gear</b> sits in the top strip's right corner (120 px, clear of the turn bar). The <b>panel</b> covers the
    /// whole screen with a dark shade that takes every tap (so nothing under it can be pressed while it is open) and holds
    /// <b>Cut-ins</b>: <i>first time full, then short</i> / <i>always full</i> / <i>off</i>, then <b>Resume</b> and
    /// <b>Quit fight</b>. This class only draws and passes taps on; <see cref="BattleScreen"/> pauses the game, keeps the
    /// choice and restarts the fight.
    ///
    /// The gear and the panel each carry their own small Canvas with "override sorting" (a Unity switch that lets a box
    /// draw and take taps above its siblings whatever the order they were made in), so the gear can still be pressed during
    /// a cut-in, which is made later and covers the whole screen, and the panel draws over a cut-in that is already playing.
    /// </summary>
    public sealed class SettingsPanel
    {
        /// <summary>17.1: a must-press button is at least 144 px; the gear is 120 px by 17.8 and 03 8.1 A5 (the top strip is 146 px tall).</summary>
        public const float GearSize = 120, OptionHeight = 170, ActionHeight = 150, MinPress = 144;
        public const int GearOrder = 10, PanelOrder = 20;

        /// <summary>Taps, passed on to BattleScreen.</summary>
        public Action GearTapped, ResumeTapped, QuitTapped;
        public Action<CutInSetting> CutInChosen;

        public RectTransform Gear { get; private set; }
        public GameObject Panel { get; private set; }
        /// <summary>The full-screen dark shade: it takes every tap while the panel is open.</summary>
        public Image Shade { get; private set; }
        public RectTransform Box { get; private set; }
        public Text Title { get; private set; }
        public Text CutInsLabel { get; private set; }
        public Text PausedNote { get; private set; }
        /// <summary>The three Cut-ins choices in the order of <see cref="CutInSetting"/>.</summary>
        public UnityEngine.UI.Button[] CutInButtons { get; } = new UnityEngine.UI.Button[3];
        public Text[] CutInTexts { get; } = new Text[3];
        public UnityEngine.UI.Button ResumeButton { get; private set; }
        public UnityEngine.UI.Button QuitButton { get; private set; }
        public Text ResumeText { get; private set; }
        public Text QuitText { get; private set; }

        readonly Image[] optionFrames = new Image[3];
        CutInSetting shown;

        public bool IsOpen => Panel != null && Panel.activeSelf;
        /// <summary>The choice that is lit.</summary>
        public CutInSetting Shown => shown;

        static readonly string[] Words = { "First time full,\nthen short", "Always full", "Off" };
        static readonly float[] Widths = { 400, 320, 240 };
        const float OptionGap = 30, BoxWidth = 1140, BoxHeight = 760;

        /// <summary>Builds the gear in <paramref name="topStrip"/> and the panel under <paramref name="canvas"/> (hidden).</summary>
        public void Build(RectTransform topStrip, Transform canvas)
        {
            BuildGear(topStrip);
            BuildPanel(canvas);
        }

        void BuildGear(RectTransform topStrip)
        {
            Gear = BattleView.NewRect("Gear", topStrip);
            Gear.anchorMin = Gear.anchorMax = new Vector2(1, 1);
            Gear.pivot = new Vector2(1, 1);
            Gear.sizeDelta = new Vector2(GearSize, GearSize);
            Gear.anchoredPosition = new Vector2(0, -(146 - GearSize) / 2);   // centred in the 146 px strip

            var face = Gear.gameObject.AddComponent<Image>();
            face.sprite = BattleView.Rounded;
            face.type = Image.Type.Sliced;
            face.color = BattleView.Grey;
            var button = Gear.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => GearTapped?.Invoke());
            var inside = BattleView.Panel("Inside", Gear, BattleView.Body, BattleView.Rounded);
            inside.type = Image.Type.Sliced;
            inside.rectTransform.Inset(5);

            // A gear is a ring of teeth round a hole: the eight-pointed burst drawn for bad effects, with a dark hole.
            var teeth = BattleView.Panel("Teeth", Gear, BattleView.MissGrey, BattleView.Spiked);
            teeth.rectTransform.Inset(18);
            BattleView.Panel("Hole", Gear, BattleView.Body, BattleView.Rounded).rectTransform.Inset(44);

            OnTop(Gear.gameObject, GearOrder);
        }

        void BuildPanel(Transform canvas)
        {
            var root = BattleView.NewRect("Settings panel", canvas);
            root.Inset(0);
            Panel = root.gameObject;
            Shade = Panel.AddComponent<Image>();
            Shade.color = new Color(0, 0, 0, 0.8f);
            Shade.raycastTarget = true;   // nothing under the panel can be pressed while it is open
            OnTop(Panel, PanelOrder);

            Box = BattleView.NewRect("Box", root);
            Box.anchorMin = Box.anchorMax = new Vector2(0.5f, 0.5f);
            Box.sizeDelta = new Vector2(BoxWidth, BoxHeight);
            BattleView.Panel("Edge", Box, BattleView.Grey);
            var body = BattleView.Panel("Body", Box, BattleView.Body).rectTransform;
            body.Inset(6);

            Title = BattleView.Label("Title", Box, "Settings", 72, Color.white, TextAnchor.MiddleCenter);
            Title.fontStyle = FontStyle.Bold;
            Title.rectTransform.Band(24, 124, left: 0, right: 0);

            CutInsLabel = BattleView.Label("Cut-ins", Box, "Cut-ins", 48, BattleView.Gold, TextAnchor.MiddleLeft);
            CutInsLabel.fontStyle = FontStyle.Bold;
            CutInsLabel.rectTransform.Band(146, 206, left: 60, right: 60);

            var x = 60f;
            for (int i = 0; i < 3; i++)
            {
                var setting = (CutInSetting)i;
                var (button, frame, text) = Choice(Box, Words[i], Widths[i], OptionHeight, 40);
                button.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, -226);
                button.onClick.AddListener(() => CutInChosen?.Invoke(setting));
                CutInButtons[i] = button;
                optionFrames[i] = frame;
                CutInTexts[i] = text;
                x += Widths[i] + OptionGap;
            }

            PausedNote = BattleView.Label("Paused", Box, "The fight is paused.", 44, BattleView.MissGrey, TextAnchor.MiddleCenter);
            PausedNote.rectTransform.Band(424, 494, left: 0, right: 0);

            var (resume, resumeFrame, resumeText) = Choice(Box, "Resume", 480, ActionHeight, 56);
            resume.GetComponent<RectTransform>().anchoredPosition = new Vector2(60, -540);
            resumeFrame.color = BattleView.Blue;
            resume.onClick.AddListener(() => ResumeTapped?.Invoke());
            ResumeButton = resume;
            ResumeText = resumeText;

            var (quit, quitFrame, quitText) = Choice(Box, "Quit fight", 480, ActionHeight, 56);
            quit.GetComponent<RectTransform>().anchoredPosition = new Vector2(BoxWidth - 60 - 480, -540);
            quitFrame.color = BattleView.Red;
            quit.onClick.AddListener(() => QuitTapped?.Invoke());
            QuitButton = quit;
            QuitText = quitText;

            Panel.SetActive(false);
        }

        // A button: a coloured frame with a dark face and a label, top-left anchored at (x, -y) in the box.
        static (UnityEngine.UI.Button button, Image frame, Text text) Choice(RectTransform parent, string words, float width, float height, int size)
        {
            var rect = BattleView.NewRect(words.Replace("\n", " "), parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(width, height);
            var frame = rect.gameObject.AddComponent<Image>();
            frame.color = BattleView.Grey;
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = frame;
            button.transition = Selectable.Transition.None;
            var face = BattleView.Panel("Face", rect, BattleView.Body).rectTransform;
            face.Inset(6);
            var text = BattleView.Label("Label", face, words, size, Color.white, TextAnchor.MiddleCenter);
            text.fontStyle = FontStyle.Bold;
            return (button, frame, text);
        }

        // Draws and takes taps above everything made before or after it (a cut-in is made later and covers the screen).
        static void OnTop(GameObject box, int order)
        {
            var canvas = box.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = order;
            box.AddComponent<GraphicRaycaster>();
        }

        /// <summary>Lights the chosen Cut-ins button (blue frame; the others grey).</summary>
        public void ShowChoice(CutInSetting setting)
        {
            shown = setting;
            for (int i = 0; i < 3; i++)
                optionFrames[i].color = i == (int)setting ? BattleView.Blue : BattleView.Grey;
        }

        public void Show(CutInSetting current)
        {
            ShowChoice(current);
            Panel.SetActive(true);
        }

        public void Hide() => Panel.SetActive(false);
    }
}
