namespace HeroHQ.Rules
{
    /// <summary>
    /// The kinds of things that can happen in a fight (03 Architecture 4.7). Each task adds the kinds its rules
    /// make happen (skill used, damaged, healed… from P1-09 on).
    /// </summary>
    public enum FightEventKind
    {
        /// <summary>A hero's turn started: their meter was full and the tie rule picked them.</summary>
        TurnStarted,
        /// <summary>The hero's turn is skipped because they are stunned (the Stun is used up with it).</summary>
        TurnSkipped,
        /// <summary>An effect landed on a hero (<see cref="FightEvent.Effect"/> names it). Also when it only reset a copy the hero had.</summary>
        EffectApplied,
        /// <summary>A Ward stopped a bad effect (<see cref="FightEvent.Effect"/> is the one that was stopped).</summary>
        EffectBlocked,
        /// <summary>An opposite removed an effect (<see cref="FightEvent.Effect"/> is the one removed; the new one is not added).</summary>
        EffectCancelled,
        /// <summary>An effect ran out of turns.</summary>
        EffectExpired,
        /// <summary>An effect was spent: a Ward that stopped something, a Stun whose turn was skipped.</summary>
        EffectUsedUp,
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
        public FightEvent(FightEventKind kind, Fighter who = null, string effect = null)
        {
            Kind = kind;
            Who = who;
            Effect = effect;
        }

        public FightEventKind Kind { get; }

        /// <summary>The hero it happened to; null when it is about the whole fight (won, lost).</summary>
        public Fighter Who { get; }

        /// <summary>The effect it is about (its data name), for the effect kinds; null otherwise.</summary>
        public string Effect { get; }

        public override string ToString()
        {
            var text = Who == null ? Kind.ToString() : $"{Kind}: {Who}";
            return Effect == null ? text : $"{text} ({Effect})";
        }
    }
}
