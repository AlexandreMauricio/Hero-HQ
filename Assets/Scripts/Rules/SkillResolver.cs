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
    /// Does one skill (02 GDD 6 and 7, 03 Architecture 4.5): its pieces in the written order. Built so far:
    /// Damage pieces, the hit and crit rolls, the Barrier, knockouts (P1-09); Heal, Apply effect, Clear, Turn
    /// meter, the six piece conditions and ultimate charge (P1-10). Counters come with P1-11; bonuses with P1-12–14.
    ///
    /// A piece's condition is checked per target as the piece runs; when it is false the piece skips that
    /// target without touching it (no roll).
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
            var tuning = fight.Data.Tuning;
            var events = new List<FightEvent> { new FightEvent(FightEventKind.SkillUsed, actor) { Button = button } };
            var rolled = new Dictionary<Fighter, bool?>();       // per enemy, this skill: null = missed, else crit?

            // "first attack" (02 GDD 7.3) is this skill if the hero hadn't attacked before it, hit or miss (R4)
            var firstAttack = !actor.Attacked;
            if (skill.Pieces.Any(p => EnemyTargets.Contains(p.Target))) actor.Attacked = true;

            // the ultimate's charge goes back to 0 as it starts, so the meter empties with the cut-in (02 GDD 4)
            if (button == Button.Ultimate) events.AddRange(SetCharge(actor, 0));

            foreach (var piece in skill.Pieces)
            {
                // the piece's targets are fixed before it does anything; a hero knocked out on the way is skipped
                foreach (var target in Targeting.Targets(fight, actor, piece.Target, chosen).ToList())
                {
                    if (target.KnockedOut) continue;
                    var scale = Scale(fight, actor, piece.If, target, firstAttack);
                    if (scale == 0) continue;                     // condition false: the piece skips this hero, no roll
                    var crit = Contact(fight, actor, skill, target, rolled, events);
                    if (crit == null) continue;                   // missed: nothing this skill does reaches them
                    events.AddRange(Do(fight, actor, piece, target, scale, crit.Value));
                }
            }

            // action charge, even on a miss (02 GDD 4); the ultimate gives none
            if (button == Button.Basic) events.AddRange(SetCharge(actor, actor.Charge + tuning["charge_basic"]));
            if (button == Button.Special) events.AddRange(SetCharge(actor, actor.Charge + tuning["charge_special"]));
            return events;
        }

        /// <summary>
        /// One piece on one target that it has reached (02 GDD 7.1). <paramref name="scale"/> multiplies the
        /// piece's number (1 unless a <c>per_ally_with</c> condition counts more allies, reading R8).
        /// </summary>
        static IReadOnlyList<FightEvent> Do(Fight fight, Fighter actor, PieceDef piece, Fighter target, int scale, bool crit)
        {
            switch (piece.Do)
            {
                case "damage":
                    var events = new List<FightEvent>();
                    for (var hit = 0; hit < (piece.Hits ?? 1) && !target.KnockedOut; hit++)
                        events.AddRange(Hit(fight, actor, target, piece.Power.Value * scale, crit));
                    return events;

                case "heal":
                    // healing bonuses come with P1-12 and P1-13; no roll, no crit (02 GDD 6)
                    var healed = DamageMath.Heal(piece.Percent.Value * scale, new double[0], target.MaxHealth, target.Health);
                    target.Health += healed;
                    return new[] { new FightEvent(FightEventKind.Healed, target) { Amount = healed } };

                case "effect":
                    // a Barrier holds percent of the holder's max Health, halves up (reading R9)
                    var size = piece.Effect == "barrier" ? Whole(target.MaxHealth * piece.Percent.Value * scale / 100) : 0;
                    return Effects.Apply(target, piece.Effect, piece.Turns ?? 0, size);

                case "clear":
                    return Effects.Clear(target, piece.Count * scale);   // no count stays "all"

                case "meter":
                    return Meter(fight, target, piece.Percent.Value * scale);

                default:
                    throw new ArgumentException($"Unknown piece '{piece.Do}' (the loader should have stopped it).");
            }
        }

        /// <summary>
        /// A Turn meter piece (02 GDD 7.1, 03 Architecture 4.4): fills (+) or drains (−) a % of a full bar,
        /// rounded down; a drain never takes the meter below 0, a fill keeps what goes over a full bar (so the
        /// hero can act at once). The event says what really changed.
        /// </summary>
        static IReadOnlyList<FightEvent> Meter(Fight fight, Fighter target, double percent)
        {
            // the tiny nudge keeps a whole result that binary fractions store as 28.999… from rounding down to 28
            var size = (int)Math.Floor(fight.Data.Tuning["meter_full"] * Math.Abs(percent) / 100 + 1e-9);
            var before = target.Meter;
            target.Meter = percent >= 0 ? target.Meter + size : Math.Max(0, target.Meter - size);
            return new[] { new FightEvent(FightEventKind.MeterChanged, target) { Amount = target.Meter - before } };
        }

        /// <summary>
        /// A piece's condition (02 GDD 7.3) for one target, checked when the piece runs: 0 = skip, 1 = do it.
        /// <c>per_ally_with</c> gives the number of matching allies, which multiplies the piece (reading R8).
        /// </summary>
        static int Scale(Fight fight, Fighter actor, ConditionDef condition, Fighter target, bool firstAttack)
        {
            if (condition == null) return 1;
            switch (condition.Test)
            {
                case "allies_with": return AlliesWith(fight, actor, condition) >= condition.Count ? 1 : 0;
                case "per_ally_with": return AlliesWith(fight, actor, condition);
                case "target_has_effect": return target.Effects.Any(e => e.Kind == condition.Effect) ? 1 : 0;
                case "target_has": return Has(target, condition) ? 1 : 0;
                case "target_not_acted": return target.Acted ? 0 : 1;
                case "first_attack": return firstAttack ? 1 : 0;
                default: throw new ArgumentException($"Unknown condition '{condition.Test}' (the loader should have stopped it).");
            }
        }

        /// <summary>
        /// How many of the hero's other four allies have the condition's trait or class. Knocked-out allies
        /// count: trait and class counts are fixed at fight start (02 GDD 8).
        /// </summary>
        static int AlliesWith(Fight fight, Fighter actor, ConditionDef condition) =>
            (actor.Side == Side.Player ? fight.Players : fight.Enemies).Count(f => f != actor && Has(f, condition));

        static bool Has(Fighter fighter, ConditionDef condition) =>
            condition.Trait != null ? fighter.Hero.Traits.Contains(condition.Trait) : fighter.Hero.Class == condition.Class;

        /// <summary>Sets a hero's ultimate charge, capped at 100 (02 GDD 4); an event only if it really changed.</summary>
        static IReadOnlyList<FightEvent> SetCharge(Fighter hero, double charge)
        {
            charge = Math.Min(100, charge);
            if (charge == hero.Charge) return new FightEvent[0];
            hero.Charge = charge;
            return new[] { new FightEvent(FightEventKind.ChargeChanged, hero) { Charge = charge } };
        }

        /// <summary>A whole number, halves rounded up (03 Architecture 4.12, review checklist 4).</summary>
        static int Whole(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

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
        /// then Health drops (never below 0). At 0 the hero is knocked out at once and loses every effect (02 GDD 2);
        /// otherwise Health lost to an enemy charges the ultimate, + <c>charge_per_hurt</c> per 1 % of max Health
        /// (02 GDD 4; the Barrier's share is not lost, so it gives none).
        /// </summary>
        static IEnumerable<FightEvent> Hit(Fight fight, Fighter actor, Fighter target, double power, bool crit)
        {
            var tuning = fight.Data.Tuning;
            var amount = DamageMath.Damage(actor.Damage, power, OffenseChanges(actor, tuning), target.Armor,
                                           tuning["armor_k"], DefenseChanges(target, tuning), crit, actor.CritDamage);

            var barrierEvents = Effects.Soak(target, amount, out var soaked);
            var before = target.Health;
            target.Health = Math.Max(0, target.Health - (amount - soaked));
            var events = new List<FightEvent> { new FightEvent(FightEventKind.Damaged, target) { Amount = amount, Soaked = soaked, Crit = crit } };
            events.AddRange(barrierEvents);

            if (target.Health == 0)
            {
                target.KnockedOut = true;
                Effects.LoseAll(target);
                events.Add(new FightEvent(FightEventKind.KnockedOut, target));
            }
            else if (target.Side != actor.Side)
            {
                var lostPercent = (before - target.Health) * 100.0 / target.MaxHealth;
                events.AddRange(SetCharge(target, target.Charge + lostPercent * tuning["charge_per_hurt"]));
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
