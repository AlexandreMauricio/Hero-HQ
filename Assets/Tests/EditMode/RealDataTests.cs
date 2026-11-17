using System.IO;
using System.Linq;
using HeroHQ.Rules;
using HeroHQ.Rules.Data;
using NUnit.Framework;
using UnityEngine;

namespace HeroHQ.Tests
{
    // P1-17 (03 Architecture 4.12): the five REAL data files in Assets/Data/ load with no problem, and the numbers
    // in them are the ones 02 GDD 10.2-10.4 (OWNER) and 01 Vision write. Unlike every other test file, these tests
    // read the real files, so they fail on purpose when the owner re-tunes a number: change the expectation here
    // in the same turn as the file (the table below IS the 02 GDD 10.3 table).
    public class RealDataTests
    {
        static string Read(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "Data", file));

        static GameData Load() =>
            GameData.Load(Read("heroes.json"), Read("classes.json"), Read("traits.json"), Read("tuning.json"), Read("teams.json"));

        static GameData data;
        [OneTimeSetUp] public void LoadOnce() => data = Load();

        // ---- the files load ------------------------------------------------------------------------------------

        [Test]
        public void TheFiveFiles_LoadWithNoProblem()
        {
            Assert.DoesNotThrow(() => Load(), "GameData.Load collects every problem of 03 Architecture 3.4 and throws with the list");
        }

        [Test]
        public void Counts_EightHeroes_SixClasses_NineTraits_FourTeams()
        {
            Assert.AreEqual(8, data.Heroes.Count);
            Assert.AreEqual(6, data.Classes.Count);
            Assert.AreEqual(9, data.Traits.Count, "Side 2 + Pantheon 3 + Origin 4 (01 Vision)");
            Assert.AreEqual(4, data.Teams.Count, "the enemy crew and three target teams (02 GDD 10.1)");
        }

        [Test]
        public void EveryId_IsLowerCaseWithUnderscores_SoDocNeferIsDoc_Nefer()
        {
            var ids = data.Heroes.Keys.Concat(data.Classes.Keys).Concat(data.Traits.Keys).Concat(data.Teams.Keys);
            foreach (var id in ids) StringAssert.IsMatch("^[a-z][a-z_]*$", id, "ids are lower case with underscores");
            Assert.That(data.Heroes.Keys, Is.EquivalentTo(
                new[] { "skipjack", "strix", "kal", "halvard", "thordis", "ibis", "hori", "doc_nefer" }));
        }

        // ---- classes and traits (01 Vision) and colours (03 Architecture 3.3) ----------------------------------

        [Test]
        public void Classes_AreTheSixOf01Vision()
        {
            Assert.That(data.Classes.Keys, Is.EquivalentTo(new[] { "vanguard", "bruiser", "hunter", "caster", "disruptor", "mender" }));
        }

        [TestCase("hero", "side"), TestCase("outlaw", "side"),
         TestCase("greek", "pantheon"), TestCase("norse", "pantheon"), TestCase("egyptian", "pantheon"),
         TestCase("godborn", "origin"), TestCase("sworn", "origin"), TestCase("relic", "origin"), TestCase("tech", "origin")]
        public void Traits_HaveTheirFamily(string id, string kind)
        {
            Assert.AreEqual(kind, data.Traits[id].Kind);
        }

        [TestCase("greek", "#1b6fd1", "#ffd23f"), TestCase("norse", "#d7263d", "#a8e6ff"), TestCase("egyptian", "#10b3a6", "#f2c14e")]
        public void PantheonColours_AreTheMockUpsPairs(string id, string background, string glow)
        {
            Assert.AreEqual(background, data.Traits[id].CutinColors.Background);
            Assert.AreEqual(glow, data.Traits[id].CutinColors.Glow);
        }

        // ---- tuning (02 GDD 10.2) ------------------------------------------------------------------------------

        [Test]
        public void Tuning_IsTheTableOf10_2()
        {
            var expected = new (string name, double value)[]
            {
                ("meter_full", 1000), ("turn_bar_length", 8), ("charge_basic", 25), ("charge_special", 15), ("charge_per_hurt", 1),
                ("max_dodge", 30), ("armor_k", 1000), ("offense_change", 25), ("defense_change", 25),
                ("momentum_step", 8), ("momentum_max", 5),
            };
            Assert.AreEqual(expected.Length, data.Tuning.Count, "no extra values");
            foreach (var (name, value) in expected) Assert.AreEqual(value, data.Tuning[name], name);
        }

