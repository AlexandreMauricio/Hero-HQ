using System;
using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules;
using HeroHQ.Rules.Data;
using UnityEngine;
using UnityEngine.UI;

namespace HeroHQ.Screens
{
    // Builds every box of the battle screen by code under one Canvas, and knows no rules (03 Architecture 7.2).
    // P2a-04: the canvas scaled from 1920 x 1080, the safe-area panel and the four zones of 02 GDD 17.3.
    // P2a-07: the ten figures at their formation's places, the turn-order bar (17.4, no sliding yet), the seed in a
    // corner and the end text. P2a-08: the beats, numbers and banner. P2a-09: the three buttons, the turn line and the
    // info strip in the bottom band, and the taps (passed on to BattleScreen through HeroTapped, EmptyTapped, ButtonTapped).
    // P2b-04: the bar's entries slide, and the three buttons exist only while your hero is choosing (they slide in and out;
    // the bottom band's right corner shows the acting hero's face and turn line the rest of the time).
    // P2b-05: the gear in the top strip and the settings panel (SettingsPanel); ClearShow takes a cut-in and banner away.
    public class BattleView
    {
        const float ReferenceWidth = 1920, ReferenceHeight = 1080;
        const float SideMargin = 90; // 02 GDD 17.1: what the player must see or press stays 90 px from the sides

        // 02 GDD 17.1's colours: yours blue, the rival red, gold for the ultimate, green for a buff.
        public static readonly Color Blue = new Color32(0x2F, 0x7B, 0xEA, 0xFF);
        public static readonly Color Red = new Color32(0xE0, 0x43, 0x3D, 0xFF);
        public static readonly Color Gold = new Color32(0xF2, 0xC2, 0x30, 0xFF);
        public static readonly Color Green = new Color32(0x3B, 0xB2, 0x73, 0xFF);
        public static readonly Color LightBlue = new Color32(0x7F, 0xD3, 0xFF, 0xFF);
        internal static readonly Color Grey = new Color32(0x5A, 0x61, 0x70, 0xFF);
        internal static readonly Color Body = new Color32(0x26, 0x2C, 0x38, 0xFF);
        internal static readonly Color Track = new Color32(0x10, 0x13, 0x1A, 0xFF);
        internal static readonly Color HealthColor = new Color32(0x8E, 0x9B, 0xB0, 0xFF);
        internal static readonly Color Background = new Color32(0x1E, 0x24, 0x30, 0xFF);   // the camera's clear colour
        /// <summary>17.1: amber is a hit chance below 100 %.</summary>
        public static readonly Color Amber = new Color32(0xFF, 0xA8, 0x2E, 0xFF);
        internal static readonly Color ShieldColor = new Color32(0xE6, 0xEA, 0xF2, 0xFF);

        const float NowSize = 140, EntrySize = 120, EntryGap = 12;   // 17.4: NOW larger; the top strip is 146 px tall

        /// <summary>One entry of the turn-order bar as drawn.</summary>
        public sealed class BarEntry
        {
            public Fighter Who;
            public bool Skipped, Now, Diamond, Dimmed;
            public RectTransform Rect;
            public Text Initials;
            public GameObject Strike;
            /// <summary>P2b-03 (17.4): the white outline a hold on one of this hero's entries lights on all of them (a UI Outline effect on the frame, off until asked).</summary>
            public Outline LitOutline;
            public Hold Hold;
            public bool Lit => LitOutline != null && LitOutline.enabled;
        }

        public Canvas Canvas { get; private set; }
        public RectTransform SafeArea { get; private set; }
        public RectTransform TopStrip { get; private set; }
        public RectTransform Banner { get; private set; }
        public RectTransform Field { get; private set; }
        public RectTransform BottomBand { get; private set; }

        /// <summary>The bar's entries live here, centred in the top strip.</summary>
        public RectTransform BarRow { get; private set; }
        public Text Seed { get; private set; }
        public GameObject End { get; private set; }
        public Text EndTitle { get; private set; }
        public Text EndLine { get; private set; }
        public UnityEngine.UI.Button EndButton { get; private set; }

        readonly Dictionary<Fighter, HeroFigure> figures = new Dictionary<Fighter, HeroFigure>();
        readonly List<BarEntry> bar = new List<BarEntry>();

        public IReadOnlyDictionary<Fighter, HeroFigure> Figures => figures;
        public IReadOnlyList<BarEntry> Bar => bar;

        Rect fittedSafeArea;
        Vector2 fittedScreen;

        // ---- taps (P2a-09): BattleScreen sets these; the view only passes the taps on ---------------------------------

        /// <summary>A hero's box was tapped.</summary>
        public Action<Fighter> HeroTapped;
        /// <summary>The field was tapped where no hero stands.</summary>
        public Action EmptyTapped;
        /// <summary>One of the three skill buttons was tapped.</summary>
        public Action<HeroHQ.Rules.Button> ButtonTapped;

        // ---- holds (P2b-03; 02 GDD 17.1, 17.4, 17.5): BattleScreen sets these; the view only passes them on ------------

        /// <summary>A finger has been held 0.4 s on a hero's figure (<c>false</c>) or on one of their turn-bar entries (<c>true</c>).</summary>
        public Action<Fighter, bool> HeroHeld;
        /// <summary>A finger has been held 0.4 s on a skill button.</summary>
        public Action<HeroHQ.Rules.Button> ButtonHeld;
        /// <summary>The finger lifted after any hold (or the held box was destroyed): the card closes, the lights go out.</summary>
        public Action HoldEnded;

        /// <summary>The fight on show (set by <see cref="ShowFight"/>); the hero card reads the class and trait names from its data.</summary>
        public Fight Fight { get; private set; }

        /// <summary>The hero card's box (P2b-03).</summary>
        public HeroCardView Card { get; } = new HeroCardView();

        /// <summary>The gear in the top strip and the settings panel (P2b-05).</summary>
        public SettingsPanel Settings { get; } = new SettingsPanel();

        public void Build(Transform parent)
        {
            var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            Canvas = go.GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Scale with the screen, keeping the full 1080 height: a wider phone shows more at the sides (17.1).
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1;

            SafeArea = NewRect("Safe area", Canvas.transform);
            SafeArea.anchorMin = Vector2.zero;
            SafeArea.anchorMax = Vector2.one;
            FitSafeArea();

            // Later siblings draw on top: the banner overlaps the top of the field and must be above it.
            TopStrip = Zone("Top strip", 24, 170);
            Field = Zone("Field", 190, 780);
            Banner = Zone("Banner", 170, 240);
            BottomBand = Zone("Bottom band", 800, 1050);

            BarRow = NewRect("Turn-order bar", TopStrip);
            BarRow.Inset(0);

            // The seed, small in the top-left corner, so a strange fight can be replayed (7.2).
            Seed = Label("Seed", TopStrip, "", 28, Grey, TextAnchor.UpperLeft);
            Seed.rectTransform.Band(0, 40, left: 0, right: 1400);

            // A tap on the field where no box stands takes the armed skill back (17.5). The boxes, made later, sit on top.
            var fieldTap = Field.gameObject.AddComponent<Image>();
            fieldTap.color = Color.clear;
            var fieldButton = Field.gameObject.AddComponent<UnityEngine.UI.Button>();
            fieldButton.targetGraphic = fieldTap;
            fieldButton.transition = Selectable.Transition.None;
            fieldButton.onClick.AddListener(() => EmptyTapped?.Invoke());

            BuildBottomBand();
            BuildEnd();
            Card.Build(SafeArea);   // last, so it draws over everything in the safe area
            Settings.Build(TopStrip, Canvas.transform);   // P2b-05: both carry their own sorting, so they stay above a cut-in
        }

