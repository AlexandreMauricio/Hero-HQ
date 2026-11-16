using System;
using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules.Data;

namespace HeroHQ.Rules
{
    /// <summary>How the fight stands, from your side: still going, won, or lost.</summary>
    public enum Outcome
    {
        Going,
        Won,
        Lost,
    }

    /// <summary>The three buttons of 02 GDD 4.</summary>
    public enum Button
    {
        Basic,
        Special,
        Ultimate,
    }

    /// <summary>What the acting hero does: a button and the hero tapped (null when the skill needs no tap).</summary>
    public readonly struct Choice
    {
        public Choice(Button button, Fighter target)
        {
            Button = button;
            Target = target;
        }

        public Button Button { get; }
        public Fighter Target { get; }
    }

    /// <summary>One entry of the turn-order bar: whose turn, and whether it will be skipped because they are stunned.</summary>
    public readonly struct PredictedTurn
    {
        public PredictedTurn(Fighter who, bool skipped)
        {
            Who = who;
            Skipped = skipped;
        }

        public Fighter Who { get; }
        public bool Skipped { get; }
    }

    /// <summary>
    /// One fight (03 Architecture 4.2–4.4). The screens call it in this loop (4.3):
    ///
    ///   fight = Fight.Create(data, playerTeam, enemyTeam, seed)
    ///   fight.AdvanceToChoice()
    ///   while fight.Outcome is Going:
    ///       fight.Act(the player's tap, or the AI's choice)
    ///       fight.AdvanceToChoice()
    ///   bar = fight.PredictTurns(turn_bar_length)      // after any call, to draw the turn bar
    ///
    /// Nothing here waits or draws: every call returns the list of what happened and the screen plays it back.
    /// Built so far: the clock, the end of a turn and the win check (P1-06), effects and Stun (P1-07). The skill
    /// itself does nothing yet (P1-09); bonuses (P1-12) and triggers (P1-13) come with their tasks.
    /// </summary>
    public class Fight
    {
        readonly List<Fighter> players;
        readonly List<Fighter> enemies;
        readonly List<Fighter> all;          // players then enemies; TurnMeter works on everyone at once
        readonly Random random;              // null on a copy: a copy may never roll (4.2)
        readonly int meterFull;

        Fight(GameData data, List<Fighter> players, List<Fighter> enemies, Random random)
        {
            Data = data;
            this.players = players;
            this.enemies = enemies;
            this.random = random;
            all = players.Concat(enemies).ToList();
            meterFull = Whole(data.Tuning["meter_full"]);
        }

        /// <summary>The game's content, read-only and shared with every copy.</summary>
        public GameData Data { get; }

        /// <summary>Your five heroes in slot order (front row top to bottom, then the back row).</summary>
        public IReadOnlyList<Fighter> Players => players;

        /// <summary>The five enemies in slot order.</summary>
        public IReadOnlyList<Fighter> Enemies => enemies;

        /// <summary>The hero whose turn it is and who must choose; null between turns.</summary>
        public Fighter Acting { get; private set; }

        public Outcome Outcome { get; private set; }

        /// <summary>
        /// Sets up a fight between two teams of the data (02 GDD 2): each hero in their slot, every meter at 0
        /// (02 GDD 3), Health full. <paramref name="seed"/> fixes the hit and crit rolls, so the same seed replays
        /// the same fight (4.9).
        /// </summary>
        public static Fight Create(GameData data, TeamDef playerTeam, TeamDef enemyTeam, int seed)
        {
            return new Fight(data, Team(data, playerTeam, Side.Player), Team(data, enemyTeam, Side.Enemy), new Random(seed));
        }

        static List<Fighter> Team(GameData data, TeamDef team, Side side)
        {
            // "3-2" = three in front, two behind: the first slots stand in the front row (02 GDD 2). The loader
            // has already checked the formation is one of the four allowed.
            var inFront = int.Parse(team.Formation.Split('-')[0]);
            return team.Slots.Select((id, slot) =>
            {
                var hero = data.Heroes[id];
                var health = Whole(hero.Stats["health"]);
                return new Fighter(side, slot, Whole(hero.Stats["speed"]), hero)
                {
                    Row = slot < inFront ? Row.Front : Row.Back,
                    Health = health,
                    MaxHealth = health,
                };
            }).ToList();
        }