        // ---- heroes: classes, traits, stats (01 Vision table; 02 GDD 10.3) -------------------------------------

        // id, class, side, pantheon, origin, Health, Armor, Damage, Speed, Accuracy, Dodge, Crit Chance, Crit Damage
        static readonly object[][] Heroes =
        {
            new object[] { "skipjack", "hunter",    "hero",   "greek",    "godborn", 2600, 150, 515, 140,  0, 10, 20, 150 },
            new object[] { "strix",    "hunter",    "outlaw", "greek",    "sworn",   2700, 150, 515, 115, 15,  5, 25, 175 },
            new object[] { "kal",      "caster",    "outlaw", "greek",    "tech",    3000, 150, 460,  90,  0,  0, 10, 150 },
            new object[] { "halvard",  "vanguard",  "hero",   "norse",    "godborn", 5000, 500, 300,  70,  0,  0,  5, 150 },
            new object[] { "thordis",  "bruiser",   "outlaw", "norse",    "tech",    4200, 350, 445,  85, 10,  0, 10, 150 },
            new object[] { "ibis",     "disruptor", "hero",   "egyptian", "sworn",   3000, 200, 350, 100, 10,  5, 10, 150 },
            new object[] { "hori",     "bruiser",   "hero",   "egyptian", "godborn", 3600, 250, 430, 110, 20, 10, 15, 150 },
            new object[] { "doc_nefer", "mender",   "outlaw", "egyptian", "relic",   3200, 200, 270,  95,  0,  5,  5, 150 },
        };

        [TestCaseSource(nameof(Heroes))]
        public void Hero_HasItsClassTraitsAndStats(string id, string cls, string side, string pantheon, string origin,
                                                   int health, int armor, int damage, int speed, int accuracy, int dodge,
                                                   int critChance, int critDamage)
        {
            var hero = data.Heroes[id];
            Assert.AreEqual(cls, hero.Class);
            Assert.That(hero.Traits, Is.EquivalentTo(new[] { side, pantheon, origin }));

            var stats = hero.Stats;
            Assert.AreEqual(health, stats["health"]);
            Assert.AreEqual(armor, stats["armor"]);
            Assert.AreEqual(damage, stats["damage"]);
            Assert.AreEqual(speed, stats["speed"]);
            Assert.AreEqual(accuracy, stats["accuracy"]);
            Assert.AreEqual(dodge, stats["dodge_chance"]);
            Assert.AreEqual(critChance, stats["crit_chance"]);
            Assert.AreEqual(critDamage, stats["crit_damage"], "Crit Damage is a percent, 150 = x1.5 (P1-04 reading)");
            foreach (var off in new[] { "block_chance", "block_amount", "will", "grit" })
                Assert.AreEqual(0, stats[off], off + " is off in the first playable (02 GDD 5)");
        }

        [Test]
        public void Speed_RunsFromSeventyToOneForty_ExactlyTwice()
        {
            var speeds = data.Heroes.Values.Select(h => h.Stats["speed"]).ToList();
            Assert.AreEqual(70, speeds.Min());
            Assert.AreEqual(140, speeds.Max());
        }

        // ---- kits (02 GDD 10.4): spot checks of the lines that are easy to get wrong ----------------------------

        [Test]
        public void Skipjack_Damage515_ReturnToSenderPunishesThoseWhoHaveNotActed_UltimateReachesTheBackRow()
        {
            var skipjack = data.Heroes["skipjack"];
            Assert.AreEqual(515, skipjack.Stats["damage"]);

            var special = skipjack.Special;
            Assert.AreEqual("Return to Sender", special.Name);
            Assert.AreEqual(1, special.Cooldown);
            Assert.AreEqual(new double?[] { 150, 150 }, special.Pieces.Select(p => p.Power).ToArray());
            Assert.IsNull(special.Pieces[0].If);
            Assert.AreEqual("target_not_acted", special.Pieces[1].If.Test);

            var ultimate = skipjack.Ultimate;
            Assert.AreEqual(450, ultimate.Pieces.Single().Power);
            Assert.IsTrue(ultimate.Pieces.Single().AlwaysCrits);
            Assert.IsTrue(ultimate.Pieces.Single().ReachesBackRow);

            var fill = skipjack.Leader.Triggers.Single();
            Assert.AreEqual("fight_start", fill.When);
            Assert.AreEqual("greek", fill.Who.Trait);
            Assert.AreEqual(50, fill.Pieces.Single().Percent);
        }

