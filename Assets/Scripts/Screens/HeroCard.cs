using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules;
using HeroHQ.Rules.Data;
using UnityEngine;
using UnityEngine.UI;

namespace HeroHQ.Screens
{
    /// <summary>
    /// The hero card (P2b-03; 02 GDD 17.1 *Press-and-hold*, 17.6, 03 Architecture 8.1 A3): what a hero is, in words, read
    /// by holding a finger on them. This class is the card's <b>content</b>, plain C# that knows no pictures: the name, the
    /// class, the three traits, the eight final stats, the three skills, the passive and (for the leader) the leader skill
    /// as sentences, and every effect with its turns left. <see cref="HeroCardView"/> draws it. The same content is
    /// what team select will show (P2b-11).
    ///
    /// The numbers are the ones the fight uses: the final stats are read from the <see cref="Fighter"/>, which
    /// <c>Fight.Create</c> filled in with the leader and passive bonuses already added (03 Architecture 4.6), so the card
    /// can't disagree with the rules (pillar 2: nothing hidden that the rules use). Health is the most Health the hero
    /// has, not the Health now: the bar on the field shows that, and it is ahead of what the screen has played.
    /// </summary>
    public sealed class HeroCard
    {
        /// <summary>One labelled line of the card ("Basic", "Passive", "Leader skill") and its sentence.</summary>
        public readonly struct Line
        {
            public Line(string label, string text)
            {
                Label = label;
                Text = text;
            }

            public string Label { get; }
            public string Text { get; }
        }

        /// <summary>The full name (the tag on the field says the short one).</summary>
        public string Name { get; private set; }
        public Side Side { get; private set; }
        public bool IsLeader { get; private set; }
        public string ClassName { get; private set; }
        /// <summary>The three traits' names: side, pantheon, origin.</summary>
        public IReadOnlyList<string> Traits { get; private set; }
        /// <summary>The eight stats on in the first fight (02 GDD 5), label and value, in the order the card shows them.</summary>
        public IReadOnlyList<(string Label, string Value)> Stats { get; private set; }
        /// <summary>Basic, Special, Ultimate, Passive, then Leader skill if this hero leads.</summary>
        public IReadOnlyList<Line> Lines { get; private set; }
        /// <summary>Every effect on the hero in the order it was put on, in words with its turns left; empty when there are none.</summary>
        public IReadOnlyList<string> ActiveEffects { get; private set; }

        /// <summary>The card of <paramref name="who"/> as the fight stands now. <paramref name="leader"/>: they lead their team.</summary>
        public static HeroCard Of(GameData data, Fighter who, bool leader)
        {
            var hero = who.Hero;
            var lines = new List<Line>
            {
                new Line("Basic", SkillText.Sentence(hero.Basic)),
                new Line("Special", SkillText.Sentence(hero.Special)),
                new Line("Ultimate", SkillText.Sentence(hero.Ultimate)),
                new Line("Passive", SkillText.PassiveSentence(hero.Passive)),
            };
            if (leader) lines.Add(new Line("Leader skill", SkillText.LeaderSentence(hero.Leader)));

            return new HeroCard
            {
                Name = hero.Name,
                Side = who.Side,
                IsLeader = leader,
                ClassName = data.Classes[hero.Class].Name,
                Traits = hero.Traits.Select(t => data.Traits[t].Name).ToList(),
                Stats = new[]
                {
                    ("Health", who.MaxHealth.ToString()),
                    ("Armor", who.Armor.ToString()),
                    ("Damage", who.Damage.ToString()),
                    ("Speed", who.Speed.ToString()),
                    ("Accuracy", who.Accuracy + " %"),
                    ("Dodge", who.DodgeChance + " %"),
                    ("Crit chance", who.CritChance + " %"),
                    ("Crit damage", who.CritDamage + " %"),
                },
                Lines = lines,
                ActiveEffects = who.Effects.Select(e => EffectWords(e, who.TurnsStarted)).ToList(),
            };
        }

        /// <summary>
        /// One effect in words: "Offense Up: 2 turns left", "Barrier (120 left): 1 turn left", "Momentum (3 stacks)",
        /// "Stun: next turn skipped". Turns left count the holder's own turns, as the icon's number does (P2b-02).
        /// </summary>
        public static string EffectWords(Effect effect, int turnsStarted)
        {
            var name = SkillText.Words(effect.Kind);
            switch (effect.Kind)
            {
                case "stun": return name + ": next turn skipped";
                case "momentum": return $"{name} ({effect.Amount} stack{(effect.Amount == 1 ? "" : "s")})";
                case "barrier": name += $" ({effect.Amount} left)"; break;
            }
            if (effect.EndsAfterTurn == Effects.NoEnd) return name;
            var left = effect.EndsAfterTurn - turnsStarted;
            return $"{name}: {left} turn{(left == 1 ? "" : "s")} left";
        }
    }

    /// <summary>
    /// The hero card's box: a panel in the middle of the screen that shows while a finger holds a hero and goes when it
    /// lifts. Built once by <see cref="BattleView"/> under the safe area, hidden, and filled each time it opens. Every
    /// piece is a plain box and a text, in reference pixels (1920 x 1080); taps pass through it (it only shows during a hold).
    /// </summary>
    public sealed class HeroCardView
    {
        /// <summary>The card's size: inside the safe area at 1920 x 1080 (90 px side margins, 24 px top and bottom).</summary>
        public const float Width = 1500, Height = 980;
        const float Pad = 24, LabelWidth = 200, LineHeight = 112, TextSize = 36;

