using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HeroHQ.Rules.Data;

namespace HeroHQ.Screens
{
    /// <summary>
    /// One plain-English sentence per skill, built from the skill's pieces (03 Architecture 7.7, 02 GDD 17.5 and 17.10
    /// item 3), so the info strip can't disagree with what the rules do. Plain C#: it reads the data classes only and
    /// knows no hero, class, trait or effect by name; it turns data names into words ("offense_up" becomes "Offense
    /// Up"). A trait or class is
    /// written as its id in capitals ("greek" is "Greek"), which is also its name in today's data. P2b-03 added
    /// <see cref="LeaderSentence"/> and <see cref="PassiveSentence"/> (02 GDD 17.10 item 3), built from the same pieces,
    /// for the hero card and, in P2b-10, for team select.
    ///
    /// The shape: <c>Name: piece, then piece, then piece</c>. Example (02 GDD 17.5): "Return to Sender: 150 % damage to
    /// one enemy, then 150 % more if it hasn't acted yet this fight".
    /// </summary>
    public static class SkillText
    {
        /// <summary>The skill's name, a colon, then one phrase per piece in the written order, joined with ", then ".</summary>
        public static string Sentence(SkillDef skill)
        {
            var phrases = new List<string>();
            var enemyChosen = false;     // an earlier piece already aimed at the chosen enemy, so "one enemy" becomes "that enemy"
            var allyChosen = false;
            PieceDef previous = null;

            foreach (var piece in skill.Pieces)
            {
                var again = piece.Target == "ally" ? allyChosen : enemyChosen;
                phrases.Add(Phrase(piece, previous, again, "self"));
                if (piece.Target == "enemy" || piece.Target == "enemy_and_neighbours") enemyChosen = true;
                if (piece.Target == "ally") allyChosen = true;
                previous = piece;
            }
            return skill.Name + ": " + string.Join(", then ", phrases);
        }

        /// <summary><paramref name="self"/>: how "self" reads, "self" in a skill, "itself" in a passive, "themselves" in a leader skill's trigger.</summary>
        static string Phrase(PieceDef piece, PieceDef previous, bool again, string self)
        {
            var who = Who(piece.Target, again, self);
            string text;
            switch (piece.Do)
            {
                case "damage":
                    text = Damage(piece, previous, who);
                    break;
                case "heal":
                    text = $"heal {who} for {Num(piece.Percent)} % of max Health";
                    break;
                case "effect":
                    text = $"{EffectName(piece)} on {who}";
                    if ((piece.Turns ?? 0) > 0) text += $" for {piece.Turns} turn{(piece.Turns == 1 ? "" : "s")}";
                    break;
                case "clear":
                    text = piece.Count == null ? $"clear all bad effects from {who}"
                         : $"clear {piece.Count} bad effect{(piece.Count == 1 ? "" : "s")} from {who}";
                    break;
                case "meter" when piece.Target == "self" && self != "self":
                    text = $"{(piece.Percent >= 0 ? "fill" : "drain")} {(self == "itself" ? "its" : "their")} own turn meter by {Num(Math.Abs(piece.Percent ?? 0))} %";
                    break;
                case "meter":
                    text = piece.Percent >= 0 ? $"fill the turn meter of {who} by {Num(piece.Percent)} %"
                                              : $"drain the turn meter of {who} by {Num(-piece.Percent)} %";
                    break;
                default:
                    throw new ArgumentException($"SkillText has no words for the piece kind '{piece.Do}'.");
            }
            return piece.If == null ? text : text + " " + Condition(piece.If);
        }

        static string Damage(PieceDef piece, PieceDef previous, string who)
        {
            var hits = piece.Hits ?? 1;
            // "150 % more if ...": a conditional damage piece straight after damage to the same target reads as a bonus
            var isBonus = piece.If != null && hits <= 1 && previous != null && previous.Do == "damage" && previous.Target == piece.Target;
            var text = isBonus ? $"{Num(piece.Power)} % more"
                     : hits > 1 ? $"{hits} hits of {Num(piece.Power)} % damage to {who}"
                     : $"{Num(piece.Power)} % damage to {who}";

            var flags = new List<string>();
            if (piece.CantMiss) flags.Add("can't miss");
            if (piece.AlwaysCrits) flags.Add("always crits");
            if (piece.ReachesBackRow) flags.Add("reaches the back row");
            return flags.Count == 0 ? text : $"{text} ({string.Join(", ", flags)})";
        }

        // ---- leader skills and passives (P2b-03; 02 GDD 17.10 item 3) -----------------------------------------------