        [Test]
        public void CooldownsAreOneExceptHaulIn_ReachesTheBackRowOnlyWhereTheGDDSays()
        {
            Assert.AreEqual(7, data.Heroes.Values.Count(h => h.Special.Cooldown == 1));
            Assert.AreEqual(2, data.Heroes["thordis"].Special.Cooldown, "Haul In stuns, so every other turn (10.4)");

            var reaching = data.Heroes.Values
                .SelectMany(h => new[] { (h.Id, "basic", h.Basic), (h.Id, "special", h.Special), (h.Id, "ultimate", h.Ultimate) })
                .Where(s => s.Item3.Pieces.Any(p => p.ReachesBackRow))
                .Select(s => $"{s.Item1}.{s.Item2}");
            Assert.That(reaching, Is.EquivalentTo(new[] { "skipjack.ultimate", "strix.ultimate", "thordis.special" }));
        }

        [Test]
        public void EveryUltimateShout_IsTheUltimatesName()
        {
            foreach (var hero in data.Heroes.Values)
                Assert.AreEqual(hero.Ultimate.Name, hero.Cutin.Shout, hero.Id);
            Assert.AreEqual("kal", data.Heroes["kal"].Cutin.Drawing, "Kal & Talos is the one finished drawing (01 Vision)");
            Assert.AreEqual("kal_behind", data.Heroes["kal"].Cutin.Behind);
            Assert.IsNull(data.Heroes["hori"].Cutin.Drawing, "the seven placeholders have no drawing (03 Architecture 3.3)");
        }

        // ---- teams (02 GDD 10.1) -------------------------------------------------------------------------------

        [Test]
        public void TheCrew_IsKalHori_StrixHalvardThordis_LeaderThordis_TwoThree()
        {
            var crew = data.Teams["enemy_crew"];
            Assert.AreEqual("enemy", crew.Kind);
            Assert.AreEqual("2-3", crew.Formation);
            Assert.AreEqual("thordis", crew.Leader);
            Assert.AreEqual(new[] { "kal", "hori", "strix", "halvard", "thordis" }, crew.Slots.ToArray());
        }

        [TestCase("greek_strike", "skipjack", "2-3", "halvard,hori,skipjack,strix,kal")]
        [TestCase("healing_wall", "doc_nefer", "3-2", "halvard,hori,thordis,ibis,doc_nefer")]
        [TestCase("outlaw_burn", "thordis", "2-3", "halvard,thordis,strix,kal,doc_nefer")]
        public void TheThreeTargetTeams_AreAsDesigned(string id, string leader, string formation, string slots)
        {
            var team = data.Teams[id];
            Assert.AreEqual("target", team.Kind);
            Assert.AreEqual(leader, team.Leader);
            Assert.AreEqual(formation, team.Formation);
            Assert.AreEqual(slots.Split(','), team.Slots.ToArray());
        }

        // ---- the real kits actually run (not balance: that is P1-18's report) ----------------------------------

        [TestCase("greek_strike")]
        [TestCase("healing_wall")]
        [TestCase("outlaw_burn")]
        public void EachTargetTeam_FightsTheCrewToAWinner_WithinTheTurnLimit(string team)
        {
            for (var seed = 1; seed <= 5; seed++)
            {
                var played = TestFights.Play(data, data.Teams[team], data.Teams["enemy_crew"], seed);
                Assert.AreNotEqual(Outcome.Going, played.Fight.Outcome, $"{team}, seed {seed}: no winner in {TestFights.MaxTurns} turns");
            }
        }

        [Test]
        public void GreekStrike_TheThreeGreeksStartHalfFull_TheOthersEmpty()
        {
            var fight = Fight.Create(data, data.Teams["greek_strike"], data.Teams["enemy_crew"], 1);
            var meters = fight.Players.ToDictionary(f => f.Hero.Id, f => f.Meter);
            Assert.AreEqual(new[] { 0, 0, 500, 500, 500 },
                new[] { meters["halvard"], meters["hori"], meters["skipjack"], meters["strix"], meters["kal"] },
                "Skipjack's leader skill (02 GDD 10.4): Greek allies fill 50 % of 1,000");
            Assert.IsTrue(fight.Enemies.All(f => f.Meter == 0), "the crew's leader (Thordis) fills nobody");
        }
    }
}
