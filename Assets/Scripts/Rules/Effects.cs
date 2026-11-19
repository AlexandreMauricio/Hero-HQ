using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroHQ.Rules
{
    /// <summary>
    /// One effect sitting on a hero (02 GDD 7.4). Made and changed only by <see cref="Effects"/>.
    /// </summary>
    public class Effect
    {
        internal Effect(string kind, int endsAfterTurn, int amount)
        {
            Kind = kind;
            EndsAfterTurn = endsAfterTurn;
            Amount = amount;
        }

        /// <summary>The effect's name as in the data: offense_up, offense_down, defense_up, defense_down, taunt, barrier, ward, counter, stun or momentum.</summary>
        public string Kind { get; }

        /// <summary>
        /// The holder's own turn number after which the effect ends (<see cref="Effects.NoEnd"/> for Stun and
        /// Momentum). A hero counts their own turns (<see cref="Fighter.TurnsStarted"/>), so "N turns" put on at
        /// turn k ends after turn k + N: exactly "the end of the holder's Nth turn that starts after it was applied".
        /// </summary>
        public int EndsAfterTurn { get; internal set; }

        /// <summary>Barrier: how much damage it can still soak. Momentum: how many stacks it has. 0 for every other effect.</summary>
        public int Amount { get; internal set; }

        internal Effect Copy() => (Effect)MemberwiseClone();

        public override string ToString() => Kind;
    }

    /// <summary>
    /// Putting effects on heroes and taking them off (02 GDD 7.4, 03 Architecture 4.5). Plain functions on one
    /// <see cref="Fighter"/>: no state of its own, no luck. Each returns the <see cref="FightEvent"/>s of what
    /// happened (checklist rule 3: every change makes an event).
    /// </summary>
    public static class Effects
    {
        /// <summary>The end turn of an effect that has no turn count (Stun, Momentum): it never runs out by itself.</summary>
        public const int NoEnd = int.MaxValue;

        /// <summary>The ten effects (02 GDD 7.4), the one list: the data loader checks names against it and the screen's
        /// <c>EffectLook</c> must have an icon for each (a test walks it).</summary>
        public static readonly IReadOnlyList<string> Kinds = new[]
        {
            "offense_up", "offense_down", "defense_up", "defense_down", "taunt",
            "barrier", "ward", "counter", "stun", "momentum",
        };

        static readonly string[] Bad = { "offense_down", "defense_down", "stun" };

        // Opposites cancel (02 GDD 7.4): applying one to a hero with the other removes the other instead.
        static readonly (string, string)[] Opposites = { ("offense_up", "offense_down"), ("defense_up", "defense_down") };

        /// <summary>A bad effect (a debuff): Offense Down, Defense Down and Stun. A Ward stops exactly these.</summary>
        public static bool IsBad(string kind) => Bad.Contains(kind);

        public static bool IsStunned(Fighter fighter) => fighter.Effects.Any(e => e.Kind == "stun");

        /// <summary>
        /// Puts an effect on a hero, in this order:
        /// 1. a knocked-out hero gets nothing; a second Stun on a stunned hero does nothing (it wouldn't land,
        ///    so it doesn't spend a Ward either: INFERENCE, 02 GDD is silent);
        /// 2. a bad effect meets a Ward first: the Ward stops it and is used up (02 GDD 10.6 q. 6);
        /// 3. an opposite on the hero is removed and the new effect is not added;
        /// 4. otherwise it is added, or, if the hero already has one, that one is reset to the longer time
        ///    (a Barrier also keeps the bigger amount). One copy of each, new ones go last (the order is what
        ///    "Clear, newest first" will use).
        /// <paramref name="turns"/> is how many of the holder's turns it lasts (ignored for Stun and Momentum,
        /// which have no turn count); <paramref name="amount"/> is a Barrier's size in Health, or, for Momentum,
        /// the most stacks it may have (<c>momentum_max</c>): each application adds one stack (P1-14).
        /// </summary>
        public static IReadOnlyList<FightEvent> Apply(Fighter target, string kind, int turns, int amount = 0)
        {
            var events = new List<FightEvent>();
            if (target.KnockedOut) return events;
            if (kind == "stun" && IsStunned(target)) return events;

            if (IsBad(kind))
            {
                var ward = target.EffectList.FirstOrDefault(e => e.Kind == "ward");
                if (ward != null)
                {
                    target.EffectList.Remove(ward);
                    events.Add(new FightEvent(FightEventKind.EffectBlocked, target, kind));
                    events.Add(new FightEvent(FightEventKind.EffectUsedUp, target, "ward"));
                    return events;
                }
            }

            foreach (var (a, b) in Opposites)
            {
                var opposite = kind == a ? b : kind == b ? a : null;
                var present = opposite == null ? null : target.EffectList.FirstOrDefault(e => e.Kind == opposite);
                if (present == null) continue;
                target.EffectList.Remove(present);
                events.Add(new FightEvent(FightEventKind.EffectCancelled, target, opposite));
                return events;
            }

            var endsAfter = kind == "stun" || kind == "momentum" ? NoEnd : target.TurnsStarted + turns;
            var existing = target.EffectList.FirstOrDefault(e => e.Kind == kind);
            if (kind == "momentum")
            {
                // Momentum is the one effect that stacks (02 GDD 7.4): +1 stack each time, up to the most allowed
                // (`amount`, = momentum_max). It keeps its stacks in `Amount`.
                if (existing == null) target.EffectList.Add(new Effect(kind, endsAfter, Math.Min(1, amount)));
                else existing.Amount = Math.Min(existing.Amount + 1, amount);
            }
            else if (existing == null)
                target.EffectList.Add(new Effect(kind, endsAfter, amount));
            else
            {
                existing.EndsAfterTurn = Math.Max(existing.EndsAfterTurn, endsAfter);
                existing.Amount = Math.Max(existing.Amount, amount);
            }
            events.Add(new FightEvent(FightEventKind.EffectApplied, target, kind));
            return events;
        }

        /// <summary>
        /// 02 GDD 2, step 5 (the effects part): at the end of a hero's turn, every effect whose last turn this
        /// was ends, in the order they were put on.
        /// </summary>
        public static IReadOnlyList<FightEvent> EndTurn(Fighter fighter)
        {
            var events = new List<FightEvent>();
            foreach (var effect in fighter.EffectList.Where(e => fighter.TurnsStarted >= e.EndsAfterTurn).ToList())
            {
                fighter.EffectList.Remove(effect);
                events.Add(new FightEvent(FightEventKind.EffectExpired, fighter, effect.Kind));
            }
            return events;
        }

        /// <summary>
        /// Takes an effect off because it was spent (a used Ward, Counter or Barrier, or the Stun when its turn
        /// is skipped). Says nothing if the hero doesn't have it.
        /// </summary>
        public static IReadOnlyList<FightEvent> UseUp(Fighter fighter, string kind)
        {
            var events = new List<FightEvent>();
            var effect = fighter.EffectList.FirstOrDefault(e => e.Kind == kind);
            if (effect == null) return events;
            fighter.EffectList.Remove(effect);
            events.Add(new FightEvent(FightEventKind.EffectUsedUp, fighter, kind));
            return events;
        }

        /// <summary>
        /// A rule takes an effect away whole (Momentum after a skill that missed every target, 02 GDD 7.4). Says
        /// nothing if the hero doesn't have it.
        /// </summary>
        public static IReadOnlyList<FightEvent> Lose(Fighter fighter, string kind)
        {
            var effect = fighter.EffectList.FirstOrDefault(e => e.Kind == kind);
            if (effect == null) return new FightEvent[0];
            fighter.EffectList.Remove(effect);
            return new[] { new FightEvent(FightEventKind.EffectLost, fighter, kind) };
        }

        /// <summary>
        /// A Clear piece (02 GDD 7.1): takes up to <paramref name="count"/> bad effects off the hero, newest first
        /// (null = all of them). Buffs stay.
        /// </summary>
        public static IReadOnlyList<FightEvent> Clear(Fighter fighter, int? count)
        {
            var events = new List<FightEvent>();
            var bad = fighter.EffectList.Where(e => IsBad(e.Kind)).Reverse().Take(count ?? int.MaxValue).ToList();
            foreach (var effect in bad)
            {
                fighter.EffectList.Remove(effect);
                events.Add(new FightEvent(FightEventKind.EffectCleared, fighter, effect.Kind));
            }
            return events;
        }

        /// <summary>
        /// A Barrier soaks a hit before Health does (02 GDD 6 and 7.4): it takes as much of <paramref name="damage"/>
        /// as it still holds and gives back how much that was. Emptied, it is used up (its event comes back);
        /// otherwise it keeps the rest. No Barrier soaks nothing.
        /// </summary>
        public static IReadOnlyList<FightEvent> Soak(Fighter fighter, int damage, out int soaked)
        {
            var barrier = fighter.EffectList.FirstOrDefault(e => e.Kind == "barrier");
            soaked = barrier == null ? 0 : Math.Min(barrier.Amount, damage);
            if (barrier == null) return new FightEvent[0];
            barrier.Amount -= soaked;
            return barrier.Amount == 0 ? UseUp(fighter, "barrier") : new FightEvent[0];
        }

        /// <summary>
        /// A knocked-out hero loses all effects (02 GDD 2). Silent: the knockout event (P1-09) tells the story.
        /// </summary>
        public static void LoseAll(Fighter fighter) => fighter.EffectList.Clear();
    }
}
