using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroHQ.Rules;
using HeroHQ.Rules.Data;
using HeroHQ.Screens;
using NUnit.Framework;
using UnityEngine;

namespace HeroHQ.Tests
{
    // P2a-05 (03 Architecture 7.2, 7.7; QA finding 3): Playback keeps the Health and charge the screen SHOWS,
    // counted from the fight's events, and says which events get a beat. A FightEvent's Who is the live fighter,
    // already at its end-of-action Health, so the screen must never read it mid-playback.
    public class PlaybackTests
    {
        static readonly string[] TargetTeams = { "greek_strike", "healing_wall", "outlaw_burn" };
        const int Seeds = 20;

        static Fight Little() => TestFights.NewFight(
            new[] { ("p0", 100), ("p1", 100), ("p2", 100), ("p3", 100), ("p4", 100) },
            new[] { ("e0", 100), ("e1", 100), ("e2", 100), ("e3", 100), ("e4", 100) });

        // FightEvent's detail fields have internal setters (only the rules write them), so a hand-made event sets them by reflection.
        static FightEvent Event(FightEventKind kind, Fighter who, int amount = 0, int soaked = 0, double charge = 0)
        {
            var e = new FightEvent(kind, who);
            void Set(string name, object value) => typeof(FightEvent).GetProperty(name).SetValue(e, value);
            Set("Amount", amount);
            Set("Soaked", soaked);
            Set("Charge", charge);
            return e;
        }

        static FightEvent Damaged(Fighter who, int amount, int soaked = 0) => Event(FightEventKind.Damaged, who, amount, soaked);
        static FightEvent Healed(Fighter who, int amount) => Event(FightEventKind.Healed, who, amount);
        static FightEvent Charged(Fighter who, double charge) => Event(FightEventKind.ChargeChanged, who, charge: charge);

        // ---- which events get a beat ------------------------------------------------------------------------------

        [Test]
        public void EveryEventKind_IsSortedIntoBeatOrNoBeat()
        {
            // GetsBeat throws for a kind nobody sorted, so a new FightEventKind fails here until it is decided.
            foreach (FightEventKind kind in Enum.GetValues(typeof(FightEventKind)))
                Assert.DoesNotThrow(() => Playback.GetsBeat(kind), $"{kind} is not sorted into beat or no beat in Playback.GetsBeat");
        }

        [Test]
        public void TheKindsThatShowSomethingOverAHero_GetABeat_TheBookkeepingOnesDoNot()
        {
            // 7.2: damage, miss, crit (a Damaged), heal, Barrier soak (a Damaged), effect put on, warded, counter,
            // knockout, skipped turn. Everything else changes the bars at once or is shown another way.
            var beat = new[]
            {
                FightEventKind.Missed, FightEventKind.Damaged, FightEventKind.KnockedOut, FightEventKind.Healed,
                FightEventKind.EffectApplied, FightEventKind.EffectBlocked, FightEventKind.CounterAttack, FightEventKind.TurnSkipped,
            };
            var actual = Enum.GetValues(typeof(FightEventKind)).Cast<FightEventKind>().Where(Playback.GetsBeat).ToList();
            CollectionAssert.AreEquivalent(beat, actual);
        }

        [Test]
        public void SkillUsed_HasNoBeat_ItsBannerAndOffLookAreTimedOnTheirOwn()
        {
            Assert.IsFalse(Playback.GetsBeat(FightEventKind.SkillUsed));
        }

        // ---- counting from the events -----------------------------------------------------------------------------

        [Test]
        public void ItStartsAtFullHealthAndNoCharge()
        {
            var fight = Little();
            var shown = new Playback(fight);
            foreach (var f in fight.Players.Concat(fight.Enemies))
            {
                Assert.AreEqual(f.MaxHealth, shown.Health(f));
                Assert.AreEqual(0, shown.Charge(f));
            }
        }

        [Test]
        public void ADamageHit_TakesOffTheAmountMinusWhatTheBarrierSoaked()
        {
            var fight = Little();
            var hero = fight.Enemies[0];
            var shown = new Playback(fight);
            shown.Apply(Damaged(hero, amount: 300, soaked: 100));
            Assert.AreEqual(hero.MaxHealth - 200, shown.Health(hero));
        }

        [Test]
        public void ADamageHit_FullySoakedByABarrier_LeavesHealthAlone()
        {
            var fight = Little();
            var hero = fight.Enemies[0];
            var shown = new Playback(fight);
            shown.Apply(Damaged(hero, amount: 300, soaked: 300));
            Assert.AreEqual(hero.MaxHealth, shown.Health(hero));
        }

        [Test]
        public void AHitThatKnocksOut_ShowsZero_NotANegativeNumber()
        {
            var fight = Little();
            var hero = fight.Enemies[0];
            var shown = new Playback(fight);
            shown.Apply(Damaged(hero, amount: hero.MaxHealth + 5000));
            shown.Apply(new FightEvent(FightEventKind.KnockedOut, hero));
            Assert.AreEqual(0, shown.Health(hero));
        }

