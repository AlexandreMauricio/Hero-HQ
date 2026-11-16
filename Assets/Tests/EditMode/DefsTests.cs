using System.IO;
using System.Linq;
using HeroHQ.Rules.Data;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace HeroHQ.Tests
{
    // P1-01 (03 Architecture 4.12): the data classes read the JSON shape of 03 Architecture 3.2,
    // and the Rules assembly uses no Unity engine code (4.1).
    public class DefsTests
    {
        // 3.2's example hero, with a different non-zero number in every place, so a field that is
        // mapped wrongly (or not at all) shows up instead of hiding behind a 0.
        const string ExampleHero = @"{
  ""id"": ""kal"",
  ""name"": ""Kal & Talos"",
  ""class"": ""caster"",
  ""traits"": [""outlaw"", ""greek"", ""tech""],
  ""stats"": { ""health"": 3001, ""armor"": 152, ""damage"": 463, ""speed"": 94,
              ""accuracy"": 5, ""dodge_chance"": 6, ""block_chance"": 7, ""block_amount"": 8,
              ""crit_chance"": 12.5, ""crit_damage"": 155, ""will"": 9, ""grit"": 10 },
  ""basic"":    { ""name"": ""Forge Fist"", ""pieces"": [ { ""do"": ""damage"", ""target"": ""enemy"", ""power"": 101 } ] },
  ""special"":  { ""name"": ""Overheat"", ""cooldown"": 1, ""pieces"": [
                  { ""do"": ""damage"", ""target"": ""enemy_and_neighbours"", ""power"": 71, ""hits"": 2,
                    ""cant_miss"": true, ""always_crits"": true, ""reaches_back_row"": true },
                  { ""do"": ""effect"", ""target"": ""self"", ""effect"": ""offense_up"", ""turns"": 3 } ] },
  ""ultimate"": { ""name"": ""TALOS, WAKE UP!"", ""pieces"": [
                  { ""do"": ""damage"", ""target"": ""all_enemies"", ""power"": 203,
                    ""if"": { ""test"": ""target_has_effect"", ""effect"": ""defense_down"" } },
                  { ""do"": ""heal"", ""target"": ""ally"", ""percent"": 21,
                    ""if"": { ""test"": ""target_has"", ""trait"": ""outlaw"" } },
                  { ""do"": ""clear"", ""target"": ""most_hurt_ally"", ""count"": 2 },
                  { ""do"": ""meter"", ""target"": ""next_enemy"", ""percent"": -50 } ] },
  ""leader"":   { ""name"": ""Forge Crew"",
                 ""bonuses"": [ { ""stat"": ""damage"", ""percent"": 20, ""who"": { ""trait"": ""tech"" } },
                              { ""stat"": ""armor"", ""percent"": 15, ""who"": { ""class"": ""vanguard"" } } ],
                 ""triggers"": [ { ""when"": ""fight_start"", ""who"": { ""trait"": ""greek"" }, ""pieces"": [
                                  { ""do"": ""meter"", ""target"": ""self"", ""percent"": 50 } ] } ] },
  ""passive"":  { ""bonuses"": [ { ""stat"": ""dodge_chance"", ""percent"": 5,
                                 ""if"": { ""test"": ""per_ally_with"", ""trait"": ""greek"" } },
                               { ""stat"": ""crit_chance"", ""percent"": 100,
                                 ""if"": { ""test"": ""first_attack"" } },
                               { ""stat"": ""speed"", ""percent"": 10,
                                 ""if"": { ""test"": ""allies_with"", ""count"": 2, ""class"": ""caster"" } } ],
                 ""triggers"": [ { ""when"": ""ally_below_half"", ""pieces"": [
                                  { ""do"": ""effect"", ""target"": ""self"", ""effect"": ""taunt"", ""turns"": 1 } ] } ] },
  ""cutin"":    { ""shout"": ""TALOS, WAKE UP!"", ""drawing"": ""kal"", ""behind"": ""kal_behind"" }
}";

        static HeroDef ReadExample() => JsonConvert.DeserializeObject<HeroDef>(ExampleHero);

        [Test]
        public void Hero_IdentityStatsAndCutin_AreRead()
        {
            var hero = ReadExample();
            Assert.AreEqual("kal", hero.Id);
            Assert.AreEqual("Kal & Talos", hero.Name);
            Assert.AreEqual("caster", hero.Class);
            CollectionAssert.AreEqual(new[] { "outlaw", "greek", "tech" }, hero.Traits);

            Assert.AreEqual(12, hero.Stats.Count);
            Assert.AreEqual(3001, hero.Stats["health"]);
            Assert.AreEqual(152, hero.Stats["armor"]);
            Assert.AreEqual(463, hero.Stats["damage"]);
            Assert.AreEqual(94, hero.Stats["speed"]);
            Assert.AreEqual(5, hero.Stats["accuracy"]);
            Assert.AreEqual(6, hero.Stats["dodge_chance"]);
            Assert.AreEqual(7, hero.Stats["block_chance"]);
            Assert.AreEqual(8, hero.Stats["block_amount"]);
            Assert.AreEqual(12.5, hero.Stats["crit_chance"], "a stat may have a decimal part");
            Assert.AreEqual(155, hero.Stats["crit_damage"]);
            Assert.AreEqual(9, hero.Stats["will"]);
            Assert.AreEqual(10, hero.Stats["grit"]);

            Assert.AreEqual("TALOS, WAKE UP!", hero.Cutin.Shout);
            Assert.AreEqual("kal", hero.Cutin.Drawing);
            Assert.AreEqual("kal_behind", hero.Cutin.Behind);
        }

        [Test]
        public void Hero_ThreeSkills_AreRead()
        {
            var hero = ReadExample();

            Assert.AreEqual("Forge Fist", hero.Basic.Name);
            Assert.IsNull(hero.Basic.Cooldown, "a Basic has no cooldown");
            Assert.AreEqual(1, hero.Basic.Pieces.Count);
            var basicPiece = hero.Basic.Pieces[0];
            Assert.AreEqual("damage", basicPiece.Do);
            Assert.AreEqual("enemy", basicPiece.Target);
            Assert.AreEqual(101, basicPiece.Power);
            Assert.IsNull(basicPiece.Hits, "a setting left out reads as null, not 0");
            Assert.IsFalse(basicPiece.CantMiss);
            Assert.IsFalse(basicPiece.AlwaysCrits);
            Assert.IsFalse(basicPiece.ReachesBackRow);
            Assert.IsNull(basicPiece.If);

            Assert.AreEqual("Overheat", hero.Special.Name);
            Assert.AreEqual(1, hero.Special.Cooldown);
            Assert.AreEqual(2, hero.Special.Pieces.Count);
            var strike = hero.Special.Pieces[0];
            Assert.AreEqual("enemy_and_neighbours", strike.Target);
            Assert.AreEqual(71, strike.Power);
            Assert.AreEqual(2, strike.Hits);
            Assert.IsTrue(strike.CantMiss);
            Assert.IsTrue(strike.AlwaysCrits);
            Assert.IsTrue(strike.ReachesBackRow);
            var buff = hero.Special.Pieces[1];
            Assert.AreEqual("effect", buff.Do);
            Assert.AreEqual("self", buff.Target);
            Assert.AreEqual("offense_up", buff.Effect);
            Assert.AreEqual(3, buff.Turns);
            Assert.IsNull(buff.Power);

            Assert.AreEqual("TALOS, WAKE UP!", hero.Ultimate.Name);
            Assert.AreEqual(4, hero.Ultimate.Pieces.Count);
        }

        [Test]
        public void Pieces_HealClearMeter_AndTheirConditions_AreRead()
        {
            var pieces = ReadExample().Ultimate.Pieces;

            Assert.AreEqual("all_enemies", pieces[0].Target);
            Assert.AreEqual(203, pieces[0].Power);
            Assert.AreEqual("target_has_effect", pieces[0].If.Test);
            Assert.AreEqual("defense_down", pieces[0].If.Effect);

            Assert.AreEqual("heal", pieces[1].Do);
            Assert.AreEqual("ally", pieces[1].Target);
            Assert.AreEqual(21, pieces[1].Percent);
            Assert.AreEqual("target_has", pieces[1].If.Test);
            Assert.AreEqual("outlaw", pieces[1].If.Trait);
            Assert.IsNull(pieces[1].If.Class);

            Assert.AreEqual("clear", pieces[2].Do);
            Assert.AreEqual("most_hurt_ally", pieces[2].Target);
            Assert.AreEqual(2, pieces[2].Count);

            Assert.AreEqual("meter", pieces[3].Do);
            Assert.AreEqual("next_enemy", pieces[3].Target);
            Assert.AreEqual(-50, pieces[3].Percent, "a drain is a negative percent");
        }

        [Test]
        public void Leader_BonusesAndTriggers_AreRead()
        {
            var leader = ReadExample().Leader;
            Assert.AreEqual("Forge Crew", leader.Name);

            Assert.AreEqual(2, leader.Bonuses.Count);
            Assert.AreEqual("damage", leader.Bonuses[0].Stat);
            Assert.AreEqual(20, leader.Bonuses[0].Percent);
            Assert.AreEqual("tech", leader.Bonuses[0].Who.Trait);
            Assert.IsNull(leader.Bonuses[0].Who.Class);
            Assert.IsNull(leader.Bonuses[0].If);
            Assert.AreEqual("armor", leader.Bonuses[1].Stat);
            Assert.AreEqual("vanguard", leader.Bonuses[1].Who.Class);
            Assert.IsNull(leader.Bonuses[1].Who.Trait);

            Assert.AreEqual(1, leader.Triggers.Count);
            Assert.AreEqual("fight_start", leader.Triggers[0].When);
            Assert.AreEqual("greek", leader.Triggers[0].Who.Trait);
            Assert.AreEqual(1, leader.Triggers[0].Pieces.Count);
            Assert.AreEqual("meter", leader.Triggers[0].Pieces[0].Do);
            Assert.AreEqual("self", leader.Triggers[0].Pieces[0].Target);
            Assert.AreEqual(50, leader.Triggers[0].Pieces[0].Percent);
        }

        [Test]
        public void Passive_NameIsOptional_AndTheThreeCountConditionsAreRead()
        {
            var passive = ReadExample().Passive;
            Assert.IsNull(passive.Name, "name is optional on a passive");

            Assert.AreEqual(3, passive.Bonuses.Count);
            Assert.IsNull(passive.Bonuses[0].Who, "a passive's bonus needs no who");
            Assert.AreEqual("per_ally_with", passive.Bonuses[0].If.Test);
            Assert.AreEqual("greek", passive.Bonuses[0].If.Trait);
            Assert.AreEqual("first_attack", passive.Bonuses[1].If.Test);
            Assert.AreEqual("allies_with", passive.Bonuses[2].If.Test);
            Assert.AreEqual(2, passive.Bonuses[2].If.Count);
            Assert.AreEqual("caster", passive.Bonuses[2].If.Class);

            Assert.AreEqual(1, passive.Triggers.Count);
            Assert.AreEqual("ally_below_half", passive.Triggers[0].When);
            Assert.IsNull(passive.Triggers[0].Who, "a passive's trigger belongs to the hero alone");
            Assert.AreEqual("taunt", passive.Triggers[0].Pieces[0].Effect);
            Assert.AreEqual(1, passive.Triggers[0].Pieces[0].Turns);
        }

        [Test]
        public void CutinBehind_LeftOut_ReadsAsNull()
        {
            var hero = JsonConvert.DeserializeObject<HeroDef>(
                @"{ ""id"": ""x"", ""cutin"": { ""shout"": ""HEY!"" } }");
            Assert.AreEqual("HEY!", hero.Cutin.Shout);
            Assert.IsNull(hero.Cutin.Drawing);
            Assert.IsNull(hero.Cutin.Behind);
        }

        [Test]
        public void MissingStat_IsMissing_NotZero()
        {
            // The reason for Newtonsoft (03 Architecture 3.1): "Armor left out by mistake" must be
            // told apart from "Armor 0". A missing stat is simply not in the list.
            var hero = JsonConvert.DeserializeObject<HeroDef>(@"{ ""id"": ""x"", ""stats"": { ""armor"": 0 } }");
            Assert.IsTrue(hero.Stats.ContainsKey("armor"));
            Assert.AreEqual(0, hero.Stats["armor"]);
            Assert.IsFalse(hero.Stats.ContainsKey("health"));
        }

        [Test]
        public void TeamTraitAndClass_AreRead()
        {
            var team = JsonConvert.DeserializeObject<TeamDef>(
                @"{ ""id"": ""enemy_crew"", ""kind"": ""enemy"", ""formation"": ""2-3"", ""leader"": ""thordis"",
                    ""slots"": [""kal"", ""hori"", ""strix"", ""halvard"", ""thordis""] }");
            Assert.AreEqual("enemy_crew", team.Id);
            Assert.AreEqual("enemy", team.Kind);
            Assert.AreEqual("2-3", team.Formation);
            Assert.AreEqual("thordis", team.Leader);
            CollectionAssert.AreEqual(new[] { "kal", "hori", "strix", "halvard", "thordis" }, team.Slots);

            var trait = JsonConvert.DeserializeObject<TraitDef>(
                @"{ ""id"": ""greek"", ""name"": ""Greek"", ""kind"": ""pantheon"",
                    ""cutin_colors"": { ""background"": ""#1b6fd1"", ""glow"": ""#ffd23f"" } }");
            Assert.AreEqual("greek", trait.Id);
            Assert.AreEqual("Greek", trait.Name);
            Assert.AreEqual("pantheon", trait.Kind);
            Assert.AreEqual("#1b6fd1", trait.CutinColors.Background);
            Assert.AreEqual("#ffd23f", trait.CutinColors.Glow);

            var side = JsonConvert.DeserializeObject<TraitDef>(@"{ ""id"": ""hero"", ""name"": ""Hero"", ""kind"": ""side"" }");
            Assert.IsNull(side.CutinColors, "only pantheon traits carry colours");

            var cls = JsonConvert.DeserializeObject<ClassDef>(@"{ ""id"": ""caster"", ""name"": ""Caster"" }");
            Assert.AreEqual("caster", cls.Id);
            Assert.AreEqual("Caster", cls.Name);
        }

        [Test]
        public void RulesAssembly_ReferencesNoUnityEngine_ButUsesNewtonsoft()
        {
            // 03 Architecture 4.1: the compiler itself must stop anyone putting a scene object in the rules.
            var rules = CompilationPipeline.GetAssemblies(AssembliesType.Editor).Single(a => a.name == "HeroHQ.Rules");
            var referenced = rules.compiledAssemblyReferences.Select(Path.GetFileName).ToList();

            // Unity adds three iOS editor-extension DLLs (UnityEditor.iOS.Extensions.*, UnityEditor.Apple.*) to every
            // editor-compiled assembly, whatever the setting; they are not engine code and are left out of this check.
            var unity = referenced.Where(n => n.StartsWith("UnityEngine") || n == "UnityEditor.dll"
                                              || (n.StartsWith("UnityEditor.") && n.EndsWith("Module.dll"))).ToList();
            Assert.IsEmpty(unity, "The Rules assembly must not reference: " + string.Join(", ", unity));
            Assert.IsEmpty(rules.assemblyReferences, "The Rules assembly must not reference other Unity assemblies.");
            CollectionAssert.Contains(referenced, "Newtonsoft.Json.dll");
        }
    }
}
