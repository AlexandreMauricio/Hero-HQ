using System;
using System.Linq;
using HeroHQ.Rules;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
    }
}
