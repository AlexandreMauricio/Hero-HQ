using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroHQ.Rules
{
    /// <summary>
    /// Who a skill may or must hit (02 GDD 2 Formation, 7.2; 10.6 questions 9 and 10). Plain functions that
    /// only look at the fight: they change nothing, roll nothing and make no events.
    ///
    /// Every list comes back in slot order and holds standing heroes only (a knocked-out hero can't be
    /// targeted, and the row closes up around them). "Opponents" and "allies" are relative to the acting hero,
    /// so the same code works for your side and the enemy's.
    /// </summary>
    public static class Targeting
    {
        /// <summary>The standing heroes on the other side from <paramref name="actor"/>.</summary>
        public static IReadOnlyList<Fighter> Opponents(Fight fight, Fighter actor) =>
            Standing(actor.Side == Side.Player ? fight.Enemies : fight.Players);

        /// <summary>
        /// The standing heroes on the actor's side. In a skill "ally" includes the hero (<paramref name="includeSelf"/>
        /// true: Halvard's Barrier or Doc Nefer's heal can land on themselves); in a trigger it means the other
        /// four (false). 02 GDD 10.6 question 9.
        /// </summary>
        public static IReadOnlyList<Fighter> Allies(Fight fight, Fighter actor, bool includeSelf) =>
            Standing(actor.Side == Side.Player ? fight.Players : fight.Enemies)
                .Where(f => includeSelf || f != actor).ToList();

        /// <summary>
        /// Who a skill aimed at a chosen enemy may pick (02 GDD 2 and 7.2), in this order:
        /// 1. while any opponent has Taunt, only the taunting heroes, in either row (a skill that
        ///    <paramref name="reachesBackRow"/> is no exception);
        /// 2. else, unless the skill reaches the back row, the front row while a front-row hero stands;
        /// 3. else every standing opponent (the front row is down, or the skill reaches the back row).
        /// </summary>
        public static IReadOnlyList<Fighter> Choosable(Fight fight, Fighter actor, bool reachesBackRow)
        {
            var opponents = Opponents(fight, actor);

            var taunting = opponents.Where(f => f.Effects.Any(e => e.Kind == "taunt")).ToList();
            if (taunting.Count > 0) return taunting;

            if (!reachesBackRow)
            {
                var front = opponents.Where(f => f.Row == Row.Front).ToList();
                if (front.Count > 0) return front;
            }
            return opponents;
        }

        /// <summary>
        /// The heroes next to <paramref name="fighter"/> in the same row, skipping knocked-out heroes (the row
        /// closes up). Never across rows. At most two: the standing hero above and the one below.
        /// </summary>
        public static IReadOnlyList<Fighter> Neighbours(Fight fight, Fighter fighter)
        {
            var side = fighter.Side == Side.Player ? fight.Players : fight.Enemies;
            var row = Standing(side).Where(f => f.Row == fighter.Row).ToList();
            var at = row.IndexOf(fighter);
            var neighbours = new List<Fighter>();
            if (at > 0) neighbours.Add(row[at - 1]);
            if (at >= 0 && at < row.Count - 1) neighbours.Add(row[at + 1]);
            return neighbours;
        }

        /// <summary>
        /// The heroes one piece hits, for the eight targets of 02 GDD 7.2 (the names as in the data).
        /// <paramref name="chosen"/> is the hero the player tapped, needed by <c>enemy</c>,
        /// <c>enemy_and_neighbours</c> and <c>ally</c> (it isn't checked against <see cref="Choosable"/>: the
        /// fight's <c>Options</c> and <c>Act</c> do that). <paramref name="inTrigger"/> is true for a piece fired
        /// by a leader skill or passive: there "ally" means the other four (10.6 question 9).
        /// </summary>
        public static IReadOnlyList<Fighter> Targets(Fight fight, Fighter actor, string target, Fighter chosen, bool inTrigger = false)
        {
            switch (target)
            {
                case "enemy":
                    return new[] { Chosen(target, chosen) };
                case "enemy_and_neighbours":
                    return Neighbours(fight, Chosen(target, chosen)).Append(chosen).OrderBy(f => f.Slot).ToList();
                case "all_enemies":
                    return Opponents(fight, actor);
                case "self":
                    return new[] { actor };
                case "ally":
                    return new[] { Chosen(target, chosen) };
                case "all_allies":
                    return Allies(fight, actor, includeSelf: !inTrigger);
                case "most_hurt_ally":
                    return MostHurt(Allies(fight, actor, includeSelf: !inTrigger));
                case "next_enemy":
                    return NextEnemy(fight, actor);
                default:
                    throw new ArgumentException($"Unknown target '{target}'.", nameof(target));
            }
        }

        static List<Fighter> Standing(IEnumerable<Fighter> fighters) => fighters.Where(f => !f.KnockedOut).ToList();

        static Fighter Chosen(string target, Fighter chosen)
        {
            if (chosen == null) throw new ArgumentException($"Target '{target}' needs the hero the player chose.", nameof(chosen));
            return chosen;
        }

        /// <summary>
        /// The lowest Health % (Health compared with MaxHealth by cross-multiplying, so no rounding), ties to
        /// the lowest slot. Nobody standing gives an empty list.
        /// </summary>
        static IReadOnlyList<Fighter> MostHurt(IReadOnlyList<Fighter> candidates)
        {
            Fighter best = null;
            foreach (var f in candidates)   // slot order, so on a tie the earlier hero is kept
                if (best == null || (long)f.Health * best.MaxHealth < (long)best.Health * f.MaxHealth) best = f;
            return best == null ? new Fighter[0] : new[] { best };
        }

        /// <summary>
        /// The first opponent on the turn bar, a turn marked skipped (stunned) included: what the bar shows is
        /// what the skill hits (02 GDD 10.6 question 10). Asks <see cref="Fight.PredictTurns"/> for a longer bar
        /// until an opponent shows up (fast heroes can fill the first entries).
        /// </summary>
        static IReadOnlyList<Fighter> NextEnemy(Fight fight, Fighter actor)
        {
            if (Opponents(fight, actor).Count == 0) return new Fighter[0];
            for (var count = Math.Max(1, (int)fight.Data.Tuning["turn_bar_length"]); count <= 4096; count *= 2)
            {
                var first = fight.PredictTurns(count).FirstOrDefault(t => t.Who.Side != actor.Side).Who;
                if (first != null) return new[] { first };
            }
            throw new InvalidOperationException("No opponent acts within 4096 turns: the Speeds must be wrong.");
        }
    }
}