        /// <summary>
        /// 02 GDD 2, steps 1–2: runs the clock (ticks) until someone's meter is full; the tie rule picks who
        /// acts, and that hero is now <see cref="Acting"/> and must choose. A stunned hero's turn is skipped
        /// right here (the Stun is used up, the turn ends like any other) and the clock carries on to the next.
        /// Does nothing while a hero is already choosing or once the fight is over.
        /// </summary>
        public IReadOnlyList<FightEvent> AdvanceToChoice()
        {
            var events = new List<FightEvent>();
            if (Outcome != Outcome.Going || Acting != null) return events;

            while (Outcome == Outcome.Going)
            {
                while ((Acting = TurnMeter.Next(all, meterFull)) == null)
                    TurnMeter.Tick(all);
                Acting.TurnsStarted++;
                events.Add(new FightEvent(FightEventKind.TurnStarted, Acting));
                if (!Effects.IsStunned(Acting)) break;

                // start-of-turn passives fire here even when stunned (P1-13), then the turn is skipped
                events.Add(new FightEvent(FightEventKind.TurnSkipped, Acting));
                events.AddRange(Effects.UseUp(Acting, "stun"));
                events.AddRange(EndTurn());
            }
            return events;
        }

        /// <summary>
        /// 02 GDD 2, steps 4–6: does the acting hero's choice, ends their turn (the meter drops by one full bar
        /// and keeps the overflow) and checks for a win. The skill itself does nothing yet (P1-09).
        /// </summary>
        public IReadOnlyList<FightEvent> Act(Choice choice)
        {
            if (Outcome != Outcome.Going) throw new InvalidOperationException("The fight is over.");
            if (Acting == null) throw new InvalidOperationException("Nobody is choosing: call AdvanceToChoice first.");
            var events = new List<FightEvent>();

            // step 4, the skill: P1-09

            // step 5 and 6, end of turn and win check
            events.AddRange(EndTurn());
            return events;
        }

        /// <summary>
        /// 02 GDD 2, steps 5–6, shared by a played turn and a skipped one: the acting hero's meter drops by one
        /// full bar (keeping the overflow), their effects count down, then the win check. Both sides can't be
        /// down at once: only one side attacks at a time, and a knocked-out hero can't counter-attack (02 GDD 2).
        /// </summary>
        List<FightEvent> EndTurn()
        {
            var events = new List<FightEvent>();
            TurnMeter.EndTurn(Acting, meterFull);
            events.AddRange(Effects.EndTurn(Acting));
            Acting = null;

            if (enemies.All(f => f.KnockedOut))
            {
                Outcome = Outcome.Won;
                events.Add(new FightEvent(FightEventKind.FightWon));
            }
            else if (players.All(f => f.KnockedOut))
            {
                Outcome = Outcome.Lost;
                events.Add(new FightEvent(FightEventKind.FightLost));
            }
            return events;
        }

        /// <summary>
        /// The turn-order bar (02 GDD 3, 03 Architecture 4.4): who acts in the next <paramref name="count"/>
        /// turns if nobody's action changes the meters, each marked <see cref="PredictedTurn.Skipped"/> when that
        /// hero is stunned (their first turn only: the Stun ends with it). Runs the same clock on a copy with
        /// nobody acting, so the fight itself never changes. While a hero is choosing, they come first. The
        /// fighters returned are this fight's own. Exact until the next action; ask again after every <see cref="Act"/>.
        /// </summary>
        public IReadOnlyList<PredictedTurn> PredictTurns(int count)
        {
            var copy = Copy();
            var turns = new List<PredictedTurn>();
            if (copy.all.All(f => f.KnockedOut)) return turns;

            while (turns.Count < count)
            {
                var next = TurnMeter.Next(copy.all, meterFull);
                if (next == null)
                {
                    TurnMeter.Tick(copy.all);
                    continue;
                }
                var skipped = Effects.IsStunned(next);
                turns.Add(new PredictedTurn(all[copy.all.IndexOf(next)], skipped));
                if (skipped) Effects.UseUp(next, "stun");
                TurnMeter.EndTurn(next, meterFull);
            }
            return turns;
        }

        /// <summary>
        /// A full, independent duplicate (a deep copy, see the Glossary): changing it never touches this fight.
        /// It shares the read-only data and has no random generator, so any roll on it is an error (4.2).
        /// </summary>
        public Fight Copy()
        {
            var copy = new Fight(Data, players.Select(f => f.Copy()).ToList(), enemies.Select(f => f.Copy()).ToList(), random: null)
            {
                Outcome = Outcome,
            };
            if (Acting != null) copy.Acting = copy.all[all.IndexOf(Acting)];
            return copy;
        }

        /// <summary>
        /// One roll of the fight's luck: true with a chance of <paramref name="percent"/> % (0 never, 100 always).
        /// Only hit and crit roll (02 GDD 2), from the skill code (P1-09). A copy has no generator and throws.
        /// </summary>
        public bool Roll(double percent)
        {
            if (random == null)
                throw new InvalidOperationException("A copy of the fight can't roll: the turn bar's prediction never uses luck (03 Architecture 4.2).");
            return random.NextDouble() * 100 < percent;
        }

        /// <summary>A stat as a whole number, halves rounded up (03 Architecture 4.12, review checklist 4).</summary>
        static int Whole(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
