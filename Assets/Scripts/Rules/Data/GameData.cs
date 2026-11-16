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
    /// can hand it text from anywhere. It runs every check of 03 Architecture 3.4, collects every problem it
    /// finds and stops with all of them in one <see cref="GameDataException"/>; it never returns
    /// half-checked data.
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
            var problems = new List<string>();

            // 1. Read the five texts. A file that can't be read is a problem, not a crash, and nothing
            //    more can be checked without it.
            var heroes = Read<List<HeroDef>>("heroes", heroesJson, problems);
            var classes = Read<List<ClassDef>>("classes", classesJson, problems);
            var traits = Read<List<TraitDef>>("traits", traitsJson, problems);
            var teams = Read<List<TeamDef>>("teams", teamsJson, problems);
            var tuning = Read<Dictionary<string, double>>("tuning", tuningJson, problems);
            if (problems.Count > 0) throw new GameDataException(problems);

            // 2. Find everything by id (a repeated or missing id is a problem).
            var data = new GameData
            {
                Heroes = ById("heroes", heroes, h => h.Id, problems),
                Classes = ById("classes", classes, c => c.Id, problems),
                Traits = ById("traits", traits, t => t.Id, problems),
                Teams = ById("teams", teams, t => t.Id, problems),
                Tuning = tuning,
            };

            // 3. Check the content, file by file, in file order.
            var checks = new Checks(data, problems);
            checks.Tuning();
            foreach (var trait in traits.Where(t => t != null && t.Id != null)) checks.Trait(trait);
            foreach (var hero in heroes.Where(h => h != null && h.Id != null)) checks.Hero(hero);
            foreach (var team in teams.Where(t => t != null && t.Id != null)) checks.Team(team);

            if (problems.Count > 0) throw new GameDataException(problems);
            return data;
        }

        static T Read<T>(string file, string json, List<string> problems) where T : class
        {
            try
            {
                var value = JsonConvert.DeserializeObject<T>(json);
                if (value == null) problems.Add($"{file}: the file is empty");
                return value;
            }
            catch (JsonException e)
            {
                problems.Add($"{file}: the file can't be read ({e.Message})");
                return null;
            }
        }

        static IReadOnlyDictionary<string, T> ById<T>(string file, List<T> records, Func<T, string> id,
                                                     List<string> problems) where T : class
        {
            var found = new Dictionary<string, T>();
            foreach (var record in records)
            {
                if (record == null) { problems.Add($"{file}: a record is empty"); continue; }
                var key = id(record);
                if (string.IsNullOrWhiteSpace(key)) problems.Add($"{file}: a record has no id");
                else if (found.ContainsKey(key)) problems.Add($"{file}: id '{key}' is used more than once");
                else found.Add(key, record);
            }
            return found;
        }

        /// <summary>
        /// The checks of 03 Architecture 3.4. The lists below are the words the rules know (02 GDD 5, 7, 8, 10.2):
        /// the code lists the kinds of piece, target, effect and so on, never a hero, class or trait.
        /// </summary>
        sealed class Checks
        {
            static readonly string[] StatNames =
            {
                "health", "armor", "damage", "speed", "accuracy", "dodge_chance",
                "block_chance", "block_amount", "crit_chance", "crit_damage", "will", "grit",
            };
            // Stats a bonus may change: the eight that are on (02 GDD 5) except Crit Damage (no rule yet), plus healing.
            static readonly string[] BonusStats =
                { "health", "armor", "damage", "speed", "accuracy", "dodge_chance", "crit_chance", "healing" };
            // The ones that may depend on the moment a skill is used (03 Architecture 4.6).
            static readonly string[] MomentBonusStats = { "accuracy", "crit_chance", "damage", "healing" };
            static readonly string[] PieceKinds = { "damage", "heal", "effect", "clear", "meter" };
            static readonly string[] Targets =
            {
                "enemy", "enemy_and_neighbours", "all_enemies", "self",
                "ally", "all_allies", "most_hurt_ally", "next_enemy",
            };
            static readonly string[] Effects =
            {
                "offense_up", "offense_down", "defense_up", "defense_down", "taunt",
                "barrier", "ward", "counter", "stun", "momentum",
            };
            // 02 GDD 7.4: these two have no turn count.
            static readonly string[] EffectsWithoutTurns = { "stun", "momentum" };
            static readonly string[] Triggers = { "fight_start", "turn_start", "attack_hits", "ally_below_half" };
            static readonly string[] ConditionTests =
                { "allies_with", "per_ally_with", "target_has_effect", "target_has", "target_not_acted", "first_attack" };
            // Trait and class counts: fixed at fight start (02 GDD 8). The other four depend on the moment.
            static readonly string[] CountTests = { "allies_with", "per_ally_with" };
            static readonly string[] TraitKinds = { "side", "pantheon", "origin" };
            static readonly string[] Formations = { "3-2", "2-3", "4-1", "1-4" };   // 5-0 is not one (02 GDD 11 test 15)
            static readonly string[] TuningNames =
            {
                "meter_full", "turn_bar_length", "charge_basic", "charge_special", "charge_per_hurt", "max_dodge",
                "armor_k", "offense_change", "defense_change", "momentum_step", "momentum_max",
            };

            readonly GameData data;
            readonly List<string> problems;

            public Checks(GameData data, List<string> problems)
            {
                this.data = data;
                this.problems = problems;
            }

            static bool In(string[] words, string word) => word != null && Array.IndexOf(words, word) >= 0;

            /// <summary>"missing" for a word left out, otherwise "unknown <kind> '<word>'".</summary>
            static string Bad(string kind, string word) => word == null ? "missing" : $"unknown {kind} '{word}'";

            void Add(string where, string field, string message) => problems.Add($"{where}, field '{field}': {message}");

            // ---- tuning, traits ------------------------------------------------------------------------

            public void Tuning()
            {
                foreach (var name in TuningNames)
                    if (!data.Tuning.ContainsKey(name)) Add("tuning", name, "missing");
            }

            public void Trait(TraitDef trait)
            {
                var where = $"trait '{trait.Id}'";
                if (!In(TraitKinds, trait.Kind)) Add(where, "kind", Bad("kind", trait.Kind));
                if (trait.Kind == "pantheon")
                {
                    if (string.IsNullOrWhiteSpace(trait.CutinColors?.Background)) Add(where, "cutin_colors.background", "missing");
                    if (string.IsNullOrWhiteSpace(trait.CutinColors?.Glow)) Add(where, "cutin_colors.glow", "missing");
                }
            }

            // ---- heroes --------------------------------------------------------------------------------

            public void Hero(HeroDef hero)
            {
                var where = $"hero '{hero.Id}'";
                if (!data.Classes.ContainsKey(hero.Class ?? "")) Add(where, "class", Bad("class", hero.Class));
                HeroTraits(where, hero);
                HeroStats(where, hero);
                Skill(where, "basic", hero.Basic, false);
                Skill(where, "special", hero.Special, true);
                Skill(where, "ultimate", hero.Ultimate, false);
                BonusSkill(where, "leader", hero.Leader);
                BonusSkill(where, "passive", hero.Passive);
                if (string.IsNullOrWhiteSpace(hero.Cutin?.Shout)) Add(where, "cutin.shout", "missing");
            }

            void HeroTraits(string where, HeroDef hero)
            {
                if (hero.Traits == null) { Add(where, "traits", "missing"); return; }
                var allKnown = true;
                foreach (var trait in hero.Traits)
                {
                    if (data.Traits.ContainsKey(trait ?? "")) continue;
                    Add(where, "traits", Bad("trait", trait));
                    allKnown = false;
                }
                if (!allKnown) return;   // the kinds can't be counted until every trait is known

                foreach (var kind in TraitKinds)
                {
                    var count = hero.Traits.Count(t => data.Traits[t].Kind == kind);
                    if (count != 1) Add(where, "traits", $"needs exactly one {kind} trait, found {count}");
                }
            }

            void HeroStats(string where, HeroDef hero)
            {
                if (hero.Stats == null) { Add(where, "stats", "missing"); return; }
                foreach (var name in StatNames)
                    if (!hero.Stats.ContainsKey(name)) Add(where, "stats", $"'{name}' is missing");
                foreach (var name in hero.Stats.Keys)
                    if (!In(StatNames, name)) Add(where, "stats", $"unknown stat '{name}'");

                if (!hero.Stats.TryGetValue("speed", out var speed)) return;
                if (speed <= 0)
                    Add(where, "stats.speed", "must be more than 0");
                else if (data.Tuning.TryGetValue("meter_full", out var full) && speed >= full)
                    Add(where, "stats.speed", $"must be less than meter_full ({full})");
            }

            void Skill(string where, string field, SkillDef skill, bool isSpecial)
            {
                if (skill == null) { Add(where, field, "missing"); return; }
                if (string.IsNullOrWhiteSpace(skill.Name)) Add(where, field + ".name", "missing");
                if (isSpecial && skill.Cooldown == null) Add(where, field + ".cooldown", "missing");
                Pieces(where, field + ".pieces", skill.Pieces);
            }

            void Pieces(string where, string field, IReadOnlyList<PieceDef> pieces)
            {
                if (pieces == null || pieces.Count == 0) { Add(where, field, "needs at least one piece"); return; }
                for (var i = 0; i < pieces.Count; i++) Piece(where, $"{field}[{i}]", pieces[i]);
            }

            void Piece(string where, string field, PieceDef piece)
            {
                if (piece == null) { Add(where, field, "empty piece"); return; }
                if (!In(PieceKinds, piece.Do)) { Add(where, field + ".do", Bad("piece kind", piece.Do)); return; }
                if (!In(Targets, piece.Target)) Add(where, field + ".target", Bad("target", piece.Target));

                switch (piece.Do)
                {
                    case "damage":
                        if (piece.Power == null) Add(where, field + ".power", "missing");
                        break;
                    case "heal":
                    case "meter":
                        if (piece.Percent == null) Add(where, field + ".percent", "missing");
                        break;
                    case "effect":
                        if (!In(Effects, piece.Effect)) Add(where, field + ".effect", Bad("effect", piece.Effect));
                        else if (piece.Turns == null && !In(EffectsWithoutTurns, piece.Effect))
                            Add(where, field + ".turns", "missing");
                        break;
                    // clear: how many is optional, left out means all (02 GDD 7.1)
                }
                Condition(where, field + ".if", piece.If);
            }

            void Condition(string where, string field, ConditionDef condition)
            {
                if (condition == null) return;
                if (!In(ConditionTests, condition.Test)) { Add(where, field + ".test", Bad("condition", condition.Test)); return; }
                if (condition.Trait != null && !data.Traits.ContainsKey(condition.Trait))
                    Add(where, field + ".trait", Bad("trait", condition.Trait));
                if (condition.Class != null && !data.Classes.ContainsKey(condition.Class))
                    Add(where, field + ".class", Bad("class", condition.Class));
                if (condition.Effect != null && !In(Effects, condition.Effect))
                    Add(where, field + ".effect", Bad("effect", condition.Effect));

                if (condition.Test == "allies_with" && condition.Count == null) Add(where, field + ".count", "missing");
                var needsTraitOrClass = condition.Test == "allies_with" || condition.Test == "per_ally_with" || condition.Test == "target_has";
                if (needsTraitOrClass && condition.Trait == null && condition.Class == null)
                    Add(where, field, $"'{condition.Test}' needs a trait or a class");
                if (condition.Test == "target_has_effect" && condition.Effect == null) Add(where, field + ".effect", "missing");
            }

            void Who(string where, string field, WhoDef who)
            {
                if (who == null) return;
                if (who.Trait != null && !data.Traits.ContainsKey(who.Trait)) Add(where, field + ".trait", Bad("trait", who.Trait));
                if (who.Class != null && !data.Classes.ContainsKey(who.Class)) Add(where, field + ".class", Bad("class", who.Class));
            }

            // ---- leader skill and passive --------------------------------------------------------------

            void BonusSkill(string where, string field, BonusSkillDef skill)
            {
                if (skill == null) { Add(where, field, "missing"); return; }
                if (skill.Bonuses != null)
                    for (var i = 0; i < skill.Bonuses.Count; i++) Bonus(where, $"{field}.bonuses[{i}]", skill.Bonuses[i]);
                if (skill.Triggers != null)
                    for (var i = 0; i < skill.Triggers.Count; i++) Trigger(where, $"{field}.triggers[{i}]", skill.Triggers[i]);
            }

            void Bonus(string where, string field, BonusDef bonus)
            {
                if (bonus == null) { Add(where, field, "empty bonus"); return; }
                var statOk = In(BonusStats, bonus.Stat);
                if (!statOk)
                    Add(where, field + ".stat", bonus.Stat == null ? "missing" : $"no rule covers a bonus on '{bonus.Stat}'");
                if (bonus.Percent == null) Add(where, field + ".percent", "missing");
                Who(where, field + ".who", bonus.Who);
                Condition(where, field + ".if", bonus.If);

                if (!statOk || bonus.If == null || !In(ConditionTests, bonus.If.Test)) return;
                if (bonus.Stat == "speed" && !In(CountTests, bonus.If.Test))
                    // else the turn bar could change in the middle of a fight and stop being exact (02 GDD 3)
                    Add(where, field + ".if", "a Speed bonus may only have a trait or class count condition");
                else if (!In(CountTests, bonus.If.Test) && !In(MomentBonusStats, bonus.Stat))
                    Add(where, field + ".if", $"a bonus on '{bonus.Stat}' can't depend on the moment (only Accuracy, Crit Chance, Damage and healing can)");
            }

            void Trigger(string where, string field, TriggerDef trigger)
            {
                if (trigger == null) { Add(where, field, "empty trigger"); return; }
                if (!In(Triggers, trigger.When)) Add(where, field + ".when", Bad("trigger", trigger.When));
                Who(where, field + ".who", trigger.Who);
                Pieces(where, field + ".pieces", trigger.Pieces);
            }

            // ---- teams ---------------------------------------------------------------------------------

            public void Team(TeamDef team)
            {
                var where = $"team '{team.Id}'";
                if (!In(Formations, team.Formation)) Add(where, "formation", Bad("formation", team.Formation));

                if (team.Slots == null || team.Slots.Count != 5)
                    Add(where, "slots", $"needs five heroes, found {team.Slots?.Count ?? 0}");
                if (team.Slots != null)
                {
                    for (var i = 0; i < team.Slots.Count; i++)
                        if (!data.Heroes.ContainsKey(team.Slots[i] ?? "")) Add(where, $"slots[{i}]", Bad("hero", team.Slots[i]));
                    foreach (var repeated in team.Slots.Where(s => s != null).GroupBy(s => s).Where(g => g.Count() > 1))
                        Add(where, "slots", $"hero '{repeated.Key}' is in more than one slot");
                }

                if (team.Slots == null || !team.Slots.Contains(team.Leader))
                    Add(where, "leader", $"'{team.Leader}' is not one of the team's five");
            }
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
