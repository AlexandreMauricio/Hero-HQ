using System;
using System.Linq;
using HeroHQ.Rules.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HeroHQ.Tests
{
    // P1-02 (03 Architecture 4.12): the five data files' text for small TEST heroes with round numbers.
    // The tests own these numbers, so they keep passing while the real heroes are tuned (4.12).
    // Every default below is valid; a test breaks one thing on purpose and says which.
    //
    // Layout of the files (set by this task, 03 Architecture 3.1): heroes, classes, traits and teams are each a
    // list of records at the top of the file; tuning is one flat name -> number record.
    public static class TestData
    {
        public static readonly string[] HeroIds = { "rowan", "mira", "vex", "orla", "dax" };

        /// <summary>Classes the test heroes use. Ids are made up on purpose: the code never names a class.</summary>
        public const string Classes = @"[
  { ""id"": ""vanguard"", ""name"": ""Vanguard"" },
  { ""id"": ""caster"",   ""name"": ""Caster"" },
  { ""id"": ""striker"",  ""name"": ""Striker"" }
]";

        /// <summary>One side, one pantheon, one origin kind each, enough for any test hero (pantheons carry both colours).</summary>
        public const string Traits = @"[
  { ""id"": ""hero"",    ""name"": ""Hero"",    ""kind"": ""side"" },
  { ""id"": ""villain"", ""name"": ""Villain"", ""kind"": ""side"" },
  { ""id"": ""greek"",   ""name"": ""Greek"",   ""kind"": ""pantheon"", ""cutin_colors"": { ""background"": ""#112233"", ""glow"": ""#445566"" } },
  { ""id"": ""norse"",   ""name"": ""Norse"",   ""kind"": ""pantheon"", ""cutin_colors"": { ""background"": ""#778899"", ""glow"": ""#aabbcc"" } },
  { ""id"": ""human"",   ""name"": ""Human"",   ""kind"": ""origin"" },
  { ""id"": ""god"",     ""name"": ""God"",     ""kind"": ""origin"" }
]";

        /// <summary>The eleven names of 02 GDD 10.2 (the loader asks for all of them) with round test values.</summary>
        public static readonly string[] TuningNames =
        {
            "meter_full", "turn_bar_length", "charge_basic", "charge_special", "charge_per_hurt", "max_dodge",
            "armor_k", "offense_change", "defense_change", "momentum_step", "momentum_max",
        };

        public const string Tuning = @"{
  ""meter_full"": 1000,
  ""turn_bar_length"": 8,
  ""charge_basic"": 25,
  ""charge_special"": 10,
  ""charge_per_hurt"": 1,
  ""max_dodge"": 30,
  ""armor_k"": 1000,
  ""offense_change"": 25,
  ""defense_change"": 25,
  ""momentum_step"": 8,
  ""momentum_max"": 5
}";

        /// <summary>Two teams in the shape of 03 Architecture 3.2: an enemy crew and a target team.</summary>
        public const string Teams = @"[
  { ""id"": ""test_crew"",   ""kind"": ""enemy"",  ""formation"": ""2-3"", ""leader"": ""vex"",
    ""slots"": [""rowan"", ""mira"", ""vex"", ""orla"", ""dax""] },
  { ""id"": ""test_target"", ""kind"": ""target"", ""formation"": ""3-2"", ""leader"": ""rowan"",
    ""slots"": [""dax"", ""orla"", ""vex"", ""mira"", ""rowan""] }
]";

        /// <summary>One valid test hero as JSON. The name is the id with a capital, so a mixed-up field shows.</summary>
        public static string Hero(string id, string cls = "vanguard", string side = "hero",
                                  string pantheon = "greek", string origin = "human", int speed = 100)
        {
            var name = char.ToUpper(id[0]) + id.Substring(1);
            return @"{
  ""id"": """ + id + @""",
  ""name"": """ + name + @""",
  ""class"": """ + cls + @""",
  ""traits"": [""" + side + @""", """ + pantheon + @""", """ + origin + @"""],
  ""stats"": { ""health"": 1000, ""armor"": 100, ""damage"": 200, ""speed"": " + speed + @",
              ""accuracy"": 0, ""dodge_chance"": 0, ""block_chance"": 0, ""block_amount"": 0,
              ""crit_chance"": 10, ""crit_damage"": 150, ""will"": 0, ""grit"": 0 },
  ""basic"":    { ""name"": """ + name + @" Strike"", ""pieces"": [ { ""do"": ""damage"", ""target"": ""enemy"", ""power"": 100 } ] },
  ""special"":  { ""name"": """ + name + @" Special"", ""cooldown"": 2, ""pieces"": [ { ""do"": ""damage"", ""target"": ""enemy_and_neighbours"", ""power"": 150 } ] },
  ""ultimate"": { ""name"": """ + name + @" Ultimate"", ""pieces"": [ { ""do"": ""damage"", ""target"": ""all_enemies"", ""power"": 300 } ] },
  ""leader"":   { ""name"": """ + name + @" Crew"", ""bonuses"": [ { ""stat"": ""damage"", ""percent"": 10, ""who"": { ""trait"": ""greek"" } } ] },
  ""passive"":  { ""bonuses"": [], ""triggers"": [] },
  ""cutin"":    { ""shout"": """ + name.ToUpper() + @"!"" }
}";
        }

        /// <summary>heroes.json from hero records (a list at the top of the file).</summary>
        public static string HeroesFile(params string[] heroes) => "[" + string.Join(",", heroes) + "]";

        /// <summary>The five valid test heroes: the standard set every team in <see cref="Teams"/> is made of.</summary>
        public static string ValidHeroes() => HeroesFile(
            Hero("rowan", cls: "vanguard", side: "hero", pantheon: "greek", origin: "human", speed: 100),
            Hero("mira", cls: "caster", side: "hero", pantheon: "norse", origin: "god", speed: 110),
            Hero("vex", cls: "striker", side: "villain", pantheon: "greek", origin: "god", speed: 120),
            Hero("orla", cls: "vanguard", side: "villain", pantheon: "norse", origin: "human", speed: 90),
            Hero("dax", cls: "caster", side: "hero", pantheon: "greek", origin: "human", speed: 80));

        // --- P1-03: edit one record of a valid file, so a test breaks exactly one thing --------------------

        /// <summary>A file's text (a list of records with ids) with one record changed.</summary>
        public static string Edit(string fileText, string id, Action<JObject> change)
        {
            var list = JArray.Parse(fileText);
            change((JObject)list.First(r => (string)r["id"] == id));
            return list.ToString(Formatting.None);
        }

        /// <summary>The standard heroes with one hero changed.</summary>
        public static string HeroesWith(string id, Action<JObject> change) => Edit(ValidHeroes(), id, change);

        /// <summary>tuning.json without one value.</summary>
        public static string TuningWithout(string name)
        {
            var tuning = JObject.Parse(Tuning);
            tuning.Remove(name);
            return tuning.ToString(Formatting.None);
        }

        /// <summary>
        /// Loads a data set. Anything not passed is the valid default, so a test names only what it breaks.
        /// </summary>
        public static GameData Load(string heroes = null, string classes = null, string traits = null,
                                    string tuning = null, string teams = null)
            => GameData.Load(heroes ?? ValidHeroes(), classes ?? Classes, traits ?? Traits,
                             tuning ?? Tuning, teams ?? Teams);
    }
}
