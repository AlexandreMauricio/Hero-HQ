namespace HeroHQ.Rules
{
    /// <summary>Which team a fighter is on. The player's side wins a tie (02 GDD 3, tie step 3).</summary>
    public enum Side
    {
        Player,
        Enemy,
    }

    /// <summary>
    /// One hero in a fight (03 Architecture 4.1). It holds only what the fight has needed so far; Health,
    /// ultimate charge, Special cooldown, effects and the "acted yet" flags are added by the tasks that
    /// use them (P1-06 onward).
    /// </summary>
    public class Fighter
    {
        public Fighter(Side side, int slot, int speed)
        {
            Side = side;
            Slot = slot;
            Speed = speed;
        }

        public Side Side { get; }

        /// <summary>Place in the team's slot order: front row top to bottom, then back row (02 GDD 2). 0 is first.</summary>
        public int Slot { get; }

        /// <summary>Final Speed, a whole number, fixed for the whole fight (02 GDD 3).</summary>
        public int Speed { get; }

        /// <summary>The turn meter. A whole number; a bar from 0 to <c>meter_full</c>, and above it the overflow is kept.</summary>
        public int Meter { get; set; }

        /// <summary>A knocked-out hero is out of the turn order and never fills.</summary>
        public bool KnockedOut { get; set; }
    }
}