        public GameObject Root { get; private set; }
        public Text Title { get; private set; }
        public Text Subtitle { get; private set; }
        public GameObject LeaderTag { get; private set; }
        public Text EffectsText { get; private set; }
        /// <summary>The card now showing, or null when hidden.</summary>
        public HeroCard Showing { get; private set; }
        public bool Visible => Root != null && Root.activeSelf;

        RectTransform frame, body, content;

        public void Build(RectTransform parent)
        {
            var shade = BattleView.NewRect("Hero card", parent);
            shade.Inset(0);
            Root = shade.gameObject;
            Root.SetActive(false);

            var box = BattleView.Panel("Frame", shade, BattleView.Blue);
            frame = box.rectTransform;
            Centre(frame, Width, Height);
            body = BattleView.Panel("Body", frame, BattleView.Body).rectTransform;
            body.Inset(8);
            content = BattleView.NewRect("Content", body);
            content.Inset(Pad);

            Title = BattleView.Label("Name", content, "", 64, Color.white, TextAnchor.MiddleLeft);
            Title.fontStyle = FontStyle.Bold;
            Title.rectTransform.Band(0, 80, left: 0, right: 260);

            var tag = BattleView.Panel("Leader tag", content, BattleView.Gold, BattleView.Rounded);
            tag.type = Image.Type.Sliced;
            tag.rectTransform.Band(8, 72, left: (float)(Width - 2 * Pad - 16 - 240), right: 0);
            BattleView.Label("Text", tag.rectTransform, "LEADER", 40, BattleView.Body, TextAnchor.MiddleCenter).fontStyle = FontStyle.Bold;
            LeaderTag = tag.gameObject;

            Subtitle = BattleView.Label("Class and traits", content, "", 40, BattleView.HealthColor, TextAnchor.MiddleLeft);
            Subtitle.rectTransform.Band(84, 136, left: 0, right: 0);
        }

        static void Centre(RectTransform rect, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = Vector2.zero;
        }

        /// <summary>Opens the card with this content (drawn fresh each time, on top of everything in the safe area).</summary>
        public void Show(HeroCard card)
        {
            Showing = card;
            var mine = card.Side == Side.Player;
            frame.GetComponent<Image>().color = mine ? BattleView.Blue : BattleView.Red;
            Title.text = card.Name;
            LeaderTag.SetActive(card.IsLeader);
            Subtitle.text = string.Join("  ·  ", new[] { card.ClassName }.Concat(card.Traits));

            // Fixed rows below the header: the stats in two rows of four, then one band per sentence, then the effects.
            Clear();
            var cell = (Width - 2 * Pad) / 4;
            for (int i = 0; i < card.Stats.Count; i++)
            {
                var stat = BattleView.Label(card.Stats[i].Label, content, $"{card.Stats[i].Label} {card.Stats[i].Value}", 40, Color.white, TextAnchor.MiddleLeft);
                stat.fontStyle = FontStyle.Bold;
                var top = 148 + (i / 4) * 60;
                var left = (i % 4) * cell;
                stat.rectTransform.Band(top, top + 56, left: left, right: Width - 2 * Pad - left - cell);
            }

            var y = 290f;
            foreach (var line in card.Lines)
            {
                var label = BattleView.Label(line.Label, content, line.Label, 36, BattleView.Gold, TextAnchor.UpperLeft);
                label.fontStyle = FontStyle.Bold;
                label.rectTransform.Band(y, y + LineHeight, left: 0, right: Width - 2 * Pad - LabelWidth);
                var text = BattleView.Label(line.Label + " text", content, line.Text, (int)TextSize, Color.white, TextAnchor.UpperLeft);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.resizeTextForBestFit = true;       // a long sentence shrinks a little, never under 32 px (17.1)
                text.resizeTextMinSize = 32;
                text.resizeTextMaxSize = (int)TextSize;
                text.rectTransform.Band(y, y + LineHeight, left: LabelWidth, right: 0);
                y += LineHeight;
            }

            EffectsText = BattleView.Label("Effects", content, "Effects: " + (card.ActiveEffects.Count == 0 ? "none" : string.Join("   ", card.ActiveEffects)), 36,
                                           card.ActiveEffects.Count == 0 ? BattleView.HealthColor : Color.white, TextAnchor.UpperLeft);
            EffectsText.horizontalOverflow = HorizontalWrapMode.Wrap;
            EffectsText.resizeTextForBestFit = true;
            EffectsText.resizeTextMinSize = 32;
            EffectsText.resizeTextMaxSize = 36;
            EffectsText.rectTransform.Band(y + 8, y + 8 + 100, left: 0, right: 0);

            Root.transform.SetAsLastSibling();
            Root.SetActive(true);
        }

        public void Hide()
        {
            Showing = null;
            if (Root != null) Root.SetActive(false);
        }

        // Everything drawn by the last Show, except the fixed header pieces built once.
        void Clear()
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i).gameObject;
                if (child == Title.gameObject || child == Subtitle.gameObject || child == LeaderTag) continue;
                child.transform.SetParent(null, false);
                if (Application.isPlaying) Object.Destroy(child); else Object.DestroyImmediate(child);
            }
        }
    }
}
