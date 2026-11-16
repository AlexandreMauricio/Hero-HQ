using System.Collections.Generic;

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
        /// <summary>
        /// The acting hero uses a skill (<see cref="FightEvent.Button"/> says which). Always the first event of
        /// an action, so an ultimate's cut-in plays before any of its hits (03 Architecture 4.7).
        /// </summary>
        SkillUsed,
        /// <summary>The skill missed this hero: nothing else it does touches them (02 GDD 6).</summary>
        Missed,
        /// <summary>One hit landed on this hero (<see cref="FightEvent.Amount"/>, <see cref="FightEvent.Crit"/>, <see cref="FightEvent.Soaked"/>).</summary>
        Damaged,
        /// <summary>The hero's Health hit 0: out of the fight at once, all effects lost (02 GDD 2).</summary>
        KnockedOut,
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

        /// <summary>SkillUsed: which button. Null otherwise.</summary>
        public Button? Button { get; internal set; }

        /// <summary>Damaged: the damage number of this hit (what the screen shows), the Barrier's share included.</summary>
        public int Amount { get; internal set; }

        /// <summary>Damaged: how much of <see cref="Amount"/> the Barrier soaked; the rest came off Health.</summary>
        public int Soaked { get; internal set; }

        /// <summary>Damaged: a critical hit.</summary>
        public bool Crit { get; internal set; }

        /// <summary>
        /// Kind, hero and the details, e.g. "Damaged: Enemy 0 e0 (182, crit, 100 soaked)". Two fights' event lists
        /// are compared by this text (P1-16), so every detail that matters is in it.
        /// </summary>
        public override string ToString()
        {
            var text = Who == null ? Kind.ToString() : $"{Kind}: {Who}";
            var details = new List<string>();
            if (Effect != null) details.Add(Effect);
            if (Button != null) details.Add(Button.ToString());
            if (Kind == FightEventKind.Damaged)
            {
                details.Add(Amount.ToString());
                if (Crit) details.Add("crit");
                if (Soaked > 0) details.Add($"{Soaked} soaked");
            }
            return details.Count == 0 ? text : $"{text} ({string.Join(", ", details)})";
        }
    }
}