        // ---- the fight on the field ------------------------------------------------------------------------------

        /// <summary>
        /// A new fight: the old figures, bar and end text go; the ten heroes stand at their formation's <c>at</c>
        /// (x from the middle outward, y down from the field's top), yours on the left, the rival's side mirrored.
        /// </summary>
        public void ShowFight(Fight fight, FormationDef players, FormationDef enemies)
        {
            Fight = fight;
            // The lists go first: a box destroyed while held calls HoldEnded, which walks them (P2b-03).
            figures.Clear();
            bar.Clear();
            Clear(Field);
            Clear(BarRow);
            End.SetActive(false);
            foreach (var (team, formation) in new[] { (fight.Players, players), (fight.Enemies, enemies) })
                foreach (var hero in team)
                {
                    var at = formation.Slots[hero.Slot].At;
                    var x = (float)at[0] * (hero.Side == Side.Player ? -1 : 1);
                    var figure = new HeroFigure(hero, Field, new Vector2(x, -(float)at[1]));
                    // P2b-03: the click that ends a hold is not a tap (the hold's own rule), so it is asked first.
                    figure.Tap.onClick.AddListener(() => { if (!figure.Hold.ConsumeClick()) HeroTapped?.Invoke(hero); });
                    figure.Hold.Started = () => HeroHeld?.Invoke(hero, false);
                    figure.Hold.Ended = () => HoldEnded?.Invoke();
                    figure.ShowShield(Targeting.Covered(fight, hero));
                    figures[hero] = figure;
                }
            HideButtons(slide: false);   // a new fight starts with the buttons away, mid-slide or not
            ShowActing(null);
        }

        /// <summary>
        /// After an action (7.2): Health and charge as <see cref="Playback"/> counted them from the events (finding 3),
        /// the effects and the Barrier from the fighters, and the shields from <see cref="Targeting.Covered"/> (a back-row
        /// hero loses theirs when the last hero covering them has fallen). A hero whose shown Health is 0 has fallen (R22).
        /// </summary>
        public void SyncAfterAction(Fight fight, Playback playback)
        {
            foreach (var pair in figures)
            {
                var hero = pair.Key;
                var figure = pair.Value;
                var health = playback.Health(hero);
                if (health == 0)
                {
                    figure.ShowKnockedOut();
                    continue;
                }
                figure.ShowHealth(health, hero.MaxHealth, BarrierOf(hero));
                figure.ShowCharge(playback.Charge(hero));
                figure.ShowEffects(hero.Effects, hero.TurnsStarted);
                figure.ShowShield(Targeting.Covered(fight, hero));
            }
        }

        /// <summary>The ring goes on the acting hero (null: nobody), and the bottom band says whose turn it is (17.3).</summary>
        public void ShowActing(Fighter acting)
        {
            foreach (var pair in figures) pair.Value.ShowActing(pair.Key == acting);
            TurnLine.text = acting == null ? "" : $"{acting.Hero.ShortName}'s turn";
            TurnLine.color = acting != null && acting.Side == Side.Enemy ? Red : Blue;
            actingNow = acting;
            ShowCorner();
        }

        // ---- the bottom band: whose turn, the info strip, the three buttons (P2a-09; 02 GDD 17.3, 17.5) ----------------

        /// <summary>How a skill button looks (17.5's states).</summary>
        public enum ButtonLook
        {
            Ready,
            Armed,
            /// <summary>The Special on cooldown: grey, with the turns left.</summary>
            Waiting,
            /// <summary>The Ultimate under 100: grey, with the charge.</summary>
            Charging,
            UltimateReady,
            /// <summary>Not your hero's choice (an enemy's turn, the show, the result): the buttons are off the screen (17.3.1, OWNER 2026-11-18).</summary>
            Hidden,
        }

        /// <summary>One of the three skill buttons as drawn.</summary>
        public sealed class SkillButton
        {
            public HeroHQ.Rules.Button Kind;
            public UnityEngine.UI.Button Ui;
            public RectTransform Rect;
            public Image Frame;
            public Text Name;
            /// <summary>The big line: the turns left, the charge, READY, or nothing.</summary>
            public Text State;
            public RectTransform ChargeFill;
            public GameObject Outline;
            public CanvasGroup Group;
            public Hold Hold;
            public ButtonLook Look;
            public float RestY;
            /// <summary>P2b-02 (17.5): the small symbols for the skill's pieces and the target mark, in one row near the bottom.</summary>
            public RectTransform SymbolRow;
            public SkillDef Shown;
            public readonly List<EffectLook.Symbol> Symbols = new List<EffectLook.Symbol>();
            public EffectLook.Reach? Reach;
            /// <summary>The dots (or the one ring, for self) of the target mark.</summary>
            public readonly List<Image> Dots = new List<Image>();
        }

        /// <summary>"Skipjack's turn" in the bottom band's left part.</summary>
        public Text TurnLine { get; private set; }
        /// <summary>The info strip: the armed skill's sentence, or why a tap did nothing (17.5).</summary>
        public Text InfoStrip { get; private set; }

        readonly List<SkillButton> buttons = new List<SkillButton>();
        /// <summary>Basic, Special, Ultimate, in that order.</summary>
        public IReadOnlyList<SkillButton> Buttons => buttons;

        /// <summary>Button sizes (17.1: at least 144 px; 17.3: the Ultimate largest, nearest the right thumb).</summary>
        public const float ButtonWidth = 230, ButtonHeight = 200, UltimateWidth = 270, UltimateHeight = 230, ButtonGap = 24;

