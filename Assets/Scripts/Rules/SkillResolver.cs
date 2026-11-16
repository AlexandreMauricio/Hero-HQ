using System;
using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules.Data;

namespace HeroHQ.Rules
{
    /// <summary>
    /// What one button offers the acting hero (03 Architecture 4.3, 02 GDD 17.5): can it be pressed, does it need
    /// a tapped hero and which ones, and the miss chance to show over each enemy before the tap (02 GDD 6).
    /// </summary>
    public class ButtonOption
    {
        internal ButtonOption(Button button, SkillDef skill, bool usable, bool needsChoice,
                              IReadOnlyList<Fighter> choosable, IReadOnlyDictionary<Fighter, double> missChance)
        {
            Button = button;
            Skill = skill;
            Usable = usable;
            NeedsChoice = needsChoice;
            Choosable = choosable;
            MissChance = missChance;
        }

        public Button Button { get; }

        /// <summary>The skill behind the button (its name for the label, its pieces for the symbols).</summary>
        public SkillDef Skill { get; }

        /// <summary>Basic always; Special when its cooldown is 0; Ultimate when the charge is 100 (02 GDD 4).</summary>
        public bool Usable { get; }

        /// <summary>True when the skill aims at a chosen enemy (or the centre of one and their neighbours) or a chosen ally.</summary>
        public bool NeedsChoice { get; }

        /// <summary>Who a tap may pick, in slot order (Taunt, front row first…); empty when the skill needs no choice.</summary>
        public IReadOnlyList<Fighter> Choosable { get; }

        /// <summary>
        /// Every standing enemy's miss chance in percent, for a skill that touches enemies (empty for heals and
        /// buffs: they never miss). It is the very number the roll uses, so the chip can't disagree with it.
        /// </summary>
        public IReadOnlyDictionary<Fighter, double> MissChance { get; }
    }

    /// <summary>
    /// Does one skill (02 GDD 6 and 7, 03 Architecture 4.5): its pieces in the written order. Built so far
    /// (P1-09): Damage pieces, the hit and crit rolls, the Barrier, knockouts. Heal, Apply effect, Clear, Turn
    /// meter, piece conditions, cooldown and charge come with P1-10; counters with P1-11; bonuses with P1-12–14.
    ///
    /// The rolls (02 GDD 6): the first time a skill touches an enemy, that enemy gets one hit roll and, on a
    /// hit, one crit roll; the result holds for the rest of the skill (a miss cancels everything the skill does
    /// to them). Targets are met in the pieces' order, each piece's targets in slot order, so a seed always
    /// rolls the same way. Only the fight's own generator rolls (<see cref="Fight.Roll"/>).
    /// </summary>
    public static class SkillResolver
    {
        static readonly string[] EnemyTargets = { "enemy", "enemy_and_neighbours", "all_enemies", "next_enemy" };
        static readonly string[] ChosenEnemyTargets = { "enemy", "enemy_and_neighbours" };

        public static SkillDef Skill(Fighter hero, Button button)
        {
            switch (button)
            {
                case Button.Basic: return hero.Hero.Basic;
                case Button.Special: return hero.Hero.Special;
                default: return hero.Hero.Ultimate;
            }
        }

        /// <summary>The acting hero's options for one button (see <see cref="ButtonOption"/>).</summary>
        public static ButtonOption Option(Fight fight, Fighter actor, Button button)
        {
            var skill = Skill(actor, button);
            var usable = button == Button.Basic
                      || (button == Button.Special && actor.Cooldown == 0)
                      || (button == Button.Ultimate && actor.Charge >= 100);

            var chosenEnemy = skill.Pieces.Where(p => ChosenEnemyTargets.Contains(p.Target)).ToList();
            var chosenAlly = skill.Pieces.Any(p => p.Target == "ally");
            if (chosenEnemy.Count > 0 && chosenAlly)
                throw new NotSupportedException($"{actor.Hero.Id}'s {button} aims at a chosen enemy and a chosen ally: a skill chooses one hero (02 GDD 7.2).");

            // reading R2 (03 Architecture 4.12): the skill reaches the back row if any of its chosen-enemy pieces does
            var choosable = chosenEnemy.Count > 0 ? Targeting.Choosable(fight, actor, chosenEnemy.Any(p => p.ReachesBackRow))
                          : chosenAlly ? Targeting.Allies(fight, actor, includeSelf: true)
                          : new Fighter[0];

            var missChance = skill.Pieces.Any(p => EnemyTargets.Contains(p.Target))
                ? Targeting.Opponents(fight, actor).ToDictionary(e => e, e => MissChance(fight, actor, skill, e))
                : new Dictionary<Fighter, double>();

            return new ButtonOption(button, skill, usable, chosenEnemy.Count > 0 || chosenAlly, choosable, missChance);
        }

        /// <summary>
        /// The chance, in percent, that this skill misses <paramref name="target"/> (02 GDD 6). "Can't miss" is a
        /// property of the skill: it holds if any of its pieces is marked so (reading R6, 03 Architecture 4.12).
        /// </summary>
        public static double MissChance(Fight fight, Fighter actor, SkillDef skill, Fighter target) =>
            DamageMath.MissChance(target.DodgeChance, actor.Accuracy, fight.Data.Tuning["max_dodge"], skill.Pieces.Any(p => p.CantMiss));

