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
    /// One hero in a fight (03 Architecture 4.1). It holds only what the fight has needed so far; later tasks
    /// add the fields they need.
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

        // The final stats the skills use (P1-09), whole numbers set by Fight.Create from the data (bonuses: P1-12).
        // Chance stats are percentages as written (25 = 25 %); Crit Damage 150 = 1.5 times.

        public int Damage { get; set; }
        public int Armor { get; set; }
        public int Accuracy { get; set; }
        public int DodgeChance { get; set; }
        public int CritChance { get; set; }
        public int CritDamage { get; set; }

        /// <summary>
        /// The fixed % from leader and passive bonuses added to every damage the hero deals, with Offense Up and
        /// the rest, applied once (02 GDD 6; 03 Architecture 4.6). Not a stat: never rounded. Set by Fight.Create (P1-12).
        /// </summary>
        public double DamageBonus { get; set; }

        /// <summary>The fixed % from leader and passive bonuses added to every heal the hero gives (02 GDD 6, 10.2).</summary>
        public double HealingBonus { get; set; }

        /// <summary>
        /// The leader and passive bonuses this hero receives that wait for the moment (03 Architecture 4.6:
        /// first attack, target has a trait, class or effect, target hasn't acted). Checked each time one of the
        /// hero's skills or counter-attacks touches a target; never by a triggered piece. Set by Fight.Create (P1-13).
        /// </summary>
        public IReadOnlyList<BonusLine> MomentBonuses { get; set; } = new BonusLine[0];

        /// <summary>Ultimate charge from 0 to 100, fractions kept (02 GDD 4). The Ultimate is usable at 100.</summary>
        public double Charge { get; set; }

        /// <summary>
        /// The hero's own turns before the Special is ready again; 0 = ready (every Special starts ready, 02 GDD 4).
        /// Set to the Special's cooldown after it is used; counts down at the end of each later turn of the hero.
        /// </summary>
        public int Cooldown { get; set; }

        /// <summary>The hero has taken a turn this fight (used a button). A skipped, stunned turn and a counter-attack are not acting (02 GDD 10.6 q. 4).</summary>
        public bool Acted { get; set; }

        /// <summary>The hero has used a skill aimed at an enemy this fight, hit or miss (reading R4): "first attack" is over.</summary>
        public bool Attacked { get; set; }

        /// <summary>
        /// The hero has dropped below half Health while standing, once this fight (P1-14): the "ally below half"
        /// triggers (Halvard's Taunt) fire once per ally, the first time only (02 GDD 8).
        /// </summary>
        public bool FellBelowHalf { get; set; }

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