        void BuildBottomBand()
        {
            // Left part: the turn line and the info strip, clear of the buttons.
            var buttonsWidth = 2 * ButtonWidth + UltimateWidth + 3 * ButtonGap;
            TurnLine = Label("Turn", BottomBand, "", 44, Blue, TextAnchor.MiddleLeft);
            TurnLine.fontStyle = FontStyle.Bold;
            TurnLine.rectTransform.Band(0, 56, left: 0, right: buttonsWidth);
            InfoStrip = Label("Info strip", BottomBand, "", 40, Color.white, TextAnchor.UpperLeft);
            InfoStrip.rectTransform.Band(64, 250, left: 0, right: buttonsWidth);
            InfoStrip.horizontalOverflow = HorizontalWrapMode.Wrap;
            InfoStrip.resizeTextForBestFit = true;   // a long sentence shrinks a little, never under 32 px (17.1)
            InfoStrip.resizeTextMinSize = 32;
            InfoStrip.resizeTextMaxSize = 40;

            // Right part: the corner (the acting hero's face and turn, shown while the buttons are away), then the three
            // buttons on top of it, in one box that slides them in and out together (P2b-04).
            BuildCorner();
            buttonsRoot = NewRect("Buttons", BottomBand);
            buttonsRoot.Inset(0);
            buttonsGroup = buttonsRoot.gameObject.AddComponent<CanvasGroup>();
            buttonsMover = buttonsRoot.gameObject.AddComponent<Mover>();
            var right = 0f;
            foreach (var kind in new[] { HeroHQ.Rules.Button.Ultimate, HeroHQ.Rules.Button.Special, HeroHQ.Rules.Button.Basic })
            {
                var ultimate = kind == HeroHQ.Rules.Button.Ultimate;
                var size = ultimate ? new Vector2(UltimateWidth, UltimateHeight) : new Vector2(ButtonWidth, ButtonHeight);
                buttons.Insert(0, SkillButtonAt(kind, size, right));
                right += size.x + ButtonGap;
            }
            foreach (var b in buttons)
            {
                b.Name.text = b.Kind.ToString();
                Draw(b, ButtonLook.Hidden, "", 0);
            }
            HideButtons(slide: false);
        }

        SkillButton SkillButtonAt(HeroHQ.Rules.Button kind, Vector2 size, float fromRight)
        {
            var rect = NewRect(kind.ToString(), buttonsRoot);
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(1, 0);
            rect.sizeDelta = size;
            var restY = 10f;
            rect.anchoredPosition = new Vector2(-fromRight, restY);
            var group = rect.gameObject.AddComponent<CanvasGroup>();

            // The armed outline sits behind the frame, so it shows as a thick white edge.
            var outline = Panel("Armed outline", rect, Color.white);
            outline.rectTransform.Inset(-8);
            outline.gameObject.SetActive(false);

            var frame = Panel("Frame", rect, Blue);
            frame.raycastTarget = true;   // the frame catches the tap
            var ui = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            ui.targetGraphic = frame;
            ui.transition = Selectable.Transition.None;
            var hold = rect.gameObject.AddComponent<Hold>();   // P2b-03 (17.5): a hold reads the sentence and arms nothing
            ui.onClick.AddListener(() => { if (!hold.ConsumeClick()) ButtonTapped?.Invoke(kind); });
            hold.Started = () => ButtonHeld?.Invoke(kind);
            hold.Ended = () => HoldEnded?.Invoke();
            var body = Panel("Body", frame.rectTransform, Body).rectTransform;
            body.Inset(6);

            var name = Label("Name", body, "", 32, Color.white, TextAnchor.UpperCenter);
            name.fontStyle = FontStyle.Bold;
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            name.rectTransform.Band(6, 82, left: 8, right: 8);
            var state = Label("State", body, "", 56, Color.white, TextAnchor.MiddleCenter);
            state.fontStyle = FontStyle.Bold;
            state.rectTransform.Band(82, size.y - 70, left: 0, right: 0);

            // P2b-02: the symbols row, 28 px tall, just above the charge bar's place (the body is size.y - 12 tall and the
            // charge bar sits at size.y - 30 to size.y - 18, so the row is at size.y - 66 to size.y - 38).
            var symbolRow = NewRect("Symbols", body);
            symbolRow.Band(size.y - 66, size.y - 38, left: SymbolSide, right: SymbolSide);

            var charge = Panel("Charge bar", body, Track).rectTransform;
            charge.Band(size.y - 30, size.y - 18, left: 10, right: 10);
            var fill = Panel("Charge", charge, Gold).rectTransform;
            fill.Span(0, 0);
            charge.gameObject.SetActive(kind == HeroHQ.Rules.Button.Ultimate);

            return new SkillButton
            {
                Kind = kind, Ui = ui, Rect = rect, Frame = frame, Name = name, State = state, ChargeFill = fill,
                Outline = outline.gameObject, Group = group, RestY = restY, SymbolRow = symbolRow, Hold = hold,
            };
        }

        /// <summary>The symbols' size and gap, the target mark's dots and gap, and the row's side margin (reference pixels).</summary>
        public const float SymbolSize = 28, SymbolGap = 4, DotSize = 12, DotGap = 3, SymbolSide = 10;

        /// <summary>
        /// 17.5's small symbols and the target mark on a button: one symbol per kind of piece on the left, the target
        /// mark (one dot, three dots, all five dots, or a ring for self) on the right. Drawn again only when the button's
        /// skill changes. A null skill (the buttons are locked) clears the row.
        /// </summary>
        void ShowSymbols(SkillButton b, SkillDef skill)
        {
            if (b.Shown == skill) return;
            b.Shown = skill;
            Clear(b.SymbolRow);
            b.Symbols.Clear();
            b.Dots.Clear();
            b.Reach = null;
            if (skill == null) return;

            var x = 0f;
            foreach (var symbol in EffectLook.Symbols(skill))
            {
                b.Symbols.Add(symbol);
                var harsh = EffectLook.IsHarsh(symbol);
                var rect = NewRect(symbol.ToString(), b.SymbolRow);
                rect.anchorMin = rect.anchorMax = new Vector2(0, 0.5f);
                rect.pivot = new Vector2(0, 0.5f);
                rect.sizeDelta = new Vector2(SymbolSize, SymbolSize);
                rect.anchoredPosition = new Vector2(x, 0);
                var frame = Panel("Frame", rect, harsh ? Red : Green, harsh ? Spiked : Rounded);
                if (!harsh) frame.type = Image.Type.Sliced;
                Panel("Inside", rect, Body, Rounded).rectTransform.Inset(harsh ? 6 : 3);
                var mark = Label("Mark", rect, EffectLook.Mark(symbol), 20, Color.white, TextAnchor.MiddleCenter);
                mark.fontStyle = FontStyle.Bold;
                x += SymbolSize + SymbolGap;
            }

            // The target mark, against the row's right end.
            var reach = EffectLook.ReachOf(skill);
            b.Reach = reach;
            var count = EffectLook.Dots(reach);
            var width = count * DotSize + (count - 1) * DotGap;
            for (int i = 0; i < count; i++)
            {
                var rect = NewRect(reach == EffectLook.Reach.Self ? "Self ring" : "Dot", b.SymbolRow);
                rect.anchorMin = rect.anchorMax = new Vector2(1, 0.5f);
                rect.pivot = new Vector2(1, 0.5f);
                rect.sizeDelta = new Vector2(DotSize, DotSize);
                rect.anchoredPosition = new Vector2(-(width - (i + 1) * DotSize - i * DotGap), 0);
                var dot = rect.gameObject.AddComponent<Image>();
                dot.sprite = reach == EffectLook.Reach.Self ? Ring : Rounded;
                dot.color = Color.white;
                dot.raycastTarget = false;
                b.Dots.Add(dot);
            }
        }

