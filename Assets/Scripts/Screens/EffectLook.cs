using System;
using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules;
using HeroHQ.Rules.Data;

namespace HeroHQ.Screens
{
    /// <summary>
    /// The screen's one table of how things look as small icons (P2b-02; 03 Architecture 8.1 A2, decision D2: placeholders
    /// drawn by code, real art later through <c>/art</c>). Plain C#: no pictures here, only which short mark and which
    /// frame each effect gets, and which symbols and which target mark a skill's button shows (02 GDD 17.5, 17.6).
    /// A real icon set later changes this table and the drawing, nothing else.
    /// </summary>
    public static class EffectLook
    {
        /// <summary>How one effect looks: a short mark inside the icon (letters, since the built-in font has no pictures), and
        /// whether it is a bad effect (a spiked red frame; a buff gets a rounded green one: a shape, not only a colour).</summary>
        public readonly struct Look
        {
            public Look(string mark, bool bad)
            {
                Mark = mark;
                Bad = bad;
            }

            public string Mark { get; }
            public bool Bad { get; }
        }

        // One entry per effect of Effects.Kinds. A test walks that list, so a new effect fails until it has an entry here
        // (the same trick as Playback.GetsBeat). Whether it is bad is Effects.IsBad, never written twice.
        static readonly Dictionary<string, string> Marks = new Dictionary<string, string>
        {
            { "offense_up", "A+" },    // attack up
            { "offense_down", "A-" },
            { "defense_up", "D+" },
            { "defense_down", "D-" },
            { "taunt", "T" },
            { "barrier", "B" },
            { "ward", "W" },
            { "counter", "C" },
            { "stun", "S" },
            { "momentum", "M" },
        };

        /// <summary>The look of an effect; throws for an effect nobody has given an icon.</summary>
        public static Look For(string kind)
        {
            if (kind == null || !Marks.TryGetValue(kind, out var mark))
                throw new ArgumentException($"EffectLook has no icon for the effect '{kind}'.", nameof(kind));
            return new Look(mark, Effects.IsBad(kind));
        }

        // ---- the symbols on a skill button (17.5) --------------------------------------------------------------------

        /// <summary>What a piece of a skill does, as a small symbol: damage, heal, a buff, a bad effect or the turn meter.</summary>
        public enum Symbol { Damage, Heal, Buff, BadEffect, Meter }

        /// <summary>The short mark inside a symbol.</summary>
        public static string Mark(Symbol symbol)
        {
            switch (symbol)
            {
                case Symbol.Damage: return "!";
                case Symbol.Heal: return "+";
                case Symbol.Buff: return "^";
                case Symbol.BadEffect: return "v";
                case Symbol.Meter: return ">>";
                default: throw new ArgumentOutOfRangeException(nameof(symbol), symbol, "no mark for this symbol");
            }
        }

        /// <summary>Whether the symbol takes the spiked red frame (damage and bad effects) or the rounded one.</summary>
        public static bool IsHarsh(Symbol symbol) => symbol == Symbol.Damage || symbol == Symbol.BadEffect;

        /// <summary>
        /// The symbols of a skill, one per kind of piece in the order they first appear (reading R37): damage, heal, meter;
        /// an effect piece is a bad effect if the effect is bad, otherwise a buff; clearing bad effects reads as a buff
        /// (it helps the target).
        /// </summary>
        public static IReadOnlyList<Symbol> Symbols(SkillDef skill)
        {
            var found = new List<Symbol>();
            foreach (var piece in skill.Pieces)
            {
                Symbol symbol;
                switch (piece.Do)
                {
                    case "damage": symbol = Symbol.Damage; break;
                    case "heal": symbol = Symbol.Heal; break;
                    case "meter": symbol = Symbol.Meter; break;
                    case "clear": symbol = Symbol.Buff; break;
                    case "effect": symbol = Effects.IsBad(piece.Effect) ? Symbol.BadEffect : Symbol.Buff; break;
                    default: throw new ArgumentException($"EffectLook has no symbol for the piece kind '{piece.Do}'.");
                }
                if (!found.Contains(symbol)) found.Add(symbol);
            }
            return found;
        }

        // ---- the target mark (17.5): one dot, three dots, all dots, self -----------------------------------------------

        /// <summary>How many it reaches: one hero, a hero and their neighbours, everyone on a side, or only the user.</summary>
        public enum Reach { Self, One, Three, All }

        /// <summary>How many dots each reach draws; <see cref="Reach.Self"/> is one ring, not a dot.</summary>
        public static int Dots(Reach reach)
        {
            switch (reach)
            {
                case Reach.Self: return 1;
                case Reach.One: return 1;
                case Reach.Three: return 3;
                case Reach.All: return 5;
                default: throw new ArgumentOutOfRangeException(nameof(reach), reach, "no dots for this reach");
            }
        }

        /// <summary>
        /// The widest reach of any piece (reading R37): "all" targets beat a hero and their neighbours, which beat one hero,
        /// which beats self. So "damage one enemy, then Offense Up on self" is one dot.
        /// </summary>
        public static Reach ReachOf(SkillDef skill)
        {
            var widest = Reach.Self;
            foreach (var piece in skill.Pieces)
            {
                var reach = ReachOf(piece.Target);
                if (reach > widest) widest = reach;
            }
            return widest;
        }

        static Reach ReachOf(string target)
        {
            switch (target)
            {
                case "self": return Reach.Self;
                case "enemy":
                case "ally":
                case "most_hurt_ally":
                case "next_enemy": return Reach.One;
                case "enemy_and_neighbours": return Reach.Three;
                case "all_enemies":
                case "all_allies": return Reach.All;
                default: throw new ArgumentException($"EffectLook has no reach for the target '{target}'.");
            }
        }
    }
}
