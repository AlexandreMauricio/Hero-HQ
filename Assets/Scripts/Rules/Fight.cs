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
    /// Built so far: the clock, the end of a turn and the win check (P1-06), effects and Stun (P1-07), the
    /// buttons and Damage pieces (P1-09, SkillResolver), the other pieces, cooldown and charge (P1-10);
    /// counters (P1-11), the fixed leader and passive bonuses (P1-12); fight-start and turn-start triggers and
    /// moment bonuses (P1-13). The attack-hits and ally-below-half triggers come with P1-14.
    /// </summary>
    public class Fight
    {
        readonly List<Fighter> players;
        readonly List<Fighter> enemies;
        readonly List<Fighter> all;          // players then enemies; TurnMeter works on everyone at once
        readonly Random random;              // null on a copy: a copy may never roll (4.2)
        readonly int meterFull;
        readonly HeroDef playerLeader;       // the named leaders: their leader skills count all fight, even after a knockout (02 GDD 8)
        readonly HeroDef enemyLeader;

        Fight(GameData data, List<Fighter> players, List<Fighter> enemies, Random random, HeroDef playerLeader, HeroDef enemyLeader)
        {
            Data = data;
            this.players = players;
            this.enemies = enemies;
            this.random = random;
            this.playerLeader = playerLeader;
            this.enemyLeader = enemyLeader;
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
        /// What the fight-start triggers did inside <see cref="Create"/> (Wards put on, meters filled), in the
        /// order it happened. The screen plays these first; the log and the report count them. Empty on a copy.
        /// </summary>
        public IReadOnlyList<FightEvent> StartEvents { get; private set; } = new FightEvent[0];

        /// <summary>
        /// Sets up a fight between two teams of the data (02 GDD 2): each hero in their slot, every meter at 0
        /// (02 GDD 3), Health full, then the fight-start triggers (<see cref="StartEvents"/>).
        /// <paramref name="seed"/> fixes the hit and crit rolls, so the same seed replays the same fight (4.9).
        /// </summary>
        public static Fight Create(GameData data, TeamDef playerTeam, TeamDef enemyTeam, int seed)
        {
            var fight = new Fight(data, Team(data, playerTeam, Side.Player), Team(data, enemyTeam, Side.Enemy), new Random(seed),
                                  data.Heroes[playerTeam.Leader], data.Heroes[enemyTeam.Leader]);
            fight.StartEvents = fight.FightStart();
            return fight;
        }

        /// <summary>
        /// The fight-start triggers in the order of 02 GDD 10.6 q. 5: the leader skills first, yours then the
        /// enemy's (each trigger on each ally it names, in slot order, as that ally's own), then the passives in
        /// slot order, your side first.
        /// </summary>
        List<FightEvent> FightStart()
        {
            var events = new List<FightEvent>();
            foreach (var hero in all)
                foreach (var trigger in Bonuses.Triggers(Leader(hero.Side), hero.Hero, "fight_start", fromLeader: true, fromPassive: false))
                    events.AddRange(SkillResolver.Trigger(this, hero, trigger));
            foreach (var hero in all)
                foreach (var trigger in Bonuses.Triggers(Leader(hero.Side), hero.Hero, "fight_start", fromLeader: false, fromPassive: true))
                    events.AddRange(SkillResolver.Trigger(this, hero, trigger));
            return events;
        }

        /// <summary>The named leader of one side (02 GDD 8).</summary>
        HeroDef Leader(Side side) => side == Side.Player ? playerLeader : enemyLeader;

        static List<Fighter> Team(GameData data, TeamDef team, Side side)
        {
            // "3-2" = three in front, two behind: the first slots stand in the front row (02 GDD 2). The loader
            // has already checked the formation is one of the four allowed.
            var inFront = int.Parse(team.Formation.Split('-')[0]);
            // Final stats from the named leader's skill and the passives, worked out once, here: the same
            // calculation team select shows (02 GDD 8; 03 Architecture 4.6, P1-12).
            return Bonuses.Preview(data, team.Slots, team.Leader).Select(b => new Fighter(side, b.Slot, b.Stat("speed"), b.Hero)
            {
                Row = b.Slot < inFront ? Row.Front : Row.Back,
                Health = b.Stat("health"),
                MaxHealth = b.Stat("health"),
                Damage = b.Stat("damage"),
                Armor = b.Stat("armor"),
                Accuracy = b.Stat("accuracy"),
                DodgeChance = b.Stat("dodge_chance"),
                CritChance = b.Stat("crit_chance"),
                CritDamage = b.Stat("crit_damage"),
                DamageBonus = b.DamageBonus,
                HealingBonus = b.HealingBonus,
                MomentBonuses = b.Lines.Where(l => l.AtTheMoment).ToList(),
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

                // start-of-turn triggers (the leader's that name this hero, then the hero's passive), even when stunned (02 GDD 2 step 2)
                foreach (var trigger in Bonuses.Triggers(Leader(Acting.Side), Acting.Hero, "turn_start", fromLeader: true, fromPassive: true))
                    events.AddRange(SkillResolver.Trigger(this, Acting, trigger));
                if (!Effects.IsStunned(Acting)) break;

                // a stunned turn is skipped
                events.Add(new FightEvent(FightEventKind.TurnSkipped, Acting));
                events.AddRange(Effects.UseUp(Acting, "stun"));
                events.AddRange(EndTurn());
            }
            return events;
        }

        /// <summary>
        /// The acting hero's three buttons, Basic, Special, Ultimate (03 Architecture 4.3): usable or not, who
        /// may be tapped, and each enemy's miss chance to show before the tap. Empty while nobody is choosing.
        /// </summary>
        public IReadOnlyList<ButtonOption> Options =>
            Acting == null
                ? new ButtonOption[0]
                : new[] { Button.Basic, Button.Special, Button.Ultimate }.Select(b => SkillResolver.Option(this, Acting, b)).ToList();

        /// <summary>
        /// 02 GDD 2, steps 4–6: does the acting hero's choice, ends their turn (the meter drops by one full bar
        /// and keeps the overflow) and checks for a win. A choice <see cref="Options"/> doesn't allow (a button
        /// that can't be pressed, a hero who can't be chosen) is an error and changes nothing; a skill that needs
        /// no choice ignores the tapped hero.
        /// </summary>
        public IReadOnlyList<FightEvent> Act(Choice choice)
        {
            if (Outcome != Outcome.Going) throw new InvalidOperationException("The fight is over.");
            if (Acting == null) throw new InvalidOperationException("Nobody is choosing: call AdvanceToChoice first.");
            var option = SkillResolver.Option(this, Acting, choice.Button);
            if (!option.Usable) throw new ArgumentException($"{Acting}'s {choice.Button} can't be used now.", nameof(choice));
            if (option.NeedsChoice && !option.Choosable.Contains(choice.Target))
                throw new ArgumentException($"{Acting}'s {choice.Button} can't choose {choice.Target?.ToString() ?? "nobody"}.", nameof(choice));
            var events = new List<FightEvent>();
            var actor = Acting;

            // step 4, the skill (counters come with P1-11)
            events.AddRange(SkillResolver.Use(this, actor, choice.Button, option.NeedsChoice ? choice.Target : null));
            actor.Acted = true;

            // step 5 and 6, end of turn and win check
            events.AddRange(EndTurn());

            // the Special's cooldown starts after this turn: greyed on the hero's next `cooldown` turns (02 GDD 4)
            if (choice.Button == Button.Special) actor.Cooldown = actor.Hero.Special.Cooldown ?? 0;
            return events;
        }

        /// <summary>
        /// 02 GDD 2, steps 5–6, shared by a played turn and a skipped one: the acting hero's meter drops by one
        /// full bar (keeping the overflow), their effects and Special cooldown count down (a skipped turn too,
        /// reading R10), then the win check. Both sides can't be down at once: only one side attacks at a time,
        /// and a knocked-out hero can't counter-attack (02 GDD 2).
        /// </summary>
        List<FightEvent> EndTurn()
        {
            var events = new List<FightEvent>();
            TurnMeter.EndTurn(Acting, meterFull);
            events.AddRange(Effects.EndTurn(Acting));
            if (Acting.Cooldown > 0) Acting.Cooldown--;
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
            var copy = new Fight(Data, players.Select(f => f.Copy()).ToList(), enemies.Select(f => f.Copy()).ToList(), random: null,
                                 playerLeader, enemyLeader)
            {
                Outcome = Outcome,
            };
            if (Acting != null) copy.Acting = copy.all[all.IndexOf(Acting)];
            return copy;
        }

        /// <summary>
        /// One roll of the fight's luck: true with a chance of <paramref name="percent"/> % (0 never, 100 always).
        /// Only hit and crit roll (02 GDD 2), from SkillResolver. A copy has no generator and throws.
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
