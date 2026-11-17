using System;
using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules;
using HeroHQ.Rules.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace HeroHQ.Tests
{
    // Shared set-up for the tests that need a whole fight (FightTurnTests, EffectsTests): two teams of five test
    // heroes with chosen Speeds and a huge Health, so nobody is knocked out while a test plays turns.
    public static class TestFights
    {
        /// <summary>A valid test hero with a chosen Speed and a huge Health, then changed by <paramref name="edit"/> if given.</summary>
        static string Hero(string id, int speed, Action<string, JObject> edit)
        {
            var hero = JObject.Parse(TestData.Hero(id, speed: speed));
            hero["stats"]["health"] = 1000000;
            // No leader bonus unless a test adds one (P1-12), so every test's damage numbers stay round.
            hero["leader"]["bonuses"] = new JArray();
            edit?.Invoke(id, hero);
            return hero.ToString(Formatting.None);
        }

        /// <summary>
        /// A fight between two teams of five test heroes, given as (id, Speed) in slot order. Both teams stand
        /// 3–2 (unless a formation is given) with their first hero as leader. <paramref name="edit"/> (hero id,
        /// hero record) changes any hero's data before it loads: stats, skills (P1-09).
        /// </summary>
        public static Fight NewFight((string id, int speed)[] players, (string id, int speed)[] enemies, int seed = 1,
                                     string playerFormation = "3-2", string enemyFormation = "3-2",
                                     Action<string, JObject> edit = null)
        {
            var heroes = TestData.HeroesFile(players.Concat(enemies).Select(h => Hero(h.id, h.speed, edit)).ToArray());
            string Team(string id, string kind, string formation, (string id, int speed)[] five) =>
                "{ \"id\": \"" + id + "\", \"kind\": \"" + kind + "\", \"formation\": \"" + formation + "\", \"leader\": \"" + five[0].id +
                "\", \"slots\": [" + string.Join(",", five.Select(h => "\"" + h.id + "\"")) + "] }";
            var teams = "[" + Team("players", "target", playerFormation, players) + "," + Team("enemies", "enemy", enemyFormation, enemies) + "]";

            var data = TestData.Load(heroes: heroes, teams: teams);
            return Fight.Create(data, data.Teams["players"], data.Teams["enemies"], seed);
        }

        /// <summary>The acting hero's Basic, aimed at the first hero it may choose (Taunt, then the front row: P1-09's <c>Options</c>).</summary>
        public static Choice Basic(Fight fight) => new Choice(Button.Basic, fight.Options[0].Choosable[0]);

        /// <summary>The safety stop of 03 Architecture 4.10: a fight still going after this many turns is "no winner".</summary>
        public const int MaxTurns = 300;

        /// <summary>One played fight: the fight, every event in order (the fight-start ones first) and how many turns were started.</summary>
        public sealed class PlayedFight
        {
            public Fight Fight;
            public List<FightEvent> Events = new List<FightEvent>();
            /// <summary>Every hero's turn, a skipped (stunned) one included (4.10).</summary>
            public int Turns => Events.Count(e => e.Kind == FightEventKind.TurnStarted);
            public List<string> Text => Events.Select(e => e.ToString()).ToList();
        }

        /// <summary>
        /// The loop of 03 Architecture 4.3 with <see cref="EnemyAI"/> choosing for whichever side is acting, until the
        /// fight is decided or <paramref name="maxTurns"/> turns have started. The one copy of the loop that the whole-fight
        /// tests, the real-data smoke test and the fight report share. <paramref name="after"/>, if given, is called with
        /// the fight and each batch of events right after it happened: the start events, every <c>AdvanceToChoice</c> and
        /// every <c>Act</c> (P2a-03; the playback test of P2a-05 uses it too).
        /// </summary>
        public static PlayedFight Play(GameData data, TeamDef players, TeamDef enemies, int seed, int maxTurns = MaxTurns,
                                       Action<Fight, IReadOnlyList<FightEvent>> after = null)
        {
            var fight = Fight.Create(data, players, enemies, seed);
            var played = new PlayedFight { Fight = fight };
            played.Events.AddRange(fight.StartEvents);
            after?.Invoke(fight, fight.StartEvents);
            var turns = 0;

            while (fight.Outcome == Outcome.Going && turns < maxTurns)
            {
                var advanced = fight.AdvanceToChoice();
                played.Events.AddRange(advanced);
                after?.Invoke(fight, advanced);                 // a hero is now choosing (Acting is set), unless the fight just ended
                turns += advanced.Count(e => e.Kind == FightEventKind.TurnStarted);
                if (fight.Outcome != Outcome.Going) break;
                Assert.IsFalse(fight.Acting.KnockedOut, "a knocked-out hero never gets a turn");
                var acted = fight.Act(EnemyAI.Choose(fight));
                played.Events.AddRange(acted);
                after?.Invoke(fight, acted);                    // the action is done (Acting is null again)
            }
            return played;
        }
    }
}