        /// <summary>
        /// A leader skill in words, from its bonuses and triggers: "+20 % Damage for every Tech ally" and, for a trigger,
        /// "At the start of the fight, every Greek ally: fill their own turn meter by 50 %". Several parts are joined
        /// with "; ". The name comes first when the data gives one.
        /// </summary>
        public static string LeaderSentence(BonusSkillDef leader) => Bonus(leader, true);

        /// <summary>A passive in words: "+5 % Dodge Chance for each other ally that is Greek", "When an ally falls below half Health: Taunt on itself for 1 turn".</summary>
        public static string PassiveSentence(BonusSkillDef passive) => Bonus(passive, false);

        static string Bonus(BonusSkillDef def, bool leader)
        {
            var parts = new List<string>();
            foreach (var b in def.Bonuses ?? new BonusDef[0])
            {
                var text = $"+{Num(b.Percent)} % {Words(b.Stat)}";
                if (leader) text += " for " + (b.Who == null ? "every ally" : "every " + Words(b.Who.Trait ?? b.Who.Class) + " ally");
                parts.Add(b.If == null ? text : text + " " + Condition(b.If));
            }
            foreach (var t in def.Triggers ?? new TriggerDef[0])
            {
                var who = !leader ? "" : t.Who == null ? " every ally" : " every " + Words(t.Who.Trait ?? t.Who.Class) + " ally";
                var self = leader ? "themselves" : "itself";
                var pieces = new List<string>();
                PieceDef previous = null;
                foreach (var piece in t.Pieces)
                {
                    pieces.Add(Phrase(piece, previous, false, self));
                    previous = piece;
                }
                parts.Add($"{When(t.When)}{(leader ? "," + who : "")}: {string.Join(", then ", pieces)}");
            }
            var sentence = string.Join("; ", parts);
            return string.IsNullOrEmpty(def.Name) ? sentence : def.Name + ": " + sentence;
        }

        /// <summary>The four trigger moments of 02 GDD 8.</summary>
        static string When(string when)
        {
            switch (when)
            {
                case "fight_start": return "At the start of the fight";
                case "turn_start": return "At the start of its turn";
                case "attack_hits": return "Whenever its attack hits";
                case "ally_below_half": return "When an ally falls below half Health";
                default: throw new ArgumentException($"SkillText has no words for the trigger moment '{when}'.");
            }
        }

        /// <summary>The eight targets of 02 GDD 7.2. <paramref name="again"/>: the chosen hero was named by an earlier piece.</summary>
        static string Who(string target, bool again, string self = "self")
        {
            switch (target)
            {
                case "enemy": return again ? "that enemy" : "one enemy";
                case "enemy_and_neighbours": return again ? "that enemy and their neighbours" : "one enemy and their neighbours";
                case "all_enemies": return "all enemies";
                case "self": return self;
                case "ally": return again ? "that ally" : "one ally";
                case "all_allies": return "all allies";
                case "most_hurt_ally": return "the most hurt ally";
                case "next_enemy": return "the enemy who acts next";
                default: throw new ArgumentException($"SkillText has no words for the target '{target}'.");
            }
        }

        /// <summary>The six conditions of 02 GDD 7.3; "it" is the hero the piece is aimed at.</summary>
        static string Condition(ConditionDef c)
        {
            switch (c.Test)
            {
                case "allies_with": return $"if {c.Count} or more other allies are {Words(c.Trait ?? c.Class)}";
                case "per_ally_with": return $"for each other ally that is {Words(c.Trait ?? c.Class)}";
                case "target_has_effect": return $"if it has {Words(c.Effect)}";
                case "target_has": return $"if it is {Words(c.Trait ?? c.Class)}";
                case "target_not_acted": return "if it hasn't acted yet this fight";
                case "first_attack": return "if it is this hero's first attack";
                default: throw new ArgumentException($"SkillText has no words for the condition '{c.Test}'.");
            }
        }

        /// <summary>The effect's name in words; a Barrier also says its size (a piece's percent is % of the holder's max Health).</summary>
        static string EffectName(PieceDef piece) =>
            piece.Effect == "barrier" ? $"Barrier ({Num(piece.Percent)} % of max Health)" : Words(piece.Effect);

        /// <summary>A data name in words: "offense_up" is "Offense Up" (the same as the fight log's). The effect chips use it too.</summary>
        internal static string Words(string dataName)
        {
            if (string.IsNullOrEmpty(dataName)) throw new ArgumentException("SkillText was given a name that is missing in the data.");
            return string.Join(" ", dataName.Split('_').Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
        }

        /// <summary>A number the way a player reads it: 150, 12.5 (never 150.00).</summary>
        static string Num(double? n) => (n ?? 0).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
