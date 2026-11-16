using System.Collections.Generic;
using System.Linq;

namespace HeroHQ.Rules
{
    /// <summary>
    /// The turn meter of 02 GDD 3, as three small steps the fight calls in this order:
    ///
    ///   repeat:
    ///       while <see cref="Next"/> gives a hero: that hero takes a turn, then <see cref="EndTurn"/>
    ///       <see cref="Tick"/>
    ///
    /// No luck anywhere: the same meters always give the same order, which is what lets the turn-order
    /// bar promise who acts next (03 Architecture 4.4).
    /// </summary>
    public static class TurnMeter
    {
        /// <summary>One tick: every living hero's meter grows by their Speed.</summary>
        public static void Tick(IEnumerable<Fighter> fighters)
        {
            foreach (var fighter in fighters)
                if (!fighter.KnockedOut) fighter.Meter += fighter.Speed;
        }

        /// <summary>
        /// The living hero who acts now, or null if nobody's meter is full. When several are full, the tie
        /// rule picks (02 GDD 3), in this order: 1. the fuller meter (it got there earlier inside the tick),
        /// 2. the higher Speed, 3. the player's side before the enemy's, 4. the lower slot number.
        /// </summary>
        public static Fighter Next(IEnumerable<Fighter> fighters, int meterFull)
        {
            return fighters
                .Where(f => !f.KnockedOut && f.Meter >= meterFull)
                .OrderByDescending(f => f.Meter)
                .ThenByDescending(f => f.Speed)
                .ThenBy(f => f.Side)      // Player comes before Enemy
                .ThenBy(f => f.Slot)
                .FirstOrDefault();
        }

        /// <summary>After a turn the meter drops by one full bar and keeps what spilled over.</summary>
        public static void EndTurn(Fighter fighter, int meterFull)
        {
            fighter.Meter -= meterFull;
        }
    }
}
