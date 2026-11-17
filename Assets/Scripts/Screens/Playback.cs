using System;
using System.Collections.Generic;
using HeroHQ.Rules;

namespace HeroHQ.Screens
{
    /// <summary>
    /// What the battle screen shows of the fight while it plays an action back (03 Architecture 7.2, QA finding 3).
    ///
    /// The rules finish a whole action before the screen starts to draw it, and a <see cref="FightEvent.Who"/> is the
    /// live fighter, already at its end-of-action Health and charge. So the screen must not read those mid-playback.
    /// This class keeps its own copy of each hero's Health and charge and changes it one event at a time, as each event
    /// is shown. Plain C#: no scene objects, nothing drawn, no waiting.
    ///
    /// A fight starts with full Health and 0 charge (<see cref="Fight.Create"/>), so that is where the copy starts, and
    /// <see cref="Fight.StartEvents"/> are played like any other batch. A hero's shown Health is 0 exactly when their
    /// <see cref="FightEventKind.KnockedOut"/> is due, so the screen greys a figure when <see cref="Health"/> reads 0.
    /// </summary>
    public sealed class Playback
    {
        readonly Dictionary<Fighter, int> health = new Dictionary<Fighter, int>();
        readonly Dictionary<Fighter, double> charge = new Dictionary<Fighter, double>();

        /// <summary>Starts every hero of the fight at full Health and 0 charge. Call it right after <see cref="Fight.Create"/>.</summary>
        public Playback(Fight fight)
        {
            foreach (var side in new[] { fight.Players, fight.Enemies })
                foreach (var hero in side)
                {
                    health[hero] = hero.MaxHealth;       // the one thing read from the fighter: it never changes
                    charge[hero] = 0;
                }
        }

        /// <summary>The Health the screen shows for this hero now.</summary>
        public int Health(Fighter who) => health[who];

        /// <summary>The ultimate charge the screen shows for this hero now (0–100, fractions kept; the bar shows whole numbers).</summary>
        public double Charge(Fighter who) => charge[who];

        /// <summary>
        /// Takes one event into account: a hit takes off its Health share (the Barrier's share was soaked), a heal adds,
        /// a charge change sets the new value. Every other kind changes neither. Call it when the event is shown.
        /// </summary>
        public void Apply(FightEvent e)
        {
            switch (e.Kind)
            {
                case FightEventKind.Damaged:
                    health[e.Who] = Math.Max(0, health[e.Who] - (e.Amount - e.Soaked));   // the rules clamp at 0 the same way
                    break;
                case FightEventKind.Healed:
                    health[e.Who] += e.Amount;
                    break;
                case FightEventKind.ChargeChanged:
                    charge[e.Who] = e.Charge;
                    break;
            }
        }

        /// <summary>Applies a whole batch of events in order, with no waiting (for a screen that jumps to the end of an action).</summary>
        public void Apply(IEnumerable<FightEvent> batch)
        {
            foreach (var e in batch) Apply(e);
        }

        /// <summary>
        /// Whether an event of this kind gets a beat of its own (about 0.4 s, 02 GDD 17.6): the kinds that show something
        /// over a hero. The bookkeeping kinds change the bars at once. A skill's use gets none because its banner or its
        /// <i>off</i> look is timed on its own; the effect chips are redrawn after the action. Throws for a kind nobody has
        /// sorted yet, so a new <see cref="FightEventKind"/> can't slip in unsorted (a test walks them all).
        /// </summary>
        public static bool GetsBeat(FightEventKind kind)
        {
            switch (kind)
            {
                case FightEventKind.Missed:
                case FightEventKind.Damaged:        // damage, crit and Barrier soak are all one hit
                case FightEventKind.KnockedOut:
                case FightEventKind.Healed:
                case FightEventKind.EffectApplied:
                case FightEventKind.EffectBlocked:  // "WARDED"
                case FightEventKind.CounterAttack:  // "COUNTER!"
                case FightEventKind.TurnSkipped:    // "<name> is stunned: turn skipped"
                    return true;

                case FightEventKind.TurnStarted:
                case FightEventKind.SkillUsed:
                case FightEventKind.EffectCleared:
                case FightEventKind.MeterChanged:
                case FightEventKind.ChargeChanged:
                case FightEventKind.EffectCancelled:
                case FightEventKind.EffectExpired:
                case FightEventKind.EffectUsedUp:
                case FightEventKind.EffectLost:
                case FightEventKind.FightWon:
                case FightEventKind.FightLost:
                    return false;

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "this event kind has not been sorted into beat or no beat");
            }
        }
    }
}
