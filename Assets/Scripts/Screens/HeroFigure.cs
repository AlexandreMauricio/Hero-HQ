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
    /// </summary>
    public sealed class HeroFigure
    {
        /// <summary>The box, in reference pixels. A figure's <c>at</c> (formations.json) is the box's centre.</summary>
        public const float Width = 280, BoxHeight = 100;
        /// <summary>The chip row above the box and the gap under it: the figure's whole footprint is 134 px tall.</summary>
        public const float ChipsHeight = 32, ChipsGap = 2;
        /// <summary>Up to four chips, then "+N" (17.6).</summary>
        public const int MaxChips = 4;

        /// <summary>One effect chip: its words, its frame, and whether it is a bad effect.</summary>
        public sealed class Chip
        {
            public Text Label;
            public Image Frame;
            public bool Bad;
        }

        readonly RectTransform chipRow;
        readonly List<Chip> chips = new List<Chip>();

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
            Name = BattleView.Label("Name", body, who.Hero.Name, 40, Color.white, TextAnchor.MiddleLeft);
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

            // The chip row sits just above the box (outside it) and goes with the box when the hero falls.
            chipRow = BattleView.NewRect("Effect chips", frame.rectTransform);
            chipRow.anchorMin = new Vector2(0, 1);
            chipRow.anchorMax = new Vector2(1, 1);
            chipRow.offsetMin = new Vector2(0, ChipsGap);
            chipRow.offsetMax = new Vector2(0, ChipsGap + ChipsHeight);
            var row = chipRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.spacing = 4;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

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

            // P2a-09: the amber hit chip, centred on the box's top edge (reading R27).
            var chip = BattleView.Panel("Hit chip", Root, BattleView.Amber, BattleView.Rounded);
            chip.type = Image.Type.Sliced;
            var chipRect = chip.rectTransform;
            chipRect.anchorMin = chipRect.anchorMax = new Vector2(0.5f, 1);
            chipRect.sizeDelta = new Vector2(170, 44);
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
        public IReadOnlyList<Chip> Chips => chips;
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
        /// The effect chips, in the order the effects were put on: the effect in words and the turns left (none for
        /// Stun and Momentum, which have no turn count); buffs in a rounded green chip, bad effects in a square red one;
        /// up to four, then "+N".
        /// </summary>
        public void ShowEffects(IReadOnlyList<Effect> effects, int turnsStarted)
        {
            BattleView.Clear(chipRow);
            chips.Clear();
            for (int i = 0; i < effects.Count && i < MaxChips; i++)
            {
                var e = effects[i];
                var words = SkillText.Words(e.Kind);
                if (e.EndsAfterTurn != Effects.NoEnd) words += " " + (e.EndsAfterTurn - turnsStarted);
                var bad = Effects.IsBad(e.Kind);
                AddChip(words, bad ? BattleView.Red : BattleView.Green, bad ? null : BattleView.Rounded, bad);
            }
            if (effects.Count > MaxChips) AddChip("+" + (effects.Count - MaxChips), BattleView.Grey, BattleView.Rounded, false);
        }

        void AddChip(string words, Color color, Sprite sprite, bool bad)
        {
            var frame = BattleView.Panel("Chip", chipRow, color, sprite);
            if (sprite != null) frame.type = Image.Type.Sliced;
            var pad = frame.gameObject.AddComponent<HorizontalLayoutGroup>();
            pad.padding = new RectOffset(10, 10, 0, 0);
            pad.childControlWidth = pad.childControlHeight = true;
            var label = BattleView.Label("Text", frame.rectTransform, words, 28, Color.white, TextAnchor.MiddleCenter);
            label.fontStyle = FontStyle.Bold;
            chips.Add(new Chip { Label = label, Frame = frame, Bad = bad });
        }

        public void ShowActing(bool acting) => Ring.SetActive(acting && !KnockedOut);

        /// <summary>A red outline round the box: where the enemy's skill is about to land (17.7). Off unless asked.</summary>
        public GameObject TargetRing { get; }

        public void ShowTargeted(bool targeted) => TargetRing.SetActive(targeted && !KnockedOut);

        // ---- your turn (P2a-09; 02 GDD 17.5) -------------------------------------------------------------------

        /// <summary>The tap on this box (BattleView passes it to BattleScreen.TapHero).</summary>
        public UnityEngine.UI.Button Tap { get; }
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
            ShowChoosing(false, false, false, null);
            Shield.SetActive(false);
        }
    }
}