        /// <summary>
        /// Your hero is choosing (17.5): each button shows its skill's name and its state from <paramref name="options"/>
        /// (<c>Fight.Options</c>), the Special's turns left from <paramref name="cooldown"/>, the Ultimate's charge from
        /// <paramref name="charge"/>; the <paramref name="armed"/> one is raised with a thick outline.
        /// </summary>
        public void ShowButtons(IReadOnlyList<ButtonOption> options, HeroHQ.Rules.Button? armed, int cooldown, double charge)
        {
            ShowButtonsPanel();
            foreach (var b in buttons)
            {
                var option = options[(int)b.Kind];
                b.Name.text = option.Skill.Name;
                ShowSymbols(b, option.Skill);
                var look = !option.Usable
                    ? (b.Kind == HeroHQ.Rules.Button.Ultimate ? ButtonLook.Charging : ButtonLook.Waiting)
                    : armed == b.Kind ? ButtonLook.Armed
                    : b.Kind == HeroHQ.Rules.Button.Ultimate ? ButtonLook.UltimateReady : ButtonLook.Ready;
                var state = look == ButtonLook.Waiting ? cooldown.ToString()
                          : look == ButtonLook.Charging ? Mathf.FloorToInt((float)charge).ToString()
                          : b.Kind == HeroHQ.Rules.Button.Ultimate ? "READY" : "";
                Draw(b, look, state, Mathf.Clamp01((float)(charge / 100)));
            }
        }

        // ---- the buttons only exist on your hero's choice (17.3.1 *No buttons off-turn*, OWNER 2026-11-18) -------------------

        /// <summary>How long the three buttons take to slide in or out (17.3.1), and how far down they go (clear of the screen's bottom edge).</summary>
        public const float ButtonSlideSeconds = 0.15f, ButtonHideDrop = 340;

        RectTransform buttonsRoot;
        CanvasGroup buttonsGroup;
        Mover buttonsMover;

        /// <summary>The three buttons are on the screen (or sliding in): only while one of your heroes is choosing.</summary>
        public bool ButtonsShown { get; private set; }
        /// <summary>The box the three buttons slide in. Its <c>Mover.Home</c> says where it is going: 0 when shown, <see cref="ButtonHideDrop"/> down when hidden.</summary>
        public RectTransform ButtonsBox => buttonsRoot;
        /// <summary>True while the buttons accept taps: shown, and not sliding out.</summary>
        public bool ButtonsTakeTaps => buttonsGroup.blocksRaycasts;

        // Your hero is choosing: the buttons slide up from the bottom (once; later taps only redraw them).
        void ShowButtonsPanel()
        {
            if (ButtonsShown) return;
            ButtonsShown = true;
            buttonsGroup.blocksRaycasts = true;
            buttonsMover.MoveTo(Vector2.zero, ButtonSlideSeconds);
            ShowCorner();
        }

        /// <summary>
        /// Not your hero's choice: the buttons slide out (17.3.1: hidden during playback, cut-ins, the result and every
        /// enemy turn, not greyed) and no tap reaches them from this moment. They keep what they showed while they leave;
        /// the next choice redraws them. <paramref name="slide"/> false puts them away at once (a new fight; the build).
        /// </summary>
        public void HideButtons(bool slide = true)
        {
            ButtonsShown = false;
            buttonsGroup.blocksRaycasts = false;
            var away = new Vector2(0, -ButtonHideDrop);
            if (slide) buttonsMover.MoveTo(away, ButtonSlideSeconds);
            else buttonsMover.SnapTo(away);
            foreach (var b in buttons) b.Look = ButtonLook.Hidden;
            if (InfoStrip != null) InfoStrip.text = "";
            ShowCorner();
        }

        // ---- the corner: whose turn it is while the buttons are away (17.3.1) ---------------------------------------------

        /// <summary>The acting hero's face and "name's turn" in the bottom band's right corner (a placeholder block with the initials, as on the bar).</summary>
        public RectTransform Corner { get; private set; }
        public Text CornerText { get; private set; }
        public Text CornerInitials { get; private set; }
        Image cornerFrame, cornerBadge;
        Fighter actingNow;
        const float CornerWidth = 600, CornerHeight = 200, FaceSize = 160;

        void BuildCorner()
        {
            Corner = NewRect("Acting corner", BottomBand);
            Corner.anchorMin = Corner.anchorMax = new Vector2(1, 0);
            Corner.pivot = new Vector2(1, 0);
            Corner.sizeDelta = new Vector2(CornerWidth, CornerHeight);
            Corner.anchoredPosition = new Vector2(0, 10);

            cornerFrame = Panel("Face", Corner, Blue);
            var face = cornerFrame.rectTransform;
            face.anchorMin = face.anchorMax = new Vector2(1, 0.5f);
            face.pivot = new Vector2(1, 0.5f);
            face.sizeDelta = new Vector2(FaceSize, FaceSize);
            face.anchoredPosition = Vector2.zero;
            var inside = Panel("Inside", face, Body).rectTransform;
            inside.Inset(6);
            CornerInitials = Label("Initials", inside, "", 64, Color.white, TextAnchor.MiddleCenter);
            CornerInitials.fontStyle = FontStyle.Bold;
            cornerBadge = Panel("Badge", inside, Blue, Rounded);
            cornerBadge.rectTransform.anchorMin = cornerBadge.rectTransform.anchorMax = new Vector2(0, 1);
            cornerBadge.rectTransform.sizeDelta = new Vector2(28, 28);
            cornerBadge.rectTransform.anchoredPosition = new Vector2(20, -20);

            CornerText = Label("Turn", Corner, "", 52, Blue, TextAnchor.MiddleRight);
            CornerText.fontStyle = FontStyle.Bold;
            CornerText.rectTransform.offsetMax = new Vector2(-(FaceSize + 16), 0);
            CornerText.horizontalOverflow = HorizontalWrapMode.Wrap;
            CornerText.resizeTextForBestFit = true;
            CornerText.resizeTextMinSize = 36;
            CornerText.resizeTextMaxSize = 52;
            Corner.gameObject.SetActive(false);
        }

