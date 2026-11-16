using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules.Data;

namespace HeroHQ.Rules
{
    /// <summary>Which team a fighter is on. The player's side wins a tie (02 GDD 3, tie step 3).</summary>
    public enum Side
    {
        Player,
        Enemy,
    }

    /// <summary>Which row a hero stands in (02 GDD 2, Formation). Fixed for the whole fight.</summary>
    public enum Row
    {
        Front,
        Back,
    }

    /// <summary>
    /// One hero in a fight (03 Architecture 4.1). It holds only what the fight has needed so far; ultimate
    /// charge, Special cooldown and the "acted yet" flags are added by the tasks that use them (P1-09 onward).
    /// </summary>
    public class Fighter
    {
        public Fighter(Side side, int slot, int speed, HeroDef hero = null)
        {
            Side = side;
            Slot = slot;
            Speed = speed;
            Hero = hero;
        }

        /// <summary>The hero's data (read-only, shared with every copy of the fight). Null only in bare turn-meter tests.</summary>
        public HeroDef Hero { get; }

        public Side Side { get; }

        /// <summary>Place in the team's slot order: front row top to bottom, then back row (02 GDD 2). 0 is first.</summary>
        public int Slot { get; }

        /// <summary>Final Speed, a whole number, fixed for the whole fight (02 GDD 3).</summary>
        public int Speed { get; }

        /// <summary>The turn meter. A whole number; a bar from 0 to <c>meter_full</c>, and above it the overflow is kept.</summary>
        public int Meter { get; set; }

        /// <summary>The row from the team's formation: the first slots stand in front (P1-08). Never changes during a fight.</summary>
        public Row Row { get; set; }

        /// <summary>Health now, a whole number. Starts at the hero's final Health.</summary>
        public int Health { get; set; }

        /// <summary>The hero's final Health at fight start (the most Health they can have). "Most hurt ally" compares Health as a % of this.</summary>
        public int MaxHealth { get; set; }

        /// <summary>A knocked-out hero is out of the turn order and never fills.</summary>
        public bool KnockedOut { get; set; }

        /// <summary>
        /// How many of this hero's own turns have started, a skipped (stunned) turn included. Effect durations
        /// are counted in these (see <see cref="Effect.EndsAfterTurn"/>).
        /// </summary>
        public int TurnsStarted { get; set; }

        List<Effect> effects = new List<Effect>();

        /// <summary>The effects on this hero, in the order they were put on. Only the static Effects class (Effects.cs) changes the list.</summary>
        public IReadOnlyList<Effect> Effects => effects;

        internal List<Effect> EffectList => effects;

        /// <summary>
        /// A full, independent duplicate for <see cref="Fight.Copy"/>. The numbers, flags and read-only data are
        /// copied as they are; the effects list is copied record by record, or the copy and the fight would
        /// share it. A task that adds another list must copy it here too.
        /// </summary>
        public Fighter Copy()
        {
            var copy = (Fighter)MemberwiseClone();
            copy.effects = effects.Select(e => e.Copy()).ToList();
            return copy;
        }

        public override string ToString() => $"{Side} {Slot} {Hero?.Id}";
    }
}
