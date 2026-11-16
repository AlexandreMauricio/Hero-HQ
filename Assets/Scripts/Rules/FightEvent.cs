namespace HeroHQ.Rules
{
    /// <summary>
    /// The kinds of things that can happen in a fight (03 Architecture 4.7). Each task adds the kinds its rules
    /// make happen (skill used, damaged, effect applied… from P1-07 on).
    /// </summary>
    public enum FightEventKind
    {
        /// <summary>A hero's turn started: their meter was full and the tie rule picked them.</summary>
        TurnStarted,
        /// <summary>All five enemies are knocked out (02 GDD 2, step 6).</summary>
        FightWon,
        /// <summary>All five of your heroes are knocked out.</summary>
        FightLost,
    }

    /// <summary>
    /// One thing that happened, in order. The battle screen plays these back, the fight log turns each into a
    /// sentence and the fight report counts them: one list for all three, so they can't disagree (4.7).
    /// </summary>
    public class FightEvent
    {
        public FightEvent(FightEventKind kind, Fighter who = null)
        {
            Kind = kind;
            Who = who;
        }

        public FightEventKind Kind { get; }

        /// <summary>The hero it happened to; null when it is about the whole fight (won, lost).</summary>
        public Fighter Who { get; }

        public override string ToString() => Who == null ? Kind.ToString() : $"{Kind}: {Who}";
    }
}
