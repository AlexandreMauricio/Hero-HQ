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
    // corner and the end text. P2a-08 adds the beats, numbers and banner; P2a-09 the buttons and the info strip.
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

        const float NowSize = 140, EntrySize = 120, EntryGap = 12;   // 17.4: NOW larger; the top strip is 146 px tall

        /// <summary>One entry of the turn-order bar as drawn.</summary>
        public sealed class BarEntry
        {
            public Fighter Who;
            public bool Skipped, Now, Diamond, Dimmed;
            public RectTransform Rect;
            public Text Initials;
            public GameObject Strike;
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

            BuildEnd();
        }

        // ---- the fight on the field ------------------------------------------------------------------------------

        /// <summary>
        /// A new fight: the old figures, bar and end text go; the ten heroes stand at their formation's <c>at</c>
        /// (x from the middle outward, y down from the field's top), yours on the left, the rival's side mirrored.
        /// </summary>
        public void ShowFight(Fight fight, FormationDef players, FormationDef enemies)
        {
            Clear(Field);
            figures.Clear();
            Clear(BarRow);
            bar.Clear();
            End.SetActive(false);
            foreach (var (team, formation) in new[] { (fight.Players, players), (fight.Enemies, enemies) })
                foreach (var hero in team)
                {
                    var at = formation.Slots[hero.Slot].At;
                    var x = (float)at[0] * (hero.Side == Side.Player ? -1 : 1);
                    figures[hero] = new HeroFigure(hero, Field, new Vector2(x, -(float)at[1]));
                }
        }

        /// <summary>
        /// After an action (7.2): Health and charge as <see cref="Playback"/> counted them from the events (finding 3),
        /// the effects and the Barrier from the fighters. A hero whose shown Health is 0 has fallen (reading R22).
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
                var barrier = hero.Effects.FirstOrDefault(e => e.Kind == "barrier")?.Amount ?? 0;
                figure.ShowHealth(health, hero.MaxHealth, barrier);
                figure.ShowCharge(playback.Charge(hero));
                figure.ShowEffects(hero.Effects, hero.TurnsStarted);
            }
        }

        /// <summary>The ring goes on the acting hero (null: nobody).</summary>
        public void ShowActing(Fighter acting)
        {
            foreach (var pair in figures) pair.Value.ShowActing(pair.Key == acting);
        }

        // ---- the turn-order bar (17.4) ---------------------------------------------------------------------------

        /// <summary>
        /// Redraws the bar from <c>PredictTurns</c>: next to act on the left, the first one larger with NOW; a blue
        /// frame and round badge for yours, red with a diamond for the rival; the first two letters of the name; a
        /// stunned (skipped) turn dimmed and struck through. No sliding in 2a.
        /// </summary>
        public void ShowBar(IReadOnlyList<PredictedTurn> turns)
        {
            Clear(BarRow);
            bar.Clear();
            var width = turns.Count == 0 ? 0 : NowSize + (turns.Count - 1) * (EntrySize + EntryGap);
            var x = -width / 2;
            for (int i = 0; i < turns.Count; i++)
            {
                var now = i == 0;
                var size = now ? NowSize : EntrySize;
                bar.Add(Entry(turns[i], now, size, x + size / 2));
                x += size + EntryGap;
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
                Rect = rect, Initials = initials, Strike = strike.gameObject,
            };
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
