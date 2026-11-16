using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace HeroHQ.Rules.Data
{
    /// <summary>
    /// All the game's content, loaded once and read-only (03 Architecture 4.1). Everything is found by id,
    /// so the code never names a hero, class or trait.
    ///
    /// <see cref="Load"/> takes the TEXT of the five files and reads no file itself, so tests and screens
    /// can hand it text from anywhere. It collects every problem it finds and stops with all of them in
    /// one <see cref="GameDataException"/> (03 Architecture 3.4); it never returns half-checked data.
    /// </summary>
    public class GameData
    {
        public IReadOnlyDictionary<string, HeroDef> Heroes { get; private set; }
        public IReadOnlyDictionary<string, ClassDef> Classes { get; private set; }
        public IReadOnlyDictionary<string, TraitDef> Traits { get; private set; }
        public IReadOnlyDictionary<string, TeamDef> Teams { get; private set; }
        /// <summary>The named tuning values of 02 GDD section 10 (meter_full, charge_basic, armor_k…).</summary>
        public IReadOnlyDictionary<string, double> Tuning { get; private set; }

        /// <summary>
        /// heroes, classes, traits and teams are each a list of records at the top of the file;
        /// tuning is one flat name → number record.
        /// </summary>
        public static GameData Load(string heroesJson, string classesJson, string traitsJson,
                                    string tuningJson, string teamsJson)
        {
            var heroes = JsonConvert.DeserializeObject<List<HeroDef>>(heroesJson);
            var classes = JsonConvert.DeserializeObject<List<ClassDef>>(classesJson);
            var traits = JsonConvert.DeserializeObject<List<TraitDef>>(traitsJson);
            var teams = JsonConvert.DeserializeObject<List<TeamDef>>(teamsJson);
            var tuning = JsonConvert.DeserializeObject<Dictionary<string, double>>(tuningJson);

            var data = new GameData
            {
                Heroes = heroes.ToDictionary(h => h.Id),
                Classes = classes.ToDictionary(c => c.Id),
                Traits = traits.ToDictionary(t => t.Id),
                Teams = teams.ToDictionary(t => t.Id),
                Tuning = tuning,
            };

            var problems = new List<string>();
            foreach (var hero in heroes)
            {
                if (!data.Classes.ContainsKey(hero.Class ?? ""))
                    problems.Add($"hero '{hero.Id}', field 'class': unknown class '{hero.Class}'");
            }

            if (problems.Count > 0) throw new GameDataException(problems);
            return data;
        }
    }

    /// <summary>The data files have problems. <see cref="Problems"/> lists every one, in file order.</summary>
    public class GameDataException : Exception
    {
        public IReadOnlyList<string> Problems { get; }

        public GameDataException(IReadOnlyList<string> problems)
            : base($"The game data has {problems.Count} problem(s):\n- " + string.Join("\n- ", problems))
        {
            Problems = problems;
        }
    }
}
