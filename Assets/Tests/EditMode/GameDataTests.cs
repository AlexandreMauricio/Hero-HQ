using System.Linq;
using HeroHQ.Rules.Data;
using NUnit.Framework;

namespace HeroHQ.Tests
{
    // P1-02 (03 Architecture 4.12): GameData.Load reads the five data texts and either hands back the
    // whole content, found by id, or stops with every problem it found, in one error (3.4).
    // The individual checks of 3.4 arrive one by one in P1-03; this task proves the loading and the error.
    public class GameDataTests
    {
        [Test]
        public void ValidSet_LoadsEveryHeroClassTraitTeamAndTuningValue_FoundById()
        {
            var data = TestData.Load();

            Assert.AreEqual(5, data.Heroes.Count);
            foreach (var id in TestData.HeroIds)
                Assert.IsTrue(data.Heroes.ContainsKey(id), "hero " + id);

            var mira = data.Heroes["mira"];
            Assert.AreEqual("mira", mira.Id);
            Assert.AreEqual("Mira", mira.Name);
            Assert.AreEqual("caster", mira.Class);
            CollectionAssert.AreEqual(new[] { "hero", "norse", "god" }, mira.Traits);
            Assert.AreEqual(110, mira.Stats["speed"]);
            Assert.AreEqual(12, mira.Stats.Count);
            Assert.AreEqual("Mira Strike", mira.Basic.Name);
            Assert.AreEqual(2, mira.Special.Cooldown);
            Assert.AreEqual(300, mira.Ultimate.Pieces[0].Power);
            Assert.AreEqual("Mira Crew", mira.Leader.Name);
            Assert.AreEqual("MIRA!", mira.Cutin.Shout);
            Assert.AreEqual(120, data.Heroes["vex"].Stats["speed"], "each hero keeps their own numbers");

            Assert.AreEqual(3, data.Classes.Count);
            Assert.AreEqual("Vanguard", data.Classes["vanguard"].Name);
            Assert.AreEqual("Caster", data.Classes["caster"].Name);
            Assert.AreEqual("Striker", data.Classes["striker"].Name);

            Assert.AreEqual(6, data.Traits.Count);
            Assert.AreEqual("side", data.Traits["villain"].Kind);
            Assert.AreEqual("pantheon", data.Traits["norse"].Kind);
            Assert.AreEqual("#778899", data.Traits["norse"].CutinColors.Background);
            Assert.AreEqual("#aabbcc", data.Traits["norse"].CutinColors.Glow);
            Assert.AreEqual("origin", data.Traits["god"].Kind);

            Assert.AreEqual(2, data.Teams.Count);
            var crew = data.Teams["test_crew"];
            Assert.AreEqual("enemy", crew.Kind);
            Assert.AreEqual("2-3", crew.Formation);
            Assert.AreEqual("vex", crew.Leader);
            CollectionAssert.AreEqual(new[] { "rowan", "mira", "vex", "orla", "dax" }, crew.Slots);
            Assert.AreEqual("target", data.Teams["test_target"].Kind);
            Assert.AreEqual("3-2", data.Teams["test_target"].Formation);

            Assert.AreEqual(TestData.TuningNames.Length, data.Tuning.Count);
            Assert.AreEqual(1000, data.Tuning["meter_full"]);
            Assert.AreEqual(25, data.Tuning["charge_basic"]);
            Assert.AreEqual(10, data.Tuning["charge_special"]);
            Assert.AreEqual(1000, data.Tuning["armor_k"]);
            Assert.AreEqual(30, data.Tuning["max_dodge"]);
        }

        [Test]
        public void IdNotInTheData_IsNotFound()
        {
            var data = TestData.Load();
            Assert.IsFalse(data.Heroes.ContainsKey("nobody"));
            Assert.IsFalse(data.Classes.ContainsKey("wizard"));
            Assert.IsFalse(data.Tuning.ContainsKey("no_such_value"));
        }

        [Test]
        public void UnknownClass_GivesOneProblem_NamingTheHeroAndTheField()
        {
            var heroes = TestData.HeroesFile(
                TestData.Hero("rowan", cls: "wizard"),
                TestData.Hero("mira", cls: "caster"),
                TestData.Hero("vex", cls: "striker"),
                TestData.Hero("orla", cls: "vanguard"),
                TestData.Hero("dax", cls: "caster"));

            var error = Assert.Throws<GameDataException>(() => TestData.Load(heroes: heroes));

            Assert.AreEqual(1, error.Problems.Count);
            StringAssert.Contains("rowan", error.Problems[0], "names the hero");
            StringAssert.Contains("class", error.Problems[0], "names the field");
            StringAssert.Contains("wizard", error.Problems[0], "names the bad value");
        }

        [Test]
        public void ThreeProblems_ComeBackTogether_InOneError()
        {
            var heroes = TestData.HeroesFile(
                TestData.Hero("rowan", cls: "wizard"),
                TestData.Hero("mira", cls: "caster"),
                TestData.Hero("vex", cls: "pirate"),
                TestData.Hero("orla", cls: "vanguard"),
                TestData.Hero("dax", cls: "knight"));

            var error = Assert.Throws<GameDataException>(() => TestData.Load(heroes: heroes));

            Assert.AreEqual(3, error.Problems.Count, string.Join(" | ", error.Problems));
            StringAssert.Contains("rowan", error.Problems[0]);
            StringAssert.Contains("vex", error.Problems[1]);
            StringAssert.Contains("dax", error.Problems[2]);
            foreach (var problem in error.Problems)
                StringAssert.Contains(problem, error.Message, "the error's message carries every problem");
            Assert.IsFalse(error.Problems.Any(p => p.Contains("mira") || p.Contains("orla")),
                           "heroes with a known class are not blamed");
        }
    }
}
