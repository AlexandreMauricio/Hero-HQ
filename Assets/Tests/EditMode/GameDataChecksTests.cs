using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules.Data;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace HeroHQ.Tests
{
    // P1-03 (03 Architecture 4.12, 3.4): one test per rejection the loader makes, so a typo in the data
    // never becomes a strange fight. Each test breaks ONE thing in an otherwise valid set (TestData) and
    // expects exactly one problem that names where it is. "Loads" tests prove the neighbouring valid case
    // is not rejected by mistake.
    public class GameDataChecksTests
    {
        // ---- helpers ----------------------------------------------------------------------------------

        static IReadOnlyList<string> Problems(string heroes = null, string classes = null, string traits = null,
                                              string tuning = null, string teams = null, string formations = null)
        {
            var error = Assert.Throws<GameDataException>(
                () => TestData.Load(heroes, classes, traits, tuning, teams, formations));
            return error.Problems;
        }

        /// <summary>Exactly one problem, and its text holds every word given (the hero, the field, the bad value).</summary>
        static void ExpectOne(IReadOnlyList<string> problems, params string[] words)
        {
            Assert.AreEqual(1, problems.Count, string.Join(" | ", problems));
            foreach (var word in words) StringAssert.Contains(word, problems[0]);
        }

        static string Rowan(System.Action<JObject> change) => TestData.HeroesWith("rowan", change);

        /// <summary>rowan's Basic becomes this one piece.</summary>
        static string RowanBasicPiece(string pieceJson) =>
            Rowan(h => h["basic"]["pieces"] = JArray.Parse("[" + pieceJson + "]"));

        /// <summary>rowan's leader skill gets this one bonus.</summary>
        static string RowanBonus(string bonusJson) =>
            Rowan(h => h["leader"]["bonuses"] = JArray.Parse("[" + bonusJson + "]"));

        static string Damage(string extra = "") =>
            "{ \"do\": \"damage\", \"target\": \"enemy\", \"power\": 100" + extra + " }";

        // ---- stats ------------------------------------------------------------------------------------

        [Test]
        public void MissingStat_IsRejected_NamingHeroAndStat()
        {
            var heroes = Rowan(h => ((JObject)h["stats"]).Remove("armor"));
            ExpectOne(Problems(heroes: heroes), "rowan", "stats", "armor", "missing");
        }

        [Test]
        public void UnknownStatName_IsRejected_NamingHeroAndStat()
        {
            var heroes = Rowan(h => h["stats"]["luck"] = 5);
            ExpectOne(Problems(heroes: heroes), "rowan", "stats", "luck", "unknown");
        }

        // ---- speed ------------------------------------------------------------------------------------

        [Test]
        public void SpeedOfZero_IsRejected()
        {
            var heroes = Rowan(h => h["stats"]["speed"] = 0);
            ExpectOne(Problems(heroes: heroes), "rowan", "speed");
        }

        [Test]
        public void SpeedOfMeterFullOrMore_IsRejected([Values(1000, 1500)] int speed)
        {
            var heroes = Rowan(h => h["stats"]["speed"] = speed);
            ExpectOne(Problems(heroes: heroes), "rowan", "speed", "meter_full");
        }

        [Test]
        public void SpeedOfOneAndJustBelowMeterFull_Load([Values(1, 999)] int speed)
        {
            var heroes = Rowan(h => h["stats"]["speed"] = speed);
            Assert.DoesNotThrow(() => TestData.Load(heroes: heroes));
        }

        // ---- unknown names in skills ------------------------------------------------------------------

        [Test]
        public void UnknownClass_IsRejected()
        {
            var heroes = Rowan(h => h["class"] = "wizard");
            ExpectOne(Problems(heroes: heroes), "rowan", "class", "wizard");
        }

        [Test]
        public void UnknownTrait_IsRejected_NamingHeroAndTrait()
        {
            var heroes = Rowan(h => h["traits"][1] = "roman");
            ExpectOne(Problems(heroes: heroes), "rowan", "traits", "roman");
        }

        [Test]
        public void UnknownPieceKind_IsRejected()
        {
            ExpectOne(Problems(heroes: RowanBasicPiece("{ \"do\": \"smash\", \"target\": \"enemy\", \"power\": 100 }")),
                      "rowan", "basic.pieces[0]", "smash");
        }

        [Test]
        public void UnknownTarget_IsRejected()
        {
            ExpectOne(Problems(heroes: RowanBasicPiece("{ \"do\": \"damage\", \"target\": \"everyone\", \"power\": 100 }")),
                      "rowan", "basic.pieces[0]", "everyone");
        }

        [Test]
        public void EveryTargetOfTheGdd_Loads([Values("enemy", "enemy_and_neighbours", "all_enemies", "self",
            "ally", "all_allies", "most_hurt_ally", "next_enemy")] string target)
        {
            var heroes = RowanBasicPiece("{ \"do\": \"damage\", \"target\": \"" + target + "\", \"power\": 100 }");
            Assert.DoesNotThrow(() => TestData.Load(heroes: heroes));
        }

        [Test]
        public void UnknownEffect_IsRejected()
        {
            ExpectOne(Problems(heroes: RowanBasicPiece("{ \"do\": \"effect\", \"target\": \"self\", \"effect\": \"rage\", \"turns\": 1 }")),
                      "rowan", "basic.pieces[0]", "rage");
        }

        [Test]
        public void UnknownTrigger_IsRejected()
        {
            var heroes = Rowan(h => h["passive"]["triggers"] =
                JArray.Parse("[ { \"when\": \"sunrise\", \"pieces\": [" + Damage() + "] } ]"));
            ExpectOne(Problems(heroes: heroes), "rowan", "passive.triggers[0]", "sunrise");
        }

        [Test]
        public void EveryTriggerOfTheGdd_Loads([Values("fight_start", "turn_start", "attack_hits", "ally_below_half")] string when)
        {
            var heroes = Rowan(h => h["passive"]["triggers"] =
                JArray.Parse("[ { \"when\": \"" + when + "\", \"pieces\": [" + Damage() + "] } ]"));
            Assert.DoesNotThrow(() => TestData.Load(heroes: heroes));
        }

        [Test]
        public void UnknownCondition_IsRejected()
        {
            ExpectOne(Problems(heroes: RowanBasicPiece(Damage(", \"if\": { \"test\": \"moon_is_full\" }"))),
                      "rowan", "basic.pieces[0].if", "moon_is_full");
        }

        [Test]
        public void UnknownTraitOrClassInACondition_IsRejected()
        {
            ExpectOne(Problems(heroes: RowanBasicPiece(Damage(", \"if\": { \"test\": \"target_has\", \"trait\": \"roman\" }"))),
                      "rowan", "basic.pieces[0].if", "roman");
            ExpectOne(Problems(heroes: RowanBasicPiece(Damage(", \"if\": { \"test\": \"target_has\", \"class\": \"wizard\" }"))),
                      "rowan", "basic.pieces[0].if", "wizard");
        }

        [Test]
        public void UnknownTraitInAWhoFilter_IsRejected()
        {
            var heroes = RowanBonus("{ \"stat\": \"damage\", \"percent\": 10, \"who\": { \"trait\": \"roman\" } }");
            ExpectOne(Problems(heroes: heroes), "rowan", "leader.bonuses[0].who", "roman");
        }

        [Test]
        public void ConditionMissingWhatItNeeds_IsRejected()
        {
            ExpectOne(Problems(heroes: RowanBasicPiece(Damage(", \"if\": { \"test\": \"allies_with\", \"trait\": \"greek\" }"))),
                      "rowan", "if.count", "missing");
            ExpectOne(Problems(heroes: RowanBasicPiece(Damage(", \"if\": { \"test\": \"target_has\" }"))),
                      "rowan", "basic.pieces[0].if", "trait or a class");
            ExpectOne(Problems(heroes: RowanBasicPiece(Damage(", \"if\": { \"test\": \"target_has_effect\" }"))),
                      "rowan", "if.effect", "missing");
        }

        [Test]
        public void EveryConditionOfTheGdd_Loads()
        {
            foreach (var condition in new[]
            {
                "{ \"test\": \"allies_with\", \"count\": 2, \"trait\": \"greek\" }",
                "{ \"test\": \"per_ally_with\", \"class\": \"caster\" }",
                "{ \"test\": \"target_has_effect\", \"effect\": \"defense_down\" }",
                "{ \"test\": \"target_has\", \"trait\": \"human\" }",
                "{ \"test\": \"target_not_acted\" }",
                "{ \"test\": \"first_attack\" }",
            })
            {
                var heroes = RowanBasicPiece(Damage(", \"if\": " + condition));
                Assert.DoesNotThrow(() => TestData.Load(heroes: heroes), condition);
            }
        }

        // ---- a piece missing a setting its kind needs (2026-11-16 addition) ---------------------------

        [Test]
        public void DamageWithoutPower_IsRejected()
        {
            ExpectOne(Problems(heroes: RowanBasicPiece("{ \"do\": \"damage\", \"target\": \"enemy\" }")),
                      "rowan", "basic.pieces[0].power", "missing");
        }

        [Test]
        public void HealWithoutPercent_IsRejected()
        {
            ExpectOne(Problems(heroes: RowanBasicPiece("{ \"do\": \"heal\", \"target\": \"ally\" }")),
                      "rowan", "basic.pieces[0].percent", "missing");
        }

        [Test]
        public void TurnMeterWithoutPercent_IsRejected()
        {
            ExpectOne(Problems(heroes: RowanBasicPiece("{ \"do\": \"meter\", \"target\": \"enemy\" }")),
                      "rowan", "basic.pieces[0].percent", "missing");
        }

        [Test]
        public void ApplyEffectWithoutEffect_IsRejected()
        {
            ExpectOne(Problems(heroes: RowanBasicPiece("{ \"do\": \"effect\", \"target\": \"self\", \"turns\": 1 }")),
                      "rowan", "basic.pieces[0].effect", "missing");
        }

        [Test]
        public void ApplyEffectWithoutTurns_IsRejected_ExceptStunAndMomentum()
        {
            ExpectOne(Problems(heroes: RowanBasicPiece("{ \"do\": \"effect\", \"target\": \"self\", \"effect\": \"taunt\" }")),
                      "rowan", "basic.pieces[0].turns", "missing");
            // 02 GDD 7.4: Stun and Momentum have no turn count.
            foreach (var effect in new[] { "stun", "momentum" })
            {
                var heroes = RowanBasicPiece("{ \"do\": \"effect\", \"target\": \"self\", \"effect\": \"" + effect + "\" }");
                Assert.DoesNotThrow(() => TestData.Load(heroes: heroes), effect);
            }
        }

        [Test]
        public void ClearWithoutCount_Loads_MeaningAll()
        {
            var heroes = RowanBasicPiece("{ \"do\": \"clear\", \"target\": \"all_allies\" }");
            Assert.DoesNotThrow(() => TestData.Load(heroes: heroes));
        }

        [Test]
        public void SpecialWithoutCooldown_IsRejected()
        {
            var heroes = Rowan(h => ((JObject)h["special"]).Remove("cooldown"));
            ExpectOne(Problems(heroes: heroes), "rowan", "special.cooldown", "missing");
        }

        [Test]
        public void MissingSkillOrSkillWithoutPieces_IsRejected()
        {
            ExpectOne(Problems(heroes: Rowan(h => ((JObject)h).Remove("ultimate"))), "rowan", "ultimate", "missing");
            ExpectOne(Problems(heroes: Rowan(h => h["basic"]["pieces"] = new JArray())),
                      "rowan", "basic.pieces", "at least one piece");
            ExpectOne(Problems(heroes: Rowan(h => ((JObject)h["basic"]).Remove("name"))), "rowan", "basic.name", "missing");
        }

        [Test]
        public void MissingLeaderOrPassive_IsRejected()
        {
            ExpectOne(Problems(heroes: Rowan(h => ((JObject)h).Remove("leader"))), "rowan", "leader", "missing");
            ExpectOne(Problems(heroes: Rowan(h => ((JObject)h).Remove("passive"))), "rowan", "passive", "missing");
        }

        // ---- bonuses ----------------------------------------------------------------------------------

        [Test]
        public void BonusOnAStatNoRuleCovers_IsRejected(
            [Values("crit_damage", "block_chance", "block_amount", "will", "grit", "luck")] string stat)
        {
            var heroes = RowanBonus("{ \"stat\": \"" + stat + "\", \"percent\": 10, \"who\": { \"trait\": \"greek\" } }");
            ExpectOne(Problems(heroes: heroes), "rowan", "leader.bonuses[0].stat", stat);
        }

        [Test]
        public void BonusOnTheCoveredStats_Loads([Values("health", "armor", "damage", "speed", "accuracy",
            "dodge_chance", "crit_chance", "healing")] string stat)
        {
            var heroes = RowanBonus("{ \"stat\": \"" + stat + "\", \"percent\": 10, \"who\": { \"trait\": \"greek\" } }");
            Assert.DoesNotThrow(() => TestData.Load(heroes: heroes));
        }

        [Test]
        public void BonusWithoutPercent_IsRejected()
        {
            ExpectOne(Problems(heroes: RowanBonus("{ \"stat\": \"damage\", \"who\": { \"trait\": \"greek\" } }")),
                      "rowan", "leader.bonuses[0].percent", "missing");
        }

        [Test]
        public void SpeedBonusWithAConditionThatIsNotATraitOrClassCount_IsRejected(
            [Values("{ \"test\": \"target_has_effect\", \"effect\": \"taunt\" }",
                    "{ \"test\": \"target_has\", \"trait\": \"greek\" }",
                    "{ \"test\": \"target_not_acted\" }",
                    "{ \"test\": \"first_attack\" }")] string condition)
        {
            var heroes = RowanBonus("{ \"stat\": \"speed\", \"percent\": 10, \"who\": { \"trait\": \"greek\" }, \"if\": " + condition + " }");
            ExpectOne(Problems(heroes: heroes), "rowan", "leader.bonuses[0].if", "Speed");
        }

        [Test]
        public void SpeedBonusWithNoConditionOrACount_Loads(
            [Values("", ", \"if\": { \"test\": \"allies_with\", \"count\": 2, \"trait\": \"greek\" }",
                    ", \"if\": { \"test\": \"per_ally_with\", \"class\": \"caster\" }")] string condition)
        {
            var heroes = RowanBonus("{ \"stat\": \"speed\", \"percent\": 10, \"who\": { \"trait\": \"greek\" }" + condition + " }");
            Assert.DoesNotThrow(() => TestData.Load(heroes: heroes));
        }

        [Test]
        public void MomentConditionOnAStatThatCannotDependOnTheMoment_IsRejected(
            [Values("health", "armor", "dodge_chance")] string stat)
        {
            var heroes = RowanBonus("{ \"stat\": \"" + stat + "\", \"percent\": 10, \"who\": { \"trait\": \"greek\" }, " +
                                    "\"if\": { \"test\": \"target_has\", \"trait\": \"human\" } }");
            ExpectOne(Problems(heroes: heroes), "rowan", "leader.bonuses[0].if", stat);
        }

        [Test]
        public void MomentConditionOnAccuracyCritChanceDamageAndHealing_Loads(
            [Values("accuracy", "crit_chance", "damage", "healing")] string stat,
            [Values("{ \"test\": \"first_attack\" }", "{ \"test\": \"target_has\", \"trait\": \"human\" }")] string condition)
        {
            var heroes = RowanBonus("{ \"stat\": \"" + stat + "\", \"percent\": 10, \"if\": " + condition + " }");
            Assert.DoesNotThrow(() => TestData.Load(heroes: heroes));
        }

        // ---- traits, cut-in, tuning -------------------------------------------------------------------

        [Test]
        public void HeroWithoutOneTraitOfEachKind_IsRejected()
        {
            // no origin trait
            ExpectOne(Problems(heroes: Rowan(h => ((JArray)h["traits"]).RemoveAt(2))), "rowan", "traits", "origin", "found 0");
            // two side traits
            ExpectOne(Problems(heroes: Rowan(h => ((JArray)h["traits"]).Add("villain"))), "rowan", "traits", "side", "found 2");
            // no pantheon trait
            ExpectOne(Problems(heroes: Rowan(h => ((JArray)h["traits"]).RemoveAt(1))), "rowan", "traits", "pantheon", "found 0");
        }

        [Test]
        public void TraitOfAnUnknownKind_IsRejected()
        {
            var traits = TestData.Edit(TestData.Traits, "human", t => t["kind"] = "element");
            // 'human' is rowan's, orla's and dax's origin: every hero that loses its origin is reported too.
            var problems = Problems(traits: traits);
            Assert.IsTrue(problems.Any(p => p.Contains("trait 'human'") && p.Contains("element")), string.Join(" | ", problems));
        }

        [Test]
        public void PantheonTraitWithoutBothColours_IsRejected()
        {
            var noGlow = TestData.Edit(TestData.Traits, "greek", t => ((JObject)t["cutin_colors"]).Remove("glow"));
            ExpectOne(Problems(traits: noGlow), "greek", "cutin_colors.glow", "missing");

            var noBackground = TestData.Edit(TestData.Traits, "greek", t => ((JObject)t["cutin_colors"]).Remove("background"));
            ExpectOne(Problems(traits: noBackground), "greek", "cutin_colors.background", "missing");

            var noColours = TestData.Edit(TestData.Traits, "norse", t => ((JObject)t).Remove("cutin_colors"));
            var problems = Problems(traits: noColours);
            Assert.AreEqual(2, problems.Count, "both colours are reported: " + string.Join(" | ", problems));
            Assert.IsTrue(problems.All(p => p.Contains("norse")));
        }

        [Test]
        public void HeroWithoutAShout_IsRejected()
        {
            ExpectOne(Problems(heroes: Rowan(h => ((JObject)h["cutin"]).Remove("shout"))), "rowan", "cutin.shout", "missing");
            ExpectOne(Problems(heroes: Rowan(h => ((JObject)h).Remove("cutin"))), "rowan", "cutin.shout", "missing");
            ExpectOne(Problems(heroes: Rowan(h => h["cutin"]["shout"] = "  ")), "rowan", "cutin.shout", "missing");
        }

        [Test]
        public void HeroWithoutADrawingOrBehindLayer_Loads()
        {
            // the placeholder silhouette (03 Architecture 3.3): the seven placeholder heroes have no drawing
            Assert.DoesNotThrow(() => TestData.Load());
            Assert.IsNull(TestData.Load().Heroes["rowan"].Cutin.Drawing);
            Assert.IsNull(TestData.Load().Heroes["rowan"].Cutin.Behind);
        }

        [Test]
        public void MissingTuningValue_IsRejected_NamingTheValue()
        {
            foreach (var name in TestData.TuningNames)
                ExpectOne(Problems(tuning: TestData.TuningWithout(name)), "tuning", name, "missing");
        }

        // ---- teams ------------------------------------------------------------------------------------

        static string CrewWith(System.Action<JObject> change) => TestData.Edit(TestData.Teams, "test_crew", change);

        [Test]
        public void TeamThatIsNotFiveHeroes_IsRejected()
        {
            var four = CrewWith(t => ((JArray)t["slots"]).RemoveAt(4));
            ExpectOne(Problems(teams: four), "test_crew", "slots", "five", "found 4");

            var six = CrewWith(t => ((JArray)t["slots"]).Add("rowan"));
            Assert.IsTrue(Problems(teams: six).Any(p => p.Contains("found 6")));
        }

        [Test]
        public void TeamWithTheSameHeroTwice_IsRejected()
        {
            var twice = CrewWith(t => t["slots"][4] = "rowan");
            ExpectOne(Problems(teams: twice), "test_crew", "slots", "rowan", "more than one");
        }

        [Test]
        public void TeamWithAnUnknownHero_IsRejected()
        {
            var unknown = CrewWith(t => t["slots"][4] = "nobody");
            ExpectOne(Problems(teams: unknown), "test_crew", "slots[4]", "nobody");
        }

        [Test]
        public void TeamWhoseLeaderIsNotOneOfTheFive_IsRejected()
        {
            var heroes = TestData.HeroesFile(
                TestData.Hero("rowan"), TestData.Hero("mira"), TestData.Hero("vex"),
                TestData.Hero("orla"), TestData.Hero("dax"), TestData.Hero("zed"));
            var outsider = CrewWith(t => t["leader"] = "zed");
            var error = Assert.Throws<GameDataException>(() => TestData.Load(heroes: heroes, teams: outsider));
            ExpectOne(error.Problems, "test_crew", "leader", "zed");
        }

        [Test]
        public void EveryFormationOfTheGdd_Loads([Values("3-2", "2-3", "4-1", "1-4")] string formation)
        {
            var teams = CrewWith(t => t["formation"] = formation);
            Assert.AreEqual(formation, TestData.Load(teams: teams).Teams["test_crew"].Formation);
        }

        [Test]
        public void TeamWithAFormationThatIsNotInTheFile_IsRejected([Values("5-0", "0-5", "3-3", "2", "")] string formation)
        {
            // 03 Architecture 7.1: the formations are the ones in formations.json, not four names written in code
            var teams = CrewWith(t => t["formation"] = formation);
            ExpectOne(Problems(teams: teams), "test_crew", "formation");
        }

        [Test]
        public void TeamWithAFormationAddedToTheFile_Loads()
        {
            // the point of the data file: a new formation needs no code change
            var wide = FormationsWith(list => list.Add(JObject.Parse(@"{ ""id"": ""wide"", ""slots"": [
                { ""slot"": 1, ""row"": ""front"", ""at"": [1, 1] }, { ""slot"": 2, ""row"": ""back"", ""covered_by"": [1], ""at"": [2, 2] },
                { ""slot"": 3, ""row"": ""back"", ""covered_by"": [1], ""at"": [2, 3] }, { ""slot"": 4, ""row"": ""back"", ""covered_by"": [1], ""at"": [2, 4] },
                { ""slot"": 5, ""row"": ""back"", ""covered_by"": [1], ""at"": [2, 5] } ] }")));
            var teams = CrewWith(t => t["formation"] = "wide");
            Assert.AreEqual("wide", TestData.Load(teams: teams, formations: wide).Teams["test_crew"].Formation);
        }

        // ---- formations (03 Architecture 7.1) --------------------------------------------------------

        /// <summary>formations.json with the list changed by <paramref name="change"/>.</summary>
        static string FormationsWith(System.Action<JArray> change)
        {
            var list = JArray.Parse(TestData.Formations);
            change(list);
            return list.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>formations.json with the "3-2" record changed.</summary>
        static string Formation32(System.Action<JObject> change) => TestData.Edit(TestData.Formations, "3-2", change);

        static JObject Slot32(JObject formation, int index) => (JObject)formation["slots"][index];

        [Test]
        public void FormationIdUsedTwice_IsRejected()
        {
            var twice = FormationsWith(list => list.Add(list[0].DeepClone()));
            ExpectOne(Problems(formations: twice), "formations", "3-2", "more than once");
        }

        [Test]
        public void FormationThatIsNotFiveSlots_IsRejected()
        {
            var four = Formation32(f => ((JArray)f["slots"]).RemoveAt(4));
            ExpectOne(Problems(formations: four), "formation '3-2'", "slots", "five", "found 4");

            var six = Formation32(f => ((JArray)f["slots"]).Add(Slot32(f, 4).DeepClone()));
            ExpectOne(Problems(formations: six), "formation '3-2'", "slots", "found 6");
        }

        [Test]
        public void FormationSlotsNotNumberedOneToFiveInOrder_AreRejected()
        {
            var wrong = Formation32(f => Slot32(f, 2)["slot"] = 7);
            ExpectOne(Problems(formations: wrong), "formation '3-2'", "slots[2].slot", "must be 3");

            var missing = Formation32(f => Slot32(f, 0).Remove("slot"));
            ExpectOne(Problems(formations: missing), "formation '3-2'", "slots[0].slot", "must be 1");
        }

        [Test]
        public void FormationSlotWithAnUnknownRow_IsRejected()
        {
            var unknown = Formation32(f => Slot32(f, 1)["row"] = "middle");
            ExpectOne(Problems(formations: unknown), "formation '3-2'", "slots[1].row", "middle");
        }

        [Test]
        public void FormationWithABackSlotBeforeAFrontSlot_IsRejected()
        {
            // slot 3 becomes a back slot (still covered by slot 1) and slot 4 a front slot: the front ones must come first
            var swapped = Formation32(f =>
            {
                Slot32(f, 2)["row"] = "back";
                Slot32(f, 2)["covered_by"] = new JArray(1);
                Slot32(f, 3)["row"] = "front";
                Slot32(f, 3).Remove("covered_by");
                Slot32(f, 4)["covered_by"] = new JArray(2);
            });
            ExpectOne(Problems(formations: swapped), "formation '3-2'", "slots", "front slots must come first");
        }

        [Test]
        public void FiveZeroWrittenAsAFormation_IsStillRefused()
        {
            // 02 GDD 11 test 15: nobody protected is not a formation; the file can't add one by mistake
            var fiveZero = FormationsWith(list => list.Add(JObject.Parse(@"{ ""id"": ""5-0"", ""slots"": [
                { ""slot"": 1, ""row"": ""front"", ""at"": [1, 1] }, { ""slot"": 2, ""row"": ""front"", ""at"": [1, 2] },
                { ""slot"": 3, ""row"": ""front"", ""at"": [1, 3] }, { ""slot"": 4, ""row"": ""front"", ""at"": [1, 4] },
                { ""slot"": 5, ""row"": ""front"", ""at"": [1, 5] } ] }")));
            ExpectOne(Problems(formations: fiveZero), "formation '5-0'", "slots", "one front slot and one back slot");

            var zeroFive = FormationsWith(list => list.Add(JObject.Parse(@"{ ""id"": ""0-5"", ""slots"": [
                { ""slot"": 1, ""row"": ""back"", ""covered_by"": [1], ""at"": [1, 1] }, { ""slot"": 2, ""row"": ""back"", ""covered_by"": [1], ""at"": [1, 2] },
                { ""slot"": 3, ""row"": ""back"", ""covered_by"": [1], ""at"": [1, 3] }, { ""slot"": 4, ""row"": ""back"", ""covered_by"": [1], ""at"": [1, 4] },
                { ""slot"": 5, ""row"": ""back"", ""covered_by"": [1], ""at"": [1, 5] } ] }")));
            Assert.IsTrue(Problems(formations: zeroFive).Any(p => p.Contains("formation '0-5'") && p.Contains("one front slot and one back slot")));
        }

        [Test]
        public void BackSlotWithNoCover_IsRejected()
        {
            var none = Formation32(f => Slot32(f, 3).Remove("covered_by"));
            ExpectOne(Problems(formations: none), "formation '3-2'", "slots[3].covered_by", "at least one");

            var empty = Formation32(f => Slot32(f, 3)["covered_by"] = new JArray());
            ExpectOne(Problems(formations: empty), "formation '3-2'", "slots[3].covered_by", "at least one");
        }

        [Test]
        public void BackSlotCoveredByASlotThatIsNotAFrontSlotOfTheFormation_IsRejected()
        {
            var aBackSlot = Formation32(f => Slot32(f, 3)["covered_by"] = new JArray(1, 5));
            ExpectOne(Problems(formations: aBackSlot), "formation '3-2'", "slots[3].covered_by", "5", "not a front slot");

            var outside = Formation32(f => Slot32(f, 3)["covered_by"] = new JArray(0, 2));
            ExpectOne(Problems(formations: outside), "formation '3-2'", "slots[3].covered_by", "0", "not a front slot");
        }

        [Test]
        public void FrontSlotWithCover_IsRejected()
        {
            var covered = Formation32(f => Slot32(f, 1)["covered_by"] = new JArray(1));
            ExpectOne(Problems(formations: covered), "formation '3-2'", "slots[1].covered_by", "front slot");
        }

        [Test]
        public void FormationSlotWithoutTwoNumbersForItsPlace_IsRejected()
        {
            var missing = Formation32(f => Slot32(f, 0).Remove("at"));
            ExpectOne(Problems(formations: missing), "formation '3-2'", "slots[0].at", "two numbers");

            var one = Formation32(f => Slot32(f, 0)["at"] = new JArray(300));
            ExpectOne(Problems(formations: one), "formation '3-2'", "slots[0].at", "two numbers");

            var three = Formation32(f => Slot32(f, 0)["at"] = new JArray(300, 100, 5));
            ExpectOne(Problems(formations: three), "formation '3-2'", "slots[0].at", "two numbers");
        }

        [Test]
        public void FormationsFileThatCannotBeRead_IsRejected_AsAProblemNotACrash()
        {
            ExpectOne(Problems(formations: "this is not json"), "formations");
            ExpectOne(Problems(formations: "null"), "formations", "empty");
        }

        // ---- files that cannot be read, repeated ids, several problems at once -----------------------

        [Test]
        public void DuplicateIds_AreRejected()
        {
            var heroes = TestData.HeroesFile(
                TestData.Hero("rowan"), TestData.Hero("mira"), TestData.Hero("vex"),
                TestData.Hero("orla"), TestData.Hero("dax"), TestData.Hero("rowan"));
            ExpectOne(Problems(heroes: heroes), "hero", "rowan", "more than once");
        }

        [Test]
        public void RecordWithoutAnId_IsRejected()
        {
            var classes = "[ { \"id\": \"vanguard\", \"name\": \"Vanguard\" }, { \"id\": \"caster\", \"name\": \"Caster\" }, " +
                          "{ \"id\": \"striker\", \"name\": \"Striker\" }, { \"name\": \"Nameless\" } ]";
            ExpectOne(Problems(classes: classes), "classes", "no id");
        }

        [Test]
        public void FileThatIsNotReadableJson_IsRejected_AsAProblemNotACrash()
        {
            ExpectOne(Problems(heroes: "this is not json"), "heroes");
            ExpectOne(Problems(classes: "{ \"id\": \"vanguard\" }"), "classes");   // an object where a list belongs
            ExpectOne(Problems(tuning: "{ \"meter_full\": \"lots\" }"), "tuning");
            ExpectOne(Problems(teams: "null"), "teams", "empty");
        }

        [Test]
        public void ProblemsFromDifferentFilesAndHeroes_ComeBackTogether_InOneError()
        {
            var heroes = TestData.HeroesWith("rowan", h => h["stats"]["speed"] = 0);
            var traits = TestData.Edit(TestData.Traits, "greek", t => ((JObject)t["cutin_colors"]).Remove("glow"));
            var teams = CrewWith(t => t["formation"] = "5-0");

            var problems = Problems(heroes: heroes, traits: traits,
                                    tuning: TestData.TuningWithout("armor_k"), teams: teams);

            Assert.AreEqual(4, problems.Count, string.Join(" | ", problems));
            Assert.IsTrue(problems.Any(p => p.Contains("rowan") && p.Contains("speed")));
            Assert.IsTrue(problems.Any(p => p.Contains("greek") && p.Contains("glow")));
            Assert.IsTrue(problems.Any(p => p.Contains("armor_k")));
            Assert.IsTrue(problems.Any(p => p.Contains("test_crew") && p.Contains("formation")));
        }
    }
}
