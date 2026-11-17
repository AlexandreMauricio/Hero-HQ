using System;
using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules.Data;

namespace HeroHQ.Rules
{
    /// <summary>Where a bonus comes from: the named leader's leader skill, or the hero's own passive (02 GDD 8).</summary>
    public enum BonusSource
    {
        Leader,
        Passive,
    }

    /// <summary>
    /// One bonus a hero receives (02 GDD 8, 03 Architecture 4.6): one line of team select's list (02 GDD 17.2).
    /// A <b>fixed</b> line (no condition, or a trait or class count) is ON or OFF for the whole fight;
    /// a line <see cref="AtTheMoment"/> waits for the moment a skill touches a target, so it has no ON or OFF.
    /// </summary>
    public class BonusLine
    {
        internal BonusLine(BonusSource source, HeroDef from, BonusDef bonus, bool atTheMoment, bool on, int allies, double percent)
        {
            Source = source;
            From = from;
            Bonus = bonus;
            AtTheMoment = atTheMoment;
            On = on;
            Allies = allies;
            Percent = percent;
        }

        public BonusSource Source { get; }

        /// <summary>The hero whose leader skill or passive it is.</summary>
        public HeroDef From { get; }

        /// <summary>The bonus as written in the data: stat, percent, who, if.</summary>
        public BonusDef Bonus { get; }

        public string Stat => Bonus.Stat;

        /// <summary>The condition depends on the moment a skill is used (Strix's first attack…): checked then, not now (P1-13).</summary>
        public bool AtTheMoment { get; }

        /// <summary>A fixed line that counts for the whole fight. Always false on a line at the moment.</summary>
        public bool On { get; }

        /// <summary>For a count condition, how many of the hero's other four allies have the trait or class (the "have" of 17.10); else 0.</summary>
        public int Allies { get; }

        /// <summary>What the line adds: the written percent, times <see cref="Allies"/> for "for each ally with", 0 when OFF.
        /// On a line at the moment, the written percent.</summary>
        public double Percent { get; }
    }

    /// <summary>
    /// One hero's bonuses for a given team and leader, and the final stats they give (02 GDD 8, 10.2, 10.6 q. 1–2).
    /// <see cref="Fight.Create"/> builds every fighter from this, so team select shows exactly what the fight uses.
    /// </summary>
    public class HeroBonuses
    {
        internal HeroBonuses(HeroDef hero, int slot, IReadOnlyList<BonusLine> lines)
        {
            Hero = hero;
            Slot = slot;
            Lines = lines;
        }

        public HeroDef Hero { get; }
        public int Slot { get; }

        /// <summary>The leader's lines first, then the passive's, each in the order written.</summary>
        public IReadOnlyList<BonusLine> Lines { get; }

        /// <summary>The sum of the fixed lines that are ON for one stat (bonuses add, 02 GDD 8).</summary>
        public double Total(string stat) => Lines.Where(l => l.On && l.Stat == stat).Sum(l => l.Percent);

        /// <summary>
        /// A final stat, a whole number, rounded once with halves up (10.6 q. 2). Health, Armor and Speed grow by
        /// a % of the stat; Accuracy, Dodge Chance and Crit Chance add points (10.2). Damage is not changed here:
        /// its bonus stays a % that the damage formula adds to Offense Up (<see cref="DamageBonus"/>).
        /// </summary>
        public int Stat(string stat)
        {
            var value = Hero.Stats[stat];
            switch (stat)
            {
                case "health":
                case "armor":
                case "speed":
                    value *= 1 + Total(stat) / 100;
                    break;
                case "accuracy":
                case "dodge_chance":
                case "crit_chance":
                    value += Total(stat);
                    break;
            }
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        /// <summary>The fixed % added to every damage the hero deals (02 GDD 6: added with Offense Up, applied once).</summary>
        public double DamageBonus => Total("damage");

        /// <summary>The fixed % added to every heal the hero gives (02 GDD 6, 10.2).</summary>
        public double HealingBonus => Total("healing");
    }

    /// <summary>
    /// Leader and passive bonuses (02 GDD 8; 03 Architecture 4.6), fixed part. Only the named leader's leader
    /// skill counts; trait and class counts are taken once, here, so nothing switches off after a knockout and a
    /// fallen leader's bonuses stay. Bonuses that depend on the moment are listed, not applied (P1-13).
    /// </summary>
    public static class Bonuses
    {
        /// <summary>
        /// The team-select preview (02 GDD 8, 17.2): for five hero ids in slot order and the named leader, each
        /// hero's bonus lines and final stats, in slot order. <see cref="Fight.Create"/> calls this same function.
        /// </summary>
        public static IReadOnlyList<HeroBonuses> Preview(GameData data, IReadOnlyList<string> slots, string leader)
        {
            var heroes = slots.Select(id => data.Heroes[id]).ToList();
            var leaderSkill = data.Heroes[leader];
            return heroes.Select((hero, slot) =>
            {
                var others = heroes.Where((_, i) => i != slot).ToList();   // the other four (02 GDD 7.3)
                var lines = new List<BonusLine>();
                foreach (var bonus in leaderSkill.Leader.Bonuses ?? new BonusDef[0])
                    if (Matches(hero, bonus.Who))
                        lines.Add(Line(BonusSource.Leader, leaderSkill, bonus, others));
                foreach (var bonus in hero.Passive.Bonuses ?? new BonusDef[0])
                    lines.Add(Line(BonusSource.Passive, hero, bonus, others));
                return new HeroBonuses(hero, slot, lines);
            }).ToList();
        }

        /// <summary>
        /// One bonus for one hero. A count condition is counted among the RECEIVING hero's other four allies
        /// (a leader's bonus works as that ally's own, like its triggers, 03 Architecture 3.2).
        /// </summary>
        static BonusLine Line(BonusSource source, HeroDef from, BonusDef bonus, IReadOnlyList<HeroDef> others)
        {
            var percent = bonus.Percent.Value;
            var condition = bonus.If;
            if (condition == null) return new BonusLine(source, from, bonus, false, true, 0, percent);

            var allies = others.Count(h => Has(h, condition.Trait, condition.Class));
            switch (condition.Test)
            {
                case "allies_with":
                    var on = allies >= condition.Count;
                    return new BonusLine(source, from, bonus, false, on, allies, on ? percent : 0);
                case "per_ally_with":
                    return new BonusLine(source, from, bonus, false, allies > 0, allies, percent * allies);
                default:
                    return new BonusLine(source, from, bonus, true, false, 0, percent);
            }
        }

        /// <summary>A leader's <c>who</c> filter: everyone when empty; else the trait and/or the class must match.</summary>
        static bool Matches(HeroDef hero, WhoDef who) =>
            who == null || ((who.Trait == null || hero.Traits.Contains(who.Trait)) && (who.Class == null || hero.Class == who.Class));

        /// <summary>A trait or class condition: the trait if one is named, else the class.</summary>
        internal static bool Has(HeroDef hero, string trait, string cls) =>
            trait != null ? hero.Traits.Contains(trait) : hero.Class == cls;
    }
}
