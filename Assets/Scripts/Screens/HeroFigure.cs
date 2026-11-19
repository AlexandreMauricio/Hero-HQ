using System.Collections.Generic;
using HeroHQ.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace HeroHQ.Screens
{
    /// <summary>
    /// One hero's box on the field (03 Architecture 7.2, 02 GDD 17.6), drawn by code in plain boxes: the name, the
    /// Health bar with its number and a Barrier stretch, the gold charge bar with ULT at 100, the effect chips above
    /// the box, the acting ring, and a grey empty slot that stays when the hero falls. Plain C#: it draws what it is
    /// told and knows no rules. P2a-09: the tap, glow, dim, the shield, the aiming outline and the amber hit chip.
    /// P2b-02: the words on the effect chips became small icons (02 GDD 17.3.1's sizes), the hit chip moved under the
    /// box, and the name tag says the hero's short name.
    /// </summary>
    public sealed class HeroFigure
    {
        /// <summary>The box, in reference pixels. A figure's <c>at</c> (formations.json) is the box's centre.</summary>
        public const float Width = 280, BoxHeight = 100;
        /// <summary>
        /// The icon row above the box and the gap under it (02 GDD 17.3.1: four slots of 40 px, 2 px apart), and the hit
        /// chip under the box (156 x 38, R31 and 17.3.1): the figure's whole footprint is 180 px tall.
        /// </summary>
        public const float IconSize = 40, IconGap = 2, IconsHeight = 40, IconsRowGap = 2, HitChipWidth = 156, HitChipHeight = 38;
        /// <summary>Four slots; with more than four effects the fourth reads "+N" (17.3.1).</summary>
        public const int MaxIcons = 4;
        const int MarkSize = 20, DigitSize = 28, MoreSize = 28;

        /// <summary>One effect icon: its frame, the short mark and the turns left in the corner; or the "+N" slot.</summary>
        public sealed class Icon
        {
            /// <summary>The effect's data name; null for the "+N" slot.</summary>
            public string Kind;
            public RectTransform Rect;
            public Image Frame;
            /// <summary>The short mark, or "+N" on the last slot.</summary>
            public Text Mark;
            /// <summary>Turns left, bold in the lower-right corner; empty for Stun and Momentum, which have no turn count.</summary>
            public Text Turns;
            public bool Bad;
            public bool IsMore => Kind == null;
        }

        readonly RectTransform iconRow;
        readonly List<Icon> icons = new List<Icon>();

        /// <summary>Makes the box in the field zone, its centre <paramref name="at"/> from the zone's top middle.</summary>
        public HeroFigure(Fighter who, RectTransform field, Vector2 at)
        {
            Who = who;
            Root = BattleView.NewRect(who.Hero.Name, field);
            Root.anchorMin = Root.anchorMax = new Vector2(0.5f, 1);
            Root.sizeDelta = new Vector2(Width, BoxHeight);
            Root.anchoredPosition = at;

            // P2a-09: the whole box is a tap target (an invisible picture that catches the tap; see the Glossary, *raycast*).
            var tapArea = Root.gameObject.AddComponent<Image>();
            tapArea.color = Color.clear;
            Tap = Root.gameObject.AddComponent<UnityEngine.UI.Button>();
            Tap.targetGraphic = tapArea;
            Tap.transition = Selectable.Transition.None;
            // P2b-03: a finger held 0.4 s on the box opens the hero card (and the click that ends it counts for nothing).
            Hold = Root.gameObject.AddComponent<Hold>();

            // Drawn in this order: the empty slot under everything, the rings just behind the box so they show as outlines.
            var slot = BattleView.Panel("Empty slot", Root, BattleView.Grey);
            BattleView.Panel("Inside", slot.rectTransform, BattleView.Background).rectTransform.Inset(4);
            EmptySlot = slot.gameObject;

            // The enemy's aim sits just outside the acting ring, so both can show at once.
            var aim = BattleView.Panel("Target ring", Root, BattleView.Red);
            aim.rectTransform.Inset(-16);
            TargetRing = aim.gameObject;
            TargetRing.SetActive(false);

            // P2a-09: your aim (a thick white outline: the skill will touch this hero), then the softer glow (you may tap
            // this hero). An aimed hero that also glows shows one thick white ring.
            var outline = BattleView.Panel("Aim outline", Root, Color.white);
            outline.rectTransform.Inset(-16);
            AimOutline = outline.gameObject;
            AimOutline.SetActive(false);

            // P2b-03: a hold on this hero's turn-bar entry rings the figure (17.4): a bright ring just outside the others.
            var spot = BattleView.Panel("Spot ring", Root, BattleView.LightBlue);
            spot.rectTransform.Inset(-24);
            SpotRing = spot.gameObject;
            SpotRing.SetActive(false);

            var glow = BattleView.Panel("Glow", Root, new Color(1, 1, 1, 0.45f));
            glow.rectTransform.Inset(-12);
            Glow = glow.gameObject;
            Glow.SetActive(false);

            var ring = BattleView.Panel("Acting ring", Root, BattleView.Gold);
            ring.rectTransform.Inset(-8);
            Ring = ring.gameObject;
            Ring.SetActive(false);

            var frame = BattleView.Panel("Box", Root, who.Side == Side.Player ? BattleView.Blue : BattleView.Red);
            Box = frame.gameObject;
            dimmer = Box.AddComponent<CanvasGroup>();
            var body = BattleView.Panel("Body", frame.rectTransform, BattleView.Body).rectTransform;
            body.Inset(4);

            // Inside the body (92 px): name 44, Health bar 30, charge bar 10, from the top.
            Name = BattleView.Label("Name", body, who.Hero.ShortName, 40, Color.white, TextAnchor.MiddleLeft);
            Name.rectTransform.Band(0, 44, left: 8, right: 70);
            Name.resizeTextForBestFit = true;          // "Kal & Talos" is the longest name: it shrinks a little, never below 32
            Name.resizeTextMinSize = 32;
            Name.resizeTextMaxSize = 40;
            Name.horizontalOverflow = HorizontalWrapMode.Wrap;

            var ult = BattleView.Panel("ULT", body, BattleView.Gold, BattleView.Rounded);
            ult.type = Image.Type.Sliced;
            ult.rectTransform.Band(6, 38, left: 214, right: 6);
            BattleView.Label("Text", ult.rectTransform, "ULT", 26, BattleView.Body, TextAnchor.MiddleCenter).fontStyle = FontStyle.Bold;
            UltTag = ult.gameObject;

            var health = BattleView.Panel("Health bar", body, BattleView.Track).rectTransform;
            health.Band(48, 78, left: 6, right: 6);
            HealthFill = BattleView.Panel("Health", health, BattleView.HealthColor).rectTransform;
            BarrierFill = BattleView.Panel("Barrier", health, BattleView.LightBlue).rectTransform;
            HealthNumber = BattleView.Label("Number", health, "", 28, Color.white, TextAnchor.MiddleCenter);
            HealthNumber.fontStyle = FontStyle.Bold;
            HealthNumber.gameObject.AddComponent<Outline>().effectDistance = new Vector2(2, -2);

            var charge = BattleView.Panel("Charge bar", body, BattleView.Track).rectTransform;
            charge.Band(82, 92, left: 6, right: 6);
            ChargeFill = BattleView.Panel("Charge", charge, BattleView.Gold).rectTransform;

            // The icon row sits just above the box (outside it) and goes with the box when the hero falls. Its icons are
            // placed by hand (centred on the row), so their places are plain numbers a test can read.
            iconRow = BattleView.NewRect("Effect icons", frame.rectTransform);
            iconRow.anchorMin = new Vector2(0, 1);
            iconRow.anchorMax = new Vector2(1, 1);
            iconRow.offsetMin = new Vector2(0, IconsRowGap);
            iconRow.offsetMax = new Vector2(0, IconsRowGap + IconsHeight);

            // P2a-09: the shield of a covered back-row hero, on the box's side that faces the middle (where the front row
            // stands); drawn after the box so a dimmed box doesn't dim it.
            var shield = BattleView.Panel("Shield", Root, BattleView.ShieldColor, BattleView.ShieldShape);
            var shieldRect = shield.rectTransform;
            var inner = who.Side == Side.Player ? 1f : 0f;
            shieldRect.anchorMin = shieldRect.anchorMax = new Vector2(inner, 0.5f);
            shieldRect.sizeDelta = new Vector2(ShieldWidth, ShieldHeight);
            shieldRect.anchoredPosition = Vector2.zero;
            Shield = shield.gameObject;
            Shield.SetActive(false);

            // P2a-09: the amber hit chip. P2b-02 (R31, 17.3.1): hung just under the box, so it covers neither the icons
            // nor the name; 156 x 38 with 32 px text.
            var chip = BattleView.Panel("Hit chip", Root, BattleView.Amber, BattleView.Rounded);
            chip.type = Image.Type.Sliced;
            var chipRect = chip.rectTransform;
            chipRect.anchorMin = chipRect.anchorMax = new Vector2(0.5f, 0);
            chipRect.pivot = new Vector2(0.5f, 1);
            chipRect.sizeDelta = new Vector2(HitChipWidth, HitChipHeight);
            chipRect.anchoredPosition = Vector2.zero;
            HitText = BattleView.Label("Text", chipRect, "", 32, BattleView.Body, TextAnchor.MiddleCenter);
            HitText.fontStyle = FontStyle.Bold;
            HitChip = chip.gameObject;
            HitChip.SetActive(false);

            ShowHealth(who.MaxHealth, who.MaxHealth, 0);   // a fight starts at full Health and no charge (reading R22)
            ShowCharge(0);
        }

        /// <summary>The shield mark's size, reference pixels.</summary>
        public const float ShieldWidth = 40, ShieldHeight = 48;

        readonly CanvasGroup dimmer;

        public Fighter Who { get; }
        /// <summary>The box's place on the field; it never moves (a fallen hero leaves a gap).</summary>
        public RectTransform Root { get; }
        /// <summary>The grey frame of the slot, always there; it is all that is left when the hero falls.</summary>
        public GameObject EmptySlot { get; }
        public GameObject Ring { get; }
        /// <summary>Everything about the hero: name, bars, chips. Hidden when the hero falls.</summary>
        public GameObject Box { get; }
        public GameObject UltTag { get; }
        public Text Name { get; }
        public Text HealthNumber { get; }
        public RectTransform HealthFill { get; }
        public RectTransform BarrierFill { get; }
        public RectTransform ChargeFill { get; }
        /// <summary>The effect icons in the order the effects were put on, the "+N" slot last when there are more than four.</summary>
        public IReadOnlyList<Icon> Icons => icons;
        public bool KnockedOut { get; private set; }

        /// <summary>
        /// The Health bar: the number is the Health; a Barrier is a light-blue stretch right after the Health. When
        /// Health and Barrier together are more than max Health, the bar stands for both, so the stretch always shows.
        /// </summary>
        public void ShowHealth(int health, int maxHealth, int barrier)
        {
            HealthNumber.text = health.ToString();
            float whole = Mathf.Max(maxHealth, health + barrier);
            float end = whole > 0 ? health / whole : 0;
            HealthFill.Span(0, end);
            BarrierFill.gameObject.SetActive(barrier > 0);
            BarrierFill.Span(end, whole > 0 ? (health + barrier) / whole : 0);
        }

        /// <summary>The gold charge bar, 0–100, and the ULT tag at 100 (both sides, 17.6).</summary>
        public void ShowCharge(double charge)
        {
            ChargeFill.Span(0, Mathf.Clamp01((float)(charge / 100)));
            UltTag.SetActive(charge >= 100);
        }

        /// <summary>
        /// The effect icons (02 GDD 17.3.1), in the order the effects were put on: a rounded green frame for a buff, a
        /// spiked red one for a bad effect, a short mark inside and the turns left in the corner (none for Stun and
        /// Momentum, which have no turn count). Up to four slots; with five or more effects the fourth slot reads "+N",
        /// N being the effects not shown, so the icons plus N are all of them. The words are in the hero card (P2b-03).
        /// </summary>
        public void ShowEffects(IReadOnlyList<Effect> effects, int turnsStarted)
        {
            BattleView.Clear(iconRow);
            icons.Clear();
            var overflow = effects.Count > MaxIcons;
            var shown = overflow ? MaxIcons - 1 : effects.Count;
            var slots = shown + (overflow ? 1 : 0);
            var left = -(slots * IconSize + (slots - 1) * IconGap) / 2;   // the row is centred over the box

            for (int i = 0; i < slots; i++)
            {
                var centre = left + i * (IconSize + IconGap) + IconSize / 2;
                icons.Add(i < shown ? EffectIcon(effects[i], turnsStarted, centre) : MoreIcon(effects.Count - shown, centre));
            }
        }

        Icon EffectIcon(Effect effect, int turnsStarted, float centre)
        {
            var look = EffectLook.For(effect.Kind);
            var rect = BattleView.NewRect(effect.Kind, iconRow);
            Place(rect, centre);

            // Buff: a rounded green frame. Bad: a spiked red one. A shape as well as a colour, so it reads without colour.
            var frame = BattleView.Panel("Frame", rect, look.Bad ? BattleView.Red : BattleView.Green,
                                         look.Bad ? BattleView.Spiked : BattleView.Rounded);
            if (!look.Bad) frame.type = Image.Type.Sliced;
            var inside = BattleView.Panel("Inside", rect, BattleView.Body, BattleView.Rounded);
            inside.rectTransform.Inset(look.Bad ? 8 : 4);
            if (!look.Bad) inside.type = Image.Type.Sliced;

            var mark = BattleView.Label("Mark", rect, look.Mark, MarkSize, Color.white, TextAnchor.UpperLeft);
            mark.fontStyle = FontStyle.Bold;
            mark.rectTransform.offsetMin = new Vector2(7, 0);
            mark.rectTransform.offsetMax = new Vector2(0, -6);

            var turns = BattleView.Label("Turns", rect, "", DigitSize, Color.white, TextAnchor.LowerRight);
            turns.fontStyle = FontStyle.Bold;
            turns.gameObject.AddComponent<Outline>().effectDistance = new Vector2(2, -2);
            if (effect.EndsAfterTurn != Effects.NoEnd) turns.text = (effect.EndsAfterTurn - turnsStarted).ToString();

            return new Icon { Kind = effect.Kind, Rect = rect, Frame = frame, Mark = mark, Turns = turns, Bad = look.Bad };
        }

        Icon MoreIcon(int more, float centre)
        {
            var rect = BattleView.NewRect("More", iconRow);
            Place(rect, centre);
            var frame = BattleView.Panel("Frame", rect, BattleView.Grey, BattleView.Rounded);
            frame.type = Image.Type.Sliced;
            var mark = BattleView.Label("Mark", rect, "+" + more, MoreSize, Color.white, TextAnchor.MiddleCenter);
            mark.fontStyle = FontStyle.Bold;
            return new Icon { Rect = rect, Frame = frame, Mark = mark };
        }

        // An icon IconSize square, its centre at x on the row, standing on the row's bottom edge.
        static void Place(RectTransform rect, float centre)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0);
            rect.pivot = new Vector2(0.5f, 0);
            rect.sizeDelta = new Vector2(IconSize, IconSize);
            rect.anchoredPosition = new Vector2(centre, 0);
        }

        public void ShowActing(bool acting) => Ring.SetActive(acting && !KnockedOut);

        /// <summary>A red outline round the box: where the enemy's skill is about to land (17.7). Off unless asked.</summary>
        public GameObject TargetRing { get; }

        public void ShowTargeted(bool targeted) => TargetRing.SetActive(targeted && !KnockedOut);

        // ---- your turn (P2a-09; 02 GDD 17.5) -------------------------------------------------------------------

        /// <summary>The tap on this box (BattleView passes it to BattleScreen.TapHero).</summary>
        public UnityEngine.UI.Button Tap { get; }
        /// <summary>P2b-03: the press-and-hold on this box. The click listener asks <see cref="Hold.ConsumeClick"/> first.</summary>
        public Hold Hold { get; }
        /// <summary>P2b-03 (17.4): the ring a hold on this hero's bar entry puts on the field. Off unless asked.</summary>
        public GameObject SpotRing { get; }
        public void ShowSpot(bool on) => SpotRing.SetActive(on && !KnockedOut);
        /// <summary>You may tap this hero for the armed skill.</summary>
        public GameObject Glow { get; }
        /// <summary>The armed skill will touch this hero.</summary>
        public GameObject AimOutline { get; }
        /// <summary>A back-row hero still covered in their lane (02 GDD 2): shown on both sides, all fight long.</summary>
        public GameObject Shield { get; }
        /// <summary>The amber "Hit N %" chip, only below 100 % (17.5).</summary>
        public GameObject HitChip { get; }
        public Text HitText { get; }
        /// <summary>You can't tap this hero for the armed skill.</summary>
        public bool Dimmed { get; private set; }

        /// <summary>
        /// How this hero looks while you choose: <paramref name="glow"/> (you may tap them), <paramref name="dim"/> (you
        /// can't), <paramref name="aimed"/> (the skill will touch them) and the hit chance to show, or null for no chip.
        /// All off: nothing armed, or not your turn.
        /// </summary>
        public void ShowChoosing(bool glow, bool dim, bool aimed, double? hitChance)
        {
            var standing = !KnockedOut;
            Glow.SetActive(glow && standing);
            AimOutline.SetActive(aimed && standing);
            Dimmed = dim && standing;
            dimmer.alpha = Dimmed ? 0.4f : 1;
            HitChip.SetActive(hitChance.HasValue && standing);
            HitText.text = hitChance.HasValue ? HitWords(hitChance.Value) : "";
        }

        /// <summary>
        /// "Hit 85 %": the hit chance as a whole number, rounded down so a chip never reads 100 % (reading R27).
        /// </summary>
        public static string HitWords(double hitChance) => $"Hit {Mathf.FloorToInt((float)hitChance)} %";

        public void ShowShield(bool covered) => Shield.SetActive(covered && !KnockedOut);

        /// <summary>The hero fell: the box goes, the grey empty slot stays, nobody moves into it (02 GDD 2, 17.6).</summary>
        public void ShowKnockedOut()
        {
            KnockedOut = true;
            Box.SetActive(false);
            Ring.SetActive(false);
            TargetRing.SetActive(false);
            SpotRing.SetActive(false);
            ShowChoosing(false, false, false, null);
            Shield.SetActive(false);
        }
    }
}
