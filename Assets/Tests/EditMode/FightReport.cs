using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HeroHQ.Rules;
using HeroHQ.Rules.Data;
using UnityEngine;

namespace HeroHQ.Tests
{
    // P1-18 (03 Architecture 4.10, 02 GDD 11 test 17): the fight report. A TEST HELPER, not a game feature: it lives in
    // the test assembly, so it never ships. It plays each target team against the enemy crew on the real data, the
    // AI choosing for both sides (TestFights.Play), counts everything from the fight's own event list (4.7: one list
    // for the screen, the log and the report), and writes a Markdown table the owner can read.
    // It reports; it does not judge balance (the numbers are for the designer).
    public static class FightReport
    {
        /// <summary>One played fight, boiled down to the numbers the report needs.</summary>
        public sealed class Row
        {
            public int Seed;
            /// <summary>From your side: Won, Lost, or Going (no winner inside the turn limit).</summary>
            public Outcome Outcome;
            /// <summary>Every hero's turn, a skipped (stunned) one included (4.10).</summary>
            public int Turns;
            /// <summary>Ultimates fired, by (side, slot). A hero who fired none is not in the list.</summary>
            public Dictionary<(Side, int), int> Ultimates;
            /// <summary>Double turns, by (side, slot). A hero with none is not in the list.</summary>
            public Dictionary<(Side, int), int> DoubleTurns;
        }

        /// <summary>One target team against the enemy crew, over several seeds.</summary>
        public sealed class TeamResult
        {
            public GameData Data;
            public TeamDef Team;
            public TeamDef Enemy;
            public List<Row> Rows = new List<Row>();
        }

