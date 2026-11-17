using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HeroHQ.Rules;
using HeroHQ.Rules.Data;

namespace HeroHQ.Tests
{
    // P1-19 (03 Architecture 4.10, 4.7): the plain-English fight log. A TEST HELPER, not a game feature: it lives in
    // the test assembly, so it never ships. It turns the fight's own event list into sentences, one or more per
    // event, so the owner can read one fight as a story. It reads events only (4.7: one list for the screen, the
    // log and the report), plus the heroes' data names; it never looks inside the rules.
    public static class FightLog
    {
        /// <summary>Where the log goes: <c>TestResults/fight-log.md</c> next to <c>Assets/</c> (git-ignored).</summary>
        public static string Path =>
            System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "TestResults", "fight-log.md"));

        // ---- the sentences -----------------------------------------------------------------------------------------

        /// <summary>
        /// Turns events into sentences while remembering the little the story needs: whose skill is playing (so a
        /// hit says who hit), and each hero's Health (counted from the events, starting at their max Health).
        /// Feed it every event in order; <see cref="Say"/> gives the sentence for one.
        /// </summary>
        public sealed class Teller
        {
            readonly Dictionary<Fighter, int> health = new Dictionary<Fighter, int>();
            Fighter actor;           // whose skill (or counter) is being played now
            string skill;            // its name

            /// <summary>"Kal" for your side, "enemy Kal" for the other, so the same hero on both sides reads clearly.</summary>
            public static string Name(Fighter f)
            {
                if (f == null) return "Someone";
                var name = f.Hero?.Name ?? f.ToString();
                return f.Side == Side.Player ? name : "enemy " + name;
            }

            /// <summary>A data name in words: "offense_up" is "Offense Up".</summary>
            public static string Words(string dataName)
            {
                if (string.IsNullOrEmpty(dataName)) return "an effect";
                var words = dataName.Split('_').Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1));
                return string.Join(" ", words);
            }

            static string SkillName(Fighter f, Button button)
            {
                var def = button == Button.Ultimate ? f?.Hero?.Ultimate : button == Button.Special ? f?.Hero?.Special : f?.Hero?.Basic;
                return def?.Name ?? button.ToString();
            }

            int Left(Fighter f) => health.TryGetValue(f, out var h) ? h : f.MaxHealth;

            static string Num(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

            /// <summary>The sentence for one event (every kind has one).</summary>
            public string Say(FightEvent e)
            {
                var who = Name(e.Who);
                var effect = Words(e.Effect);

                switch (e.Kind)
                {
                    case FightEventKind.TurnStarted:
                        actor = null; skill = null;
                        return $"{who} takes a turn.";

                    case FightEventKind.TurnSkipped:
                        return $"{who} is stunned and loses the turn.";

                    case FightEventKind.SkillUsed:
                    {
                        var button = e.Button ?? Button.Basic;
                        actor = e.Who; skill = SkillName(e.Who, button);
                        return button == Button.Ultimate
                            ? $"ULTIMATE! {who} unleashes {skill} (the cut-in plays here)."
                            : $"{who} uses {button.ToString().ToLowerInvariant()} skill {skill}.";
                    }

                    case FightEventKind.Missed:
                        return actor == null ? $"The attack misses {who}." : $"{skill} misses {who}.";

                    case FightEventKind.Damaged:
                    {
                        if (e.Who != null) health[e.Who] = Math.Max(0, Left(e.Who) - (e.Amount - e.Soaked));
                        var from = actor == null ? "" : $" from {Name(actor)}'s {skill}";
                        var text = $"{who} takes {Num(e.Amount)} damage{from}";
                        if (e.Crit) text += " (critical hit!)";
                        if (e.Soaked > 0) text += $", {Num(e.Soaked)} of it soaked by a Barrier";
                        return e.Who == null ? text + "." : $"{text}; {Num(Left(e.Who))} of {Num(e.Who.MaxHealth)} Health left.";
                    }

                    case FightEventKind.KnockedOut:
                        return $"{who} is knocked out!";

                    case FightEventKind.Healed:
                    {
                        if (e.Who != null) health[e.Who] = Math.Min(e.Who.MaxHealth, Left(e.Who) + e.Amount);
                        return e.Amount == 0
                            ? $"{who} is healed, but was already at full Health."
                            : $"{who} recovers {Num(e.Amount)} Health.";
                    }

                    case FightEventKind.EffectCleared:
                        return $"{effect} is cleared from {who}.";

                    case FightEventKind.MeterChanged:
                        return e.Amount < 0
                            ? $"{who}'s turn meter is drained by {Num(-e.Amount)}."
                            : $"{who}'s turn meter fills by {Num(e.Amount)}.";

                    case FightEventKind.ChargeChanged:
                        return e.Charge >= 100
                            ? $"{who}'s ultimate is fully charged (100 %)."
                            : $"{who}'s ultimate charge is now {e.Charge.ToString("0.#", CultureInfo.InvariantCulture)} %.";

                    case FightEventKind.EffectApplied:
                        return $"{who} gets {effect}.";

                    case FightEventKind.EffectBlocked:
                        return $"{who}'s Ward blocks {effect}.";

                    case FightEventKind.EffectCancelled:
                        return $"{effect} on {who} is cancelled by its opposite.";

                    case FightEventKind.EffectExpired:
                        return $"{effect} on {who} runs out.";

                    case FightEventKind.EffectUsedUp:
                        return $"{effect} on {who} is used up.";

                    case FightEventKind.EffectLost:
                        return $"{who} loses {effect}.";

                    case FightEventKind.CounterAttack:
                        actor = e.Who; skill = SkillName(e.Who, Button.Basic);
                        return $"COUNTER! {who} hits back with {skill}.";

                    case FightEventKind.FightWon:
                        return "All five enemies are knocked out. You win!";

                    case FightEventKind.FightLost:
                        return "All five of your heroes are knocked out. You lose.";

                    default:
                        throw new ArgumentOutOfRangeException(nameof(e), e.Kind, "FightLog has no sentence for this kind of event");
                }
            }
        }

        // ---- the whole log -----------------------------------------------------------------------------------------

        static string Names(GameData data, TeamDef team) => string.Join(", ", team.Slots.Select(id => data.Heroes[id].Name));

        /// <summary>
        /// One played fight as Markdown: who fought, then what happened before the first turn (fight-start
        /// leader and passive triggers), then each turn under its own heading, then the ending.
        /// </summary>
        public static string Markdown(GameData data, TeamDef team, TeamDef enemy, int seed, TestFights.PlayedFight played)
        {
            var teller = new Teller();
            var text = new StringBuilder();
            text.AppendLine($"# Hero HQ fight log: {team.Id} vs {enemy.Id}, seed {seed}");
            text.AppendLine();
            text.AppendLine("One fight told as a story, the enemy AI choosing for both sides (02 GDD 9). Every line is one thing that " +
                            "happened, in order. Health counts are worked out from the events. \"Enemy\" in front of a name means the other side.");
            text.AppendLine();
            text.AppendLine($"- **Your team:** {Names(data, team)} (leader {data.Heroes[team.Leader].Name}, {team.Formation}).");
            text.AppendLine($"- **Enemy crew:** {Names(data, enemy)} (leader {data.Heroes[enemy.Leader].Name}, {enemy.Formation}).");

            var turn = 0;
            var open = false;
            foreach (var e in played.Events)
            {
                if (e.Kind == FightEventKind.TurnStarted)
                {
                    turn++;
                    text.AppendLine();
                    text.AppendLine($"## Turn {turn}: {Teller.Name(e.Who)} ({(e.Who.Side == Side.Player ? "your team" : "enemy crew")})");
                    text.AppendLine();
                    open = true;
                }
                else if (!open)
                {
                    text.AppendLine();
                    text.AppendLine("## Before the first turn");
                    text.AppendLine();
                    open = true;
                }
                text.AppendLine("- " + teller.Say(e));
            }

            text.AppendLine();
            text.AppendLine("## The end");
            text.AppendLine();
            var ending = played.Fight.Outcome == Outcome.Won ? "You won" : played.Fight.Outcome == Outcome.Lost ? "You lost" : "No winner";
            text.AppendLine(played.Fight.Outcome == Outcome.Going
                ? $"No winner after {turn} turns (the {TestFights.MaxTurns}-turn safety stop)."
                : $"{ending} after {turn} turns.");
            return text.ToString();
        }
    }
}