        // On while somebody is acting and the buttons are away; yours blue with a round badge, the rival's red with a diamond.
        void ShowCorner()
        {
            if (Corner == null) return;
            Corner.gameObject.SetActive(actingNow != null && !ButtonsShown);
            if (actingNow == null) return;
            var mine = actingNow.Side == Side.Player;
            var name = actingNow.Hero.Name;
            CornerInitials.text = name.Substring(0, Math.Min(2, name.Length));
            CornerText.text = $"{actingNow.Hero.ShortName}'s turn";
            CornerText.color = mine ? Blue : Red;
            cornerFrame.color = mine ? Blue : Red;
            cornerBadge.color = mine ? Blue : Red;
            cornerBadge.sprite = mine ? Rounded : null;
            cornerBadge.rectTransform.localRotation = mine ? Quaternion.identity : Quaternion.Euler(0, 0, 45);
        }

        void Draw(SkillButton b, ButtonLook look, string state, float charge)
        {
            b.Look = look;
            b.State.text = state;
            var grey = look == ButtonLook.Waiting || look == ButtonLook.Charging || look == ButtonLook.Hidden;
            b.Frame.color = look == ButtonLook.UltimateReady || (look == ButtonLook.Armed && b.Kind == HeroHQ.Rules.Button.Ultimate) ? Gold
                          : grey ? Grey : Blue;
            b.State.color = look == ButtonLook.UltimateReady ? Gold : Color.white;
            b.Group.alpha = 1;
            b.Outline.SetActive(look == ButtonLook.Armed);
            b.Rect.anchoredPosition = new Vector2(b.Rect.anchoredPosition.x, b.RestY + (look == ButtonLook.Armed ? 16 : 0));
            b.ChargeFill.Span(0, charge);
        }

        /// <summary>Not choosing any more: every glow, dim, outline and hit chip off, the buttons slide out.</summary>
        public void ClearChoosing()
        {
            foreach (var figure in figures.Values) figure.ShowChoosing(false, false, false, null);
            HideButtons();
        }

        /// <summary>
        /// Takes away whatever the show has put over the battle: a cut-in that is playing, the off look, the banner
        /// (P2b-05: Quit fight mid-cut-in leaves no cut-in behind).
        /// </summary>
        public void ClearShow()
        {
            for (int i = Canvas.transform.childCount - 1; i >= 0; i--)
            {
                var child = Canvas.transform.GetChild(i).gameObject;
                if (child.name == "Cut-in" || child.name == "Off look" || child.name == "Off look flash") Mover.Remove(child);
            }
            Clear(Banner);
        }

        /// <summary>The info strip's words: the armed skill's sentence, a reason, or nothing.</summary>
        public void ShowInfo(string text) => InfoStrip.text = text ?? "";

        // ---- the show (P2a-08; 02 GDD 17.6 and 17.7) -----------------------------------------------------------------

        /// <summary>How long the banner stays, the white flash of the <i>off</i> look, the whole look, and a floating number.</summary>
        public const float BannerSeconds = 0.8f, FlashSeconds = 0.1f, OffLookSeconds = 0.3f, NumberSeconds = 1f;

        public static readonly Color Yellow = new Color32(0xFF, 0xE2, 0x3A, 0xFF);
        public static readonly Color MissGrey = new Color32(0xB4, 0xBA, 0xC6, 0xFF);

        /// <summary>What a floating number says, in which colour and how big (reference pixels).</summary>
        public readonly struct FloatingText
        {
            public FloatingText(string text, Color color, int size)
            {
                Text = text;
                Color = color;
                Size = size;
            }

            public string Text { get; }
            public Color Color { get; }
            public int Size { get; }
        }

        /// <summary>The words of the banner now showing (null until the first one).</summary>
        public Text BannerText { get; private set; }
        /// <summary>The newest floating number (null until the first).</summary>
        public Text LastNumber { get; private set; }
        /// <summary>How many times the <i>off</i> look has played, and the shout of the last one.</summary>
        public int OffLooks { get; private set; }
        public string LastShout { get; private set; }

