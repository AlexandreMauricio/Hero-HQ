using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroHQ.Rules;
using HeroHQ.Rules.Data;
using NUnit.Framework;
using UnityEngine;

namespace HeroHQ.Tests
{
    // P1-18 (03 Architecture 4.10, 02 GDD 11 test 17): the fight report. Each of the three target teams fights the
    // enemy crew on the REAL data, 20 seeds, EnemyAI choosing for both sides; the numbers are counted from the fight's
    // own event list and written to TestResults/fight-report.md for the owner. It REPORTS, it does not judge balance:
    // these tests fail only when the data won't load, a fight crashes, the counting is wrong, or the same run gives
    // a different report twice. Run alone with `tools/test.sh FightReport`.
    [Category("FightReport")]
    public class FightReportTests
    {
        const int Seeds = 20;
        static readonly string[] TargetTeams = { "greek_strike", "healing_wall", "outlaw_burn" };

        static GameData data;
        [OneTimeSetUp]
        public void LoadOnce()
        {
            string Read(string file) => File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "Data", file));
            data = GameData.Load(Read("heroes.json"), Read("classes.json"), Read("traits.json"), Read("tuning.json"), Read("teams.json"));
        }

        // ---- the counting, on small made-up turn orders -----------------------------------------------------------
        // Fighters come from a real little fight; the events are written by hand so each case is exactly one turn order.

        static Fight Little() => TestFights.NewFight(
            new[] { ("p0", 200), ("p1", 100), ("p2", 100), ("p3", 100), ("p4", 100) },
            new[] { ("e0", 100), ("e1", 100), ("e2", 100), ("e3", 100), ("e4", 100) });

        static List<FightEvent> Turns(Fight fight, params string[] order) =>
            order.SelectMany(who =>
            {
                var f = who[0] == 'p' ? fight.Players[who[1] - '0'] : fight.Enemies[who[1] - '0'];
                // "e0!" = a stunned turn: TurnStarted then TurnSkipped, as the fight emits it
                var events = new List<FightEvent> { new FightEvent(FightEventKind.TurnStarted, f) };
                if (who.EndsWith("!")) events.Add(new FightEvent(FightEventKind.TurnSkipped, f));
                return events;
            }).ToList();

        static int Doubles(Fight fight, string who, params string[] order)
        {
            var counts = FightReport.DoubleTurns(Turns(fight, order));
            var f = who[0] == 'p' ? fight.Players[who[1] - '0'] : fight.Enemies[who[1] - '0'];
            return counts.TryGetValue((f.Side, f.Slot), out var n) ? n : 0;
        }

        [Test]
        public void DoubleTurn_TheSameHeroTwiceInARow_IsOne() =>
            Assert.AreEqual(1, Doubles(Little(), "p0", "p0", "p0"));

        [Test]
        public void DoubleTurn_AnAllyBetween_StillCounts_NoOpponentTurnBetween() =>
            Assert.AreEqual(1, Doubles(Little(), "p0", "p0", "p1", "p0"), "02 GDD 10.6 q. 8: no OPPONENT turn since the hero's last turn");

        [Test]
        public void DoubleTurn_AnOpponentBetween_IsNotADoubleTurn() =>
            Assert.AreEqual(0, Doubles(Little(), "p0", "p0", "e0", "p0"));

        [Test]
        public void DoubleTurn_AStunnedSkippedOpponentTurnBetween_CountsAsATurnBetween() =>
            Assert.AreEqual(0, Doubles(Little(), "p0", "p0", "e0!", "p0"), "a skipped turn is a portrait on the bar (4.10)");

        [Test]
        public void DoubleTurn_AHeroFirstTurn_IsNeverOne() =>
            Assert.AreEqual(0, Doubles(Little(), "p0", "e0", "p0"));

        [Test]
        public void DoubleTurn_CountsBothSides_AndEachHeroOwnTurns()
        {
            var fight = Little();
            var order = new[] { "p0", "e0", "e0", "p1", "p0" };
            Assert.AreEqual(1, Doubles(fight, "e0", order), "e0 acted twice with no player turn between");
            Assert.AreEqual(0, Doubles(fight, "p0", order), "p0 had e0 turns between its two");
        }

        [Test]
        public void Ultimates_CountsOnlySkillUsedWithTheUltimateButton_PerSide_AndSlot()
        {
            var fight = Little();
            fight.Players[0].Charge = 100;                    // p0 is the fastest and fully charged
            var events = new List<FightEvent>(fight.AdvanceToChoice());
            Assert.AreSame(fight.Players[0], fight.Acting);
            events.AddRange(fight.Act(EnemyAI.Choose(fight)));    // the AI fires a charged, useful ultimate (02 GDD 9)

            var ultimates = FightReport.Ultimates(events);
            Assert.AreEqual(1, ultimates[(Side.Player, 0)]);
            Assert.AreEqual(1, ultimates.Values.Sum(), "nobody else fired one; Basic and Special turns are not counted");
        }

        // ---- the report on the real data --------------------------------------------------------------------------

        static List<FightReport.TeamResult> RunAll() => TargetTeams.Select(t => FightReport.Run(data, t, "enemy_crew", Seeds)).ToList();

        [Test]
        public void ThreeTeams_TwentySeedsEach_OnTheRealData_EveryFightIsRecorded()
        {
            var results = RunAll();
            Assert.AreEqual(3, results.Count);
            foreach (var r in results)
            {
                Assert.AreEqual(Seeds, r.Rows.Count, r.Team.Id);
                Assert.AreEqual(Enumerable.Range(1, Seeds), r.Rows.Select(x => x.Seed), "seeds 1..20, in order");
                Assert.IsTrue(r.Rows.All(x => x.Turns > 0), "every fight played turns");
            }
        }

        [Test]
        public void TheSameRunTwice_GivesTheSameReport()
        {
            Assert.AreEqual(FightReport.Markdown(RunAll()), FightReport.Markdown(RunAll()), "same seeds, same data: the very same text");
        }

        [Test]
        public void TheReport_NamesEveryTeam_EveryHero_AndTheThreeEndings()
        {
            var text = FightReport.Markdown(RunAll());
            foreach (var team in TargetTeams) StringAssert.Contains(team, text);
            foreach (var hero in data.Heroes.Values) StringAssert.Contains(hero.Name, text, "every hero has a row");
            StringAssert.Contains("Wins", text);
            StringAssert.Contains("Losses", text);
            StringAssert.Contains("No winner", text);
            StringAssert.Contains("Ultimates", text);
            StringAssert.Contains("Double turns", text);
        }

        [Test]
        public void TheReport_IsWrittenToTestResults()
        {
            var text = FightReport.Markdown(RunAll());
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FightReport.Path));
            File.WriteAllText(FightReport.Path, text);
            Assert.IsTrue(File.Exists(FightReport.Path));
            Assert.AreEqual(text, File.ReadAllText(FightReport.Path));
            TestContext.Out.WriteLine("Fight report written to " + FightReport.Path);
        }
    }
}