        /// <summary>Where the report goes: <c>TestResults/fight-report.md</c> next to <c>Assets/</c> (git-ignored).</summary>
        public static string Path =>
            System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "TestResults", "fight-report.md"));

        // ---- counting, from the event list only -------------------------------------------------------------------

        /// <summary>Ultimates fired: a <c>SkillUsed</c> whose button is the Ultimate (a counter has no <c>SkillUsed</c>, so it never counts).</summary>
        public static Dictionary<(Side, int), int> Ultimates(IEnumerable<FightEvent> events)
        {
            var counts = new Dictionary<(Side, int), int>();
            foreach (var e in events)
                if (e.Kind == FightEventKind.SkillUsed && e.Button == Button.Ultimate)
                    Add(counts, (e.Who.Side, e.Who.Slot));
            return counts;
        }

        /// <summary>
        /// Double turns (OWNER, 02 GDD 10.6 q. 8): a hero's turn with <b>no opponent's turn</b> since that hero's last turn.
        /// Allies' turns in between are fine; an opponent's stunned, skipped turn counts as a turn in between, because it
        /// is a portrait on the bar. Every <c>TurnStarted</c> is a turn, skipped ones included.
        /// </summary>
        public static Dictionary<(Side, int), int> DoubleTurns(IEnumerable<FightEvent> events)
        {
            var counts = new Dictionary<(Side, int), int>();
            var lastTurnOfHero = new Dictionary<(Side, int), int>();
            var lastTurnOfSide = new Dictionary<Side, int> { { Side.Player, 0 }, { Side.Enemy, 0 } };
            var index = 0;                                   // turns are numbered from 1, so 0 means "none yet"

            foreach (var e in events)
            {
                if (e.Kind != FightEventKind.TurnStarted) continue;
                index++;
                var hero = (e.Who.Side, e.Who.Slot);
                var opponent = e.Who.Side == Side.Player ? Side.Enemy : Side.Player;
                if (lastTurnOfHero.TryGetValue(hero, out var previous) && lastTurnOfSide[opponent] < previous)
                    Add(counts, hero);
                lastTurnOfHero[hero] = index;
                lastTurnOfSide[e.Who.Side] = index;
            }
            return counts;
        }

        static void Add(Dictionary<(Side, int), int> counts, (Side, int) key) =>
            counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;

        // ---- running ----------------------------------------------------------------------------------------------

        /// <summary>One target team against the crew, on seeds 1 to <paramref name="seeds"/>.</summary>
        public static TeamResult Run(GameData data, string team, string enemy, int seeds)
        {
            var result = new TeamResult { Data = data, Team = data.Teams[team], Enemy = data.Teams[enemy] };
            for (var seed = 1; seed <= seeds; seed++)
            {
                var played = TestFights.Play(data, result.Team, result.Enemy, seed);
                result.Rows.Add(new Row
                {
                    Seed = seed,
                    Outcome = played.Fight.Outcome,
                    Turns = played.Turns,
                    Ultimates = Ultimates(played.Events),
                    DoubleTurns = DoubleTurns(played.Events),
                });
            }
            return result;
        }

        // ---- writing ----------------------------------------------------------------------------------------------

        static string One(double x) => x.ToString("0.0", CultureInfo.InvariantCulture);
        static string Two(double x) => x.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>The report as Markdown: one section per team (endings, turns, then a row per hero).</summary>
        public static string Markdown(IEnumerable<TeamResult> results)
        {
            var list = results.ToList();
            var text = new StringBuilder();
            text.AppendLine("# Hero HQ fight report");
            text.AppendLine();
            text.AppendLine($"Each target team fights the enemy crew on seeds 1–{list[0].Rows.Count}, the enemy AI choosing for both sides " +
                            $"(02 GDD 9). A fight still going after {TestFights.MaxTurns} turns is \"no winner\". " +
                            "**Turns** are every hero's turn, skipped (stunned) ones included. A **double turn** is a hero's turn with no " +
                            "opponent's turn since their last one (02 GDD 10.6 q. 8). **Ultimates** aim: 1–2 per hero per fight (pillar 3). " +
                            "It reports; it does not judge balance.");

            foreach (var r in list)
            {
                var fights = r.Rows.Count;
                var wins = r.Rows.Count(x => x.Outcome == Outcome.Won);
                var losses = r.Rows.Count(x => x.Outcome == Outcome.Lost);
                var none = r.Rows.Count(x => x.Outcome == Outcome.Going);
                var turns = r.Rows.Select(x => x.Turns).ToList();
                var doubles = r.Rows.Average(x => x.DoubleTurns.Values.Sum());

                text.AppendLine();
                text.AppendLine($"## {r.Team.Id} vs {r.Enemy.Id} ({fights} fights)");
                text.AppendLine();
                text.AppendLine($"Your team: {Names(r.Data, r.Team)} (leader {r.Data.Heroes[r.Team.Leader].Name}, {r.Team.Formation}). " +
                                $"Enemy crew: {Names(r.Data, r.Enemy)} (leader {r.Data.Heroes[r.Enemy.Leader].Name}, {r.Enemy.Formation}).");
                text.AppendLine();
                text.AppendLine("| Wins | Losses | No winner | Turns per fight (min–max) | Double turns per fight (both sides) |");
                text.AppendLine("|---|---|---|---|---|");
                text.AppendLine($"| {wins} | {losses} | {none} | {One(turns.Average())} ({turns.Min()}–{turns.Max()}) | {One(doubles)} |");
                text.AppendLine();
                text.AppendLine("| Side | Slot | Hero | Ultimates per fight | Fights with no ultimate | Double turns per fight |");
                text.AppendLine("|---|---|---|---|---|---|");
                foreach (var (side, team) in new[] { (Side.Player, r.Team), (Side.Enemy, r.Enemy) })
                    for (var slot = 0; slot < team.Slots.Count; slot++)
                    {
                        var key = (side, slot);
                        int Of(Dictionary<(Side, int), int> d) => d.TryGetValue(key, out var n) ? n : 0;
                        text.AppendLine($"| {(side == Side.Player ? "Your team" : "Crew")} | {slot} | {r.Data.Heroes[team.Slots[slot]].Name} | " +
                                        $"{One(r.Rows.Average(x => Of(x.Ultimates)))} | {r.Rows.Count(x => Of(x.Ultimates) == 0)} | " +
                                        $"{Two(r.Rows.Average(x => Of(x.DoubleTurns)))} |");
                    }
            }
            return text.ToString();
        }

        static string Names(GameData data, TeamDef team) => string.Join(", ", team.Slots.Select(id => data.Heroes[id].Name));
    }
}