        /// <summary>
        /// 02 GDD 2, step 4: the acting hero uses <paramref name="button"/>. <paramref name="chosen"/> is the
        /// tapped hero (already checked by <see cref="Fight.Act"/>), null when the skill needs no choice. The
        /// first event is always <see cref="FightEventKind.SkillUsed"/>, so an ultimate's cut-in plays first.
        /// </summary>
        public static IReadOnlyList<FightEvent> Use(Fight fight, Fighter actor, Button button, Fighter chosen)
        {
            var skill = Skill(actor, button);
            var events = new List<FightEvent> { new FightEvent(FightEventKind.SkillUsed, actor) { Button = button } };
            var rolled = new Dictionary<Fighter, bool?>();       // per enemy, this skill: null = missed, else crit?

            foreach (var piece in skill.Pieces)
            {
                if (piece.Do != "damage" || piece.If != null)
                    throw new NotSupportedException($"{actor.Hero.Id}'s {button}: '{piece.Do}' pieces and piece conditions come with P1-10.");

                // the piece's targets are fixed before it hits anyone; a hero knocked out on the way is skipped
                foreach (var target in Targeting.Targets(fight, actor, piece.Target, chosen).ToList())
                {
                    if (target.KnockedOut) continue;
                    var crit = Contact(fight, actor, skill, target, rolled, events);
                    if (crit == null) continue;                   // missed: nothing this skill does reaches them
                    for (var hit = 0; hit < (piece.Hits ?? 1) && !target.KnockedOut; hit++)
                        events.AddRange(Hit(fight, actor, target, piece.Power.Value, crit.Value));
                }
            }
            return events;
        }

        /// <summary>
        /// The skill's first touch on a hero decides hit and crit for the whole skill (02 GDD 6): one hit roll,
        /// then, on a hit, one crit roll, skipped when the skill always crits (reading R6: if any piece is marked
        /// so). Returns null for a miss, else whether it crits. A hero of the acting hero's own side is never
        /// rolled for: it lands, no crit (no approved kit damages its own side).
        /// </summary>
        static bool? Contact(Fight fight, Fighter actor, SkillDef skill, Fighter target,
                             Dictionary<Fighter, bool?> rolled, List<FightEvent> events)
        {
            if (target.Side == actor.Side) return false;
            if (rolled.TryGetValue(target, out var known)) return known;

            bool? result;
            if (fight.Roll(MissChance(fight, actor, skill, target)))
            {
                result = null;
                events.Add(new FightEvent(FightEventKind.Missed, target));
            }
            else
                result = skill.Pieces.Any(p => p.AlwaysCrits) || fight.Roll(actor.CritChance);
            rolled[target] = result;
            return result;
        }

        /// <summary>
        /// One hit (02 GDD 6): the damage number from <see cref="DamageMath.Damage"/>, the Barrier soaks first,
        /// then Health drops (never below 0). At 0 the hero is knocked out at once and loses every effect (02 GDD 2).
        /// </summary>
        static IEnumerable<FightEvent> Hit(Fight fight, Fighter actor, Fighter target, double power, bool crit)
        {
            var tuning = fight.Data.Tuning;
            var amount = DamageMath.Damage(actor.Damage, power, OffenseChanges(actor, tuning), target.Armor,
                                           tuning["armor_k"], DefenseChanges(target, tuning), crit, actor.CritDamage);

            var barrierEvents = Effects.Soak(target, amount, out var soaked);
            target.Health = Math.Max(0, target.Health - (amount - soaked));
            var events = new List<FightEvent> { new FightEvent(FightEventKind.Damaged, target) { Amount = amount, Soaked = soaked, Crit = crit } };
            events.AddRange(barrierEvents);

            if (target.Health == 0)
            {
                target.KnockedOut = true;
                Effects.LoseAll(target);
                events.Add(new FightEvent(FightEventKind.KnockedOut, target));
            }
            return events;
        }

        /// <summary>The attacker's % changes to damage dealt from effects: Offense Up +, Offense Down − (02 GDD 7.4). Momentum: P1-14; leader and passives: P1-12.</summary>
        static IEnumerable<double> OffenseChanges(Fighter actor, IReadOnlyDictionary<string, double> tuning) =>
            actor.Effects.Select(e => e.Kind == "offense_up" ? tuning["offense_change"]
                                    : e.Kind == "offense_down" ? -tuning["offense_change"] : 0);

        /// <summary>The target's % changes to damage taken: Defense Up −, Defense Down + (02 GDD 7.4).</summary>
        static IEnumerable<double> DefenseChanges(Fighter target, IReadOnlyDictionary<string, double> tuning) =>
            target.Effects.Select(e => e.Kind == "defense_up" ? -tuning["defense_change"]
                                     : e.Kind == "defense_down" ? tuning["defense_change"] : 0);
    }
}