        [Test]
        public void AHeal_AddsTheHealthGivenBack()
        {
            var fight = Little();
            var hero = fight.Players[1];
            var shown = new Playback(fight);
            shown.Apply(Damaged(hero, amount: 900));
            shown.Apply(Healed(hero, 400));
            Assert.AreEqual(hero.MaxHealth - 500, shown.Health(hero));
        }

        [Test]
        public void AChargeChange_SetsTheChargeToTheNewValue_FractionsKept()
        {
            var fight = Little();
            var hero = fight.Players[2];
            var shown = new Playback(fight);
            shown.Apply(Charged(hero, 37.25));
            Assert.AreEqual(37.25, shown.Charge(hero));
            shown.Apply(Charged(hero, 0));
            Assert.AreEqual(0, shown.Charge(hero), "an ultimate's use sets the charge back to 0");
        }

        [Test]
        public void TheOtherKinds_ChangeNeitherHealthNorCharge()
        {
            var fight = Little();
            var hero = fight.Players[0];
            var shown = new Playback(fight);
            foreach (FightEventKind kind in Enum.GetValues(typeof(FightEventKind)))
            {
                if (kind == FightEventKind.Damaged || kind == FightEventKind.Healed || kind == FightEventKind.ChargeChanged) continue;
                shown.Apply(Event(kind, hero, amount: 999, soaked: 999, charge: 55));
            }
            Assert.AreEqual(hero.MaxHealth, shown.Health(hero));
            Assert.AreEqual(0, shown.Charge(hero));
        }

        [Test]
        public void ItNeverReadsTheFightersHealthOrChargeAfterTheStart()
        {
            // The fighter jumps ahead (as the live one does while a long action is played back); the shown values must not follow.
            var fight = Little();
            var hero = fight.Enemies[3];
            var shown = new Playback(fight);
            var full = hero.MaxHealth;
            hero.Health = 1;
            hero.Charge = 80;
            Assert.AreEqual(full, shown.Health(hero));
            Assert.AreEqual(0, shown.Charge(hero));
            shown.Apply(Damaged(hero, amount: 10));
            Assert.AreEqual(full - 10, shown.Health(hero));
        }

        [Test]
        public void ABatch_IsAppliedInOrder()
        {
            var fight = Little();
            var hero = fight.Players[4];
            var shown = new Playback(fight);
            shown.Apply(new[]
            {
                Damaged(hero, amount: 600),
                Healed(hero, 100),
                Damaged(hero, amount: 50),
            });
            Assert.AreEqual(hero.MaxHealth - 550, shown.Health(hero));
        }

        // ---- QA finding 3, on the real data -------------------------------------------------------------------------

        static GameData LoadReal()
        {
            string Read(string file) => File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "Data", file));
            return GameData.Load(Read("heroes.json"), Read("classes.json"), Read("traits.json"), Read("tuning.json"), Read("teams.json"), Read("formations.json"));
        }

        [Test]
        public void OverTheSixtyReportFights_TheShownHealthAndChargeEqualTheFightersOwn_AfterEveryBatch()
        {
            var data = LoadReal();
            var fights = 0;
            var batches = 0;
            var knockouts = 0;
            foreach (var team in TargetTeams)
                for (var seed = 1; seed <= Seeds; seed++)
                {
                    Playback shown = null;
                    var label = $"{team} seed {seed}";
                    var played = TestFights.Play(data, data.Teams[team], data.Teams["enemy_crew"], seed, after: (fight, batch) =>
                    {
                        // Built once, right after Create, before the start events are played (the first call).
                        if (shown == null) shown = new Playback(fight);
                        shown.Apply(batch);
                        batches++;
                        foreach (var f in fight.Players.Concat(fight.Enemies))
                        {
                            Assert.AreEqual(f.Health, shown.Health(f), $"{label}: Health of {f} after a batch ending {batch.LastOrDefault()}");
                            Assert.AreEqual(f.Charge, shown.Charge(f), $"{label}: charge of {f} after a batch ending {batch.LastOrDefault()}");
                        }
                    });
                    knockouts += played.Events.Count(e => e.Kind == FightEventKind.KnockedOut);
                    fights++;
                }
            Assert.AreEqual(60, fights);
            Assert.Greater(batches, 1000, "the callback really ran through the fights");
            Assert.Greater(knockouts, 100, "the knockout case was really met");
        }

        [Test]
        public void InTheRealFights_AKnockedOutHero_ShowsZeroOnceTheKnockoutIsApplied()
        {
            var data = LoadReal();
            var seen = 0;
            Playback shown = null;
            TestFights.Play(data, data.Teams["greek_strike"], data.Teams["enemy_crew"], 1, after: (fight, batch) =>
            {
                if (shown == null) shown = new Playback(fight);
                foreach (var e in batch)
                {
                    shown.Apply(e);
                    if (e.Kind != FightEventKind.KnockedOut) continue;
                    seen++;
                    Assert.AreEqual(0, shown.Health(e.Who), $"{e.Who} shows 0 at their own KnockedOut event");
                }
            });
            Assert.Greater(seen, 0);
        }
    }
}