        /// <summary>
        /// What an event shows over its hero, or null when it shows nothing (17.6): a hit in white 64 px, a critical hit
        /// "CRIT!" over the number in yellow and bigger, a hit the Barrier soaked whole in light blue, MISS in grey, a heal
        /// "+N" in green, WARDED, COUNTER!, and (reading R26) the effect put on in words, green for a buff, red for a bad
        /// one, because "effect put on" gets a beat and a beat needs something to look at.
        /// </summary>
        public static FloatingText? NumberFor(FightEvent e)
        {
            switch (e.Kind)
            {
                case FightEventKind.Damaged:
                    if (e.Crit) return new FloatingText($"CRIT!\n{e.Amount}", Yellow, 88);
                    var soakedWhole = e.Soaked > 0 && e.Soaked >= e.Amount;
                    return new FloatingText(e.Amount.ToString(), soakedWhole ? LightBlue : Color.white, 64);
                case FightEventKind.Missed:
                    return new FloatingText("MISS", MissGrey, 64);
                case FightEventKind.Healed:
                    return new FloatingText("+" + e.Amount, Green, 64);
                case FightEventKind.EffectBlocked:
                    return new FloatingText("WARDED", Color.white, 56);
                case FightEventKind.CounterAttack:
                    return new FloatingText("COUNTER!", Color.white, 56);
                case FightEventKind.EffectApplied:
                    return new FloatingText(SkillText.Words(e.Effect), Effects.IsBad(e.Effect) ? Red : Green, 44);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Shows one event of an action and says how many seconds the screen should now wait. Takes the event into
        /// <paramref name="playback"/> itself, then draws from it: a hit or a heal moves that hero's Health bar to the
        /// Health Playback counts (finding 3, never the fighter's own), a charge change moves the gold bar, a knockout
        /// takes the box away (the hit before it only emptied the bar), and anything that shows over a hero floats its
        /// number there. Waits: a beat for the kinds <see cref="Playback.GetsBeat"/> names, 0.3 s for an ultimate's
        /// <i>off</i> look, 0.8 s for a skipped turn's banner, nothing for bookkeeping. Effect chips and the Barrier
        /// are redrawn after the whole action by <see cref="SyncAfterAction"/> (P2a-08's plan).
        /// </summary>
        public float ShowEvent(FightEvent e, Playback playback, float beatSeconds)
        {
            playback.Apply(e);
            HeroFigure figure = null;
            if (e.Who != null) figures.TryGetValue(e.Who, out figure);

            switch (e.Kind)
            {
                case FightEventKind.SkillUsed:
                    if (e.Button != HeroHQ.Rules.Button.Ultimate) return 0;
                    ShowOffLook(e.Who.Hero.Cutin.Shout);
                    return OffLookSeconds;
                case FightEventKind.TurnSkipped:
                    ShowBanner($"{e.Who.Hero.Name} is stunned: turn skipped");
                    return BannerSeconds;
                case FightEventKind.ChargeChanged:
                    figure?.ShowCharge(playback.Charge(e.Who));
                    break;
                case FightEventKind.Damaged:
                case FightEventKind.Healed:
                    figure?.ShowHealth(playback.Health(e.Who), e.Who.MaxHealth, BarrierOf(e.Who));
                    break;
                case FightEventKind.KnockedOut:
                    figure?.ShowKnockedOut();
                    break;
            }

            var number = NumberFor(e);
            if (number.HasValue && figure != null) FloatNumber(figure, number.Value);
            return Playback.GetsBeat(e.Kind) ? beatSeconds : 0;
        }

        static int BarrierOf(Fighter hero) => hero.Effects.FirstOrDefault(e => e.Kind == "barrier")?.Amount ?? 0;

        void FloatNumber(HeroFigure figure, FloatingText number)
        {
            var label = Label("Number", Field, number.Text, number.Size, number.Color, TextAnchor.MiddleCenter);
            label.fontStyle = FontStyle.Bold;
            label.gameObject.AddComponent<Outline>().effectDistance = new Vector2(3, -3);   // readable on any box
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
            rect.sizeDelta = new Vector2(HeroFigure.Width, 220);
            rect.anchoredPosition = figure.Root.anchoredPosition + new Vector2(0, 20);
            var floater = label.gameObject.AddComponent<Floater>();
            floater.seconds = NumberSeconds;
            floater.rise = 70;
            LastNumber = label;
        }

        /// <summary>
        /// The banner just under the turn bar (R20): "Strix uses Mark" for an enemy action, "&lt;name&gt; is stunned:
        /// turn skipped". It replaces the one before and goes by itself after <see cref="BannerSeconds"/>.
        /// </summary>
        public void ShowBanner(string text)
        {
            Clear(Banner);
            var box = Panel("Banner box", Banner, new Color(0, 0, 0, 0.75f));
            BannerText = Label("Text", box.rectTransform, text, 48, Color.white, TextAnchor.MiddleCenter);
            BannerText.fontStyle = FontStyle.Bold;
            var floater = box.gameObject.AddComponent<Floater>();
            floater.seconds = BannerSeconds;
            floater.fade = false;
        }

        /// <summary>
        /// An enemy is about to act (17.7): the banner says what it does and its target is outlined in red; the screen
        /// holds a beat, then calls <see cref="ClearAim"/>. A skill with no tap (it needs no chosen hero) outlines nobody.
        /// </summary>
        public void ShowEnemyAction(Fighter actor, Choice choice)
        {
            var hero = actor.Hero;
            var skill = choice.Button == HeroHQ.Rules.Button.Basic ? hero.Basic
                      : choice.Button == HeroHQ.Rules.Button.Special ? hero.Special : hero.Ultimate;
            ShowBanner($"{hero.Name} uses {skill.Name}");
            if (choice.Target != null && figures.TryGetValue(choice.Target, out var figure)) figure.ShowTargeted(true);
        }

        public void ClearAim()
        {
            foreach (var figure in figures.Values) figure.ShowTargeted(false);
        }

        /// <summary>
        /// The <i>off</i> look of an ultimate (01 Vision, 17.6): a 0.1 s white flash over the whole screen, then the
        /// hero's shout across a black band in the middle, 0.3 s in all. Since P2b-01 it is what the "off" setting shows;
        /// full and short play the real cut-in (<see cref="CutIn"/>, started by BattleScreen.Show).
        /// </summary>
        void ShowOffLook(string shout)
        {
            var band = Panel("Off look", Canvas.transform, Color.black);
            var rect = band.rectTransform;
            rect.anchorMin = new Vector2(0, 0.5f);
            rect.anchorMax = new Vector2(1, 0.5f);
            rect.sizeDelta = new Vector2(0, 220);
            rect.anchoredPosition = Vector2.zero;
            var text = Label("Shout", rect, shout, 110, Color.white, TextAnchor.MiddleCenter);
            text.fontStyle = FontStyle.Bold;
            var life = band.gameObject.AddComponent<Floater>();
            life.seconds = OffLookSeconds;
            life.fade = false;

            var flash = Panel("Off look flash", Canvas.transform, Color.white);
            var flashLife = flash.gameObject.AddComponent<Floater>();
            flashLife.seconds = FlashSeconds;

            OffLooks++;
            LastShout = shout;
        }

        // ---- the turn-order bar (17.4) ---------------------------------------------------------------------------

        /// <summary>How long the bar's entries take to slide to their new places (17.4).</summary>
        public const float BarSlideSeconds = 0.3f;

        /// <summary>The centre x of entry <paramref name="index"/> in a bar of <paramref name="count"/> entries: NOW larger, the bar centred.</summary>
        public static float BarX(int count, int index)
        {
            var width = count == 0 ? 0 : NowSize + (count - 1) * (EntrySize + EntryGap);
            var left = -width / 2 + (index == 0 ? 0 : NowSize + EntryGap + (index - 1) * (EntrySize + EntryGap));
            return left + (index == 0 ? NowSize : EntrySize) / 2;
        }

        /// <summary>
        /// Redraws the bar from <c>PredictTurns</c>: next to act on the left, the first one larger with NOW; a blue
        /// frame and round badge for yours, red with a diamond for the rival; the first two letters of the name; a
        /// stunned (skipped) turn dimmed and struck through. P2b-04 (17.4): when the order has changed, the entries slide
        /// to their new places in 0.3 s instead of jumping: the first one (who has acted) slides out to the left, each
        /// other entry goes from where its hero's entry was to where it now is, a new one arrives from the right, and an
        /// entry with no place left (a hero who fell) fades where it stood. The slides run on their own and add no waiting.
        /// </summary>
        public void ShowBar(IReadOnlyList<PredictedTurn> turns)
        {
            var old = bar.ToList();
            bar.Clear();   // first: see ShowFight
            var same = old.Count == turns.Count;
            for (int i = 0; same && i < old.Count; i++) same = old[i].Who == turns[i].Who && old[i].Skipped == turns[i].Skipped;
            if (same || old.Count == 0) Clear(BarRow);   // nothing moved: a plain redraw

            // Which old entry each new one comes from (the first old one has acted, so it is never one of them).
            var pool = old.Skip(1).ToList();
            var from = new float[turns.Count];
            for (int i = 0; i < turns.Count; i++)
            {
                var came = same ? null : pool.FirstOrDefault(e => e.Who == turns[i].Who);
                if (came != null) pool.Remove(came);
                from[i] = came != null ? came.Rect.anchoredPosition.x
                        : same || old.Count == 0 ? BarX(turns.Count, i)
                        : BarX(turns.Count, turns.Count - 1) + EntrySize + EntryGap;   // arrives from the right
                if (came != null) Mover.Remove(came.Rect.gameObject);
            }

            // What is left of the old bar leaves: the first slides out to the left, the others fade where they stood.
            if (!same && old.Count > 0)
            {
                foreach (var leaving in pool.Prepend(old[0]).Where(e => e.Rect != null))
                {
                    leaving.Rect.GetComponent<Image>().raycastTarget = false;   // not in the way of the new entries
                    leaving.Hold.enabled = false;
                    var place = leaving.Rect.anchoredPosition;
                    var away = leaving == old[0] ? place - new Vector2(NowSize + EntryGap, 0) : place;
                    leaving.Rect.gameObject.AddComponent<Mover>().MoveTo(away, BarSlideSeconds, vanish: true);
                }
            }

            for (int i = 0; i < turns.Count; i++)
            {
                var now = i == 0;
                var entry = Entry(turns[i], now, now ? NowSize : EntrySize, from[i]);
                bar.Add(entry);
                var to = BarX(turns.Count, i);
                if (Mathf.Approximately(from[i], to)) continue;
                entry.Rect.gameObject.AddComponent<Mover>().MoveTo(new Vector2(to, 0), BarSlideSeconds);
            }
        }

        BarEntry Entry(PredictedTurn turn, bool now, float size, float x)
        {
            var who = turn.Who;
            var mine = who.Side == Side.Player;

            var frame = Panel(who.Hero.Name, BarRow, mine ? Blue : Red);
            var rect = frame.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(x, 0);
            // A hold on an entry opens that hero's card (17.4); a plain tap on it does nothing, so there is no Button.
            frame.raycastTarget = true;
            var lit = frame.gameObject.AddComponent<Outline>();   // a thick white edge round the frame while lit
            lit.effectColor = Color.white;
            lit.effectDistance = new Vector2(8, -8);
            lit.enabled = false;
            var hold = frame.gameObject.AddComponent<Hold>();
            hold.Started = () => HeroHeld?.Invoke(who, true);
            hold.Ended = () => HoldEnded?.Invoke();
            var inside = Panel("Inside", rect, Body).rectTransform;
            inside.Inset(6);

            var initials = Label("Initials", inside, who.Hero.Name.Substring(0, Math.Min(2, who.Hero.Name.Length)),
                                 now ? 56 : 48, Color.white, TextAnchor.MiddleCenter);
            initials.fontStyle = FontStyle.Bold;
            if (now)
            {
                initials.rectTransform.Band(0, 90, left: 0, right: 0);
                var tag = Label("NOW", inside, "NOW", 32, Gold, TextAnchor.MiddleCenter);
                tag.fontStyle = FontStyle.Bold;
                tag.rectTransform.Band(84, 124, left: 0, right: 0);
            }

            // The side's badge in the top-left corner: a circle for yours, a square turned 45 degrees for the rival.
            var badge = Panel("Badge", inside, mine ? Blue : Red, mine ? Rounded : null);
            badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = new Vector2(0, 1);
            badge.rectTransform.sizeDelta = new Vector2(24, 24);
            badge.rectTransform.anchoredPosition = new Vector2(18, -18);
            if (!mine) badge.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);

            var strike = Panel("Struck through", rect, Color.white);
            strike.rectTransform.anchorMin = strike.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            strike.rectTransform.sizeDelta = new Vector2(size * 1.3f, 6);
            strike.rectTransform.localRotation = Quaternion.Euler(0, 0, -45);
            strike.gameObject.SetActive(turn.Skipped);
            if (turn.Skipped) frame.gameObject.AddComponent<CanvasGroup>().alpha = 0.4f;

            return new BarEntry
            {
                Who = who, Skipped = turn.Skipped, Now = now, Diamond = !mine, Dimmed = turn.Skipped,
                Rect = rect, Initials = initials, Strike = strike.gameObject, LitOutline = lit, Hold = hold,
            };
        }

        // ---- holds: what a hold shows (P2b-03) -------------------------------------------------------------------------

        /// <summary>Opens the hero card with this content.</summary>
        public void ShowCard(HeroCard card) => Card.Show(card);

        /// <summary>
        /// A hold on a turn-bar entry (17.4): every entry of <paramref name="hero"/> lights up and their figure on the
        /// field gets the bright ring. Null puts every light out.
        /// </summary>
        public void ShowSpot(Fighter hero)
        {
            foreach (var entry in bar) entry.LitOutline.enabled = hero != null && entry.Who == hero;
            foreach (var pair in figures) pair.Value.ShowSpot(hero != null && pair.Key == hero);
        }

        /// <summary>The card closes and every hold light goes out.</summary>
        public void HideHold()
        {
            Card.Hide();
            ShowSpot(null);
        }

        // ---- the seed and the end --------------------------------------------------------------------------------

        public void ShowSeed(int seed) => Seed.text = $"Seed {seed}";

        void BuildEnd()
        {
            var shade = Panel("End", SafeArea, new Color(0, 0, 0, 0.75f));
            shade.raycastTarget = true;
            End = shade.gameObject;
            EndButton = End.AddComponent<UnityEngine.UI.Button>();
            EndButton.targetGraphic = shade;

            EndTitle = Label("Title", shade.rectTransform, "", 160, Gold, TextAnchor.MiddleCenter);
            EndTitle.fontStyle = FontStyle.Bold;
            EndTitle.rectTransform.Band(300, 500, left: 0, right: 0);
            EndLine = Label("Turns", shade.rectTransform, "", 48, Color.white, TextAnchor.MiddleCenter);
            EndLine.rectTransform.Band(520, 590, left: 0, right: 0);
            Label("Hint", shade.rectTransform, "Tap to play again", 40, HealthColor, TextAnchor.MiddleCenter)
                .rectTransform.Band(640, 700, left: 0, right: 0);
            End.SetActive(false);
        }

        /// <summary>VICTORY or DEFEAT in big letters and "Fight took N turns"; a tap anywhere calls <paramref name="onTap"/>.</summary>
        public void ShowEnd(Outcome outcome, int turns, Action onTap)
        {
            if (outcome == Outcome.Going) throw new ArgumentException("The fight is still going.", nameof(outcome));
            EndTitle.text = outcome == Outcome.Won ? "VICTORY" : "DEFEAT";
            EndTitle.color = outcome == Outcome.Won ? Gold : Red;
            EndLine.text = $"Fight took {turns} turns";
            EndButton.onClick.RemoveAllListeners();
            EndButton.onClick.AddListener(() => onTap());
            End.transform.SetAsLastSibling();
            End.SetActive(true);
        }

        // ---- the safe area (P2a-04) ------------------------------------------------------------------------------

        // Called every frame by BattleScreen; only does work when the safe area or the screen size changed.
        public void FitSafeArea() => FitSafeArea(Screen.safeArea, new Vector2(Screen.width, Screen.height));

        public void FitSafeArea(Rect safe, Vector2 screen)
        {
            if (SafeArea == null || screen.x <= 0 || screen.y <= 0) return;
            if (safe == fittedSafeArea && screen == fittedScreen) return;
            fittedSafeArea = safe;
            fittedScreen = screen;
            SafeArea.anchorMin = new Vector2(safe.xMin / screen.x, safe.yMin / screen.y);
            SafeArea.anchorMax = new Vector2(safe.xMax / screen.x, safe.yMax / screen.y);
            SafeArea.offsetMin = SafeArea.offsetMax = Vector2.zero;
        }

        // A full-width band of the safe area between two heights measured down from the top of the 1080 reference
        // screen (17.3's numbers), 90 px in from each side.
        RectTransform Zone(string name, float top, float bottom)
        {
            var zone = NewRect(name, SafeArea);
            zone.anchorMin = new Vector2(0, 1 - bottom / ReferenceHeight);
            zone.anchorMax = new Vector2(1, 1 - top / ReferenceHeight);
            zone.offsetMin = new Vector2(SideMargin, 0);
            zone.offsetMax = new Vector2(-SideMargin, 0);
            return zone;
        }

        // ---- small drawing helpers shared with HeroFigure ----------------------------------------------------------

        internal static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>A plain coloured box filling its parent (narrow it with the layout helpers). Taps pass through it.</summary>
        internal static Image Panel(string name, Transform parent, Color color, Sprite sprite = null)
        {
            var rect = NewRect(name, parent);
            rect.Inset(0);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A text filling its parent, in Unity's built-in font (nothing to import: P2a-04's note).</summary>
        internal static Text Label(string name, Transform parent, string text, int size, Color color, TextAnchor anchor)
        {
            var rect = NewRect(name, parent);
            rect.Inset(0);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = Font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = anchor;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        static Font font;
        static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        static Sprite rounded;
        /// <summary>
        /// A white circle drawn by code (no art file): whole, it is a round badge; sliced (corners kept, middle
        /// stretched), it gives a box with rounded ends, the buff chips' shape.
        /// </summary>
        internal static Sprite Rounded
        {
            get
            {
                if (rounded != null) return rounded;
                const int size = 32;
                const float radius = size / 2f;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "Rounded (made by code)", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave,
                };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        var d = new Vector2(x + 0.5f - radius, y + 0.5f - radius).magnitude;
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255));
                    }
                texture.SetPixels32(pixels);
                texture.Apply();
                rounded = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0,
                                        SpriteMeshType.FullRect, new Vector4(15, 15, 15, 15));
                rounded.hideFlags = HideFlags.DontSave;
                return rounded;
            }
        }

        static Sprite spiked;
        /// <summary>
        /// A white eight-pointed burst drawn by code (no art file): the frame of a bad effect and of the damage and
        /// bad-effect symbols (P2b-02, 17.3.1: "spiked red frame = bad effect"), a shape so it reads without colour.
        /// </summary>
        internal static Sprite Spiked
        {
            get
            {
                if (spiked != null) return spiked;
                const int size = 48, points = 8;
                const float inner = 0.72f, outer = 1f, half = size / 2f;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "Spiked (made by code)", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave,
                };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        var dx = x + 0.5f - half;
                        var dy = y + 0.5f - half;
                        var turn = Mathf.Atan2(dy, dx) / (2 * Mathf.PI) * points;       // whole numbers are the tips
                        var wave = Mathf.Abs(turn - Mathf.Round(turn)) * 2;             // 0 at a tip, 1 between two tips
                        var edge = Mathf.Lerp(outer, inner, wave) * half;               // how far out the burst reaches this way
                        var reach = new Vector2(dx, dy).magnitude;
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(edge - reach + 0.5f) * 255));
                    }
                texture.SetPixels32(pixels);
                texture.Apply();
                spiked = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
                spiked.hideFlags = HideFlags.DontSave;
                return spiked;
            }
        }

        static Sprite ring;
        /// <summary>A white ring drawn by code: the "self" mark on a button (a dot you can't miss, but hollow).</summary>
        internal static Sprite Ring
        {
            get
            {
                if (ring != null) return ring;
                const int size = 32;
                const float outer = size / 2f, thickness = 4;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "Ring (made by code)", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave,
                };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        var d = new Vector2(x + 0.5f - outer, y + 0.5f - outer).magnitude;
                        var alpha = Mathf.Clamp01(outer - d + 0.5f) * Mathf.Clamp01(d - (outer - thickness) + 0.5f);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                    }
                texture.SetPixels32(pixels);
                texture.Apply();
                ring = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
                ring.hideFlags = HideFlags.DontSave;
                return ring;
            }
        }

        static Sprite shieldShape;
        /// <summary>
        /// A white shield drawn by code (no art file): straight sides for the top half, then narrowing to a point at the
        /// bottom. The mark a covered back-row hero wears (17.5).
        /// </summary>
        internal static Sprite ShieldShape
        {
            get
            {
                if (shieldShape != null) return shieldShape;
                const int width = 40, height = 48;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    name = "Shield (made by code)", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave,
                };
                var pixels = new Color32[width * height];
                for (int y = 0; y < height; y++)          // y counts up from the bottom row of pixels
                    for (int x = 0; x < width; x++)
                    {
                        var up = (y + 0.5f) / height;        // 0 at the bottom tip, 1 at the top
                        var halfWidth = up >= 0.5f ? width / 2f : width / 2f * (up / 0.5f);
                        var inside = Mathf.Abs(x + 0.5f - width / 2f) <= halfWidth;
                        pixels[y * width + x] = new Color32(255, 255, 255, (byte)(inside ? 255 : 0));
                    }
                texture.SetPixels32(pixels);
                texture.Apply();
                shieldShape = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100);
                shieldShape.hideFlags = HideFlags.DontSave;
                return shieldShape;
            }
        }

        /// <summary>Removes every child at once (in the editor's edit mode too, where Unity can't wait for the frame's end).</summary>
        internal static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    child.transform.SetParent(null, false);   // gone from the count now, destroyed at the frame's end
                    UnityEngine.Object.Destroy(child);
                }
                else UnityEngine.Object.DestroyImmediate(child);
            }
        }
    }

    /// <summary>Three ways to place a box inside its parent, in reference pixels.</summary>
    static class Layout
    {
        /// <summary>Fills the parent, <paramref name="by"/> px in from every edge (a negative number sticks out).</summary>
        public static void Inset(this RectTransform rect, float by)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(by, by);
            rect.offsetMax = new Vector2(-by, -by);
        }

        /// <summary>A band across the parent between two heights measured down from its top, inset from the sides.</summary>
        public static void Band(this RectTransform rect, float top, float bottom, float left, float right)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.offsetMin = new Vector2(left, -bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>The parent's full height, from one fraction of its width to another (a bar's fill).</summary>
        public static void Span(this RectTransform rect, float from, float to)
        {
            rect.anchorMin = new Vector2(from, 0);
            rect.anchorMax = new Vector2(to, 1);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
